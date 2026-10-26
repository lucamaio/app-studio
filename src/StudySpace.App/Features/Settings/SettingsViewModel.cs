using StudySpace.Application.Quizzes;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Services;
namespace StudySpace.App.Features.Settings;

public sealed record ThemeChoice(string Title, ThemePreference Value);
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IThemeService themes;
    private readonly ISemanticEvaluationSettingsStore evaluationSettings;
    [ObservableProperty] public partial bool EvaluationEnabled {get;set;}
    [ObservableProperty] public partial string EvaluationEndpoint {get;set;}="";
    [ObservableProperty] public partial string EvaluationModel {get;set;}="";
    [ObservableProperty] public partial string EvaluationSaved {get;set;}="";
    partial void OnEvaluationEnabledChanged(bool value)=>EvaluationSaved="";
    partial void OnEvaluationEndpointChanged(string value)=>EvaluationSaved="";
    partial void OnEvaluationModelChanged(string value)=>EvaluationSaved="";
    [RelayCommand] private Task SaveEvaluationAsync()=>RunAsync(()=>
    {
        var value=new SemanticEvaluationSettings(EvaluationEnabled,EvaluationEndpoint,EvaluationModel).Validate();
        evaluationSettings.Save(value);
        EvaluationEndpoint=value.Endpoint;EvaluationModel=value.Model;
        EvaluationSaved=value.Enabled?"Impostazioni salvate. Il modello verrà contattato quando verifichi una risposta o termini il quiz.":"Valutazione disattivata. Le risposte restano sul dispositivo.";
        return Task.CompletedTask;
    });
    public IReadOnlyList<ThemeChoice> Themes { get; } =
    [
        new("Sistema", ThemePreference.System),
        new("Chiaro", ThemePreference.Light),
        new("Scuro", ThemePreference.Dark)
    ];
    private ThemeChoice? selectedTheme;
    public ThemeChoice? SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (SetProperty(ref selectedTheme, value)) ApplyTheme(value);
        }
    }
    public SettingsViewModel(IThemeService themes, ILogger<SettingsViewModel> logger, ISemanticEvaluationSettingsStore? evaluationSettings=null) : base(logger)
    {
        this.themes = themes;
        this.evaluationSettings=evaluationSettings??new MemorySemanticEvaluationSettingsStore();
        var config=this.evaluationSettings.Read(); EvaluationEnabled=config.Enabled; EvaluationEndpoint=config.Endpoint; EvaluationModel=config.Model;
        selectedTheme = Themes.Single(choice => choice.Value == themes.Current);
    }
    private void ApplyTheme(ThemeChoice? value)
    {
        if (value is null) return;
        _ = RunAsync(() =>
        {
            try { themes.SetTheme(value.Value); }
            catch
            {
                selectedTheme = Themes.Single(choice => choice.Value == themes.Current);
                OnPropertyChanged(nameof(SelectedTheme));
                throw;
            }
            return Task.CompletedTask;
        });
    }
}


