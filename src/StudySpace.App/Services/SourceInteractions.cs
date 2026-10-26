using StudySpace.Application.Sources;
namespace StudySpace.App.Services;

public sealed class SourceInteractions : ISourceInteractions
{
    public async Task<SourceFileSelection?> PickFileAsync()
    {
        var selection = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle="Aggiungi materiale di studio",
            FileTypes=new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI]=[".pdf", ".txt", ".md", ".markdown", ".docx"],
                [DevicePlatform.Android]=["application/pdf", "text/plain", "text/markdown", "text/x-markdown",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/octet-stream"]
            })
        });
        return selection is null ? null : new(selection.FileName, selection.ContentType, async ct =>
        {
            ct.ThrowIfCancellationRequested();
            // FullPath can be an Android content URI; read through the picker grant.
            return await selection.OpenReadAsync();
        });
    }
    public Task<bool> ConfirmArchiveAsync(string title) => Shell.Current.DisplayAlertAsync("Elimina fonte",
        $"Vuoi rimuovere “{title}” dalle fonti? Il testo e i riferimenti già utilizzati resteranno conservati per mantenere valide le citazioni. L'eliminazione del progetto rimuove anche questi dati.",
        "Elimina", "Annulla");
}
