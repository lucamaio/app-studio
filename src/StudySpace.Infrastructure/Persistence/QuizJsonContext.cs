using System.Text.Json.Serialization;
using StudySpace.Application.Quizzes;
namespace StudySpace.Infrastructure.Persistence;

[JsonSerializable(typeof(QuizQuestionSnapshot))]
[JsonSerializable(typeof(AnswerFeedback))]
[JsonSerializable(typeof(IReadOnlyList<RubricCriterion>))]
[JsonSerializable(typeof(QuizFilters))]
[JsonSerializable(typeof(Guid[]))]
internal partial class QuizJsonContext:JsonSerializerContext { }

