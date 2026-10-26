namespace StudySpace.App.Features.Flashcards;
public partial class FlashcardsPage:ContentPage,IQueryAttributable
{
    private readonly FlashcardsViewModel viewModel;
    public FlashcardsPage(FlashcardsViewModel viewModel) { InitializeComponent(); BindingContext=this.viewModel=viewModel; }
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(query.TryGetValue("projectId",out var p) ? p?.ToString() ?? "" : "");
    protected override void OnAppearing() { base.OnAppearing(); _=viewModel.LoadCommand.ExecuteAsync(null); }
}
