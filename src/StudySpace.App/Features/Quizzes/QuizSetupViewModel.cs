using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Features.Flashcards;
using StudySpace.App.Navigation;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
namespace StudySpace.App.Features.Quizzes;

public sealed record QuizModeChoice(QuizMode Value,string Title);
public sealed record QuizCountChoice(int? Value,string Title);
public partial class QuizSetupViewModel(QuizService quizzes,ProjectService projects,StudyMaterialPickerViewModel materials,
    INavigationService navigation,ILogger<QuizSetupViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId; private Guid operationId=Guid.NewGuid();private Guid? started;private bool loaded;
    public StudyMaterialPickerViewModel Materials {get;}=materials;
    public IReadOnlyList<QuizModeChoice> Modes {get;}=[new(QuizMode.Closed,"Risposta chiusa"),new(QuizMode.Open,"Risposta aperta"),new(QuizMode.Mixed,"Misto")];
    public IReadOnlyList<QuizCountChoice> Counts {get;}=[new(5,"5"),new(10,"10"),new(15,"15"),new(20,"20"),new(null,"Personalizzato")];
    public IReadOnlyList<DifficultyChoice> Difficulties {get;}=[new("Mista",null),new("Facile",Difficulty.Easy),new("Media",Difficulty.Medium),new("Difficile",Difficulty.Hard)];
    [ObservableProperty] public partial QuizModeChoice? SelectedMode {get;set;}
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCustomCount))] public partial QuizCountChoice? SelectedCount {get;set;}
    [ObservableProperty] public partial DifficultyChoice? SelectedDifficulty {get;set;}
    [ObservableProperty] public partial string CustomCount {get;set;}="5";
    [ObservableProperty] public partial string ProjectName {get;set;}="";
    [ObservableProperty] public partial bool IsReady {get;set;}
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(GenerateCaption))] public partial bool IsStarted {get;set;}
    public bool IsCustomCount=>SelectedCount?.Value is null;
    public string GenerateCaption=>IsStarted?"Apri il quiz salvato":quizzes.Provider.IsMock?"Crea quiz dimostrativo":"Genera quiz";
    public string ProviderMessage=>quizzes.Provider.IsMock?"Generazione dimostrativa locale: domande di completamento ed estratti. La valutazione semantica delle risposte aperte richiede Ollama, configurabile nelle Impostazioni senza API key.":"Provider: "+quizzes.Provider.DisplayName;
    public void Configure(string id)=>projectId=Guid.TryParse(id,out var p)?p:Guid.Empty;
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        if(loaded)return; ProjectName=(await projects.GetAsync(projectId)).Name;
        SelectedMode??=Modes[0]; SelectedCount??=Counts[0]; SelectedDifficulty??=Difficulties[0];
        Materials.Configure(projectId);await Materials.LoadCommand.ExecuteAsync(null);
        if(Materials.HasError)throw new StudyValidationException(Materials.ErrorMessage!);
        loaded=true;IsReady=true;
    });
    [RelayCommand(IncludeCancelCommand=true)] private Task GenerateAsync(CancellationToken ct)=>RunAsync(async()=>
    {
        if(!IsReady)return;
        if(started is null)
        {
            var count=SelectedCount?.Value;
            if(count is null){if(!int.TryParse(CustomCount,out var custom))throw new StudyValidationException("Inserisci un numero da 1 a 50.");count=custom;}
            started=await quizzes.GenerateAsync(projectId,operationId,new(SelectedMode?.Value??QuizMode.Closed,count.Value,SelectedDifficulty?.Value,
                new(SourceIds:Materials.Selected.Select(r=>r.Citation.SourceId).Distinct().ToArray())),Materials.SelectedChunkIds,ct);
            IsStarted=true;
        }
        await navigation.OpenQuizAttemptAsync(projectId,started.Value);
    });
}
public partial class QuizAttemptItemViewModel:ObservableObject
{
    public Guid Id {get;}
    public string Title {get;}
    public string Details {get;}
    public IAsyncRelayCommand OpenCommand {get;}
    public QuizAttemptItemViewModel(QuizAttemptSummary item,Func<Task> open)
    {
        Id=item.Id;Title=item.Title+(item.IsMock?" · demo":"");
        var state=item.Status switch {AttemptStatus.InProgress=>"In corso",AttemptStatus.Completed=>"Completato",AttemptStatus.Abandoned=>"Abbandonato",_=>"Valutazioni in attesa"};
        Details=$"{item.StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} · {state} · {item.AnsweredCount}/{item.Count} risposte"+
            (item.Score.HasValue?$" · {item.Score:0.##}/{item.Count} punti":"");
        OpenCommand=new AsyncRelayCommand(open);
    }
}
public partial class QuizzesViewModel(QuizService quizzes,ProjectService projects,INavigationService navigation,ILogger<QuizzesViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId;private int offset;
    public ObservableCollection<QuizAttemptItemViewModel> Items {get;}=[];
    [ObservableProperty] public partial string ProjectName {get;set;}="";
    [ObservableProperty] public partial bool HasMore {get;set;}
    [ObservableProperty] public partial bool IsEmpty {get;set;}
    public void Configure(string id)=>projectId=Guid.TryParse(id,out var p)?p:Guid.Empty;
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        IsEmpty=false;ProjectName=(await projects.GetAsync(projectId)).Name;Items.Clear();offset=0;await AppendAsync();
    });
    private async Task AppendAsync()
    {
        var page=await quizzes.ListAsync(projectId,offset);offset+=page.Count;
        foreach(var item in page)Items.Add(new(item,()=>RunAsync(()=>item.Status==AttemptStatus.Completed?navigation.OpenQuizResultAsync(projectId,item.Id):navigation.OpenQuizAttemptAsync(projectId,item.Id))));
        HasMore=page.Count==30;IsEmpty=Items.Count==0;
    }
    [RelayCommand] private Task MoreAsync()=>RunAsync(AppendAsync);
    [RelayCommand] private Task NewQuizAsync()=>RunAsync(()=>navigation.NewQuizAsync(projectId));
}


