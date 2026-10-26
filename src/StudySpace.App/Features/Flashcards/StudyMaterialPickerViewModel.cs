using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Domain.Sources;
namespace StudySpace.App.Features.Flashcards;

public sealed record MaterialSourceChoice(Guid Id,string Title,int ChunkCount)
{ public string DisplayTitle=>$"{Title} · {ChunkCount} parti"; }
public partial class ChunkChoiceViewModel(StudyChunk context,bool selected,Action<ChunkChoiceViewModel,bool> changed):ObservableObject
{
    public StudyChunk Context { get; }=context;
    [ObservableProperty] public partial bool IsSelected { get; set; }=selected;
    partial void OnIsSelectedChanged(bool value)=>changed(this,value);
    public string Title=>$"Parte {Context.Chunk.Ordinal+1}";
    public string Locator=>FlashcardService.Locator(Context.Chunk) ?? "Testo della fonte";
    public string Preview=>Context.Chunk.Text.Length>260 ? Context.Chunk.Text[..260]+"…" : Context.Chunk.Text;
}
public sealed class SelectedReferenceViewModel
{
    public SourceCitation Citation { get; }
    public string Title=>Citation.SourceTitle+(string.IsNullOrWhiteSpace(Citation.Locator) ? "" : " · "+Citation.Locator);
    public string Preview=>Citation.Quote.Length>180 ? Citation.Quote[..180]+"…" : Citation.Quote;
    public IRelayCommand RemoveCommand { get; }
    public SelectedReferenceViewModel(SourceCitation citation,Action<Guid> remove)
    { Citation=citation; RemoveCommand=new RelayCommand(()=>remove(citation.ChunkId)); }
}
public partial class StudyMaterialPickerViewModel(FlashcardService flashcards,ILogger<StudyMaterialPickerViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId; private int sourceOffset,chunkOffset;
    private readonly Dictionary<Guid,SourceCitation> selected=[];
    private readonly Dictionary<Guid,int> lengths=[];
    public ObservableCollection<MaterialSourceChoice> Sources { get; }=[];
    public ObservableCollection<ChunkChoiceViewModel> Chunks { get; }=[];
    public ObservableCollection<SelectedReferenceViewModel> Selected { get; }=[];
    [ObservableProperty] public partial MaterialSourceChoice? SelectedSource { get; set; }
    [ObservableProperty] public partial bool HasMoreSources { get; set; }
    [ObservableProperty] public partial bool HasMoreChunks { get; set; }
    [ObservableProperty] public partial bool CanPreviousChunks { get; set; }
    [ObservableProperty] public partial string SelectionLabel { get; set; }="Nessun riferimento selezionato";
    [ObservableProperty] public partial string MaterialMessage { get; set; }="Scegli una fonte pronta e carica le sue parti.";
    public IReadOnlyList<Guid> SelectedChunkIds=>selected.Keys.ToArray();
    public IReadOnlyList<ChunkReferenceDraft> References=>selected.Values.Select(c=>new ChunkReferenceDraft(c.ChunkId,c.Quote)).ToArray();
    public int SelectedCharacters=>selected.Sum(c=>lengths.TryGetValue(c.Key,out var length) ? length : c.Value.Quote.Length);
    public void Configure(Guid id)=>projectId=id;
    public void SetInitial(IEnumerable<SourceCitation> citations)
    { selected.Clear(); lengths.Clear(); foreach(var citation in citations) selected[citation.ChunkId]=citation; RefreshSelected(); }
    partial void OnSelectedSourceChanged(MaterialSourceChoice? value)
    { Chunks.Clear(); chunkOffset=0; HasMoreChunks=false; CanPreviousChunks=false; MaterialMessage=value is null ? "Scegli una fonte pronta." : "Premi Carica parti per vedere il testo disponibile."; }
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        Sources.Clear(); sourceOffset=0; await AppendSourcesAsync(0);
        SelectedSource=Sources.FirstOrDefault();
        if(Sources.Count==0) MaterialMessage="Nessuna fonte pronta: aggiungi materiale nella sezione Fonti del progetto.";
    });
    private async Task AppendSourcesAsync(int offset)
    {
        var page=await flashcards.ReadySourcesAsync(projectId,offset);
        sourceOffset=offset+page.Count;
        foreach(var source in page) if(!Sources.Any(s=>s.Id==source.Id)) Sources.Add(new(source.Id,source.Title,source.ChunkCount));
        HasMoreSources=page.Count==20;
    }
    [RelayCommand] private Task MoreSourcesAsync()=>RunAsync(()=>AppendSourcesAsync(sourceOffset));
    [RelayCommand] private Task LoadChunksAsync()=>RunAsync(async()=>
    {
        if(SelectedSource is null) throw new StudyValidationException("Scegli una fonte prima di caricarne le parti.");
        chunkOffset=0; await FetchChunksAsync();
    });
    [RelayCommand] private Task MoreChunksAsync()=>RunAsync(async()=>
    {
        if(SelectedSource is null || !HasMoreChunks) return;
        chunkOffset+=20; await FetchChunksAsync();
    });
    [RelayCommand] private Task PreviousChunksAsync()=>RunAsync(async()=>
    { if(CanPreviousChunks) { chunkOffset=Math.Max(0,chunkOffset-20); await FetchChunksAsync(); } });
    private async Task FetchChunksAsync()
    {
        if(SelectedSource is null) return;
        var id=SelectedSource.Id; var page=await flashcards.SourceChunksAsync(projectId,id,chunkOffset);
        if(SelectedSource?.Id!=id) return;
        Chunks.Clear(); foreach(var chunk in page) Chunks.Add(Choice(chunk));
        HasMoreChunks=page.Count==20; CanPreviousChunks=chunkOffset>0;
        MaterialMessage=page.Count==0 ? "Questa pagina non contiene parti disponibili. I riferimenti già salvati restano conservati." : "Seleziona le parti da utilizzare. Puoi aggiungerne anche da altre fonti.";
    }
    private ChunkChoiceViewModel Choice(StudyChunk context)=>new(context,selected.ContainsKey(context.Chunk.Id),(choice,value)=>
    {
        var chunk=choice.Context;
        if(value)
        {
            if(selected.Count>=20 && !selected.ContainsKey(chunk.Chunk.Id))
            { choice.IsSelected=false; ErrorMessage="Puoi selezionare fino a 20 parti."; return; }
            if(!selected.ContainsKey(chunk.Chunk.Id)) selected[chunk.Chunk.Id]=new(chunk.Chunk.SourceId,chunk.Chunk.SourceRevisionId,chunk.Chunk.Id,
                chunk.Chunk.Text.Trim(),chunk.SourceTitle,FlashcardService.Locator(chunk.Chunk));
            lengths[chunk.Chunk.Id]=chunk.Chunk.Text.Length;
        }
        else selected.Remove(chunk.Chunk.Id);
        ErrorMessage=null; RefreshSelected();
    });
    private void Remove(Guid id)
    { selected.Remove(id); var loaded=Chunks.FirstOrDefault(c=>c.Context.Chunk.Id==id); if(loaded is not null) loaded.IsSelected=false; RefreshSelected(); }
    [RelayCommand] private void ClearSelection()
    { selected.Clear(); foreach(var chunk in Chunks) chunk.IsSelected=false; RefreshSelected(); }
    private void RefreshSelected()
    {
        Selected.Clear(); foreach(var citation in selected.Values) Selected.Add(new(citation,Remove));
        SelectionLabel=selected.Count==0 ? "Nessun riferimento selezionato" : $"{selected.Count} parti selezionate · {SelectedCharacters:N0} caratteri";
        OnPropertyChanged(nameof(SelectedChunkIds)); OnPropertyChanged(nameof(SelectedCharacters));
    }
}
