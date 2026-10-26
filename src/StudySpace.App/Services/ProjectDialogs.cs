namespace StudySpace.App.Services;
public sealed class ProjectDialogs : IProjectDialogs
{
    public async Task<ProjectMenuAction> ShowMenuAsync(string projectName, bool isFavorite)
    {
        var favorite = isFavorite ? "Rimuovi dai preferiti" : "Aggiungi ai preferiti";
        var choice = await Shell.Current.DisplayActionSheetAsync(projectName, "Annulla", "Elimina", "Modifica", favorite);
        return choice switch
        {
            "Modifica" => ProjectMenuAction.Edit,
            "Elimina" => ProjectMenuAction.Delete,
            _ when choice == favorite => ProjectMenuAction.ToggleFavorite,
            _ => ProjectMenuAction.Cancel
        };
    }
    public Task<bool> ConfirmDeleteAsync(string projectName) => Shell.Current.DisplayAlertAsync(
        "Eliminare il progetto?",
        $"Il progetto «{projectName}» e tutti i suoi contenuti, compreso lo storico, saranno eliminati definitivamente.",
        "Elimina progetto", "Annulla");
}

