using Microsoft.Extensions.Configuration;
using QBSharp.AI;
using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary;

namespace QBSharp.Tests;

public sealed class RalfConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qbs-ralf-config-" + Guid.NewGuid());
    public RalfConfigurationTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string FilePath => Path.Combine(root, "external.json");
    private IConfiguration Config(params (string Key, string Value)[] overrides) => new ConfigurationBuilder().AddInMemoryCollection(
        new[] { new KeyValuePair<string, string?>("QBS_AI_CONFIG_FILE", FilePath) }.Concat(overrides.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))).Build();
    [Theory]
    [InlineData("openai", "OpenAI")]
    [InlineData("anthropic", "Anthropic")]
    [InlineData("foundry", "AzureAIFoundry")]
    public void ExternalCredentialsLoadOnlySelectedProviderAndEnvironmentOverrides(string provider, string group)
    {
        File.WriteAllText(FilePath, "{\"AIProvider\":\"" + provider + "\",\"" + group + "\":{\"ApiKey\":\"test-file-secret\",\"ModelId\":\"file-model\",\"Endpoint\":\"https://example.test\"}}");
        var settings = RalfSettings.FromConfiguration(Config()); settings.Validate();
        Assert.Equal(provider, settings.Provider); Assert.Equal("file-model", settings.Model); Assert.Equal("test-file-secret", settings.ApiKey);
        Assert.DoesNotContain("test-file-secret", settings.ToString());
        settings = RalfSettings.FromConfiguration(Config(("QBS_AI_MODEL", "override-model"), ("QBS_AI_API_KEY", "override-key")));
        Assert.Equal("override-model", settings.Model); Assert.Equal("override-key", settings.ApiKey);
    }
    [Fact]
    public void FoundryContextAndDefaultModelAliasesAndApiAreSupported()
    {
        File.WriteAllText(FilePath, """
            {"AIProvider":"foundry","AzureAIFoundry":{"ApiKey":"fake","Endpoint":"https://example.test","ModelId":"base","Cost":0.25,"Enabled":true,"Api":"responses","DefaultModel":"default","ContextModels":{"qbsharp":"script"},"Models":{"script":{"ModelId":"script-id"},"default":{"ModelId":"default-id"}}}}
            """);
        var settings = RalfSettings.FromConfiguration(Config()); settings.Validate(); Assert.Equal("script-id", settings.Model); Assert.Equal("responses", settings.FoundryApi);
        settings = RalfSettings.FromConfiguration(Config(("QBS_AI_CONTEXT_KEY", "other"), ("QBS_AI_FOUNDRY_API", "chat"))); Assert.Equal("default-id", settings.Model); Assert.Equal("chat", settings.FoundryApi);
        var provider = RalfProviderClient.BuildProviderConfiguration(settings); Assert.Equal("chat", provider["AzureAIFoundry:Api"]);
    }
    [Theory]
    [InlineData("{\"AIProvider\":\"openai\", \"OpenAI\":{\"ApiKey\":\"sensitive", false)]
    [InlineData("[]", false)]
    [InlineData("{\"AIProvider\":\"openai\",\"OpenAI\":{\"ApiKey\":\"fake\",\"apikey\":\"other\"}}", false)]
    [InlineData("", true)]
    public void InvalidExternalFilesHaveSanitizedErrors(string text, bool large)
    {
        File.WriteAllText(FilePath, large ? new string('x', RalfConfiguration.MaximumFileBytes + 1) : text);
        var error = Assert.Throws<RalfException>(() => RalfSettings.FromConfiguration(Config()));
        Assert.Null(error.InnerException); Assert.DoesNotContain("sensitive", error.ToString()); Assert.DoesNotContain(FilePath, error.ToString());
    }
    [Fact]
    public void MissingRelativePathsAndExplicitDisabledAiDoNotLeakOrReadFiles()
    {
        Assert.Throws<RalfException>(() => RalfSettings.FromConfiguration(Config()));
        var disabled = RalfSettings.FromConfiguration(Config(("QBS_AI_PROVIDER", ""))); Assert.Equal("", disabled.Provider);
        Assert.Throws<RalfException>(() => RalfSettings.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["QBS_AI_CONFIG_FILE"] = "relative.json" }).Build()));
    }
    [Fact]
    public void FileReloadAndUtf8BomPreserveCredentialsWithoutCopyingAnything()
    {
        File.WriteAllText(FilePath, "{\"AIProvider\":\"openai\",\"OpenAI\":{\"ApiKey\":\"fake\",\"ModelId\":\"one\"}}", new System.Text.UTF8Encoding(true));
        var config = Config(); Assert.Equal("one", RalfSettings.FromConfiguration(config).Model);
        File.WriteAllText(FilePath, "{\"AIProvider\":\"openai\",\"OpenAI\":{\"ApiKey\":\"fake\",\"ModelId\":\"two\"}}"); Assert.Equal("two", RalfSettings.FromConfiguration(config).Model);
        Assert.Single(Directory.GetFiles(root));
    }
    private sealed class Client : IRalfClient
    {
        public RalfRequest? Request;
        public ValueTask<string> CompleteAsync(RalfRequest request, CancellationToken token) { Request = request; return ValueTask.FromResult("answer"); }
    }
    [Fact]
    public async Task ExternalConfigNeverAppearsInScriptConfigOrPrompt()
    {
        File.WriteAllText(FilePath, "{\"AIProvider\":\"openai\",\"OpenAI\":{\"ApiKey\":\"fake-file-secret\",\"ModelId\":\"unit-model\"}}");
        var client = new Client(); var host = new RalfHost(new RalfService(client, () => RalfSettings.FromConfiguration(Config())));
        var bindings = await new StandardLibraryHost(new() { LoadConfiguration = false }).CreateBindingsAsync(); host.AddBindings(bindings);
        await using var session = new ScriptEngine().CreateSession(bindings); await session.ExecuteAsync(new("Dim answer = Ralf(\"question\")"));
        Assert.Equal("fake-file-secret", client.Request!.Settings.ApiKey); Assert.DoesNotContain("fake-file-secret", client.Request.Prompt);
        Assert.Null(await session.EvaluateAsync(new("Config.Get(\"OpenAI.ApiKey\")")));
        Assert.DoesNotContain(session.Variables.Keys, key => key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }
}
