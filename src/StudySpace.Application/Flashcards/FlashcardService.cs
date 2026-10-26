using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Common;
using StudySpace.Domain.Flashcards;
using StudySpace.Domain.Sources;
namespace StudySpace.Application.Flashcards;

public sealed class FlashcardService(IFlashcardRepository repository,IStudyContextRepository context,ProjectService projects,IClock clock)
{
    public const int PageSize=30;
    public async Task<IReadOnlyList<FlashCard>> SearchAsync(Guid projectId,FlashcardQuery query,int offset=0,CancellationToken ct=default)
    {
        await projects.GetAsync(projectId,ct); ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return await repository.SearchAsync(projectId,ValidateQuery(query),offset,PageSize,ct);
    }
    public async Task<IReadOnlyList<Guid>> StudyIdsAsync(Guid projectId,FlashcardQuery query,CancellationToken ct=default)
    { await projects.GetAsync(projectId,ct); return await repository.ListStudyIdsAsync(projectId,ValidateQuery(query),ct); }
    public async Task<IReadOnlyList<string>> TagsAsync(Guid projectId,CancellationToken ct=default)
    { await projects.GetAsync(projectId,ct); return await repository.ListTagsAsync(projectId,ct); }
    public async Task<FlashCard> GetAsync(Guid projectId,Guid id,CancellationToken ct=default)=>
        await repository.GetAsync(projectId,id,ct) ?? throw new FlashcardNotFoundException();
    public async Task<FlashCard> CreateAsync(Guid projectId,FlashcardDraft draft,CancellationToken ct=default)
    {
        await projects.GetAsync(projectId,ct);
        var now=clock.UtcNow;
        var card=await PrepareAsync(projectId,new FlashCard { ProjectId=projectId,Front="",Back="",CreatedAtUtc=now,UpdatedAtUtc=now },draft,ct);
        await repository.SaveAsync(card,null,ct); return card;
    }
    public async Task UpdateAsync(Guid projectId,Guid id,int expectedRevision,FlashcardDraft draft,CancellationToken ct=default)
    {
        var existing=await GetAsync(projectId,id,ct);
        if(existing.Revision!=expectedRevision) throw new FlashcardConflictException();
        var card=await PrepareAsync(projectId,existing,draft,ct);
        await repository.SaveAsync(card with { UpdatedAtUtc=clock.UtcNow,Revision=expectedRevision+1,
            IsUserEdited=existing.IsUserEdited || (existing.GenerationRunId.HasValue && !SameContent(existing,card)) },expectedRevision,ct);
    }
    public Task FavoriteAsync(FlashCard card,CancellationToken ct=default)=>
        repository.SetFavoriteAsync(card.ProjectId,card.Id,card.Revision,!card.IsFavorite,clock.UtcNow,ct);
    public Task DeleteAsync(FlashCard card,CancellationToken ct=default)=>repository.DeleteAsync(card.ProjectId,card.Id,card.Revision,ct);
    public async Task<IReadOnlyList<MaterialSource>> ReadySourcesAsync(Guid projectId,int offset=0,CancellationToken ct=default)
    { await projects.GetAsync(projectId,ct); return await context.ListReadySourcesAsync(projectId,offset,20,ct); }
    public Task<IReadOnlyList<StudyChunk>> SourceChunksAsync(Guid projectId,Guid sourceId,int offset=0,CancellationToken ct=default)=>
        context.ListChunksAsync(projectId,sourceId,offset,20,ct);

