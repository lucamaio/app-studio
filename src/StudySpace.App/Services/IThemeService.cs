namespace StudySpace.App.Services;

public enum ThemePreference { System = 0, Light = 1, Dark = 2 }
public interface IThemeService
{
    ThemePreference Current { get; }
    void Initialize();
    void SetTheme(ThemePreference preference);
}

