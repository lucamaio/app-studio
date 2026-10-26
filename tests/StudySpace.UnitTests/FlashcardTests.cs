using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.App.Features.Flashcards;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Domain.Common;
using StudySpace.Domain.Flashcards;
using StudySpace.Domain.Projects;
namespace StudySpace.UnitTests;

public sealed class FlashcardTests
{
    private readonly TestProjectRepository projects=new(); private readonly TestFlashcardRepository repository=new();
    private readonly TestStudyContext context=new(); private readonly TestClock clock=new(); private readonly TestAiStudyService ai=new();
    private readonly TestNavigation navigation=new(); private readonly TestFlashcardDialogs dialogs=new();
    private readonly StudyProject project=new() { Name="Reti di Calcolatori" };
    public FlashcardTests()=>projects.Items.Add(project);
    private ProjectService Projects()=>new(projects,clock);
    private FlashcardService Cards()=>new(repository,context,Projects(),clock);
    private FlashcardGenerationService Generation()=>new(ai,context,repository,Projects(),clock);
    private StudyMaterialPickerViewModel Materials()=>new(Cards(),NullLogger<StudyMaterialPickerViewModel>.Instance);
    private StudyChunk Chunk(string text="TCP offre un trasporto affidabile fra due applicazioni.",Guid? owner=null,Guid? source=null,int ordinal=0)
    {
        var chunk=new StudyChunk(new() { ProjectId=owner ?? project.Id,SourceId=source ?? Guid.NewGuid(),SourceRevisionId=Guid.NewGuid(),
            Ordinal=ordinal,Text=text,StartOffset=0,EndOffset=text.Length },"Lezione",true);
        context.Chunks.Add(chunk); return chunk;
    }
    private FlashCard Card(string front="Che cosa offre TCP?",string back="Trasporto affidabile")
    { var c=new FlashCard { ProjectId=project.Id,Front=front,Back=back,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow }; repository.Items.Add(c); return c; }
    private StudyFlashcardsViewModel Study()=>new(Cards(),Projects(),navigation,NullLogger<StudyFlashcardsViewModel>.Instance);