    internal async Task<FlashCard> PrepareAsync(Guid projectId,FlashCard existing,FlashcardDraft draft,CancellationToken ct)
    {
        var clean=Validate(draft);
        MaterialSource? source=null;
        if(clean.SourceId is {} sourceId)
            source=await context.GetSourceAsync(projectId,sourceId,existing.SourceId==sourceId,ct) ??
                throw new StudyValidationException("La fonte collegata non è disponibile in questo progetto.");
        var refs=clean.References!;
        var chunks=await context.GetChunksAsync(projectId,refs.Select(r=>r.ChunkId).ToArray(),ct);
        if(chunks.Count!=refs.Count) throw new StudyValidationException("I riferimenti devono appartenere a questo progetto.");
        var byId=chunks.ToDictionary(c=>c.Chunk.Id);
        var citations=new List<SourceCitation>();
        foreach(var r in refs)
        {
            var chunk=byId[r.ChunkId];
            var preserved=existing.Citations.Any(c=>c.ChunkId==r.ChunkId && c.Quote==r.Quote);
            if((!chunk.IsAvailable && !preserved) || !chunk.Chunk.Text.Contains(r.Quote,StringComparison.Ordinal))
                throw new StudyValidationException("Un riferimento non è valido o la fonte non è più pronta. Ricarica le fonti.");
            citations.Add(new(chunk.Chunk.SourceId,chunk.Chunk.SourceRevisionId,r.ChunkId,r.Quote,chunk.SourceTitle,Locator(chunk.Chunk)));
        }
        return existing with { Front=clean.Front,Back=clean.Back,Explanation=clean.Explanation,Difficulty=clean.Difficulty,
            Tags=Array.AsReadOnly(clean.Tags!.ToArray()),IsFavorite=clean.IsFavorite,SourceId=clean.SourceId,SourceTitle=source?.Title,
            Citations=Array.AsReadOnly(citations.ToArray()) };
    }
    public static FlashcardDraft Validate(FlashcardDraft draft)
    {
        var front=draft.Front?.Trim() ?? ""; var back=draft.Back?.Trim() ?? ""; var explanation=draft.Explanation?.Trim() ?? "";
        if(front.Length is <1 or >2000) throw new StudyValidationException("Inserisci una domanda da 1 a 2.000 caratteri.");
        if(back.Length is <1 or >8000) throw new StudyValidationException("Inserisci una risposta da 1 a 8.000 caratteri.");
        if(explanation.Length>8000) throw new StudyValidationException("La spiegazione può contenere al massimo 8.000 caratteri.");
        if(draft.Difficulty.HasValue && !Enum.IsDefined(draft.Difficulty.Value)) throw new StudyValidationException("Seleziona una difficoltà disponibile.");
        var tags=(draft.Tags ?? []).Select(t=>t.Trim()).Where(t=>t.Length>0).DistinctBy(ProjectText.NormalizeTag).ToArray();
        if(tags.Length>10 || tags.Any(t=>t.Length>32 || t.Contains(','))) throw new StudyValidationException("Usa fino a 10 etichette da 32 caratteri, separate da virgole.");
        var refs=(draft.References ?? []).Select(r=>r with { Quote=r.Quote?.Trim() ?? "" }).ToArray();
        if(refs.Length>20 || refs.Select(r=>r.ChunkId).Distinct().Count()!=refs.Length || refs.Any(r=>r.ChunkId==Guid.Empty || r.Quote.Length is <1 or >4000))
            throw new StudyValidationException("Seleziona fino a 20 riferimenti distinti con estratti validi.");
        if(draft.SourceId==Guid.Empty) throw new StudyValidationException("Seleziona una fonte valida oppure nessuna fonte.");
        return new(front,back,explanation.Length==0 ? null : explanation,draft.Difficulty,Array.AsReadOnly(tags),draft.IsFavorite,draft.SourceId,Array.AsReadOnly(refs));
    }
    public static FlashcardQuery ValidateQuery(FlashcardQuery query)
    {
        var search=query.Search?.Trim() ?? "";
        if(search.Length>200) throw new StudyValidationException("La ricerca può contenere al massimo 200 caratteri.");
        if(query.Difficulty.HasValue && !Enum.IsDefined(query.Difficulty.Value)) throw new StudyValidationException("Difficoltà non valida.");
        return query with { Search=search,Tag=string.IsNullOrWhiteSpace(query.Tag) ? null : query.Tag.Trim() };
    }
    internal static bool SameContent(FlashCard a,FlashCard b)=>a.Front==b.Front && a.Back==b.Back && a.Explanation==b.Explanation &&
        a.Difficulty==b.Difficulty && a.SourceId==b.SourceId && a.Citations.Select(c=>(c.ChunkId,c.Quote)).OrderBy(c=>c.ChunkId)
        .SequenceEqual(b.Citations.Select(c=>(c.ChunkId,c.Quote)).OrderBy(c=>c.ChunkId));
    public static string? Locator(SourceChunk chunk)=>string.Join(" · ",new[] { chunk.PageStart.HasValue ?
        (chunk.PageStart==chunk.PageEnd ? $"Pagina {chunk.PageStart}" : $"Pagine {chunk.PageStart}–{chunk.PageEnd}") : null,chunk.SectionPath }
        .Where(s=>!string.IsNullOrWhiteSpace(s))) is { Length:>0 } locator ? locator : null;
}
