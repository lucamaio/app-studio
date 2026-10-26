using System.Text.Json;
using System.Text.Json.Serialization;
using StudySpace.Application.Quizzes;

namespace StudySpace.Infrastructure.Ai;

// Strict external boundary: unknown, duplicate, missing and null fields never turn into a grade.
public static class OpenAnswerEvaluationJson
{
    public static OpenAnswerEvaluation Parse(string json, QuizQuestionSnapshot question)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 64000) throw OpenAnswerEvaluationValidator.Invalid();
        try
        {
            using var doc = JsonDocument.Parse(json, new() { MaxDepth = 16 });
            CheckDuplicates(doc.RootElement);
            Require(doc.RootElement, "score", "status", "isCorrect", "summary", "correctConcepts", "missingConcepts",
                "incorrectConcepts", "suggestions", "idealAnswer", "sourceReferences", "confidence", "criteria");
            var status = doc.RootElement.GetProperty("status");
            if (status.ValueKind != JsonValueKind.String || status.GetString() is not ("Correct" or "PartiallyCorrect" or "Incorrect")) throw new JsonException();
            foreach (var item in doc.RootElement.GetProperty("criteria").EnumerateArray()) Require(item, "criterionId", "grade");
            foreach (var item in doc.RootElement.GetProperty("sourceReferences").EnumerateArray()) Require(item, "chunkId", "quote");
            var value = JsonSerializer.Deserialize(json, EvaluationJsonContext.Default.OpenAnswerEvaluation)!;
            OpenAnswerEvaluationValidator.Validate(question, value);
            return value;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        { throw OpenAnswerEvaluationValidator.Invalid(); }
    }
    private static void Require(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != names.Length ||
            names.Any(n => !element.TryGetProperty(n, out var p) || p.ValueKind == JsonValueKind.Null)) throw new JsonException();
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new JsonException(); CheckDuplicates(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = new[] { typeof(JsonStringEnumConverter<OpenAnswerStatus>) })]
[JsonSerializable(typeof(OpenAnswerEvaluation))]
[JsonSerializable(typeof(OpenAnswerEvaluationRequest))]
internal partial class EvaluationJsonContext : JsonSerializerContext;
