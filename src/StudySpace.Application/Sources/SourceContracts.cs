using StudySpace.Domain.Common;
using StudySpace.Domain.Sources;
namespace StudySpace.Application.Sources;

public sealed record SourceImportOptions
{
    public long MaxFileBytes { get; init; } = 25 * 1024 * 1024;
    public int MaxTextCharacters { get; init; } = 2_000_000;
    public long MaxDocxExpandedBytes { get; init; } = 50 * 1024 * 1024;
    public int MaxPdfPages { get; init; } = 2000;
}
public sealed record SourceFileSelection(string FileName, string? ContentType,
    Func<CancellationToken, Task<Stream>> OpenReadAsync);
public sealed record StoredSourceFile(string RelativePath, long SizeBytes, string ContentHash);
public sealed record TextSegment(string Text, int? Page = null, string? Section = null);
public sealed record ExtractedDocument(IReadOnlyList<TextSegment> Segments, string ExtractorVersion);
public sealed record TextLocator(int StartOffset, int EndOffset, int? Page, string? Section);
public sealed record NormalizedDocument(string Text, IReadOnlyList<TextLocator> Locators);
public sealed record SourceSummary(StudySource Source, SourceRevision Revision, int ChunkCount);
public sealed record SourceDetails(SourceSummary Summary);

public interface ISourceImporter
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<SourceDetails> ImportFileAsync(Guid projectId, SourceFileSelection file,
        IProgress<SourceSummary>? progress = null, CancellationToken ct = default);
    Task<SourceDetails> ImportTextAsync(Guid projectId, string title, string text, CancellationToken ct = default);
    Task<SourceDetails> RetryAsync(Guid projectId, Guid sourceId, CancellationToken ct = default);
}
public interface IDocumentTextExtractor
{
    SourceKind Kind { get; }
    Task<ExtractedDocument> ExtractAsync(Stream stream, CancellationToken ct = default);
}
public interface ISourceChunkingService
{
    NormalizedDocument Normalize(ExtractedDocument document);
    IReadOnlyList<SourceChunk> Chunk(Guid projectId, Guid sourceId, Guid revisionId,
        NormalizedDocument document, DateTimeOffset now, CancellationToken ct = default);
}
public interface ISourceRepository
{
    Task<IReadOnlyList<SourceSummary>> ListAsync(Guid projectId, int offset, int limit, CancellationToken ct = default);
    Task<SourceDetails?> GetAsync(Guid projectId, Guid sourceId, CancellationToken ct = default);
    Task<IReadOnlyList<SourceChunk>> GetChunksAsync(Guid projectId, Guid sourceId, int offset, int limit, CancellationToken ct = default);
    Task CreateAsync(StudySource source, SourceRevision revision, CancellationToken ct = default);
    Task SetFileAsync(Guid projectId, Guid sourceId, Guid revisionId, StoredSourceFile file, CancellationToken ct = default);
    Task CompleteAsync(Guid projectId, Guid sourceId, Guid revisionId, string text, string extractorVersion,
        IReadOnlyList<SourceChunk> chunks, DateTimeOffset now, CancellationToken ct = default);
    Task SetStatusAsync(Guid projectId, Guid sourceId, ExtractionStatus status, string? code, string? message, CancellationToken ct = default);
    Task RenameAsync(Guid projectId, Guid sourceId, int expectedRevision, string title, DateTimeOffset now, CancellationToken ct = default);
    Task ArchiveAsync(Guid projectId, Guid sourceId, int expectedRevision, DateTimeOffset now, CancellationToken ct = default);
    Task RecoverInterruptedAsync(CancellationToken ct = default);
    Task<IReadOnlySet<string>> ReferencedFilesAsync(CancellationToken ct = default);
}
public interface ISourceFileStore
{
    Task<StoredSourceFile> SaveAsync(Guid projectId, Guid sourceId, Guid revisionId, string extension, Stream input, CancellationToken ct);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct);
    Task DeleteProjectAsync(Guid projectId);
    Task CleanupOrphansAsync(IReadOnlySet<string> referencedPaths, CancellationToken ct);
}

public sealed class SourceImportException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
public sealed class SourceNotFoundException() : Exception("La fonte non è più disponibile in questo progetto.");
public sealed class SourceConflictException() : Exception("La fonte è stata modificata. Ricarica prima di riprovare.");

public static class SourceFormats
{
    public static SourceKind FromFileName(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".pdf" => SourceKind.Pdf, ".txt" => SourceKind.Text,
        ".md" or ".markdown" => SourceKind.Markdown, ".docx" => SourceKind.Docx,
        _ => throw new StudySpace.Application.Common.StudyValidationException("Scegli un file PDF, TXT, Markdown o DOCX.")
    };
    public static string Title(string? title)
    {
        var clean = title?.Trim() ?? "";
        if (clean.Length is < 1 or > 200)
            throw new StudySpace.Application.Common.StudyValidationException("Inserisci un titolo da 1 a 200 caratteri.");
        return clean;
    }
}
