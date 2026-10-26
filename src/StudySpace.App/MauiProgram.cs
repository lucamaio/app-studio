using StudySpace.App.Features.Performance;
using Microsoft.Extensions.Logging;
using StudySpace.App.Features.Projects;
using StudySpace.App.Features.Settings;
using StudySpace.App.Features.Sources;
using StudySpace.App.Features.Flashcards;
using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Infrastructure.DependencyInjection;
using StudySpace.Infrastructure.Persistence;

namespace StudySpace.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddDebug();
#if ANDROID
        builder.Logging.AddProvider(new AndroidLogProvider());
#endif
        var dataDirectory = FileSystem.AppDataDirectory;
#if DEBUG
        // An explicitly selected test archive keeps UI smoke tests away from personal projects.
        var testDirectory = Environment.GetEnvironmentVariable("STUDYSPACE_TEST_DATA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(testDirectory))
        {
            if (!Path.IsPathFullyQualified(testDirectory))
                throw new ArgumentException("The test data directory must be absolute.");
            dataDirectory = testDirectory;
        }
#endif
        builder.Services.AddSingleton<StudySpace.Application.Quizzes.ISemanticEvaluationSettingsStore,SemanticEvaluationSettingsStore>();
        builder.Services.AddStudySpace(new DatabaseOptions(Path.Combine(dataDirectory, "studyspace.db3")));
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
        builder.Services.AddSingleton<IProjectDialogs, ProjectDialogs>();
        builder.Services.AddSingleton<ProjectActions>();
        builder.Services.AddSingleton<ISourceInteractions, SourceInteractions>();
        builder.Services.AddSingleton<SourceStartup>();
        builder.Services.AddSingleton<IFlashcardDialogs,FlashcardDialogs>();
        builder.Services.AddSingleton<IQuizDialogs,QuizDialogs>();
        builder.Services.AddTransient<StudySpace.App.Features.Quizzes.QuizSetupViewModel>();
        builder.Services.AddTransient<StudySpace.App.Features.Quizzes.QuizSetupPage>();
        builder.Services.AddTransient<StudySpace.App.Features.Quizzes.QuizzesViewModel>();
        builder.Services.AddTransient<StudySpace.App.Features.Quizzes.QuizzesPage>();
        builder.Services.AddTransient<StudySpace.App.Features.Quizzes.QuizRunViewModel>();
        builder.Services.AddTransient<StudySpace.App.Features.Quizzes.QuizRunPage>();
        builder.Services.AddTransient<QuizResultsViewModel>();
        builder.Services.AddTransient<QuizResultsPage>();
        builder.Services.AddTransient<QuizHistoryViewModel>();
        builder.Services.AddTransient<QuizHistoryPage>();
        builder.Services.AddTransient<ProjectStatisticsViewModel>();
        builder.Services.AddTransient<ProjectStatisticsPage>();
        builder.Services.AddTransient<QuizEvidenceViewModel>();
        builder.Services.AddTransient<QuizEvidencePage>();
        builder.Services.AddTransient<StudyMaterialPickerViewModel>();
        builder.Services.AddTransient<FlashcardsViewModel>();
        builder.Services.AddTransient<FlashcardsPage>();
        builder.Services.AddTransient<EditFlashcardViewModel>();
        builder.Services.AddTransient<EditFlashcardPage>();
        builder.Services.AddTransient<StudyFlashcardsViewModel>();
        builder.Services.AddTransient<StudyFlashcardsPage>();
        builder.Services.AddTransient<GenerateFlashcardsViewModel>();
        builder.Services.AddTransient<GenerateFlashcardsPage>();
        builder.Services.AddTransient<SourcesViewModel>();
        builder.Services.AddTransient<SourcesPage>();
        builder.Services.AddTransient<ManualSourceViewModel>();
        builder.Services.AddTransient<ManualSourcePage>();
        builder.Services.AddTransient<SourceDetailViewModel>();
        builder.Services.AddTransient<SourceDetailPage>();
        builder.Services.AddTransient<ProjectsViewModel>();
        builder.Services.AddTransient<ProjectsPage>();
        builder.Services.AddTransient<CreateProjectViewModel>();
        builder.Services.AddTransient<CreateProjectPage>();
        builder.Services.AddTransient<ProjectDashboardViewModel>();
        builder.Services.AddTransient<ProjectDashboardPage>();
        builder.Services.AddTransient<ProjectSectionViewModel>();
        builder.Services.AddTransient<ProjectSectionPage>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<AppShell>();
        return builder.Build();
    }
}



