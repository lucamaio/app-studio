using CommunityToolkit.Mvvm.Input;
using StudySpace.Domain.Common;
using StudySpace.Domain.Flashcards;
namespace StudySpace.App.Features.Flashcards;

public sealed record DifficultyChoice(string Title,Difficulty? Value)
{
    public static IReadOnlyList<DifficultyChoice> All { get; }=[new("Non specificata",null),new("Facile",Difficulty.Easy),new("Media",Difficulty.Medium),new("Difficile",Difficulty.Hard)];
    public static IReadOnlyList<DifficultyChoice> Filters { get; }=[new("Tutte le difficoltà",null),..All.Skip(1)];
}
public sealed record FlashcardTagChoice(string Title,string? Value);
public sealed class FlashcardCardViewModel
{
    public FlashCard Card { get; }
    public string Front=>Card.Front;
    public string ShortFront=>Card.Front.Length>260 ? Card.Front[..260]+"…" : Card.Front;
    public string FavoriteSymbol=>Card.IsFavorite ? "★" : "☆";
    public string FavoriteLabel=>Card.IsFavorite ? "Rimuovi dai preferiti" : "Aggiungi ai preferiti";
    public string DifficultyLabel=>DifficultyChoice.All.FirstOrDefault(c=>c.Value==Card.Difficulty)?.Title ?? "Non specificata";
    public string OriginLabel=>(!Card.GenerationRunId.HasValue ? "Manuale" : Card.IsMockGenerated ? "Dimostrativa" : "Generata dall'AI")+(Card.IsUserEdited ? " · modificata" : "");
    public string Info=>$"{DifficultyLabel} · {OriginLabel}";
    public string DateLabel=>$"Aggiornata il {Card.UpdatedAtUtc.ToLocalTime():dd/MM/yyyy}";
    public string TagsLabel=>string.Join(" · ",Card.Tags);
    public bool HasTags=>Card.Tags.Count>0;
    public string ReferenceLabel=>Card.Citations.Count>0 ? $"{Card.Citations.Count} riferimenti alle fonti" : Card.SourceTitle is not null ?
        $"Fonte: {Card.SourceTitle}"+(Card.IsSourceArchived ? " (archiviata)" : "") : "";
    public bool HasReferences=>ReferenceLabel.Length>0;
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand FavoriteCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public FlashcardCardViewModel(FlashCard card,Func<Task> edit,Func<Task> favorite,Func<Task> delete)
    { Card=card; EditCommand=new AsyncRelayCommand(edit); FavoriteCommand=new AsyncRelayCommand(favorite); DeleteCommand=new AsyncRelayCommand(delete); }
}
