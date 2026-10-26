using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudySpace.Application.Flashcards;
using StudySpace.Application.Quizzes;
using StudySpace.Domain.Common;
using StudySpace.Infrastructure.Ai;
using StudySpace.Infrastructure.Persistence;

namespace StudySpace.IntegrationTests;

public sealed class SemanticEvaluationTests
{
    private static QuizQuestionSnapshot Question()
    {
        var id=Guid.NewGuid();
        return new(Guid.NewGuid(),0,QuestionKind.Open,"Quale servizio offre TCP?","Trasporto affidabile.",
            "TCP offre consegna affidabile e ordinata dei dati.",Difficulty.Medium,[],
            [new("reliability","Consegna affidabile",0.8,[id]),new("order","Dati ordinati",0.2,[id])],
            [new(Guid.NewGuid(),Guid.NewGuid(),id,"TCP offre consegna affidabile e ordinata dei dati.","Reti","Pagina 2")]);
    }
    private static string Json(OpenAnswerEvaluation value)=>JsonSerializer.Serialize(value,EvaluationJsonContext.Default.OpenAnswerEvaluation);

    [Fact] public void Valid_json_preserves_every_feedback_field_and_trusted_evidence()
    {
        var q=Question();var value=TestEvaluation.Create(q) with{IncorrectConcepts=["La perdita non è garantita."],Confidence=0.71};
        var parsed=OpenAnswerEvaluationJson.Parse(Json(value),q);
        Assert.Equal(100,parsed.Score);Assert.True(parsed.IsCorrect);Assert.Equal(OpenAnswerStatus.Correct,parsed.Status);
        Assert.Equal(value.IncorrectConcepts,parsed.IncorrectConcepts);Assert.Equal(0.71,parsed.Confidence);
        Assert.Equal(q.Citations[0].ChunkId,Assert.Single(parsed.SourceReferences).ChunkId);
    }

    [Theory]
    [InlineData("missing-score")][InlineData("missing-confidence")][InlineData("unknown")][InlineData("null-list")]
    [InlineData("negative")][InlineData("over-100")][InlineData("wrong-score")][InlineData("wrong-status")]
    [InlineData("numeric-status")][InlineData("wrong-correct")][InlineData("confidence")][InlineData("foreign-citation")]
    [InlineData("invented-quote")][InlineData("missing-criteria")][InlineData("unknown-criterion")]
    [InlineData("duplicate-criterion")][InlineData("missing-grade")][InlineData("invalid-grade")]
    [InlineData("blank-summary")][InlineData("long-suggestion")][InlineData("no-citations")][InlineData("duplicate-citation")]
    public void Invalid_structured_results_never_become_a_grade(string mutation)
    {
        var q=Question();var node=JsonNode.Parse(Json(TestEvaluation.Create(q)))!.AsObject();
        switch(mutation)
        {
            case "missing-score":node.Remove("score");break;
            case "missing-confidence":node.Remove("confidence");break;
            case "unknown":node["secret"]=true;break;
            case "null-list":node["correctConcepts"]=null;break;
            case "negative":node["score"]=-1;break;
            case "over-100":node["score"]=101;break;
            case "wrong-score":node["score"]=70;break;
            case "wrong-status":node["status"]="Incorrect";break;
            case "numeric-status":node["status"]=0;break;
            case "wrong-correct":node["isCorrect"]=false;break;
            case "confidence":node["confidence"]=1.1;break;
            case "foreign-citation":node["sourceReferences"]![0]!["chunkId"]=Guid.NewGuid().ToString();break;
            case "invented-quote":node["sourceReferences"]![0]!["quote"]="TCP non ordina i dati.";break;
            case "no-citations":node["sourceReferences"]=new JsonArray();break;
            case "duplicate-citation":node["sourceReferences"]!.AsArray().Add(node["sourceReferences"]![0]!.DeepClone());break;
            case "missing-criteria":node["criteria"]!.AsArray().RemoveAt(0);break;
            case "unknown-criterion":node["criteria"]![0]!["criterionId"]="injected";break;
            case "duplicate-criterion":node["criteria"]![1]!["criterionId"]="reliability";break;
            case "missing-grade":node["criteria"]![0]!.AsObject().Remove("grade");break;
            case "invalid-grade":node["criteria"]![0]!["grade"]=2;break;
            case "blank-summary":node["summary"]=" ";break;
            case "long-suggestion":node["suggestions"]=new JsonArray(new string('x',2001));break;
        }
        Assert.Throws<AiStudyException>(()=>OpenAnswerEvaluationJson.Parse(node.ToJsonString(),q));
    }
    [Theory]
    [InlineData("{")][InlineData("null")][InlineData("[]")][InlineData("```json\n{}\n```")]
    [InlineData("{\"score\":0,\"score\":100}")][InlineData("{\"score\":NaN}")]
    public void Malformed_or_ambiguous_json_is_rejected(string json)=>Assert.Throws<AiStudyException>(()=>OpenAnswerEvaluationJson.Parse(json,Question()));

