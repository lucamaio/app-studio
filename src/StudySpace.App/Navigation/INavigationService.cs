namespace StudySpace.App.Navigation;
public interface INavigationService
{
    Task CreateProjectAsync();
    Task OpenProjectAsync(Guid projectId);
    Task EditProjectAsync(Guid projectId);
    Task OpenProjectSectionAsync(Guid projectId, string section);
    Task GoToProjectsAsync();
    Task GoBackAsync();
    Task AddManualSourceAsync(Guid projectId);
    Task OpenSourceAsync(Guid projectId, Guid sourceId);
    Task EditFlashcardAsync(Guid projectId,Guid? cardId);
    Task GenerateFlashcardsAsync(Guid projectId);
    Task StudyFlashcardsAsync(Guid projectId,StudySpace.Application.Flashcards.FlashcardQuery query);
    Task NewQuizAsync(Guid projectId);
    Task OpenQuizAttemptAsync(Guid projectId,Guid attemptId);
    Task OpenQuizResultAsync(Guid projectId,Guid attemptId);
    Task OpenQuizEvidenceAsync(Guid projectId,Guid attemptId,Guid questionId,Guid chunkId);
}

