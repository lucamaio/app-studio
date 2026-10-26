using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Projects;
using StudySpace.Application.Quizzes;
namespace StudySpace.App.Features.Projects;

public partial class ProjectDashboardViewModel(ProjectService projects, INavigationService navigation,
    ProjectActions actions, ILogger<ProjectDashboardViewModel> logger,QuizService? quizzes=null) : ViewModelBase(logger)
{
    private Guid projectId;
    private Guid? resumeAttemptId;
    [ObservableProperty] public partial string ContinueHint {get;set;}="";
    [ObservableProperty] public partial ProjectCardViewModel? Card { get; set; }
    [ObservableProperty] public partial bool HasProject { get; set; }
    [ObservableProperty] public partial int SourceCount { get; set; }
    [ObservableProperty] public partial int FlashcardCount { get; set; }
    [ObservableProperty] public partial int CompletedQuizCount { get; set; }
    public void Configure(string rawId) => projectId = Guid.TryParse(rawId, out var id) ? id : Guid.Empty;
    [RelayCommand] private Task LoadAsync() => RunAsync(RefreshAsync);
    private async Task RefreshAsync()
    {
        HasProject = false;
        var summary = await projects.GetSummaryAsync(projectId);
        var p = summary.Project;
        Card = new(p, () => Task.CompletedTask, () => RunAsync(() => MenuAsync(p)),
            action => RunAsync(() => ActAsync(p, action)));
        SourceCount = summary.SourceCount; FlashcardCount = summary.FlashcardCount; CompletedQuizCount = summary.CompletedQuizCount;
        resumeAttemptId=quizzes is null?null:await quizzes.LatestInProgressAsync(projectId);
        ContinueHint=resumeAttemptId.HasValue?"Riprendi il quiz lasciato in corso.":FlashcardCount>0?"Ripassa le flashcard del progetto.":SourceCount>0?"Riapri il materiale di studio.":"Aggiungi la prima fonte per iniziare.";
        HasProject = true;
    }
    private async Task MenuAsync(StudyProject p) => await AfterActionAsync(await actions.ShowMenuAsync(p));
    private async Task ActAsync(StudyProject p, ProjectMenuAction action) => await AfterActionAsync(await actions.ExecuteAsync(p, action));
    private async Task AfterActionAsync(ProjectActionResult result)
    {
        if (result == ProjectActionResult.Deleted)
        {
            HasProject = false;
            Card = null;
            await navigation.GoToProjectsAsync();
        }
        else if (result == ProjectActionResult.Changed) await RefreshAsync();
    }
    [RelayCommand] private Task BackToProjectsAsync() => RunAsync(navigation.GoToProjectsAsync);
    [RelayCommand] private Task NewQuizAsync()=>RunAsync(()=>navigation.NewQuizAsync(projectId));
    [RelayCommand] private Task ContinueStudyAsync()=>RunAsync(async()=>
    {
        if(!HasProject)throw new ProjectNotFoundException();
        if(resumeAttemptId.HasValue)await navigation.OpenQuizAttemptAsync(projectId,resumeAttemptId.Value);
        else if(FlashcardCount>0)await navigation.StudyFlashcardsAsync(projectId,new());
        else await navigation.OpenProjectSectionAsync(projectId,"sources");
    });
    [RelayCommand] private Task OpenSectionAsync(string section) => RunAsync(async () =>
    {
        if (!HasProject || !ProjectSectionViewModel.Sections.ContainsKey(section)) throw new ProjectNotFoundException();
        await navigation.OpenProjectSectionAsync(projectId, section);
    });
}

