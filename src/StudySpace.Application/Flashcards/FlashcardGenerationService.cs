using System.Security.Cryptography;
using System.Text;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Projects;
using StudySpace.Domain.Common;
using StudySpace.Domain.Flashcards;
namespace StudySpace.Application.Flashcards;

public sealed class FlashcardGenerationService(IAiStudyService ai,IStudyContextRepository context,IFlashcardRepository repository,
    ProjectService projects,IClock clock)
{
    public const int MaxCards=30,MaxChunks=20,MaxContextCharacters=40000;
    public AiProviderInfo Provider=>ai.Provider;
    public async Task<FlashcardDraftBatch> GenerateAsync(Guid projectId,IReadOnlyList<Guid> chunkIds,int count,Difficulty? difficulty,CancellationToken ct=default)
    {
        if(count is <1 or >MaxCards) throw new StudyValidationException("Richiedi da 1 a 30 flashcard.");
        if(difficulty.HasValue && !Enum.IsDefined(difficulty.Value)) throw new StudyValidationException("Difficoltà non valida.");
        if(chunkIds.Count is <1 or >MaxChunks || chunkIds.Distinct().Count()!=chunkIds.Count)
            throw new StudyValidationException("Seleziona da 1 a 20 chunk distinti.");
        var project=await projects.GetAsync(projectId,ct);
        var chunks=await context.GetChunksAsync(projectId,chunkIds,ct);
        if(chunks.Count!=chunkIds.Count || chunks.Any(c=>!c.IsAvailable))
            throw new StudyValidationException("Usa soltanto chunk di fonti pronte e attive di questo progetto.");
        if(chunks.Sum(c=>c.Chunk.Text.Length)>MaxContextCharacters)
            throw new StudyValidationException("Il materiale selezionato supera 40.000 caratteri. Scegli meno chunk.");
        var provider=ai.Provider;
        if(string.IsNullOrWhiteSpace(provider.Key)) throw new AiStudyException("Provider non configurato.");
        var run=new FlashcardGenerationRun(Guid.NewGuid(),projectId,provider,count,clock.UtcNow,Array.AsReadOnly(chunkIds.ToArray()));
        AiFlashcardResult result;
        try { result=await ai.GenerateFlashCardsAsync(new(project,chunks,count,difficulty),ct); }
        catch(OperationCanceledException) { throw; }
        catch(AiStudyException) { throw; }
        catch(Exception) { throw new AiStudyException("La generazione non è riuscita. Puoi riprovare senza perdere le flashcard salvate."); }
        if(result.Drafts.Count is <1 || result.Drafts.Count>count) throw new AiStudyException("Il provider non ha restituito un numero valido di flashcard.");
        var allowed=chunks.ToDictionary(c=>c.Chunk.Id);
        var drafts=new List<ProposedFlashcard>();
        var unique=new HashSet<string>(StringComparer.Ordinal);
        foreach(var candidate in result.Drafts)
        {
            ct.ThrowIfCancellationRequested();
            if(candidate.References is null || candidate.References.Count==0 || candidate.References.Any(r=>!allowed.ContainsKey(r.ChunkId)))
                throw new AiStudyException("Il provider ha restituito riferimenti non presenti nel materiale selezionato.");
            var sourceIds=candidate.References.Select(r=>allowed[r.ChunkId].Chunk.SourceId).Distinct().ToArray();
            FlashcardDraft draft;
            try
            {
                draft=FlashcardService.Validate(new(candidate.Front,candidate.Back,candidate.Explanation,candidate.Difficulty ?? difficulty,
                    SourceId:sourceIds.Length==1 ? sourceIds[0] : null,References:candidate.References));
                if(draft.References!.Any(r=>!allowed[r.ChunkId].Chunk.Text.Contains(r.Quote,StringComparison.Ordinal)))
                    throw new StudyValidationException("Citazione non riscontrata.");
            }
            catch(StudyValidationException) { throw new AiStudyException("Il provider ha restituito contenuti o citazioni non validi. Riprova."); }
            if(!unique.Add(ProjectText.SearchKey(draft.Front+"\n"+draft.Back))) throw new AiStudyException("Il provider ha restituito flashcard duplicate.");
            drafts.Add(new(Guid.NewGuid(),draft));
        }
        var messages=result.Messages.ToList();
        if(drafts.Count<count) messages.Add($"Materiale sufficiente per {drafts.Count} schede su {count} richieste. Nessun contenuto aggiuntivo è stato inventato.");
        return new(run,drafts,messages);
    }
    public async Task<int> ImportAsync(FlashcardDraftBatch batch,IReadOnlyList<ProposedFlashcard> selected,CancellationToken ct=default)
    {
        if(selected.Count<1 || selected.Count>MaxCards || selected.Select(c=>c.Id).Distinct().Count()!=selected.Count)
            throw new StudyValidationException("Seleziona almeno una flashcard, senza duplicati.");
        var originals=batch.Drafts.ToDictionary(d=>d.Id);
        var cards=new List<FlashCard>();
        foreach(var chosen in selected)
        {
            if(!originals.TryGetValue(chosen.Id,out var original)) throw new StudyValidationException("La selezione non appartiene a questa anteprima.");
            var clean=FlashcardService.Validate(chosen.Draft);
            if(!clean.References!.Select(r=>(r.ChunkId,r.Quote)).OrderBy(r=>r.ChunkId).SequenceEqual(
                original.Draft.References!.Select(r=>(r.ChunkId,r.Quote)).OrderBy(r=>r.ChunkId)) || clean.SourceId!=original.Draft.SourceId)
                throw new StudyValidationException("I riferimenti dell'anteprima devono rimanere collegati ai brani originali.");
            // The original references were validated before preview. The transaction rechecks availability.
            var seed=new FlashCard { Id=chosen.Id,ProjectId=batch.Run.ProjectId,Front="",Back="",CreatedAtUtc=clock.UtcNow,
                UpdatedAtUtc=clock.UtcNow,GenerationRunId=batch.Run.Id,GenerationProviderKey=batch.Run.Provider.Key,IsMockGenerated=batch.Run.Provider.IsMock };
            var references=original.Draft.References!;
            var chunks=await context.GetChunksAsync(batch.Run.ProjectId,references.Select(r=>r.ChunkId).ToArray(),ct);
            if(chunks.Count!=references.Count) throw new StudyValidationException("Il materiale originale non è più disponibile.");
            var byId=chunks.ToDictionary(c=>c.Chunk.Id);
            var citations=references.Select(r=>new StudySpace.Domain.Sources.SourceCitation(byId[r.ChunkId].Chunk.SourceId,
                byId[r.ChunkId].Chunk.SourceRevisionId,r.ChunkId,r.Quote,byId[r.ChunkId].SourceTitle,FlashcardService.Locator(byId[r.ChunkId].Chunk))).ToArray();
            cards.Add(seed with { Front=clean.Front,Back=clean.Back,Explanation=clean.Explanation,Difficulty=clean.Difficulty,
                IsFavorite=clean.IsFavorite,Tags=clean.Tags!,SourceId=clean.SourceId,Citations=Array.AsReadOnly(citations),
                IsUserEdited=clean.Front!=original.Draft.Front || clean.Back!=original.Draft.Back || clean.Explanation!=original.Draft.Explanation || clean.Difficulty!=original.Draft.Difficulty });
        }
        var fingerprint=new StringBuilder();
        void Part(string? text) { text ??=""; fingerprint.Append(text.Length).Append(':').Append(text); }
        foreach(var card in cards.OrderBy(c=>c.Id))
        {
            Part(card.Id.ToString()); Part(card.Front); Part(card.Back); Part(card.Explanation); Part(card.Difficulty?.ToString());
            Part(card.IsFavorite.ToString()); Part(card.SourceId?.ToString()); Part(card.IsUserEdited.ToString());
            Part(card.Tags.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach(var tag in card.Tags.Order(StringComparer.Ordinal)) Part(tag);
            Part(card.Citations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach(var reference in card.Citations.OrderBy(c=>c.ChunkId)) { Part(reference.ChunkId.ToString()); Part(reference.Quote); }
        }
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
        await repository.SaveGeneratedAsync(batch.Run,hash,cards,ct);
        return cards.Count;
    }
}
