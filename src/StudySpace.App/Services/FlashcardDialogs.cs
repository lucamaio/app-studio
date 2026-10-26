namespace StudySpace.App.Services;
public sealed class FlashcardDialogs:IFlashcardDialogs
{
    public Task<bool> ConfirmDeleteAsync(string question)=>Shell.Current.DisplayAlertAsync("Elimina flashcard",
        $"Vuoi eliminare questa flashcard e i suoi collegamenti?\n\n{(question.Length>120 ? question[..120]+"…" : question)}","Elimina","Annulla");
}
