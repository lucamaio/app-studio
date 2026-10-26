using Microsoft.Extensions.DependencyInjection.Extensions;
using StudySpace.Application.Quizzes;
using Microsoft.Extensions.DependencyInjection;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Projects;
using StudySpace.Infrastructure.Persistence;
using StudySpace.Application.Sources;
using StudySpace.Infrastructure.Files;
using StudySpace.Infrastructure.Import;
using StudySpace.Application.Flashcards;
using StudySpace.Infrastructure.Ai;
namespace StudySpace.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStudySpace(this IServiceCollection services, DatabaseOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<StudyDatabase>();
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton(new SourceImportOptions());
        services.AddSingleton<ISourceRepository, SourceRepository>();
        services.AddSingleton<ISourceFileStore, SourceFileStore>();
        services.AddSingleton<ISourceChunkingService, SourceChunkingService>();
        services.AddSingleton<IDocumentTextExtractor, PlainTextExtractor>();
        services.AddSingleton<IDocumentTextExtractor, MarkdownTextExtractor>();
        services.AddSingleton<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddSingleton<IDocumentTextExtractor, DocxTextExtractor>();
        services.AddSingleton<ISourceImporter, SourceImportService>();
        services.AddSingleton<SourceService>();
        services.AddSingleton<IStudyContextRepository,StudyContextRepository>();
        services.AddSingleton<IFlashcardRepository,FlashcardRepository>();
        services.AddSingleton<FlashcardService>();
        services.AddSingleton<MockAiStudyService>();
        services.TryAddSingleton<ISemanticEvaluationSettingsStore,MemorySemanticEvaluationSettingsStore>();
        services.AddSingleton(EvaluationPolicy.Default);
        services.AddSingleton(_=>new HttpClient(new SocketsHttpHandler { AllowAutoRedirect=false, PooledConnectionLifetime=TimeSpan.FromMinutes(5) }) { Timeout=Timeout.InfiniteTimeSpan });
        services.AddSingleton<IAiStudyService,OllamaStudyService>();
        services.AddSingleton<FlashcardGenerationService>();
        services.AddSingleton<StudySpace.Application.Quizzes.IQuizRepository,QuizRepository>();
        services.AddSingleton<StudySpace.Application.Quizzes.QuizService>();
        services.AddSingleton<StudySpace.Application.Quizzes.QuizAttemptService>();
        services.AddSingleton<StudySpace.Application.Performance.IStudyPerformanceRepository,StudyPerformanceRepository>();
        services.AddSingleton<StudySpace.Application.Performance.StudyPerformanceService>();
        return services;
    }
    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}


