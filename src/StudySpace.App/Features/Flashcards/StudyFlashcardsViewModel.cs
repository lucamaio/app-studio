using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Domain.Common;
namespace StudySpace.App.Features.Flashcards;

public sealed record CitationPresentation(string Title,string Quote);
public partial class StudyFlashcardsViewModel(FlashcardService flashcards,ProjectService projects,INavigationService navigation,
    ILogger<StudyFlashcardsViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId; private FlashcardQuery query=new(); private bool loaded,invalidQuery;
    private List<Guid> ids=[]; private int index;
    public ObservableCollection<CitationPresentation> Citations { get; }=[];
    [ObservableProperty] public partial string ProjectName { get; set; }="";
    [ObservableProperty] public partial string Front { get; set; }="";
    [ObservableProperty] public partial string Back { get; set; }="";
    [ObservableProperty] public partial string Explanation { get; set; }="";
    [ObservableProperty] public partial bool HasExplanation { get; set; }
    [ObservableProperty] public partial bool HasCard { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool CanPrevious { get; set; }
    [ObservableProperty] public partial bool CanNext { get; set; }
    [ObservableProperty] public partial string PositionLabel { get; set; }="";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AnswerButtonLabel))] public partial bool IsAnswerVisible { get; set; }
    public string AnswerButtonLabel=>IsAnswerVisible ? "Nascondi risposta" : "Mostra risposta";
    public void Configure(string project,string? search,string? tag,string? difficulty,string? favorite)
    {
        projectId=Guid.TryParse(project,out var p) ? p : Guid.Empty;
        Difficulty? value=null; invalidQuery=false;
        if(!string.IsNullOrWhiteSpace(difficulty))
        { if(int.TryParse(difficulty,out var n) && Enum.IsDefined((Difficulty)n)) value=(Difficulty)n; else invalidQuery=true; }
        if(favorite is not null && favorite is not "0" and not "1") invalidQuery=true;
        query=new(search ?? "",string.IsNullOrWhiteSpace(tag) ? null : tag,value,favorite=="1");
    }
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    {
        if(loaded) return;
        if(invalidQuery) throw new StudyValidationException("I filtri della sessione non sono validi. Avvia di nuovo lo studio dalle flashcard.");
        ProjectName=(await projects.GetAsync(projectId)).Name;
        ids=(await flashcards.StudyIdsAsync(projectId,query)).Distinct().ToList(); index=0;
        await ShowCurrentAsync(1); loaded=true;
    });
    private async Task ShowCurrentAsync(int direction)
    {
        IsAnswerVisible=false; HasCard=false;
        while(ids.Count>0)
        {
            index=Math.Clamp(index,0,ids.Count-1);
            try
            {
                var card=await flashcards.GetAsync(projectId,ids[index]);
                Front=card.Front; Back=card.Back; Explanation=card.Explanation ?? ""; HasExplanation=Explanation.Length>0;
                Citations.Clear(); foreach(var citation in card.Citations)
                    Citations.Add(new(citation.SourceTitle+(string.IsNullOrWhiteSpace(citation.Locator) ? "" : " · "+citation.Locator),citation.Quote));
                HasCard=true; IsEmpty=false; PositionLabel=$"Scheda {index+1} di {ids.Count}";
                CanPrevious=index>0; CanNext=index<ids.Count-1; return;
            }
            catch(FlashcardNotFoundException)
            { ids.RemoveAt(index); if(direction<0) index=Math.Max(0,index-1); }
        }
        Front=""; Back=""; Explanation=""; Citations.Clear(); IsEmpty=true; PositionLabel="Nessuna scheda disponibile"; CanPrevious=false; CanNext=false;
    }
    [RelayCommand] private void ToggleAnswer() { if(HasCard && !IsBusy) IsAnswerVisible=!IsAnswerVisible; }
    [RelayCommand] private Task PreviousAsync()=>RunAsync(async()=>{ if(CanPrevious) { index--; await ShowCurrentAsync(-1); } });
    [RelayCommand] private Task NextAsync()=>RunAsync(async()=>{ if(CanNext) { index++; await ShowCurrentAsync(1); } });
    [RelayCommand] private Task ShuffleAsync()=>RunAsync(async()=>
    {
        if(!HasCard) return;
        var before=ids.ToArray();
        for(var i=ids.Count-1;i>0;i--) { var j=Random.Shared.Next(i+1); (ids[i],ids[j])=(ids[j],ids[i]); }
        if(ids.Count>1 && ids.SequenceEqual(before)) { var first=ids[0]; ids.RemoveAt(0); ids.Add(first); }
        index=0; await ShowCurrentAsync(1);
    });
    [RelayCommand] private Task BackToCardsAsync()=>RunAsync(navigation.GoBackAsync);
}
