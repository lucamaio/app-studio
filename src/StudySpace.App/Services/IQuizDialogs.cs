namespace StudySpace.App.Services;
public interface IQuizDialogs
{
    Task<bool> ConfirmFinishAsync(int unanswered);
    Task<bool> ConfirmAbandonAsync();
}

