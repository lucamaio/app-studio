using StudySpace.Application.Sources;
using StudySpace.Infrastructure.Import;
namespace StudySpace.IntegrationTests;

public sealed class SourceChunkingTests
{
    private readonly SourceChunkingService chunker=new(new());
    [Fact] public void Chunks_cover_normalized_text_in_order_with_bounded_overlap_and_stable_offsets()
    {
        var document=chunker.Normalize(new([new(string.Join("\r\n\r\n",Enumerable.Range(0,75).Select(i=>$"Paragrafo {i}. "+new string('a',215))),1,"Introduzione")],"fixture"));
        var p=Guid.NewGuid(); var s=Guid.NewGuid(); var r=Guid.NewGuid(); var now=DateTimeOffset.UtcNow;
        var chunks=chunker.Chunk(p,s,r,document,now);
        Assert.True(chunks.Count>3); Assert.Equal(0,chunks[0].StartOffset); Assert.Equal(document.Text.Length,chunks[^1].EndOffset);
        for(var i=0;i<chunks.Count;i++)
        {
            var c=chunks[i]; Assert.Equal(i,c.Ordinal); Assert.Equal(p,c.ProjectId); Assert.Equal(s,c.SourceId);
            Assert.Equal(document.Text[c.StartOffset..c.EndOffset],c.Text);
            Assert.InRange(c.Text.Length,1000,4000); Assert.Equal(1,c.PageStart); Assert.Equal("Introduzione",c.SectionPath);
            if(i>0) { Assert.InRange(chunks[i-1].EndOffset-c.StartOffset,0,250); Assert.True(c.EndOffset>chunks[i-1].EndOffset); }
        }
        var repeated=chunker.Chunk(p,s,r,document,now);
        Assert.Equal(chunks.Select(c=>(c.Text,c.StartOffset,c.EndOffset)),repeated.Select(c=>(c.Text,c.StartOffset,c.EndOffset)));
    }
    [Fact] public void Short_text_stays_in_one_chunk_and_long_unbroken_text_is_bounded()
    {
        var shortDoc=chunker.Normalize(new([new("  Appunto breve\r\n  ")],"test"));
        Assert.Equal("Appunto breve",Assert.Single(chunker.Chunk(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),shortDoc,DateTimeOffset.UtcNow)).Text);
        var longDoc=chunker.Normalize(new([new(string.Concat(Enumerable.Repeat("😀",6500)))],"test"));
        var chunks=chunker.Chunk(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),longDoc,DateTimeOffset.UtcNow);
        Assert.All(chunks,c=> { Assert.True(c.Text.Length<=4000); Assert.False(char.IsLowSurrogate(c.Text[0])); Assert.False(char.IsHighSurrogate(c.Text[^1])); });
    }
    [Fact] public void Empty_or_excessive_normalized_text_is_rejected()
    {
        Assert.Throws<SourceImportException>(()=>chunker.Normalize(new([new("  \r\n ")],"test")));
        var limited=new SourceChunkingService(new() { MaxTextCharacters=10 });
        Assert.Throws<SourceImportException>(()=>limited.Normalize(new([new(new string('x',11))],"test")));
    }
    [Fact] public void Page_and_section_boundaries_survive_normalization_and_chunking()
    {
        var doc=chunker.Normalize(new([new("  Prima\r\nriga ",1,"A"),new("Seconda",2,"B")],"test"));
        Assert.Equal("Prima\nriga\n\nSeconda",doc.Text);
        var c=Assert.Single(chunker.Chunk(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),doc,DateTimeOffset.UtcNow));
        Assert.Equal(1,c.PageStart); Assert.Equal(2,c.PageEnd); Assert.Equal("A → B",c.SectionPath);
        Assert.All(doc.Locators,l=>Assert.False(string.IsNullOrWhiteSpace(doc.Text[l.StartOffset..l.EndOffset])));
    }
}
