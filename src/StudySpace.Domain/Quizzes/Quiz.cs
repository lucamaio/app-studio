using StudySpace.Domain.Common;
using StudySpace.Domain.Sources;
namespace StudySpace.Domain.Quizzes;

public sealed record Quiz : ProjectEntity
{
    public required string Title { get; init; }
    public QuizMode Mode { get; init; }
    public int RequestedCount { get; init; }
    public Difficulty Difficulty { get; init; }
    public bool IsArchived { get; init; }
}
public sealed record QuizQuestion : ProjectEntity
{
    public required Guid QuizId { get; init; }
    public int Position { get; init; }
    public QuestionKind Kind { get; init; }
    public required string Prompt { get; init; }
    public string Explanation { get; init; } = "";
    public string IdealAnswer { get; init; } = "";
    public string RubricJson { get; init; } = "[]";
    public Difficulty Difficulty { get; init; }
    public IReadOnlyList<QuestionOption> Options { get; init; } = [];
    public IReadOnlyList<SourceCitation> Citations { get; init; } = [];
}
public sealed record QuestionOption : ProjectEntity
{
    public required Guid QuestionId { get; init; }
    public int Position { get; init; }
    public required string Text { get; init; }
    public bool IsCorrect { get; init; }
}

