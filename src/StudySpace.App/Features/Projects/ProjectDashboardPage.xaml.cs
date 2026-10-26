namespace StudySpace.App.Features.Projects;
public partial class ProjectDashboardPage : ContentPage, IQueryAttributable
{
    private readonly ProjectDashboardViewModel viewModel;
    public ProjectDashboardPage(ProjectDashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        viewModel.Configure(query.TryGetValue("projectId", out var id) ? id?.ToString() ?? "" : "");
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadCommand.ExecuteAsync(null);
    }
}

