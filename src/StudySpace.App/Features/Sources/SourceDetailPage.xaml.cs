namespace StudySpace.App.Features.Sources;
public partial class SourceDetailPage : ContentPage, IQueryAttributable
{
    private readonly SourceDetailViewModel viewModel;
    public SourceDetailPage(SourceDetailViewModel viewModel)
    { InitializeComponent(); BindingContext=this.viewModel=viewModel; }
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(
        query.TryGetValue("projectId",out var p) ? p?.ToString() ?? "" : "",
        query.TryGetValue("sourceId",out var s) ? s?.ToString() ?? "" : "");
    protected override void OnAppearing() { base.OnAppearing(); _=viewModel.LoadCommand.ExecuteAsync(null); }
}
