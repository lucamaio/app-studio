using StudySpace.Application.Flashcards;
using StudySpace.Domain.Common;

namespace StudySpace.Application.Quizzes;

public enum OpenAnswerStatus { Correct, PartiallyCorrect, Incorrect }
public sealed record EvaluationSourceReference(Guid ChunkId, string Quote);
public sealed record OpenAnswerEvaluation(double Score, OpenAnswerStatus Status, bool IsCorrect,
    string Summary, IReadOnlyList<string> CorrectConcepts, IReadOnlyList<string> MissingConcepts,
    IReadOnlyList<string> IncorrectConcepts, IReadOnlyList<string> Suggestions, string IdealAnswer,
    IReadOnlyList<EvaluationSourceReference> SourceReferences, double Confidence,
    IReadOnlyList<CriterionAssessment> Criteria);
public sealed record EvaluationMetadata(OpenAnswerEvaluation Evaluation, string Model, string PromptVersion);
public sealed record EvaluationPolicy(TimeSpan Timeout)
{
    public static EvaluationPolicy Default { get; } = new(TimeSpan.FromSeconds(90));
}

public static class OpenAnswerEvaluationValidator
{
    public static double Validate(QuizQuestionSnapshot question, OpenAnswerEvaluation value)
    {
        var rubric = question.Rubric;
        if (value is null || question.Kind != QuestionKind.Open || rubric.Count is < 1 or > 10 ||
            rubric.Any(r => !double.IsFinite(r.Weight) || r.Weight <= 0) ||
            rubric.Select(r => r.Id).Distinct().Count() != rubric.Count || Math.Abs(rubric.Sum(r => r.Weight) - 1) > 0.000001 ||
            value.Criteria is null || value.Criteria.Count != rubric.Count || value.Criteria.Any(c => c is null) ||
            value.Criteria.Select(c => c.CriterionId).Distinct().Count() != rubric.Count ||
            value.Criteria.Any(c => !double.IsFinite(c.Grade) || c.Grade is < 0 or > 1 || !rubric.Any(r => r.Id == c.CriterionId)) ||
            !double.IsFinite(value.Score) || value.Score is < 0 or > 100 || !Enum.IsDefined(value.Status) ||
            !double.IsFinite(value.Confidence) || value.Confidence is < 0 or > 1 ||
            !Text(value.Summary, 4000) || !Text(value.IdealAnswer, 8000) ||
            !List(value.CorrectConcepts) || !List(value.MissingConcepts) || !List(value.IncorrectConcepts) ||
            !List(value.Suggestions) || value.SourceReferences is null || value.SourceReferences.Count is < 1 or > 20 ||
            value.SourceReferences.Any(r => r is null || !Text(r.Quote, 4000) ||
                !question.Citations.Any(c => c.ChunkId == r.ChunkId && c.Quote.Contains(r.Quote, StringComparison.Ordinal))) ||
            value.SourceReferences.Select(r => r.ChunkId).Distinct().Count() != value.SourceReferences.Count)
            throw Invalid();
        var grades = value.Criteria.ToDictionary(c => c.CriterionId, c => c.Grade);
        var score = Math.Clamp(rubric.Sum(r => r.Weight * grades[r.Id]), 0, 1);
        var status = score >= 0.8 ? OpenAnswerStatus.Correct : score > 0 ? OpenAnswerStatus.PartiallyCorrect : OpenAnswerStatus.Incorrect;
        if (Math.Abs(value.Score - score * 100) > 0.05 || value.Status != status || value.IsCorrect != (status == OpenAnswerStatus.Correct))
            throw Invalid();
        return score;
    }
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    private static bool List(IReadOnlyList<string>? value) => value is not null && value.Count <= 20 && value.All(s => Text(s, 2000));
    public static AiStudyException Invalid() => new("La valutazione ricevuta non è valida. La risposta è salvata: riprova, nessun punteggio è stato assegnato.");
}
