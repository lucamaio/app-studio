using StudySpace.Domain.Common;
using StudySpace.Domain.Flashcards;
using StudySpace.Domain.Projects;
using StudySpace.Domain.Sources;
namespace StudySpace.Application.Flashcards;

public sealed record FlashcardQuery(string Search="", string? Tag=null, Difficulty? Difficulty=null, bool FavoritesOnly=false);
public sealed record ChunkReferenceDraft(Guid ChunkId,string Quote);
public sealed record FlashcardDraft(string Front,string Back,string? Explanation=null,Difficulty? Difficulty=null,
    IReadOnlyList<string>? Tags=null,bool IsFavorite=false,Guid? SourceId=null,IReadOnlyList<ChunkReferenceDraft>? References=null);
public sealed record MaterialSource(Guid Id,string Title,int ChunkCount);
public sealed record StudyChunk(SourceChunk Chunk,string SourceTitle,bool IsAvailable);
public sealed record AiProviderInfo(string Key,string DisplayName,bool IsMock);
public sealed record FlashcardGenerationRequest(StudyProject Project,IReadOnlyList<StudyChunk> Chunks,int Count,Difficulty? Difficulty,string Language="it");
public sealed record AiFlashcardDraft(string Front,string Back,string? Explanation,Difficulty? Difficulty,IReadOnlyList<ChunkReferenceDraft> References);
public sealed record AiFlashcardResult(IReadOnlyList<AiFlashcardDraft> Drafts,IReadOnlyList<string> Messages);
public sealed record ProposedFlashcard(Guid Id,FlashcardDraft Draft);
public sealed record FlashcardGenerationRun(Guid Id,Guid ProjectId,AiProviderInfo Provider,int RequestedCount,
    DateTimeOffset CreatedAtUtc,IReadOnlyList<Guid> InputChunkIds,string PromptVersion="flashcards-v1");

// Only the application service creates batches; the editable preview never mutates their originals.
public sealed class FlashcardDraftBatch
{
    public FlashcardGenerationRun Run { get; }
    public IReadOnlyList<ProposedFlashcard> Drafts { get; }
    public IReadOnlyList<string> Messages { get; }
    internal FlashcardDraftBatch(FlashcardGenerationRun run,IEnumerable<ProposedFlashcard> drafts,IEnumerable<string> messages)
    { Run=run; Drafts=Array.AsReadOnly(drafts.ToArray()); Messages=Array.AsReadOnly(messages.ToArray()); }
}
public interface IAiStudyService
{
    AiProviderInfo Provider { get; }
    AiProviderInfo EvaluationProvider => Provider;
    Task<AiFlashcardResult> GenerateFlashCardsAsync(FlashcardGenerationRequest request,CancellationToken ct=default);
    Task<StudySpace.Application.Quizzes.AiQuizDraft> GenerateQuizAsync(StudySpace.Application.Quizzes.QuizGenerationRequest request,CancellationToken ct=default)
        => throw new AiStudyException("Questo provider non supporta ancora i quiz.");
    Task<StudySpace.Application.Quizzes.OpenAnswerEvaluation> EvaluateOpenAnswerAsync(StudySpace.Application.Quizzes.OpenAnswerEvaluationRequest request,CancellationToken ct=default)
        => throw new AiStudyException("Questo provider non supporta ancora la valutazione delle risposte aperte.");
}
public interface IFlashcardRepository
{
    Task<IReadOnlyList<FlashCard>> SearchAsync(Guid projectId,FlashcardQuery query,int offset,int limit,CancellationToken ct=default);
    Task<IReadOnlyList<Guid>> ListStudyIdsAsync(Guid projectId,FlashcardQuery query,CancellationToken ct=default);
    Task<IReadOnlyList<string>> ListTagsAsync(Guid projectId,CancellationToken ct=default);
    Task<FlashCard?> GetAsync(Guid projectId,Guid id,CancellationToken ct=default);
    Task SaveAsync(FlashCard card,int? expectedRevision,CancellationToken ct=default);
    Task SetFavoriteAsync(Guid projectId,Guid id,int expectedRevision,bool favorite,DateTimeOffset now,CancellationToken ct=default);
    Task DeleteAsync(Guid projectId,Guid id,int expectedRevision,CancellationToken ct=default);
    Task SaveGeneratedAsync(FlashcardGenerationRun run,string selectionHash,IReadOnlyList<FlashCard> cards,CancellationToken ct=default);
}
public interface IStudyContextRepository
{
    Task<IReadOnlyList<MaterialSource>> ListReadySourcesAsync(Guid projectId,int offset,int limit,CancellationToken ct=default);
    Task<IReadOnlyList<StudyChunk>> ListChunksAsync(Guid projectId,Guid sourceId,int offset,int limit,CancellationToken ct=default);
    Task<IReadOnlyList<StudyChunk>> GetChunksAsync(Guid projectId,IReadOnlyList<Guid> ids,CancellationToken ct=default);
    Task<MaterialSource?> GetSourceAsync(Guid projectId,Guid sourceId,bool includeArchived,CancellationToken ct=default);
}
public sealed class FlashcardNotFoundException():Exception("Questa flashcard non è più disponibile nel progetto.");
public sealed class FlashcardConflictException():Exception("Le flashcard sono state modificate o l'anteprima è già stata importata. Ricarica prima di riprovare.");
public sealed class AiStudyException(string message):Exception(message);

