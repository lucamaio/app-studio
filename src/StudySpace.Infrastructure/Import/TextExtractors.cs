using System.Text;
using System.Text.RegularExpressions;
using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
namespace StudySpace.Infrastructure.Import;

public sealed class PlainTextExtractor(SourceImportOptions options) : IDocumentTextExtractor
{
    public SourceKind Kind => SourceKind.Text;
    public async Task<ExtractedDocument> ExtractAsync(Stream stream, CancellationToken ct = default) =>
        new([new(await TextDecoding.ReadAsync(stream, options, ct))], "text-strict-v1");
}
public sealed class MarkdownTextExtractor(SourceImportOptions options) : IDocumentTextExtractor
{
    public SourceKind Kind => SourceKind.Markdown;
    private static readonly Regex Heading = new(@"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$", RegexOptions.NonBacktracking);
    private static readonly Regex Link = new(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.NonBacktracking);
    public async Task<ExtractedDocument> ExtractAsync(Stream stream, CancellationToken ct = default)
    {
        var text = await TextDecoding.ReadAsync(stream, options, ct);
        var segments = new List<TextSegment>();
        var paragraph = new StringBuilder();
        string? section = null;
        var inCode = false;
        void Flush()
        {
            if (paragraph.Length > 0) { segments.Add(new(paragraph.ToString(), Section: section)); paragraph.Clear(); }
        }
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~")) { inCode = !inCode; continue; }
            var heading = Heading.Match(line);
            if (!inCode && heading.Success)
            {
                Flush(); section = heading.Groups[1].Value; paragraph.AppendLine(section);
            }
            else if (string.IsNullOrWhiteSpace(line)) Flush();
            else paragraph.AppendLine(inCode ? line : Link.Replace(line, "$1"));
        }
        Flush();
        return new(segments, "markdown-plain-v1");
    }
}
internal static class TextDecoding
{
    public static async Task<string> ReadAsync(Stream stream, SourceImportOptions options, CancellationToken ct)
    {
        using var data = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (data.Length + read > options.MaxFileBytes) throw new SourceImportException("FileTooLarge", "Il file supera il limite di importazione.");
            data.Write(buffer, 0, read);
        }
        var bytes = data.GetBuffer().AsSpan(0, (int)data.Length);
        Encoding encoding = new UTF8Encoding(false, true);
        var offset = 0;
        if (bytes.StartsWith(new byte[] { 0xFF,0xFE,0,0 })) { encoding = new UTF32Encoding(false, false, true); offset=4; }
        else if (bytes.StartsWith(new byte[] { 0,0,0xFE,0xFF })) { encoding = new UTF32Encoding(true, false, true); offset=4; }
        else if (bytes.StartsWith(new byte[] { 0xFF,0xFE })) { encoding = new UnicodeEncoding(false, false, true); offset=2; }
        else if (bytes.StartsWith(new byte[] { 0xFE,0xFF })) { encoding = new UnicodeEncoding(true, false, true); offset=2; }
        else if (bytes.StartsWith(new byte[] { 0xEF,0xBB,0xBF })) offset=3;
        try
        {
            var result = encoding.GetString(bytes[offset..]);
            if (result.Length > options.MaxTextCharacters) throw new SourceImportException("TextTooLarge", "Il testo supera il limite di caratteri.");
            if (result.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t' and not '\f'))
                throw new SourceImportException("BinaryText", "Il file contiene dati binari e non è un documento di testo leggibile.");
            return result;
        }
        catch (DecoderFallbackException)
        { throw new SourceImportException("InvalidEncoding", "Codifica non leggibile. Salva il testo in UTF-8 oppure UTF-16/UTF-32 con BOM."); }
    }
}
