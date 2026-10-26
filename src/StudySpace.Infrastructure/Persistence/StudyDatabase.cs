using Microsoft.Extensions.Logging;
using SQLite;
using StudySpace.Application.Common;
namespace StudySpace.Infrastructure.Persistence;

// All access is serialized and executed off the UI thread. No pooled connection escapes.
public sealed class StudyDatabase(DatabaseOptions options, ILogger<StudyDatabase> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private const int CurrentVersion = 6;

    internal async Task<T> ExecuteAsync<T>(Func<SQLiteConnection, T> operation, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
                using var connection = new SQLiteConnection(options.DatabasePath,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                connection.BusyTimeout = TimeSpan.FromSeconds(5);
                connection.Execute("PRAGMA foreign_keys = ON");
                if (connection.ExecuteScalar<int>("PRAGMA foreign_keys") != 1)
                    throw new InvalidOperationException("Foreign key enforcement unavailable.");
                Migrate(connection);
                // Once the operation starts, report its real result, even if cancellation races with commit.
                ct.ThrowIfCancellationRequested();
                return operation(connection);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SQLiteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Do not log SQL parameters, user content or local paths.
            logger.LogError("Database operation failed. Error type: {ErrorType}", ex.GetType().Name);
            throw new StorageUnavailableException("Non è possibile accedere ai dati locali. Riprova.", ex);
        }
        finally { gate.Release(); }
    }

    private static void Migrate(SQLiteConnection connection)
    {
        connection.Execute("CREATE TABLE IF NOT EXISTS SchemaMigration (Version INTEGER PRIMARY KEY, AppliedAtUtc TEXT NOT NULL)");
        var version = connection.ExecuteScalar<int>("SELECT COALESCE(MAX(Version), 0) FROM SchemaMigration");
        if (version > CurrentVersion)
            throw new InvalidOperationException("Database was created by a newer application.");
        if (version == CurrentVersion) return;

        var migrations = new[] { "001_Initial.sql", "002_ProjectSearch.sql", "003_SourceImport.sql", "004_Flashcards.sql", "005_Quizzes.sql", "006_PerformanceIndex.sql" };
        for (var next = version + 1; next <= CurrentVersion; next++)
        {
            using var stream = typeof(StudyDatabase).Assembly.GetManifestResourceStream(
                "StudySpace.Infrastructure.Persistence.Migrations." + migrations[next - 1])
                ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(stream);
            var script = reader.ReadToEnd();
            connection.RunInTransaction(() =>
            {
                // These migrations contain simple DDL, no triggers or semicolons in literals.
                foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    connection.Execute(statement);
                if (next == 2)
                    foreach (var row in connection.Query<ProjectRow>("SELECT * FROM StudyProject"))
                        connection.Execute("UPDATE StudyProject SET SearchText=? WHERE Id=?",
                            ProjectRepository.SearchText(row.ToDomain()), row.Id);
                if (next == 4)
                    foreach (var row in connection.Query<FlashcardRow>("SELECT * FROM FlashCard"))
                        connection.Execute("UPDATE FlashCard SET SearchText=? WHERE ProjectId=? AND Id=?",
                            FlashcardRepository.SearchText(row.Front,row.Back,row.Explanation),row.ProjectId,row.Id);
                connection.Execute("INSERT INTO SchemaMigration(Version, AppliedAtUtc) VALUES (?, ?)",
                    next, DateTimeOffset.UtcNow.ToString("O"));
            });
        }
    }
}

