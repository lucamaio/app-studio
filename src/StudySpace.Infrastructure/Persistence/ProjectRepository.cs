using SQLite;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Projects;
namespace StudySpace.Infrastructure.Persistence;

public sealed class ProjectRepository(StudyDatabase database, StudySpace.Application.Sources.ISourceFileStore? files = null) : IProjectRepository
{
    public Task<IReadOnlyList<StudyProject>> ListAsync(int offset, int limit, CancellationToken cancellationToken = default) =>
        SearchAsync(new(), offset, limit, cancellationToken);

    public Task<IReadOnlyList<StudyProject>> SearchAsync(ProjectQuery query, int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        return database.ExecuteAsync<IReadOnlyList<StudyProject>>(db =>
        {
            var search = ProjectText.SearchKey(query.Search);
            var tag = string.IsNullOrWhiteSpace(query.Tag) ? "" : ProjectText.NormalizeTag(query.Tag);
            var rows = db.Query<ProjectRow>("""
                SELECT p.* FROM StudyProject p
                WHERE (? = '' OR instr(p.SearchText, ?) > 0)
                  AND (? = 0 OR p.IsFavorite = 1)
                  AND (? = '' OR EXISTS (
                    SELECT 1 FROM ProjectTag pt JOIN Tag t ON t.Id = pt.TagId AND t.ProjectId = pt.ProjectId
                    WHERE pt.ProjectId = p.Id AND t.NormalizedName = ?))
                ORDER BY p.UpdatedAtUtc DESC, p.Id LIMIT ? OFFSET ?
                """, search, search, query.FavoritesOnly, tag, tag, limit, offset);
            return Hydrate(db, rows);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken cancellationToken = default) =>
        database.ExecuteAsync<IReadOnlyList<string>>(db => db.Query<TagRow>("""
            SELECT MIN(t.Name) AS Name FROM Tag t
            JOIN ProjectTag pt ON t.ProjectId = pt.ProjectId AND t.Id = pt.TagId
            GROUP BY t.NormalizedName ORDER BY t.NormalizedName
            """).Select(t => t.Name).ToList(), cancellationToken);

    public Task<StudyProject?> GetAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        database.ExecuteAsync(db => Read(db, projectId), cancellationToken);

    public Task<ProjectSummary?> GetSummaryAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        database.ExecuteAsync(db =>
        {
            var project = Read(db, projectId);
            if (project is null) return null;
            var id = projectId.ToString();
            return new ProjectSummary(project,
                db.ExecuteScalar<int>("SELECT COUNT(*) FROM StudySource WHERE ProjectId = ? AND IsArchived = 0", id),
                db.ExecuteScalar<int>("SELECT COUNT(*) FROM FlashCard WHERE ProjectId = ?", id),
                db.ExecuteScalar<int>("SELECT COUNT(*) FROM QuizAttempt WHERE ProjectId = ? AND Status = 2", id));
        }, cancellationToken);

    public async Task AddAsync(StudyProject project, CancellationToken cancellationToken = default)
    {
        await database.ExecuteAsync(db =>
        {
            db.RunInTransaction(() =>
            {
                db.Execute("""
                    INSERT INTO StudyProject(Id, Name, Description, Color, IconKey, IsFavorite, CreatedAtUtc, UpdatedAtUtc, Revision, SearchText)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """, project.Id.ToString(), project.Name, project.Description, project.Color, project.IconKey,
                    project.IsFavorite, Utc(project.CreatedAtUtc), Utc(project.UpdatedAtUtc), project.Revision, SearchText(project));
                SaveTags(db, project);
            });
            return 0;
        }, cancellationToken);
    }

    public async Task UpdateAsync(StudyProject project, int expectedRevision, CancellationToken cancellationToken = default)
    {
        await database.ExecuteAsync(db =>
        {
            db.RunInTransaction(() =>
            {
                RequireChanged(db.Execute("""
                    UPDATE StudyProject SET Name=?, Description=?, Color=?, IconKey=?, IsFavorite=?, UpdatedAtUtc=?,
                        Revision=Revision+1, SearchText=? WHERE Id=? AND Revision=?
                    """, project.Name, project.Description, project.Color, project.IconKey, project.IsFavorite,
                    Utc(project.UpdatedAtUtc), SearchText(project), project.Id.ToString(), expectedRevision));
                SaveTags(db, project);
            });
            return 0;
        }, cancellationToken);
    }

    public async Task SetFavoriteAsync(Guid projectId, bool favorite, int expectedRevision, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default)
    {
        await database.ExecuteAsync(db =>
        {
            RequireChanged(db.Execute("UPDATE StudyProject SET IsFavorite=?, UpdatedAtUtc=?, Revision=Revision+1 WHERE Id=? AND Revision=?",
                favorite, Utc(modifiedAt), projectId.ToString(), expectedRevision));
            return 0;
        }, cancellationToken);
    }
    public async Task DeleteAsync(Guid projectId, int expectedRevision, CancellationToken cancellationToken = default)
    {
        await database.ExecuteAsync(db =>
        {
            // All project-owned rows are removed by declared foreign keys in the same transaction.
            db.RunInTransaction(() => RequireChanged(db.Execute("DELETE FROM StudyProject WHERE Id=? AND Revision=?",
                projectId.ToString(), expectedRevision)));
            return 0;
        }, cancellationToken);
        if (files is not null) await files.DeleteProjectAsync(projectId);
    }

    private static void RequireChanged(int count)
    {
        if (count != 1) throw new ProjectConflictException();
    }
    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O");
    internal static string SearchText(StudyProject p) => ProjectText.SearchKey(p.Name + "\n" + p.Description);

    private static StudyProject? Read(SQLiteConnection db, Guid id) =>
        Hydrate(db, db.Query<ProjectRow>("SELECT * FROM StudyProject WHERE Id=?", id.ToString())).SingleOrDefault();

    private static List<StudyProject> Hydrate(SQLiteConnection db, List<ProjectRow> rows)
    {
        if (rows.Count == 0) return [];
        var placeholders = string.Join(",", rows.Select(_ => "?"));
        var tags = db.Query<TagRow>($"""
            SELECT t.ProjectId, t.Name FROM Tag t JOIN ProjectTag pt ON pt.ProjectId=t.ProjectId AND pt.TagId=t.Id
            WHERE t.ProjectId IN ({placeholders}) ORDER BY t.NormalizedName
            """, rows.Select(r => (object)r.Id).ToArray()).ToLookup(t => t.ProjectId);
        return rows.Select(r => r.ToDomain() with
        {
            Tags = tags[r.Id].Any() ? tags[r.Id].Select(t => t.Name).ToArray() : Array.Empty<string>()
        }).ToList();
    }

    private static void SaveTags(SQLiteConnection db, StudyProject project)
    {
        var id = project.Id.ToString();
        db.Execute("DELETE FROM ProjectTag WHERE ProjectId=?", id);
        foreach (var tag in project.Tags)
        {
            var normalized = ProjectText.NormalizeTag(tag);
            var existingId = db.ExecuteScalar<string?>("SELECT Id FROM Tag WHERE ProjectId=? AND NormalizedName=?", id, normalized);
            var tagId = existingId ?? Guid.NewGuid().ToString();
            if (existingId is null)
                db.Execute("INSERT INTO Tag(Id,ProjectId,Name,NormalizedName,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,?,?,?)",
                    tagId, id, tag, normalized, Utc(project.UpdatedAtUtc), Utc(project.UpdatedAtUtc));
            else
                db.Execute("UPDATE Tag SET Name=?, UpdatedAtUtc=?, Revision=Revision+1 WHERE Id=? AND ProjectId=?",
                    tag, Utc(project.UpdatedAtUtc), tagId, id);
            db.Execute("INSERT INTO ProjectTag(ProjectId,TagId,CreatedAtUtc) VALUES (?,?,?)", id, tagId, Utc(project.UpdatedAtUtc));
        }
        // Retain vocabulary still used by sources/flashcards; discard genuinely orphaned tags.
        db.Execute("""
            DELETE FROM Tag WHERE ProjectId=?
            AND NOT EXISTS (SELECT 1 FROM ProjectTag WHERE TagId=Tag.Id AND ProjectId=Tag.ProjectId)
            AND NOT EXISTS (SELECT 1 FROM SourceTag WHERE TagId=Tag.Id AND ProjectId=Tag.ProjectId)
            AND NOT EXISTS (SELECT 1 FROM FlashcardTag WHERE TagId=Tag.Id AND ProjectId=Tag.ProjectId)
            """, id);
    }
}

public sealed class TagRow
{
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
}