    [Fact] public async Task Http_adapter_sends_question_rubric_evidence_and_answer_with_schema_and_no_keys()
    {
        var q=Question();var expected=TestEvaluation.Create(q);
        var handler=new Handler(async message=>
        {
            Assert.Equal("http://localhost:11434/api/chat",message.RequestUri!.AbsoluteUri);
            Assert.Null(message.Headers.Authorization);
            using var body=JsonDocument.Parse(await message.Content!.ReadAsStringAsync());var root=body.RootElement;
            Assert.False(root.GetProperty("stream").GetBoolean());Assert.Equal("test-model",root.GetProperty("model").GetString());
            Assert.Equal("object",root.GetProperty("format").GetProperty("type").GetString());
            Assert.Contains("parafrasi",root.GetProperty("messages")[0].GetProperty("content").GetString());
            using var data=JsonDocument.Parse(root.GetProperty("messages")[1].GetProperty("content").GetString()!);
            Assert.Equal("Consegna sicura, nella sequenza originale.",data.RootElement.GetProperty("answer").GetString());
            Assert.Equal(q.Prompt,data.RootElement.GetProperty("question").GetProperty("prompt").GetString());
            Assert.Equal(q.Citations[0].Quote,data.RootElement.GetProperty("question").GetProperty("citations")[0].GetProperty("quote").GetString());
            return Reply(expected);
        });
        var ai=Service(handler);var result=await ai.EvaluateOpenAnswerAsync(new(Guid.NewGuid(),Guid.NewGuid(),q.Id,2,q,"Consegna sicura, nella sequenza originale."));
        Assert.Equal(100,result.Score);Assert.False(ai.EvaluationProvider.IsMock);Assert.True(ai.Provider.IsMock);
    }
    [Theory][InlineData(404)][InlineData(429)][InlineData(500)][InlineData(503)][InlineData(302)]
    public async Task Http_failures_are_retryable_and_do_not_expose_raw_server_messages(int code)
    {
        var handler=new Handler(_=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)code){Content=new StringContent("sensitive internal detail")}));
        var q=Question();var ex=await Assert.ThrowsAsync<AiStudyException>(()=>Service(handler).EvaluateOpenAnswerAsync(new(Guid.NewGuid(),Guid.NewGuid(),q.Id,1,q,"Risposta")));
        Assert.Contains("salvata",ex.Message);Assert.DoesNotContain("sensitive",ex.Message);
    }
    [Theory][InlineData("offline")][InlineData("broken-body")][InlineData("oversize")][InlineData("not-done")][InlineData("truncated")][InlineData("bad-json")]
    public async Task Connection_stream_and_invalid_envelope_errors_are_safe(string failure)
    {
        var q=Question();var handler=new Handler(_=>
        {
            if(failure=="offline")throw new HttpRequestException("private details");
            if(failure=="broken-body")throw new IOException("private details");
            if(failure=="oversize")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',128001))});
            if(failure=="bad-json")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{")});
            return Task.FromResult(Reply(TestEvaluation.Create(q),failure!="not-done",failure=="truncated"?"length":"stop"));
        });
        await Assert.ThrowsAsync<AiStudyException>(()=>Service(handler).EvaluateOpenAnswerAsync(new(Guid.NewGuid(),Guid.NewGuid(),q.Id,1,q,"Risposta")));
    }
    [Fact] public async Task Disabled_configuration_and_demo_never_fall_back_to_keyword_grading()
    {
        var called=false;var handler=new Handler(_=>{called=true;throw new Exception();});
        var ai=new OllamaStudyService(new HttpClient(handler),new MemorySemanticEvaluationSettingsStore(),new());var q=Question();
        var request=new OpenAnswerEvaluationRequest(Guid.NewGuid(),Guid.NewGuid(),q.Id,1,q,q.IdealAnswer);
        await Assert.ThrowsAsync<AiStudyException>(()=>ai.EvaluateOpenAnswerAsync(request));
        await Assert.ThrowsAsync<AiStudyException>(()=>new MockAiStudyService().EvaluateOpenAnswerAsync(request));Assert.False(called);
    }
    [Fact] public void Old_persisted_feedback_without_semantic_metadata_remains_readable()
    {
        var feedback=new AnswerFeedback(AnswerOutcome.Correct,1,"Vecchia demo","Soluzione",[],[],[],"",[],"mock",true);
        var json=JsonSerializer.Serialize(feedback,QuizJsonContext.Default.AnswerFeedback);
        var node=JsonNode.Parse(json)!.AsObject();node.Remove("SemanticEvaluation");
        var read=JsonSerializer.Deserialize(node.ToJsonString(),QuizJsonContext.Default.AnswerFeedback)!;
        Assert.Equal(1,read.ScoringVersion);Assert.Null(read.SemanticEvaluation);Assert.Equal(1,read.Score);
    }
    private static OllamaStudyService Service(Handler handler)
    {
        var settings=new MemorySemanticEvaluationSettingsStore();settings.Save(new(true,"http://localhost:11434","test-model"));
        return new(new HttpClient(handler),settings,new());
    }
    private static HttpResponseMessage Reply(OpenAnswerEvaluation value,bool done=true,string reason="stop")=>new(HttpStatusCode.OK)
    {Content=new StringContent(new JsonObject{["done"]=done,["done_reason"]=reason,["message"]=new JsonObject{["role"]="assistant",["content"]=Json(value)}}.ToJsonString(),Encoding.UTF8,"application/json")};
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request);}
}
