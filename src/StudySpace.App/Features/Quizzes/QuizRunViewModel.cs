using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Common;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
namespace StudySpace.App.Features.Quizzes;

public partial class QuestionNumberViewModel(int position,Func<int,Task> select):ObservableObject
{
    public int Position {get;}=position;
    public string Number=>(Position+1).ToString();
    [ObservableProperty] public partial string StateLabel {get;set;}="○";
    [ObservableProperty] public partial string AccessibleTitle {get;set;}="";
    [ObservableProperty] public partial bool IsCurrent {get;set;}
    [ObservableProperty] public partial string VisualState {get;set;}="Unanswered";
    public IAsyncRelayCommand SelectCommand {get;}=new AsyncRelayCommand(()=>select(position));
}
public sealed class QuizOptionViewModel
{
    public Guid Id {get;}
    public string Text {get;}
    public string Feedback {get;}
    public bool IsEnabled {get;}
    public string VisualState {get;}
    public bool HasFeedback=>Feedback.Length>0;
    public IAsyncRelayCommand ChooseCommand {get;}
    public QuizOptionViewModel(QuizOptionSnapshot option,QuizAnswerState answer,bool enabled,bool review,Func<Guid,Task> choose)
    {
        Id=option.Id;Text=option.Text;IsEnabled=enabled;
        VisualState=answer.Status==EvaluationStatus.Evaluated||review?option.IsCorrect?"Correct":answer.SelectedOptionId==option.Id?"Incorrect":"Neutral":"Neutral";
        Feedback=answer.Status==EvaluationStatus.Evaluated||review?(option.IsCorrect?"✓ Risposta corretta":answer.SelectedOptionId==option.Id?"✕ Risposta scelta":""):"";
        ChooseCommand=new AsyncRelayCommand(()=>choose(option.Id));
    }
}
public sealed record QuizCitationViewModel(string Title,string Quote);
public partial class QuizRunViewModel(QuizAttemptService attempts,INavigationService navigation,IQuizDialogs dialogs,ILogger<QuizRunViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId,attemptId;private QuizSession? session;private int index;private bool loaded,settingText,leaving,evaluating,foreground=true;
    private readonly SemaphoreSlim saveGate=new(1,1);
    private readonly Stopwatch active=new();
    private long baseActiveMs;
    private CancellationTokenSource? debounce;
    public ObservableCollection<QuestionNumberViewModel> Numbers {get;}=[];
    public bool IsLeaving=>leaving;
    public ObservableCollection<QuizOptionViewModel> Options {get;}=[];
    public ObservableCollection<QuizCitationViewModel> Citations {get;}=[];
    [ObservableProperty] public partial string PositionLabel {get;set;}="";
    [ObservableProperty] public partial string Prompt {get;set;}="";
    [ObservableProperty] public partial string OpenText {get;set;}="";
    [ObservableProperty] public partial bool IsOpen {get;set;}
    [ObservableProperty] public partial bool IsClosed {get;set;}
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsReadOnly))] public partial bool IsEditable {get;set;}
    public bool IsReadOnly=>!IsEditable;
    [ObservableProperty] public partial bool CanVerify {get;set;}
    [ObservableProperty] public partial bool CanPrevious {get;set;}
    [ObservableProperty] public partial bool CanNext {get;set;}
    [ObservableProperty] public partial bool HasSession {get;set;}
    [ObservableProperty] public partial bool IsInProgress {get;set;}
    [ObservableProperty] public partial bool IsPending {get;set;}
    [ObservableProperty] public partial bool IsCompleted {get;set;}
    [ObservableProperty] public partial bool HasFeedback {get;set;}
    [ObservableProperty] public partial string FeedbackTitle {get;set;}="";
    [ObservableProperty] public partial string FeedbackState {get;set;}="Neutral";
    [ObservableProperty] public partial string FeedbackText {get;set;}="";
    [ObservableProperty] public partial string IdealAnswer {get;set;}="";
    [ObservableProperty] public partial string AnswerError {get;set;}="";
    [ObservableProperty] public partial bool HasAnswerError {get;set;}
    [ObservableProperty] public partial string SaveStatus {get;set;}="";
    [ObservableProperty] public partial string Summary {get;set;}="";
    [ObservableProperty] public partial string ProviderMessage {get;set;}="";
    [ObservableProperty] public partial double Progress {get;set;}
    [ObservableProperty] public partial int CurrentPosition {get;set;}
    [ObservableProperty] public partial string CorrectConceptsText {get;set;}="";
    [ObservableProperty] public partial string MissingConceptsText {get;set;}="";
    [ObservableProperty] public partial string IncorrectConceptsText {get;set;}="";
    [ObservableProperty] public partial string SuggestionsText {get;set;}="";
    [ObservableProperty] public partial string EvaluationDetails {get;set;}="";
    [ObservableProperty] public partial bool HasOpenFeedback {get;set;}
    [ObservableProperty] public partial bool IsEvaluationRunning {get;set;}
    [RelayCommand] private void CancelEvaluation()
    { VerifyCommand.Cancel(); FinishCommand.Cancel(); RetryEvaluationCommand.Cancel(); }
    public void Configure(string project,string attempt)
    {projectId=Guid.TryParse(project,out var p)?p:Guid.Empty;attemptId=Guid.TryParse(attempt,out var a)?a:Guid.Empty;}
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        if(loaded){ResumeTimer();return;}
        session=await attempts.ResumeAsync(projectId,attemptId);baseActiveMs=session.Attempt.ActiveDurationMs;
        index=Math.Clamp(await attempts.PositionAsync(projectId,attemptId),0,session.Questions.Count-1);
        Numbers.Clear();for(var i=0;i<session.Questions.Count;i++)Numbers.Add(new(i,SelectAsync));
        loaded=true;HasSession=true;leaving=false;Present();ResumeTimer();
    });
    private QuizQuestionSnapshot Current=>session!.Questions[index];
    private QuizAnswerState Answer=>session!.Answers.Single(a=>a.QuestionId==Current.Id);
    private void SetAnswer(QuizAnswerState answer)=>session=session! with {Answers=session!.Answers.Select(a=>a.QuestionId==answer.QuestionId?answer:a).ToArray()};
    private void Present()
    {
        if(session is null)return;
        var q=Current;var answer=Answer;IsInProgress=session.Attempt.Status==AttemptStatus.InProgress;IsPending=session.Attempt.Status==AttemptStatus.SubmittedPendingEvaluation;
        IsCompleted=session.Attempt.Status==AttemptStatus.Completed;
        IsOpen=q.Kind==QuestionKind.Open;IsClosed=!IsOpen;IsEditable=IsInProgress&&!answer.IsLocked;
        CanVerify=IsOpen&&IsEditable;CanPrevious=index>0;CanNext=index<session.Questions.Count-1;
        PositionLabel=$"Domanda {index+1} di {session.Questions.Count}";Prompt=q.Prompt;
        settingText=true;OpenText=answer.Text;settingText=false;
        var review=session.Attempt.Status==AttemptStatus.Completed;
        Options.Clear();foreach(var option in q.Options)Options.Add(new(option,answer,IsEditable,review,ChooseAsync));
        HasFeedback=answer.Feedback is not null||review;
        FeedbackState=answer.Feedback?.Outcome switch {AnswerOutcome.Correct=>"Correct",AnswerOutcome.Partial=>"Partial",AnswerOutcome.Incorrect=>"Incorrect",_=>"Neutral"};
        FeedbackTitle=answer.Feedback?.Outcome switch {AnswerOutcome.Correct=>"✓ Corretto",AnswerOutcome.Partial=>"◐ Parzialmente corretto",AnswerOutcome.Incorrect=>"↻ Da rivedere",_=>review?"○ Non risposta":""};
        FeedbackText=answer.Feedback?.Explanation??(review?q.Explanation:"");IdealAnswer=answer.Feedback?.IdealAnswer??(review?q.Kind==QuestionKind.Closed?q.Options.Single(o=>o.IsCorrect).Text:q.IdealAnswer:"");
        var f=answer.Feedback;
        HasOpenFeedback=IsOpen&&f is not null;
        static string Items(IReadOnlyList<string>? items)=>items is {Count:>0}?string.Join("\n",items.Select(x=>"• "+x)):"Nessun elemento segnalato.";
        CorrectConceptsText=Items(f?.CorrectConcepts);MissingConceptsText=Items(f?.MissingConcepts);IncorrectConceptsText=Items(f?.Errors);
        SuggestionsText=f?.SemanticEvaluation is { } details?Items(details.Evaluation.Suggestions):f?.Hint??"";
        EvaluationDetails=f is null?"":$"{f.Score*100:0.#}/100"+(f.SemanticEvaluation is { } meta?$" · Confidenza dichiarata dal modello: {meta.Evaluation.Confidence:P0}"+(meta.Evaluation.Confidence<0.6?" · Evidenze incerte: confronta il feedback con le fonti.":""):"");
        Citations.Clear();
        if(HasFeedback) foreach(var c in q.Citations)
        {
            var reference=f?.SemanticEvaluation?.Evaluation.SourceReferences.SingleOrDefault(r=>r.ChunkId==c.ChunkId);
            if(f?.SemanticEvaluation is not null&&reference is null)continue;
            Citations.Add(new(c.SourceTitle+(string.IsNullOrWhiteSpace(c.Locator)?"":" · "+c.Locator),reference?.Quote??c.Quote));
        }
        AnswerError=answer.Error??"";HasAnswerError=AnswerError.Length>0;
        SaveStatus=answer.Status==EvaluationStatus.Evaluating?"Verifica interrotta o in corso":IsInProgress?"Risposte salvate":"Tentativo in sola lettura";
        ProviderMessage=session.IsMock?"Domande dimostrative · la valutazione aperta richiede Ollama configurato nelle Impostazioni.":"";
        UpdateOverview();CurrentPosition=index;
    }
    private void UpdateOverview()
    {
        if(session is null)return;
        var answered=session.Answers.Count(a=>a.IsAnswered);
        Progress=(double)answered/session.Questions.Count;
        for(var i=0;i<Numbers.Count;i++)
        {
            var answer=session.Answers.Single(a=>a.QuestionId==session.Questions[i].Id);
            var dirty=i==index&&IsOpen&&!string.IsNullOrWhiteSpace(OpenText)&&OpenText!=answer.Text;
            var state=answer.Feedback?.Outcome switch {AnswerOutcome.Correct=>("✓","corretta","Correct"),AnswerOutcome.Partial=>("◐","parzialmente corretta","Partial"),AnswerOutcome.Incorrect=>("✕","errata","Incorrect"),_=>
                answer.IsAnswered||dirty?("●","risposta non verificata","Answered"):("○","non risposta","Unanswered")};
            Numbers[i].StateLabel=state.Item1;Numbers[i].IsCurrent=i==index;Numbers[i].AccessibleTitle=$"Domanda {i+1}, {state.Item2}"+(i==index?", corrente":"");
            Numbers[i].VisualState=state.Item3;
        }
        var status=session.Attempt.Status;
        if(status==AttemptStatus.Completed)
        {
            var correct=session.Answers.Count(a=>a.Feedback?.Outcome==AnswerOutcome.Correct);
            var partial=session.Answers.Count(a=>a.Feedback?.Outcome==AnswerOutcome.Partial);
            var incorrect=session.Answers.Count(a=>a.Feedback?.Outcome==AnswerOutcome.Incorrect);
            Summary=$"Completato · {session.Attempt.Score:0.##}/{session.Questions.Count} punti ({100*session.Attempt.Score/session.Questions.Count:0.#}%) · Corrette {correct} · Parziali {partial} · Errate {incorrect} · Non risposte {session.Questions.Count-answered}";
        }
        else Summary=status==AttemptStatus.SubmittedPendingEvaluation?"Risultato provvisorio · alcune valutazioni sono in attesa. Premi Riprova valutazioni.":
            status==AttemptStatus.Abandoned?"Tentativo abbandonato · risposte conservate":$"{answered}/{session.Questions.Count} risposte salvate";
    }
    partial void OnOpenTextChanged(string value)
    {
        if(settingText||!loaded||!IsOpen||!IsEditable)return;
        SaveStatus="Modifiche da salvare…";UpdateOverview();
        debounce?.Cancel();debounce=new();_=AutosaveAsync(debounce.Token);
    }
    private async Task AutosaveAsync(CancellationToken token)
    {
        try {await Task.Delay(650,token);await SaveAsync(token);}
        catch(OperationCanceledException){}
        catch(Exception ex){SaveStatus="Salvataggio non riuscito";ErrorMessage=ex is StudyValidationException or QuizConflictException or StorageUnavailableException?ex.Message:"Risposta non salvata. Premi Riprova salvataggio prima di uscire.";}
    }
    private async Task SaveAsync(CancellationToken ct=default)
    {
        await saveGate.WaitAsync(ct);
        try
        {
            if(session is null||!IsInProgress)return;
            if(IsOpen&&OpenText!=Answer.Text)
            {
                var text=OpenText;var answer=await attempts.SaveOpenAsync(projectId,attemptId,Current.Id,text,Answer.Revision,ct);SetAnswer(answer);
                SaveStatus=text==OpenText?"Risposta salvata":"Modifiche da salvare…";UpdateOverview();
            }
        }
        finally{saveGate.Release();}
    }
    public async Task FlushAsync()
    {
        debounce?.Cancel();await SaveAsync();
        if(session is not null)await attempts.CheckpointAsync(projectId,attemptId,index,baseActiveMs+active.ElapsedMilliseconds);
    }
    [RelayCommand] private Task RetrySaveAsync()=>RunAsync(FlushAsync);
    private Task SelectAsync(int position)=>RunAsync(async()=>
    {
        if(session is null||position<0||position>=session.Questions.Count)return;
        await FlushAsync();index=position;Present();await attempts.CheckpointAsync(projectId,attemptId,index,baseActiveMs+active.ElapsedMilliseconds);
    });
    [RelayCommand] private Task PreviousAsync()=>SelectAsync(index-1);
    [RelayCommand] private Task NextAsync()=>SelectAsync(index+1);
    private Task ChooseAsync(Guid option)=>RunAsync(async()=>
    {
        if(!IsEditable)return;
        var answer=await attempts.AnswerClosedAsync(projectId,attemptId,Current.Id,option,Answer.Revision);SetAnswer(answer);Present();
    });
    [RelayCommand] private Task VerifyAsync(CancellationToken cancellationToken)=>RunAsync(async()=>
    {
        if(!CanVerify)return;await FlushAsync();evaluating=true;IsEvaluationRunning=true;active.Stop();IsEditable=false;CanVerify=false;
        try{await attempts.EvaluateAsync(projectId,attemptId,Current.Id,cancellationToken);}
        finally{evaluating=false;IsEvaluationRunning=false;session=await attempts.GetAsync(projectId,attemptId);Present();ResumeTimer();}
    });
    [RelayCommand] private Task FinishAsync(CancellationToken cancellationToken)=>RunAsync(async()=>
    {
        if(!IsInProgress)return;await FlushAsync();
        if(!await dialogs.ConfirmFinishAsync(session!.Answers.Count(a=>!a.IsAnswered)))return;
        active.Stop();await FlushAsync();evaluating=true;IsEvaluationRunning=true;
        try{session=await attempts.FinishAsync(projectId,attemptId,cancellationToken);}
        finally{evaluating=false;IsEvaluationRunning=false;session=await attempts.GetAsync(projectId,attemptId);Present();ResumeTimer();}
        if(IsCompleted)await OpenResultsCoreAsync();
    });
    [RelayCommand] private Task RetryEvaluationAsync(CancellationToken cancellationToken)=>RunAsync(async()=>
    {
        if(!IsPending)return;IsEvaluationRunning=true;
        try{session=await attempts.FinishAsync(projectId,attemptId,cancellationToken);}
        finally{IsEvaluationRunning=false;session=await attempts.GetAsync(projectId,attemptId);Present();}
        if(IsCompleted)await OpenResultsCoreAsync();
    });
    [RelayCommand] private Task ShowResultsAsync()=>RunAsync(OpenResultsCoreAsync);
    private async Task OpenResultsCoreAsync()
    {
        if(!IsCompleted)return;
        leaving=true;try{await navigation.OpenQuizResultAsync(projectId,attemptId);}finally{leaving=false;}
    }
    [RelayCommand] private Task AbandonAsync()=>RunAsync(async()=>
    {
        if(!IsInProgress||!await dialogs.ConfirmAbandonAsync())return;
        await FlushAsync();active.Stop();await attempts.AbandonAsync(projectId,attemptId);session=await attempts.GetAsync(projectId,attemptId);Present();
    });
    [RelayCommand] private Task ExitAsync()=>RunAsync(async()=>{await FlushAsync();active.Stop();leaving=true;try{await navigation.GoBackAsync();}finally{leaving=false;}});
    public async Task<bool> PrepareToLeaveAsync()
    {
        if(leaving)return true;
        if(IsBusy)return false;
        var success=false;await RunAsync(async()=>{await FlushAsync();active.Stop();success=true;});return success;
    }
    public async Task PauseAsync()
    {
        foreground=false;active.Stop();await RunAsync(FlushAsync);
    }
    public void Activate(){foreground=true;ResumeTimer();}
    public void ResumeTimer(){if(loaded&&IsInProgress&&foreground&&!evaluating)active.Start();}
    public Task TickAsync()=>IsBusy?Task.CompletedTask:RunCheckpointAsync();
    private async Task RunCheckpointAsync()
    {
        try { if(loaded&&IsInProgress)await attempts.CheckpointAsync(projectId,attemptId,index,baseActiveMs+active.ElapsedMilliseconds); }
        catch(Exception){SaveStatus="Checkpoint non riuscito; le risposte già salvate restano disponibili.";}
    }
}



