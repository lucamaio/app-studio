using StudySpace.Domain.Projects;
using StudySpace.Application.Projects;
namespace StudySpace.Application.Abstractions;

public interface IProjectRepository
{
    Task<IReadOnlyList<StudyProject>> ListAsync(int offset, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudyProject>> SearchAsync(ProjectQuery query, int offset, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken cancellationToken = default);
    Task<StudyProject?> GetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectSummary?> GetSummaryAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task AddAsync(StudyProject project, CancellationToken cancellationToken = default);
    Task UpdateAsync(StudyProject project, int expectedRevision, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(Guid projectId, bool favorite, int expectedRevision, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid projectId, int expectedRevision, CancellationToken cancellationToken = default);
}
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

