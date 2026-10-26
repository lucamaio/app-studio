namespace StudySpace.Domain.Common;

public abstract record Entity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public int Revision { get; init; } = 1;
}
public abstract record ProjectEntity : Entity
{
    public required Guid ProjectId { get; init; }
}
public enum SourceKind { Manual = 0, Text = 1, Markdown = 2, Pdf = 3, Docx = 4 }
public enum ExtractionStatus { Pending = 0, Processing = 1, Ready = 2, Failed = 3, Cancelled = 4 }
public enum Difficulty { Easy = 0, Medium = 1, Hard = 2 }
public enum QuizMode { Closed = 0, Open = 1, Mixed = 2 }
public enum QuestionKind { Closed = 0, Open = 1 }
public enum AttemptStatus { InProgress = 0, SubmittedPendingEvaluation = 1, Completed = 2, Abandoned = 3 }
public enum EvaluationStatus { NotRequested = 0, Pending = 1, Evaluating = 2, Evaluated = 3, Failed = 4 }

