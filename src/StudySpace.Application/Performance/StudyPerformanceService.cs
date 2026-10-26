using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
namespace StudySpace.Application.Performance;

// Read-only projections of historical evidence. This service has no AI dependency.
public sealed class StudyPerformanceService(IStudyPerformanceRepository repository,IQuizRepository quizzes,ProjectService projects,IClock clock)
{
    public async Task<IReadOnlyList<HistoryAttempt>> HistoryAsync(Guid projectId,int offset=0,CancellationToken ct=default)
    {
        await projects.GetAsync(projectId,ct);ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return await repository.HistoryAsync(projectId,offset,30,ct);
    }
    public async Task<QuizResult> ResultAsync(Guid projectId,Guid attemptId,CancellationToken ct=default)
    {
        await projects.GetAsync(projectId,ct);
        var session=await quizzes.GetAsync(projectId,attemptId,ct)??throw new QuizNotFoundException();
        if(session.Attempt.Status!=AttemptStatus.Completed||!session.Attempt.Score.HasValue)
            throw new StudyValidationException("Il risultato finale sarà disponibile al completamento delle valutazioni. Riprendi il tentativo dallo storico.");
        var performance=await AnalyzeAsync(projectId,session.Attempt.CompletedAtUtc??session.Attempt.StartedAtUtc,ct);
        return BuildResult(session,performance.Difficulties);
    }
    public static QuizResult BuildResult(QuizSession session,IReadOnlyList<PerformanceGroup> groups)
    {
        var answers=session.Answers;
        return new(session,100*(session.Attempt.Score??0)/session.Questions.Count,
            answers.Count(a=>a.IsAnswered&&a.Feedback?.Outcome==AnswerOutcome.Correct),
            answers.Count(a=>a.IsAnswered&&a.Feedback?.Outcome==AnswerOutcome.Partial),
            answers.Count(a=>a.IsAnswered&&a.Feedback?.Outcome==AnswerOutcome.Incorrect),answers.Count(a=>!a.IsAnswered),groups);
    }
    public async Task<ProjectPerformance> StatisticsAsync(Guid projectId,CancellationToken ct=default)
    {await projects.GetAsync(projectId,ct);return await AnalyzeAsync(projectId,clock.UtcNow,ct);}
    private async Task<ProjectPerformance> AnalyzeAsync(Guid projectId,DateTimeOffset asOf,CancellationToken ct)
    {
        var aggregate=new PerformanceAccumulator();PerformanceCursor? cursor=null;
        while(true)
        {
            var page=await repository.CompletedAsync(projectId,asOf,cursor,20,ct).ConfigureAwait(false);
            foreach(var session in page){ct.ThrowIfCancellationRequested();aggregate.Add(session);}
            if(page.Count<20)break;
            var last=page[^1].Attempt;cursor=new(last.CompletedAtUtc??last.StartedAtUtc,last.Id);
        }
        return aggregate.Build();
    }
    public static HistoryAttempt Summarize(QuizSession s)=>new(s.Attempt.Id,s.Title,s.Attempt.StartedAtUtc,s.Attempt.Mode,
        s.Attempt.Difficulty,s.MixedDifficulty,s.Attempt.Status,s.Questions.Count,s.Attempt.Score,s.Attempt.ActiveDurationMs,s.IsMock);
}
