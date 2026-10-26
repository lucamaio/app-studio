namespace StudySpace.App.Controls;

public partial class EmptyStateView : ContentView
{
    public static readonly BindableProperty HeadingProperty = BindableProperty.Create(nameof(Heading), typeof(string), typeof(EmptyStateView), "");
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(nameof(Message), typeof(string), typeof(EmptyStateView), "");
    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public EmptyStateView() => InitializeComponent();
}

