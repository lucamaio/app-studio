namespace StudySpace.App.Features.Flashcards;
public partial class StudyFlashcardsPage:ContentPage,IQueryAttributable
{
    private readonly StudyFlashcardsViewModel viewModel;
    public StudyFlashcardsPage(StudyFlashcardsViewModel viewModel) { InitializeComponent(); BindingContext=this.viewModel=viewModel; }
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(
        query.TryGetValue("projectId",out var p) ? p?.ToString() ?? "" : "",query.TryGetValue("search",out var s) ? s?.ToString() : null,
        query.TryGetValue("tag",out var t) ? t?.ToString() : null,query.TryGetValue("difficulty",out var d) ? d?.ToString() : null,
        query.TryGetValue("favorite",out var f) ? f?.ToString() : null);
    protected override void OnAppearing() { base.OnAppearing(); _=viewModel.LoadCommand.ExecuteAsync(null); }
}
