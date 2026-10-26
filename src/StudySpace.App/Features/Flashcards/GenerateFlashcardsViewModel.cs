using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
namespace StudySpace.App.Features.Flashcards;

public partial class FlashcardPreviewItemViewModel:ObservableObject
{
    private readonly ProposedFlashcard original;
    private readonly Action selectionChanged;
    [ObservableProperty] public partial bool IsSelected { get; set; }=true;
    [ObservableProperty] public partial string Front { get; set; }="";
    [ObservableProperty] public partial string Back { get; set; }="";
    [ObservableProperty] public partial string Explanation { get; set; }="";
    [ObservableProperty] public partial string TagsText { get; set; }="";
    [ObservableProperty] public partial DifficultyChoice? SelectedDifficulty { get; set; }
    public IReadOnlyList<DifficultyChoice> Difficulties=>DifficultyChoice.All;
    public IReadOnlyList<CitationPresentation> Citations { get; }
    public IRelayCommand RemoveCommand { get; }
    public FlashcardPreviewItemViewModel(ProposedFlashcard proposed,IReadOnlyDictionary<Guid,SelectedReferenceViewModel> references,
        Action<FlashcardPreviewItemViewModel> remove,Action selectionChanged)
    {
        original=proposed; this.selectionChanged=selectionChanged; Front=proposed.Draft.Front; Back=proposed.Draft.Back;
        Explanation=proposed.Draft.Explanation ?? ""; TagsText=string.Join(", ",proposed.Draft.Tags ?? []);
        SelectedDifficulty=Difficulties.First(c=>c.Value==proposed.Draft.Difficulty);
        Citations=proposed.Draft.References!.Select(r=>new CitationPresentation(references.TryGetValue(r.ChunkId,out var reference) ? reference.Title : "Brano selezionato",r.Quote)).ToArray();
        RemoveCommand=new RelayCommand(()=>remove(this));
    }
    partial void OnIsSelectedChanged(bool value)=>selectionChanged();
    public ProposedFlashcard ToDraft()=>original with { Draft=original.Draft with { Front=Front,Back=Back,Explanation=Explanation,
        Tags=TagsText.Split(','),Difficulty=SelectedDifficulty?.Value } };
}
public partial class GenerateFlashcardsViewModel(FlashcardGenerationService generation,ProjectService projects,StudyMaterialPickerViewModel materials,
    INavigationService navigation,ILogger<GenerateFlashcardsViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId; private FlashcardDraftBatch? batch; private bool loaded;
    public StudyMaterialPickerViewModel Materials { get; }=materials;
    public IReadOnlyList<DifficultyChoice> Difficulties=>DifficultyChoice.All;
    public ObservableCollection<FlashcardPreviewItemViewModel> Preview { get; }=[];
    public string ProviderLabel=>generation.Provider.IsMock ? "Provider di prova · nessuna API key" : generation.Provider.DisplayName;
    public string ProviderMessage=>generation.Provider.IsMock ?
        "Dimostrazione locale: le schede sono estratti automatici delle fonti, non risultati di un modello AI. Puoi modificarle prima di importarle." :
        "Le schede verranno preparate dal provider configurato utilizzando soltanto i brani selezionati.";
    public string GenerateCaption=>generation.Provider.IsMock ? "Crea anteprima dimostrativa" : "Genera anteprima";
    [ObservableProperty] public partial string ProjectName { get; set; }="";
    [ObservableProperty] public partial string CountText { get; set; }="6";
    [ObservableProperty] public partial DifficultyChoice? SelectedDifficulty { get; set; }=DifficultyChoice.All[0];
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSelecting))] public partial bool IsPreview { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ImportCaption))] public partial bool IsImported { get; set; }
    [ObservableProperty] public partial string Messages { get; set; }="";
    [ObservableProperty] public partial string SelectionLabel { get; set; }="";
    public bool IsSelecting=>!IsPreview;
    public string ImportCaption=>IsImported ? "Torna alle flashcard" : "Importa le schede selezionate";
    public void Configure(string rawId)=>projectId=Guid.TryParse(rawId,out var id) ? id : Guid.Empty;
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        if(loaded) return; IsReady=false; ProjectName=(await projects.GetAsync(projectId)).Name;
        Materials.Configure(projectId); await Materials.LoadCommand.ExecuteAsync(null);
        if(Materials.HasError) throw new StudyValidationException(Materials.ErrorMessage!);
        loaded=true; IsReady=true;
    });
    [RelayCommand(IncludeCancelCommand=true)] private Task GenerateAsync(CancellationToken ct)=>RunAsync(async()=>
    {
        if(!IsReady || IsImported) return;
        if(!int.TryParse(CountText,out var count)) throw new StudyValidationException("Inserisci un numero intero da 1 a 30.");
        var generated=await generation.GenerateAsync(projectId,Materials.SelectedChunkIds,count,SelectedDifficulty?.Value,ct);
        batch=generated; Preview.Clear();
        var references=Materials.Selected.ToDictionary(r=>r.Citation.ChunkId);
        foreach(var draft in batch.Drafts) Preview.Add(new(draft,references,Remove,UpdateSelection));
        Messages=string.Join("\n",batch.Messages); IsPreview=true; UpdateSelection();
    });
    private void Remove(FlashcardPreviewItemViewModel item) { if(IsBusy || IsImported) return; Preview.Remove(item); UpdateSelection(); }
    private void UpdateSelection()=>SelectionLabel=$"{Preview.Count(p=>p.IsSelected)} di {Preview.Count} schede selezionate";
    [RelayCommand] private void SelectAll()
    { if(IsBusy || IsImported) return; foreach(var card in Preview) card.IsSelected=true; UpdateSelection(); }
    [RelayCommand] private void DeselectAll()
    { if(IsBusy || IsImported) return; foreach(var card in Preview) card.IsSelected=false; UpdateSelection(); }
    [RelayCommand] private void BackToMaterials() { if(IsBusy || IsImported) return; IsPreview=false; ErrorMessage=null; }
    [RelayCommand] private Task ImportAsync()=>RunAsync(async()=>
    {
        if(!IsPreview || batch is null) return;
        if(!IsImported)
        {
            var count=await generation.ImportAsync(batch,Preview.Where(c=>c.IsSelected).Select(c=>c.ToDraft()).ToArray());
            IsImported=true; Messages=$"{count} flashcard importate nel progetto.";
        }
        await navigation.GoBackAsync();
    });
}
