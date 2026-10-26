using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Sources;
using StudySpace.Domain.Sources;
namespace StudySpace.App.Features.Sources;

public partial class SourceDetailViewModel(SourceService sources, ISourceImporter importer, ISourceInteractions interactions,
    INavigationService navigation, ILogger<SourceDetailViewModel> logger) : ViewModelBase(logger)
{
    private Guid projectId, sourceId;
    private SourceDetails? details;
    private int textPage;
    private int chunkOffset;
    private const int TextPageSize=6000;
    public ObservableCollection<ChunkPresentation> Chunks { get; }=[];
    [ObservableProperty] public partial SourceCardViewModel? Card { get; set; }
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial string RenameTitle { get; set; }="";
    [ObservableProperty] public partial string OriginalFileName { get; set; }="";
    [ObservableProperty] public partial string LocalCopy { get; set; }="";
    [ObservableProperty] public partial string ExtractedPage { get; set; }="";
    [ObservableProperty] public partial string PageLabel { get; set; }="";
    [ObservableProperty] public partial bool CanPrevious { get; set; }
    [ObservableProperty] public partial bool CanNext { get; set; }
    [ObservableProperty] public partial bool HasMoreChunks { get; set; }
    [ObservableProperty] public partial bool CanPreviousChunks { get; set; }
    [ObservableProperty] public partial string ChunkPageLabel { get; set; }="";
    [ObservableProperty] public partial bool HasText { get; set; }
    public void Configure(string project, string source)
    {
        projectId=Guid.TryParse(project,out var p) ? p : Guid.Empty;
        sourceId=Guid.TryParse(source,out var s) ? s : Guid.Empty;
    }
    [RelayCommand] private Task LoadAsync() => RunAsync(RefreshAsync);
    private async Task RefreshAsync()
    {
        IsReady=false;
        details=await sources.GetAsync(projectId,sourceId);
        Card=new(details.Summary,()=>Task.CompletedTask);
        RenameTitle=Card.Title;
        OriginalFileName=details.Summary.Source.OriginalFileName ?? "Testo inserito manualmente";
        LocalCopy=details.Summary.Revision.RelativeFilePath ?? "Nessun file locale";
        textPage=0; ShowTextPage();
        chunkOffset=0;
        await AppendChunksAsync();
        IsReady=true;
    }
    private void ShowTextPage()
    {
        var text=details?.Summary.Revision.ExtractedText ?? "";
        HasText=details?.Summary.Revision.Status == StudySpace.Domain.Common.ExtractionStatus.Ready && text.Length>0;
        var start=Math.Min(textPage*TextPageSize,text.Length);
        var end=Math.Min(start+TextPageSize,text.Length);
        if(start>0 && start<text.Length && char.IsLowSurrogate(text[start])) start--;
        if(end<text.Length && char.IsLowSurrogate(text[end])) end--;
        ExtractedPage=HasText ? text[start..end] : "";
        var total=Math.Max(1,(text.Length+TextPageSize-1)/TextPageSize);
        PageLabel=$"Testo estratto · parte {textPage+1} di {total}";
        CanPrevious=textPage>0; CanNext=textPage+1<total;
    }
    [RelayCommand] private void NextText() { if(CanNext) { textPage++; ShowTextPage(); } }
    [RelayCommand] private void PreviousText() { if(CanPrevious) { textPage--; ShowTextPage(); } }
    private async Task AppendChunksAsync()
    {
        var page=await sources.GetChunksAsync(projectId,sourceId,chunkOffset);
        Chunks.Clear();
        foreach(var c in page) Chunks.Add(new(c));
        HasMoreChunks=chunkOffset+page.Count < (details?.Summary.ChunkCount ?? 0) && page.Count>0;
        CanPreviousChunks=chunkOffset>0;
        ChunkPageLabel=page.Count>0 ? $"Chunk {chunkOffset+1}–{chunkOffset+page.Count} di {details?.Summary.ChunkCount}" : "";
    }
    [RelayCommand] private Task LoadMoreChunksAsync()=>RunAsync(async()=>
    { if(HasMoreChunks) { chunkOffset+=20; await AppendChunksAsync(); } });
    [RelayCommand] private Task PreviousChunksAsync()=>RunAsync(async()=>
    { if(CanPreviousChunks) { chunkOffset=Math.Max(0,chunkOffset-20); await AppendChunksAsync(); } });
    [RelayCommand] private Task RenameAsync()=>RunAsync(async()=>
    {
        if(details is null || !IsReady) return;
        await sources.RenameAsync(details.Summary,RenameTitle);
        await RefreshAsync();
    });
    [RelayCommand(IncludeCancelCommand=true)] private Task RetryAsync(CancellationToken ct)=>RunAsync(async()=>
    {
        if(Card?.CanRetry != true) return;
        Card=new(Card.Summary with { Revision=Card.Summary.Revision with {
            Status=StudySpace.Domain.Common.ExtractionStatus.Processing, ErrorMessage=null } },()=>Task.CompletedTask);
        await importer.RetryAsync(projectId,sourceId,ct);
        await RefreshAsync();
    });
    [RelayCommand] private Task DeleteAsync()=>RunAsync(async()=>
    {
        if(details is null || !IsReady || !await interactions.ConfirmArchiveAsync(details.Summary.Source.Title)) return;
        await sources.ArchiveAsync(details.Summary);
        IsReady=false;
        await navigation.OpenProjectSectionAsync(projectId,"sources");
    });
}
public sealed class ChunkPresentation(SourceChunk chunk)
{
    public string Title=>$"Chunk {chunk.Ordinal+1} · caratteri {chunk.StartOffset}–{chunk.EndOffset}";
    public string Locator=>string.Join(" · ",new[] { chunk.PageStart.HasValue ?
        (chunk.PageStart==chunk.PageEnd ? $"Pagina {chunk.PageStart}" : $"Pagine {chunk.PageStart}–{chunk.PageEnd}") : null,
        chunk.SectionPath, chunk.ChunkerVersion }.Where(s=>!string.IsNullOrWhiteSpace(s)));
    public string Text=>chunk.Text.Length>320 ? chunk.Text[..320]+"…" : chunk.Text;
}
