using SQLite;
using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
using StudySpace.Domain.Sources;
namespace StudySpace.Infrastructure.Persistence;

public sealed class SourceRepository(StudyDatabase database) : ISourceRepository
{
    private const string Columns = """
        s.Id, s.ProjectId, s.Title, s.Kind, s.OriginalFileName, s.CreatedAtUtc, s.UpdatedAtUtc, s.Revision,
        r.Id AS RevisionId, r.Number, r.RelativeFilePath, r.ContentHash, r.ExtractorVersion, r.Status,
        r.ErrorCode, r.ErrorMessage, r.SizeBytes, r.CreatedAtUtc AS RevisionCreatedAtUtc, r.UpdatedAtUtc AS RevisionUpdatedAtUtc,
        (SELECT COUNT(*) FROM SourceChunk c WHERE c.ProjectId=s.ProjectId AND c.SourceRevisionId=r.Id) AS ChunkCount
        """;
    private const string Join = " FROM StudySource s JOIN SourceRevision r ON r.ProjectId=s.ProjectId AND r.SourceId=s.Id AND r.Id=s.CurrentRevisionId ";

    public Task<IReadOnlyList<SourceSummary>> ListAsync(Guid projectId, int offset, int limit, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return database.ExecuteAsync<IReadOnlyList<SourceSummary>>(db => db.Query<SourceRow>("SELECT " + Columns + Join +
            "WHERE s.ProjectId=? AND s.IsArchived=0 ORDER BY s.CreatedAtUtc DESC, s.Id LIMIT ? OFFSET ?",
            projectId.ToString(), limit, offset).Select(r => r.ToSummary()).ToArray(), ct);
    }
    public Task<SourceDetails?> GetAsync(Guid projectId, Guid sourceId, CancellationToken ct = default) =>
        database.ExecuteAsync(db =>
        {
            var row = db.FindWithQuery<SourceRow>("SELECT " + Columns + ", r.ExtractedText" + Join +
                "WHERE s.ProjectId=? AND s.Id=? AND s.IsArchived=0", projectId.ToString(), sourceId.ToString());
            return row is null ? null : new SourceDetails(row.ToSummary());
        }, ct);

    public Task<IReadOnlyList<SourceChunk>> GetChunksAsync(Guid projectId, Guid sourceId, int offset, int limit, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return database.ExecuteAsync<IReadOnlyList<SourceChunk>>(db => db.Query<ChunkRow>("""
            SELECT c.*, s.Id AS SourceId FROM SourceChunk c
            JOIN StudySource s ON s.ProjectId=c.ProjectId AND s.CurrentRevisionId=c.SourceRevisionId
            WHERE s.ProjectId=? AND s.Id=? AND s.IsArchived=0 ORDER BY c.Ordinal LIMIT ? OFFSET ?
            """, projectId.ToString(), sourceId.ToString(), limit, offset).Select(r => r.ToDomain()).ToArray(), ct);
    }
    public async Task CreateAsync(StudySource source, SourceRevision revision, CancellationToken ct = default)
    {
        if (source.ProjectId != revision.ProjectId || source.Id != revision.SourceId || source.CurrentRevisionId != revision.Id)
            throw new ArgumentException("Revision must belong to the source.");
        await database.ExecuteAsync(db =>
        {
            db.RunInTransaction(() =>
            {
                db.Execute("""
                    INSERT INTO StudySource(Id,ProjectId,Title,Kind,OriginalFileName,CurrentRevisionId,CreatedAtUtc,UpdatedAtUtc)
                    VALUES (?,?,?,?,?,?,?,?)
                    """, source.Id.ToString(), source.ProjectId.ToString(), source.Title, (int)source.Kind, source.OriginalFileName,
                    revision.Id.ToString(), Utc(source.CreatedAtUtc), Utc(source.UpdatedAtUtc));
                db.Execute("""
                    INSERT INTO SourceRevision(Id,ProjectId,SourceId,Number,Status,ExtractedText,SizeBytes,CreatedAtUtc,UpdatedAtUtc)
                    VALUES (?,?,?,1,?,?,?,?,?)
                    """, revision.Id.ToString(), revision.ProjectId.ToString(), source.Id.ToString(), (int)revision.Status,
                    revision.ExtractedText, revision.SizeBytes, Utc(revision.CreatedAtUtc), Utc(revision.UpdatedAtUtc));
            });
            return 0;
        }, ct);
    }
    public async Task SetFileAsync(Guid projectId, Guid sourceId, Guid revisionId, StoredSourceFile file, CancellationToken ct = default) =>
        await database.ExecuteAsync(db =>
        {
            RequireChanged(db.Execute("""
                UPDATE SourceRevision SET RelativeFilePath=?, SizeBytes=?, ContentHash=?
                WHERE ProjectId=? AND SourceId=? AND Id=? AND Status=1
                  AND EXISTS(SELECT 1 FROM StudySource s WHERE s.ProjectId=? AND s.Id=? AND s.IsArchived=0)
                """, file.RelativePath, file.SizeBytes, file.ContentHash, projectId.ToString(), sourceId.ToString(), revisionId.ToString(),
                projectId.ToString(), sourceId.ToString()));
            return 0;
        }, ct);

