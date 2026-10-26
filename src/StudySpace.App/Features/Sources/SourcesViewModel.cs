using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Projects;
using StudySpace.Application.Sources;
namespace StudySpace.App.Features.Sources;

public partial class SourcesViewModel(SourceService sources, ISourceImporter importer, ProjectService projects,
    ISourceInteractions interactions, INavigationService navigation, ILogger<SourcesViewModel> logger) : ViewModelBase(logger)
{
    private Guid projectId;
    private int progressGeneration;
    public ObservableCollection<SourceCardViewModel> Items { get; } = [];
    [ObservableProperty] public partial string ProjectName { get; set; } = "";
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty] public partial string Activity { get; set; } = "Caricamento…";
    public void Configure(string rawId) => projectId=Guid.TryParse(rawId, out var id) ? id : Guid.Empty;
    [RelayCommand] private Task LoadAsync() => RunAsync(async () =>
    {
        Activity="Caricamento…"; IsReady=false; IsEmpty=false;
        ProjectName=(await projects.GetAsync(projectId)).Name;
        await ReloadAsync(); IsReady=true;
    });
    private SourceCardViewModel Card(SourceSummary summary) => new(summary,
        () => RunAsync(() => navigation.OpenSourceAsync(projectId, summary.Source.Id)));
    private async Task ReloadAsync()
    {
        var page=await sources.ListAsync(projectId);
        Items.Clear(); foreach (var source in page) Items.Add(Card(source));
        HasMore=page.Count==SourceService.PageSize; IsEmpty=Items.Count==0;
    }
    [RelayCommand] private Task LoadMoreAsync() => RunAsync(async () =>
    {
        var page=await sources.ListAsync(projectId, Items.Count);
        foreach (var source in page) Items.Add(Card(source));
        HasMore=page.Count==SourceService.PageSize;
    });
    [RelayCommand] private Task AddManualAsync() => RunAsync(async () =>
    { if (IsReady) await navigation.AddManualSourceAsync(projectId); });
    [RelayCommand(IncludeCancelCommand=true)] private Task ImportFileAsync(CancellationToken ct) => RunAsync(async () =>
    {
        if (!IsReady) return;
        Activity="Selezione del documento…";
        var file=await interactions.PickFileAsync();
        if (file is null) return;
        Activity="Elaborazione del documento…";
        var generation=++progressGeneration;
        var progress=new Progress<SourceSummary>(summary =>
        {
            if (generation != progressGeneration) return;
            var existing=Items.FirstOrDefault(x => x.Summary.Source.Id==summary.Source.Id);
            if (existing is not null) Items[Items.IndexOf(existing)]=Card(summary);
            else Items.Insert(0, Card(summary));
            IsEmpty=false;
        });
        try { await importer.ImportFileAsync(projectId, file, progress, ct); }
        finally { ++progressGeneration; }
        await ReloadAsync();
    });
}
