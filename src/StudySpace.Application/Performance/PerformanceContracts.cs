using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
using StudySpace.Domain.Sources;
namespace StudySpace.Application.Performance;

public enum PerformanceGroupKind { Topic, Tag, Source, Concept }
public sealed record PerformanceCursor(DateTimeOffset CompletedAtUtc,Guid Id);
public sealed record HistoryAttempt(Guid Id,string Title,DateTimeOffset StartedAtUtc,QuizMode Mode,Difficulty Difficulty,
    bool MixedDifficulty,AttemptStatus Status,int Count,double? Score,long ActiveDurationMs,bool IsMock);
public sealed record PerformanceReference(Guid AttemptId,Guid QuestionId,SourceCitation Citation);
public sealed record PerformanceGroup(PerformanceGroupKind Kind,string Key,string Name,int EvidenceCount,int Unanswered,
    double DifficultyIndex,int RecentCount,int RecentErrors,int RecentPartial,double? Improvement,
    IReadOnlyList<string> Suggestions,IReadOnlyList<PerformanceReference> References);
public sealed record ProjectPerformance(int CompletedCount,double? AveragePercentage,IReadOnlyList<HistoryAttempt> Recent,
    IReadOnlyList<PerformanceGroup> Difficulties,IReadOnlyList<PerformanceGroup> Improving,int DemonstrationCount);
public sealed record QuizResult(QuizSession Session,double Percentage,int Correct,int Partial,int Incorrect,int Unanswered,
    IReadOnlyList<PerformanceGroup> Difficulties);
public interface IStudyPerformanceRepository
{
    Task<IReadOnlyList<HistoryAttempt>> HistoryAsync(Guid projectId,int offset,int limit,CancellationToken ct=default);
    Task<IReadOnlyList<QuizSession>> CompletedAsync(Guid projectId,DateTimeOffset asOf,PerformanceCursor? cursor,int limit,CancellationToken ct=default);
}
