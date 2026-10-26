using StudySpace.App.Features.Performance;
using StudySpace.App.Controls;
using StudySpace.App.Features.Projects;
using StudySpace.App.Features.Settings;
using StudySpace.App.Features.Sources;
using StudySpace.App.Features.Flashcards;
using StudySpace.App.Features.Quizzes;
namespace StudySpace.App;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();
        AddSection("Progetti", "projects", () => services.GetRequiredService<ProjectsPage>());
        AddSection("Preferiti", "favorites", () =>
        {
            var page = services.GetRequiredService<ProjectsPage>();
            page.ConfigureFavorites();
            return page;
        });
        AddSection("Statistiche", "statistics", () => new PlaceholderPage(
            "Statistiche", "Ogni passo conta",
            "Qui potrai seguire i tuoi progressi e scoprire gli argomenti da ripassare. Apri Statistiche dalla dashboard di un progetto per seguire i risultati."));
        AddSection("Impostazioni", "settings", () => services.GetRequiredService<SettingsPage>());
        Routing.RegisterRoute("project-create", typeof(CreateProjectPage));
        Routing.RegisterRoute("project-edit", typeof(CreateProjectPage));
        Routing.RegisterRoute("project", typeof(ProjectDashboardPage));
        Routing.RegisterRoute("project-section", typeof(ProjectSectionPage));
        Routing.RegisterRoute("sources", typeof(SourcesPage));
        Routing.RegisterRoute("source-manual", typeof(ManualSourcePage));
        Routing.RegisterRoute("source-detail", typeof(SourceDetailPage));
        Routing.RegisterRoute("flashcards",typeof(FlashcardsPage));
        Routing.RegisterRoute("flashcard-edit",typeof(EditFlashcardPage));
        Routing.RegisterRoute("flashcard-study",typeof(StudyFlashcardsPage));
        Routing.RegisterRoute("flashcard-generate",typeof(GenerateFlashcardsPage));
        Routing.RegisterRoute("quiz-setup",typeof(QuizSetupPage));
        Routing.RegisterRoute("quizzes",typeof(QuizzesPage));
        Routing.RegisterRoute("quiz-run",typeof(QuizRunPage));
        Routing.RegisterRoute("quiz-results",typeof(QuizResultsPage));
        Routing.RegisterRoute("quiz-history",typeof(QuizHistoryPage));
        Routing.RegisterRoute("project-statistics",typeof(ProjectStatisticsPage));
        Routing.RegisterRoute("quiz-evidence",typeof(QuizEvidencePage));
        SizeChanged += (_, _) => FlyoutBehavior = Width >= Design.DesignMetrics.Value("DesktopNavigationBreakpoint") ? FlyoutBehavior.Locked : FlyoutBehavior.Flyout;
    }
    private void AddSection(string title, string route, Func<Page> factory)
    {
        var content = new ShellContent { Title = title, Route = route + "-content", ContentTemplate = new DataTemplate(factory) };
        var section = new ShellSection { Title = title, Route = route + "-section" };
        section.Items.Add(content);
        var iconKey=route switch {"projects"=>"ProjectsIcon","favorites"=>"FavoritesIcon","statistics"=>"StatisticsIcon",_=>"SettingsIcon"};
        var item = new FlyoutItem { Title = title, Route = route, Icon=(ImageSource)Microsoft.Maui.Controls.Application.Current!.Resources[iconKey] };
        item.Items.Add(section);
        Items.Add(item);
    }
}




