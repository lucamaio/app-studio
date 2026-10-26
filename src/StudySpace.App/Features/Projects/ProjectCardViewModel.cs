using CommunityToolkit.Mvvm.Input;
using StudySpace.App.Services;
using StudySpace.Domain.Projects;
namespace StudySpace.App.Features.Projects;

public sealed class ProjectCardViewModel
{
    public StudyProject Project { get; }
    public string Name => Project.Name;
    public string Description => Project.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string Color => Project.Color;
    public IReadOnlyList<string> Tags => Project.Tags;
    public bool HasTags => Tags.Count > 0;
    public string Icon => IconChoice.GlyphFor(Project.IconKey);
    public string FavoriteSymbol => Project.IsFavorite ? "★" : "☆";
    public string FavoriteAction => Project.IsFavorite ? "Rimuovi dai preferiti" : "Aggiungi ai preferiti";
    public string UpdatedLabel => $"Aggiornato il {Project.UpdatedAtUtc.ToLocalTime():dd MMM yyyy}";
    public string CreatedLabel => $"Creato il {Project.CreatedAtUtc.ToLocalTime():dd MMM yyyy}";
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand MenuCommand { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand FavoriteCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public ProjectCardViewModel(StudyProject project, Func<Task> open, Func<Task> menu,
        Func<ProjectMenuAction, Task> action)
    {
        Project = project;
        OpenCommand = new AsyncRelayCommand(open);
        MenuCommand = new AsyncRelayCommand(menu);
        EditCommand = new AsyncRelayCommand(() => action(ProjectMenuAction.Edit));
        FavoriteCommand = new AsyncRelayCommand(() => action(ProjectMenuAction.ToggleFavorite));
        DeleteCommand = new AsyncRelayCommand(() => action(ProjectMenuAction.Delete));
    }
}
public sealed record IconChoice(string Key, string Title, string Glyph)
{
    public static IReadOnlyList<IconChoice> All { get; } = new[]
    {
        new IconChoice("", "Nessuna icona", ""), new("book", "Libro", "▤"),
        new("shield", "Sicurezza", "◇"), new("network", "Reti", "⌘"),
        new("calculator", "Calcolo", "∑"), new("computer", "Computer", "▣")
    };
    public static string GlyphFor(string key) => All.FirstOrDefault(i => i.Key == key)?.Glyph ?? "";
}
public sealed record ColorChoice(string Title, string Hex)
{
    public static IReadOnlyList<ColorChoice> All { get; } = new[]
    {
        new ColorChoice("Indaco", "#5B63D3"), new("Oceano", "#167B9B"),
        new("Bosco", "#287C61"), new("Terracotta", "#B25F45"),
        new("Viola", "#8854A8"), new("Rosa", "#B94B7A")
    };
}
public sealed record TagChoice(string Title, string? Value);

