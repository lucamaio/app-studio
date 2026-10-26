using SQLite;
using StudySpace.Application.Flashcards;
namespace StudySpace.Infrastructure.Persistence;

public sealed class StudyContextRepository(StudyDatabase database):IStudyContextRepository
{
    public Task<IReadOnlyList<MaterialSource>> ListReadySourcesAsync(Guid projectId,int offset,int limit,CancellationToken ct=default)
    {
        ValidatePage(offset,limit);
        return database.ExecuteAsync<IReadOnlyList<MaterialSource>>(db=>db.Query<MaterialSourceRow>("""
            SELECT s.Id,s.Title,(SELECT COUNT(*) FROM SourceChunk c WHERE c.ProjectId=s.ProjectId AND c.SourceRevisionId=r.Id) AS ChunkCount
            FROM StudySource s JOIN SourceRevision r ON r.ProjectId=s.ProjectId AND r.SourceId=s.Id AND r.Id=s.CurrentRevisionId
            WHERE s.ProjectId=? AND s.IsArchived=0 AND r.Status=2 ORDER BY s.CreatedAtUtc DESC,s.Id LIMIT ? OFFSET ?
            """,projectId.ToString(),limit,offset).Select(r=>r.ToDomain()).ToArray(),ct);
    }
    public Task<MaterialSource?> GetSourceAsync(Guid projectId,Guid sourceId,bool includeArchived,CancellationToken ct=default)=>
        database.ExecuteAsync(db=>db.FindWithQuery<MaterialSourceRow>("""
            SELECT s.Id,s.Title,0 AS ChunkCount FROM StudySource s WHERE s.ProjectId=? AND s.Id=? AND (?=1 OR s.IsArchived=0)
            """,projectId.ToString(),sourceId.ToString(),includeArchived)?.ToDomain(),ct);
    public Task<IReadOnlyList<StudyChunk>> ListChunksAsync(Guid projectId,Guid sourceId,int offset,int limit,CancellationToken ct=default)
    {
        ValidatePage(offset,limit);
        return database.ExecuteAsync<IReadOnlyList<StudyChunk>>(db=>db.Query<StudyChunkRow>(Select+"""
            WHERE s.ProjectId=? AND s.Id=? AND s.IsArchived=0 AND r.Status=2 AND s.CurrentRevisionId=r.Id
            ORDER BY c.Ordinal LIMIT ? OFFSET ?
            """,projectId.ToString(),sourceId.ToString(),limit,offset).Select(r=>r.ToContext()).ToArray(),ct);
    }
    public Task<IReadOnlyList<StudyChunk>> GetChunksAsync(Guid projectId,IReadOnlyList<Guid> ids,CancellationToken ct=default)
    {
        if(ids.Count>20) throw new ArgumentOutOfRangeException(nameof(ids));
        return database.ExecuteAsync<IReadOnlyList<StudyChunk>>(db=>ReadChunks(db,projectId,ids),ct);
    }
    internal static IReadOnlyList<StudyChunk> ReadChunks(SQLiteConnection db,Guid projectId,IReadOnlyList<Guid> ids)
    {
        if(ids.Count==0) return [];
        var placeholders=string.Join(',',ids.Select(_=>"?"));
        var args=new object[] { projectId.ToString() }.Concat(ids.Select(id=>(object)id.ToString())).ToArray();
        var rows=db.Query<StudyChunkRow>(Select+$" WHERE c.ProjectId=? AND c.Id IN ({placeholders})",args).ToDictionary(r=>r.Id);
        return ids.Where(id=>rows.ContainsKey(id.ToString())).Select(id=>rows[id.ToString()].ToContext()).ToArray();
    }
    private const string Select="""
        SELECT c.*,s.Id AS SourceId,s.Title AS SourceTitle,
        CASE WHEN s.IsArchived=0 AND r.Status=2 AND s.CurrentRevisionId=r.Id THEN 1 ELSE 0 END AS IsAvailable
        FROM SourceChunk c JOIN SourceRevision r ON r.ProjectId=c.ProjectId AND r.Id=c.SourceRevisionId
        JOIN StudySource s ON s.ProjectId=r.ProjectId AND s.Id=r.SourceId
        
        """;
    private static void ValidatePage(int offset,int limit)
    { ArgumentOutOfRangeException.ThrowIfNegative(offset); if(limit is <1 or >100) throw new ArgumentOutOfRangeException(nameof(limit)); }
}
public sealed class MaterialSourceRow
{
    public string Id { get; set; }="";
    public string Title { get; set; }="";
    public int ChunkCount { get; set; }
    public MaterialSource ToDomain()=>new(Guid.Parse(Id),Title,ChunkCount);
}
public sealed class StudyChunkRow
{
    public string Id { get; set; }="";
    public string ProjectId { get; set; }="";
    public string SourceId { get; set; }="";
    public string SourceRevisionId { get; set; }="";
    public string SourceTitle { get; set; }="";
    public bool IsAvailable { get; set; }
    public string Text { get; set; }="";
    public int Ordinal { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string? SectionPath { get; set; }
    public string ChunkerVersion { get; set; }="";
    public string CreatedAtUtc { get; set; }="";
    public string UpdatedAtUtc { get; set; }="";
    public StudyChunk ToContext()=>new(new() { Id=Guid.Parse(Id),ProjectId=Guid.Parse(ProjectId),SourceId=Guid.Parse(SourceId),
        SourceRevisionId=Guid.Parse(SourceRevisionId),Text=Text,Ordinal=Ordinal,StartOffset=StartOffset,EndOffset=EndOffset,
        PageStart=PageStart,PageEnd=PageEnd,SectionPath=SectionPath,ChunkerVersion=ChunkerVersion,
        CreatedAtUtc=DateTimeOffset.Parse(CreatedAtUtc),UpdatedAtUtc=DateTimeOffset.Parse(UpdatedAtUtc) },SourceTitle,IsAvailable);
}
