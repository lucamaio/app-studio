using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
namespace StudySpace.App.Features.Projects;

// Shared form for create and edit. It retains drafts across ordinary appearances.
public partial class CreateProjectViewModel(ProjectService projects, INavigationService navigation,
    ILogger<CreateProjectViewModel> logger) : ViewModelBase(logger)
{
    private Guid? projectId;
    private int expectedRevision;
    private bool loaded;
    private bool invalidRoute;
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial string TagsText { get; set; } = "";
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    public partial bool IsSaved { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Heading))]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    public partial bool IsEditing { get; set; }
    [ObservableProperty] public partial bool IsReady { get; set; } = true;
    [ObservableProperty] public partial IconChoice? SelectedIcon { get; set; } = IconChoice.All[1];
    [ObservableProperty] public partial ColorChoice? SelectedColor { get; set; } = ColorChoice.All[0];
    public IReadOnlyList<IconChoice> Icons => IconChoice.All;
    public ObservableCollection<ColorChoice> Colors { get; } = new(ColorChoice.All);
    public string Title => IsEditing ? "Modifica progetto" : "Nuovo progetto";
    public string Heading => IsEditing ? "Il tuo progetto, aggiornato" : "Dai spazio a una nuova materia";
    public string SaveLabel => IsSaved ? "Salvato · Torna indietro" : IsEditing ? "Salva modifiche" : "Crea progetto";

    public void Configure(string rawId)
    {
        IsEditing = true;
        invalidRoute = !Guid.TryParse(rawId, out var id) || id == Guid.Empty;
        projectId = invalidRoute ? null : id;
        IsReady = false;
        loaded = false;
    }
    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        if (invalidRoute) throw new ProjectNotFoundException();
        if (projectId is null || loaded) return;
        var p = await projects.GetAsync(projectId.Value);
        Name = p.Name; Description = p.Description; TagsText = string.Join(", ", p.Tags);
        IsFavorite = p.IsFavorite;
        SelectedIcon = Icons.FirstOrDefault(i => i.Key == p.IconKey) ?? Icons[0];
        SelectedColor = Colors.FirstOrDefault(c => c.Hex.Equals(p.Color, StringComparison.OrdinalIgnoreCase));
        if (SelectedColor is null)
        {
            var custom = new ColorChoice("Colore attuale", p.Color);
            Colors.Add(custom); SelectedColor = custom;
        }
        expectedRevision = p.Revision;
        loaded = true; IsReady = true;
    });
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsBusy) return;
        loaded = false; IsReady = false;
        await LoadAsync();
    }
    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (!IsReady || invalidRoute) throw new ProjectNotFoundException();
        if (!IsSaved)
        {
            var draft = new ProjectDraft(Name, Description, SelectedIcon?.Key ?? "",
                SelectedColor?.Hex ?? "", IsFavorite, TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries));
            if (projectId is { } id) await projects.UpdateAsync(id, expectedRevision, draft);
            else await projects.CreateAsync(draft);
            IsSaved = true;
        }
        await navigation.GoBackAsync();
    });
}

