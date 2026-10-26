using StudySpace.Domain.Common;
namespace StudySpace.Domain.Sources;

public sealed record StudySource : ProjectEntity
{
    public required string Title { get; init; }
    public SourceKind Kind { get; init; }
    public string? OriginalFileName { get; init; }
    public Guid? CurrentRevisionId { get; init; }
    public bool IsArchived { get; init; }
}
public sealed record SourceRevision : ProjectEntity
{
    public required Guid SourceId { get; init; }
    public int Number { get; init; } = 1;
    public string? RelativeFilePath { get; init; }
    public string ContentHash { get; init; } = "";
    public string ExtractedText { get; init; } = "";
    public string ExtractorVersion { get; init; } = "1";
    public ExtractionStatus Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public long SizeBytes { get; init; }
}
public sealed record SourceChunk : ProjectEntity
{
    public required Guid SourceId { get; init; }
    public required Guid SourceRevisionId { get; init; }
    public int Ordinal { get; init; }
    public required string Text { get; init; }
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public int? PageStart { get; init; }
    public int? PageEnd { get; init; }
    public string? SectionPath { get; init; }
    public string ChunkerVersion { get; init; } = "1";
}
public sealed record SourceCitation(Guid SourceId, Guid SourceRevisionId, Guid ChunkId,
    string Quote, string SourceTitle, string? Locator);

