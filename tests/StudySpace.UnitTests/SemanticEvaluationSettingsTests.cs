using StudySpace.Application.Common;
using StudySpace.Application.Quizzes;

namespace StudySpace.UnitTests;
public sealed class SemanticEvaluationSettingsTests
{
    [Theory]
    [InlineData("http://localhost:11434")][InlineData("http://127.0.0.1:11434")][InlineData("http://10.0.2.2:11434")]
    [InlineData("http://192.168.1.20:11434")][InlineData("https://models.example.org")]
    public void Explicit_local_or_https_endpoints_are_allowed(string endpoint)
        =>Assert.Equal("model",new SemanticEvaluationSettings(true,endpoint," model ").Validate().Model);
    [Theory]
    [InlineData("file:///tmp/model")][InlineData("http://example.org")][InlineData("http://8.8.8.8")]
    [InlineData("https://user:secret@models.example.org")][InlineData("https://models.example.org/api/chat")]
    [InlineData("https://models.example.org?key=secret")][InlineData("https://models.example.org#key")]
    public void Unsupported_or_credential_bearing_addresses_are_rejected(string endpoint)
        =>Assert.Throws<StudyValidationException>(()=>new SemanticEvaluationSettings(true,endpoint,"model").Validate());
    [Fact] public void Model_is_required_only_when_enabling_and_configuration_defaults_to_no_network()
    {
        var store=new MemorySemanticEvaluationSettingsStore();Assert.False(store.Read().Enabled);
        store.Save(new());
        Assert.Throws<StudyValidationException>(()=>store.Save(new(true)));
        store.Save(new(true,Model:"local-model"));Assert.True(store.Read().Enabled);Assert.Equal("local-model",store.Read().Model);
    }
}
