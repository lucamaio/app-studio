namespace StudySpace.App.Features.Projects;
public partial class ProjectsPage : ContentPage
{
    private readonly ProjectsViewModel viewModel;
    public ProjectsPage(ProjectsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        // Presentation-only adaptation to the actual list width, including split-screen.
        ProjectList.SizeChanged += (_, _) => AdaptLayout();
        viewModel.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(ProjectsViewModel.ShowFilters))AdaptLayout();};
    }
    private void AdaptLayout()
    {
        var width=ProjectList.Width;
        double Breakpoint(string key)=>Design.DesignMetrics.Value(key);
        CardsLayout.Span=width>=Breakpoint("GridFourColumns")?4:width>=Breakpoint("GridThreeColumns")?3:width>=Breakpoint("GridTwoColumns")?2:1;
        AdvancedFilters.IsVisible=width>=Breakpoint("FilterExpandedBreakpoint")||viewModel.ShowFilters;
    }
    public void ConfigureFavorites() => viewModel.ConfigureFavorites();
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadCommand.ExecuteAsync(null);
    }
    protected override void OnDisappearing()
    {
        viewModel.LoadCommand.Cancel();
        viewModel.LoadMoreCommand.Cancel();
        base.OnDisappearing();
    }
}

