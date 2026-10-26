using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using StudySpace.Application.Sources;
using StudySpace.Infrastructure.Persistence;
namespace StudySpace.Infrastructure.Files;

public sealed class SourceFileStore(DatabaseOptions database, SourceImportOptions options, ILogger<SourceFileStore> logger) : ISourceFileStore
{
    private readonly string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "sources"));
    private static readonly HashSet<string> Extensions = [".pdf", ".txt", ".md", ".markdown", ".docx"];

    public async Task<StoredSourceFile> SaveAsync(Guid projectId, Guid sourceId, Guid revisionId, string extension, Stream input, CancellationToken ct)
    {
        if (!Extensions.Contains(extension)) throw new ArgumentException("Unsupported extension.");
        var relative = $"sources/{projectId:D}/{sourceId:D}/{revisionId:D}/original{extension}";
        var finalPath = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        VerifyNoLinks(finalPath);
        var temporary = finalPath + ".part";
        try
        {
            long size = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    size += read;
                    if (size > options.MaxFileBytes) throw new SourceImportException("FileTooLarge", $"Il file supera il limite di {options.MaxFileBytes/(1024d*1024):0.##} MB.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            if (size == 0) throw new SourceImportException("EmptyFile", "Il file selezionato è vuoto.");
            File.Move(temporary, finalPath);
            return new(relative, size, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(Resolve(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read));
    }
    public Task DeleteProjectAsync(Guid projectId) => Task.Run(() =>
    {
        try
        {
            var path = Path.Combine(root, projectId.ToString("D"));
            VerifyNoLinks(path);
            if (!Directory.Exists(path)) return;
            // Resolve and check every descendant before removing anything; never follow reparse points.
            var files = Collect(path).ToArray();
            foreach (var file in files) File.Delete(file);
            Prune(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { logger.LogWarning("Deferred source file cleanup: {ErrorType}", ex.GetType().Name); }
    });
    public Task CleanupOrphansAsync(IReadOnlySet<string> referencedPaths, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            VerifyNoLinks(root);
            if (!Directory.Exists(root)) return;
            var files = Collect(root).ToArray();
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var relative = "sources/" + Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!referencedPaths.Contains(relative) && IsManagedFile(relative)) File.Delete(file);
            }
            Prune(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { logger.LogWarning("Deferred orphan cleanup: {ErrorType}", ex.GetType().Name); }
    }, ct);
    private string Resolve(string relative)
    {
        if (!IsManagedFile(relative) || relative.EndsWith(".part", StringComparison.Ordinal)) throw new IOException("Invalid source path.");
        var absolute = Path.GetFullPath(Path.Combine(root, relative[8..].Replace('/', Path.DirectorySeparatorChar)));
        if (!absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid source path.");
        VerifyNoLinks(absolute);
        return absolute;
    }
    private static bool IsManagedFile(string relative)
    {
        var parts = relative.Split('/');
        if (parts.Length != 5 || parts[0] != "sources" || parts.Skip(1).Take(3).Any(p => !Guid.TryParseExact(p, "D", out _))) return false;
        var file = parts[4];
        if (file.EndsWith(".part", StringComparison.Ordinal)) file = file[..^5];
        return Extensions.Any(e => file == "original" + e);
    }
    private void VerifyNoLinks(string path)
    {
        // The platform-owned app-data directory is trusted. Android exposes system-managed
        // aliases/mounts above it; only our sources root and its descendants are under our control.
        var current=Path.GetFullPath(path);
        if(current != root && !current.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid source path.");
        while(true)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked source paths are not supported.");
            if(current == root) break;
            current=Path.GetDirectoryName(current)!;
        }
    }
    private IEnumerable<string> Collect(string directory)
    {
        VerifyNoLinks(directory);
        foreach (var item in Directory.EnumerateFileSystemEntries(directory))
        {
            VerifyNoLinks(item);
            if (Directory.Exists(item)) { foreach (var child in Collect(item)) yield return child; }
            else yield return item;
        }
    }
    private void Prune(string directory)
    {
        VerifyNoLinks(directory);
        foreach (var child in Directory.EnumerateDirectories(directory)) Prune(child);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
}
