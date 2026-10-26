namespace StudySpace.App.Features.Projects;
public partial class ProjectSectionPage : ContentPage, IQueryAttributable
{
    private readonly ProjectSectionViewModel viewModel;
    public ProjectSectionPage(ProjectSectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query) => viewModel.Configure(
        query.TryGetValue("projectId", out var id) ? id?.ToString() ?? "" : "",
        query.TryGetValue("section", out var section) ? section?.ToString() ?? "" : "");
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadCommand.ExecuteAsync(null);
    }
}

