using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.Application.Projects;
using StudySpace.Application.Sources;
namespace StudySpace.App.Features.Sources;

public partial class ManualSourceViewModel(ISourceImporter importer, ProjectService projects, INavigationService navigation,
    ILogger<ManualSourceViewModel> logger) : ViewModelBase(logger)
{
    private Guid projectId;
    private SourceDetails? saved;
    [ObservableProperty] public partial string ProjectName { get; set; } = "";
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool IsSaved { get; set; }
    public void Configure(string rawId) => projectId=Guid.TryParse(rawId, out var id) ? id : Guid.Empty;
    [RelayCommand] private Task LoadAsync() => RunAsync(async () =>
    {
        IsReady=false; ProjectName=(await projects.GetAsync(projectId)).Name; IsReady=true;
    });
    [RelayCommand] private Task SaveAsync() => RunAsync(async () =>
    {
        if (!IsReady) return;
        if (saved is null)
        {
            saved=await importer.ImportTextAsync(projectId, Title, Text);
            IsSaved=true;
        }
        // Retry after navigation failure reuses the committed source.
        await navigation.GoBackAsync();
    });
}
