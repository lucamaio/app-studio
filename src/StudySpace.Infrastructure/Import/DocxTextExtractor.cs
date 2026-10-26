using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
namespace StudySpace.Infrastructure.Import;

public sealed class DocxTextExtractor(SourceImportOptions options) : IDocumentTextExtractor
{
    public SourceKind Kind => SourceKind.Docx;
    public Task<ExtractedDocument> ExtractAsync(Stream stream, CancellationToken ct = default) => Task.Run(() =>
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 5000 || archive.Entries.Sum(e => e.Length) > options.MaxDocxExpandedBytes)
            throw new SourceImportException("ExpandedTooLarge", "Il DOCX supera il limite di dimensione decompressa.");
        if (archive.GetEntry("[Content_Types].xml") is null)
            throw new SourceImportException("InvalidDocx", "Il file non è un documento DOCX valido.");
        var main = archive.GetEntry("word/document.xml") ?? throw new SourceImportException("InvalidDocx", "Contenuto DOCX mancante o danneggiato.");
        using var input = main.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,
            XmlResolver=null, MaxCharactersInDocument=options.MaxDocxExpandedBytes, IgnoreComments=true });
        var xml = XDocument.Load(reader);
        XNamespace word = xml.Root?.Name.Namespace ?? XNamespace.None;
        if (xml.Root?.Name.LocalName != "document" || (word != "http://schemas.openxmlformats.org/wordprocessingml/2006/main" &&
            word != "http://purl.oclc.org/ooxml/wordprocessingml/main"))
            throw new SourceImportException("InvalidDocx", "Il file non contiene testo Word valido.");
        var body = xml.Root.Element(word + "body") ?? throw new SourceImportException("InvalidDocx", "Corpo del documento mancante.");
        var segments = new List<TextSegment>();
        var count = 0;
        string? section = null;
        foreach (var p in body.Descendants(word + "p"))
        {
            ct.ThrowIfCancellationRequested();
            var text = new StringBuilder();
            foreach (var node in p.Descendants())
            {
                if (node.Ancestors(word + "del").Any()) continue;
                if (node.Name == word + "t") text.Append(node.Value);
                else if (node.Name == word + "tab") text.Append('\t');
                else if (node.Name == word + "br" || node.Name == word + "cr") text.Append('\n');
                if ((long)count + text.Length > options.MaxTextCharacters)
                    throw new SourceImportException("TextTooLarge", "Il documento supera il limite di testo estraibile.");
            }
            var value = text.ToString().Trim();
            if (value.Length == 0) continue;
            count += value.Length + 2;
            var properties = p.Element(word + "pPr");
            var style = properties?.Element(word + "pStyle")?.Attribute(word + "val")?.Value ?? "";
            if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || style.StartsWith("Titolo", StringComparison.OrdinalIgnoreCase) ||
                properties?.Element(word + "outlineLvl") is not null) section=value;
            segments.Add(new(value, Section: section));
        }
        return new ExtractedDocument(segments, "docx-zipxml-v1");
    }, ct);
}
