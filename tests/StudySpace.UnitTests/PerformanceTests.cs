using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.App.Features.Performance;
using StudySpace.Application.Performance;
using StudySpace.Application.Projects;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
using StudySpace.Domain.Projects;
using StudySpace.Domain.Quizzes;
namespace StudySpace.UnitTests;

public sealed class PerformanceTests
{
    private readonly Guid project=Guid.NewGuid(),source=Guid.NewGuid(),chunk=Guid.NewGuid(),tag=Guid.NewGuid();
    private static readonly DateTimeOffset Now=new(2026,10,26,12,0,0,TimeSpan.Zero);
    private QuizSession Session(params double?[] scores)
    {
        var questions=scores.Select((_,i)=>new QuizQuestionSnapshot(Guid.NewGuid(),i,QuestionKind.Open,"Domanda "+i,"Spiegazione salvata","Risposta ideale",Difficulty.Medium,[],
            [new("criterion","Autenticazione a più fattori",1,[chunk])],
            [new(source,Guid.NewGuid(),chunk,"Usare fattori indipendenti.","Sicurezza","Autenticazione")],Topics:[new("Autenticazione",[chunk])],Tags:[new(tag,"Sicurezza",[chunk])])).ToArray();
        var answers=questions.Select((q,i)=>new QuizAnswerState(q.Id,scores[i].HasValue?"La mia risposta":"",null,2,
            scores[i].HasValue?EvaluationStatus.Evaluated:EvaluationStatus.NotRequested,
            scores[i].HasValue?new(scores[i]>=0.8?AnswerOutcome.Correct:scores[i]>0?AnswerOutcome.Partial:AnswerOutcome.Incorrect,scores[i]!.Value,
                "Feedback salvato",q.IdealAnswer,[],["Fattori indipendenti"],[],"Rivedi i fattori indipendenti.",[new("criterion",scores[i]!.Value)],"fixture",false):null,null)).ToArray();
        return new(new QuizAttempt{ProjectId=project,Mode=QuizMode.Open,Difficulty=Difficulty.Medium,Status=AttemptStatus.Completed,StartedAtUtc=Now,CompletedAtUtc=Now,
            MaximumScore=scores.Length,Score=scores.Sum(s=>s??0),ActiveDurationMs=125000},"Quiz",false,"fixture",false,questions,answers);
    }
    [Fact] public void Counts_are_disjoint_sum_to_question_count_and_preserve_partial_credit()
    {
        var result=StudyPerformanceService.BuildResult(Session(1,0.5,0,null),[]);
        Assert.Equal(37.5,result.Percentage);Assert.Equal(1,result.Correct);Assert.Equal(1,result.Partial);Assert.Equal(1,result.Incorrect);Assert.Equal(1,result.Unanswered);
        Assert.Equal(4,result.Correct+result.Partial+result.Incorrect+result.Unanswered);
    }
    [Fact] public void Unanswered_questions_never_create_a_conceptual_weakness()
    {
        var accumulator=new PerformanceAccumulator();accumulator.Add(Session(null,null));var stats=accumulator.Build();
        Assert.Empty(stats.Difficulties);Assert.Empty(stats.Improving);Assert.Equal(0,stats.AveragePercentage);Assert.Equal(1,stats.CompletedCount);
    }
    [Fact] public void All_dimensions_use_weighted_evidence_and_deduplicate_question_counts()
    {
        var s=Session(0,0.5,1,null);var accumulator=new PerformanceAccumulator();accumulator.Add(s);var result=accumulator.Build();
        Assert.Equal(4,result.Difficulties.Count);
        foreach(var group in result.Difficulties)
        {
            Assert.Equal(3,group.EvidenceCount);Assert.Equal(1,group.Unanswered);Assert.Equal(0.5,group.DifficultyIndex,8);
            Assert.Equal(1,group.RecentErrors);Assert.Equal(1,group.RecentPartial);Assert.NotEmpty(group.References);
            Assert.Contains("Sicurezza",group.Suggestions[0]);
        }
    }
    [Fact] public void Separate_criteria_do_not_assign_one_concept_error_to_every_topic_or_source()
    {
        var s=Session(0.5);var q=s.Questions[0];var second=Guid.NewGuid();var otherSource=Guid.NewGuid();
        q=q with{Rubric=[new("a","Fattori indipendenti",0.5,[chunk]),new("b","Cifratura",0.5,[second])],
            Citations=[q.Citations[0],new(otherSource,Guid.NewGuid(),second,"Cifrare i dati.","Crittografia",null)],
            Topics=[new("Autenticazione",[chunk]),new("Cifratura",[second])]};
        s=s with{Questions=[q],Answers=[s.Answers[0] with{Feedback=s.Answers[0].Feedback! with{Criteria=[new("a",0),new("b",1)]}}]};
        var accumulator=new PerformanceAccumulator();accumulator.Add(s);var groups=accumulator.Build().Difficulties;
        Assert.Contains(groups,g=>g.Kind==PerformanceGroupKind.Topic&&g.Name=="Autenticazione");
        Assert.DoesNotContain(groups,g=>g.Kind==PerformanceGroupKind.Topic&&g.Name=="Cifratura");
        Assert.DoesNotContain(groups,g=>g.Kind==PerformanceGroupKind.Source&&g.Name=="Crittografia");
        Assert.Equal(1,groups.Single(g=>g.Kind==PerformanceGroupKind.Topic).EvidenceCount);
    }
    [Fact] public void Improvement_requires_sufficient_previous_evidence_and_uses_more_than_last_quiz()
    {
        var accumulator=new PerformanceAccumulator();accumulator.Add(Session(1,1,1));accumulator.Add(Session(1,1));accumulator.Add(Session(0,0,0,0,0));
        var stats=accumulator.Build();Assert.Equal(3,stats.CompletedCount);Assert.InRange(stats.AveragePercentage!.Value,66.66,66.67);
        Assert.Equal(4,stats.Improving.Count);Assert.All(stats.Improving,g=>Assert.Equal(0.6,g.Improvement!.Value,8));
        Assert.All(stats.Improving,g=>Assert.Equal(10,g.EvidenceCount));
        var sparse=new PerformanceAccumulator();sparse.Add(Session(1,1));sparse.Add(Session(0,0));Assert.Empty(sparse.Build().Improving);
        var singleQuiz=new PerformanceAccumulator();singleQuiz.Add(Session(0,0,0,0,0,1,1,1,1,1));Assert.Empty(singleQuiz.Build().Improving);
    }
    [Fact] public void Pending_abandoned_and_in_progress_are_excluded_from_statistics()
    {
        var accumulator=new PerformanceAccumulator();foreach(var status in new[]{AttemptStatus.InProgress,AttemptStatus.Abandoned,AttemptStatus.SubmittedPendingEvaluation})
        {var s=Session(0);accumulator.Add(s with{Attempt=s.Attempt with{Status=status}});}
        Assert.Equal(0,accumulator.Build().CompletedCount);Assert.Null(accumulator.Build().AveragePercentage);
    }
    [Fact] public void Exactly_ten_percentage_points_of_improvement_is_not_lost_to_floating_point_precision()
    {
        var accumulator=new PerformanceAccumulator();accumulator.Add(Session(0.6,0.6,0.6));accumulator.Add(Session(0.5,0.5,0.5));
        Assert.Equal(4,accumulator.Build().Improving.Count);
        Assert.All(accumulator.Build().Improving,g=>Assert.Equal(0.1,g.Improvement!.Value,8));
    }
    [Fact] public async Task Result_details_show_saved_choices_feedback_and_clickable_historical_reference()
    {
        var s=Session(0);var q=s.Questions[0] with{Kind=QuestionKind.Closed,Options=[new(Guid.NewGuid(),"Scelta sbagliata",false),new(Guid.NewGuid(),"Scelta corretta",true)]};
        s=s with{Questions=[q],Answers=[s.Answers[0] with{Text="",SelectedOptionId=q.Options[0].Id,Feedback=s.Answers[0].Feedback! with{IdealAnswer="Scelta corretta"}}]};
        PerformanceReference? opened=null;var vm=new ResultQuestionViewModel(s,q,r=>{opened=r;return Task.CompletedTask;});
        Assert.Equal("Scelta sbagliata",vm.MyAnswer);Assert.Equal("Scelta corretta",vm.IdealAnswer);Assert.Contains("Feedback salvato",vm.Evaluation);
        await Assert.Single(vm.References).OpenCommand.ExecuteAsync(null);Assert.Equal(s.Attempt.Id,opened!.AttemptId);Assert.Equal(chunk,opened.Citation.ChunkId);
    }
    [Theory][InlineData(0,"0 min 00 s")][InlineData(125000,"2 min 05 s")][InlineData(3661000,"1 h 01 min 01 s")]
    public void Active_duration_is_readable(long milliseconds,string expected)=>Assert.Equal(expected,PerformanceText.Duration(milliseconds));
    [Fact] public void History_presents_configuration_and_never_displays_pending_score_as_final()
    {
        var s=Session(1);var summary=StudyPerformanceService.Summarize(s);var vm=new HistoryItemViewModel(summary,()=>Task.CompletedTask);
        Assert.Contains("Risposta aperta",vm.Details);Assert.Contains("Media",vm.Details);Assert.Contains("2 min 05 s",vm.Details);Assert.Contains("100%",vm.Score);
        var pending=new HistoryItemViewModel(summary with{Status=AttemptStatus.SubmittedPendingEvaluation},()=>Task.CompletedTask);Assert.False(pending.HasScore);Assert.Contains("non disponibile",pending.Score);
    }
}