    public async Task CompleteAsync(Guid projectId, Guid sourceId, Guid revisionId, string text, string extractorVersion,
        IReadOnlyList<SourceChunk> chunks, DateTimeOffset now, CancellationToken ct = default)
    {
        if (chunks.Count == 0 || chunks.Any(c => c.ProjectId != projectId || c.SourceId != sourceId || c.SourceRevisionId != revisionId))
            throw new ArgumentException("Chunks must belong to this source and revision.");
        await database.ExecuteAsync(db =>
        {
            db.RunInTransaction(() =>
            {
                RequireChanged(db.Execute("""
                    UPDATE SourceRevision SET ExtractedText=?,ExtractorVersion=?,Status=2,ErrorCode=NULL,ErrorMessage=NULL,UpdatedAtUtc=?,
                        ContentHash=CASE WHEN ContentHash='' THEN ? ELSE ContentHash END
                    WHERE ProjectId=? AND SourceId=? AND Id=? AND Status=1
                      AND EXISTS(SELECT 1 FROM StudySource s WHERE s.ProjectId=? AND s.Id=? AND s.IsArchived=0)
                    """, text, extractorVersion, Utc(now), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))),
                    projectId.ToString(), sourceId.ToString(), revisionId.ToString(),
                    projectId.ToString(), sourceId.ToString()));
                foreach (var c in chunks)
                    db.Execute("""
                        INSERT INTO SourceChunk(Id,ProjectId,SourceRevisionId,Ordinal,Text,StartOffset,EndOffset,PageStart,PageEnd,SectionPath,ChunkerVersion,CreatedAtUtc,UpdatedAtUtc)
                        VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)
                        """, c.Id.ToString(), projectId.ToString(), revisionId.ToString(), c.Ordinal, c.Text, c.StartOffset, c.EndOffset,
                        c.PageStart, c.PageEnd, c.SectionPath, c.ChunkerVersion, Utc(c.CreatedAtUtc), Utc(c.UpdatedAtUtc));
            });
            return 0;
        }, ct);
    }
    public async Task SetStatusAsync(Guid projectId, Guid sourceId, ExtractionStatus status, string? code, string? message, CancellationToken ct = default) =>
        await database.ExecuteAsync(db =>
        {
            // Ready revisions and their chunks are immutable; retry only a failed/interrupted import.
            RequireChanged(db.Execute("""
                UPDATE SourceRevision SET Status=?,ErrorCode=?,ErrorMessage=? WHERE ProjectId=? AND SourceId=? AND Status<>2
                AND Id=(SELECT CurrentRevisionId FROM StudySource WHERE ProjectId=? AND Id=? AND IsArchived=0)
                """, (int)status, code, message, projectId.ToString(), sourceId.ToString(), projectId.ToString(), sourceId.ToString()));
            return 0;
        }, ct);
    public async Task RenameAsync(Guid projectId, Guid sourceId, int expectedRevision, string title, DateTimeOffset now, CancellationToken ct = default) =>
        await database.ExecuteAsync(db =>
        {
            RequireChanged(db.Execute("UPDATE StudySource SET Title=?, UpdatedAtUtc=?, Revision=Revision+1 WHERE ProjectId=? AND Id=? AND Revision=? AND IsArchived=0",
                title, Utc(now), projectId.ToString(), sourceId.ToString(), expectedRevision));
            return 0;
        }, ct);
    public async Task ArchiveAsync(Guid projectId, Guid sourceId, int expectedRevision, DateTimeOffset now, CancellationToken ct = default) =>
        await database.ExecuteAsync(db =>
        {
            RequireChanged(db.Execute("UPDATE StudySource SET IsArchived=1, UpdatedAtUtc=?, Revision=Revision+1 WHERE ProjectId=? AND Id=? AND Revision=? AND IsArchived=0",
                Utc(now), projectId.ToString(), sourceId.ToString(), expectedRevision));
            return 0;
        }, ct);
    public async Task RecoverInterruptedAsync(CancellationToken ct = default) => await database.ExecuteAsync(db =>
        db.Execute("UPDATE SourceRevision SET Status=3,ErrorCode='Interrupted',ErrorMessage=? WHERE Status IN (0,1)",
            "Elaborazione interrotta. Riprova dalla copia locale oppure importa nuovamente il file."), ct);
    public Task<IReadOnlySet<string>> ReferencedFilesAsync(CancellationToken ct = default) =>
        database.ExecuteAsync<IReadOnlySet<string>>(db => db.Query<SourceRow>(
            "SELECT RelativeFilePath FROM SourceRevision WHERE RelativeFilePath IS NOT NULL")
            .Select(r => r.RelativeFilePath!).ToHashSet(StringComparer.Ordinal), ct);
    private static string Utc(DateTimeOffset time) => time.ToUniversalTime().ToString("O");
    private static void RequireChanged(int count) { if (count != 1) throw new SourceConflictException(); }
}

