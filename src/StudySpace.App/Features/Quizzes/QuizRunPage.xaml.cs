using System.ComponentModel;
namespace StudySpace.App.Features.Quizzes;
public partial class QuizRunPage:ContentPage,IQueryAttributable
{
    private readonly QuizRunViewModel viewModel;
    private IDispatcherTimer? timer;
    private Window? parentWindow;
    private bool replaying,handlingNavigation;
    public QuizRunPage(QuizRunViewModel vm)
    {
        InitializeComponent();BindingContext=viewModel=vm;
    }
    public void ApplyQueryAttributes(IDictionary<string,object> query)=>viewModel.Configure(
        query.TryGetValue("projectId",out var p)?p?.ToString()??"":"",
        query.TryGetValue("attemptId",out var a)?a?.ToString()??"":"");
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.PropertyChanged+=OnViewModelChanged;
        Shell.Current.Navigating+=OnNavigating;
        parentWindow=Window;
        if(parentWindow is not null){parentWindow.Deactivated+=OnPaused;parentWindow.Stopped+=OnPaused;parentWindow.Activated+=OnActivated;parentWindow.Resumed+=OnActivated;}
        timer=Dispatcher.CreateTimer();timer.Interval=TimeSpan.FromSeconds(5);timer.Tick+=OnTick;timer.Start();
        await viewModel.LoadCommand.ExecuteAsync(null);viewModel.Activate();ScrollCurrent();
    }
    protected override void OnDisappearing()
    {
        timer?.Stop();if(timer is not null)timer.Tick-=OnTick;timer=null;
        Shell.Current.Navigating-=OnNavigating;viewModel.PropertyChanged-=OnViewModelChanged;
        if(parentWindow is not null){parentWindow.Deactivated-=OnPaused;parentWindow.Stopped-=OnPaused;parentWindow.Activated-=OnActivated;parentWindow.Resumed-=OnActivated;parentWindow=null;}
        base.OnDisappearing();
    }
    private async void OnTick(object? sender,EventArgs e)=>await viewModel.TickAsync();
    private async void OnPaused(object? sender,EventArgs e)=>await viewModel.PauseAsync();
    private void OnActivated(object? sender,EventArgs e)=>viewModel.Activate();
    private void OnViewModelChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName==nameof(QuizRunViewModel.CurrentPosition))ScrollCurrent();}
    private void ScrollCurrent()
    {
        if(viewModel.Numbers.Count==0)return;
        Dispatcher.Dispatch(()=>QuestionBar.ScrollTo(viewModel.CurrentPosition,position:ScrollToPosition.Center,animate:false));
    }
    private async void OnNavigating(object? sender,ShellNavigatingEventArgs e)
    {
        if(replaying||viewModel.IsLeaving||!e.CanCancel)return;
        e.Cancel();if(handlingNavigation)return;handlingNavigation=true;
        try
        {
            if(!await viewModel.PrepareToLeaveAsync())return;
            await Task.Yield();replaying=true;await Shell.Current.GoToAsync(e.Target.Location);
        }
        catch(Exception){viewModel.ErrorMessage="Navigazione non riuscita. Le risposte salvate sono conservate. Riprova a uscire.";}
        finally{replaying=false;handlingNavigation=false;}
    }
}

