namespace StudySpace.App.Services;
public enum ProjectMenuAction { Cancel, Edit, ToggleFavorite, Delete }
public interface IProjectDialogs
{
    Task<ProjectMenuAction> ShowMenuAsync(string projectName, bool isFavorite);
    Task<bool> ConfirmDeleteAsync(string projectName);
}

