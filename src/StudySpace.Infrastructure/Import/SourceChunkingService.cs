using System.Text;
using StudySpace.Application.Sources;
using StudySpace.Domain.Sources;
namespace StudySpace.Infrastructure.Import;

public sealed class SourceChunkingService(SourceImportOptions options) : ISourceChunkingService
{
    public const int MaxChunkCharacters = 4000;
    public const int TargetChunkCharacters = 3000;
    public const int MinChunkCharacters = 1000;
    public const int OverlapCharacters = 250;
    public const string Version = "paragraph-v1";

    public NormalizedDocument Normalize(ExtractedDocument document)
    {
        var text = new StringBuilder();
        var locators = new List<TextLocator>();
        foreach (var segment in document.Segments)
        {
            var value = segment.Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (value.Length == 0) continue;
            if ((long)text.Length + value.Length + 2 > options.MaxTextCharacters)
                throw new SourceImportException("TextTooLarge", "Il documento supera il limite di testo estraibile.");
            if (text.Length > 0) text.Append("\n\n");
            var start = text.Length;
            text.Append(value);
            locators.Add(new(start, text.Length, segment.Page, segment.Section));
        }
        if (text.Length == 0) throw new SourceImportException("NoText", "Nessun testo leggibile. Per scansioni o immagini usa il testo manuale; OCR non disponibile.");
        return new(text.ToString(), locators);
    }
    public IReadOnlyList<SourceChunk> Chunk(Guid projectId, Guid sourceId, Guid revisionId,
        NormalizedDocument document, DateTimeOffset now, CancellationToken ct = default)
    {
        var chunks = new List<SourceChunk>();
        var text = document.Text;
        var start = 0;
        while (start < text.Length)
        {
            ct.ThrowIfCancellationRequested();
            var end = text.Length - start <= MaxChunkCharacters ? text.Length : FindEnd(text, start);
            // Avoid cutting a UTF-16 surrogate pair at the hard boundary.
            if (end < text.Length && char.IsLowSurrogate(text[end])) end--;
            var touched = document.Locators.Where(l => l.StartOffset < end && l.EndOffset > start).ToArray();
            var pages = touched.Where(l => l.Page.HasValue).Select(l => l.Page!.Value).ToArray();
            var sections = touched.Select(l => l.Section).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
            chunks.Add(new SourceChunk { ProjectId=projectId, SourceId=sourceId, SourceRevisionId=revisionId,
                Ordinal=chunks.Count, Text=text[start..end], StartOffset=start, EndOffset=end,
                PageStart=pages.Length > 0 ? pages.Min() : null, PageEnd=pages.Length > 0 ? pages.Max() : null,
                SectionPath=sections.Length > 0 ? string.Join(" → ", sections.Take(3)) : null,
                ChunkerVersion=Version, CreatedAtUtc=now, UpdatedAtUtc=now });
            if (end == text.Length) break;
            var next = end - OverlapCharacters;
            while (next < end && !char.IsWhiteSpace(text[next])) next++;
            if (next < text.Length && char.IsLowSurrogate(text[next])) next++;
            start = next;
        }
        return chunks;
    }
    private static int FindEnd(string text, int start)
    {
        var max = Math.Min(start + MaxChunkCharacters, text.Length - MinChunkCharacters);
        var min = start + MinChunkCharacters;
        var target = Math.Min(start + TargetChunkCharacters, max);
        // Prefer paragraph boundaries, then sentences, then whitespace, nearest the target.
        for (var kind = 0; kind < 3; kind++)
        {
            var best = -1;
            for (var i = min; i <= max; i++)
            {
                var boundary = kind switch
                {
                    0 => text[i-1] == '\n' && text[i] == '\n',
                    1 => char.IsWhiteSpace(text[i]) && ".!?".Contains(text[i-1]),
                    _ => char.IsWhiteSpace(text[i])
                };
                if (boundary && (best < 0 || Math.Abs(i-target) < Math.Abs(best-target))) best = i;
            }
            if (best >= 0) return best;
        }
        return max;
    }
}
