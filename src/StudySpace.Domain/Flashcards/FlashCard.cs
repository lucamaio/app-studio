using StudySpace.Domain.Common;
using StudySpace.Domain.Sources;
namespace StudySpace.Domain.Flashcards;

public sealed record FlashCard : ProjectEntity
{
    public required string Front { get; init; }
    public required string Back { get; init; }
    public string? Explanation { get; init; }
    public Difficulty? Difficulty { get; init; }
    public bool IsFavorite { get; init; }
    public Guid? SourceId { get; init; }
    public string? SourceTitle { get; init; }
    public bool IsSourceArchived { get; init; }
    public Guid? GenerationRunId { get; init; }
    public string? GenerationProviderKey { get; init; }
    public bool IsMockGenerated { get; init; }
    public bool IsUserEdited { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<SourceCitation> Citations { get; init; } = [];
    public IReadOnlyList<Guid> TagIds { get; init; } = [];
}

