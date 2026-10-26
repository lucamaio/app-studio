using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Projects;
using StudySpace.Domain.Projects;
namespace StudySpace.App.Features.Projects;

public enum ProjectActionResult { None, Changed, Deleted }
public sealed class ProjectActions(ProjectService projects, INavigationService navigation, IProjectDialogs dialogs)
{
    public async Task<ProjectActionResult> ExecuteAsync(StudyProject project, ProjectMenuAction action)
    {
        switch (action)
        {
            case ProjectMenuAction.Edit:
                await navigation.EditProjectAsync(project.Id);
                return ProjectActionResult.None;
            case ProjectMenuAction.ToggleFavorite:
                await projects.SetFavoriteAsync(project, !project.IsFavorite);
                return ProjectActionResult.Changed;
            case ProjectMenuAction.Delete:
                if (!await dialogs.ConfirmDeleteAsync(project.Name)) return ProjectActionResult.None;
                await projects.DeleteAsync(project);
                return ProjectActionResult.Deleted;
            default:
                return ProjectActionResult.None;
        }
    }
    public async Task<ProjectActionResult> ShowMenuAsync(StudyProject project) =>
        await ExecuteAsync(project, await dialogs.ShowMenuAsync(project.Name, project.IsFavorite));
}

