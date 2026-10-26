using CommunityToolkit.Mvvm.Input;
using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
namespace StudySpace.App.Features.Sources;

public sealed class SourceCardViewModel
{
    public SourceSummary Summary { get; }
    public string Title => Summary.Source.Title;
    public string KindLabel => Summary.Source.Kind switch { SourceKind.Pdf=>"PDF", SourceKind.Docx=>"DOCX",
        SourceKind.Markdown=>"Markdown", SourceKind.Text=>"TXT", _=>"Testo manuale" };
    public string Status => Summary.Revision.Status switch { ExtractionStatus.Ready=>"Pronto",
        ExtractionStatus.Processing or ExtractionStatus.Pending=>"Elaborazione", _=>"Errore" };
    public string Tone => Summary.Revision.Status switch { ExtractionStatus.Ready=>"Success",
        ExtractionStatus.Processing or ExtractionStatus.Pending=>"Warning", _=>"Error" };
    public string ErrorMessage => Summary.Revision.ErrorMessage ?? "";
    public bool HasError => ErrorMessage.Length > 0;
    public string Info => $"{KindLabel} · {SizeLabel} · {Summary.Source.CreatedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
    public string SizeLabel => Summary.Revision.SizeBytes >= 1024*1024 ? $"{Summary.Revision.SizeBytes/(1024d*1024):0.#} MB" :
        Summary.Revision.SizeBytes >= 1024 ? $"{Summary.Revision.SizeBytes/1024d:0.#} KB" : $"{Summary.Revision.SizeBytes} B";
    public string ChunkLabel => $"{Summary.ChunkCount} chunk";
    public bool CanRetry => Summary.Revision.Status is ExtractionStatus.Failed or ExtractionStatus.Cancelled &&
        (Summary.Revision.RelativeFilePath is not null || Summary.Source.Kind == SourceKind.Manual);
    public IAsyncRelayCommand OpenCommand { get; }
    public SourceCardViewModel(SourceSummary summary, Func<Task> open)
    { Summary=summary; OpenCommand=new AsyncRelayCommand(open); }
}
