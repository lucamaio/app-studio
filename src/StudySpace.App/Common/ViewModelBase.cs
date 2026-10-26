using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using StudySpace.Application.Common;
namespace StudySpace.App.Common;

public abstract partial class ViewModelBase(ILogger logger) : ObservableObject
{
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    protected async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (StudyValidationException ex) { ErrorMessage = ex.Message; }
        catch (StorageUnavailableException ex) { ErrorMessage = ex.Message; }
        catch (ProjectConflictException ex) { ErrorMessage = ex.Message; }
        catch (ProjectNotFoundException ex) { ErrorMessage = ex.Message; }
        catch (StudySpace.Application.Sources.SourceNotFoundException ex) { ErrorMessage = ex.Message; }
        catch (StudySpace.Application.Sources.SourceConflictException ex) { ErrorMessage = ex.Message; }
        catch (StudySpace.Application.Flashcards.FlashcardNotFoundException ex) { ErrorMessage = ex.Message; }
        catch (StudySpace.Application.Flashcards.FlashcardConflictException ex) { ErrorMessage = ex.Message; }
        catch (StudySpace.Application.Flashcards.AiStudyException ex) { ErrorMessage = ex.Message; }
        catch (StudySpace.Application.Quizzes.QuizNotFoundException ex) { ErrorMessage = ex.Message; }
        catch (StudySpace.Application.Quizzes.QuizConflictException ex) { ErrorMessage = ex.Message; }
        catch (Exception ex)
        {
            var errorId = Guid.NewGuid().ToString("N")[..8];
            logger.LogError("Operation failed: {ErrorId}, {ErrorType}", errorId, ex.GetType().Name);
            ErrorMessage = $"Operazione non riuscita. Riprova. Riferimento: {errorId}";
        }
        finally { IsBusy = false; }
    }
}

