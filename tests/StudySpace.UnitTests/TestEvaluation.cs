using StudySpace.Application.Quizzes;

internal static class TestEvaluation
{
    // Scripted provider output: deliberately independent of the student's wording.
    public static OpenAnswerEvaluation Create(QuizQuestionSnapshot question, IReadOnlyList<CriterionAssessment>? criteria = null)
    {
        criteria ??= question.Rubric.Select(r => new CriterionAssessment(r.Id, 1)).ToArray();
        var score = question.Rubric.Sum(r => r.Weight * criteria.First(c => c.CriterionId == r.Id).Grade) * 100;
        var status = score >= 80 ? OpenAnswerStatus.Correct : score > 0 ? OpenAnswerStatus.PartiallyCorrect : OpenAnswerStatus.Incorrect;
        return new(score, status, status == OpenAnswerStatus.Correct, "Il ragionamento è stato confrontato con le fonti.",
            ["Concetto riconosciuto"], [], [], ["Rivedi il collegamento con le fonti."], question.IdealAnswer,
            question.Citations.Select(c => new EvaluationSourceReference(c.ChunkId, c.Quote)).ToArray(), 0.9, criteria);
    }
}
