namespace StudySpace.App.Design;

// Numeric breakpoints share the same dictionary as XAML, and follow the actual available width.
public static class DesignMetrics
{
    public static double Value(string key)=>(double)Microsoft.Maui.Controls.Application.Current!.Resources[key];
}
