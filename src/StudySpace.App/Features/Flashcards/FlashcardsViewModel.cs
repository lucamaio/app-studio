using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudySpace.App.Common;
using StudySpace.App.Navigation;
using StudySpace.App.Services;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Domain.Flashcards;
namespace StudySpace.App.Features.Flashcards;

public partial class FlashcardsViewModel(FlashcardService flashcards,ProjectService projects,INavigationService navigation,
    IFlashcardDialogs dialogs,ILogger<FlashcardsViewModel> logger):ViewModelBase(logger)
{
    private Guid projectId;
    private FlashcardQuery activeQuery=new();
    public ObservableCollection<FlashcardCardViewModel> Items { get; }=[];
    public ObservableCollection<FlashcardTagChoice> Tags { get; }=[];
    public IReadOnlyList<DifficultyChoice> Difficulties=>DifficultyChoice.Filters;
    [ObservableProperty] public partial string ProjectName { get; set; }="";
    [ObservableProperty] public partial string SearchText { get; set; }="";
    [ObservableProperty] public partial FlashcardTagChoice? SelectedTag { get; set; }
    [ObservableProperty] public partial DifficultyChoice? SelectedDifficulty { get; set; }=DifficultyChoice.Filters[0];
    [ObservableProperty] public partial bool FavoritesOnly { get; set; }
    [ObservableProperty] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool HasCards { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty] public partial string EmptyMessage { get; set; }="";
    public void Configure(string rawId)=>projectId=Guid.TryParse(rawId,out var id) ? id : Guid.Empty;
    [RelayCommand] private Task LoadAsync()=>RunAsync(async()=>
    { IsReady=false; IsEmpty=false; ProjectName=(await projects.GetAsync(projectId)).Name; await ReloadAsync(); IsReady=true; });
    [RelayCommand] private Task SearchAsync()=>RunAsync(async()=>
    {
        activeQuery=FlashcardService.ValidateQuery(new(SearchText,SelectedTag?.Value,SelectedDifficulty?.Value,FavoritesOnly));
        await ReloadAsync();
    });
    [RelayCommand] private Task ResetAsync()=>RunAsync(async()=>
    { SearchText=""; SelectedTag=Tags.FirstOrDefault(); SelectedDifficulty=Difficulties[0]; FavoritesOnly=false; activeQuery=new(); await ReloadAsync(); });
    private async Task ReloadAsync()
    {
        var page=await flashcards.SearchAsync(projectId,activeQuery);
        var tags=await flashcards.TagsAsync(projectId);
        Items.Clear(); foreach(var card in page) Items.Add(ToCard(card));
        Tags.Clear(); Tags.Add(new("Tutte le etichette",null)); foreach(var tag in tags) Tags.Add(new(tag,tag));
        if(activeQuery.Tag is not null && !Tags.Any(t=>t.Value==activeQuery.Tag)) Tags.Add(new(activeQuery.Tag,activeQuery.Tag));
        SelectedTag=Tags.FirstOrDefault(t=>t.Value==activeQuery.Tag) ?? Tags[0];
        HasMore=page.Count==FlashcardService.PageSize; HasCards=Items.Count>0; IsEmpty=!HasCards;
        EmptyMessage=activeQuery==new FlashcardQuery() ? "Crea la prima scheda oppure prepara un'anteprima dimostrativa dalle fonti del progetto." :
            "Nessuna scheda corrisponde ai filtri. Prova un'altra ricerca oppure azzera i filtri.";
    }
    private FlashcardCardViewModel ToCard(FlashCard card)=>new(card,
        ()=>RunAsync(()=>navigation.EditFlashcardAsync(projectId,card.Id)),
        ()=>RunAsync(async()=>{ await flashcards.FavoriteAsync(card); await ReloadAsync(); }),
        ()=>RunAsync(async()=>{ if(!await dialogs.ConfirmDeleteAsync(card.Front)) return; await flashcards.DeleteAsync(card); await ReloadAsync(); }));
    [RelayCommand] private Task LoadMoreAsync()=>RunAsync(async()=>
    {
        var page=await flashcards.SearchAsync(projectId,activeQuery,Items.Count);
        foreach(var card in page) Items.Add(ToCard(card)); HasMore=page.Count==FlashcardService.PageSize;
    });
    [RelayCommand] private Task CreateAsync()=>RunAsync(async()=>{ if(IsReady) await navigation.EditFlashcardAsync(projectId,null); });
    [RelayCommand] private Task GenerateAsync()=>RunAsync(async()=>{ if(IsReady) await navigation.GenerateFlashcardsAsync(projectId); });
    [RelayCommand] private Task StudyAsync()=>RunAsync(async()=>{ if(IsReady && HasCards) await navigation.StudyFlashcardsAsync(projectId,activeQuery); });
}
