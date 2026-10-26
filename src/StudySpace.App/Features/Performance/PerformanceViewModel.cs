using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.Application.Performance;
using StudySpace.Application.Projects;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
namespace StudySpace.App.Features.Performance;

public static class PerformanceText
{
    public static string Mode(QuizMode mode)=>mode switch{QuizMode.Closed=>"Risposta chiusa",QuizMode.Open=>"Risposta aperta",_=>"Misto"};
    public static string Difficulty(Difficulty value,bool mixed)=>mixed?"Mista":value switch{StudySpace.Domain.Common.Difficulty.Easy=>"Facile",StudySpace.Domain.Common.Difficulty.Medium=>"Media",_=>"Difficile"};
    public static string Duration(long ms)
    {
        var seconds=Math.Max(0,ms)/1000;
        return seconds>=3600?$"{seconds/3600} h {(seconds/60)%60:00} min {seconds%60:00} s":$"{seconds/60} min {seconds%60:00} s";
    }
    public static string Outcome(AnswerFeedback? feedback)=>feedback?.Outcome switch{AnswerOutcome.Correct=>"Corretto",AnswerOutcome.Partial=>"Parzialmente corretto",AnswerOutcome.Incorrect=>"Da rivedere",_=>"Non risposta"};
    public static string Items(IReadOnlyList<string> items)=>items.Count>0?string.Join("\n",items.Select(s=>"• "+s)):"Nessun elemento segnalato.";
}
public sealed class EvidenceLinkViewModel
{
    public string Title {get;}
    public IAsyncRelayCommand OpenCommand {get;}
    public EvidenceLinkViewModel(PerformanceReference reference,Func<PerformanceReference,Task> open)
    {Title=reference.Citation.SourceTitle+(string.IsNullOrWhiteSpace(reference.Citation.Locator)?"":" · "+reference.Citation.Locator);OpenCommand=new AsyncRelayCommand(()=>open(reference));}
}
public sealed class PerformanceGroupViewModel
{
    public string Name {get;}
    public string Kind {get;}
    public string Details {get;}
    public string Suggestions {get;}
    public IReadOnlyList<EvidenceLinkViewModel> References {get;}
    public PerformanceGroupViewModel(PerformanceGroup group,Func<PerformanceReference,Task> open,bool improving=false)
    {
        Name=group.Name;Kind=group.Kind switch{PerformanceGroupKind.Topic=>"Argomento",PerformanceGroupKind.Tag=>"Tag",PerformanceGroupKind.Source=>"Fonte",_=>"Concetto"};
        Details=improving?$"In miglioramento · +{100*group.Improvement:0.#} punti percentuali rispetto alle risposte precedenti":
            $"Da ripassare · Ultime risposte: {group.RecentCount} · Errori: {group.RecentErrors} · Parziali: {group.RecentPartial}";
        Details+=$"\nDomande valutate nello storico: {group.EvidenceCount}"+(group.EvidenceCount<3?" · Evidenze limitate":"")+
            (group.Unanswered>0?$" · Non risposte: {group.Unanswered}, escluse dall'indice":"");
        Suggestions=PerformanceText.Items(group.Suggestions);References=group.References.Select(r=>new EvidenceLinkViewModel(r,open)).ToArray();
    }
}
public sealed class HistoryItemViewModel
{
    public Guid Id {get;}
    public string Title {get;}
    public string Details {get;}
    public string Score {get;}
    public double Progress {get;}
    public bool HasScore {get;}
    public IAsyncRelayCommand OpenCommand {get;}
    public HistoryItemViewModel(HistoryAttempt item,Func<Task> open)
    {
        Id=item.Id;Title=$"{item.StartedAtUtc.ToLocalTime():dd MMM yyyy · HH:mm}";
        var status=item.Status switch{AttemptStatus.Completed=>"Completato",AttemptStatus.Abandoned=>"Abbandonato",AttemptStatus.InProgress=>"In corso",_=>"Valutazioni in attesa"};
        Details=$"{PerformanceText.Mode(item.Mode)} · {item.Count} domande · {PerformanceText.Difficulty(item.Difficulty,item.MixedDifficulty)}\nDurata attiva: {PerformanceText.Duration(item.ActiveDurationMs)} · {status}"+(item.IsMock?" · Domande demo":"");
        HasScore=item.Status==AttemptStatus.Completed&&item.Score.HasValue;
        Score=HasScore?$"{item.Score:0.##}/{item.Count} punti · {100*item.Score/item.Count:0.#}%":"Punteggio finale non disponibile";
        Progress=HasScore?item.Score!.Value/item.Count:0;OpenCommand=new AsyncRelayCommand(open);
    }
}
public sealed class ResultQuestionViewModel
{
    public string Heading {get;}
    public string Prompt {get;}
    public string MyAnswer {get;}
    public string IdealAnswer {get;}
    public string Explanation {get;}
    public string Evaluation {get;}
    public string Concepts {get;}
    public IReadOnlyList<EvidenceLinkViewModel> References {get;}
    public ResultQuestionViewModel(QuizSession session,QuizQuestionSnapshot q,Func<PerformanceReference,Task> open)
    {
        var answer=session.Answers.Single(a=>a.QuestionId==q.Id);var f=answer.Feedback;
        Heading=$"Domanda {q.Position+1} · {PerformanceText.Outcome(f)}";Prompt=q.Prompt;
        MyAnswer=!answer.IsAnswered?"Nessuna risposta":answer.SelectedOptionId.HasValue?q.Options.Single(o=>o.Id==answer.SelectedOptionId).Text:answer.Text;
        IdealAnswer=f?.IdealAnswer??(q.Kind==QuestionKind.Closed?q.Options.Single(o=>o.IsCorrect).Text:q.IdealAnswer);
        Explanation=q.Explanation;
        Evaluation=f is null?"Non risposta · 0/100. Non è una prova di difficoltà sul concetto.":$"{f.Score*100:0.#}/100 · {f.Explanation}"+
            (f.IsMock?"\nValutazione dimostrativa salvata.":"")+(f.SemanticEvaluation is { } meta?$"\nConfidenza dichiarata: {meta.Evaluation.Confidence:P0}":"");
        Concepts=f is null?"":"Cosa hai spiegato bene\n"+PerformanceText.Items(f.CorrectConcepts)+"\n\nCosa manca\n"+PerformanceText.Items(f.MissingConcepts)+
            "\n\nCosa è errato\n"+PerformanceText.Items(f.Errors)+"\n\nCome migliorare\n"+(f.SemanticEvaluation is { } details?PerformanceText.Items(details.Evaluation.Suggestions):f.Hint);
        References=q.Citations.Select(c=>new EvidenceLinkViewModel(new(session.Attempt.Id,q.Id,c),open)).ToArray();
    }
}
public partial class QuizResultsViewModel(StudyPerformanceService performance,INavigationService navigation,ILogger<QuizResultsViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId,attemptId;private QuizResult? result;private int page;
    public ObservableCollection<ResultQuestionViewModel> Questions {get;}=[];
    public ObservableCollection<PerformanceGroupViewModel> Difficulties {get;}=[];
    [ObservableProperty] public partial bool IsReady {get;set;}
    [ObservableProperty] public partial string Score {get;set;}="";
    [ObservableProperty] public partial string Details {get;set;}="";
    [ObservableProperty] public partial string Counts {get;set;}="";
    [ObservableProperty] public partial string PageLabel {get;set;}="";
    [ObservableProperty] public partial bool CanPrevious {get;set;}
    [ObservableProperty] public partial bool CanNext {get;set;}
    [ObservableProperty] public partial bool HasDifficulties {get;set;}
    public void Configure(string project,string attempt){Guid.TryParse(project,out projectId);Guid.TryParse(attempt,out attemptId);}
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        IsReady=false;result=await performance.ResultAsync(projectId,attemptId);var s=result.Session;
        Score=$"{s.Attempt.Score:0.##}/{s.Questions.Count} punti · {result.Percentage:0.#}%";
        Details=$"{s.Attempt.StartedAtUtc.ToLocalTime():dd MMM yyyy · HH:mm} · {PerformanceText.Mode(s.Attempt.Mode)} · {PerformanceText.Difficulty(s.Attempt.Difficulty,s.MixedDifficulty)}\nDurata attiva: {PerformanceText.Duration(s.Attempt.ActiveDurationMs)} · {s.Questions.Count} domande"+(s.IsMock?" · Domande dimostrative":"");
        Counts=$"Corrette: {result.Correct}   Parziali: {result.Partial}   Errate: {result.Incorrect}   Non risposte: {result.Unanswered}";
        Difficulties.Clear();foreach(var group in Highlights(result.Difficulties))Difficulties.Add(new(group,OpenEvidenceAsync));HasDifficulties=Difficulties.Count>0;
        page=Math.Clamp(page,0,(s.Questions.Count-1)/5);PresentQuestions();IsReady=true;
    });
    internal static IEnumerable<PerformanceGroup> Highlights(IReadOnlyList<PerformanceGroup> groups)=>Enum.GetValues<PerformanceGroupKind>().SelectMany(kind=>groups.Where(g=>g.Kind==kind).Take(3));
    private Task OpenEvidenceAsync(PerformanceReference r)=>RunAsync(()=>navigation.OpenQuizEvidenceAsync(projectId,r.AttemptId,r.QuestionId,r.Citation.ChunkId));
    private void PresentQuestions()
    {
        if(result is null)return;Questions.Clear();
        foreach(var q in result.Session.Questions.Skip(page*5).Take(5))Questions.Add(new(result.Session,q,OpenEvidenceAsync));
        PageLabel=$"Domande {page*5+1}–{Math.Min((page+1)*5,result.Session.Questions.Count)} di {result.Session.Questions.Count}";
        CanPrevious=page>0;CanNext=(page+1)*5<result.Session.Questions.Count;
    }
    [RelayCommand] private void Next(){if(CanNext){page++;PresentQuestions();}}
    [RelayCommand] private void Previous(){if(CanPrevious){page--;PresentQuestions();}}
    [RelayCommand] private Task HistoryAsync()=>RunAsync(()=>navigation.OpenProjectSectionAsync(projectId,"history"));
    [RelayCommand] private Task StatisticsAsync()=>RunAsync(()=>navigation.OpenProjectSectionAsync(projectId,"statistics"));
}
public partial class QuizHistoryViewModel(StudyPerformanceService performance,ProjectService projects,INavigationService navigation,ILogger<QuizHistoryViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId;private int offset;
    public ObservableCollection<HistoryItemViewModel> Items {get;}=[];
    [ObservableProperty] public partial string ProjectName {get;set;}="";
    [ObservableProperty] public partial bool IsEmpty {get;set;}
    [ObservableProperty] public partial bool HasMore {get;set;}
    public void Configure(string id)=>Guid.TryParse(id,out projectId);
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>{IsEmpty=false;ProjectName=(await projects.GetAsync(projectId)).Name;Items.Clear();offset=0;await AppendAsync();});
    private async Task AppendAsync()
    {
        var items=await performance.HistoryAsync(projectId,offset);offset+=items.Count;
        foreach(var item in items)Items.Add(new(item,()=>RunAsync(()=>item.Status==AttemptStatus.Completed?navigation.OpenQuizResultAsync(projectId,item.Id):navigation.OpenQuizAttemptAsync(projectId,item.Id))));
        HasMore=items.Count==30;IsEmpty=Items.Count==0;
    }
    [RelayCommand] private Task MoreAsync()=>RunAsync(AppendAsync);
    [RelayCommand] private Task NewQuizAsync()=>RunAsync(()=>navigation.NewQuizAsync(projectId));
}
public partial class ProjectStatisticsViewModel(StudyPerformanceService performance,ProjectService projects,INavigationService navigation,ILogger<ProjectStatisticsViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId;
    public ObservableCollection<HistoryItemViewModel> Recent {get;}=[];
    public ObservableCollection<PerformanceGroupViewModel> Difficulties {get;}=[];
    public ObservableCollection<PerformanceGroupViewModel> Improving {get;}=[];
    [ObservableProperty] public partial string ProjectName {get;set;}="";
    [ObservableProperty] public partial string Summary {get;set;}="";
    [ObservableProperty] public partial bool IsReady {get;set;}
    [ObservableProperty] public partial bool HasResults {get;set;}
    [ObservableProperty] public partial bool HasDifficulties {get;set;}
    [ObservableProperty] public partial bool HasImproving {get;set;}
    [ObservableProperty] public partial string DemoNotice {get;set;}="";
    public void Configure(string id)=>Guid.TryParse(id,out projectId);
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        IsReady=false;ProjectName=(await projects.GetAsync(projectId)).Name;var stats=await performance.StatisticsAsync(projectId);
        Summary=$"Quiz completati: {stats.CompletedCount} · Percentuale media: "+(stats.AveragePercentage.HasValue?$"{stats.AveragePercentage:0.#}%":"—");
        DemoNotice=stats.DemonstrationCount>0?$"Include {stats.DemonstrationCount} quiz con domande dimostrative. Quiz e difficoltà diversi offrono indicazioni da leggere nel loro contesto.":"Quiz e difficoltà diversi non sono automaticamente confrontabili.";
        Recent.Clear();foreach(var item in stats.Recent)Recent.Add(new(item,()=>RunAsync(()=>navigation.OpenQuizResultAsync(projectId,item.Id))));
        Difficulties.Clear();foreach(var group in QuizResultsViewModel.Highlights(stats.Difficulties))Difficulties.Add(new(group,OpenEvidenceAsync));
        Improving.Clear();foreach(var group in QuizResultsViewModel.Highlights(stats.Improving))Improving.Add(new(group,OpenEvidenceAsync,true));
        HasResults=stats.CompletedCount>0;HasDifficulties=Difficulties.Count>0;HasImproving=Improving.Count>0;IsReady=true;
    });
    private Task OpenEvidenceAsync(PerformanceReference r)=>RunAsync(()=>navigation.OpenQuizEvidenceAsync(projectId,r.AttemptId,r.QuestionId,r.Citation.ChunkId));
    [RelayCommand] private Task HistoryAsync()=>RunAsync(()=>navigation.OpenProjectSectionAsync(projectId,"history"));
}
public partial class QuizEvidenceViewModel(QuizAttemptService attempts,ILogger<QuizEvidenceViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId,attemptId,questionId,chunkId;
    [ObservableProperty] public partial string Title {get;set;}="";
    [ObservableProperty] public partial string Locator {get;set;}="";
    [ObservableProperty] public partial string Quote {get;set;}="";
    [ObservableProperty] public partial bool IsReady {get;set;}
    public void Configure(string p,string a,string q,string c){Guid.TryParse(p,out projectId);Guid.TryParse(a,out attemptId);Guid.TryParse(q,out questionId);Guid.TryParse(c,out chunkId);}
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        IsReady=false;var session=await attempts.GetAsync(projectId,attemptId);
        var question=session.Questions.SingleOrDefault(q=>q.Id==questionId)??throw new QuizNotFoundException();
        var answer=session.Answers.Single(a=>a.QuestionId==questionId);
        if(session.Attempt.Status!=AttemptStatus.Completed&&answer.Status!=EvaluationStatus.Evaluated)throw new QuizNotFoundException();
        var citation=question.Citations.SingleOrDefault(c=>c.ChunkId==chunkId)??throw new QuizNotFoundException();
        Title=citation.SourceTitle;Locator=citation.Locator??"Estratto della fonte";Quote=citation.Quote;IsReady=true;
    });
}
