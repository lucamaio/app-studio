namespace StudySpace.App.Features.Performance;
public partial class ProjectStatisticsPage:ContentPage,IQueryAttributable
{
    private readonly ProjectStatisticsViewModel viewModel;
    public ProjectStatisticsPage(ProjectStatisticsViewModel vm){InitializeComponent();BindingContext=viewModel=vm;}
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(query.TryGetValue("projectId",out var projectId)?projectId?.ToString()??"":"");
    protected override void OnAppearing(){base.OnAppearing();_=viewModel.LoadCommand.ExecuteAsync(null);}
}
