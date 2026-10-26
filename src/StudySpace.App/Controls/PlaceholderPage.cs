namespace StudySpace.App.Controls;

public sealed class PlaceholderPage : ContentPage
{
    public PlaceholderPage(string title, string heading, string message)
    {
        Title = title;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = Design.DesignMetrics.Value("Space24"), MaximumWidthRequest = Design.DesignMetrics.Value("ContentWidth"),
                Children = { new EmptyStateView { Heading = heading, Message = message } }
            }
        };
        ((VerticalStackLayout)((ScrollView)Content).Content).SetDynamicResource(Microsoft.Maui.Controls.Layout.PaddingProperty,"PageInset");
    }
}


