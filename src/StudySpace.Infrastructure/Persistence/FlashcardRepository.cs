using SQLite;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Domain.Common;
using StudySpace.Domain.Flashcards;
using StudySpace.Domain.Sources;
namespace StudySpace.Infrastructure.Persistence;

public sealed class FlashcardRepository(StudyDatabase database):IFlashcardRepository
{
    private const string Select="""
        SELECT f.*,d.SourceId,s.Title AS SourceTitle,COALESCE(s.IsArchived,0) AS IsSourceArchived,
        d.GenerationRunId,r.ProviderKey AS GenerationProviderKey,COALESCE(r.IsMock,0) AS IsMockGenerated,
        COALESCE(d.IsUserEdited,0) AS IsUserEdited FROM FlashCard f
        LEFT JOIN FlashcardDetails d ON d.ProjectId=f.ProjectId AND d.FlashcardId=f.Id
        LEFT JOIN StudySource s ON s.ProjectId=d.ProjectId AND s.Id=d.SourceId
        LEFT JOIN GenerationRun r ON r.ProjectId=d.ProjectId AND r.Id=d.GenerationRunId
        
        """;
    private const string Filter="""
        WHERE f.ProjectId=? AND (?='' OR instr(f.SearchText,?)>0) AND (?=0 OR f.IsFavorite=1)
        AND (? IS NULL OR f.Difficulty=?) AND (?='' OR EXISTS(
          SELECT 1 FROM FlashcardTag ft JOIN Tag t ON t.ProjectId=ft.ProjectId AND t.Id=ft.TagId
          WHERE ft.ProjectId=f.ProjectId AND ft.FlashcardId=f.Id AND t.NormalizedName=?))
        
        """;
    private static object?[] QueryArgs(Guid projectId,FlashcardQuery query)
    {
        var search=ProjectText.SearchKey(query.Search);
        var tag=query.Tag is null ? "" : ProjectText.NormalizeTag(query.Tag);
        return [projectId.ToString(),search,search,query.FavoritesOnly,(int?)query.Difficulty,(int?)query.Difficulty,tag,tag];
    }
    public Task<IReadOnlyList<FlashCard>> SearchAsync(Guid projectId,FlashcardQuery query,int offset,int limit,CancellationToken ct=default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if(limit is <1 or >100) throw new ArgumentOutOfRangeException(nameof(limit));
        return database.ExecuteAsync<IReadOnlyList<FlashCard>>(db=>Hydrate(db,projectId,db.Query<FlashcardRow>(Select+Filter+
            "ORDER BY f.UpdatedAtUtc DESC,f.Id LIMIT ? OFFSET ?",QueryArgs(projectId,query).Concat(new object[] { limit,offset }).ToArray()!)),ct);
    }
    public Task<IReadOnlyList<Guid>> ListStudyIdsAsync(Guid projectId,FlashcardQuery query,CancellationToken ct=default)=>
        database.ExecuteAsync<IReadOnlyList<Guid>>(db=>db.Query<FlashcardRow>("SELECT f.Id FROM FlashCard f "+Filter+
            "ORDER BY f.UpdatedAtUtc DESC,f.Id",QueryArgs(projectId,query)!).Select(r=>Guid.Parse(r.Id)).ToArray(),ct);
    public Task<IReadOnlyList<string>> ListTagsAsync(Guid projectId,CancellationToken ct=default)=>
        database.ExecuteAsync<IReadOnlyList<string>>(db=>db.Query<FlashcardTagRow>("""
            SELECT MIN(t.Name) AS Name FROM Tag t JOIN FlashcardTag ft ON ft.ProjectId=t.ProjectId AND ft.TagId=t.Id
            WHERE t.ProjectId=? GROUP BY t.NormalizedName ORDER BY t.NormalizedName
            """,projectId.ToString()).Select(t=>t.Name).ToArray(),ct);
    public Task<FlashCard?> GetAsync(Guid projectId,Guid id,CancellationToken ct=default)=>
        database.ExecuteAsync(db=>Hydrate(db,projectId,db.Query<FlashcardRow>(Select+"WHERE f.ProjectId=? AND f.Id=?",projectId.ToString(),id.ToString())).SingleOrDefault(),ct);
    public async Task SaveAsync(FlashCard card,int? expectedRevision,CancellationToken ct=default)=>
        await database.ExecuteAsync(db=>
        {
            db.RunInTransaction(()=>
            {
                var existing=expectedRevision.HasValue ? db.FindWithQuery<FlashcardRow>(Select+"WHERE f.ProjectId=? AND f.Id=?",card.ProjectId.ToString(),card.Id.ToString()) : null;
                if(expectedRevision.HasValue && (existing is null || existing.Revision!=expectedRevision.Value || existing.GenerationRunId!=card.GenerationRunId?.ToString()))
                    throw new FlashcardConflictException();
                if(!expectedRevision.HasValue && card.GenerationRunId.HasValue) throw new StudyValidationException("Usa l'importazione dell'anteprima per le flashcard generate.");
                ValidateReferences(db,card,existing);
                WriteCard(db,card,expectedRevision);
            }); return 0;
        },ct);
    public async Task SetFavoriteAsync(Guid projectId,Guid id,int expectedRevision,bool favorite,DateTimeOffset now,CancellationToken ct=default)=>
        await database.ExecuteAsync(db=>
        {
            RequireChanged(db.Execute("UPDATE FlashCard SET IsFavorite=?,UpdatedAtUtc=?,Revision=Revision+1 WHERE ProjectId=? AND Id=? AND Revision=?",
                favorite,Utc(now),projectId.ToString(),id.ToString(),expectedRevision)); return 0;
        },ct);
    public async Task DeleteAsync(Guid projectId,Guid id,int expectedRevision,CancellationToken ct=default)=>
        await database.ExecuteAsync(db=>
        {
            db.RunInTransaction(()=>RequireChanged(db.Execute("DELETE FROM FlashCard WHERE ProjectId=? AND Id=? AND Revision=?",
                projectId.ToString(),id.ToString(),expectedRevision))); return 0;
        },ct);
    public async Task SaveGeneratedAsync(FlashcardGenerationRun run,string selectionHash,IReadOnlyList<FlashCard> cards,CancellationToken ct=default)=>
        await database.ExecuteAsync(db=>
        {
            db.RunInTransaction(()=>
            {
                var existing=db.ExecuteScalar<string?>("SELECT SelectionHash FROM GenerationRun WHERE ProjectId=? AND Id=?",run.ProjectId.ToString(),run.Id.ToString());
                if(existing is not null)
                { if(existing!=selectionHash) throw new FlashcardConflictException(); return; }
                var input=StudyContextRepository.ReadChunks(db,run.ProjectId,run.InputChunkIds);
                if(input.Count!=run.InputChunkIds.Count || input.Any(c=>!c.IsAvailable))
                    throw new StudyValidationException("Una fonte è cambiata o è stata archiviata. Genera una nuova anteprima prima di importare.");
                if(cards.Count<1 || cards.Any(c=>c.ProjectId!=run.ProjectId || c.GenerationRunId!=run.Id || c.Citations.Count==0 || c.Citations.Any(r=>!run.InputChunkIds.Contains(r.ChunkId))))
                    throw new StudyValidationException("Anteprima o riferimenti non validi per il progetto.");
                db.Execute("INSERT INTO GenerationRun(Id,ProjectId,ProviderKey,ProviderName,IsMock,RequestedCount,CreatedAtUtc,PromptVersion,SelectionHash) VALUES (?,?,?,?,?,?,?,?,?)",
                    run.Id.ToString(),run.ProjectId.ToString(),run.Provider.Key,run.Provider.DisplayName,run.Provider.IsMock,run.RequestedCount,Utc(run.CreatedAtUtc),run.PromptVersion,selectionHash);
                foreach(var chunk in run.InputChunkIds)
                    db.Execute("INSERT INTO GenerationInputChunk(ProjectId,GenerationRunId,ChunkId) VALUES (?,?,?)",run.ProjectId.ToString(),run.Id.ToString(),chunk.ToString());
                foreach(var card in cards) { ValidateReferences(db,card,null); WriteCard(db,card,null); }
            }); return 0;
        },ct);
    private static void ValidateReferences(SQLiteConnection db,FlashCard card,FlashcardRow? existing)
    {
        if(card.SourceId.HasValue && db.ExecuteScalar<int>("SELECT COUNT(*) FROM StudySource WHERE ProjectId=? AND Id=? AND (IsArchived=0 OR ?=Id)",
            card.ProjectId.ToString(),card.SourceId.Value.ToString(),existing?.SourceId)!=1)
            throw new StudyValidationException("La fonte collegata non è disponibile in questo progetto.");
        var chunks=StudyContextRepository.ReadChunks(db,card.ProjectId,card.Citations.Select(c=>c.ChunkId).ToArray()).ToDictionary(c=>c.Chunk.Id);
        foreach(var citation in card.Citations)
        {
            var preserved=existing is not null && db.ExecuteScalar<int>("SELECT COUNT(*) FROM FlashcardCitation WHERE ProjectId=? AND FlashcardId=? AND ChunkId=? AND (Quote=? OR Quote='')",
                card.ProjectId.ToString(),card.Id.ToString(),citation.ChunkId.ToString(),citation.Quote)==1;
            if(!chunks.TryGetValue(citation.ChunkId,out var chunk) || (!chunk.IsAvailable && !preserved) ||
                chunk.Chunk.SourceId!=citation.SourceId || chunk.Chunk.SourceRevisionId!=citation.SourceRevisionId || !chunk.Chunk.Text.Contains(citation.Quote,StringComparison.Ordinal))
                throw new StudyValidationException("Una citazione non appartiene al progetto o non corrisponde al testo della fonte.");
        }
    }
    private static void WriteCard(SQLiteConnection db,FlashCard card,int? expectedRevision)
    {
        var p=card.ProjectId.ToString(); var id=card.Id.ToString();
        if(expectedRevision.HasValue)
            RequireChanged(db.Execute("UPDATE FlashCard SET Front=?,Back=?,Explanation=?,Difficulty=?,IsFavorite=?,UpdatedAtUtc=?,Revision=Revision+1,SearchText=? WHERE ProjectId=? AND Id=? AND Revision=?",
                card.Front,card.Back,card.Explanation,(int?)card.Difficulty,card.IsFavorite,Utc(card.UpdatedAtUtc),SearchText(card.Front,card.Back,card.Explanation),p,id,expectedRevision.Value));
        else db.Execute("INSERT INTO FlashCard(Id,ProjectId,Front,Back,Explanation,Difficulty,IsFavorite,CreatedAtUtc,UpdatedAtUtc,SearchText) VALUES (?,?,?,?,?,?,?,?,?,?)",
            id,p,card.Front,card.Back,card.Explanation,(int?)card.Difficulty,card.IsFavorite,Utc(card.CreatedAtUtc),Utc(card.UpdatedAtUtc),SearchText(card.Front,card.Back,card.Explanation));
        db.Execute("INSERT INTO FlashcardDetails(ProjectId,FlashcardId,SourceId,GenerationRunId,IsUserEdited) VALUES (?,?,?,?,?) ON CONFLICT(ProjectId,FlashcardId) DO UPDATE SET SourceId=excluded.SourceId,IsUserEdited=excluded.IsUserEdited",
            p,id,card.SourceId?.ToString(),card.GenerationRunId?.ToString(),card.IsUserEdited);
        db.Execute("DELETE FROM FlashcardTag WHERE ProjectId=? AND FlashcardId=?",p,id);
        foreach(var tag in card.Tags)
        {
            var normalized=ProjectText.NormalizeTag(tag);
            var tagId=db.ExecuteScalar<string?>("SELECT Id FROM Tag WHERE ProjectId=? AND NormalizedName=?",p,normalized);
            if(tagId is null)
            {
                tagId=Guid.NewGuid().ToString();
                db.Execute("INSERT INTO Tag(Id,ProjectId,Name,NormalizedName,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,?,?,?)",tagId,p,tag,normalized,Utc(card.UpdatedAtUtc),Utc(card.UpdatedAtUtc));
            }
            db.Execute("INSERT INTO FlashcardTag(ProjectId,FlashcardId,TagId) VALUES (?,?,?)",p,id,tagId);
        }
        db.Execute("DELETE FROM FlashcardCitation WHERE ProjectId=? AND FlashcardId=?",p,id);
        foreach(var citation in card.Citations)
            db.Execute("INSERT INTO FlashcardCitation(ProjectId,FlashcardId,ChunkId,Quote) VALUES (?,?,?,?)",p,id,citation.ChunkId.ToString(),citation.Quote);
    }
    private static List<FlashCard> Hydrate(SQLiteConnection db,Guid projectId,List<FlashcardRow> rows)
    {
        if(rows.Count==0) return [];
        var placeholders=string.Join(',',rows.Select(_=>"?"));
        var args=new object[] { projectId.ToString() }.Concat(rows.Select(r=>(object)r.Id)).ToArray();
        var tags=db.Query<FlashcardTagRow>($"SELECT ft.FlashcardId,t.Id,t.Name FROM FlashcardTag ft JOIN Tag t ON t.ProjectId=ft.ProjectId AND t.Id=ft.TagId WHERE ft.ProjectId=? AND ft.FlashcardId IN ({placeholders}) ORDER BY t.NormalizedName",args).ToLookup(r=>r.FlashcardId);
        var citations=db.Query<FlashcardCitationRow>($"""
            SELECT fc.FlashcardId,fc.ChunkId,fc.Quote,c.Text AS ChunkText,c.SourceRevisionId,r.SourceId,s.Title AS SourceTitle,c.PageStart,c.PageEnd,c.SectionPath
            FROM FlashcardCitation fc JOIN SourceChunk c ON c.ProjectId=fc.ProjectId AND c.Id=fc.ChunkId
            JOIN SourceRevision r ON r.ProjectId=c.ProjectId AND r.Id=c.SourceRevisionId
            JOIN StudySource s ON s.ProjectId=r.ProjectId AND s.Id=r.SourceId
            WHERE fc.ProjectId=? AND fc.FlashcardId IN ({placeholders}) ORDER BY c.Ordinal,c.Id
            """,args).ToLookup(r=>r.FlashcardId);
        return rows.Select(r=>r.ToDomain() with { Tags=tags[r.Id].Select(t=>t.Name).ToArray(),TagIds=tags[r.Id].Select(t=>Guid.Parse(t.Id)).ToArray(),
            Citations=citations[r.Id].Select(c=>c.ToDomain()).ToArray() }).ToList();
    }
    internal static string SearchText(string front,string back,string? explanation)=>ProjectText.SearchKey(front+"\n"+back+"\n"+explanation);
    private static string Utc(DateTimeOffset now)=>now.ToUniversalTime().ToString("O");
    private static void RequireChanged(int count) { if(count!=1) throw new FlashcardConflictException(); }
}
public sealed class FlashcardRow
{
    public string Id { get; set; }="";
    public string ProjectId { get; set; }="";
    public string Front { get; set; }="";
    public string Back { get; set; }="";
    public string? Explanation { get; set; }
    public int? Difficulty { get; set; }
    public bool IsFavorite { get; set; }
    public string CreatedAtUtc { get; set; }="";
    public string UpdatedAtUtc { get; set; }="";
    public int Revision { get; set; }
    public string? SourceId { get; set; }
    public string? SourceTitle { get; set; }
    public bool IsSourceArchived { get; set; }
    public string? GenerationRunId { get; set; }
    public string? GenerationProviderKey { get; set; }
    public bool IsMockGenerated { get; set; }
    public bool IsUserEdited { get; set; }
    public FlashCard ToDomain()=>new() { Id=Guid.Parse(Id),ProjectId=Guid.Parse(ProjectId),Front=Front,Back=Back,Explanation=Explanation,
        Difficulty=(StudySpace.Domain.Common.Difficulty?)Difficulty,IsFavorite=IsFavorite,CreatedAtUtc=DateTimeOffset.Parse(CreatedAtUtc),UpdatedAtUtc=DateTimeOffset.Parse(UpdatedAtUtc),Revision=Revision,
        SourceId=Guid.TryParse(SourceId,out var s) ? s : null,SourceTitle=SourceTitle,IsSourceArchived=IsSourceArchived,
        GenerationRunId=Guid.TryParse(GenerationRunId,out var g) ? g : null,GenerationProviderKey=GenerationProviderKey,IsMockGenerated=IsMockGenerated,IsUserEdited=IsUserEdited };
}
public sealed class FlashcardTagRow
{
    public string FlashcardId { get; set; }="";
    public string Id { get; set; }="";
    public string Name { get; set; }="";
}
public sealed class FlashcardCitationRow
{
    public string FlashcardId { get; set; }="";
    public string ChunkId { get; set; }="";
    public string Quote { get; set; }="";
    public string ChunkText { get; set; }="";
    public string SourceRevisionId { get; set; }="";
    public string SourceId { get; set; }="";
    public string SourceTitle { get; set; }="";
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string? SectionPath { get; set; }
    public SourceCitation ToDomain()=>new(Guid.Parse(SourceId),Guid.Parse(SourceRevisionId),Guid.Parse(ChunkId),Quote.Length>0 ? Quote : ChunkText,SourceTitle,
        string.Join(" · ",new[] { PageStart.HasValue ? (PageStart==PageEnd ? $"Pagina {PageStart}" : $"Pagine {PageStart}–{PageEnd}") : null,SectionPath }.Where(s=>!string.IsNullOrWhiteSpace(s))));
}
