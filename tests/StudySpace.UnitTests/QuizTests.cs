using StudySpace.Application.Performance;
using StudySpace.App.Features.Performance;
using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.App.Features.Flashcards;
using StudySpace.App.Features.Quizzes;
using StudySpace.App.Services;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
using StudySpace.Domain.Projects;
using StudySpace.Domain.Quizzes;
using StudySpace.Domain.Sources;
namespace StudySpace.UnitTests;

public sealed class QuizTests
{
    private readonly TestClock clock=new();
    private readonly QuizMemory repository=new();
    private readonly QuizAi ai=new();
    private readonly TestNavigation navigation=new();
    private readonly QuizDialogs dialogs=new();
    private QuizAttemptService Attempts()=>new(repository,ai,clock);
    private QuizRunViewModel Run()=>new(Attempts(),navigation,dialogs,NullLogger<QuizRunViewModel>.Instance);
    private void Configure(QuizRunViewModel vm)=>vm.Configure(repository.Session.Attempt.ProjectId.ToString(),repository.Session.Attempt.Id.ToString());

    [Theory]
    [InlineData(QuizMode.Mixed,1)][InlineData(QuizMode.Closed,0)][InlineData(QuizMode.Open,51)][InlineData((QuizMode)99,5)]
    public void Invalid_configuration_is_rejected(QuizMode mode,int count)=>Assert.Throws<StudyValidationException>(()=>QuizService.Validate(new(mode,count,null)));
    [Fact] public void Open_assessment_uses_rubric_weights_and_exact_outcome_thresholds()
    {
        var q=repository.Session.Questions[1] with{Rubric=[new("a","uno",0.8,[Guid.NewGuid()]),new("b","due",0.2,[Guid.NewGuid()])]};
        OpenAnswerEvaluation Result(double a,double b)=>TestEvaluation.Create(q,[new("a",a),new("b",b)]);
        Assert.Equal(AnswerOutcome.Correct,QuizAttemptService.ValidateAssessment(q,Result(1,0),ai.Provider).Outcome);
        Assert.Equal(AnswerOutcome.Partial,QuizAttemptService.ValidateAssessment(q,Result(0,1),ai.Provider).Outcome);
        Assert.Equal(AnswerOutcome.Incorrect,QuizAttemptService.ValidateAssessment(q,Result(0,0),ai.Provider).Outcome);
        Assert.Throws<AiStudyException>(()=>QuizAttemptService.ValidateAssessment(q,Result(double.NaN,0),ai.Provider));
        Assert.Throws<AiStudyException>(()=>QuizAttemptService.ValidateAssessment(q,Result(1,2),ai.Provider));
    }
    [Fact] public async Task All_fifty_numbers_are_directly_navigable_and_current_is_orthogonal_to_answer_state()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(50,vm.Numbers.Count);Assert.Equal("Domanda 1 di 50",vm.PositionLabel);Assert.Empty(vm.Citations);
        await vm.Numbers[49].SelectCommand.ExecuteAsync(null);
        Assert.Equal("Domanda 50 di 50",vm.PositionLabel);Assert.True(vm.IsOpen);Assert.Contains("corrente",vm.Numbers[49].AccessibleTitle);Assert.True(vm.Numbers[49].IsCurrent);Assert.False(vm.Numbers[0].IsCurrent);
        vm.OpenText="Risposta finale";await vm.FlushAsync();
        Assert.Equal("●",vm.Numbers[49].StateLabel);Assert.Contains("corrente",vm.Numbers[49].AccessibleTitle);
        await vm.Numbers[0].SelectCommand.ExecuteAsync(null);Assert.True(vm.IsClosed);
        await vm.Numbers[49].SelectCommand.ExecuteAsync(null);Assert.Equal("Risposta finale",vm.OpenText);
        Assert.Equal(0,ai.Evaluations);Assert.Equal(1d/50,vm.Progress);
    }
    [Fact] public async Task Typing_autosaves_without_calling_evaluator_and_change_question_flushes()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);await vm.Numbers[1].SelectCommand.ExecuteAsync(null);
        vm.OpenText="Prima bozza";vm.OpenText="Bozza aggiornata";
        await Task.Delay(850);
        Assert.Equal("Bozza aggiornata",repository.Session.Answers[1].Text);Assert.Equal(0,ai.Evaluations);
        vm.OpenText="Ultimo testo";await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal("Ultimo testo",repository.Session.Answers[1].Text);Assert.Equal("Domanda 3 di 50",vm.PositionLabel);
    }
    [Fact] public async Task Failed_save_keeps_draft_and_current_question_until_retry()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);await vm.Numbers[1].SelectCommand.ExecuteAsync(null);
        repository.FailSave=true;vm.OpenText="Testo da conservare";await vm.NextCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);Assert.Equal("Testo da conservare",vm.OpenText);Assert.Equal("Domanda 2 di 50",vm.PositionLabel);
        Assert.False(await vm.PrepareToLeaveAsync());Assert.False(repository.Session.Answers[1].IsAnswered);
        repository.FailSave=false;await vm.RetrySaveCommand.ExecuteAsync(null);Assert.Equal("Testo da conservare",repository.Session.Answers[1].Text);
        Assert.True(await vm.PrepareToLeaveAsync());
    }
    [Fact] public async Task Closed_choice_shows_feedback_and_citations_then_locks_choices()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);Assert.False(vm.HasFeedback);Assert.Empty(vm.Citations);
        await vm.Options[0].ChooseCommand.ExecuteAsync(null);
        Assert.True(vm.HasFeedback);Assert.Equal("✓ Corretto",vm.FeedbackTitle);Assert.NotEmpty(vm.Citations);
        Assert.All(vm.Options,o=>Assert.False(o.IsEnabled));Assert.Equal("✓",vm.Numbers[0].StateLabel);Assert.Equal(0,ai.Evaluations);
        await vm.Options[1].ChooseCommand.ExecuteAsync(null);Assert.Equal("✓ Corretto",vm.FeedbackTitle);
    }
    [Fact] public async Task Open_verification_is_explicit_and_can_produce_partial_feedback()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);await vm.Numbers[1].SelectCommand.ExecuteAsync(null);
        await vm.VerifyCommand.ExecuteAsync(null);Assert.True(vm.HasError);Assert.Equal(0,ai.Evaluations);
        vm.OpenText="Risposta parziale";await vm.VerifyCommand.ExecuteAsync(null);
        Assert.Equal(1,ai.Evaluations);Assert.Equal("◐ Parzialmente corretto",vm.FeedbackTitle);Assert.True(vm.IsReadOnly);
        Assert.Equal("◐",vm.Numbers[1].StateLabel);Assert.NotEmpty(vm.Citations);
        await vm.PreviousCommand.ExecuteAsync(null);Assert.False(vm.HasFeedback);Assert.Empty(vm.Citations);
    }
    [Fact] public async Task Finish_requires_confirmation_and_completed_attempt_has_a_read_only_summary()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);
        dialogs.Confirm=false;await vm.FinishCommand.ExecuteAsync(null);Assert.True(vm.IsInProgress);Assert.Equal(0,repository.Submits);
        dialogs.Confirm=true;await vm.FinishCommand.ExecuteAsync(null);
        Assert.False(vm.IsInProgress);Assert.False(vm.IsEditable);Assert.Contains("Non risposte 50",vm.Summary);
        Assert.Equal(0,ai.Evaluations);Assert.Equal(1,repository.Submits);Assert.Equal(repository.Session.Attempt.Id,navigation.OpenedResult);
    }
    [Fact] public async Task Abandon_requires_confirmation_and_invalid_route_shows_recoverable_error()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);
        await vm.AbandonCommand.ExecuteAsync(null);Assert.True(vm.IsInProgress);
        dialogs.Confirm=true;await vm.AbandonCommand.ExecuteAsync(null);Assert.False(vm.IsInProgress);Assert.Contains("abbandonato",vm.Summary);
        var invalid=Run();invalid.Configure("bad","bad");await invalid.LoadCommand.ExecuteAsync(null);Assert.True(invalid.HasError);Assert.False(invalid.HasSession);
    }
    [Fact] public async Task Setup_exposes_presets_custom_mixed_difficulty_and_does_not_duplicate_after_navigation_failure()
    {
        var projects=new TestProjectRepository();var project=new StudyProject{Name="Reti"};projects.Items.Add(project);
        var context=new TestStudyContext();var chunk=new SourceChunk{ProjectId=project.Id,SourceId=Guid.NewGuid(),SourceRevisionId=Guid.NewGuid(),Text="Materiale del progetto per domande verificabili e complete.",StartOffset=0,EndOffset=58};
        context.Chunks.Add(new(chunk,"Lezione",true));
        var projectService=new ProjectService(projects,clock);
        var cards=new FlashcardService(new TestFlashcardRepository(),context,projectService,clock);
        var material=new StudyMaterialPickerViewModel(cards,NullLogger<StudyMaterialPickerViewModel>.Instance);
        var service=new QuizService(ai,context,repository,projectService,clock);
        var vm=new QuizSetupViewModel(service,projectService,material,navigation,NullLogger<QuizSetupViewModel>.Instance);
        vm.Configure(project.Id.ToString());await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(new int?[]{5,10,15,20,null},vm.Counts.Select(c=>c.Value));
        Assert.Equal(3,vm.Modes.Count);Assert.Null(vm.SelectedDifficulty!.Value);
        vm.SelectedCount=vm.Counts[^1];vm.CustomCount="50";Assert.True(vm.IsCustomCount);
        await material.LoadChunksCommand.ExecuteAsync(null);material.Chunks[0].IsSelected=true;
        navigation.FailQuizOpen=true;await vm.GenerateCommand.ExecuteAsync(null);Assert.True(vm.HasError);
        Assert.True(vm.IsStarted);Assert.Equal(50,repository.Created!.Configuration.Count);Assert.Equal(50,repository.Created.Questions.Count);
        navigation.FailQuizOpen=false;await vm.GenerateCommand.ExecuteAsync(null);Assert.Equal(1,repository.Creates);
    }
    [Fact] public async Task Open_feedback_exposes_each_learning_section_and_confidence()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);await vm.Numbers[1].SelectCommand.ExecuteAsync(null);
        vm.OpenText="Una risposta molto breve";await vm.VerifyCommand.ExecuteAsync(null);
        Assert.True(vm.HasOpenFeedback);Assert.Contains("Concetto riconosciuto",vm.CorrectConceptsText);
        Assert.NotEmpty(vm.MissingConceptsText);Assert.NotEmpty(vm.IncorrectConceptsText);Assert.NotEmpty(vm.SuggestionsText);
        Assert.Contains("50/100",vm.EvaluationDetails);Assert.Contains("Confidenza",vm.EvaluationDetails);
        Assert.NotEmpty(vm.IdealAnswer);Assert.False(vm.IsEvaluationRunning);
    }
    [Fact] public async Task Cancel_verification_stops_loading_and_keeps_saved_answer()
    {
        var vm=Run();Configure(vm);await vm.LoadCommand.ExecuteAsync(null);await vm.Numbers[1].SelectCommand.ExecuteAsync(null);
        var pending=new TaskCompletionSource<OpenAnswerEvaluation>();ai.Pending=pending.Task;
        vm.OpenText="Bozza da non perdere";var operation=vm.VerifyCommand.ExecuteAsync(null);
        Assert.True(vm.IsEvaluationRunning);Assert.Equal("Bozza da non perdere",repository.Session.Answers[1].Text);
        vm.CancelEvaluationCommand.Execute(null);await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(vm.IsEvaluationRunning);Assert.False(vm.HasFeedback);Assert.Equal("Bozza da non perdere",vm.OpenText);
        pending.SetResult(TestEvaluation.Create(repository.Session.Questions[1]));
    }
    private StudyPerformanceService Performance()
    {
        var projects=new TestProjectRepository();projects.Items.Add(new StudyProject{Id=repository.Session.Attempt.ProjectId,Name="Reti"});
        return new(new ReadPerformance(()=>repository.Session),repository,new ProjectService(projects,clock),clock);
    }
    [Fact] public async Task Results_page_exposes_all_fifty_saved_questions_and_opens_exact_historical_evidence()
    {
        repository.Session=repository.Session with{Attempt=repository.Session.Attempt with{Status=AttemptStatus.Completed,Score=0,CompletedAtUtc=clock.UtcNow}};
        var vm=new QuizResultsViewModel(Performance(),navigation,NullLogger<QuizResultsViewModel>.Instance);
        vm.Configure(repository.Session.Attempt.ProjectId.ToString(),repository.Session.Attempt.Id.ToString());await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsReady);Assert.Equal(5,vm.Questions.Count);Assert.Contains("Non risposte: 50",vm.Counts);Assert.Contains("50 domande",vm.Details);
        await vm.Questions[0].References[0].OpenCommand.ExecuteAsync(null);Assert.Equal(repository.Session.Attempt.Id,navigation.OpenedEvidence!.Value.Attempt);
        for(var i=0;i<9;i++)vm.NextCommand.Execute(null);Assert.Contains("46–50",vm.PageLabel);Assert.False(vm.CanNext);Assert.Contains("Domanda 50",vm.Questions[^1].Heading);
        await vm.LoadCommand.ExecuteAsync(null);Assert.Contains("46–50",vm.PageLabel);
        vm.PreviousCommand.Execute(null);Assert.Contains("41–45",vm.PageLabel);Assert.Equal(0,ai.Evaluations);
    }
    [Fact] public async Task History_routes_completed_results_and_statistics_handle_no_completed_quizzes()
    {
        var projects=new TestProjectRepository();projects.Items.Add(new StudyProject{Id=repository.Session.Attempt.ProjectId,Name="Reti"});var projectService=new ProjectService(projects,clock);
        var stats=new ProjectStatisticsViewModel(Performance(),projectService,navigation,NullLogger<ProjectStatisticsViewModel>.Instance);
        stats.Configure(repository.Session.Attempt.ProjectId.ToString());await stats.LoadCommand.ExecuteAsync(null);
        Assert.True(stats.IsReady);Assert.False(stats.HasResults);Assert.Contains("Quiz completati: 0",stats.Summary);Assert.Empty(stats.Recent);
        repository.Session=repository.Session with{Attempt=repository.Session.Attempt with{Status=AttemptStatus.Completed,Score=0,CompletedAtUtc=clock.UtcNow}};
        var history=new QuizHistoryViewModel(Performance(),projectService,navigation,NullLogger<QuizHistoryViewModel>.Instance);
        history.Configure(repository.Session.Attempt.ProjectId.ToString());await history.LoadCommand.ExecuteAsync(null);
        Assert.False(history.IsEmpty);await Assert.Single(history.Items).OpenCommand.ExecuteAsync(null);Assert.Equal(repository.Session.Attempt.Id,navigation.OpenedResult);
        await stats.LoadCommand.ExecuteAsync(null);Assert.True(stats.HasResults);Assert.Single(stats.Recent);Assert.False(stats.HasDifficulties);
    }
    [Fact] public async Task Evidence_is_hidden_before_verification_and_foreign_routes_are_rejected()
    {
        var q=repository.Session.Questions[0];var a=repository.Session.Attempt;
        var vm=new QuizEvidenceViewModel(Attempts(),NullLogger<QuizEvidenceViewModel>.Instance);
        vm.Configure(a.ProjectId.ToString(),a.Id.ToString(),q.Id.ToString(),q.Citations[0].ChunkId.ToString());await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.IsReady);Assert.True(vm.HasError);
        await Attempts().AnswerClosedAsync(a.ProjectId,a.Id,q.Id,q.Options[0].Id,1);await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsReady);Assert.Equal(q.Citations[0].Quote,vm.Quote);Assert.Equal(q.Citations[0].SourceTitle,vm.Title);
        vm.Configure(Guid.NewGuid().ToString(),a.Id.ToString(),q.Id.ToString(),q.Citations[0].ChunkId.ToString());await vm.LoadCommand.ExecuteAsync(null);Assert.False(vm.IsReady);Assert.True(vm.HasError);
    }
    private sealed class ReadPerformance(Func<QuizSession> session):IStudyPerformanceRepository
    {
        public Task<IReadOnlyList<HistoryAttempt>> HistoryAsync(Guid p,int offset,int limit,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<HistoryAttempt>>(offset==0?[StudyPerformanceService.Summarize(session())]:[]);
        public Task<IReadOnlyList<QuizSession>> CompletedAsync(Guid p,DateTimeOffset asOf,PerformanceCursor? cursor,int limit,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<QuizSession>>(
            cursor is null&&session().Attempt.Status==AttemptStatus.Completed?[session()]:[]);
    }
    [Fact] public async Task Continue_study_resumes_live_quiz_then_falls_back_to_cards_or_sources()
    {
        var projectRepo=new TestProjectRepository{FlashcardCount=3,SourceCount=1};var p=new StudyProject{Id=repository.Session.Attempt.ProjectId,Name="Reti"};projectRepo.Items.Add(p);
        var projects=new ProjectService(projectRepo,clock);var quizzes=new QuizService(ai,new TestStudyContext(),repository,projects,clock);
        var vm=new StudySpace.App.Features.Projects.ProjectDashboardViewModel(projects,navigation,new(projects,navigation,new TestDialogs()),NullLogger<StudySpace.App.Features.Projects.ProjectDashboardViewModel>.Instance,quizzes);
        vm.Configure(p.Id.ToString());await vm.LoadCommand.ExecuteAsync(null);await vm.ContinueStudyCommand.ExecuteAsync(null);
        Assert.Equal(repository.Session.Attempt.Id,navigation.OpenedQuizAttempt);Assert.Contains("quiz",vm.ContinueHint);Assert.Null(navigation.StudyQuery);
        repository.Session=repository.Session with{Attempt=repository.Session.Attempt with{Status=AttemptStatus.Completed}};
        await vm.LoadCommand.ExecuteAsync(null);await vm.ContinueStudyCommand.ExecuteAsync(null);Assert.NotNull(navigation.StudyQuery);Assert.Contains("flashcard",vm.ContinueHint);
        projectRepo.FlashcardCount=0;await vm.LoadCommand.ExecuteAsync(null);await vm.ContinueStudyCommand.ExecuteAsync(null);Assert.Equal("sources",navigation.Section);
        Assert.Equal(0,ai.Evaluations);Assert.Equal(0,repository.Creates);
    }
    private sealed class QuizDialogs:IQuizDialogs
    {
        public bool Confirm;
        public Task<bool> ConfirmFinishAsync(int unanswered)=>Task.FromResult(Confirm);
        public Task<bool> ConfirmAbandonAsync()=>Task.FromResult(Confirm);
    }
    private sealed class QuizAi:IAiStudyService
    {
        public int Evaluations;public Task<OpenAnswerEvaluation>? Pending;
        public AiProviderInfo Provider=>new("unit","Demo",true);
        public Task<AiFlashcardResult> GenerateFlashCardsAsync(FlashcardGenerationRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<AiQuizDraft> GenerateQuizAsync(QuizGenerationRequest request,CancellationToken ct=default)
        {
            var c=request.Chunks[0].Chunk;
            return Task.FromResult(new AiQuizDraft(Enumerable.Range(0,request.Configuration.Count).Select(i=>new AiQuizQuestion(QuestionKind.Closed,
                "Domanda "+i,"Spiegazione","Soluzione",(Difficulty)(i%3),[new("Giusta",true),new("Errata",false)],[],[new(c.Id,c.Text)])).ToArray()));
        }
        public Task<OpenAnswerEvaluation> EvaluateOpenAnswerAsync(OpenAnswerEvaluationRequest request,CancellationToken ct=default)
        {Evaluations++;if(Pending is not null)return Pending;return Task.FromResult(TestEvaluation.Create(request.Question,request.Question.Rubric.Select(r=>new CriterionAssessment(r.Id,0.5)).ToArray()));}
    }
    private sealed class QuizMemory:IQuizRepository
    {
        public QuizSession Session;
        public bool FailSave;public int Submits,Creates;public QuizStartData? Created;
        public QuizMemory()
        {
            var p=Guid.NewGuid();var a=Guid.NewGuid();var chunk=Guid.NewGuid();
            var questions=Enumerable.Range(0,50).Select(i=>new QuizQuestionSnapshot(Guid.NewGuid(),i,i%2==0?QuestionKind.Closed:QuestionKind.Open,
                "Domanda "+i,"Spiegazione","Risposta ideale",Difficulty.Medium,i%2==0?[new(Guid.NewGuid(),"Giusta",true),new(Guid.NewGuid(),"Errata",false)]:[],
                i%2==1?[new("concept","Concetto",1,[chunk])]:[],[new(Guid.NewGuid(),Guid.NewGuid(),chunk,"Citazione","Lezione",null)])).ToArray();
            Session=new(new QuizAttempt{Id=a,ProjectId=p,MaximumScore=50,Mode=QuizMode.Mixed},"Quiz",true,"unit",true,questions,
                questions.Select(q=>new QuizAnswerState(q.Id,"",null,1,EvaluationStatus.NotRequested,null,null)).ToArray());
        }
        private QuizAnswerState Answer(Guid q)=>Session.Answers.Single(x=>x.QuestionId==q);
        private void Set(QuizAnswerState b)=>Session=Session with{Answers=Session.Answers.Select(x=>x.QuestionId==b.QuestionId?b:x).ToArray()};
        public Task<Guid> CreateAsync(QuizStartData quiz,CancellationToken ct=default){Creates++;Created=quiz;return Task.FromResult(quiz.OperationId);}
        public Task<Guid?> LatestInProgressAsync(Guid p,CancellationToken ct=default)=>Task.FromResult<Guid?>(Session.Attempt.ProjectId==p&&Session.Attempt.Status==AttemptStatus.InProgress?Session.Attempt.Id:null);
        public Task<IReadOnlyList<QuizAttemptSummary>> ListAsync(Guid p,int o,int l,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<QuizAttemptSummary>>([]);
        public Task<QuizSession?> GetAsync(Guid p,Guid a,CancellationToken ct=default)=>Task.FromResult<QuizSession?>(p==Session.Attempt.ProjectId&&a==Session.Attempt.Id?Session:null);
        public Task RecoverAsync(Guid p,Guid a,DateTimeOffset n,CancellationToken ct=default)=>Task.CompletedTask;
        public Task<QuizAnswerState> SaveOpenAsync(Guid p,Guid a,Guid q,string text,int r,DateTimeOffset n,CancellationToken ct=default)
        {
            if(FailSave)throw new StorageUnavailableException("Archivio occupato",new IOException());
            var b=Answer(q) with{Text=text,Revision=r+1};Set(b);return Task.FromResult(b);
        }
        public Task<QuizAnswerState> AnswerClosedAsync(Guid p,Guid a,Guid q,Guid o,int r,DateTimeOffset n,CancellationToken ct=default)
        {
            var question=Session.Questions.Single(x=>x.Id==q);var option=question.Options.Single(x=>x.Id==o);
            var b=Answer(q) with{SelectedOptionId=o,Revision=r+1,Status=EvaluationStatus.Evaluated,
                Feedback=new(option.IsCorrect?AnswerOutcome.Correct:AnswerOutcome.Incorrect,option.IsCorrect?1:0,"Spiegazione","Giusta",[],[],[],"",[],"local",false)};Set(b);return Task.FromResult(b);
        }
        public Task<EvaluationClaim?> ClaimAsync(Guid p,Guid a,Guid q,DateTimeOffset n,CancellationToken ct=default)
        {
            var b=Answer(q);if(!b.IsAnswered)throw new StudyValidationException("Scrivi una risposta.");
            return Task.FromResult<EvaluationClaim?>(new(Session.Questions.Single(x=>x.Id==q),b,Guid.NewGuid()));
        }
        public Task<bool> AcceptAsync(Guid p,Guid a,EvaluationClaim c,AnswerFeedback f,DateTimeOffset n,CancellationToken ct=default)
        {Set(Answer(c.Question.Id) with{Feedback=f,Status=EvaluationStatus.Evaluated});return Task.FromResult(true);}
        public Task FailAsync(Guid p,Guid a,EvaluationClaim c,string m,DateTimeOffset n,CancellationToken ct=default)=>Task.CompletedTask;
        public Task SubmitAsync(Guid p,Guid a,DateTimeOffset n,CancellationToken ct=default){Submits++;Session=Session with{Attempt=Session.Attempt with{Status=AttemptStatus.SubmittedPendingEvaluation}};return Task.CompletedTask;}
        public Task<bool> CompleteAsync(Guid p,Guid a,DateTimeOffset n,CancellationToken ct=default)
        {Session=Session with{Attempt=Session.Attempt with{Status=AttemptStatus.Completed,Score=Session.Answers.Sum(b=>b.Feedback?.Score??0)}};return Task.FromResult(true);}
        public Task AbandonAsync(Guid p,Guid a,DateTimeOffset n,CancellationToken ct=default)
        {Session=Session with{Attempt=Session.Attempt with{Status=AttemptStatus.Abandoned}};return Task.CompletedTask;}
        public Task CheckpointAsync(Guid p,Guid a,int position,long ms,DateTimeOffset n,CancellationToken ct=default)=>Task.CompletedTask;
        public Task<int> GetPositionAsync(Guid p,Guid a,CancellationToken ct=default)=>Task.FromResult(0);
    }
}





