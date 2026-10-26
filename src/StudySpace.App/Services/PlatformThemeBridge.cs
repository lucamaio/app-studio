namespace StudySpace.App.Services;

// AppThemeBinding handles MAUI resources. Native WinUI controls also need the root's requested theme.
public static class PlatformThemeBridge
{
    public static void Attach(Window window)
    {
        var app=Microsoft.Maui.Controls.Application.Current!;
        void Refresh(object? sender,EventArgs args)=>window.Dispatcher.Dispatch(()=>Apply(window));
        void ThemeChanged(object? sender,AppThemeChangedEventArgs args)=>Refresh(sender,args);
        window.Created+=Refresh;
        app.RequestedThemeChanged+=ThemeChanged;
        window.Destroying+=(_,_)=>{window.Created-=Refresh;app.RequestedThemeChanged-=ThemeChanged;};
    }
    public static void UpdateApplication()
    {
        if(Microsoft.Maui.Controls.Application.Current is not { } app)return;
        foreach(var window in app.Windows)window.Dispatcher.Dispatch(()=>Apply(window));
    }
    private static void Apply(Window window)
    {
#if WINDOWS
        if(window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)return;
        var app=Microsoft.Maui.Controls.Application.Current!;
        if(native.Content is Microsoft.UI.Xaml.FrameworkElement root)
            root.RequestedTheme=app.UserAppTheme switch
            {
                AppTheme.Light=>Microsoft.UI.Xaml.ElementTheme.Light,
                AppTheme.Dark=>Microsoft.UI.Xaml.ElementTheme.Dark,
                _=>Microsoft.UI.Xaml.ElementTheme.Default
            };
        var dark=app.UserAppTheme==AppTheme.Dark||app.UserAppTheme==AppTheme.Unspecified&&app.RequestedTheme==AppTheme.Dark;
        global::Windows.UI.Color Palette(string key)
        {
            var color=(Color)app.Resources[key+(dark?"Dark":"Light")];
            return global::Windows.UI.Color.FromArgb((byte)(color.Alpha*255),(byte)(color.Red*255),(byte)(color.Green*255),(byte)(color.Blue*255));
        }
        var bar=native.AppWindow.TitleBar;
        bar.BackgroundColor=Palette("Surface");bar.ForegroundColor=Palette("Ink");
        bar.ButtonBackgroundColor=Palette("Surface");bar.ButtonForegroundColor=Palette("Ink");
        bar.ButtonInactiveBackgroundColor=Palette("Surface");bar.ButtonInactiveForegroundColor=Palette("Muted");
        bar.ButtonHoverBackgroundColor=Palette("Elevated");bar.ButtonHoverForegroundColor=Palette("Ink");
        bar.ButtonPressedBackgroundColor=Palette("Border");bar.ButtonPressedForegroundColor=Palette("Ink");
#endif
    }
}
