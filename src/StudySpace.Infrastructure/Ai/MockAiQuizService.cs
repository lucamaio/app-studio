using StudySpace.Application.Flashcards;
using StudySpace.Application.Projects;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
namespace StudySpace.Infrastructure.Ai;

public sealed partial class MockAiStudyService
{
    public async Task<AiQuizDraft> GenerateQuizAsync(QuizGenerationRequest request,CancellationToken ct=default)
    {
        // Reuse bounded extractive passages; this intentionally does not simulate semantic knowledge.
        var needed=request.Configuration.Mode==QuizMode.Open?request.Configuration.Count:Math.Max(2,request.Configuration.Count);
        var passages=await GenerateFlashCardsAsync(new(request.Project,request.Chunks,needed,null),ct);
        if(passages.Drafts.Count<needed)
            throw new AiStudyException("Materiale insufficiente per il numero richiesto di domande dimostrative. Aggiungi brani distinti o riduci il numero.");
        var questions=new List<AiQuizQuestion>();
        for(var i=0;i<request.Configuration.Count;i++)
        {
            ct.ThrowIfCancellationRequested();
            var passage=passages.Drafts[i]; var quote=passage.Back;
            var pivot=Math.Min(quote.Length/2,110);
            while(pivot>0&&!char.IsWhiteSpace(quote[pivot]))pivot--;
            if(pivot<5)pivot=0;
            var prefix=quote[..pivot].Trim(); var answer=quote[pivot..].Trim();
            var isClosed=request.Configuration.Mode==QuizMode.Closed||request.Configuration.Mode==QuizMode.Mixed&&i%2==0;
            var options=new List<AiQuizOption>();
            if(isClosed)
            {
                options.Add(new(answer,true));
                foreach(var other in passages.Drafts.Where((_,n)=>n!=i))
                {
                    var distractor=other.Back;
                    if(ProjectText.SearchKey(distractor)==ProjectText.SearchKey(answer))continue;
                    options.Add(new(distractor,false));if(options.Count==4)break;
                }
                var shift=i%options.Count;
                options=options.Skip(shift).Concat(options.Take(shift)).ToList();
            }
            // Preserve a complete proposition as the expected concept, never a bag of keywords.
            var rubric=isClosed?Array.Empty<RubricCriterion>():new[]{new RubricCriterion("concept-1",quote,1,passage.References.Select(r=>r.ChunkId).ToArray())};
            var prompt=(isClosed?"Quale continuazione completa esattamente il brano?":"Completa il concetto con parole tue.")+
                (prefix.Length>0?$"\n«{prefix} …»":$"\nRiproduci il concetto del brano {i+1} selezionato.");
            questions.Add(new(isClosed?QuestionKind.Closed:QuestionKind.Open,prompt,
                "Esercizio dimostrativo di completamento. Brano originale: "+quote,isClosed?answer:quote,
                request.Configuration.Difficulty??(Difficulty)(i%3),options,rubric,passage.References));
        }
        return new(questions);
    }
    public Task<OpenAnswerEvaluation> EvaluateOpenAnswerAsync(OpenAnswerEvaluationRequest request,CancellationToken ct=default)
        => throw new AiStudyException("Configura Ollama nelle Impostazioni per valutare il significato delle risposte. La risposta resta salvata.");
}
