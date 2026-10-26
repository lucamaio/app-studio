using StudySpace.App.Services;
namespace StudySpace.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider services;
    public App(IServiceProvider services, IThemeService themes, SourceStartup sourceStartup)
    {
        InitializeComponent();
        this.services = services;
        startup=sourceStartup;
        themes.Initialize();
    }
    private readonly SourceStartup startup;
    // Resolve Shell only after application resources exist: its XAML uses global styles.
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(services.GetRequiredService<AppShell>());
        PlatformThemeBridge.Attach(window);
#if WINDOWS
        window.Width=1180;window.Height=760;window.MinimumWidth=360;window.MinimumHeight=520;
#endif
        window.Created+=(_,_)=>startup.Start();
#if DEBUG
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STUDYSPACE_TEST_DATA_DIRECTORY")))
            window.Title = "StudySpace · Verifica";
#endif
        return window;
    }
}

