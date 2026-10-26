using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Domain.Common;
using StudySpace.Domain.Sources;
namespace StudySpace.Application.Quizzes;

public sealed class QuizService(IAiStudyService ai,IStudyContextRepository context,IQuizRepository repository,ProjectService projects,IClock clock)
{
    public AiProviderInfo Provider=>ai.Provider;
    public async Task<Guid?> LatestInProgressAsync(Guid projectId,CancellationToken ct=default)
    {await projects.GetAsync(projectId,ct);return await repository.LatestInProgressAsync(projectId,ct);}
    public static QuizConfiguration Validate(QuizConfiguration config)
    {
        if(!Enum.IsDefined(config.Mode)||config.Count is <1 or >50 || config.Mode==QuizMode.Mixed && config.Count<2)
            throw new StudyValidationException("Scegli da 1 a 50 domande; per un quiz misto ne servono almeno due.");
        if(config.Difficulty.HasValue&&!Enum.IsDefined(config.Difficulty.Value)) throw new StudyValidationException("Difficoltà non valida.");
        var filters=config.Filters;
        if((filters?.TopicIds?.Count??0)>0||(filters?.TagIds?.Count??0)>0)
            throw new StudyValidationException("I filtri per argomenti e tag saranno disponibili in seguito.");
        if(filters?.SourceIds is { } ids && (ids.Count>20||ids.Any(id=>id==Guid.Empty)||ids.Distinct().Count()!=ids.Count))
            throw new StudyValidationException("Seleziona al massimo 20 fonti distinte.");
        return config;
    }
    public async Task<Guid> GenerateAsync(Guid projectId,Guid operationId,QuizConfiguration configuration,IReadOnlyList<Guid> selectedIds,CancellationToken ct=default)
    {
        Validate(configuration);
        if(operationId==Guid.Empty) throw new StudyValidationException("Operazione non valida.");
        if(selectedIds.Count is <1 or >20 || selectedIds.Distinct().Count()!=selectedIds.Count)
            throw new StudyValidationException("Seleziona da 1 a 20 parti delle fonti del progetto.");
        var project=await projects.GetAsync(projectId,ct);
        var chunks=await context.GetChunksAsync(projectId,selectedIds,ct);
        if(chunks.Count!=selectedIds.Count||chunks.Any(c=>!c.IsAvailable)||
           chunks.Sum(c=>c.Chunk.Text.Length)>40000)
            throw new StudyValidationException("Usa parti di fonti pronte e attive del progetto, per un massimo di 40.000 caratteri.");
        if(configuration.Filters?.SourceIds is {Count:>0} sources && chunks.Any(c=>!sources.Contains(c.Chunk.SourceId)))
            throw new StudyValidationException("Il materiale non corrisponde al filtro fonti.");
        AiQuizDraft draft;
        try { draft=await ai.GenerateQuizAsync(new(project,configuration,chunks),ct); }
        catch(OperationCanceledException){throw;} catch(AiStudyException){throw;}
        catch(Exception){throw new AiStudyException("Generazione non riuscita. I tentativi già salvati restano disponibili.");}
        var questions=ValidateDraft(configuration,chunks,draft);
        return await repository.CreateAsync(new(operationId,projectId,configuration,ai.Provider,clock.UtcNow,selectedIds.ToArray(),questions),ct);
    }
    public static IReadOnlyList<QuizQuestionSnapshot> ValidateDraft(QuizConfiguration config,IReadOnlyList<StudyChunk> chunks,AiQuizDraft draft)
    {
        if(draft?.Questions is null || draft.Questions.Count!=config.Count || draft.Questions.Any(q=>q is null)) throw new AiStudyException("Il materiale non ha prodotto il numero di domande richiesto. Riduci il numero o aggiungi fonti.");
        var closed=config.Mode switch { QuizMode.Closed=>config.Count,QuizMode.Open=>0,_=>(config.Count+1)/2 };
        if(draft.Questions.Count(q=>q.Kind==QuestionKind.Closed)!=closed || draft.Questions.Any(q=>!Enum.IsDefined(q.Kind)))
            throw new AiStudyException("Il provider ha restituito una distribuzione dei tipi non valida.");
        if(config.Difficulty is null&&config.Count>1&&draft.Questions.Select(q=>q.Difficulty).Distinct().Count()<Math.Min(3,config.Count))
            throw new AiStudyException("La difficoltà mista richiede domande di difficoltà diverse.");
        var allowed=chunks.ToDictionary(c=>c.Chunk.Id); var seen=new HashSet<string>(); var result=new List<QuizQuestionSnapshot>();
        foreach(var q in draft.Questions)
        {
            bool Text(string? value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max;
            if(!Text(q.Prompt,2000)||!Text(q.Explanation,8000)||!Text(q.IdealAnswer,8000)||!Enum.IsDefined(q.Difficulty)||
                config.Difficulty.HasValue&&q.Difficulty!=config.Difficulty.Value ||!seen.Add(ProjectText.SearchKey(q.Prompt)))
                throw new AiStudyException("Il provider ha restituito domande vuote, duplicate o non valide.");
            if(q.References is null||q.References.Count is <1 or >20||q.References.Select(r=>r.ChunkId).Distinct().Count()!=q.References.Count||
               q.References.Any(r=>!allowed.TryGetValue(r.ChunkId,out var c)||!Text(r.Quote,4000)||!c.Chunk.Text.Contains(r.Quote,StringComparison.Ordinal)))
                throw new AiStudyException("La provenienza di una domanda non corrisponde al materiale fornito.");
            if(q.Options is null || (q.Kind==QuestionKind.Closed
               ? q.Options.Count is <2 or >6||q.Options.Count(o=>o.IsCorrect)!=1||q.Options.Any(o=>!Text(o.Text,2000))||q.Options.Select(o=>ProjectText.SearchKey(o.Text)).Distinct().Count()!=q.Options.Count
               : q.Options.Count!=0)) throw new AiStudyException("Opzioni della domanda non valide.");
            if(q.Rubric is null || q.Kind==QuestionKind.Closed&&q.Rubric.Count!=0 || q.Kind==QuestionKind.Open&&(q.Rubric.Count is <1 or >10 ||
                q.Rubric.Any(r=>!Text(r.Id,80)||!Text(r.Concept,1000)||!double.IsFinite(r.Weight)||r.Weight<=0||
                    r.ChunkIds is null||r.ChunkIds.Count is <1 or >20||r.ChunkIds.Distinct().Count()!=r.ChunkIds.Count||r.ChunkIds.Any(id=>!q.References.Any(c=>c.ChunkId==id)))||
                q.Rubric.Select(r=>r.Id).Distinct().Count()!=q.Rubric.Count||Math.Abs(q.Rubric.Sum(r=>r.Weight)-1)>0.000001))
                throw new AiStudyException("Rubrica della domanda aperta non valida.");
            var citations=q.References.Select(r=>{var c=allowed[r.ChunkId];return new SourceCitation(c.Chunk.SourceId,c.Chunk.SourceRevisionId,c.Chunk.Id,r.Quote,c.SourceTitle,FlashcardService.Locator(c.Chunk));}).ToArray();
            result.Add(new(Guid.NewGuid(),result.Count,q.Kind,q.Prompt.Trim(),q.Explanation.Trim(),q.IdealAnswer.Trim(),q.Difficulty,
                q.Options.Select(o=>new QuizOptionSnapshot(Guid.NewGuid(),o.Text.Trim(),o.IsCorrect)).ToArray(),q.Rubric.ToArray(),citations,
                Topics:citations.Select(c=>allowed[c.ChunkId].Chunk).Where(c=>!string.IsNullOrWhiteSpace(c.SectionPath))
                    .GroupBy(c=>ProjectText.SearchKey(c.SectionPath!)).Select(g=>new QuizTopicSnapshot(g.First().SectionPath!,g.Select(c=>c.Id).ToArray())).ToArray()));
        }
        return result;
    }
    public Task<IReadOnlyList<QuizAttemptSummary>> ListAsync(Guid projectId,int offset=0,CancellationToken ct=default)=>repository.ListAsync(projectId,offset,30,ct);
}