    [Theory]
    [InlineData("","Risposta")]
    [InlineData("Domanda"," ")]
    public async Task Mandatory_fields_are_validated_before_saving(string front,string back)
    {
        await Assert.ThrowsAsync<StudyValidationException>(()=>Cards().CreateAsync(project.Id,new(front,back)));
        Assert.Equal(0,repository.Saves);
    }
    [Fact] public async Task Manual_card_normalizes_tags_and_preserves_creation_on_update()
    {
        var card=await Cards().CreateAsync(project.Id,new(" Domanda "," Risposta ",Tags:[" Reti ","reti","Esame"],Difficulty:Difficulty.Easy));
        Assert.Equal("Domanda",card.Front); Assert.Equal(new[] { "Reti","Esame" },card.Tags);
        clock.UtcNow=clock.UtcNow.AddHours(1);
        await Cards().UpdateAsync(project.Id,card.Id,1,new("Modificata","Nuova risposta"));
        var updated=await Cards().GetAsync(project.Id,card.Id);
        Assert.Equal(card.CreatedAtUtc,updated.CreatedAtUtc); Assert.Equal(clock.UtcNow,updated.UpdatedAtUtc); Assert.Equal(2,updated.Revision);
        await Assert.ThrowsAsync<FlashcardConflictException>(()=>Cards().UpdateAsync(project.Id,card.Id,1,new("Obsoleta","No")));
    }
    [Fact] public async Task Foreign_source_or_chunk_cannot_be_attached_to_a_manual_card()
    {
        var foreign=Chunk(owner:Guid.NewGuid());
        await Assert.ThrowsAsync<StudyValidationException>(()=>Cards().CreateAsync(project.Id,new("Domanda","Risposta",SourceId:foreign.Chunk.SourceId)));
        await Assert.ThrowsAsync<StudyValidationException>(()=>Cards().CreateAsync(project.Id,new("Domanda","Risposta",References:[new(foreign.Chunk.Id,foreign.Chunk.Text)])));
        Assert.Empty(repository.Items);
    }
    [Fact] public async Task Existing_citations_remain_valid_when_the_source_is_archived()
    {
        var chunk=Chunk();
        var card=await Cards().CreateAsync(project.Id,new("Domanda","Risposta",SourceId:chunk.Chunk.SourceId,References:[new(chunk.Chunk.Id,chunk.Chunk.Text)]));
        context.Chunks[0]=chunk with { IsAvailable=false };
        await Cards().UpdateAsync(project.Id,card.Id,1,new("Domanda modificata","Risposta",SourceId:chunk.Chunk.SourceId,References:[new(chunk.Chunk.Id,chunk.Chunk.Text)]));
        Assert.Single((await Cards().GetAsync(project.Id,card.Id)).Citations);
        await Assert.ThrowsAsync<StudyValidationException>(()=>Cards().CreateAsync(project.Id,new("Nuova","Risposta",References:[new(chunk.Chunk.Id,chunk.Chunk.Text)])));
    }
    [Fact] public async Task Generation_receives_only_the_selected_project_context_and_saves_nothing_before_import()
    {
        var chosen=Chunk(); Chunk(owner:Guid.NewGuid());
        var batch=await Generation().GenerateAsync(project.Id,[chosen.Chunk.Id],4,Difficulty.Hard);
        Assert.Equal(project.Id,ai.Request!.Project.Id); Assert.Equal(chosen.Chunk.Id,Assert.Single(ai.Request.Chunks).Chunk.Id);
        Assert.Equal(4,ai.Request.Count); Assert.Equal(Difficulty.Hard,ai.Request.Difficulty);
        Assert.Empty(repository.Items); Assert.Empty(repository.Runs); Assert.Single(batch.Drafts); Assert.NotEmpty(batch.Messages);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Invalid_generation_count_does_not_call_the_provider(int count)
    {
        var chunk=Chunk(); await Assert.ThrowsAsync<StudyValidationException>(()=>Generation().GenerateAsync(project.Id,[chunk.Chunk.Id],count,null));
        Assert.Equal(0,ai.Calls);
    }
    [Fact] public async Task Foreign_archived_or_excessive_context_is_rejected_before_provider_call()
    {
        var foreign=Chunk(owner:Guid.NewGuid());
        await Assert.ThrowsAsync<StudyValidationException>(()=>Generation().GenerateAsync(project.Id,[foreign.Chunk.Id],1,null));
        var old=Chunk(); context.Chunks[1]=old with { IsAvailable=false };
        await Assert.ThrowsAsync<StudyValidationException>(()=>Generation().GenerateAsync(project.Id,[old.Chunk.Id],1,null));
        var large=Enumerable.Range(0,11).Select(_=>Chunk(new string('x',4000)).Chunk.Id).ToArray();
        await Assert.ThrowsAsync<StudyValidationException>(()=>Generation().GenerateAsync(project.Id,large,1,null)); Assert.Equal(0,ai.Calls);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Provider_outputs_with_unknown_chunks_or_invented_quotes_are_rejected(bool unknownChunk)
    {
        var chunk=Chunk(); ai.Response=r=>new([new("Domanda","Risposta",null,null,
            [new(unknownChunk ? Guid.NewGuid() : chunk.Chunk.Id,unknownChunk ? chunk.Chunk.Text : "Citazione inventata")])],[]);
        await Assert.ThrowsAsync<AiStudyException>(()=>Generation().GenerateAsync(project.Id,[chunk.Chunk.Id],1,null)); Assert.Empty(repository.Items);
    }
    [Fact] public async Task Preview_import_keeps_citations_and_marks_content_edits()
    {
        var chunk=Chunk(); var batch=await Generation().GenerateAsync(project.Id,[chunk.Chunk.Id],1,Difficulty.Medium);
        var original=Assert.Single(batch.Drafts);
        var selected=original with { Draft=original.Draft with { Front="Domanda migliorata",Tags=["Esame"] } };
        await Generation().ImportAsync(batch,[selected]);
        var saved=Assert.Single(repository.Items);
        Assert.True(saved.IsMockGenerated); Assert.True(saved.IsUserEdited); Assert.Equal(batch.Run.Id,saved.GenerationRunId);
        Assert.Equal(chunk.Chunk.Id,Assert.Single(saved.Citations).ChunkId); Assert.Equal("Esame",Assert.Single(saved.Tags));
        await Generation().ImportAsync(batch,[selected]); Assert.Single(repository.Items);
        await Assert.ThrowsAsync<FlashcardConflictException>(()=>Generation().ImportAsync(batch,[original]));
    }
    [Fact] public async Task Preview_cannot_replace_the_validated_citations_or_import_an_empty_selection()
    {
        var chunk=Chunk(); var batch=await Generation().GenerateAsync(project.Id,[chunk.Chunk.Id],1,null);
        await Assert.ThrowsAsync<StudyValidationException>(()=>Generation().ImportAsync(batch,[]));
        var altered=batch.Drafts[0] with { Draft=batch.Drafts[0].Draft with { References=[] } };
        await Assert.ThrowsAsync<StudyValidationException>(()=>Generation().ImportAsync(batch,[altered])); Assert.Empty(repository.Items);
    }
    [Fact] public async Task Study_hides_answers_on_entry_navigation_and_shuffle_and_preserves_the_deck()
    {
        Card("Prima","Uno"); Card("Seconda","Due"); Card("Terza","Tre");
        var vm=Study(); vm.Configure(project.Id.ToString(),null,null,null,"0"); await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal("Prima",vm.Front); Assert.False(vm.IsAnswerVisible); Assert.False(vm.CanPrevious);
        vm.ToggleAnswerCommand.Execute(null); Assert.True(vm.IsAnswerVisible);
        await vm.NextCommand.ExecuteAsync(null); Assert.Equal("Seconda",vm.Front); Assert.False(vm.IsAnswerVisible);
        vm.ToggleAnswerCommand.Execute(null); await vm.PreviousCommand.ExecuteAsync(null); Assert.False(vm.IsAnswerVisible);
        vm.ToggleAnswerCommand.Execute(null); await vm.ShuffleCommand.ExecuteAsync(null); Assert.False(vm.IsAnswerVisible); Assert.False(vm.CanPrevious);
        var seen=new HashSet<string> { vm.Front }; while(vm.CanNext) { await vm.NextCommand.ExecuteAsync(null); seen.Add(vm.Front); }
        Assert.Equal(3,seen.Count); Assert.Contains("Prima",seen); Assert.Contains("Seconda",seen); Assert.Contains("Terza",seen);
    }
    [Fact] public async Task Study_skips_a_card_deleted_after_the_session_started()
    {
        Card("Prima","Uno"); var removed=Card("Eliminata","Due"); Card("Terza","Tre");
        var vm=Study(); vm.Configure(project.Id.ToString(),null,null,null,"0"); await vm.LoadCommand.ExecuteAsync(null);
        repository.Items.Remove(removed); await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal("Terza",vm.Front); Assert.Equal("Scheda 2 di 2",vm.PositionLabel); Assert.False(vm.CanNext);
    }
    [Fact] public async Task Empty_or_invalid_study_routes_do_not_reveal_any_content()
    {
        var empty=Study(); empty.Configure(project.Id.ToString(),null,null,null,"0"); await empty.LoadCommand.ExecuteAsync(null);
        Assert.True(empty.IsEmpty); empty.ToggleAnswerCommand.Execute(null); Assert.False(empty.IsAnswerVisible);
        Card(); var invalid=Study(); invalid.Configure(project.Id.ToString(),null,null,"99","0"); await invalid.LoadCommand.ExecuteAsync(null);
        Assert.True(invalid.HasError); Assert.False(invalid.HasCard); Assert.Empty(invalid.Back);
    }
    [Fact] public async Task List_requires_delete_confirmation_and_studies_the_applied_filters()
    {
        Card(); var preferred=Card("Reti","Preferita") with { IsFavorite=true,Tags=["Esame"],Difficulty=Difficulty.Easy }; repository.Items[1]=preferred;
        var vm=new FlashcardsViewModel(Cards(),Projects(),navigation,dialogs,NullLogger<FlashcardsViewModel>.Instance);
        vm.Configure(project.Id.ToString()); await vm.LoadCommand.ExecuteAsync(null);
        await vm.Items[0].DeleteCommand.ExecuteAsync(null); Assert.Equal(0,repository.Deletes); Assert.Equal(1,dialogs.Calls);
        vm.FavoritesOnly=true; vm.SelectedTag=new("Esame","Esame"); vm.SelectedDifficulty=DifficultyChoice.Filters[1]; await vm.SearchCommand.ExecuteAsync(null);
        Assert.Single(vm.Items); vm.SearchText="non ancora applicata"; await vm.StudyCommand.ExecuteAsync(null);
        Assert.Equal(new FlashcardQuery(Tag:"Esame",Difficulty:Difficulty.Easy,FavoritesOnly:true),navigation.StudyQuery);
        dialogs.Confirmed=true; await vm.Items[0].DeleteCommand.ExecuteAsync(null); Assert.True(vm.IsEmpty); Assert.Equal(1,repository.Deletes);
    }
    [Fact] public async Task Manual_editor_does_not_duplicate_a_commit_when_navigation_fails()
    {
        var vm=new EditFlashcardViewModel(Cards(),Projects(),Materials(),navigation,NullLogger<EditFlashcardViewModel>.Instance);
        vm.Configure(project.Id.ToString(),null); await vm.LoadCommand.ExecuteAsync(null); vm.Front="Domanda"; vm.Back="Risposta";
        navigation.FailBack=true; await vm.SaveCommand.ExecuteAsync(null); Assert.True(vm.IsSaved); Assert.True(vm.HasError);
        navigation.FailBack=false; await vm.SaveCommand.ExecuteAsync(null); Assert.Single(repository.Items); Assert.Equal(1,repository.Saves);
    }
    [Fact] public async Task Invalid_edit_id_cannot_fall_back_to_creating_a_new_card()
    {
        var vm=new EditFlashcardViewModel(Cards(),Projects(),Materials(),navigation,NullLogger<EditFlashcardViewModel>.Instance);
        vm.Configure(project.Id.ToString(),"invalid"); await vm.LoadCommand.ExecuteAsync(null); await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsReady); Assert.Empty(repository.Items);
    }
    [Fact] public async Task Material_picker_keeps_selected_references_while_browsing_bounded_pages()
    {
        var source=Guid.NewGuid(); for(var i=0;i<45;i++) Chunk($"Materiale della parte {i} abbastanza lungo per lo studio.",source:source,ordinal:i);
        var vm=Materials(); vm.Configure(project.Id); await vm.LoadCommand.ExecuteAsync(null); await vm.LoadChunksCommand.ExecuteAsync(null);
        Assert.Equal(20,vm.Chunks.Count); var selected=vm.Chunks[0].Context.Chunk.Id; vm.Chunks[0].IsSelected=true;
        await vm.MoreChunksCommand.ExecuteAsync(null); Assert.Equal(20,vm.Chunks.Count); Assert.Equal(selected,Assert.Single(vm.SelectedChunkIds));
        await vm.PreviousChunksCommand.ExecuteAsync(null); Assert.True(vm.Chunks[0].IsSelected);
        vm.ClearSelectionCommand.Execute(null); Assert.Empty(vm.SelectedChunkIds);
    }
    [Fact] public async Task Preview_supports_deselection_edit_removal_and_single_import_after_navigation_error()
    {
        var chunk=Chunk("Prima frase abbastanza lunga da citare. Seconda frase abbastanza lunga da citare. Terza frase abbastanza lunga da citare.");
        ai.Response=r=>new(Enumerable.Range(0,3).Select(i=>new AiFlashcardDraft($"Domanda {i}",$"Risposta {i}",null,null,[new(chunk.Chunk.Id,chunk.Chunk.Text)])).ToArray(),[]);
        var material=Materials(); var vm=new GenerateFlashcardsViewModel(Generation(),Projects(),material,navigation,NullLogger<GenerateFlashcardsViewModel>.Instance);
        vm.Configure(project.Id.ToString()); await vm.LoadCommand.ExecuteAsync(null); await material.LoadChunksCommand.ExecuteAsync(null);
        material.Chunks[0].IsSelected=true; vm.CountText="3"; await vm.GenerateCommand.ExecuteAsync(null);
        Assert.True(vm.IsPreview); Assert.Equal(3,vm.Preview.Count); Assert.Empty(repository.Items);
        vm.DeselectAllCommand.Execute(null); vm.Preview[0].IsSelected=true; vm.Preview[0].Front="Domanda modificata";
        vm.Preview[1].RemoveCommand.Execute(null); Assert.Equal(2,vm.Preview.Count);
        navigation.FailBack=true; await vm.ImportCommand.ExecuteAsync(null); Assert.True(vm.IsImported); Assert.True(vm.HasError);
        navigation.FailBack=false; await vm.ImportCommand.ExecuteAsync(null);
        Assert.Equal("Domanda modificata",Assert.Single(repository.Items).Front); Assert.Equal(1,repository.BatchSaves);
    }
}
