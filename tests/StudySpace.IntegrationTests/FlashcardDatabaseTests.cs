using Microsoft.Extensions.Logging.Abstractions;
using StudySpace.Application.Abstractions;
using StudySpace.Application.Common;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Application.Sources;
using StudySpace.Domain.Common;
using StudySpace.Domain.Flashcards;
using StudySpace.Domain.Projects;
using StudySpace.Infrastructure.Ai;
using StudySpace.Infrastructure.Files;
using StudySpace.Infrastructure.Import;
using StudySpace.Infrastructure.Persistence;
namespace StudySpace.IntegrationTests;

public sealed class FlashcardDatabaseTests:IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"StudySpace.FlashcardTests",Guid.NewGuid().ToString("N"));
    private readonly Clock clock=new();
    private DatabaseOptions Options=>new(Path.Combine(directory,"test.db3"));
    private StudyDatabase Database()=>new(Options,NullLogger<StudyDatabase>.Instance);
    private ProjectRepository ProjectRepository()=>new(Database());
    private ProjectService Projects()=>new(ProjectRepository(),clock);
    private FlashcardRepository Repository()=>new(Database());
    private StudyContextRepository Context()=>new(Database());
    private FlashcardService Cards()=>new(Repository(),Context(),Projects(),clock);
    private FlashcardGenerationService Generation()=>new(new MockAiStudyService(),Context(),Repository(),Projects(),clock);
    private async Task<StudyProject> ProjectAsync()=>await Projects().CreateAsync(new ProjectDraft("Reti"));
    private async Task<(SourceSummary Summary,IReadOnlyList<StudyChunk> Chunks)> SourceAsync(Guid projectId,string? text=null)
    {
        var options=new SourceImportOptions();
        var importer=new SourceImportService(new SourceRepository(Database()),new SourceFileStore(Options,options,NullLogger<SourceFileStore>.Instance),
            [],new SourceChunkingService(options),Projects(),clock,options);
        var source=await importer.ImportTextAsync(projectId,"Protocolli",text ?? "TCP offre trasporto affidabile fra applicazioni. UDP invia datagrammi senza garantire la consegna. IP instrada i pacchetti nella rete.");
        return (source.Summary,await Context().ListChunksAsync(projectId,source.Summary.Source.Id,0,20));
    }
    [Fact] public async Task Manual_card_all_metadata_tags_and_citations_survive_reopening_and_editing()
    {
        var p=await ProjectAsync(); var source=await SourceAsync(p.Id); var chunk=source.Chunks[0].Chunk;
        var created=await Cards().CreateAsync(p.Id,new("  Che cosa offre TCP?  "," Trasporto affidabile ","Orientato alla connessione",Difficulty.Medium,
            ["Reti"," Esame ","reti"],true,source.Summary.Source.Id,[new(chunk.Id,"TCP offre trasporto affidabile fra applicazioni.")]));
        var loaded=(await Repository().GetAsync(p.Id,created.Id))!;
        Assert.Equal("Che cosa offre TCP?",loaded.Front); Assert.Equal("Trasporto affidabile",loaded.Back); Assert.Equal("Orientato alla connessione",loaded.Explanation);
        Assert.True(loaded.IsFavorite); Assert.Equal(Difficulty.Medium,loaded.Difficulty); Assert.Equal(source.Summary.Source.Id,loaded.SourceId);
        Assert.Equal("Protocolli",loaded.SourceTitle); Assert.Equal(new[] { "Esame","Reti" },loaded.Tags); Assert.Equal(2,loaded.TagIds.Count);
        var reference=Assert.Single(loaded.Citations); Assert.Equal(chunk.Id,reference.ChunkId); Assert.Equal(source.Summary.Revision.Id,reference.SourceRevisionId);
        Assert.Equal("TCP offre trasporto affidabile fra applicazioni.",reference.Quote); Assert.Null(loaded.GenerationRunId);
        clock.UtcNow=clock.UtcNow.AddHours(2);
        await Cards().UpdateAsync(p.Id,loaded.Id,loaded.Revision,new("TCP?","Affidabile",Tags:["Solo"],References:[new(chunk.Id,reference.Quote)]));
        var updated=await Cards().GetAsync(p.Id,loaded.Id);
        Assert.Equal(loaded.CreatedAtUtc,updated.CreatedAtUtc); Assert.Equal(clock.UtcNow,updated.UpdatedAtUtc); Assert.Equal(2,updated.Revision);
        Assert.Null(updated.SourceId); Assert.Equal("Solo",Assert.Single(updated.Tags)); Assert.Single(updated.Citations);
        Assert.Equal(1,(await ProjectRepository().GetSummaryAsync(p.Id))!.FlashcardCount);
    }
    [Fact] public async Task Search_combines_literal_unicode_text_tags_difficulty_and_favorites_without_cross_project_results()
    {
        var p=await ProjectAsync(); var other=await ProjectAsync();
        var expected=await Cards().CreateAsync(p.Id,new("Perché c'è il 100%_?","Risposta utile",Difficulty:Difficulty.Easy,Tags:["Esame"],IsFavorite:true));
        await Cards().CreateAsync(p.Id,new("Perche c'e il 100%_?","Risposta diversa",Difficulty:Difficulty.Hard,Tags:["Esame"]));
        await Cards().CreateAsync(other.Id,new("Perché c'è il 100%_?","Altra materia",Difficulty:Difficulty.Easy,Tags:["Esame"],IsFavorite:true));
        var query=new FlashcardQuery("PERCHE C'E IL 100%_", "esame",Difficulty.Easy,true);
        Assert.Equal(expected.Id,Assert.Single(await Cards().SearchAsync(p.Id,query)).Id);
        Assert.Equal(expected.Id,Assert.Single(await Cards().StudyIdsAsync(p.Id,query)));
        Assert.Equal("Esame",Assert.Single(await Cards().TagsAsync(p.Id)));
        Assert.Empty(await Cards().SearchAsync(p.Id,new("' OR 1=1 --")));
    }
    [Fact] public async Task Card_paging_and_study_id_order_are_stable_when_dates_match()
    {
        var p=await ProjectAsync();
        for(var i=0;i<37;i++) await Cards().CreateAsync(p.Id,new($"Domanda {i}",$"Risposta {i}"));
        var first=await Repository().SearchAsync(p.Id,new(),0,30); var second=await Repository().SearchAsync(p.Id,new(),30,30);
        Assert.Equal(30,first.Count); Assert.Equal(7,second.Count);
        Assert.Equal(37,first.Concat(second).Select(c=>c.Id).Distinct().Count());
        Assert.Equal(first.Concat(second).Select(c=>c.Id),await Repository().ListStudyIdsAsync(p.Id,new()));
    }
    [Fact] public async Task Stale_mutations_are_rejected_and_favorite_does_not_rewrite_content()
    {
        var p=await ProjectAsync(); var c=await Cards().CreateAsync(p.Id,new("Domanda","Risposta",Tags:["Esame"]));
        await Cards().FavoriteAsync(c);
        await Assert.ThrowsAsync<FlashcardConflictException>(()=>Cards().UpdateAsync(p.Id,c.Id,1,new("Obsoleta","No")));
        await Assert.ThrowsAsync<FlashcardConflictException>(()=>Cards().DeleteAsync(c));
        await Assert.ThrowsAsync<FlashcardConflictException>(()=>Cards().FavoriteAsync(c));
        var current=await Cards().GetAsync(p.Id,c.Id); Assert.True(current.IsFavorite); Assert.Equal("Risposta",current.Back); Assert.Equal("Esame",Assert.Single(current.Tags));
    }
    [Fact] public async Task Foreign_ownership_is_enforced_by_queries_services_and_database_constraints()
    {
        var p=await ProjectAsync(); var other=await ProjectAsync(); var foreign=await SourceAsync(other.Id); var chunk=foreign.Chunks[0].Chunk;
        var card=await Cards().CreateAsync(p.Id,new("Domanda","Risposta"));
        Assert.Null(await Repository().GetAsync(other.Id,card.Id)); Assert.Empty(await Context().GetChunksAsync(p.Id,[chunk.Id]));
        await Assert.ThrowsAsync<StudyValidationException>(()=>Cards().CreateAsync(p.Id,new("Domanda","Risposta",SourceId:foreign.Summary.Source.Id)));
        await Assert.ThrowsAsync<StorageUnavailableException>(()=>Database().ExecuteAsync(db=>db.Execute("UPDATE FlashcardDetails SET SourceId=? WHERE ProjectId=? AND FlashcardId=?",foreign.Summary.Source.Id.ToString(),p.Id.ToString(),card.Id.ToString())));
        await Assert.ThrowsAsync<StorageUnavailableException>(()=>Database().ExecuteAsync(db=>db.Execute("INSERT INTO FlashcardCitation(ProjectId,FlashcardId,ChunkId,Quote) VALUES (?,?,?,?)",p.Id.ToString(),card.Id.ToString(),chunk.Id.ToString(),chunk.Text)));
        await Assert.ThrowsAsync<FlashcardConflictException>(()=>Repository().DeleteAsync(other.Id,card.Id,1));
    }
    [Fact] public async Task Tags_reuse_the_project_vocabulary_but_do_not_link_different_projects()
    {
        var p=await Projects().CreateAsync(new ProjectDraft("Reti",Tags:["Esame"])); var other=await ProjectAsync();
        var first=await Cards().CreateAsync(p.Id,new("A","B",Tags:["esame"])); var second=await Cards().CreateAsync(p.Id,new("C","D",Tags:["Esame"]));
        var third=await Cards().CreateAsync(other.Id,new("E","F",Tags:["Esame"]));
        var a=await Cards().GetAsync(p.Id,first.Id); var b=await Cards().GetAsync(p.Id,second.Id); var c=await Cards().GetAsync(other.Id,third.Id);
        Assert.Equal(Assert.Single(a.TagIds),Assert.Single(b.TagIds)); Assert.NotEqual(Assert.Single(a.TagIds),Assert.Single(c.TagIds));
        await Cards().DeleteAsync(a); Assert.Single((await Cards().GetAsync(p.Id,b.Id)).Tags);
        await Cards().DeleteAsync(b); Assert.Empty(await Cards().TagsAsync(p.Id)); Assert.Single((await Projects().GetAsync(p.Id)).Tags);
    }
    [Fact] public async Task Mock_generation_is_deterministic_grounded_and_honest_about_insufficient_material()
    {
        var p=await ProjectAsync(); var source=await SourceAsync(p.Id); var ids=source.Chunks.Select(c=>c.Chunk.Id).ToArray();
        var first=await Generation().GenerateAsync(p.Id,ids,20,Difficulty.Hard); var second=await Generation().GenerateAsync(p.Id,ids,20,Difficulty.Hard);
        Assert.True(first.Run.Provider.IsMock); Assert.InRange(first.Drafts.Count,1,19); Assert.NotEmpty(first.Messages);
        Assert.Equal(first.Drafts.Select(d=>(d.Draft.Front,d.Draft.Back)),second.Drafts.Select(d=>(d.Draft.Front,d.Draft.Back)));
        Assert.All(first.Drafts,d=>{ Assert.Equal(Difficulty.Hard,d.Draft.Difficulty); Assert.Contains(d.Draft.References![0].Quote,source.Chunks[0].Chunk.Text); });
        Assert.Empty(await Cards().SearchAsync(p.Id,new())); Assert.Equal(0,await Database().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM GenerationRun")));
    }
    [Fact] public async Task Preview_import_saves_only_selected_edits_and_persists_provenance_idempotently()
    {
        var p=await ProjectAsync(); var source=await SourceAsync(p.Id);
        var batch=await Generation().GenerateAsync(p.Id,[source.Chunks[0].Chunk.Id],3,null); Assert.Equal(3,batch.Drafts.Count);
        var chosen=batch.Drafts[1] with { Draft=batch.Drafts[1].Draft with { Front="Domanda rivista dall'utente",Tags=["Ripasso"] } };
        Assert.Equal(1,await Generation().ImportAsync(batch,[chosen]));
        var saved=Assert.Single(await Cards().SearchAsync(p.Id,new()));
        Assert.Equal(chosen.Id,saved.Id); Assert.True(saved.IsUserEdited); Assert.True(saved.IsMockGenerated); Assert.Equal(batch.Run.Id,saved.GenerationRunId);
        Assert.Equal("mock-extractive-v1",saved.GenerationProviderKey); Assert.Equal("Ripasso",Assert.Single(saved.Tags)); Assert.Single(saved.Citations);
        await Generation().ImportAsync(batch,[chosen]); Assert.Single(await Cards().SearchAsync(p.Id,new()));
        Assert.Equal(1,await Database().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM GenerationRun")));
        Assert.Equal(1,await Database().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM GenerationInputChunk")));
        await Assert.ThrowsAsync<FlashcardConflictException>(()=>Generation().ImportAsync(batch,batch.Drafts));
    }
    [Fact] public async Task Source_archival_keeps_existing_cards_citable_but_prevents_new_generation_import()
    {
        var p=await ProjectAsync(); var source=await SourceAsync(p.Id);
        var first=await Generation().GenerateAsync(p.Id,[source.Chunks[0].Chunk.Id],1,null); await Generation().ImportAsync(first,first.Drafts);
        var pending=await Generation().GenerateAsync(p.Id,[source.Chunks[0].Chunk.Id],1,null);
        await new SourceRepository(Database()).ArchiveAsync(p.Id,source.Summary.Source.Id,1,clock.UtcNow);
        Assert.Empty(await Context().ListReadySourcesAsync(p.Id,0,20));
        await Assert.ThrowsAsync<StudyValidationException>(()=>Generation().ImportAsync(pending,pending.Drafts));
        await Generation().ImportAsync(first,first.Drafts); // An acknowledged retry does not need active sources again.
        var card=Assert.Single(await Cards().SearchAsync(p.Id,new())); Assert.True(card.IsSourceArchived); Assert.Single(card.Citations);
        await Cards().UpdateAsync(p.Id,card.Id,card.Revision,new("Ripasso modificato",card.Back,SourceId:card.SourceId,References:card.Citations.Select(c=>new ChunkReferenceDraft(c.ChunkId,c.Quote)).ToArray()));
        var edited=await Cards().GetAsync(p.Id,card.Id); Assert.True(edited.IsUserEdited); Assert.True(edited.IsSourceArchived); Assert.Single(edited.Citations);
    }
    [Fact] public async Task Batch_write_rolls_back_everything_when_one_card_cannot_be_saved()
    {
        var p=await ProjectAsync(); var source=await SourceAsync(p.Id); var chunk=source.Chunks[0].Chunk;
        var run=new FlashcardGenerationRun(Guid.NewGuid(),p.Id,new("mock","Demo",true),2,clock.UtcNow,[chunk.Id]);
        var citation=new StudySpace.Domain.Sources.SourceCitation(chunk.SourceId,chunk.SourceRevisionId,chunk.Id,chunk.Text,"Protocolli",null);
        var valid=new FlashCard { ProjectId=p.Id,Front="A",Back="B",GenerationRunId=run.Id,Citations=[citation] };
        var duplicate=valid with { Front="Altra scheda" };
        await Assert.ThrowsAsync<StorageUnavailableException>(()=>Repository().SaveGeneratedAsync(run,"test",[valid,duplicate]));
        Assert.Empty(await Cards().SearchAsync(p.Id,new()));
        Assert.Equal(0,await Database().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM GenerationRun")));
        Assert.Equal(0,await Database().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT COUNT(*) FROM FlashcardCitation")));
    }
    [Fact] public async Task Deleting_a_generated_card_does_not_resurrect_it_on_batch_retry_and_project_delete_cascades_provenance()
    {
        var p=await ProjectAsync(); var other=await ProjectAsync(); var source=await SourceAsync(p.Id);
        var batch=await Generation().GenerateAsync(p.Id,[source.Chunks[0].Chunk.Id],2,null); await Generation().ImportAsync(batch,batch.Drafts);
        var all=await Cards().SearchAsync(p.Id,new()); await Cards().DeleteAsync(all[0]);
        await Generation().ImportAsync(batch,batch.Drafts); Assert.Single(await Cards().SearchAsync(p.Id,new()));
        await Cards().CreateAsync(other.Id,new("Conservata","Risposta"));
        await Projects().DeleteAsync(p);
        foreach(var table in new[] { "FlashCard","FlashcardDetails","FlashcardCitation","FlashcardTag","GenerationRun","GenerationInputChunk" })
            Assert.Equal(0,await Database().ExecuteAsync(db=>db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table} WHERE ProjectId=?",p.Id.ToString())));
        Assert.Single(await Cards().SearchAsync(other.Id,new()));
    }
    [Fact] public async Task Migration_from_version_three_preserves_existing_cards_tags_and_citations_and_backfills_search()
    {
        Directory.CreateDirectory(directory); var p=Guid.NewGuid().ToString(); var card=Guid.NewGuid().ToString(); var now=clock.UtcNow.ToString("O");
        using(var db=new SQLite.SQLiteConnection(Options.DatabasePath))
        {
            db.Execute("PRAGMA foreign_keys=ON");
            foreach(var file in new[] { "001_Initial.sql","002_ProjectSearch.sql","003_SourceImport.sql" })
            {
                using var stream=typeof(StudyDatabase).Assembly.GetManifestResourceStream("StudySpace.Infrastructure.Persistence.Migrations."+file)!;
                using var reader=new StreamReader(stream);
                foreach(var sql in reader.ReadToEnd().Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)) db.Execute(sql);
            }
            db.Execute("CREATE TABLE SchemaMigration(Version INTEGER PRIMARY KEY,AppliedAtUtc TEXT NOT NULL)");
            for(var i=1;i<=3;i++) db.Execute("INSERT INTO SchemaMigration VALUES (?,?)",i,now);
            db.Execute("INSERT INTO StudyProject(Id,Name,Color,IconKey,CreatedAtUtc,UpdatedAtUtc) VALUES (?,'Reti','#5B63D3','book',?,?)",p,now,now);
            db.Execute("INSERT INTO FlashCard(Id,ProjectId,Front,Back,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,'Perché?','Risposta',?,?)",card,p,now,now);
            var tag=Guid.NewGuid().ToString();
            db.Execute("INSERT INTO Tag(Id,ProjectId,Name,NormalizedName,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,'Esame','ESAME',?,?)",tag,p,now,now);
            db.Execute("INSERT INTO FlashcardTag VALUES (?,?,?)",p,card,tag);
            var source=Guid.NewGuid().ToString(); var revision=Guid.NewGuid().ToString(); var chunk=Guid.NewGuid().ToString();
            db.Execute("INSERT INTO StudySource(Id,ProjectId,Title,Kind,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,'Appunti precedenti',0,?,?)",source,p,now,now);
            db.Execute("INSERT INTO SourceRevision(Id,ProjectId,SourceId,Number,Status,ExtractedText,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,1,2,'Citazione legacy',?,?)",revision,p,source,now,now);
            db.Execute("UPDATE StudySource SET CurrentRevisionId=? WHERE Id=?",revision,source);
            db.Execute("INSERT INTO SourceChunk(Id,ProjectId,SourceRevisionId,Ordinal,Text,StartOffset,EndOffset,CreatedAtUtc,UpdatedAtUtc) VALUES (?,?,?,0,'Citazione legacy',0,16,?,?)",chunk,p,revision,now,now);
            db.Execute("INSERT INTO FlashcardCitation VALUES (?,?,?)",p,card,chunk);
        }
        var loaded=Assert.Single(await Cards().SearchAsync(Guid.Parse(p),new("PERCHE", "Esame")));
        Assert.Equal(Guid.Parse(card),loaded.Id); Assert.Equal("Risposta",loaded.Back); Assert.Null(loaded.GenerationRunId); Assert.Null(loaded.SourceId);
        Assert.Equal("Citazione legacy",Assert.Single(loaded.Citations).Quote); Assert.Equal("Appunti precedenti",loaded.Citations[0].SourceTitle);
        Assert.Equal(6,await Database().ExecuteAsync(db=>db.ExecuteScalar<int>("SELECT MAX(Version) FROM SchemaMigration")));
    }
    private sealed class Clock:IClock { public DateTimeOffset UtcNow { get; set; }=new(2026,10,25,10,0,0,TimeSpan.Zero); }
    public void Dispose() { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
}

