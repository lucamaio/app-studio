using StudySpace.App.Features.Performance;
namespace StudySpace.App.Controls;
public partial class PerformanceGroupsView:ContentView
{
    public static readonly BindableProperty ItemsSourceProperty=BindableProperty.Create(nameof(ItemsSource),typeof(IEnumerable<PerformanceGroupViewModel>),typeof(PerformanceGroupsView));
    public IEnumerable<PerformanceGroupViewModel>? ItemsSource {get=>(IEnumerable<PerformanceGroupViewModel>?)GetValue(ItemsSourceProperty);set=>SetValue(ItemsSourceProperty,value);}
    public PerformanceGroupsView()=>InitializeComponent();
}
