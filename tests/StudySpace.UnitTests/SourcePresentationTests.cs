using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.App.Features.Sources;
using StudySpace.App.Services;
using StudySpace.Application.Projects;
using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
using StudySpace.Domain.Projects;
using StudySpace.Domain.Sources;
namespace StudySpace.UnitTests;

public sealed class SourcePresentationTests
{
    private readonly TestProjectRepository projects=new();
    private readonly TestClock clock=new();
    private readonly SourceRepository repository=new();
    private readonly Importer importer=new();
    private readonly TestNavigation navigation=new();
    private readonly Interactions interactions=new();
    private SourceService Service()=>new(repository,importer,new(projects,clock),clock);
    private SourceDetailViewModel Detail()=>new(Service(),importer,interactions,navigation,NullLogger<SourceDetailViewModel>.Instance);

    [Fact] public async Task Manual_save_after_navigation_failure_does_not_create_duplicate_source()
    {
        var p=new StudyProject { Name="Reti" }; projects.Items.Add(p);
        var vm=new ManualSourceViewModel(importer,new(projects,clock),navigation,NullLogger<ManualSourceViewModel>.Instance);
        vm.Configure(p.Id.ToString()); await vm.LoadCommand.ExecuteAsync(null);
        vm.Title="Appunti"; vm.Text="Materiale"; navigation.FailBack=true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaved); Assert.True(vm.HasError); Assert.Equal(1,importer.ManualCalls);
        navigation.FailBack=false; await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1,importer.ManualCalls); Assert.False(vm.HasError);
    }
    [Fact] public async Task Invalid_project_route_blocks_manual_import_and_file_picker()
    {
        var manual=new ManualSourceViewModel(importer,new(projects,clock),navigation,NullLogger<ManualSourceViewModel>.Instance);
        manual.Configure("invalid"); await manual.LoadCommand.ExecuteAsync(null);
        Assert.True(manual.HasError); Assert.False(manual.IsReady);
        await manual.SaveCommand.ExecuteAsync(null); Assert.Equal(0,importer.ManualCalls);
        var list=new SourcesViewModel(Service(),importer,new(projects,clock),interactions,navigation,NullLogger<SourcesViewModel>.Instance);
        list.Configure("invalid"); await list.LoadCommand.ExecuteAsync(null); await list.ImportFileCommand.ExecuteAsync(null);
        Assert.False(list.IsReady); Assert.Equal(0,interactions.PickerCalls);
    }
    [Fact] public async Task Source_delete_requires_confirmation_and_returns_to_project_sources()
    {
        var source=Summary(); repository.Items.Add(source);
        var vm=Detail(); vm.Configure(source.Source.ProjectId.ToString(),source.Source.Id.ToString());
        await vm.LoadCommand.ExecuteAsync(null); await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Single(repository.Items); Assert.Equal(1,interactions.ConfirmCalls);
        interactions.Confirmed=true; await vm.DeleteCommand.ExecuteAsync(null);
        Assert.Empty(repository.Items); Assert.Equal("sources",navigation.Section); Assert.False(vm.IsReady);
    }
    [Fact] public async Task Detail_paginates_text_and_chunks_without_retaining_all_chunks_in_ui()
    {
        var summary=Summary(new string('x',13000),45); repository.Items.Add(summary);
        var vm=Detail(); vm.Configure(summary.Source.ProjectId.ToString(),summary.Source.Id.ToString());
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(6000,vm.ExtractedPage.Length); Assert.Equal(20,vm.Chunks.Count); Assert.True(vm.HasMoreChunks);
        vm.NextTextCommand.Execute(null); vm.NextTextCommand.Execute(null); Assert.Equal(1000,vm.ExtractedPage.Length); Assert.False(vm.CanNext);
        vm.PreviousTextCommand.Execute(null); Assert.Equal(6000,vm.ExtractedPage.Length);
        await vm.LoadMoreChunksCommand.ExecuteAsync(null); Assert.Equal(20,vm.Chunks.Count);
        await vm.LoadMoreChunksCommand.ExecuteAsync(null); Assert.Equal(5,vm.Chunks.Count); Assert.False(vm.HasMoreChunks);
        await vm.PreviousChunksCommand.ExecuteAsync(null); Assert.Equal(20,vm.Chunks.Count);
    }
    [Fact] public async Task Source_details_are_not_accessible_through_another_project_route()
    {
        var source=Summary(); repository.Items.Add(source);
        var vm=Detail(); vm.Configure(Guid.NewGuid().ToString(),source.Source.Id.ToString());
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasError); Assert.False(vm.IsReady); Assert.Null(vm.Card);
    }
    [Fact] public async Task Empty_source_list_and_cancelled_picker_do_not_create_sources()
    {
        var p=new StudyProject { Name="Reti" }; projects.Items.Add(p);
        var vm=new SourcesViewModel(Service(),importer,new(projects,clock),interactions,navigation,NullLogger<SourcesViewModel>.Instance);
        vm.Configure(p.Id.ToString()); await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty); Assert.True(vm.IsReady);
        await vm.ImportFileCommand.ExecuteAsync(null); Assert.Equal(1,interactions.PickerCalls); Assert.Empty(vm.Items);
    }
    [Fact] public void Error_status_has_message_and_retry_only_when_local_content_is_available()
    {
        var ready=Summary(); var failed=ready with { Revision=ready.Revision with { Status=ExtractionStatus.Failed,ErrorMessage="Documento corrotto",RelativeFilePath=null } };
        var card=new SourceCardViewModel(failed,()=>Task.CompletedTask);
        Assert.Equal("Errore",card.Status); Assert.True(card.HasError); Assert.False(card.CanRetry);
        var retryable=new SourceCardViewModel(failed with { Revision=failed.Revision with { RelativeFilePath="copy" } },()=>Task.CompletedTask);
        Assert.True(retryable.CanRetry);
    }
    private static SourceSummary Summary(string text="Appunti",int chunks=1)
    {
        var rev=Guid.NewGuid(); var source=new StudySource { ProjectId=Guid.NewGuid(),Title="Testo",Kind=SourceKind.Text,CurrentRevisionId=rev };
        return new(source,new SourceRevision { Id=rev,ProjectId=source.ProjectId,SourceId=source.Id,ExtractedText=text,Status=ExtractionStatus.Ready },chunks);
    }
    private sealed class Interactions:ISourceInteractions
    {
        public bool Confirmed; public int ConfirmCalls; public int PickerCalls;
        public Task<SourceFileSelection?> PickFileAsync() { PickerCalls++; return Task.FromResult<SourceFileSelection?>(null); }
        public Task<bool> ConfirmArchiveAsync(string title) { ConfirmCalls++; return Task.FromResult(Confirmed); }
    }
    private sealed class Importer:ISourceImporter
    {
        public int ManualCalls;
        public Task InitializeAsync(CancellationToken ct=default)=>Task.CompletedTask;
        public Task<SourceDetails> ImportTextAsync(Guid projectId,string title,string text,CancellationToken ct=default)
        { ManualCalls++; return Task.FromResult(new SourceDetails(Summary(text))); }
        public Task<SourceDetails> ImportFileAsync(Guid projectId,SourceFileSelection file,IProgress<SourceSummary>? progress=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<SourceDetails> RetryAsync(Guid projectId,Guid sourceId,CancellationToken ct=default)=>throw new NotSupportedException();
    }
    private sealed class SourceRepository:ISourceRepository
    {
        public List<SourceSummary> Items=[];
        public Task<SourceDetails?> GetAsync(Guid p,Guid s,CancellationToken ct=default)=>Task.FromResult(Items.FirstOrDefault(x=>x.Source.ProjectId==p && x.Source.Id==s) is {} summary ? new SourceDetails(summary) : null);
        public Task<IReadOnlyList<SourceSummary>> ListAsync(Guid p,int offset,int limit,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<SourceSummary>>(Items.Where(x=>x.Source.ProjectId==p).Skip(offset).Take(limit).ToList());
        public Task<IReadOnlyList<SourceChunk>> GetChunksAsync(Guid p,Guid s,int offset,int limit,CancellationToken ct=default)
        {
            var item=Items.Single(x=>x.Source.ProjectId==p && x.Source.Id==s);
            return Task.FromResult<IReadOnlyList<SourceChunk>>(Enumerable.Range(0,item.ChunkCount).Skip(offset).Take(limit).Select(i=>new SourceChunk { ProjectId=p,SourceId=s,SourceRevisionId=item.Revision.Id,Ordinal=i,Text="Chunk" }).ToArray());
        }
        public Task ArchiveAsync(Guid p,Guid s,int expectedRevision,DateTimeOffset now,CancellationToken ct=default)
        { Items.RemoveAll(x=>x.Source.ProjectId==p && x.Source.Id==s); return Task.CompletedTask; }
        public Task RenameAsync(Guid p,Guid s,int expectedRevision,string title,DateTimeOffset now,CancellationToken ct=default)=>Task.CompletedTask;
        public Task CreateAsync(StudySource source,SourceRevision revision,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task SetFileAsync(Guid p,Guid s,Guid r,StoredSourceFile file,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task CompleteAsync(Guid p,Guid s,Guid r,string text,string extractor,IReadOnlyList<SourceChunk> chunks,DateTimeOffset now,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task SetStatusAsync(Guid p,Guid s,ExtractionStatus status,string? code,string? message,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task RecoverInterruptedAsync(CancellationToken ct=default)=>Task.CompletedTask;
        public Task<IReadOnlySet<string>> ReferencedFilesAsync(CancellationToken ct=default)=>Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }
}
