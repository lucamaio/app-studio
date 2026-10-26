using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Projects;
using StudySpace.Infrastructure.Persistence;
namespace StudySpace.IntegrationTests;

public sealed class ProjectManagementDatabaseTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "StudySpace.Tests", Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(directory, "projects.db3");
    private StudyDatabase Database() => new(new(DbPath), NullLogger<StudyDatabase>.Instance);
    private ProjectRepository Repository() => new(Database());
    private ProjectService Service() => new(Repository(), new Clock());
    private static string Now => DateTimeOffset.UtcNow.ToString("O");
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    [Fact]
    public async Task All_project_fields_and_tags_survive_reopening_and_update()
    {
        var p = await Service().CreateAsync(new("Reti", "TCP/IP", "network", "#167B9B", true, ["Esame", "Informatica"]));
        var read = await Service().GetAsync(p.Id);
        Assert.Equal(p.Name, read.Name);
        Assert.Equal(p.Description, read.Description);
        Assert.Equal(p.IconKey, read.IconKey);
        Assert.Equal(p.Color, read.Color);
        Assert.True(read.IsFavorite);
        Assert.Equal(["Esame", "Informatica"], read.Tags);
        await Service().UpdateAsync(p.Id, read.Revision, new("Reti II", "Nuovi appunti", "", "#B94B7A", false, ["Semestre 2"]));
        var updated = await Service().GetAsync(p.Id);
        Assert.Equal("Reti II", updated.Name);
        Assert.Equal("", updated.IconKey);
        Assert.Equal("#B94B7A", updated.Color);
        Assert.False(updated.IsFavorite);
        Assert.Equal(["Semestre 2"], updated.Tags);
        Assert.Equal(p.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.True(updated.UpdatedAtUtc >= read.UpdatedAtUtc);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(["Semestre 2"], await Repository().ListTagsAsync());
    }

    [Fact]
    public async Task Search_favorites_and_tag_filters_combine_and_treat_wildcards_literally()
    {
        var service = Service();
        var first = await service.CreateAsync(new("Reti", "Affidabilità al 100%", IsFavorite: true, Tags: ["Esame"]));
        await service.CreateAsync(new("Reti II", "Affidabilità", Tags: ["Esame"]));
        await service.CreateAsync(new("Calcolo", "Reti numeriche", IsFavorite: true, Tags: ["Matematica"]));
        var found = await Repository().SearchAsync(new("AFFIDABILITA", "esame", true), 0, 30);
        Assert.Equal(first.Id, Assert.Single(found).Id);
        Assert.Equal(first.Id, Assert.Single(await Repository().SearchAsync(new("%"), 0, 30)).Id);
        Assert.Empty(await Repository().SearchAsync(new("_"), 0, 30));
        Assert.Empty(await Repository().SearchAsync(new("' OR 1=1 --"), 0, 30));
        Assert.Equal(2, (await Repository().SearchAsync(new(FavoritesOnly: true), 0, 30)).Count);
    }

    [Fact]
    public async Task Filtered_paging_has_no_duplicates()
    {
        var service = Service();
        for (int i = 0; i < 7; i++) await service.CreateAsync(new($"Reti {i}", IsFavorite: i % 2 == 0, Tags: ["Esame"]));
        var a = await Repository().SearchAsync(new(Tag: "Esame", FavoritesOnly: true), 0, 2);
        var b = await Repository().SearchAsync(new(Tag: "Esame", FavoritesOnly: true), 2, 2);
        Assert.Equal(4, a.Concat(b).Select(p => p.Id).Distinct().Count());
        Assert.Empty(await Repository().SearchAsync(new(Tag: "Esame", FavoritesOnly: true), 4, 2));
    }

    [Fact]
    public async Task Favorite_changes_are_persistent_and_stale_mutations_are_rejected()
    {
        var service = Service();
        var p = await service.CreateAsync(new("Reti", Tags: ["Esame"]));
        await service.SetFavoriteAsync(p, true);
        var changed = await Service().GetAsync(p.Id);
        Assert.True(changed.IsFavorite);
        Assert.Equal(2, changed.Revision);
        Assert.Equal(p.CreatedAtUtc, changed.CreatedAtUtc);
        await Assert.ThrowsAsync<ProjectConflictException>(() => service.UpdateAsync(p.Id, p.Revision, new("Obsoleto", Tags: ["Altro"])));
        await Assert.ThrowsAsync<ProjectConflictException>(() => service.DeleteAsync(p));
        await Assert.ThrowsAsync<ProjectConflictException>(() => service.SetFavoriteAsync(p, false));
        Assert.Equal(["Esame"], (await Service().GetAsync(p.Id)).Tags);
        await service.SetFavoriteAsync(changed, false);
        Assert.Empty(await Repository().SearchAsync(new(FavoritesOnly: true), 0, 30));
    }

    [Fact]
    public async Task Same_tag_name_in_different_projects_has_distinct_identity()
    {
        var a = await Service().CreateAsync(new("A", Tags: ["Esame"]));
        var b = await Service().CreateAsync(new("B", Tags: ["Esame"]));
        Assert.Equal(2, await Database().ExecuteAsync(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM Tag")));
        Assert.Single(await Repository().ListTagsAsync());
        await Service().UpdateAsync(a.Id, a.Revision, new("A", Tags: ["Modificato"]));
        Assert.Equal(["Esame"], (await Service().GetAsync(b.Id)).Tags);
    }

    [Fact]
    public async Task Summary_counts_only_own_active_sources_and_completed_attempts()
    {
        var a = await Service().CreateAsync("A", null);
        var b = await Service().CreateAsync("B", null);
        await SeedContents(a.Id);
        await SeedContents(b.Id);
        await Database().ExecuteAsync(db => db.Execute(
            "UPDATE StudySource SET IsArchived=1 WHERE ProjectId=?", a.Id.ToString()));
        var summary = await Service().GetSummaryAsync(a.Id);
        Assert.Equal(0, summary.SourceCount);
        Assert.Equal(1, summary.FlashcardCount);
        Assert.Equal(1, summary.CompletedQuizCount);
        Assert.Equal(1, (await Service().GetSummaryAsync(b.Id)).SourceCount);
        Assert.Null(await Repository().GetSummaryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_project_cascades_through_sources_history_and_tags_without_touching_other_project()
    {
        var a = await Service().CreateAsync(new("A", Tags: ["Esame"]));
        var b = await Service().CreateAsync(new("B", Tags: ["Esame"]));
        await SeedContents(a.Id);
        await SeedContents(b.Id);
        await Service().DeleteAsync(a);
        Assert.Null(await Repository().GetAsync(a.Id));
        Assert.NotNull(await Repository().GetAsync(b.Id));
        foreach (var table in new[] { "Tag", "ProjectTag", "StudySource", "SourceRevision", "SourceChunk", "FlashCard",
            "Quiz", "QuizQuestion", "QuestionOption", "QuizAttempt", "AttemptQuestion", "QuizAnswer" })
        {
            Assert.Equal(0, await Database().ExecuteAsync(db => db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE ProjectId=?", a.Id.ToString())));
            Assert.True(await Database().ExecuteAsync(db => db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE ProjectId=?", b.Id.ToString())) > 0);
        }
        Assert.Equal(0, await Database().ExecuteAsync(db => db.ExecuteScalar<int>("SELECT COUNT(*) FROM pragma_foreign_key_check")));
    }

    [Fact]
    public async Task Upgrading_v1_preserves_data_and_backfills_unicode_search()
    {
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString();
        using (var raw = new SQLite.SQLiteConnection(DbPath))
        {
            using var stream = typeof(StudyDatabase).Assembly.GetManifestResourceStream("StudySpace.Infrastructure.Persistence.Migrations.001_Initial.sql")!;
            using var reader = new StreamReader(stream);
            foreach (var statement in reader.ReadToEnd().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                raw.Execute(statement);
            raw.Execute("CREATE TABLE SchemaMigration (Version INTEGER PRIMARY KEY, AppliedAtUtc TEXT NOT NULL)");
            raw.Execute("INSERT INTO SchemaMigration VALUES (1,?)", Now);
            raw.Execute("INSERT INTO StudyProject(Id,Name,Description,Color,IconKey,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,'#5B63D3','book',?,?)",
                id, "Probabilità", "Appunti esistenti", Now, Now);
        }
        var result = await Repository().SearchAsync(new("PROBABILITA"), 0, 30);
        Assert.Equal(id, Assert.Single(result).Id.ToString());
        Assert.Equal("Appunti esistenti", result[0].Description);
        Assert.Equal(6, await Database().ExecuteAsync(db => db.ExecuteScalar<int>("SELECT MAX(Version) FROM SchemaMigration")));
        Assert.Single(await Repository().ListAsync(0, 30));
    }

    private async Task SeedContents(Guid projectId)
    {
        await Database().ExecuteAsync(db =>
        {
            var p = projectId.ToString();
            var source = Guid.NewGuid().ToString(); var revision = Guid.NewGuid().ToString();
            var chunk = Guid.NewGuid().ToString(); var quiz = Guid.NewGuid().ToString();
            var question = Guid.NewGuid().ToString(); var attempt = Guid.NewGuid().ToString();
            var snapshot = Guid.NewGuid().ToString();
            db.RunInTransaction(() =>
            {
                db.Execute("INSERT INTO StudySource(Id,ProjectId,Title,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,?,?)", source, p, "Fonte", Now, Now);
                db.Execute("INSERT INTO SourceRevision(Id,ProjectId,SourceId,Number,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,1,?,?)", revision, p, source, Now, Now);
                db.Execute("UPDATE StudySource SET CurrentRevisionId=? WHERE Id=?", revision, source);
                db.Execute("INSERT INTO SourceChunk(Id,ProjectId,SourceRevisionId,Ordinal,Text,StartOffset,EndOffset,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,'Testo',0,5,?,?)", chunk, p, revision, Now, Now);
                db.Execute("INSERT INTO FlashCard(Id,ProjectId,Front,Back,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,'D','R',?,?)", Guid.NewGuid().ToString(), p, Now, Now);
                db.Execute("INSERT INTO Quiz(Id,ProjectId,Title,Mode,RequestedCount,Difficulty,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,'Quiz',0,1,0,?,?)", quiz, p, Now, Now);
                db.Execute("INSERT INTO QuizQuestion(Id,ProjectId,QuizId,Position,Kind,Prompt,Difficulty,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,0,'D',0,?,?)", question, p, quiz, Now, Now);
                db.Execute("INSERT INTO QuestionOption(Id,ProjectId,QuestionId,Position,Text,IsCorrect,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,'R',1,?,?)", Guid.NewGuid().ToString(), p, question, Now, Now);
                db.Execute("INSERT INTO QuizAttempt(Id,ProjectId,QuizId,Mode,Difficulty,Status,StartedAtUtc,MaximumScore,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,0,2,?,1,?,?)", attempt, p, quiz, Now, Now, Now);
                db.Execute("INSERT INTO QuizAttempt(Id,ProjectId,QuizId,Mode,Difficulty,Status,StartedAtUtc,MaximumScore,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,0,0,?,1,?,?)", Guid.NewGuid().ToString(), p, quiz, Now, Now, Now);
                db.Execute("INSERT INTO AttemptQuestion(Id,ProjectId,AttemptId,Position,SnapshotJson,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,'{}',?,?)", snapshot, p, attempt, Now, Now);
                db.Execute("INSERT INTO QuizAnswer(Id,ProjectId,AttemptQuestionId,Text,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,'Risposta',?,?)", Guid.NewGuid().ToString(), p, snapshot, Now, Now);
            });
            return 0;
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}


