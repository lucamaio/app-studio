using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
namespace StudySpace.App.Features.Projects;

public partial class ProjectSectionViewModel(ProjectService projects, ILogger<ProjectSectionViewModel> logger) : ViewModelBase(logger)
{
    public static IReadOnlyDictionary<string, string> Sections { get; } = new Dictionary<string, string>
    {
        ["sources"] = "Fonti", ["flashcards"] = "Flashcard", ["quizzes"] = "Quiz",
        ["history"] = "Storico", ["statistics"] = "Statistiche"
    };
    private Guid projectId;
    private string section = "";
    [ObservableProperty] public partial string Title { get; set; } = "Progetto";
    [ObservableProperty] public partial string ProjectName { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial bool IsReady { get; set; }
    public void Configure(string rawId, string sectionKey)
    {
        projectId = Guid.TryParse(rawId, out var id) ? id : Guid.Empty;
        section = sectionKey;
    }
    [RelayCommand] private Task LoadAsync() => RunAsync(async () =>
    {
        IsReady = false;
        if (!Sections.TryGetValue(section, out var title)) throw new ProjectNotFoundException();
        var project = await projects.GetAsync(projectId);
        Title = title; ProjectName = project.Name;
        Message = section switch
        {
            "sources" => "Qui raccoglierai PDF, documenti e appunti di questa materia. L'importazione sarà disponibile nella prossima fase.",
            "flashcards" => "Qui creerai e studierai le flashcard di questo progetto. La funzione sarà disponibile in una prossima fase.",
            "quizzes" => "Qui potrai metterti alla prova sui contenuti di questo progetto. I quiz saranno disponibili in una prossima fase.",
            "history" => "Qui ritroverai i tentativi e le valutazioni dei quiz di questo progetto.",
            _ => "Qui seguirai i progressi e gli argomenti da ripassare di questo progetto."
        };
        IsReady = true;
    });
}

