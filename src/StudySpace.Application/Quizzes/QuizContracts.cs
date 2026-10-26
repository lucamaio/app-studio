using StudySpace.Application.Flashcards;
using StudySpace.Domain.Common;
using StudySpace.Domain.Projects;
using StudySpace.Domain.Quizzes;
using StudySpace.Domain.Sources;
namespace StudySpace.Application.Quizzes;

public enum AnswerOutcome { Incorrect=0, Partial=1, Correct=2 }
public sealed record QuizFilters(IReadOnlyList<Guid>? SourceIds=null,IReadOnlyList<Guid>? TopicIds=null,IReadOnlyList<Guid>? TagIds=null);
public sealed record QuizConfiguration(QuizMode Mode,int Count,Difficulty? Difficulty,QuizFilters? Filters=null);
public sealed record RubricCriterion(string Id,string Concept,double Weight,IReadOnlyList<Guid> ChunkIds);
public sealed record QuizOptionSnapshot(Guid Id,string Text,bool IsCorrect);
public sealed record QuizTopicSnapshot(string Name,IReadOnlyList<Guid> ChunkIds);
public sealed record QuizTagSnapshot(Guid Id,string Name,IReadOnlyList<Guid> ChunkIds);
public sealed record QuizQuestionSnapshot(Guid Id,int Position,QuestionKind Kind,string Prompt,string Explanation,
    string IdealAnswer,Difficulty Difficulty,IReadOnlyList<QuizOptionSnapshot> Options,
    IReadOnlyList<RubricCriterion> Rubric,IReadOnlyList<SourceCitation> Citations,int SchemaVersion=1,
    IReadOnlyList<QuizTopicSnapshot>? Topics=null,IReadOnlyList<QuizTagSnapshot>? Tags=null);
public sealed record AiQuizOption(string Text,bool IsCorrect);
public sealed record AiQuizQuestion(QuestionKind Kind,string Prompt,string Explanation,string IdealAnswer,Difficulty Difficulty,
    IReadOnlyList<AiQuizOption> Options,IReadOnlyList<RubricCriterion> Rubric,IReadOnlyList<ChunkReferenceDraft> References);
public sealed record AiQuizDraft(IReadOnlyList<AiQuizQuestion> Questions);
public sealed record QuizGenerationRequest(StudyProject Project,QuizConfiguration Configuration,IReadOnlyList<StudyChunk> Chunks,string Language="it");
public sealed record CriterionAssessment(string CriterionId,double Grade);
public sealed record OpenAnswerEvaluationRequest(Guid ProjectId,Guid AttemptId,Guid QuestionId,int AnswerRevision,
    QuizQuestionSnapshot Question,string Answer);
public sealed record AnswerFeedback(AnswerOutcome Outcome,double Score,string Explanation,string IdealAnswer,
    IReadOnlyList<string> CorrectConcepts,IReadOnlyList<string> MissingConcepts,IReadOnlyList<string> Errors,string Hint,
    IReadOnlyList<CriterionAssessment> Criteria,string ProviderKey,bool IsMock,int ScoringVersion=1,EvaluationMetadata? SemanticEvaluation=null);
public sealed record QuizAnswerState(Guid QuestionId,string Text,Guid? SelectedOptionId,int Revision,EvaluationStatus Status,
    AnswerFeedback? Feedback,string? Error,Guid? EvaluationToken=null)
{
    public bool IsAnswered=>!string.IsNullOrWhiteSpace(Text)||SelectedOptionId.HasValue;
    public bool IsLocked=>Status is EvaluationStatus.Evaluated or EvaluationStatus.Evaluating;
}
public sealed record QuizSession(QuizAttempt Attempt,string Title,bool IsMock,string ProviderKey,bool MixedDifficulty,
    IReadOnlyList<QuizQuestionSnapshot> Questions,IReadOnlyList<QuizAnswerState> Answers);
public sealed record QuizAttemptSummary(Guid Id,string Title,AttemptStatus Status,DateTimeOffset StartedAtUtc,int Count,int AnsweredCount,double? Score,bool IsMock);
public sealed record QuizStartData(Guid OperationId,Guid ProjectId,QuizConfiguration Configuration,AiProviderInfo Provider,
    DateTimeOffset CreatedAtUtc,IReadOnlyList<Guid> InputChunkIds,IReadOnlyList<QuizQuestionSnapshot> Questions);
public sealed record EvaluationClaim(QuizQuestionSnapshot Question,QuizAnswerState Answer,Guid Token);
public interface IQuizRepository
{
    Task<Guid> CreateAsync(QuizStartData quiz,CancellationToken ct=default);
    Task<IReadOnlyList<QuizAttemptSummary>> ListAsync(Guid projectId,int offset,int limit,CancellationToken ct=default);
    Task<Guid?> LatestInProgressAsync(Guid projectId,CancellationToken ct=default);
    Task<QuizSession?> GetAsync(Guid projectId,Guid attemptId,CancellationToken ct=default);
    Task RecoverAsync(Guid projectId,Guid attemptId,DateTimeOffset now,CancellationToken ct=default);
    Task<QuizAnswerState> SaveOpenAsync(Guid projectId,Guid attemptId,Guid questionId,string text,int expectedRevision,DateTimeOffset now,CancellationToken ct=default);
    Task<QuizAnswerState> AnswerClosedAsync(Guid projectId,Guid attemptId,Guid questionId,Guid optionId,int expectedRevision,DateTimeOffset now,CancellationToken ct=default);
    Task<EvaluationClaim?> ClaimAsync(Guid projectId,Guid attemptId,Guid questionId,DateTimeOffset now,CancellationToken ct=default);
    Task<bool> AcceptAsync(Guid projectId,Guid attemptId,EvaluationClaim claim,AnswerFeedback feedback,DateTimeOffset now,CancellationToken ct=default);
    Task FailAsync(Guid projectId,Guid attemptId,EvaluationClaim claim,string message,DateTimeOffset now,CancellationToken ct=default);
    Task SubmitAsync(Guid projectId,Guid attemptId,DateTimeOffset now,CancellationToken ct=default);
    Task<bool> CompleteAsync(Guid projectId,Guid attemptId,DateTimeOffset now,CancellationToken ct=default);
    Task AbandonAsync(Guid projectId,Guid attemptId,DateTimeOffset now,CancellationToken ct=default);
    Task CheckpointAsync(Guid projectId,Guid attemptId,int position,long activeDurationMs,DateTimeOffset now,CancellationToken ct=default);
    Task<int> GetPositionAsync(Guid projectId,Guid attemptId,CancellationToken ct=default);
}
public sealed class QuizNotFoundException():Exception("Questo tentativo non è disponibile nel progetto.");
public sealed class QuizConflictException():Exception("Il tentativo è cambiato o la risposta è già verificata. Ricarica prima di riprovare.");


