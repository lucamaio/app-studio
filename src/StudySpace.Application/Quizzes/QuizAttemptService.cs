using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Domain.Common;
namespace StudySpace.Application.Quizzes;

public sealed class QuizAttemptService(IQuizRepository repository,IAiStudyService ai,IClock clock,EvaluationPolicy? policy=null)
{
    public async Task<QuizSession> GetAsync(Guid projectId,Guid attemptId,CancellationToken ct=default)=>
        await repository.GetAsync(projectId,attemptId,ct) ?? throw new QuizNotFoundException();
    public async Task<QuizSession> ResumeAsync(Guid projectId,Guid attemptId,CancellationToken ct=default)
    {
        await repository.RecoverAsync(projectId,attemptId,clock.UtcNow,ct);
        return await GetAsync(projectId,attemptId,ct);
    }
    public Task<QuizAnswerState> SaveOpenAsync(Guid p,Guid a,Guid q,string text,int revision,CancellationToken ct=default)
    {
        if(text.Length>8000) throw new StudyValidationException("La risposta può contenere al massimo 8.000 caratteri.");
        return repository.SaveOpenAsync(p,a,q,text,revision,clock.UtcNow,ct);
    }
    public Task<QuizAnswerState> AnswerClosedAsync(Guid p,Guid a,Guid q,Guid option,int revision,CancellationToken ct=default)=>
        repository.AnswerClosedAsync(p,a,q,option,revision,clock.UtcNow,ct);
    public async Task EvaluateAsync(Guid p,Guid a,Guid q,CancellationToken ct=default)
    {
        var claim=await repository.ClaimAsync(p,a,q,clock.UtcNow,ct);
        if(claim is null) return;
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
            var timeout=(policy??EvaluationPolicy.Default).Timeout;
            deadline.CancelAfter(timeout);
            var provider=ai.EvaluationProvider;
            OpenAnswerEvaluation assessment;
            try
            {
                var task=ai.EvaluateOpenAnswerAsync(new(p,a,q,claim.Answer.Revision,claim.Question,claim.Answer.Text),deadline.Token);
                assessment=await task.WaitAsync(timeout,ct);
            }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new TimeoutException(); }
            var feedback=ValidateAssessment(claim.Question,assessment,provider);
            await repository.AcceptAsync(p,a,claim,feedback,clock.UtcNow,ct);
        }
        catch(Exception ex)
        {
            var message=ex is TimeoutException ? "Il modello sta impiegando troppo tempo. La risposta è salvata: riprova." :
                ex is HttpRequestException ? "Connessione al modello non disponibile. La risposta è salvata: riprova." :
                ex is OperationCanceledException ? "Verifica interrotta. Puoi riprovare." :
                ex is AiStudyException ? ex.Message : "Valutazione non disponibile. La risposta è salvata: puoi riprovare.";
            await repository.FailAsync(p,a,claim,message,clock.UtcNow,CancellationToken.None);
            if(ex is OperationCanceledException) throw;
            throw new AiStudyException(message);
        }
    }
    public static AnswerFeedback ValidateAssessment(QuizQuestionSnapshot question,OpenAnswerEvaluation assessment,AiProviderInfo provider)
    {
        var score=OpenAnswerEvaluationValidator.Validate(question,assessment);
        return new(score>=0.8?AnswerOutcome.Correct:score>0?AnswerOutcome.Partial:AnswerOutcome.Incorrect,score,
            assessment.Summary,assessment.IdealAnswer,assessment.CorrectConcepts.ToArray(),assessment.MissingConcepts.ToArray(),
            assessment.IncorrectConcepts.ToArray(),string.Join("\n",assessment.Suggestions),assessment.Criteria.ToArray(),provider.Key,provider.IsMock,2,
            new(assessment with {Score=score*100},provider.Key,"semantic-evaluation-v1"));
    }
    public async Task<QuizSession> FinishAsync(Guid p,Guid a,CancellationToken ct=default)
    {
        await repository.SubmitAsync(p,a,clock.UtcNow,ct);
        var session=await GetAsync(p,a,ct);
        if(session.Attempt.Status is AttemptStatus.Completed or AttemptStatus.Abandoned) return session;
        foreach(var q in session.Questions.Where(q=>q.Kind==QuestionKind.Open))
        {
            var answer=session.Answers.Single(x=>x.QuestionId==q.Id);
            if(!answer.IsAnswered||answer.Status==EvaluationStatus.Evaluated) continue;
            try { await EvaluateAsync(p,a,q.Id,ct); } catch(AiStudyException){ break; /* Avoid repeating a failing service call for every remaining answer. */ }
        }
        await repository.CompleteAsync(p,a,clock.UtcNow,ct);
        return await GetAsync(p,a,ct);
    }
    public Task AbandonAsync(Guid p,Guid a,CancellationToken ct=default)=>repository.AbandonAsync(p,a,clock.UtcNow,ct);
    public Task CheckpointAsync(Guid p,Guid a,int position,long activeMs,CancellationToken ct=default)=>repository.CheckpointAsync(p,a,position,activeMs,clock.UtcNow,ct);
    public Task<int> PositionAsync(Guid p,Guid a,CancellationToken ct=default)=>repository.GetPositionAsync(p,a,ct);
}


