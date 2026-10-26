using StudySpace.Application.Performance;
using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Application.Quizzes;
using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
using StudySpace.Infrastructure.Ai;
using StudySpace.Infrastructure.Files;
using StudySpace.Infrastructure.Import;
using StudySpace.Infrastructure.Persistence;
namespace StudySpace.IntegrationTests;

public sealed class QuizDatabaseTests:IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"StudySpace.QuizTests",Guid.NewGuid().ToString("N"));
    private readonly QuizClock clock=new();
    private readonly QuizAi ai=new();
    private DatabaseOptions Options=>new(Path.Combine(directory,"quiz.db3"));
    private StudyDatabase Db()=>new(Options,NullLogger<StudyDatabase>.Instance);
    private QuizRepository Repo()=>new(Db());
    private ProjectService Projects()=>new(new ProjectRepository(Db()),clock);
    private QuizService Quizzes()=>new(ai,new StudyContextRepository(Db()),Repo(),Projects(),clock);
    private QuizAttemptService Attempts()=>new(Repo(),ai,clock);
    private StudyPerformanceService Performance()=>new(new StudyPerformanceRepository(Db()),Repo(),Projects(),clock);
    private const string Text="TCP garantisce un trasporto affidabile fra due applicazioni. UDP trasmette datagrammi senza garantire la consegna finale. IP instrada pacchetti attraverso reti differenti e interconnesse. DNS traduce nomi simbolici negli indirizzi dei server richiesti. HTTP consente lo scambio di rappresentazioni fra client e server. TLS protegge la comunicazione attraverso cifratura e autenticazione.";
    private async Task<(Guid Project,Guid Source,Guid[] Chunks)> MaterialAsync()
    {
        var p=await Projects().CreateAsync(new("Reti di Calcolatori"));
        var options=new SourceImportOptions();
        var importer=new SourceImportService(new SourceRepository(Db()),new SourceFileStore(Options,options,NullLogger<SourceFileStore>.Instance),
            [],new SourceChunkingService(options),Projects(),clock,options);
        var source=await importer.ImportTextAsync(p.Id,"Protocolli",Text);
        var chunks=await new StudyContextRepository(Db()).ListChunksAsync(p.Id,source.Summary.Source.Id,0,20);
        return(p.Id,source.Summary.Source.Id,chunks.Select(c=>c.Chunk.Id).ToArray());
    }
    private async Task<(Guid Project,Guid Attempt)> StartAsync(QuizMode mode=QuizMode.Mixed,int count=5)
    {
        var m=await MaterialAsync();var a=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(mode,count,null),m.Chunks);return(m.Project,a);
    }
    [Theory]
    [InlineData(QuizMode.Closed,5,5)]
    [InlineData(QuizMode.Open,5,0)]
    [InlineData(QuizMode.Mixed,5,3)]
    [InlineData(QuizMode.Closed,1,1)]
    public async Task Generation_saves_configuration_options_snapshots_and_project_citations(QuizMode mode,int count,int closed)
    {
        var m=await MaterialAsync();var id=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(mode,count,null),m.Chunks);
        var s=await Attempts().GetAsync(m.Project,id);
        Assert.Equal(count,s.Questions.Count);Assert.Equal(closed,s.Questions.Count(q=>q.Kind==QuestionKind.Closed));
        Assert.Equal(AttemptStatus.InProgress,s.Attempt.Status);Assert.True(s.IsMock);Assert.True(s.MixedDifficulty);
        Assert.Equal(count,s.Answers.Count);Assert.All(s.Answers,a=>Assert.False(a.IsAnswered));
        Assert.All(s.Questions,q=>{Assert.NotEmpty(q.Citations);Assert.All(q.Citations,c=>Assert.Contains(c.ChunkId,m.Chunks));});
        Assert.All(s.Questions.Where(q=>q.Kind==QuestionKind.Closed),q=>Assert.Single(q.Options,o=>o.IsCorrect));
        if(count>1)Assert.Equal(3,s.Questions.Select(q=>q.Difficulty).Distinct().Count());
        Assert.Equal(count,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM AttemptCitation")));
    }
    [Fact] public async Task Closed_answer_is_atomic_immediate_locked_idempotent_and_reopened_with_feedback()
    {
        var (p,a)=await StartAsync(QuizMode.Closed);var s=await Attempts().GetAsync(p,a);var q=s.Questions[0];
        var right=q.Options.Single(o=>o.IsCorrect);
        var answer=await Attempts().AnswerClosedAsync(p,a,q.Id,right.Id,1);
        Assert.Equal(AnswerOutcome.Correct,answer.Feedback!.Outcome);Assert.Equal(1,answer.Feedback.Score);
        Assert.Equal(right.Text,answer.Feedback.IdealAnswer);Assert.Equal(EvaluationStatus.Evaluated,answer.Status);
        await Attempts().AnswerClosedAsync(p,a,q.Id,right.Id,1);
        await Assert.ThrowsAsync<QuizConflictException>(()=>Attempts().AnswerClosedAsync(p,a,q.Id,q.Options.First(o=>!o.IsCorrect).Id,answer.Revision));
        var reopened=await Attempts().ResumeAsync(p,a);
        Assert.Equal(answer.SelectedOptionId,reopened.Answers[0].SelectedOptionId);Assert.Equal(answer.Revision,reopened.Answers[0].Revision);
        Assert.Equal(answer.Feedback.Score,reopened.Answers[0].Feedback!.Score);Assert.Equal(answer.Feedback.Explanation,reopened.Answers[0].Feedback!.Explanation);
        Assert.Equal(1,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerEvaluation")));
        Assert.Equal(0,ai.Evaluations);
    }
    [Fact] public async Task Wrong_closed_choice_preserves_original_choice_and_correct_solution()
    {
        var (p,a)=await StartAsync(QuizMode.Closed);var q=(await Attempts().GetAsync(p,a)).Questions[0];var wrong=q.Options.First(o=>!o.IsCorrect);
        var answer=await Attempts().AnswerClosedAsync(p,a,q.Id,wrong.Id,1);
        Assert.Equal(wrong.Id,answer.SelectedOptionId);Assert.Equal(AnswerOutcome.Incorrect,answer.Feedback!.Outcome);
        Assert.Equal(q.Options.Single(o=>o.IsCorrect).Text,answer.Feedback.IdealAnswer);
    }
    [Fact] public async Task Open_text_is_persisted_without_evaluation_then_explicitly_verified_and_locked()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        var saved=await Attempts().SaveOpenAsync(p,a,q.Id,q.IdealAnswer,1);
        Assert.Equal(0,ai.Evaluations);Assert.Equal(2,saved.Revision);Assert.Equal(EvaluationStatus.NotRequested,saved.Status);
        await Repo().CheckpointAsync(p,a,3,12345,clock.UtcNow);
        var reopened=await Attempts().GetAsync(p,a);Assert.Equal(q.IdealAnswer,reopened.Answers[0].Text);
        Assert.Equal(3,await Attempts().PositionAsync(p,a));Assert.Equal(12345,reopened.Attempt.ActiveDurationMs);
        await Attempts().EvaluateAsync(p,a,q.Id);var evaluated=(await Attempts().GetAsync(p,a)).Answers[0];
        Assert.Equal(AnswerOutcome.Correct,evaluated.Feedback!.Outcome);Assert.True(evaluated.Feedback.IsMock);
        Assert.NotEmpty(evaluated.Feedback.CorrectConcepts);
        await Assert.ThrowsAsync<QuizConflictException>(()=>Attempts().SaveOpenAsync(p,a,q.Id,"Nuova risposta",evaluated.Revision));
        await Attempts().EvaluateAsync(p,a,q.Id);Assert.Equal(1,ai.Evaluations);
    }
    [Fact] public async Task Partial_and_incorrect_results_follow_weighted_rubric()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var s=await Attempts().GetAsync(p,a);
        ai.Grade=0.5;
        await Attempts().SaveOpenAsync(p,a,s.Questions[0].Id,s.Questions[0].Rubric[0].Concept,1);
        await Attempts().EvaluateAsync(p,a,s.Questions[0].Id);
        ai.Grade=0;
        await Attempts().SaveOpenAsync(p,a,s.Questions[1].Id,"Contenuto estraneo",1);
        await Attempts().EvaluateAsync(p,a,s.Questions[1].Id);
        var read=await Attempts().GetAsync(p,a);
        Assert.Equal(AnswerOutcome.Partial,read.Answers[0].Feedback!.Outcome);Assert.InRange(read.Answers[0].Feedback!.Score,0.01,0.79);
        Assert.Equal(AnswerOutcome.Incorrect,read.Answers[1].Feedback!.Outcome);Assert.Equal(0,read.Answers[1].Feedback!.Score);
    }
    [Fact] public async Task Empty_answers_never_call_provider_and_stale_writes_are_rejected()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        await Attempts().SaveOpenAsync(p,a,q.Id,"   ",1);
        await Assert.ThrowsAsync<StudyValidationException>(()=>Attempts().EvaluateAsync(p,a,q.Id));
        await Assert.ThrowsAsync<QuizConflictException>(()=>Attempts().SaveOpenAsync(p,a,q.Id,"Testo",1));
        Assert.Equal(0,ai.Evaluations);
    }
    [Fact] public async Task Provider_failure_retains_answer_without_zero_and_finish_is_retryable()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        await Attempts().SaveOpenAsync(p,a,q.Id,q.IdealAnswer,1);ai.Fail=true;
        var pending=await Attempts().FinishAsync(p,a);
        Assert.Equal(AttemptStatus.SubmittedPendingEvaluation,pending.Attempt.Status);Assert.Null(pending.Attempt.Score);
        Assert.Equal(EvaluationStatus.Failed,pending.Answers[0].Status);Assert.Null(pending.Answers[0].Feedback);
        Assert.Equal(q.IdealAnswer,pending.Answers[0].Text);
        ai.Fail=false;var completed=await Attempts().FinishAsync(p,a);
        Assert.Equal(AttemptStatus.Completed,completed.Attempt.Status);Assert.Equal(1,completed.Attempt.Score);
        Assert.Equal(5,completed.Attempt.MaximumScore);
        var calls=ai.Evaluations;await Attempts().FinishAsync(p,a);Assert.Equal(calls,ai.Evaluations);
    }
    [Fact] public async Task Finish_skips_already_verified_and_unanswered_questions_and_does_not_duplicate_evaluations()
    {
        var (p,a)=await StartAsync();var s=await Attempts().GetAsync(p,a);
        var closed=s.Questions.First(q=>q.Kind==QuestionKind.Closed);var open=s.Questions.First(q=>q.Kind==QuestionKind.Open);
        await Attempts().AnswerClosedAsync(p,a,closed.Id,closed.Options.Single(o=>o.IsCorrect).Id,1);
        await Attempts().SaveOpenAsync(p,a,open.Id,open.IdealAnswer,1);await Attempts().EvaluateAsync(p,a,open.Id);
        var done=await Attempts().FinishAsync(p,a);
        Assert.Equal(AttemptStatus.Completed,done.Attempt.Status);Assert.Equal(2,done.Attempt.Score);
        Assert.Equal(3,done.Answers.Count(b=>!b.IsAnswered));Assert.Equal(1,ai.Evaluations);
        await Attempts().FinishAsync(p,a);Assert.Equal(1,ai.Evaluations);
        Assert.Equal(2,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerEvaluation")));
        Assert.Equal(1,(await new ProjectRepository(Db()).GetSummaryAsync(p))!.CompletedQuizCount);
    }
    [Fact] public async Task Recovery_invalidates_old_evaluation_token_even_when_answer_revision_is_unchanged()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        await Attempts().SaveOpenAsync(p,a,q.Id,q.IdealAnswer,1);
        var old=(await Repo().ClaimAsync(p,a,q.Id,clock.UtcNow))!;
        await Attempts().ResumeAsync(p,a);
        var fresh=(await Repo().ClaimAsync(p,a,q.Id,clock.UtcNow))!;
        var assessment=await ai.EvaluateOpenAnswerAsync(new(p,a,q.Id,old.Answer.Revision,q,q.IdealAnswer));
        var feedback=QuizAttemptService.ValidateAssessment(q,assessment,ai.Provider);
        Assert.False(await Repo().AcceptAsync(p,a,old,feedback,clock.UtcNow));
        Assert.True(await Repo().AcceptAsync(p,a,fresh,feedback,clock.UtcNow));
        Assert.Equal(EvaluationStatus.Evaluated,(await Attempts().GetAsync(p,a)).Answers[0].Status);
    }
    [Fact] public async Task Abandon_and_completed_attempts_are_read_only_and_foreign_ids_never_load()
    {
        var (p,a)=await StartAsync();var other=await MaterialAsync();var s=await Attempts().GetAsync(p,a);
        Assert.Null(await Repo().GetAsync(other.Project,a));
        await Assert.ThrowsAsync<QuizNotFoundException>(()=>Attempts().AnswerClosedAsync(other.Project,a,s.Questions[0].Id,s.Questions[0].Options[0].Id,1));
        await Attempts().AbandonAsync(p,a);await Attempts().AbandonAsync(p,a);
        await Assert.ThrowsAsync<QuizConflictException>(()=>Attempts().AnswerClosedAsync(p,a,s.Questions[0].Id,s.Questions[0].Options[0].Id,1));
        Assert.Equal(AttemptStatus.Abandoned,(await Attempts().FinishAsync(p,a)).Attempt.Status);
    }
    [Fact] public async Task Snapshot_and_citations_survive_archived_sources_and_changes_to_live_quiz()
    {
        var m=await MaterialAsync();var a=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Closed,5,Difficulty.Hard),m.Chunks);
        var before=await Attempts().GetAsync(m.Project,a);
        await Db().ExecuteAsync(db=>
        {
            db.Execute("UPDATE QuizQuestion SET Prompt='Domanda modificata' WHERE ProjectId=?",m.Project.ToString());
            db.Execute("UPDATE QuestionOption SET Text='Opzione modificata',IsCorrect=0 WHERE ProjectId=?",m.Project.ToString());
            db.Execute("UPDATE StudySource SET Title='Titolo modificato',IsArchived=1 WHERE ProjectId=?",m.Project.ToString());return 0;
        });
        var after=await Attempts().ResumeAsync(m.Project,a);
        Assert.Equal(before.Questions[0].Prompt,after.Questions[0].Prompt);
        Assert.Equal(before.Questions[0].Options[0],after.Questions[0].Options[0]);
        Assert.Equal("Protocolli",after.Questions[0].Citations[0].SourceTitle);
        var q=after.Questions[0];var answer=await Attempts().AnswerClosedAsync(m.Project,a,q.Id,q.Options.Single(o=>o.IsCorrect).Id,1);
        Assert.Equal(AnswerOutcome.Correct,answer.Feedback!.Outcome);
    }
    [Fact] public async Task Generation_is_idempotent_and_delete_project_cascades_only_its_quiz_graph()
    {
        var m=await MaterialAsync();var op=Guid.NewGuid();var config=new QuizConfiguration(QuizMode.Mixed,5,null);
        var a=await Quizzes().GenerateAsync(m.Project,op,config,m.Chunks);
        Assert.Equal(a,await Quizzes().GenerateAsync(m.Project,op,config,m.Chunks));
        Assert.Equal(1,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM Quiz")));
        var other=await StartAsync();
        var project=(await Projects().GetAsync(m.Project));await Projects().DeleteAsync(project);
        Assert.Null(await Repo().GetAsync(m.Project,a));Assert.NotNull(await Repo().GetAsync(other.Project,other.Attempt));
        foreach(var table in new[]{"Quiz","QuizDetails","QuizAttempt","QuizAttemptDetails","QuizQuestion","QuestionOption","AttemptQuestion","QuizAnswer","AttemptCitation","AnswerEvaluation"})
            Assert.Equal(0,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE ProjectId=?",m.Project.ToString())));
    }
    [Theory]
    [InlineData("count")][InlineData("options")][InlineData("citation")][InlineData("rubric")][InlineData("duplicate")]
    public async Task Invalid_provider_output_is_rejected_without_partial_quiz(string defect)
    {
        var m=await MaterialAsync();
        ai.Transform=d=>
        {
            var list=d.Questions.ToArray();
            list[0]=defect switch
            {
                "options"=>list[0] with{Options=[new("Uguale",true),new("Uguale",true)]},
                "citation"=>list[0] with{References=[new(Guid.NewGuid(),"Testo inventato")]},
                "rubric"=>list[0],
                "duplicate"=>list[0] with{Prompt=list[1].Prompt},
                _=>list[0]
            };
            if(defect=="rubric")list[1]=list[1] with{Rubric=[new("bad","concetto",0.7,[m.Chunks[0]])]};
            return new(defect=="count"?list.Take(2).ToArray():list);
        };
        await Assert.ThrowsAsync<AiStudyException>(()=>Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Mixed,5,null),m.Chunks));
        Assert.Empty(await Quizzes().ListAsync(m.Project));
    }
    [Fact] public async Task Invalid_evaluation_is_failed_without_score_and_can_be_retried()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        await Attempts().SaveOpenAsync(p,a,q.Id,q.IdealAnswer,1);
        ai.BadScore=true;await Assert.ThrowsAsync<AiStudyException>(()=>Attempts().EvaluateAsync(p,a,q.Id));
        var failed=(await Attempts().GetAsync(p,a)).Answers[0];Assert.Null(failed.Feedback);Assert.Equal(EvaluationStatus.Failed,failed.Status);
        ai.BadScore=false;await Attempts().EvaluateAsync(p,a,q.Id);Assert.NotNull((await Attempts().GetAsync(p,a)).Answers[0].Feedback);
    }
    [Fact] public async Task Foreign_or_archived_material_and_unsupported_filters_never_reach_provider()
    {
        var m=await MaterialAsync();var other=await MaterialAsync();
        await Assert.ThrowsAsync<StudyValidationException>(()=>Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Mixed,5,null),other.Chunks));
        await Assert.ThrowsAsync<StudyValidationException>(()=>Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Mixed,5,null,new(TopicIds:[Guid.NewGuid()])),m.Chunks));
        await Db().ExecuteAsync(db=>db.Execute("UPDATE StudySource SET IsArchived=1 WHERE Id=?",m.Source.ToString()));
        await Assert.ThrowsAsync<StudyValidationException>(()=>Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Mixed,5,null),m.Chunks));
        Assert.Equal(0,ai.Generations);
    }
    [Fact] public async Task Insufficient_demo_material_does_not_invent_questions()
    {
        var m=await MaterialAsync();
        await Assert.ThrowsAsync<AiStudyException>(()=>Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Mixed,20,null),m.Chunks));
        Assert.Empty(await Quizzes().ListAsync(m.Project));
    }
    [Fact] public async Task Failed_batch_rolls_back_and_foreign_attempt_citation_is_rejected_by_sqlite()
    {
        var m=await MaterialAsync();var other=await MaterialAsync();
        var context=await new StudyContextRepository(Db()).GetChunksAsync(m.Project,m.Chunks);
        var project=await Projects().GetAsync(m.Project);var config=new QuizConfiguration(QuizMode.Mixed,5,null);
        var draft=await ai.GenerateQuizAsync(new(project,config,context));
        var questions=QuizService.ValidateDraft(config,context,draft).ToArray();
        questions[1]=questions[1] with{Id=questions[0].Id};
        await Assert.ThrowsAsync<StorageUnavailableException>(()=>Repo().CreateAsync(new(Guid.NewGuid(),m.Project,config,ai.Provider,clock.UtcNow,m.Chunks,questions)));
        Assert.Empty(await Quizzes().ListAsync(m.Project));
        Assert.Equal(0,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM Quiz WHERE ProjectId=?",m.Project.ToString())));
        var id=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),config,m.Chunks);
        var q=(await Attempts().GetAsync(m.Project,id)).Questions[0];
        await Assert.ThrowsAsync<StorageUnavailableException>(()=>Db().ExecuteAsync(db=>db.Execute("INSERT INTO AttemptCitation VALUES(?,?,?,?,?,?)",
            m.Project.ToString(),q.Id.ToString(),other.Chunks[0].ToString(),"Estratto","Fonte",null)));
        Assert.Single(await Quizzes().ListAsync(m.Project));
    }
    [Fact] public async Task Unicode_whitespace_is_unanswered_in_list_and_completion()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        await Attempts().SaveOpenAsync(p,a,q.Id,"\u2003\u00A0",1);
        Assert.Equal(0,Assert.Single(await Quizzes().ListAsync(p)).AnsweredCount);
        var result=await Attempts().FinishAsync(p,a);Assert.Equal(0,result.Attempt.Score);Assert.Equal(0,ai.Evaluations);
    }
    [Fact] public async Task Semantic_metadata_persists_on_reopen_and_old_verified_answers_are_not_regraded()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        await Attempts().SaveOpenAsync(p,a,q.Id,"Una parafrasi sintetica della risposta.",1);
        await Attempts().EvaluateAsync(p,a,q.Id);
        var read=await new QuizAttemptService(new QuizRepository(Db()),ai,clock).ResumeAsync(p,a);
        var feedback=read.Answers[0].Feedback!;
        Assert.Equal(2,feedback.ScoringVersion);Assert.Equal(100,feedback.SemanticEvaluation!.Evaluation.Score);
        Assert.Equal(0.9,feedback.SemanticEvaluation.Evaluation.Confidence);
        Assert.Equal(q.Citations[0].ChunkId,feedback.SemanticEvaluation.Evaluation.SourceReferences[0].ChunkId);
        Assert.Equal("semantic-evaluation-v1",feedback.SemanticEvaluation.PromptVersion);
        await Attempts().FinishAsync(p,a);Assert.Equal(1,ai.Evaluations);
    }
    [Fact] public async Task Timeout_preserves_draft_rejects_late_result_and_allows_retry()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        await Attempts().SaveOpenAsync(p,a,q.Id,"Risposta da conservare",1);
        var late=new TaskCompletionSource<OpenAnswerEvaluation>();ai.Delayed=late.Task;
        var attempts=new QuizAttemptService(Repo(),ai,clock,new(TimeSpan.FromMilliseconds(40)));
        var ex=await Assert.ThrowsAsync<AiStudyException>(()=>attempts.EvaluateAsync(p,a,q.Id));Assert.Contains("tempo",ex.Message);
        late.SetResult(TestEvaluation.Create(q));
        var read=await Attempts().ResumeAsync(p,a);Assert.Equal("Risposta da conservare",read.Answers[0].Text);
        Assert.Equal(EvaluationStatus.Failed,read.Answers[0].Status);Assert.Null(read.Answers[0].Feedback);
        ai.Delayed=null;await Attempts().EvaluateAsync(p,a,q.Id);
        Assert.Equal(EvaluationStatus.Evaluated,(await Attempts().GetAsync(p,a)).Answers[0].Status);
        Assert.Equal(1,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM AnswerEvaluation")));
    }
    [Fact] public async Task Cancellation_retains_pending_attempt_and_resume_can_complete()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var q=(await Attempts().GetAsync(p,a)).Questions[0];
        await Attempts().SaveOpenAsync(p,a,q.Id,"Risposta breve",1);await Repo().SubmitAsync(p,a,clock.UtcNow);
        var pending=new TaskCompletionSource<OpenAnswerEvaluation>();ai.Delayed=pending.Task;
        using var cancel=new CancellationTokenSource();var evaluation=Attempts().EvaluateAsync(p,a,q.Id,cancel.Token);
        while(ai.Evaluations==0)await Task.Delay(5);
        cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>evaluation);
        var read=await Attempts().ResumeAsync(p,a);Assert.Null(read.Attempt.Score);
        Assert.Equal(AttemptStatus.SubmittedPendingEvaluation,read.Attempt.Status);Assert.Null(read.Answers[0].Feedback);
        pending.SetResult(TestEvaluation.Create(q));ai.Delayed=null;
        Assert.Equal(AttemptStatus.Completed,(await Attempts().FinishAsync(p,a)).Attempt.Status);
    }
    [Fact] public async Task Finish_pauses_on_service_failure_then_evaluates_only_remaining_answers()
    {
        var (p,a)=await StartAsync(QuizMode.Open);var questions=(await Attempts().GetAsync(p,a)).Questions;
        foreach(var q in questions.Take(3))await Attempts().SaveOpenAsync(p,a,q.Id,"Risposta",1);
        await Attempts().EvaluateAsync(p,a,questions[0].Id);ai.Fail=true;
        var pending=await Attempts().FinishAsync(p,a);Assert.Equal(2,ai.Evaluations);
        Assert.Equal(AttemptStatus.SubmittedPendingEvaluation,pending.Attempt.Status);Assert.Null(pending.Attempt.Score);
        Assert.Equal(EvaluationStatus.Evaluated,pending.Answers[0].Status);Assert.Null(pending.Answers[2].Feedback);
        ai.Fail=false;var done=await Attempts().FinishAsync(p,a);Assert.Equal(4,ai.Evaluations);
        Assert.Equal(3,done.Attempt.Score);Assert.Equal(AttemptStatus.Completed,done.Attempt.Status);
        await Attempts().FinishAsync(p,a);Assert.Equal(4,ai.Evaluations);
    }
    [Fact] public async Task Performance_reads_saved_result_history_and_duration_without_provider_calls()
    {
        var (p,a)=await StartAsync(QuizMode.Closed);var s=await Attempts().GetAsync(p,a);
        await Attempts().AnswerClosedAsync(p,a,s.Questions[0].Id,s.Questions[0].Options.Single(o=>o.IsCorrect).Id,1);
        await Attempts().AnswerClosedAsync(p,a,s.Questions[1].Id,s.Questions[1].Options.First(o=>!o.IsCorrect).Id,1);
        await Attempts().CheckpointAsync(p,a,2,125000);await Attempts().FinishAsync(p,a);ai.Fail=true;
        var result=await Performance().ResultAsync(p,a);Assert.Equal(20,result.Percentage);Assert.Equal(1,result.Correct);Assert.Equal(1,result.Incorrect);Assert.Equal(3,result.Unanswered);
        Assert.Equal(125000,result.Session.Attempt.ActiveDurationMs);
        var item=Assert.Single(await Performance().HistoryAsync(p));Assert.Equal(QuizMode.Closed,item.Mode);Assert.True(item.MixedDifficulty);Assert.Equal(125000,item.ActiveDurationMs);
        Assert.Equal(20,(await Performance().StatisticsAsync(p)).AveragePercentage);Assert.Equal(0,ai.Evaluations);
        Assert.Equal(1,ai.Generations);
    }
    [Fact] public async Task New_attempts_snapshot_topics_and_tags_while_existing_metadata_survives_changes()
    {
        var m=await MaterialAsync();var project=await Projects().GetAsync(m.Project);
        await Projects().UpdateAsync(m.Project,project.Revision,new(project.Name,Tags:["Sicurezza"]));
        await Db().ExecuteAsync(db=>db.Execute("UPDATE SourceChunk SET SectionPath='Autenticazione' WHERE ProjectId=?",m.Project.ToString()));
        var a=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Open,1,Difficulty.Medium),m.Chunks);
        var q=(await Attempts().GetAsync(m.Project,a)).Questions[0];Assert.Equal("Autenticazione",Assert.Single(q.Topics!).Name);Assert.Equal("Sicurezza",Assert.Single(q.Tags!).Name);
        await Attempts().SaveOpenAsync(m.Project,a,q.Id,"Risposta incompleta",1);ai.Grade=0;await Attempts().FinishAsync(m.Project,a);
        await Db().ExecuteAsync(db=>
        {
            db.Execute("UPDATE Tag SET Name='Tag rinominato' WHERE ProjectId=?",m.Project.ToString());
            db.Execute("UPDATE SourceChunk SET SectionPath='Capitolo cambiato' WHERE ProjectId=?",m.Project.ToString());
            db.Execute("UPDATE StudySource SET Title='Fonte rinominata',IsArchived=1 WHERE ProjectId=?",m.Project.ToString());return 0;
        });
        var result=await Performance().ResultAsync(m.Project,a);Assert.Contains(result.Difficulties,g=>g.Kind==PerformanceGroupKind.Topic&&g.Name=="Autenticazione");
        Assert.Contains(result.Difficulties,g=>g.Kind==PerformanceGroupKind.Tag&&g.Name=="Sicurezza");
        Assert.Contains(result.Difficulties,g=>g.Kind==PerformanceGroupKind.Source&&g.Name=="Protocolli");
        Assert.Equal("Protocolli",result.Session.Questions[0].Citations[0].SourceTitle);
    }
    [Fact] public async Task Performance_is_project_scoped_and_incomplete_attempts_have_no_final_result()
    {
        var (p,a)=await StartAsync();var other=await MaterialAsync();
        await Assert.ThrowsAsync<StudyValidationException>(()=>Performance().ResultAsync(p,a));
        await Attempts().FinishAsync(p,a);
        await Assert.ThrowsAsync<QuizNotFoundException>(()=>Performance().ResultAsync(other.Project,a));
        Assert.Empty(await Performance().HistoryAsync(other.Project));Assert.Equal(0,(await Performance().StatisticsAsync(other.Project)).CompletedCount);
        Assert.Empty((await Performance().StatisticsAsync(p)).Difficulties);
        await Assert.ThrowsAsync<ProjectNotFoundException>(()=>Performance().StatisticsAsync(Guid.NewGuid()));
    }
    [Fact] public async Task Completed_history_uses_stable_keyset_pages_and_results_ignore_later_evidence()
    {
        var m=await MaterialAsync();var completed=new List<Guid>();
        for(var i=0;i<23;i++)
        {
            var a=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Closed,1,Difficulty.Easy),m.Chunks);
            var q=(await Attempts().GetAsync(m.Project,a)).Questions[0];
            await Attempts().AnswerClosedAsync(m.Project,a,q.Id,q.Options.Single(o=>o.IsCorrect).Id,1);
            await Attempts().FinishAsync(m.Project,a);completed.Add(a);
        }
        var repo=new StudyPerformanceRepository(Db());var first=await repo.CompletedAsync(m.Project,clock.UtcNow,null,20);
        var last=first[^1].Attempt;var second=await repo.CompletedAsync(m.Project,clock.UtcNow,new(last.CompletedAtUtc!.Value,last.Id),20);
        Assert.Equal(20,first.Count);Assert.Equal(3,second.Count);Assert.Equal(23,first.Concat(second).Select(s=>s.Attempt.Id).Distinct().Count());
        Assert.Equal(23,(await Performance().StatisticsAsync(m.Project)).CompletedCount);Assert.Equal(10,(await Performance().StatisticsAsync(m.Project)).Recent.Count);
        clock.UtcNow=clock.UtcNow.AddDays(1);
        var later=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Closed,1,Difficulty.Easy),m.Chunks);
        var wrong=(await Attempts().GetAsync(m.Project,later)).Questions[0];await Attempts().AnswerClosedAsync(m.Project,later,wrong.Id,wrong.Options.First(o=>!o.IsCorrect).Id,1);await Attempts().FinishAsync(m.Project,later);
        Assert.Empty((await Performance().ResultAsync(m.Project,completed[0])).Difficulties);
        Assert.Equal(24,(await Performance().StatisticsAsync(m.Project)).CompletedCount);
    }
    [Fact] public async Task Migration_from_five_preserves_old_snapshots_without_topic_or_tag_metadata()
    {
        var (p,a)=await StartAsync(QuizMode.Closed,1);await Attempts().FinishAsync(p,a);
        await Db().ExecuteAsync(db=>
        {
            var row=db.FindWithQuery<QuizSnapshotRow>("SELECT SnapshotJson FROM AttemptQuestion WHERE ProjectId=?",p.ToString());
            var json=System.Text.Json.Nodes.JsonNode.Parse(row.SnapshotJson)!.AsObject();json.Remove("Topics");json.Remove("Tags");
            db.Execute("UPDATE AttemptQuestion SET SnapshotJson=? WHERE ProjectId=?",json.ToJsonString(),p.ToString());
            db.Execute("DROP INDEX IX_QuizAttempt_Completed");db.Execute("DELETE FROM SchemaMigration WHERE Version=6");return 0;
        });
        var result=await Performance().ResultAsync(p,a);Assert.Equal(1,result.Unanswered);Assert.Null(result.Session.Questions[0].Topics);Assert.Null(result.Session.Questions[0].Tags);
        Assert.Equal(6,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT MAX(Version) FROM SchemaMigration")));
        Assert.Equal(1,await Db().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_QuizAttempt_Completed'")));
    }
    [Fact] public async Task Resume_shortcut_finds_most_recently_used_live_attempt_in_its_project_only()
    {
        var m=await MaterialAsync();var first=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Closed,1,Difficulty.Easy),m.Chunks);
        clock.UtcNow=clock.UtcNow.AddMinutes(1);var second=await Quizzes().GenerateAsync(m.Project,Guid.NewGuid(),new(QuizMode.Closed,1,Difficulty.Easy),m.Chunks);
        Assert.Equal(second,await Quizzes().LatestInProgressAsync(m.Project));
        clock.UtcNow=clock.UtcNow.AddMinutes(1);var q=(await Attempts().GetAsync(m.Project,first)).Questions[0];await Attempts().AnswerClosedAsync(m.Project,first,q.Id,q.Options[0].Id,1);
        Assert.Equal(first,await Quizzes().LatestInProgressAsync(m.Project));await Attempts().FinishAsync(m.Project,first);Assert.Equal(second,await Quizzes().LatestInProgressAsync(m.Project));
        await Attempts().AbandonAsync(m.Project,second);var other=await StartAsync();Assert.Null(await Quizzes().LatestInProgressAsync(m.Project));Assert.Equal(other.Attempt,await Quizzes().LatestInProgressAsync(other.Project));
        await Assert.ThrowsAsync<ProjectNotFoundException>(()=>Quizzes().LatestInProgressAsync(Guid.NewGuid()));
    }
    public void Dispose(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
    private sealed class QuizClock:IClock {public DateTimeOffset UtcNow {get;set;}=new(2026,10,25,10,0,0,TimeSpan.Zero);}
    private sealed class QuizAi:IAiStudyService
    {
        private readonly MockAiStudyService mock=new();
        public AiProviderInfo Provider=>mock.Provider;
        public int Generations,Evaluations;public bool Fail,BadScore;public double Grade=1;public Task<OpenAnswerEvaluation>? Delayed;
        public Func<AiQuizDraft,AiQuizDraft>? Transform;
        public Task<AiFlashcardResult> GenerateFlashCardsAsync(FlashcardGenerationRequest request,CancellationToken ct=default)=>mock.GenerateFlashCardsAsync(request,ct);
        public async Task<AiQuizDraft> GenerateQuizAsync(QuizGenerationRequest request,CancellationToken ct=default)
        {Generations++;var draft=await mock.GenerateQuizAsync(request,ct);return Transform?.Invoke(draft)??draft;}
        public Task<OpenAnswerEvaluation> EvaluateOpenAnswerAsync(OpenAnswerEvaluationRequest request,CancellationToken ct=default)
        {
            Evaluations++;if(Delayed is not null)return Delayed;if(Fail)throw new IOException("Provider offline");
            var result=TestEvaluation.Create(request.Question,request.Question.Rubric.Select(r=>new CriterionAssessment(r.Id,Grade)).ToArray());
            return Task.FromResult(BadScore?result with{Criteria=[new("unknown",double.NaN)]}:result);
        }
    }
}






