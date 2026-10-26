using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
namespace StudySpace.Infrastructure.Import;

public sealed class PdfTextExtractor(SourceImportOptions options) : IDocumentTextExtractor
{
    public SourceKind Kind => SourceKind.Pdf;
    public Task<ExtractedDocument> ExtractAsync(Stream stream, CancellationToken ct = default) => Task.Run(() =>
    {
        using var document = PdfDocument.Open(stream);
        if (document.NumberOfPages > options.MaxPdfPages)
            throw new SourceImportException("TooManyPages", "Il PDF contiene troppe pagine per un'unica importazione.");
        var segments = new List<TextSegment>();
        var count = 0;
        for (var i=1; i<=document.NumberOfPages; i++)
        {
            ct.ThrowIfCancellationRequested();
            var text = ContentOrderTextExtractor.GetText(document.GetPage(i));
            count += text.Length + 2;
            if (count > options.MaxTextCharacters)
                throw new SourceImportException("TextTooLarge", "Il PDF supera il limite di testo estraibile.");
            segments.Add(new(text, Page: i));
        }
        return new ExtractedDocument(segments, "pdfpig-0.1.16-contentorder-v1");
    }, ct);
}
