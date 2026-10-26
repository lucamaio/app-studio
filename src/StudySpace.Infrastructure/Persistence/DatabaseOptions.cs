namespace StudySpace.Infrastructure.Persistence;

public sealed record DatabaseOptions
{
    public string DatabasePath { get; }
    public DatabaseOptions(string databasePath)
    {
        if (!Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("Database path must be absolute.", nameof(databasePath));
        DatabasePath = databasePath;
    }
}

