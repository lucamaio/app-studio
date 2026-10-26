namespace StudySpace.App.Features.Performance;
public partial class QuizEvidencePage:ContentPage,IQueryAttributable
{
    private readonly QuizEvidenceViewModel viewModel;
    public QuizEvidencePage(QuizEvidenceViewModel vm){InitializeComponent();BindingContext=viewModel=vm;}
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(query.TryGetValue("projectId",out var projectId)?projectId?.ToString()??"":"",query.TryGetValue("attemptId",out var attemptId)?attemptId?.ToString()??"":"",query.TryGetValue("questionId",out var questionId)?questionId?.ToString()??"":"",query.TryGetValue("chunkId",out var chunkId)?chunkId?.ToString()??"":"");
    protected override void OnAppearing(){base.OnAppearing();_=viewModel.LoadCommand.ExecuteAsync(null);}
}
