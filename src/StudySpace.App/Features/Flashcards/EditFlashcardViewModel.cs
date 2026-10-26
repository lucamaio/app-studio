using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Domain.Flashcards;
namespace StudySpace.App.Features.Flashcards;

public partial class EditFlashcardViewModel(FlashcardService flashcards,ProjectService projects,StudyMaterialPickerViewModel materials,
    INavigationService navigation,ILogger<EditFlashcardViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId,cardId; private bool editing,invalidRoute,loaded,subscribed; private FlashCard? original;
    public StudyMaterialPickerViewModel Materials { get; }=materials;
    public IReadOnlyList<DifficultyChoice> Difficulties=>DifficultyChoice.All;
    public ObservableCollection<MaterialSourceChoice> SourceChoices { get; }=[];
    [ObservableProperty] public partial string ProjectName { get; set; }="";
    [ObservableProperty] public partial string Front { get; set; }="";
    [ObservableProperty] public partial string Back { get; set; }="";
    [ObservableProperty] public partial string Explanation { get; set; }="";
    [ObservableProperty] public partial string TagsText { get; set; }="";
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty] public partial DifficultyChoice? SelectedDifficulty { get; set; }=DifficultyChoice.All[0];
    [ObservableProperty] public partial MaterialSourceChoice? PrimarySource { get; set; }
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SaveCaption))] public partial bool IsSaved { get; set; }
    public string Heading=>editing ? "Modifica flashcard" : "Una domanda, un concetto";
    public string SaveCaption=>IsSaved ? "Torna alle flashcard" : editing ? "Salva modifiche" : "Crea flashcard";
    public void Configure(string project,string? card)
    {
        projectId=Guid.TryParse(project,out var p) ? p : Guid.Empty; editing=!string.IsNullOrEmpty(card);
        invalidRoute=editing && !Guid.TryParse(card,out cardId); OnPropertyChanged(nameof(Heading)); OnPropertyChanged(nameof(SaveCaption));
    }
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        if(loaded) return; IsReady=false;
        if(invalidRoute) throw new FlashcardNotFoundException();
        ProjectName=(await projects.GetAsync(projectId)).Name;
        Materials.Configure(projectId); await Materials.LoadCommand.ExecuteAsync(null);
        if(Materials.HasError) throw new StudyValidationException(Materials.ErrorMessage!);
        foreach(var choice in Materials.Sources) if(!SourceChoices.Any(s=>s.Id==choice.Id)) SourceChoices.Add(choice);
        if(!subscribed) Materials.Sources.CollectionChanged+=(_,args)=>
        {
            if(args.NewItems is null) return;
            foreach(var choice in args.NewItems.OfType<MaterialSourceChoice>())
                if(!SourceChoices.Any(s=>s.Id==choice.Id)) SourceChoices.Add(choice);
        };
        subscribed=true;
        if(editing)
        {
            original=await flashcards.GetAsync(projectId,cardId);
            Front=original.Front; Back=original.Back; Explanation=original.Explanation ?? ""; TagsText=string.Join(", ",original.Tags);
            IsFavorite=original.IsFavorite; SelectedDifficulty=Difficulties.First(d=>d.Value==original.Difficulty);
            Materials.SetInitial(original.Citations);
            if(original.SourceId.HasValue)
            {
                PrimarySource=SourceChoices.FirstOrDefault(s=>s.Id==original.SourceId.Value);
                if(PrimarySource is null)
                {
                    PrimarySource=new(original.SourceId.Value,(original.SourceTitle ?? "Fonte collegata")+(original.IsSourceArchived ? " (archiviata)" : ""),original.Citations.Count);
                    SourceChoices.Insert(0,PrimarySource);
                }
            }
        }
        loaded=true; IsReady=true;
    });
    [RelayCommand] private void ClearSource()=>PrimarySource=null;
    [RelayCommand] private Task SaveAsync()=>RunAsync(async()=>
    {
        if(!IsReady) return;
        if(!IsSaved)
        {
            var draft=new FlashcardDraft(Front,Back,Explanation,SelectedDifficulty?.Value,TagsText.Split(','),IsFavorite,PrimarySource?.Id,Materials.References);
            if(editing) await flashcards.UpdateAsync(projectId,cardId,original!.Revision,draft);
            else await flashcards.CreateAsync(projectId,draft);
            IsSaved=true;
        }
        await navigation.GoBackAsync();
    });
}
