namespace StudySpace.App.Services;
public sealed class QuizDialogs:IQuizDialogs
{
    public Task<bool> ConfirmFinishAsync(int unanswered)=>Shell.Current.DisplayAlertAsync("Termina quiz",
        unanswered>0?$"Ci sono {unanswered} domande senza risposta. Terminare e valutare le risposte aperte salvate?":"Terminare e valutare le risposte aperte non ancora verificate?",
        "Termina","Continua");
    public Task<bool> ConfirmAbandonAsync()=>Shell.Current.DisplayAlertAsync("Abbandona tentativo",
        "Le risposte salvate resteranno consultabili, ma non potrai continuare questo tentativo. Per riprenderlo più tardi usa invece Salva e esci.","Abbandona","Annulla");
}

