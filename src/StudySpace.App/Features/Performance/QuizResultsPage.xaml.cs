namespace StudySpace.App.Features.Performance;
public partial class QuizResultsPage:ContentPage,IQueryAttributable
{
    private readonly QuizResultsViewModel viewModel;
    public QuizResultsPage(QuizResultsViewModel vm)
    {
        InitializeComponent();BindingContext=viewModel=vm;
        viewModel.PropertyChanged+=(_,e)=>
        {
            if(viewModel.IsReady&&e.PropertyName==nameof(QuizResultsViewModel.PageLabel))
                Dispatcher.Dispatch(async()=>await ResultScroll.ScrollToAsync(QuestionsHeading,ScrollToPosition.Start,false));
        };
    }
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(query.TryGetValue("projectId",out var projectId)?projectId?.ToString()??"":"",query.TryGetValue("attemptId",out var attemptId)?attemptId?.ToString()??"":"");
    protected override void OnAppearing(){base.OnAppearing();_=viewModel.LoadCommand.ExecuteAsync(null);}
}
