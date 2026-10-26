using StudySpace.Domain.Common;
namespace StudySpace.Domain.Quizzes;

public sealed record QuizAttempt : ProjectEntity
{
    public Guid? QuizId { get; init; }
    public QuizMode Mode { get; init; }
    public Difficulty Difficulty { get; init; }
    public AttemptStatus Status { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public long ActiveDurationMs { get; init; }
    public double? Score { get; init; }
    public int MaximumScore { get; init; }
    public int ScoringVersion { get; init; } = 1;
}
public sealed record AttemptQuestion : ProjectEntity
{
    public required Guid AttemptId { get; init; }
    public Guid? OriginalQuestionId { get; init; }
    public int Position { get; init; }
    public required string SnapshotJson { get; init; }
    public int SnapshotSchemaVersion { get; init; } = 1;
}
public sealed record QuizAnswer : ProjectEntity
{
    public required Guid AttemptQuestionId { get; init; }
    public string? Text { get; init; }
    public Guid? SelectedOptionId { get; init; }
    public int AnswerRevision { get; init; } = 1;
    public DateTimeOffset? AnsweredAtUtc { get; init; }
    public EvaluationStatus EvaluationStatus { get; init; }
}

