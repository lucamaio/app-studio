namespace StudySpace.App.Features.Performance;
public partial class QuizHistoryPage:ContentPage,IQueryAttributable
{
    private readonly QuizHistoryViewModel viewModel;
    public QuizHistoryPage(QuizHistoryViewModel vm){InitializeComponent();BindingContext=viewModel=vm;}
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(query.TryGetValue("projectId",out var projectId)?projectId?.ToString()??"":"");
    protected override void OnAppearing(){base.OnAppearing();_=viewModel.LoadCommand.ExecuteAsync(null);}
}
