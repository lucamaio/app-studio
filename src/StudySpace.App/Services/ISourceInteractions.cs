using StudySpace.Application.Sources;
namespace StudySpace.App.Services;

public interface ISourceInteractions
{
    Task<SourceFileSelection?> PickFileAsync();
    Task<bool> ConfirmArchiveAsync(string title);
}
