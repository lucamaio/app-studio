using System.Text.Json;
using SQLite;
using StudySpace.Application.Quizzes;
using StudySpace.Application.Common;
using StudySpace.Domain.Common;
using StudySpace.Domain.Quizzes;
namespace StudySpace.Infrastructure.Persistence;

public sealed class QuizRepository(StudyDatabase database):IQuizRepository
{
    public Task<Guid?> LatestInProgressAsync(Guid projectId,CancellationToken ct=default)=>database.ExecuteAsync<Guid?>(db=>
    {
        var row=db.FindWithQuery<QuizAttemptRow>("SELECT * FROM QuizAttempt WHERE ProjectId=? AND Status=0 ORDER BY UpdatedAtUtc DESC,Id DESC LIMIT 1",projectId.ToString());
        return row is null?null:Guid.Parse(row.Id);
    },ct);
    public Task<Guid> CreateAsync(QuizStartData quiz,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        var existing=FindAttempt(db,quiz.ProjectId,quiz.OperationId);
        if(existing is not null) return quiz.OperationId;
        if(quiz.Questions.Count!=quiz.Configuration.Count) throw new StudyValidationException("Quiz incompleto.");
        db.RunInTransaction(()=>
        {
            var chunks=StudyContextRepository.ReadChunks(db,quiz.ProjectId,quiz.InputChunkIds);
            if(chunks.Count!=quiz.InputChunkIds.Count||chunks.Any(c=>!c.IsAvailable)) throw new StudyValidationException("Le fonti sono cambiate. Genera nuovamente il quiz.");
            var byId=chunks.ToDictionary(c=>c.Chunk.Id);
            var p=quiz.ProjectId.ToString(); var id=quiz.OperationId.ToString(); var now=Utc(quiz.CreatedAtUtc);
            db.Execute("INSERT INTO Quiz(Id,ProjectId,Title,Mode,RequestedCount,Difficulty,CreatedAtUtc,UpdatedAtUtc) VALUES(?,?,?,?,?,?,?,?)",
                id,p,"Quiz di studio",quiz.Configuration.Mode,quiz.Configuration.Count,quiz.Configuration.Difficulty??Difficulty.Medium,now,now);
            db.Execute("INSERT INTO QuizDetails VALUES(?,?,?,?,?,?,?,?)",p,id,quiz.Provider.Key,quiz.Provider.IsMock,quiz.Configuration.Difficulty is null,
                JsonSerializer.Serialize(quiz.InputChunkIds.ToArray(),QuizJsonContext.Default.GuidArray),
                JsonSerializer.Serialize(quiz.Configuration.Filters??new(),QuizJsonContext.Default.QuizFilters),"quiz-v1");
            db.Execute("INSERT INTO QuizAttempt(Id,ProjectId,QuizId,Mode,Difficulty,StartedAtUtc,MaximumScore,CreatedAtUtc,UpdatedAtUtc) VALUES(?,?,?,?,?,?,?,?,?)",
                id,p,id,quiz.Configuration.Mode,quiz.Configuration.Difficulty??Difficulty.Medium,now,quiz.Questions.Count,now,now);
            db.Execute("INSERT INTO QuizAttemptDetails(ProjectId,AttemptId,Title,ProviderKey,IsMock,MixedDifficulty) VALUES(?,?,?,?,?,?)",
                p,id,"Quiz di studio",quiz.Provider.Key,quiz.Provider.IsMock,quiz.Configuration.Difficulty is null);
            var projectTags=db.Query<PerformanceTagRow>("SELECT t.Id AS TagId,t.Name FROM ProjectTag p JOIN Tag t ON t.ProjectId=p.ProjectId AND t.Id=p.TagId WHERE p.ProjectId=?",p);
            var sourceIds=chunks.Select(c=>c.Chunk.SourceId.ToString()).Distinct().ToArray();
            var sourceTags=db.Query<PerformanceTagRow>($"SELECT t.Id AS TagId,t.Name,s.SourceId FROM SourceTag s JOIN Tag t ON t.ProjectId=s.ProjectId AND t.Id=s.TagId WHERE s.ProjectId=? AND s.SourceId IN ({string.Join(",",sourceIds.Select(_=>"?"))})",new object[]{p}.Concat(sourceIds.Cast<object>()).ToArray());
            foreach(var original in quiz.Questions)
            {
                var tags=projectTags.Concat(sourceTags.Where(t=>original.Citations.Any(c=>c.SourceId.ToString()==t.SourceId))).GroupBy(t=>t.TagId)
                    .Select(g=>new QuizTagSnapshot(Guid.Parse(g.Key),g.First().Name,original.Citations.Where(c=>g.Any(t=>t.SourceId.Length==0||t.SourceId==c.SourceId.ToString())).Select(c=>c.ChunkId).Distinct().ToArray())).ToArray();
                var q=original with{Tags=tags};
                if(q.Citations.Count==0||q.Citations.Any(c=>!byId.TryGetValue(c.ChunkId,out var source)||
                    source.Chunk.SourceId!=c.SourceId||source.Chunk.SourceRevisionId!=c.SourceRevisionId||!source.Chunk.Text.Contains(c.Quote,StringComparison.Ordinal)))
                    throw new StudyValidationException("Provenienza della domanda non valida.");
                var qid=q.Id.ToString();
                db.Execute("INSERT INTO QuizQuestion(Id,ProjectId,QuizId,Position,Kind,Prompt,Explanation,IdealAnswer,RubricJson,Difficulty,CreatedAtUtc,UpdatedAtUtc) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                    qid,p,id,q.Position,q.Kind,q.Prompt,q.Explanation,q.IdealAnswer,
                    JsonSerializer.Serialize(q.Rubric,QuizJsonContext.Default.IReadOnlyListRubricCriterion),q.Difficulty,now,now);
                for(var n=0;n<q.Options.Count;n++)
                {
                    var o=q.Options[n];
                    db.Execute("INSERT INTO QuestionOption(Id,ProjectId,QuestionId,Position,Text,IsCorrect,CreatedAtUtc,UpdatedAtUtc) VALUES(?,?,?,?,?,?,?,?)",
                        o.Id.ToString(),p,qid,n,o.Text,o.IsCorrect,now,now);
                }
                db.Execute("INSERT INTO AttemptQuestion(Id,ProjectId,AttemptId,OriginalQuestionId,Position,SnapshotJson,CreatedAtUtc,UpdatedAtUtc) VALUES(?,?,?,?,?,?,?,?)",
                    qid,p,id,qid,q.Position,JsonSerializer.Serialize(q,QuizJsonContext.Default.QuizQuestionSnapshot),now,now);
                db.Execute("INSERT INTO QuizAnswer(Id,ProjectId,AttemptQuestionId,CreatedAtUtc,UpdatedAtUtc) VALUES(?,?,?,?,?)",Guid.NewGuid().ToString(),p,qid,now,now);
                foreach(var c in q.Citations)
                {
                    db.Execute("INSERT INTO QuestionCitation VALUES(?,?,?)",p,qid,c.ChunkId.ToString());
                    db.Execute("INSERT INTO AttemptCitation VALUES(?,?,?,?,?,?)",p,qid,c.ChunkId.ToString(),c.Quote,c.SourceTitle,c.Locator);
                }
            }
        });
        return quiz.OperationId;
    },ct);
    public Task<IReadOnlyList<QuizAttemptSummary>> ListAsync(Guid projectId,int offset,int limit,CancellationToken ct=default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset); if(limit is <1 or >100) throw new ArgumentOutOfRangeException(nameof(limit));
        return database.ExecuteAsync<IReadOnlyList<QuizAttemptSummary>>(db=>db.Query<QuizAttemptListRow>("""
            SELECT a.Id,d.Title,a.Status,a.StartedAtUtc,a.MaximumScore AS Count,a.Score,d.IsMock,
            (SELECT COUNT(*) FROM QuizAnswer b JOIN AttemptQuestion q ON q.ProjectId=b.ProjectId AND q.Id=b.AttemptQuestionId
             WHERE q.ProjectId=a.ProjectId AND q.AttemptId=a.Id AND b.AnsweredAtUtc IS NOT NULL) AS AnsweredCount
            FROM QuizAttempt a JOIN QuizAttemptDetails d ON d.ProjectId=a.ProjectId AND d.AttemptId=a.Id
            WHERE a.ProjectId=? ORDER BY a.StartedAtUtc DESC,a.Id LIMIT ? OFFSET ?
            """,projectId.ToString(),limit,offset).Select(r=>new QuizAttemptSummary(Guid.Parse(r.Id),r.Title,(AttemptStatus)r.Status,
                DateTimeOffset.Parse(r.StartedAtUtc),r.Count,r.AnsweredCount,r.Score,r.IsMock)).ToArray(),ct);
    }
    public Task<QuizSession?> GetAsync(Guid p,Guid a,CancellationToken ct=default)=>database.ExecuteAsync(db=>Read(db,p,a),ct);
    internal static QuizSession? Read(SQLiteConnection db,Guid p,Guid a)
    {
        var row=FindAttempt(db,p,a); if(row is null) return null;
        var details=db.FindWithQuery<QuizAttemptDetailsRow>("SELECT * FROM QuizAttemptDetails WHERE ProjectId=? AND AttemptId=?",p.ToString(),a.ToString());
        if(details is null) return null;
        var questions=db.Query<QuizSnapshotRow>("SELECT SnapshotJson FROM AttemptQuestion WHERE ProjectId=? AND AttemptId=? ORDER BY Position",p.ToString(),a.ToString())
            .Select(r=>JsonSerializer.Deserialize(r.SnapshotJson,QuizJsonContext.Default.QuizQuestionSnapshot)??throw new InvalidOperationException("Snapshot missing.")).ToArray();
        if(questions.Length==0||questions.Any(q=>q.SchemaVersion!=1)) throw new InvalidOperationException("Unsupported quiz snapshot.");
        var answers=db.Query<QuizAnswerRow>(AnswerSelect+" WHERE b.ProjectId=? AND q.AttemptId=? ORDER BY q.Position",p.ToString(),a.ToString()).Select(r=>r.ToDomain()).ToArray();
        return new(row.ToDomain(),details.Title,details.IsMock,details.ProviderKey,details.MixedDifficulty,questions,answers);
    }
    public Task RecoverAsync(Guid p,Guid a,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        RequireAttempt(db,p,a);
        db.Execute("""
            UPDATE QuizAnswer SET EvaluationStatus=4,EvaluationToken=NULL,EvaluationError=?,UpdatedAtUtc=?
            WHERE ProjectId=? AND EvaluationStatus=2 AND AttemptQuestionId IN(SELECT Id FROM AttemptQuestion WHERE ProjectId=? AND AttemptId=?)
            ""","La verifica è stata interrotta. Premi Verifica ora o Riprova valutazioni.",Utc(now),p.ToString(),p.ToString(),a.ToString()); return 0;
    },ct);
    public Task<QuizAnswerState> SaveOpenAsync(Guid p,Guid a,Guid q,string text,int expectedRevision,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        QuizAnswerState? result=null;
        db.RunInTransaction(()=>
        {
            var attempt=RequireAttempt(db,p,a); RequireInProgress(attempt);
            var snapshot=Question(db,p,a,q); if(snapshot.Kind!=QuestionKind.Open) throw new StudyValidationException("Questa domanda non è aperta.");
            var answer=Answer(db,p,a,q);
            if(answer.Revision!=expectedRevision||answer.IsLocked) throw new QuizConflictException();
            if(text==answer.Text){result=answer;return;}
            Changed(db.Execute("UPDATE QuizAnswer SET Text=?,SelectedOptionId=NULL,AnswerRevision=AnswerRevision+1,Revision=Revision+1,AnsweredAtUtc=?,EvaluationStatus=0,EvaluationToken=NULL,EvaluationError=NULL,UpdatedAtUtc=? WHERE ProjectId=? AND AttemptQuestionId=? AND AnswerRevision=? AND EvaluationStatus NOT IN(2,3)",
                text,string.IsNullOrWhiteSpace(text)?null:Utc(now),Utc(now),p.ToString(),q.ToString(),expectedRevision));
            Touch(db,p,a,now); result=Answer(db,p,a,q);
        }); return result!;
    },ct);
    public Task<QuizAnswerState> AnswerClosedAsync(Guid p,Guid a,Guid q,Guid optionId,int expectedRevision,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        QuizAnswerState? result=null;
        db.RunInTransaction(()=>
        {
            RequireInProgress(RequireAttempt(db,p,a)); var snapshot=Question(db,p,a,q);
            if(snapshot.Kind!=QuestionKind.Closed) throw new StudyValidationException("Questa domanda non è chiusa.");
            var selected=snapshot.Options.SingleOrDefault(o=>o.Id==optionId)??throw new StudyValidationException("Opzione non valida per questa domanda.");
            var answer=Answer(db,p,a,q);
            if(answer.Status==EvaluationStatus.Evaluated&&answer.SelectedOptionId==optionId){result=answer;return;}
            if(answer.IsLocked||answer.Revision!=expectedRevision) throw new QuizConflictException();
            Changed(db.Execute("UPDATE QuizAnswer SET SelectedOptionId=?,Text=NULL,AnswerRevision=AnswerRevision+1,Revision=Revision+1,EvaluationStatus=3,EvaluationError=NULL,AnsweredAtUtc=?,UpdatedAtUtc=? WHERE ProjectId=? AND AttemptQuestionId=? AND AnswerRevision=? AND EvaluationStatus NOT IN(2,3)",
                optionId.ToString(),Utc(now),Utc(now),p.ToString(),q.ToString(),expectedRevision));
            var feedback=new AnswerFeedback(selected.IsCorrect?AnswerOutcome.Correct:AnswerOutcome.Incorrect,selected.IsCorrect?1:0,
                snapshot.Explanation,snapshot.Options.Single(o=>o.IsCorrect).Text,[],[],[],"",[],"local-closed-v1",false);
            InsertEvaluation(db,p,q,expectedRevision+1,feedback,now); Touch(db,p,a,now); result=Answer(db,p,a,q);
        });return result!;
    },ct);
    public Task<EvaluationClaim?> ClaimAsync(Guid p,Guid a,Guid q,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync<EvaluationClaim?>(db=>
    {
        EvaluationClaim? claim=null; db.RunInTransaction(()=>
        {
            var attempt=RequireAttempt(db,p,a);
            if((AttemptStatus)attempt.Status is not (AttemptStatus.InProgress or AttemptStatus.SubmittedPendingEvaluation)) return;
            var snapshot=Question(db,p,a,q); if(snapshot.Kind!=QuestionKind.Open) throw new StudyValidationException("Questa domanda non è aperta.");
            var answer=Answer(db,p,a,q);
            if(!answer.IsAnswered) throw new StudyValidationException("Scrivi una risposta prima di verificarla.");
            if(answer.Status is EvaluationStatus.Evaluated or EvaluationStatus.Evaluating) return;
            var token=Guid.NewGuid();
            Changed(db.Execute("UPDATE QuizAnswer SET EvaluationStatus=2,EvaluationToken=?,EvaluationError=NULL,UpdatedAtUtc=? WHERE ProjectId=? AND AttemptQuestionId=? AND AnswerRevision=? AND EvaluationStatus NOT IN(2,3)",
                token.ToString(),Utc(now),p.ToString(),q.ToString(),answer.Revision));
            claim=new(snapshot,answer,token);
        }); return claim;
    },ct);
    public Task<bool> AcceptAsync(Guid p,Guid a,EvaluationClaim claim,AnswerFeedback feedback,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        var accepted=false; db.RunInTransaction(()=>
        {
            var attempt=RequireAttempt(db,p,a);
            if((AttemptStatus)attempt.Status is not (AttemptStatus.InProgress or AttemptStatus.SubmittedPendingEvaluation)) return;
            Question(db,p,a,claim.Question.Id);
            var n=db.Execute("UPDATE QuizAnswer SET EvaluationStatus=3,EvaluationToken=NULL,EvaluationError=NULL,UpdatedAtUtc=? WHERE ProjectId=? AND AttemptQuestionId=? AND AnswerRevision=? AND EvaluationStatus=2 AND EvaluationToken=?",
                Utc(now),p.ToString(),claim.Question.Id.ToString(),claim.Answer.Revision,claim.Token.ToString());
            if(n==0) return;
            InsertEvaluation(db,p,claim.Question.Id,claim.Answer.Revision,feedback,now); Touch(db,p,a,now);accepted=true;
        });return accepted;
    },ct);
    public Task FailAsync(Guid p,Guid a,EvaluationClaim claim,string message,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        Question(db,p,a,claim.Question.Id);
        return db.Execute("UPDATE QuizAnswer SET EvaluationStatus=4,EvaluationToken=NULL,EvaluationError=?,UpdatedAtUtc=? WHERE ProjectId=? AND AttemptQuestionId=? AND AnswerRevision=? AND EvaluationStatus=2 AND EvaluationToken=?",
            message,Utc(now),p.ToString(),claim.Question.Id.ToString(),claim.Answer.Revision,claim.Token.ToString());
    },ct);
    public Task SubmitAsync(Guid p,Guid a,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        RequireAttempt(db,p,a);
        db.Execute("UPDATE QuizAttempt SET Status=1,SubmittedAtUtc=?,UpdatedAtUtc=?,Revision=Revision+1 WHERE ProjectId=? AND Id=? AND Status=0",Utc(now),Utc(now),p.ToString(),a.ToString());return 0;
    },ct);
    public Task<bool> CompleteAsync(Guid p,Guid a,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        var complete=false; db.RunInTransaction(()=>
        {
            var row=RequireAttempt(db,p,a); if((AttemptStatus)row.Status==AttemptStatus.Completed){complete=true;return;}
            if((AttemptStatus)row.Status!=AttemptStatus.SubmittedPendingEvaluation)return;
            var answers=db.Query<QuizAnswerRow>(AnswerSelect+" WHERE b.ProjectId=? AND q.AttemptId=?",p.ToString(),a.ToString()).Select(r=>r.ToDomain()).ToArray();
            if(answers.Any(b=>b.IsAnswered&&(b.Status!=EvaluationStatus.Evaluated||b.Feedback is null))) return;
            var score=answers.Sum(b=>b.Feedback?.Score??0);
            var scoringVersion=answers.Max(b=>b.Feedback?.ScoringVersion??1);
            Changed(db.Execute("UPDATE QuizAttempt SET Status=2,Score=?,ScoringVersion=?,CompletedAtUtc=?,UpdatedAtUtc=?,Revision=Revision+1 WHERE ProjectId=? AND Id=? AND Status=1",
                score,scoringVersion,Utc(now),Utc(now),p.ToString(),a.ToString())); complete=true;
        }); return complete;
    },ct);
    public Task AbandonAsync(Guid p,Guid a,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        db.RunInTransaction(()=>
        {
            var row=RequireAttempt(db,p,a);if((AttemptStatus)row.Status==AttemptStatus.Abandoned)return;
            RequireInProgress(row);
            db.Execute("UPDATE QuizAttempt SET Status=3,UpdatedAtUtc=?,Revision=Revision+1 WHERE ProjectId=? AND Id=? AND Status=0",Utc(now),p.ToString(),a.ToString());
            db.Execute("UPDATE QuizAnswer SET EvaluationToken=NULL,EvaluationStatus=4,EvaluationError=? WHERE ProjectId=? AND EvaluationStatus=2 AND AttemptQuestionId IN(SELECT Id FROM AttemptQuestion WHERE ProjectId=? AND AttemptId=?)",
                "Tentativo abbandonato.",p.ToString(),p.ToString(),a.ToString());
        });return 0;
    },ct);
    public Task CheckpointAsync(Guid p,Guid a,int position,long activeDurationMs,DateTimeOffset now,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        var row=RequireAttempt(db,p,a); if(position<0||position>=row.MaximumScore||activeDurationMs<0)throw new StudyValidationException("Checkpoint non valido.");
        db.RunInTransaction(()=>
        {
            db.Execute("UPDATE QuizAttemptDetails SET CurrentPosition=? WHERE ProjectId=? AND AttemptId=?",position,p.ToString(),a.ToString());
            db.Execute("UPDATE QuizAttempt SET ActiveDurationMs=MAX(ActiveDurationMs,?),UpdatedAtUtc=? WHERE ProjectId=? AND Id=? AND Status=0",activeDurationMs,Utc(now),p.ToString(),a.ToString());
        });return 0;
    },ct);
    public Task<int> GetPositionAsync(Guid p,Guid a,CancellationToken ct=default)=>database.ExecuteAsync(db=>
    {
        RequireAttempt(db,p,a);return db.ExecuteScalar<int>("SELECT CurrentPosition FROM QuizAttemptDetails WHERE ProjectId=? AND AttemptId=?",p.ToString(),a.ToString());
    },ct);
    private const string AnswerSelect="""
        SELECT b.*,e.FeedbackJson FROM QuizAnswer b
        JOIN AttemptQuestion q ON q.ProjectId=b.ProjectId AND q.Id=b.AttemptQuestionId
        LEFT JOIN AnswerEvaluation e ON e.ProjectId=b.ProjectId AND e.AttemptQuestionId=b.AttemptQuestionId AND e.AnswerRevision=b.AnswerRevision
        """;
    private static QuizAnswerState Answer(SQLiteConnection db,Guid p,Guid a,Guid q)=>
        db.FindWithQuery<QuizAnswerRow>(AnswerSelect+" WHERE b.ProjectId=? AND q.AttemptId=? AND b.AttemptQuestionId=?",p.ToString(),a.ToString(),q.ToString())?.ToDomain()??throw new QuizNotFoundException();
    private static QuizQuestionSnapshot Question(SQLiteConnection db,Guid p,Guid a,Guid q)
    {
        var row=db.FindWithQuery<QuizSnapshotRow>("SELECT SnapshotJson FROM AttemptQuestion WHERE ProjectId=? AND AttemptId=? AND Id=?",p.ToString(),a.ToString(),q.ToString())??throw new QuizNotFoundException();
        return JsonSerializer.Deserialize(row.SnapshotJson,QuizJsonContext.Default.QuizQuestionSnapshot)??throw new InvalidOperationException("Snapshot missing.");
    }
    private static QuizAttemptRow? FindAttempt(SQLiteConnection db,Guid p,Guid a)=>db.FindWithQuery<QuizAttemptRow>("SELECT * FROM QuizAttempt WHERE ProjectId=? AND Id=?",p.ToString(),a.ToString());
    private static QuizAttemptRow RequireAttempt(SQLiteConnection db,Guid p,Guid a)=>FindAttempt(db,p,a)??throw new QuizNotFoundException();
    private static void RequireInProgress(QuizAttemptRow a){if((AttemptStatus)a.Status!=AttemptStatus.InProgress)throw new QuizConflictException();}
    private static void Changed(int count){if(count!=1)throw new QuizConflictException();}
    private static string Utc(DateTimeOffset now)=>now.ToUniversalTime().ToString("O");
    private static void Touch(SQLiteConnection db,Guid p,Guid a,DateTimeOffset now)=>db.Execute("UPDATE QuizAttempt SET UpdatedAtUtc=?,Revision=Revision+1 WHERE ProjectId=? AND Id=?",Utc(now),p.ToString(),a.ToString());
    private static void InsertEvaluation(SQLiteConnection db,Guid p,Guid q,int revision,AnswerFeedback feedback,DateTimeOffset now)=>
        db.Execute("INSERT INTO AnswerEvaluation VALUES(?,?,?,?,?,?)",p.ToString(),q.ToString(),revision,feedback.Score,
            JsonSerializer.Serialize(feedback,QuizJsonContext.Default.AnswerFeedback),Utc(now));
}
public sealed class QuizSnapshotRow { public string SnapshotJson {get;set;}=""; }
public sealed class QuizAttemptDetailsRow
{
    public string Title {get;set;}=""; public string ProviderKey {get;set;}=""; public bool IsMock {get;set;} public bool MixedDifficulty {get;set;}
}
public sealed class QuizAttemptListRow
{
    public string Id {get;set;}=""; public string Title {get;set;}=""; public int Status {get;set;}
    public string StartedAtUtc {get;set;}=""; public int Count {get;set;} public int AnsweredCount {get;set;} public double? Score {get;set;} public bool IsMock {get;set;}
}
public sealed class QuizAttemptRow
{
    public string Id {get;set;}=""; public string ProjectId {get;set;}=""; public string? QuizId {get;set;}
    public int Mode {get;set;} public int Difficulty {get;set;} public int Status {get;set;} public int Revision {get;set;}
    public string CreatedAtUtc {get;set;}=""; public string UpdatedAtUtc {get;set;}=""; public string StartedAtUtc {get;set;}="";
    public string? SubmittedAtUtc {get;set;} public string? CompletedAtUtc {get;set;} public long ActiveDurationMs {get;set;}
    public double? Score {get;set;} public int MaximumScore {get;set;} public int ScoringVersion {get;set;}
    public QuizAttempt ToDomain()=>new(){Id=Guid.Parse(Id),ProjectId=Guid.Parse(ProjectId),QuizId=Guid.TryParse(QuizId,out var id)?id:null,
        Mode=(QuizMode)Mode,Difficulty=(StudySpace.Domain.Common.Difficulty)Difficulty,Status=(AttemptStatus)Status,Revision=Revision,
        CreatedAtUtc=DateTimeOffset.Parse(CreatedAtUtc),UpdatedAtUtc=DateTimeOffset.Parse(UpdatedAtUtc),StartedAtUtc=DateTimeOffset.Parse(StartedAtUtc),
        SubmittedAtUtc=SubmittedAtUtc is null?null:DateTimeOffset.Parse(SubmittedAtUtc),CompletedAtUtc=CompletedAtUtc is null?null:DateTimeOffset.Parse(CompletedAtUtc),
        ActiveDurationMs=ActiveDurationMs,Score=Score,MaximumScore=MaximumScore,ScoringVersion=ScoringVersion};
}
public sealed class QuizAnswerRow
{
    public string AttemptQuestionId {get;set;}=""; public string? Text {get;set;} public string? SelectedOptionId {get;set;}
    public int AnswerRevision {get;set;} public int EvaluationStatus {get;set;} public string? FeedbackJson {get;set;}
    public string? EvaluationToken {get;set;} public string? EvaluationError {get;set;}
    public QuizAnswerState ToDomain()=>new(Guid.Parse(AttemptQuestionId),Text??"",Guid.TryParse(SelectedOptionId,out var id)?id:null,AnswerRevision,
        (StudySpace.Domain.Common.EvaluationStatus)EvaluationStatus,FeedbackJson is null?null:JsonSerializer.Deserialize(FeedbackJson,QuizJsonContext.Default.AnswerFeedback),
        EvaluationError,Guid.TryParse(EvaluationToken,out var token)?token:null);
}

