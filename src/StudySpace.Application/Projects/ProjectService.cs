using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Domain.Projects;
namespace StudySpace.Application.Projects;

public sealed class ProjectService(IProjectRepository repository, IClock clock)
{
    public const int PageSize = 30;
    public static readonly IReadOnlyList<string> IconKeys = Array.AsReadOnly(new[] { "", "book", "shield", "network", "calculator", "computer" });

    public Task<IReadOnlyList<StudyProject>> ListAsync(int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return repository.ListAsync(offset, PageSize, cancellationToken);
    }
    public Task<IReadOnlyList<StudyProject>> SearchAsync(ProjectQuery query, int offset = 0, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (query.Search.Length > 200) throw new StudyValidationException("La ricerca può contenere al massimo 200 caratteri.");
        return repository.SearchAsync(query with { Search = query.Search.Trim(), Tag = query.Tag?.Trim() }, offset, PageSize, ct);
    }
    public Task<IReadOnlyList<string>> ListTagsAsync(CancellationToken ct = default) => repository.ListTagsAsync(ct);
    public async Task<StudyProject> GetAsync(Guid id, CancellationToken ct = default) =>
        await repository.GetAsync(id, ct) ?? throw new ProjectNotFoundException();
    public async Task<ProjectSummary> GetSummaryAsync(Guid id, CancellationToken ct = default) =>
        await repository.GetSummaryAsync(id, ct) ?? throw new ProjectNotFoundException();

    public Task<StudyProject> CreateAsync(string name, string? description, CancellationToken cancellationToken = default) =>
        CreateAsync(new ProjectDraft(name, description), cancellationToken);
    public async Task<StudyProject> CreateAsync(ProjectDraft draft, CancellationToken ct = default)
    {
        var clean = Validate(draft);
        var now = clock.UtcNow;
        var project = new StudyProject
        {
            Name = clean.Name, Description = clean.Description!, IconKey = clean.IconKey!,
            Color = clean.Color, IsFavorite = clean.IsFavorite, Tags = clean.Tags!,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        await repository.AddAsync(project, ct);
        return project;
    }
    public async Task UpdateAsync(Guid id, int expectedRevision, ProjectDraft draft, CancellationToken ct = default)
    {
        var clean = Validate(draft);
        var existing = await GetAsync(id, ct);
        await repository.UpdateAsync(existing with
        {
            Name = clean.Name, Description = clean.Description!, Color = clean.Color, IconKey = clean.IconKey!,
            IsFavorite = clean.IsFavorite, Tags = clean.Tags!, UpdatedAtUtc = clock.UtcNow,
            Revision = expectedRevision + 1
        }, expectedRevision, ct);
    }
    public Task SetFavoriteAsync(StudyProject project, bool favorite, CancellationToken ct = default) =>
        repository.SetFavoriteAsync(project.Id, favorite, project.Revision, clock.UtcNow, ct);
    public Task DeleteAsync(StudyProject project, CancellationToken ct = default) =>
        repository.DeleteAsync(project.Id, project.Revision, ct);

    private static ProjectDraft Validate(ProjectDraft draft)
    {
        var name = draft.Name?.Trim() ?? "";
        var description = draft.Description?.Trim() ?? "";
        var icon = draft.IconKey?.Trim() ?? "";
        var color = draft.Color?.Trim().ToUpperInvariant() ?? "";
        if (name.Length is < 1 or > 120)
            throw new StudyValidationException("Inserisci un nome da 1 a 120 caratteri.");
        if (description.Length > 2000)
            throw new StudyValidationException("La descrizione può contenere al massimo 2000 caratteri.");
        if (color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit))
            throw new StudyValidationException("Il colore deve essere nel formato #RRGGBB.");
        if (!IconKeys.Contains(icon)) throw new StudyValidationException("Seleziona un'icona disponibile.");
        var tags = (draft.Tags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0)
            .DistinctBy(ProjectText.NormalizeTag).ToArray();
        if (tags.Length > 10 || tags.Any(t => t.Length > 32 || t.Contains(',')))
            throw new StudyValidationException("Usa al massimo 10 etichette da 32 caratteri, separate da virgole.");
        return new(name, description, icon, color, draft.IsFavorite, tags);
    }
}