// Flat mapping avoids loading extracted text and chunks in the paginated source list.
public sealed class SourceRow
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Kind { get; set; }
    public string? OriginalFileName { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
    public int Revision { get; set; }
    public string RevisionId { get; set; } = "";
    public string RevisionCreatedAtUtc { get; set; } = "";
    public string RevisionUpdatedAtUtc { get; set; } = "";
    public int Number { get; set; }
    public string? RelativeFilePath { get; set; }
    public string ContentHash { get; set; } = "";
    public string ExtractedText { get; set; } = "";
    public string ExtractorVersion { get; set; } = "";
    public int Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public long SizeBytes { get; set; }
    public int ChunkCount { get; set; }
    public SourceSummary ToSummary()
    {
        var source = new StudySource { Id=Guid.Parse(Id), ProjectId=Guid.Parse(ProjectId), Title=Title, Kind=(SourceKind)Kind,
            OriginalFileName=OriginalFileName, CurrentRevisionId=Guid.Parse(RevisionId), Revision=Revision,
            CreatedAtUtc=DateTimeOffset.Parse(CreatedAtUtc), UpdatedAtUtc=DateTimeOffset.Parse(UpdatedAtUtc) };
        return new(source, new SourceRevision { Id=Guid.Parse(RevisionId), ProjectId=source.ProjectId, SourceId=source.Id,
            Number=Number, RelativeFilePath=RelativeFilePath, ContentHash=ContentHash, ExtractedText=ExtractedText,
            ExtractorVersion=ExtractorVersion, Status=(ExtractionStatus)Status, ErrorCode=ErrorCode, ErrorMessage=ErrorMessage,
            SizeBytes=SizeBytes, CreatedAtUtc=DateTimeOffset.Parse(RevisionCreatedAtUtc), UpdatedAtUtc=DateTimeOffset.Parse(RevisionUpdatedAtUtc) }, ChunkCount);
    }
}
public sealed class ChunkRow
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string SourceRevisionId { get; set; } = "";
    public int Ordinal { get; set; }
    public string Text { get; set; } = "";
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string? SectionPath { get; set; }
    public string ChunkerVersion { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
    public SourceChunk ToDomain() => new() { Id=Guid.Parse(Id), ProjectId=Guid.Parse(ProjectId), SourceId=Guid.Parse(SourceId),
        SourceRevisionId=Guid.Parse(SourceRevisionId), Ordinal=Ordinal, Text=Text, StartOffset=StartOffset, EndOffset=EndOffset,
        PageStart=PageStart, PageEnd=PageEnd, SectionPath=SectionPath, ChunkerVersion=ChunkerVersion,
        CreatedAtUtc=DateTimeOffset.Parse(CreatedAtUtc), UpdatedAtUtc=DateTimeOffset.Parse(UpdatedAtUtc) };
}
