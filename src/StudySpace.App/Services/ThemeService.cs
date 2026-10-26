using Microsoft.Extensions.Logging;
namespace StudySpace.App.Services;

public sealed class ThemeService(ILogger<ThemeService> logger) : IThemeService
{
    private const string PreferenceKey = "appearance.theme";
    public ThemePreference Current { get; private set; } = ThemePreference.System;
    public void Initialize()
    {
        try
        {
            var value = Preferences.Default.Get(PreferenceKey, (int)ThemePreference.System);
            Current = Enum.IsDefined(typeof(ThemePreference), value) ? (ThemePreference)value : ThemePreference.System;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Theme preference could not be read: {ErrorType}", ex.GetType().Name);
            Current = ThemePreference.System;
        }
        Apply(Current);
    }
    public void SetTheme(ThemePreference preference)
    {
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        Preferences.Default.Set(PreferenceKey, (int)preference);
        Apply(preference);
        Current = preference;
    }
    private static void Apply(ThemePreference preference)
    {
        if (Microsoft.Maui.Controls.Application.Current is { } app)
            app.UserAppTheme = preference switch
            {
                ThemePreference.Light => AppTheme.Light,
                ThemePreference.Dark => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
        PlatformThemeBridge.UpdateApplication();
    }
}

