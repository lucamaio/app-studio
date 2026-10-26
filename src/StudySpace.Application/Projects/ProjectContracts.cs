using System.Globalization;
using System.Text;
using StudySpace.Domain.Projects;

namespace StudySpace.Application.Projects;

public sealed record ProjectDraft(string Name, string? Description = null, string? IconKey = "book",
    string Color = "#5B63D3", bool IsFavorite = false, IReadOnlyList<string>? Tags = null);
public sealed record ProjectQuery(string Search = "", string? Tag = null, bool FavoritesOnly = false);
public sealed record ProjectSummary(StudyProject Project, int SourceCount, int FlashcardCount, int CompletedQuizCount);

public static class ProjectText
{
    public static string NormalizeTag(string value) => value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    public static string SearchKey(string value)
    {
        var text = value.Normalize(NormalizationForm.FormD);
        return new string(text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}

