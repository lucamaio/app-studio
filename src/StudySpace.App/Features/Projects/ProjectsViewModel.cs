using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Projects;
using StudySpace.Domain.Projects;
namespace StudySpace.App.Features.Projects;

public partial class ProjectsViewModel(ProjectService projects, INavigationService navigation,
    ProjectActions actions, ILogger<ProjectsViewModel> logger) : ViewModelBase(logger)
{
    private ProjectQuery? appliedQuery;
    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];
    public ObservableCollection<TagChoice> TagFilters { get; } = [new("Tutte le etichette", null)];
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial TagChoice? SelectedTag { get; set; }
    [ObservableProperty] public partial bool FavoritesOnly { get; set; }
    [ObservableProperty] public partial bool FavoritesPage { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty] public partial bool ShowFilters { get; set; }
    [RelayCommand] private void ToggleFilters()=>ShowFilters=!ShowFilters;
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string EmptyHeading { get; set; } = "";
    [ObservableProperty] public partial string EmptyMessage { get; set; } = "";
    public string Title => FavoritesPage ? "Preferiti" : "Progetti";
    public bool CanChangeFavoriteFilter => !FavoritesPage;

    public void ConfigureFavorites()
    {
        FavoritesPage = true;
        FavoritesOnly = true;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanChangeFavoriteFilter));
    }
    private ProjectQuery Query => new(SearchText, SelectedTag?.Value, FavoritesPage || FavoritesOnly);

    [RelayCommand]
    private Task LoadAsync(CancellationToken ct) => RunAsync(() => ReloadAsync(ct));

    private async Task ReloadAsync(CancellationToken ct = default)
    {
        IsEmpty = false;
        HasMore = false;
        var query = Query;
        var page = await projects.SearchAsync(query, 0, ct);
        var tags = await projects.ListTagsAsync(ct);
        ct.ThrowIfCancellationRequested();
        Projects.Clear();
        foreach (var p in page) Projects.Add(Card(p));
        appliedQuery = query;
        HasMore = page.Count == ProjectService.PageSize;
        var chosenTag = SelectedTag?.Value;
        TagFilters.Clear();
        TagFilters.Add(new("Tutte le etichette", null));
        foreach (var tag in tags) TagFilters.Add(new(tag, tag));
        // Keep an active filter visible even if its last project was removed.
        if (chosenTag is not null && !tags.Contains(chosenTag))
            TagFilters.Add(new(chosenTag, chosenTag));
        SelectedTag = TagFilters.FirstOrDefault(t => t.Value == chosenTag) ?? TagFilters[0];
        IsEmpty = Projects.Count == 0;
        var filtered = !string.IsNullOrWhiteSpace(query.Search) || query.Tag is not null || query.FavoritesOnly;
        EmptyHeading = filtered ? "Nessun progetto trovato" : "Il tuo primo progetto ti aspetta";
        EmptyMessage = filtered ? "Prova un'altra ricerca o cambia i filtri. I preferiti si aggiungono con la stella."
            : "Crea uno spazio per una materia, ad esempio Reti di Calcolatori, e inizia a organizzare lo studio.";
    }

    [RelayCommand]
    private Task LoadMoreAsync(CancellationToken ct) => RunAsync(async () =>
    {
        if (Query != appliedQuery) { await ReloadAsync(ct); return; }
        if (!HasMore) return;
        var page = await projects.SearchAsync(Query, Projects.Count, ct);
        foreach (var p in page) Projects.Add(Card(p));
        HasMore = page.Count == ProjectService.PageSize;
    });

    [RelayCommand]
    private Task ResetFiltersAsync() => RunAsync(async () =>
    {
        SearchText = "";
        SelectedTag = TagFilters[0];
        FavoritesOnly = FavoritesPage;
        await ReloadAsync();
    });
    [RelayCommand]
    private Task CreateAsync() => RunAsync(navigation.CreateProjectAsync);

    private ProjectCardViewModel Card(StudyProject project) => new(project,
        () => RunAsync(() => navigation.OpenProjectAsync(project.Id)),
        () => RunAsync(async () => { if (await actions.ShowMenuAsync(project) != ProjectActionResult.None) await ReloadAsync(); }),
        action => RunAsync(async () => { if (await actions.ExecuteAsync(project, action) != ProjectActionResult.None) await ReloadAsync(); }));
}

