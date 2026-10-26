namespace StudySpace.App.Features.Flashcards;
public partial class GenerateFlashcardsPage:ContentPage,IQueryAttributable
{
    private readonly GenerateFlashcardsViewModel viewModel;
    public GenerateFlashcardsPage(GenerateFlashcardsViewModel viewModel) { InitializeComponent(); BindingContext=this.viewModel=viewModel; }
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(query.TryGetValue("projectId",out var p) ? p?.ToString() ?? "" : "");
    protected override void OnAppearing() { base.OnAppearing(); _=viewModel.LoadCommand.ExecuteAsync(null); }
}
