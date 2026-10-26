namespace StudySpace.App.Features.Sources;
public partial class ManualSourcePage : ContentPage, IQueryAttributable
{
    private readonly ManualSourceViewModel viewModel;
    public ManualSourcePage(ManualSourceViewModel viewModel)
    { InitializeComponent(); BindingContext=this.viewModel=viewModel; }
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(
        query.TryGetValue("projectId",out var id) ? id?.ToString() ?? "" : "");
    protected override void OnAppearing() { base.OnAppearing(); _=viewModel.LoadCommand.ExecuteAsync(null); }
}
