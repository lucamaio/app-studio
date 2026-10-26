using System.Text.RegularExpressions;
using StudySpace.Application.Flashcards;
namespace StudySpace.Infrastructure.Ai;

// Local, deterministic extraction for development. It does not perform semantic AI generation.
public sealed partial class MockAiStudyService:IAiStudyService
{
    public AiProviderInfo Provider { get; }=new("mock-extractive-v1","Dimostrazione locale",true);
    public Task<AiFlashcardResult> GenerateFlashCardsAsync(FlashcardGenerationRequest request,CancellationToken ct=default)=>Task.Run(()=>
    {
        var cards=new List<AiFlashcardDraft>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var material in request.Chunks)
        {
            foreach(var sentence in Regex.Split(material.Chunk.Text,@"(?<=[.!?])\s+|\n+",RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1)))
            {
                ct.ThrowIfCancellationRequested();
                for(var offset=0;offset<sentence.Length;)
                {
                    var end=Math.Min(offset+500,sentence.Length);
                    if(end<sentence.Length)
                    {
                        var space=sentence.LastIndexOf(' ',end-1,end-offset);
                        if(space>offset+100) end=space;
                        if(char.IsLowSurrogate(sentence[end])) end--;
                    }
                    var quote=sentence[offset..end].Trim(); offset=end;
                    if(quote.Length<25 || !seen.Add(StudySpace.Application.Projects.ProjectText.SearchKey(quote))) continue;
                    var pivot=Math.Min(quote.Length/2,160);
                    while(pivot>0 && !char.IsWhiteSpace(quote[pivot])) pivot--;
                    var front=pivot>10 ? $"Completa il brano da «{material.SourceTitle}»:\n{quote[..pivot]}…" :
                        $"Quale informazione riporta «{material.SourceTitle}» nel brano {material.Chunk.Ordinal+1}?";
                    cards.Add(new(front,quote,"Scheda dimostrativa estratta dal testo: controlla sempre il riferimento alla fonte.",
                        request.Difficulty,[new(material.Chunk.Id,quote)]));
                    if(cards.Count==request.Count) return new AiFlashcardResult(cards,[]);
                }
            }
        }
        if(cards.Count==0) throw new AiStudyException("Il materiale selezionato è troppo breve per creare schede dimostrative. Seleziona altri chunk.");
        return new AiFlashcardResult(cards,[]);
    },ct);
}
