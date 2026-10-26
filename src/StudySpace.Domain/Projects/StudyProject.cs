using StudySpace.Domain.Common;
namespace StudySpace.Domain.Projects;

public sealed record StudyProject : Entity
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Color { get; init; } = "#5B63D3";
    public string IconKey { get; init; } = "book";
    public bool IsFavorite { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
public sealed record Tag : ProjectEntity
{
    public required string Name { get; init; }
    public required string NormalizedName { get; init; }
}
// Association: identity is the composite (ProjectId, TagId).
public sealed record ProjectTag(Guid ProjectId, Guid TagId, DateTimeOffset CreatedAtUtc);

