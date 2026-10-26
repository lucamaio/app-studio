namespace StudySpace.App.Navigation;
public sealed class ShellNavigationService : INavigationService
{
    public Task NewQuizAsync(Guid projectId)=>Shell.Current.GoToAsync($"quiz-setup?projectId={projectId:D}");
    public Task OpenQuizAttemptAsync(Guid projectId,Guid attemptId)=>Shell.Current.GoToAsync($"quiz-run?projectId={projectId:D}&attemptId={attemptId:D}");
    public Task OpenQuizResultAsync(Guid projectId,Guid attemptId)=>Shell.Current.GoToAsync($"quiz-results?projectId={projectId:D}&attemptId={attemptId:D}");
    public Task OpenQuizEvidenceAsync(Guid projectId,Guid attemptId,Guid questionId,Guid chunkId)=>Shell.Current.GoToAsync($"quiz-evidence?projectId={projectId:D}&attemptId={attemptId:D}&questionId={questionId:D}&chunkId={chunkId:D}");
    public Task CreateProjectAsync() => Shell.Current.GoToAsync("project-create");
    public Task OpenProjectAsync(Guid projectId) => Shell.Current.GoToAsync($"project?projectId={projectId:D}");
    public Task EditProjectAsync(Guid projectId) => Shell.Current.GoToAsync($"project-edit?projectId={projectId:D}");
    public Task OpenProjectSectionAsync(Guid projectId, string section) =>
        Shell.Current.GoToAsync(section == "history" ? $"quiz-history?projectId={projectId:D}" : section == "statistics" ? $"project-statistics?projectId={projectId:D}" : section == "quizzes" ? $"quizzes?projectId={projectId:D}" : section == "sources" ? $"sources?projectId={projectId:D}" : section == "flashcards" ? $"flashcards?projectId={projectId:D}" :
            $"project-section?projectId={projectId:D}&section={Uri.EscapeDataString(section)}");
    public Task AddManualSourceAsync(Guid projectId) => Shell.Current.GoToAsync($"source-manual?projectId={projectId:D}");
    public Task OpenSourceAsync(Guid projectId, Guid sourceId) => Shell.Current.GoToAsync($"source-detail?projectId={projectId:D}&sourceId={sourceId:D}");
    public Task GoToProjectsAsync() => Shell.Current.GoToAsync("//projects");
    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
    public Task EditFlashcardAsync(Guid projectId,Guid? cardId)=>Shell.Current.GoToAsync($"flashcard-edit?projectId={projectId:D}"+(cardId.HasValue ? $"&cardId={cardId:D}" : ""));
    public Task GenerateFlashcardsAsync(Guid projectId)=>Shell.Current.GoToAsync($"flashcard-generate?projectId={projectId:D}");
    public Task StudyFlashcardsAsync(Guid projectId,StudySpace.Application.Flashcards.FlashcardQuery query)=>Shell.Current.GoToAsync(
        $"flashcard-study?projectId={projectId:D}&search={Uri.EscapeDataString(query.Search)}&tag={Uri.EscapeDataString(query.Tag ?? "")}&difficulty={(int?)query.Difficulty}&favorite={(query.FavoritesOnly ? 1 : 0)}");
}

