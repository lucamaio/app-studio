namespace StudySpace.App.Features.Flashcards;
public partial class EditFlashcardPage:ContentPage,IQueryAttributable
{
    private readonly EditFlashcardViewModel viewModel;
    public EditFlashcardPage(EditFlashcardViewModel viewModel) { InitializeComponent(); BindingContext=this.viewModel=viewModel; }
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(query.TryGetValue("projectId",out var p) ? p?.ToString() ?? "" : "",
        query.TryGetValue("cardId",out var c) ? c?.ToString() : null);
    protected override void OnAppearing() { base.OnAppearing(); _=viewModel.LoadCommand.ExecuteAsync(null); }
}
