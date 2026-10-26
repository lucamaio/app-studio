namespace StudySpace.App.Features.Projects;
public partial class CreateProjectPage : ContentPage, IQueryAttributable
{
    private readonly CreateProjectViewModel viewModel;
    public CreateProjectPage(CreateProjectViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("projectId", out var id)) viewModel.Configure(id?.ToString() ?? "");
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadCommand.ExecuteAsync(null);
    }
}

