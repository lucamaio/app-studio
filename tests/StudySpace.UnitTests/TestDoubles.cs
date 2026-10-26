using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Projects;

namespace StudySpace.UnitTests;

internal class TestProjectRepository : IProjectRepository
{
    public List<StudyProject> Items { get; } = [];
    public int SourceCount {get;set;} public int FlashcardCount {get;set;}
    public bool FailReads { get; set; }
    public int DeleteCalls { get; private set; }
    public int FavoriteCalls { get; private set; }
    public Task AddAsync(StudyProject project, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Items.Add(project); return Task.CompletedTask;
    }
    public Task<StudyProject?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.SingleOrDefault(p => p.Id == id));
    public Task<IReadOnlyList<StudyProject>> ListAsync(int offset, int limit, CancellationToken cancellationToken = default) =>
        SearchAsync(new(), offset, limit, cancellationToken);
    public Task<IReadOnlyList<StudyProject>> SearchAsync(ProjectQuery q, int offset, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailReads) throw new StorageUnavailableException("Archivio non disponibile.", new IOException());
        return Task.FromResult<IReadOnlyList<StudyProject>>(Items.Where(p =>
            (!q.FavoritesOnly || p.IsFavorite) &&
            (q.Tag is null || p.Tags.Any(t => ProjectText.NormalizeTag(t) == ProjectText.NormalizeTag(q.Tag))) &&
            ProjectText.SearchKey(p.Name + p.Description).Contains(ProjectText.SearchKey(q.Search)))
            .Skip(offset).Take(limit).ToList());
    }
    public Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Items.SelectMany(p => p.Tags).Distinct().ToList());
    public async Task<ProjectSummary?> GetSummaryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var p = await GetAsync(id, cancellationToken);
        return p is null ? null : new(p, SourceCount, FlashcardCount, 0);
    }
    public Task UpdateAsync(StudyProject p, int expectedRevision, CancellationToken cancellationToken = default)
    {
        var index = Items.FindIndex(x => x.Id == p.Id && x.Revision == expectedRevision);
        if (index < 0) throw new ProjectConflictException();
        Items[index] = p; return Task.CompletedTask;
    }
    public Task SetFavoriteAsync(Guid id, bool favorite, int expectedRevision, DateTimeOffset modifiedAt, CancellationToken cancellationToken = default)
    {
        FavoriteCalls++;
        var p = Items.Single(x => x.Id == id);
        return UpdateAsync(p with { IsFavorite = favorite, Revision = expectedRevision + 1, UpdatedAtUtc = modifiedAt }, expectedRevision, cancellationToken);
    }
    public Task DeleteAsync(Guid id, int expectedRevision, CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        if (Items.RemoveAll(p => p.Id == id && p.Revision == expectedRevision) != 1) throw new ProjectConflictException();
        return Task.CompletedTask;
    }
}
internal class TestNavigation : INavigationService
{
    public bool FailBack { get; set; }
    public int BackCalls { get; private set; }
    public Guid? OpenedProject { get; private set; }
    public string? Section { get; private set; }
    public bool AtProjects { get; private set; }
    public (Guid ProjectId,Guid? CardId)? EditedFlashcard { get; private set; }
    public StudySpace.Application.Flashcards.FlashcardQuery? StudyQuery { get; private set; }
    public Task CreateProjectAsync() => Task.CompletedTask;
    public Task NewQuizAsync(Guid projectId)=>Task.CompletedTask;
    public Guid? OpenedResult {get;private set;}
    public (Guid Project,Guid Attempt,Guid Question,Guid Chunk)? OpenedEvidence {get;private set;}
    public Task OpenQuizResultAsync(Guid projectId,Guid attemptId){OpenedResult=attemptId;return Task.CompletedTask;}
    public Task OpenQuizEvidenceAsync(Guid projectId,Guid attemptId,Guid questionId,Guid chunkId){OpenedEvidence=(projectId,attemptId,questionId,chunkId);return Task.CompletedTask;}
    public bool FailQuizOpen { get; set; }
    public Guid? OpenedQuizAttempt {get;private set;}
    public Task OpenQuizAttemptAsync(Guid projectId,Guid attemptId)
    {if(FailQuizOpen)throw new InvalidOperationException("Navigation failure");OpenedQuizAttempt=attemptId;return Task.CompletedTask;}
    public Task AddManualSourceAsync(Guid projectId) => Task.CompletedTask;
    public Task OpenSourceAsync(Guid projectId, Guid sourceId) => Task.CompletedTask;
    public Task EditFlashcardAsync(Guid projectId,Guid? cardId) { EditedFlashcard=(projectId,cardId); return Task.CompletedTask; }
    public Task GenerateFlashcardsAsync(Guid projectId)=>Task.CompletedTask;
    public Task StudyFlashcardsAsync(Guid projectId,StudySpace.Application.Flashcards.FlashcardQuery query) { OpenedProject=projectId; StudyQuery=query; return Task.CompletedTask; }
    public Task OpenProjectAsync(Guid id) { OpenedProject = id; return Task.CompletedTask; }
    public Task EditProjectAsync(Guid id) { OpenedProject = id; return Task.CompletedTask; }
    public Task OpenProjectSectionAsync(Guid id, string section) { OpenedProject = id; Section = section; return Task.CompletedTask; }
    public Task GoToProjectsAsync() { AtProjects = true; return Task.CompletedTask; }
    public Task GoBackAsync()
    {
        BackCalls++;
        if (FailBack) throw new InvalidOperationException("Navigation failure");
        return Task.CompletedTask;
    }
}
internal sealed class TestDialogs : IProjectDialogs
{
    public bool Confirmed { get; set; }
    public int ConfirmCalls { get; private set; }
    public string? ConfirmedName { get; private set; }
    public ProjectMenuAction Action { get; set; }
    public Task<ProjectMenuAction> ShowMenuAsync(string projectName, bool isFavorite) => Task.FromResult(Action);
    public Task<bool> ConfirmDeleteAsync(string projectName)
    {
        ConfirmCalls++; ConfirmedName = projectName; return Task.FromResult(Confirmed);
    }
}
internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
}


