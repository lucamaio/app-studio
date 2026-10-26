using System.Text.Json;
using System.Text.Json.Serialization;
using StudySpace.Application.Quizzes;
namespace StudySpace.App.Services;

public sealed class SemanticEvaluationSettingsStore : ISemanticEvaluationSettingsStore
{
    private const string Key = "semantic-evaluation-v1";
    public SemanticEvaluationSettings Read()
    {
        try { return JsonSerializer.Deserialize(Preferences.Default.Get(Key, "{}"), SettingsJsonContext.Default.SemanticEvaluationSettings) ?? new(); }
        catch (JsonException) { return new(); }
    }
    public void Save(SemanticEvaluationSettings settings) => Preferences.Default.Set(Key,
        JsonSerializer.Serialize(settings.Validate(), SettingsJsonContext.Default.SemanticEvaluationSettings));
}
[JsonSerializable(typeof(SemanticEvaluationSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
