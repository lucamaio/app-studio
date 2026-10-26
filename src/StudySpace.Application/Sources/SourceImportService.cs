using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Common;
using StudySpace.Domain.Sources;
namespace StudySpace.Application.Sources;

public sealed class SourceImportService(ISourceRepository repository, ISourceFileStore files,
    IEnumerable<IDocumentTextExtractor> extractors, ISourceChunkingService chunker,
    ProjectService projects, IClock clock, SourceImportOptions options, ILogger<SourceImportService>? logger=null) : ISourceImporter
{
    private readonly SemaphoreSlim gate = new(1,1);
    private readonly IReadOnlyDictionary<SourceKind, IDocumentTextExtractor> parsers = extractors.ToDictionary(p => p.Kind);
    private bool initialized;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { await InitializeCoreAsync(ct).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        if (initialized) return;
        await repository.RecoverInterruptedAsync(ct).ConfigureAwait(false);
        await files.CleanupOrphansAsync(await repository.ReferencedFilesAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        initialized = true;
    }
    public async Task<SourceDetails> ImportFileAsync(Guid projectId, SourceFileSelection file,
        IProgress<SourceSummary>? progress = null, CancellationToken ct = default)
    {
        var name = file.FileName.Replace('\\','/').Split('/')[^1];
        if (name.Length is < 1 or > 255) throw new StudyValidationException("Il nome del file deve contenere da 1 a 255 caratteri.");
        var kind = SourceFormats.FromFileName(name);
        ValidateMime(kind, file.ContentType);
        var title = Path.GetFileNameWithoutExtension(name);
        if (title.Length > 200) title=title[..200];
        title = SourceFormats.Title(title);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(ct).ConfigureAwait(false);
            var source = await CreateAsync(projectId, title, kind, name, "", ct).ConfigureAwait(false);
            progress?.Report(source.Summary);
            return await ProcessAsync(source, file, progress, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task<SourceDetails> ImportTextAsync(Guid projectId, string title, string text, CancellationToken ct = default)
    {
        title = SourceFormats.Title(title);
        if (string.IsNullOrWhiteSpace(text)) throw new StudyValidationException("Inserisci il testo da studiare.");
        if (text.Length > options.MaxTextCharacters) throw new StudyValidationException("Il testo supera il limite di caratteri.");
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(ct).ConfigureAwait(false);
            var source = await CreateAsync(projectId, title, SourceKind.Manual, null, text, ct).ConfigureAwait(false);
            return await ProcessAsync(source, null, null, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task<SourceDetails> RetryAsync(Guid projectId, Guid sourceId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(ct).ConfigureAwait(false);
            var details = await repository.GetAsync(projectId, sourceId, ct).ConfigureAwait(false) ?? throw new SourceNotFoundException();
            var revision = details.Summary.Revision;
            if (revision.Status is ExtractionStatus.Ready or ExtractionStatus.Processing)
                throw new StudyValidationException("Questa fonte non richiede una nuova elaborazione.");
            if (revision.RelativeFilePath is null && details.Summary.Source.Kind != SourceKind.Manual)
                throw new StudyValidationException("La copia locale non è disponibile. Importa nuovamente il file originale.");
            await repository.SetStatusAsync(projectId, sourceId, ExtractionStatus.Processing, null, null, ct).ConfigureAwait(false);
            return await ProcessAsync(details, null, null, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private async Task<SourceDetails> CreateAsync(Guid projectId, string title, SourceKind kind, string? name, string text, CancellationToken ct)
    {
        await projects.GetAsync(projectId, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var revisionId = Guid.NewGuid();
        var source = new StudySource { ProjectId=projectId, Title=title, Kind=kind, OriginalFileName=name,
            CurrentRevisionId=revisionId, CreatedAtUtc=now, UpdatedAtUtc=now };
        var revision = new SourceRevision { Id=revisionId, ProjectId=projectId, SourceId=source.Id,
            Status=ExtractionStatus.Processing, ExtractedText=text, SizeBytes=Encoding.UTF8.GetByteCount(text),
            CreatedAtUtc=now, UpdatedAtUtc=now };
        await repository.CreateAsync(source, revision, ct).ConfigureAwait(false);
        return new(new(source, revision, 0));
    }
    private async Task<SourceDetails> ProcessAsync(SourceDetails details, SourceFileSelection? input,
        IProgress<SourceSummary>? progress, CancellationToken ct)
    {
        var source = details.Summary.Source;
        var revision = details.Summary.Revision;
        var stage="OpenOriginal";
        try
        {
            if (input is not null)
            {
                await using var stream = await input.OpenReadAsync(ct).ConfigureAwait(false);
                stage="CopyPrivateFile";
                var stored = await files.SaveAsync(source.ProjectId, source.Id, revision.Id,
                    Path.GetExtension(source.OriginalFileName!).ToLowerInvariant(), stream, ct).ConfigureAwait(false);
                await repository.SetFileAsync(source.ProjectId, source.Id, revision.Id, stored, ct).ConfigureAwait(false);
                revision = revision with { RelativeFilePath=stored.RelativePath, SizeBytes=stored.SizeBytes, ContentHash=stored.ContentHash };
            }
            stage="ExtractText";
            ExtractedDocument extracted;
            if (source.Kind == SourceKind.Manual) extracted = new([new(revision.ExtractedText)], "manual-v1");
            else
            {
                await using var copy = await files.OpenReadAsync(revision.RelativeFilePath!, ct).ConfigureAwait(false);
                if (input is null)
                {
                    var hash=Convert.ToHexString(await SHA256.HashDataAsync(copy,ct).ConfigureAwait(false));
                    if (copy.Length != revision.SizeBytes || hash != revision.ContentHash)
                        throw new SourceImportException("ChangedLocalFile", "La copia locale è cambiata o danneggiata. Importa nuovamente il documento originale.");
                    copy.Position=0;
                }
                extracted = await parsers[source.Kind].ExtractAsync(copy, ct).ConfigureAwait(false);
            }
            // Normalization and chunking are CPU work, also for manually entered text.
            var (normalized, chunks) = await Task.Run(() =>
            {
                var doc = chunker.Normalize(extracted);
                return (doc, chunker.Chunk(source.ProjectId, source.Id, revision.Id, doc, clock.UtcNow, ct));
            }, ct).ConfigureAwait(false);
            await repository.CompleteAsync(source.ProjectId, source.Id, revision.Id, normalized.Text,
                extracted.ExtractorVersion, chunks, clock.UtcNow, ct).ConfigureAwait(false);
        }
        catch (StorageUnavailableException)
        {
            try
            {
                await repository.SetStatusAsync(source.ProjectId,source.Id,ExtractionStatus.Failed,"StorageError",
                    "Salvataggio interrotto. Riprova l'elaborazione quando l'archivio è disponibile.",CancellationToken.None).ConfigureAwait(false);
            }
            catch (StorageUnavailableException) { /* Startup recovery handles an unavailable archive. */ }
            throw;
        }
        catch (Exception ex) when (ex is not SourceConflictException and not SourceNotFoundException)
        {
            logger?.LogWarning("Source import failed. Stage: {Stage}; type: {ErrorType}; code: {HResult}",stage,ex.GetType().Name,ex.HResult);
            var cancelled = ex is OperationCanceledException;
            var code = ex is SourceImportException error ? error.Code : cancelled ? "Cancelled" : "UnreadableDocument";
            var message = ex is SourceImportException known ? known.Message : cancelled ?
                "Elaborazione annullata. Puoi riprovare dalla copia locale, se disponibile." : stage is "OpenOriginal" or "CopyPrivateFile" ?
                "Non è possibile leggere o salvare una copia del file. Controlla lo spazio disponibile e seleziona nuovamente il documento." :
                "Il documento è danneggiato, protetto o non leggibile. Prova un altro file oppure inserisci il testo manualmente.";
            // Persist the outcome even when the operation token has been cancelled.
            await repository.SetStatusAsync(source.ProjectId, source.Id,
                cancelled ? ExtractionStatus.Cancelled : ExtractionStatus.Failed, code, message, CancellationToken.None).ConfigureAwait(false);
        }
        var result = await repository.GetAsync(source.ProjectId, source.Id, CancellationToken.None).ConfigureAwait(false) ?? throw new SourceNotFoundException();
        progress?.Report(result.Summary);
        return result;
    }
    private static void ValidateMime(SourceKind kind, string? contentType)
    {
        var mime = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(mime) || mime == "application/octet-stream") return;
        var valid = kind switch
        {
            SourceKind.Pdf => mime == "application/pdf",
            SourceKind.Docx => mime is "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/zip",
            SourceKind.Markdown => mime is "text/markdown" or "text/x-markdown" or "text/plain",
            _ => mime == "text/plain"
        };
        if (!valid) throw new StudyValidationException("Il tipo del file non corrisponde all'estensione. Scegli un documento supportato.");
    }
}
