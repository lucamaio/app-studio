using Microsoft.Extensions.Logging;
using StudySpace.Application.Sources;
namespace StudySpace.App.Services;

public sealed class SourceStartup(ISourceImporter importer, ILogger<SourceStartup> logger)
{
    public void Start() => _=InitializeAsync();
    private async Task InitializeAsync()
    {
        try { await importer.InitializeAsync(); }
        catch (Exception ex)
        {
            // Pages surface storage errors with a retry; startup must not close the app.
            logger.LogWarning("Source recovery deferred: {ErrorType}",ex.GetType().Name);
        }
    }
}
