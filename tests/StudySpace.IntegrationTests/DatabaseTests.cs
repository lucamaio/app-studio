using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Domain.Projects;
using StudySpace.Infrastructure.Persistence;

namespace StudySpace.IntegrationTests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "StudySpace.Tests", Guid.NewGuid().ToString("N"));
    private StudyDatabase Open() => new(new DatabaseOptions(Path.Combine(directory, "test.db3")), NullLogger<StudyDatabase>.Instance);
    private static StudyProject Project(string name) => new() { Name = name };
    private static string Now => DateTimeOffset.UtcNow.ToString("O");

    [Fact]
    public async Task Fresh_database_is_empty_and_migration_is_idempotent()
    {
        Assert.Empty(await new ProjectRepository(Open()).ListAsync(0, 30));
        var reopened = Open();
        Assert.Empty(await new ProjectRepository(reopened).ListAsync(0, 30));
        Assert.Equal(6, await reopened.ExecuteAsync(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM SchemaMigration")));
        Assert.Equal(1, await reopened.ExecuteAsync(db => db.ExecuteScalar<int>("PRAGMA foreign_keys")));
    }

    [Fact]
    public async Task Project_survives_reopening_and_quotes_are_parameterized()
    {
        var project = Project("Reti'); DROP TABLE StudyProject;--") with { Description = "È uno spazio di studio" };
        await new ProjectRepository(Open()).AddAsync(project);
        var actual = await new ProjectRepository(Open()).GetAsync(project.Id);
        Assert.Equal(project, actual);
        Assert.Null(await new ProjectRepository(Open()).GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Paging_is_stable_when_timestamps_match()
    {
        var db = Open();
        var repository = new ProjectRepository(db);
        var sameTime = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            await repository.AddAsync(Project($"Materia {i}") with { UpdatedAtUtc = sameTime });
        var first = await repository.ListAsync(0, 3);
        var second = await repository.ListAsync(3, 3);
        Assert.Equal(5, first.Concat(second).Select(p => p.Id).Distinct().Count());
        Assert.Empty(await repository.ListAsync(6, 3));
    }

    [Fact]
    public async Task Concurrent_writes_are_serialized()
    {
        var repository = new ProjectRepository(Open());
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => repository.AddAsync(Project($"Progetto {i}"))));
        Assert.Equal(12, (await repository.ListAsync(0, 30)).Count);
    }

    [Fact]
    public async Task Project_tag_cannot_reference_another_projects_tag()
    {
        var db = Open();
        var repository = new ProjectRepository(db);
        var first = Project("A"); var second = Project("B"); var tagId = Guid.NewGuid().ToString();
        await repository.AddAsync(first); await repository.AddAsync(second);
        await db.ExecuteAsync(c => c.Execute(
            "INSERT INTO Tag(Id, ProjectId, Name, NormalizedName, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?)",
            tagId, first.Id.ToString(), "Esame", "ESAME", Now, Now));
        await Assert.ThrowsAsync<StorageUnavailableException>(() => db.ExecuteAsync(c => c.Execute(
            "INSERT INTO ProjectTag(ProjectId, TagId, CreatedAtUtc) VALUES (?, ?, ?)", second.Id.ToString(), tagId, Now)));
        Assert.Equal(0, await db.ExecuteAsync(c => c.ExecuteScalar<int>("SELECT COUNT(*) FROM ProjectTag")));
    }

    [Fact]
    public async Task Chunk_cannot_reference_a_revision_in_another_project()
    {
        var db = Open(); var repository = new ProjectRepository(db);
        var first = Project("A"); var second = Project("B");
        await repository.AddAsync(first); await repository.AddAsync(second);
        var source = Guid.NewGuid().ToString(); var revision = Guid.NewGuid().ToString();
        await db.ExecuteAsync(c =>
        {
            c.Execute("INSERT INTO StudySource(Id,ProjectId,Title,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,?,?)",
                source, first.Id.ToString(), "Appunti", Now, Now);
            return c.Execute("INSERT INTO SourceRevision(Id,ProjectId,SourceId,Number,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,1,?,?)",
                revision, first.Id.ToString(), source, Now, Now);
        });
        await Assert.ThrowsAsync<StorageUnavailableException>(() => db.ExecuteAsync(c => c.Execute(
            "INSERT INTO SourceChunk(Id,ProjectId,SourceRevisionId,Ordinal,Text,StartOffset,EndOffset,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,'Testo',0,5,?,?)",
            Guid.NewGuid().ToString(), second.Id.ToString(), revision, Now, Now)));
    }

    [Fact]
    public async Task Transaction_rolls_back_when_a_constraint_fails()
    {
        var db = Open();
        await new ProjectRepository(db).ListAsync(0, 30);
        await Assert.ThrowsAsync<StorageUnavailableException>(() => db.ExecuteAsync(c =>
        {
            c.RunInTransaction(() =>
            {
                c.Execute("INSERT INTO StudyProject(Id,Name,Color,IconKey,CreatedAtUtc,UpdatedAtUtc) VALUES ('a','Valido','#000','book',?,?)", Now, Now);
                c.Execute("INSERT INTO StudyProject(Id,Name,Color,IconKey,CreatedAtUtc,UpdatedAtUtc) VALUES ('b','','#000','book',?,?)", Now, Now);
            });
            return 0;
        }));
        Assert.Empty(await new ProjectRepository(db).ListAsync(0, 30));
    }

    [Fact]
    public async Task Newer_database_is_preserved_and_rejected()
    {
        var db = Open(); var project = Project("Da conservare");
        await new ProjectRepository(db).AddAsync(project);
        await db.ExecuteAsync(c => c.Execute("INSERT INTO SchemaMigration VALUES (99, ?)", Now));
        await Assert.ThrowsAsync<StorageUnavailableException>(() => new ProjectRepository(Open()).ListAsync(0, 30));
        using var raw = new SQLite.SQLiteConnection(Path.Combine(directory, "test.db3"));
        Assert.Equal(1, raw.ExecuteScalar<int>("SELECT COUNT(*) FROM StudyProject"));
    }

    [Fact]
    public async Task Cancelled_operation_does_not_create_a_database()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProjectRepository(Open()).ListAsync(0, 30, cancellation.Token));
        Assert.False(File.Exists(Path.Combine(directory, "test.db3")));
    }

    public void Dispose()
    {
        // Unique test-only directory under the process temp directory.
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}


