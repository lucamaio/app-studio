using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
using StudySpace.Domain.Projects;
using StudySpace.Domain.Sources;
using StudySpace.Infrastructure.Files;
using StudySpace.Infrastructure.Import;
using StudySpace.Infrastructure.Persistence;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
namespace StudySpace.IntegrationTests;

public sealed class SourceImportTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"StudySpace.SourceTests",Guid.NewGuid().ToString("N"));
    private readonly Clock clock=new();
    private SourceImportOptions options=new();
    private DatabaseOptions Options=>new(Path.Combine(directory,"test.db3"));
    private StudyDatabase Database()=>new(Options,NullLogger<StudyDatabase>.Instance);
    private SourceRepository Repository()=>new(Database());
    private SourceFileStore Files()=>new(Options,options,NullLogger<SourceFileStore>.Instance);
    private ProjectRepository Projects()=>new(Database(),Files());
    private SourceImportService Importer()=>new(Repository(),Files(),
        [new PlainTextExtractor(options),new MarkdownTextExtractor(options),new DocxTextExtractor(options),new PdfTextExtractor(options)],
        new SourceChunkingService(options),new ProjectService(Projects(),clock),clock,options);
    private async Task<StudyProject> ProjectAsync()
    {
        var p=new StudyProject { Name="Reti" }; await Projects().AddAsync(p); return p;
    }
    private static SourceFileSelection File(string name,byte[] data,string? mime=null)=>new(name,mime,
        _=>Task.FromResult<Stream>(new MemoryStream(data,writable:false)));

    [Fact] public async Task Txt_import_persists_private_copy_metadata_hash_text_and_chunks_after_reopening()
    {
        var p=await ProjectAsync();
        var bytes=Encoding.UTF8.GetBytes("È una rete.\r\n\r\nGli host comunicano.");
        var imported=await Importer().ImportFileAsync(p.Id,File("appunti.txt",bytes,"text/plain"));
        var reopened=(await Repository().GetAsync(p.Id,imported.Summary.Source.Id))!.Summary;
        Assert.Equal(ExtractionStatus.Ready,reopened.Revision.Status);
        Assert.Equal(SourceKind.Text,reopened.Source.Kind);
        Assert.Equal("appunti",reopened.Source.Title); Assert.Equal("appunti.txt",reopened.Source.OriginalFileName);
        Assert.Equal(clock.UtcNow,reopened.Source.CreatedAtUtc); Assert.Equal(p.Id,reopened.Source.ProjectId);
        Assert.Equal(bytes.Length,reopened.Revision.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),reopened.Revision.ContentHash);
        Assert.Equal("È una rete.\n\nGli host comunicano.",reopened.Revision.ExtractedText);
        Assert.StartsWith($"sources/{p.Id:D}/",reopened.Revision.RelativeFilePath);
        await using var copy=await Files().OpenReadAsync(reopened.Revision.RelativeFilePath!,default);
        using var actual=new MemoryStream(); await copy.CopyToAsync(actual); Assert.Equal(bytes,actual.ToArray());
        var chunk=Assert.Single(await Repository().GetChunksAsync(p.Id,reopened.Source.Id,0,20));
        Assert.Equal(reopened.Source.Id,chunk.SourceId); Assert.Equal(p.Id,chunk.ProjectId);
        Assert.Equal(reopened.Revision.Id,chunk.SourceRevisionId); Assert.Equal(reopened.Revision.ExtractedText,chunk.Text);
        Assert.Equal(chunk.Text.Length,chunk.EndOffset-chunk.StartOffset);
        Assert.Empty(Assert.Single(await Repository().ListAsync(p.Id,0,30)).Revision.ExtractedText);
    }
    [Fact] public async Task Manual_text_requires_no_file_and_has_persistent_size_and_normalized_hash()
    {
        var p=await ProjectAsync();
        var result=(await Importer().ImportTextAsync(p.Id,"  Appunto  ","Testo\r\nmanuale")).Summary;
        Assert.Equal("Appunto",result.Source.Title); Assert.Null(result.Source.OriginalFileName);
        Assert.Null(result.Revision.RelativeFilePath); Assert.Equal(ExtractionStatus.Ready,result.Revision.Status);
        Assert.Equal(Encoding.UTF8.GetByteCount("Testo\r\nmanuale"),result.Revision.SizeBytes);
        Assert.NotEmpty(result.Revision.ContentHash);
    }
    [Theory]
    [InlineData("file.pdf")]
    [InlineData("file.docx")]
    public async Task Corrupt_documents_persist_failed_state_without_partial_chunks(string name)
    {
        var p=await ProjectAsync();
        var result=(await Importer().ImportFileAsync(p.Id,File(name,[1,2,3,4]))).Summary;
        Assert.Equal(ExtractionStatus.Failed,result.Revision.Status); Assert.NotEmpty(result.Revision.ErrorMessage!);
        Assert.Equal("UnreadableDocument",result.Revision.ErrorCode); Assert.Equal(0,result.ChunkCount);
        Assert.Empty(result.Revision.ExtractedText); Assert.NotNull(result.Revision.RelativeFilePath);
    }
    [Fact] public async Task Invalid_encoding_is_reported_instead_of_replacing_characters()
    {
        var p=await ProjectAsync();
        var result=(await Importer().ImportFileAsync(p.Id,File("appunti.txt",[0xC3,0x28]))).Summary;
        Assert.Equal(ExtractionStatus.Failed,result.Revision.Status); Assert.Equal("InvalidEncoding",result.Revision.ErrorCode);
    }
    [Fact] public async Task Utf16_bom_is_read_strictly()
    {
        var p=await ProjectAsync(); var encoding=new UnicodeEncoding(false,true,true);
        var bytes=encoding.GetPreamble().Concat(encoding.GetBytes("Città università")).ToArray();
        var result=(await Importer().ImportFileAsync(p.Id,File("unicode.txt",bytes))).Summary;
        Assert.Equal("Città università",result.Revision.ExtractedText);
    }
    [Fact] public async Task Empty_and_binary_text_are_failed_sources()
    {
        var p=await ProjectAsync(); var importer=Importer();
        var blank=(await importer.ImportFileAsync(p.Id,File("vuoto.txt",[]))).Summary;
        var binary=(await importer.ImportFileAsync(p.Id,File("binario.txt",[0,1,2]))).Summary;
        Assert.Equal("EmptyFile",blank.Revision.ErrorCode); Assert.Equal("BinaryText",binary.Revision.ErrorCode);
    }
    [Fact] public async Task Size_limit_applies_while_copying_and_cleans_partial_file()
    {
        options=new() { MaxFileBytes=16 }; var p=await ProjectAsync();
        var result=(await Importer().ImportFileAsync(p.Id,File("grande.txt",new byte[17]))).Summary;
        Assert.Equal("FileTooLarge",result.Revision.ErrorCode); Assert.Null(result.Revision.RelativeFilePath);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directory,"sources"),"*",SearchOption.AllDirectories));
    }
    [Fact] public async Task Wrong_extension_or_mime_is_rejected_before_a_source_is_created()
    {
        var p=await ProjectAsync(); var importer=Importer();
        await Assert.ThrowsAsync<StudyValidationException>(()=>importer.ImportFileAsync(p.Id,File("app.exe",[1])));
        await Assert.ThrowsAsync<StudyValidationException>(()=>importer.ImportFileAsync(p.Id,File("app.txt",[1],"application/pdf")));
        Assert.Empty(await Repository().ListAsync(p.Id,0,30));
    }
    [Fact] public async Task Pdf_extracts_page_locators_and_scanned_pdf_has_actionable_error()
    {
        var p=await ProjectAsync(); var importer=Importer();
        var pdf=(await importer.ImportFileAsync(p.Id,File("lezione.pdf",Pdf(true)))).Summary;
        Assert.Equal(ExtractionStatus.Ready,pdf.Revision.Status); Assert.Contains("Computer networks",pdf.Revision.ExtractedText);
        var chunk=Assert.Single(await Repository().GetChunksAsync(p.Id,pdf.Source.Id,0,20));
        Assert.Equal(1,chunk.PageStart); Assert.Equal(2,chunk.PageEnd);
        var scan=(await importer.ImportFileAsync(p.Id,File("scan.pdf",Pdf(false)))).Summary;
        Assert.Equal("NoText",scan.Revision.ErrorCode); Assert.Contains("OCR",scan.Revision.ErrorMessage);
    }
    [Fact] public async Task Docx_extracts_headings_paragraphs_and_table_cells_without_fabricated_pages()
    {
        var p=await ProjectAsync();
        var result=(await Importer().ImportFileAsync(p.Id,File("lezione.docx",Docx()))).Summary;
        Assert.Equal(ExtractionStatus.Ready,result.Revision.Status);
        Assert.Contains("Protocolli",result.Revision.ExtractedText); Assert.Contains("TCP",result.Revision.ExtractedText);
        Assert.Contains("Affidabile",result.Revision.ExtractedText);
        var chunk=Assert.Single(await Repository().GetChunksAsync(p.Id,result.Source.Id,0,20));
        Assert.Contains("Protocolli",chunk.SectionPath); Assert.Null(chunk.PageStart);
    }
    [Fact] public async Task Docx_expansion_limit_and_xml_external_entities_are_rejected()
    {
        var p=await ProjectAsync(); options=new() { MaxDocxExpandedBytes=32 };
        var large=(await Importer().ImportFileAsync(p.Id,File("large.docx",Docx()))).Summary;
        Assert.Equal("ExpandedTooLarge",large.Revision.ErrorCode);
        options=new();
        var xml=(await Importer().ImportFileAsync(p.Id,File("entity.docx",Docx("<!DOCTYPE document [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><document>&x;</document>")))).Summary;
        Assert.Equal(ExtractionStatus.Failed,xml.Revision.Status); Assert.Equal(0,xml.ChunkCount);
    }
    [Fact] public async Task Markdown_retains_sections_and_link_labels_as_plain_text()
    {
        var p=await ProjectAsync();
        var result=(await Importer().ImportFileAsync(p.Id,File("lezione.md",Encoding.UTF8.GetBytes("# Reti\n\nConsulta [la guida](https://example.invalid).\n")))).Summary;
        Assert.Equal("Reti\n\nConsulta la guida.",result.Revision.ExtractedText);
        Assert.Equal("Reti",Assert.Single(await Repository().GetChunksAsync(p.Id,result.Source.Id,0,20)).SectionPath);
    }
    [Fact] public async Task Rename_archive_and_reads_are_scoped_and_keep_citation_chunks()
    {
        var p=await ProjectAsync(); var other=await ProjectAsync();
        var result=(await Importer().ImportTextAsync(p.Id,"Nome","Materiale universitario")).Summary;
        var repository=Repository();
        Assert.Null(await repository.GetAsync(other.Id,result.Source.Id));
        Assert.Empty(await repository.GetChunksAsync(other.Id,result.Source.Id,0,20));
        await Assert.ThrowsAsync<SourceConflictException>(()=>repository.RenameAsync(other.Id,result.Source.Id,1,"Altro",clock.UtcNow));
        await repository.RenameAsync(p.Id,result.Source.Id,1,"Nuovo",clock.UtcNow.AddMinutes(1));
        await Assert.ThrowsAsync<SourceConflictException>(()=>repository.ArchiveAsync(p.Id,result.Source.Id,1,clock.UtcNow));
        await repository.ArchiveAsync(p.Id,result.Source.Id,2,clock.UtcNow);
        Assert.Null(await repository.GetAsync(p.Id,result.Source.Id)); Assert.Empty(await repository.ListAsync(p.Id,0,30));
        Assert.Equal(1,await Database().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM SourceChunk WHERE ProjectId=?",p.Id.ToString())));
        Assert.Equal(0,(await Projects().GetSummaryAsync(p.Id))!.SourceCount);
    }
    [Fact] public async Task Interrupted_import_is_failed_after_restart_and_retry_is_atomic_and_not_duplicated()
    {
        var p=await ProjectAsync(); var repository=Repository();
        var revisionId=Guid.NewGuid(); var source=new StudySource { ProjectId=p.Id,Title="Interrotta",Kind=SourceKind.Text,CurrentRevisionId=revisionId };
        var revision=new SourceRevision { Id=revisionId,ProjectId=p.Id,SourceId=source.Id,Status=ExtractionStatus.Processing };
        await repository.CreateAsync(source,revision);
        var stored=await Files().SaveAsync(p.Id,source.Id,revisionId,".txt",new MemoryStream(Encoding.UTF8.GetBytes("Testo recuperato")),default);
        await repository.SetFileAsync(p.Id,source.Id,revisionId,stored);
        var importer=Importer(); await importer.InitializeAsync();
        var failed=(await repository.GetAsync(p.Id,source.Id))!.Summary;
        Assert.Equal("Interrupted",failed.Revision.ErrorCode);
        var retried=await importer.RetryAsync(p.Id,source.Id);
        Assert.Equal(source.Id,retried.Summary.Source.Id); Assert.Equal(ExtractionStatus.Ready,retried.Summary.Revision.Status);
        Assert.Equal(1,retried.Summary.ChunkCount);
        // A recoverable copy survives restart and is retried using the same identity.
        var manual=await importer.ImportTextAsync(p.Id,"Pronta","Contenuto");
        await Assert.ThrowsAsync<StudyValidationException>(()=>importer.RetryAsync(p.Id,manual.Summary.Source.Id));
        var bad=(await importer.ImportFileAsync(p.Id,File("retry.txt",[0xC3,0x28]))).Summary;
        var path=Path.Combine(directory,bad.Revision.RelativeFilePath!.Replace('/',Path.DirectorySeparatorChar));
        await System.IO.File.WriteAllTextAsync(path,"Testo recuperato",new UTF8Encoding(false));
        var changed=await importer.RetryAsync(p.Id,bad.Source.Id);
        Assert.Equal("ChangedLocalFile",changed.Summary.Revision.ErrorCode);
    }
    [Fact] public async Task Commit_failure_rolls_back_text_and_all_chunks()
    {
        var p=await ProjectAsync(); var repo=Repository(); var rev=Guid.NewGuid();
        var source=new StudySource { ProjectId=p.Id,Title="Rollback",CurrentRevisionId=rev };
        await repo.CreateAsync(source,new SourceRevision { Id=rev,SourceId=source.Id,ProjectId=p.Id,Status=ExtractionStatus.Processing });
        var chunk=new SourceChunk { ProjectId=p.Id,SourceId=source.Id,SourceRevisionId=rev,Ordinal=0,Text="Testo",StartOffset=0,EndOffset=5 };
        await Assert.ThrowsAsync<StorageUnavailableException>(()=>repo.CompleteAsync(p.Id,source.Id,rev,"Testo","test",[chunk,chunk with { Id=Guid.NewGuid() }],clock.UtcNow));
        var actual=(await repo.GetAsync(p.Id,source.Id))!.Summary;
        Assert.Equal(ExtractionStatus.Processing,actual.Revision.Status); Assert.Empty(actual.Revision.ExtractedText); Assert.Equal(0,actual.ChunkCount);
    }
    [Fact] public async Task Project_delete_removes_its_files_and_orphan_sweep_preserves_other_archived_sources()
    {
        var p=await ProjectAsync(); var other=await ProjectAsync(); var importer=Importer();
        var first=(await importer.ImportFileAsync(p.Id,File("first.txt",Encoding.UTF8.GetBytes("Uno")))).Summary;
        var second=(await importer.ImportFileAsync(other.Id,File("second.txt",Encoding.UTF8.GetBytes("Due")))).Summary;
        await Repository().ArchiveAsync(other.Id,second.Source.Id,1,clock.UtcNow);
        var orphan=await Files().SaveAsync(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),".txt",new MemoryStream([65]),default);
        await Projects().DeleteAsync(p.Id,p.Revision);
        Assert.False(System.IO.File.Exists(Path.Combine(directory,first.Revision.RelativeFilePath!)));
        await Importer().InitializeAsync();
        Assert.False(System.IO.File.Exists(Path.Combine(directory,orphan.RelativePath)));
        Assert.True(System.IO.File.Exists(Path.Combine(directory,second.Revision.RelativeFilePath!)));
    }
    [Fact] public async Task File_store_rejects_paths_outside_its_private_root()
    {
        await Assert.ThrowsAsync<IOException>(()=>Files().OpenReadAsync("../outside.txt",default));
        await Assert.ThrowsAsync<IOException>(()=>Files().OpenReadAsync("C:/outside.txt",default));
    }
    [Fact] public async Task Cancellation_after_source_creation_is_persisted_without_partial_chunks()
    {
        var p=await ProjectAsync(); using var cancellation=new CancellationTokenSource();
        var selection=new SourceFileSelection("cancel.txt",null,_=>
        {
            cancellation.Cancel();
            return Task.FromCanceled<Stream>(cancellation.Token);
        });
        var result=(await Importer().ImportFileAsync(p.Id,selection,ct:cancellation.Token)).Summary;
        Assert.Equal(ExtractionStatus.Cancelled,result.Revision.Status);
        Assert.Equal("Cancelled",result.Revision.ErrorCode); Assert.Equal(0,result.ChunkCount);
        Assert.Equal(ExtractionStatus.Cancelled,(await Repository().GetAsync(p.Id,result.Source.Id))!.Summary.Revision.Status);
    }
    [Fact] public async Task Orphan_cleanup_preserves_files_in_the_trusted_app_data_parent()
    {
        Directory.CreateDirectory(directory);
        var outside=Path.Combine(directory,"personal-note.txt"); await System.IO.File.WriteAllTextAsync(outside,"Keep");
        await Files().SaveAsync(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),".txt",new MemoryStream([65]),default);
        await Files().CleanupOrphansAsync(new HashSet<string>(),default);
        Assert.Equal("Keep",await System.IO.File.ReadAllTextAsync(outside));
        Assert.False(Directory.Exists(Path.Combine(directory,"sources")));
    }
    private static byte[] Pdf(bool withText)
    {
        var builder=new PdfDocumentBuilder(); var font=builder.AddStandard14Font(Standard14Font.Helvetica);
        var first=builder.AddPage(PageSize.A4);
        if(withText) first.AddText("Computer networks",12,new PdfPoint(25,700),font);
        var second=builder.AddPage(PageSize.A4);
        if(withText) second.AddText("Transport protocols",12,new PdfPoint(25,700),font);
        return builder.Build();
    }
    private static byte[] Docx(string? document=null)
    {
        using var result=new MemoryStream();
        using(var zip=new ZipArchive(result,ZipArchiveMode.Create,true))
        {
            using(var writer=new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open())) writer.Write("<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'/>");
            using var body=new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            body.Write(document ?? """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                 <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Protocolli</w:t></w:r></w:p>
                 <w:p><w:r><w:t>Livello trasporto</w:t></w:r></w:p>
                 <w:tbl><w:tr><w:tc><w:p><w:r><w:t>TCP</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Affidabile</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                </w:body></w:document>
                """);
        }
        return result.ToArray();
    }
    private sealed class Clock:IClock { public DateTimeOffset UtcNow=>new(2026,10,24,12,0,0,TimeSpan.Zero); }
    public void Dispose() { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
}
