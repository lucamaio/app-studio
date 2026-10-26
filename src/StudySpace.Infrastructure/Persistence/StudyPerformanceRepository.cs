using StudySpace.Application.Performance;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
namespace StudySpace.Infrastructure.Persistence;

public sealed class StudyPerformanceRepository(StudyDatabase database):IStudyPerformanceRepository
{
    public Task<IReadOnlyList<HistoryAttempt>> HistoryAsync(Guid projectId,int offset,int limit,CancellationToken ct=default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);if(limit is <1 or >100)throw new ArgumentOutOfRangeException(nameof(limit));
        return database.ExecuteAsync<IReadOnlyList<HistoryAttempt>>(db=>db.Query<PerformanceHistoryRow>("""
            SELECT a.Id,a.StartedAtUtc,a.Mode,a.Difficulty,a.Status,a.MaximumScore,a.Score,a.ActiveDurationMs,
                d.Title,d.MixedDifficulty,d.IsMock
            FROM QuizAttempt a JOIN QuizAttemptDetails d ON d.ProjectId=a.ProjectId AND d.AttemptId=a.Id
            WHERE a.ProjectId=? ORDER BY a.StartedAtUtc DESC,a.Id DESC LIMIT ? OFFSET ?
            """,projectId.ToString(),limit,offset).Select(r=>r.ToDomain()).ToArray(),ct);
    }
    public Task<IReadOnlyList<QuizSession>> CompletedAsync(Guid projectId,DateTimeOffset asOf,PerformanceCursor? cursor,int limit,CancellationToken ct=default)
    {
        if(limit is <1 or >50)throw new ArgumentOutOfRangeException(nameof(limit));
        return database.ExecuteAsync<IReadOnlyList<QuizSession>>(db=>
        {
            var args=new List<object>{projectId.ToString(),asOf.ToUniversalTime().ToString("O")};
            var filter="";
            if(cursor is not null)
            {
                filter=" AND (CompletedAtUtc<? OR (CompletedAtUtc=? AND Id<?))";
                args.Add(cursor.CompletedAtUtc.ToUniversalTime().ToString("O"));args.Add(cursor.CompletedAtUtc.ToUniversalTime().ToString("O"));args.Add(cursor.Id.ToString());
            }
            args.Add(limit);
            return db.Query<QuizAttemptRow>("SELECT * FROM QuizAttempt WHERE ProjectId=? AND Status=2 AND CompletedAtUtc<=?"+filter+" ORDER BY CompletedAtUtc DESC,Id DESC LIMIT ?",args.ToArray())
                .Select(r=>QuizRepository.Read(db,projectId,Guid.Parse(r.Id))??throw new InvalidOperationException("Attempt metadata missing.")).ToArray();
        },ct);
    }
}
public sealed class PerformanceHistoryRow
{
    public string Id {get;set;}="";public string StartedAtUtc {get;set;}="";public string Title {get;set;}="";
    public int Mode {get;set;}public int Difficulty {get;set;}public int Status {get;set;}public int MaximumScore {get;set;}
    public double? Score {get;set;}public long ActiveDurationMs {get;set;}public bool MixedDifficulty {get;set;}public bool IsMock {get;set;}
    public HistoryAttempt ToDomain()=>new(Guid.Parse(Id),Title,DateTimeOffset.Parse(StartedAtUtc),(QuizMode)Mode,(Difficulty)Difficulty,
        MixedDifficulty,(AttemptStatus)Status,MaximumScore,Score,ActiveDurationMs,IsMock);
}
public sealed class PerformanceTagRow
{
    public string TagId {get;set;}="";public string Name {get;set;}="";public string SourceId {get;set;}="";
}
