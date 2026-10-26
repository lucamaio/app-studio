namespace StudySpace.App.Features.Quizzes;
public partial class QuizSetupPage:ContentPage,IQueryAttributable
{
    private readonly QuizSetupViewModel viewModel;
    public QuizSetupPage(QuizSetupViewModel vm){InitializeComponent();BindingContext=viewModel=vm;}
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(query.TryGetValue("projectId",out var p)?p?.ToString()??"":"");
    protected override void OnAppearing(){base.OnAppearing();_=viewModel.LoadCommand.ExecuteAsync(null);}
}

