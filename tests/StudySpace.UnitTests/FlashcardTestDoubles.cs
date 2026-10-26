using StudySpace.App.Services;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Domain.Flashcards;
namespace StudySpace.UnitTests;

internal sealed class TestFlashcardRepository:IFlashcardRepository
{
    public List<FlashCard> Items { get; }=[];
    public Dictionary<Guid,string> Runs { get; }=[];
    public int Saves,BatchSaves,Deletes,Favorites;
    public FlashcardQuery? LastQuery;
    private IEnumerable<FlashCard> Filter(Guid p,FlashcardQuery q)=>Items.Where(c=>c.ProjectId==p && (!q.FavoritesOnly || c.IsFavorite) &&
        (!q.Difficulty.HasValue || q.Difficulty==c.Difficulty) && (q.Tag is null || c.Tags.Contains(q.Tag)) &&
        ProjectText.SearchKey(c.Front+"\n"+c.Back+"\n"+c.Explanation).Contains(ProjectText.SearchKey(q.Search)));
    public Task<IReadOnlyList<FlashCard>> SearchAsync(Guid p,FlashcardQuery q,int offset,int limit,CancellationToken ct=default)
    { LastQuery=q; return Task.FromResult<IReadOnlyList<FlashCard>>(Filter(p,q).Skip(offset).Take(limit).ToArray()); }
    public Task<IReadOnlyList<Guid>> ListStudyIdsAsync(Guid p,FlashcardQuery q,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<Guid>>(Filter(p,q).Select(c=>c.Id).ToArray());
    public Task<IReadOnlyList<string>> ListTagsAsync(Guid p,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<string>>(Items.Where(c=>c.ProjectId==p).SelectMany(c=>c.Tags).Distinct().ToArray());
    public Task<FlashCard?> GetAsync(Guid p,Guid id,CancellationToken ct=default)=>Task.FromResult(Items.SingleOrDefault(c=>c.ProjectId==p && c.Id==id));
    public Task SaveAsync(FlashCard card,int? expected,CancellationToken ct=default)
    {
        Saves++;
        if(expected.HasValue)
        {
            var index=Items.FindIndex(c=>c.ProjectId==card.ProjectId && c.Id==card.Id && c.Revision==expected);
            if(index<0) throw new FlashcardConflictException(); Items[index]=card;
        }
        else Items.Add(card);
        return Task.CompletedTask;
    }
    public Task SetFavoriteAsync(Guid p,Guid id,int revision,bool favorite,DateTimeOffset now,CancellationToken ct=default)
    {
        Favorites++; var index=Items.FindIndex(c=>c.ProjectId==p && c.Id==id && c.Revision==revision);
        if(index<0) throw new FlashcardConflictException(); Items[index]=Items[index] with { IsFavorite=favorite,UpdatedAtUtc=now,Revision=revision+1 };
        return Task.CompletedTask;
    }
    public Task DeleteAsync(Guid p,Guid id,int revision,CancellationToken ct=default)
    {
        Deletes++; if(Items.RemoveAll(c=>c.ProjectId==p && c.Id==id && c.Revision==revision)!=1) throw new FlashcardConflictException();
        return Task.CompletedTask;
    }
    public Task SaveGeneratedAsync(FlashcardGenerationRun run,string hash,IReadOnlyList<FlashCard> cards,CancellationToken ct=default)
    {
        BatchSaves++;
        if(Runs.TryGetValue(run.Id,out var previous)) { if(previous!=hash) throw new FlashcardConflictException(); return Task.CompletedTask; }
        Runs.Add(run.Id,hash); Items.AddRange(cards); return Task.CompletedTask;
    }
}
internal sealed class TestStudyContext:IStudyContextRepository
{
    public List<StudyChunk> Chunks { get; }=[];
    public int LastSourceOffset;
    public Task<IReadOnlyList<MaterialSource>> ListReadySourcesAsync(Guid p,int offset,int limit,CancellationToken ct=default)
    {
        LastSourceOffset=offset;
        return Task.FromResult<IReadOnlyList<MaterialSource>>(Chunks.Where(c=>c.Chunk.ProjectId==p && c.IsAvailable).GroupBy(c=>c.Chunk.SourceId)
            .Select(g=>new MaterialSource(g.Key,g.First().SourceTitle,g.Count())).Skip(offset).Take(limit).ToArray());
    }
    public Task<IReadOnlyList<StudyChunk>> ListChunksAsync(Guid p,Guid s,int offset,int limit,CancellationToken ct=default)=>
        Task.FromResult<IReadOnlyList<StudyChunk>>(Chunks.Where(c=>c.Chunk.ProjectId==p && c.Chunk.SourceId==s && c.IsAvailable).OrderBy(c=>c.Chunk.Ordinal).Skip(offset).Take(limit).ToArray());
    public Task<IReadOnlyList<StudyChunk>> GetChunksAsync(Guid p,IReadOnlyList<Guid> ids,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<StudyChunk>>(Chunks.Where(c=>c.Chunk.ProjectId==p && ids.Contains(c.Chunk.Id)).ToArray());
    public Task<MaterialSource?> GetSourceAsync(Guid p,Guid s,bool includeArchived,CancellationToken ct=default)
    {
        var chunks=Chunks.Where(c=>c.Chunk.ProjectId==p && c.Chunk.SourceId==s && (includeArchived || c.IsAvailable)).ToArray();
        return Task.FromResult(chunks.Length==0 ? null : new MaterialSource(s,chunks[0].SourceTitle,chunks.Length));
    }
}
internal sealed class TestAiStudyService:IAiStudyService
{
    public AiProviderInfo Provider { get; }=new("test-mock","Provider di prova",true);
    public int Calls; public FlashcardGenerationRequest? Request;
    public Func<FlashcardGenerationRequest,AiFlashcardResult>? Response;
    public Task<AiFlashcardResult> GenerateFlashCardsAsync(FlashcardGenerationRequest request,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested(); Calls++; Request=request;
        return Task.FromResult(Response?.Invoke(request) ?? new AiFlashcardResult([new("Domanda dalla fonte?",request.Chunks[0].Chunk.Text,null,
            request.Difficulty,[new(request.Chunks[0].Chunk.Id,request.Chunks[0].Chunk.Text)])],[]));
    }
}
internal sealed class TestFlashcardDialogs:IFlashcardDialogs
{
    public bool Confirmed; public int Calls;
    public Task<bool> ConfirmDeleteAsync(string question) { Calls++; return Task.FromResult(Confirmed); }
}
