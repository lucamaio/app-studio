using System.Net;
using StudySpace.Application.Common;
namespace StudySpace.Application.Quizzes;

public sealed record SemanticEvaluationSettings(bool Enabled = false, string Endpoint = "http://localhost:11434", string Model = "")
{
    public SemanticEvaluationSettings Validate()
    {
        if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/" ||
            !(uri.Scheme == "https" || uri.Scheme == "http" && IsLocal(uri)))
            throw new StudyValidationException("Indica l'indirizzo del server Ollama, senza percorsi o credenziali. HTTP è consentito solo su localhost o indirizzi IP privati; altrimenti usa HTTPS.");
        var model = Model.Trim();
        if (model.Length > 100 || model.Any(char.IsControl) || Enabled && string.IsNullOrWhiteSpace(model))
            throw new StudyValidationException("Indica il nome del modello già installato sul server Ollama (massimo 100 caratteri).");
        return this with { Endpoint = uri.GetLeftPart(UriPartial.Authority), Model = model };
    }
    private static bool IsLocal(Uri uri)
    {
        if (uri.IsLoopback) return true;
        if (!IPAddress.TryParse(uri.Host, out var ip)) return false;
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31);
    }
}
public interface ISemanticEvaluationSettingsStore
{
    SemanticEvaluationSettings Read();
    void Save(SemanticEvaluationSettings settings);
}
public sealed class MemorySemanticEvaluationSettingsStore : ISemanticEvaluationSettingsStore
{
    private SemanticEvaluationSettings settings = new();
    public SemanticEvaluationSettings Read() => settings;
    public void Save(SemanticEvaluationSettings value) => settings = value.Validate();
}
