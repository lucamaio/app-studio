using StudySpace.Domain.Common;
namespace StudySpace.Domain.Learning;

public sealed record Topic : ProjectEntity
{
    public required string Name { get; init; }
    public required string NormalizedName { get; init; }
}
// Evidence for one topic in one attempt, not a global diagnosis.
public sealed record StudyWeakness : ProjectEntity
{
    public required Guid AttemptId { get; init; }
    public required Guid TopicId { get; init; }
    public double EarnedScore { get; init; }
    public double MaximumScore { get; init; }
    public int EvidenceCount { get; init; }
    public string DetailsJson { get; init; } = "{}";
}

