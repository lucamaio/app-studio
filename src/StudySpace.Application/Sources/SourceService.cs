using StudySpace.Application.Projects;
namespace StudySpace.Application.Sources;

public sealed class SourceService(ISourceRepository repository, ISourceImporter importer, ProjectService projects,
    StudySpace.Application.Abstractions.IClock clock)
{
    public const int PageSize = 30;
    public async Task<IReadOnlyList<SourceSummary>> ListAsync(Guid projectId, int offset = 0, CancellationToken ct = default)
    {
        await importer.InitializeAsync(ct);
        await projects.GetAsync(projectId, ct);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return await repository.ListAsync(projectId, offset, PageSize, ct);
    }
    public async Task<SourceDetails> GetAsync(Guid projectId, Guid sourceId, CancellationToken ct = default)
    {
        await importer.InitializeAsync(ct);
        return await repository.GetAsync(projectId, sourceId, ct) ?? throw new SourceNotFoundException();
    }
    public Task<IReadOnlyList<StudySpace.Domain.Sources.SourceChunk>> GetChunksAsync(Guid projectId, Guid sourceId, int offset, CancellationToken ct = default) =>
        repository.GetChunksAsync(projectId, sourceId, offset, 20, ct);
    public Task RenameAsync(SourceSummary source, string title, CancellationToken ct = default) =>
        repository.RenameAsync(source.Source.ProjectId, source.Source.Id, source.Source.Revision,
            SourceFormats.Title(title), clock.UtcNow, ct);
    public Task ArchiveAsync(SourceSummary source, CancellationToken ct = default) =>
        repository.ArchiveAsync(source.Source.ProjectId, source.Source.Id, source.Source.Revision, clock.UtcNow, ct);
}
