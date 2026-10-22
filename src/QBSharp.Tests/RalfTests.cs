using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QBSharp.AI;
using QBSharp.Cli;
using QBSharp.Cli.Editor;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using RalfAI.Providers;
using Spectre.Console;

namespace QBSharp.Tests;

public sealed class RalfTests
{
    private static RalfSettings Settings => new() { Provider = "openai", Model = "unit-model", ApiKey = "test-secret-not-a-real-key" };
    private sealed class Fake(Func<RalfRequest, CancellationToken, ValueTask<string>> action) : IRalfClient
    {
        public int Calls; public RalfRequest? Request;
        public ValueTask<string> CompleteAsync(RalfRequest request, CancellationToken token) { Calls++; Request = request; return action(request, token); }
    }
    private static Fake Reply(string text) => new((_, _) => ValueTask.FromResult(text));
    private static RalfHost Host(Fake fake, RalfSettings? settings = null) => new(new RalfService(fake, settings ?? Settings));
    [Theory]
    [InlineData("", "model", "key", null)]
    [InlineData("wrong", "model", "key", null)]
    [InlineData("openai", "", "key", null)]
    [InlineData("openai", "model", "", null)]
    [InlineData("openai", "model", "key", "https://example.test")]
    [InlineData("foundry", "model", "key", "http://example.test")]
    [InlineData("foundry", "model", "key", "https://user:pass@example.test")]
    [InlineData("foundry", "model", "key", "https://example.test?key=secret")]
    public void InvalidConfigurationIsRejectedWithoutEchoingSecrets(string provider, string model, string key, string? endpoint)
    {
        var settings = Settings with { Provider = provider, Model = model, ApiKey = key, Endpoint = endpoint };
        var error = Assert.Throws<RalfException>(settings.Validate); Assert.DoesNotContain("pass@", error.ToString()); Assert.DoesNotContain("key=secret", error.ToString());
    }
    [Theory]
    [InlineData(0, 2048)] [InlineData(301, 2048)] [InlineData(60, 63)] [InlineData(60, 8193)]
    public void TimeoutAndTokenBoundsAreValidated(int seconds, int tokens) => Assert.Throws<RalfException>((Settings with { Timeout = TimeSpan.FromSeconds(seconds), MaximumOutputTokens = tokens }).Validate);
    [Fact]
    public void EnvironmentConfigurationIsMappedToActualRalfProviderSchema()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["QBS_AI_PROVIDER"] = "Foundry", ["QBS_AI_MODEL"] = "unit-model", ["QBS_AI_API_KEY"] = "secret", ["QBS_AI_ENDPOINT"] = "https://example.test", ["QBS_AI_TIMEOUT_SECONDS"] = "30" }).Build();
        var settings = RalfSettings.FromConfiguration(config); settings.Validate();
        var provider = RalfProviderClient.BuildProviderConfiguration(settings);
        Assert.Equal("foundry", provider["AIProvider"]); Assert.Equal("secret", provider["AzureAIFoundry:ApiKey"]); Assert.Equal("chat", provider["AzureAIFoundry:Api"]);
        Assert.Equal("unit-model", provider["AzureAIFoundry:ModelId"]); Assert.Equal(TimeSpan.FromSeconds(30), settings.Timeout); Assert.DoesNotContain("secret", settings.ToString());
    }
    [Theory]
    [InlineData("openai")] [InlineData("anthropic")] [InlineData("foundry")]
    public void RealRalfFactoryConstructsClientsWithoutNetwork(string provider)
    {
        var settings = Settings with { Provider = provider, Endpoint = provider == "foundry" ? "https://example.test" : null };
        var (client, model) = ChatClientFactory.Create(RalfProviderClient.BuildProviderConfiguration(settings), null!);
        using (client) Assert.Equal("unit-model", model);
    }
    [Theory]
    [InlineData("Ralf()")]
    [InlineData("Ralf(42)")]
    [InlineData("Ralf(\"x\", \"code\")")]
    [InlineData("Ralf(\"x\", 1)")]
    [InlineData("Ralf(\"x\", \"xml\")")]
    public async Task InvalidScriptCallsHaveCallSiteAndDoNotCallProvider(string source)
    {
        var fake = Reply("answer"); var bindings = new HostBindings(); Host(fake).AddBindings(bindings);
        await using var session = new ScriptEngine().CreateSession(bindings);
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new(source, "ai.bas")));
        Assert.Equal("ai.bas", error.Diagnostic.Span.File); Assert.Equal(1, error.Diagnostic.Span.Start.Line); Assert.Equal(0, fake.Calls);
    }
    [Fact]
    public async Task BindingReturnsTextJsonAndClrDynamicFieldsWithCancellation()
    {
        var fake = Reply("{\"Select\":42}"); var bindings = new HostBindings(); Host(fake).AddBindings(bindings);
        await using var session = new ScriptEngine().CreateSession(bindings);
        await session.ExecuteAsync(new("Dim answer = Ralf(\"prompt\", \"json\")"));
        Assert.Equal(42, await session.EvaluateAsync(new("answer.Data.Select"))); Assert.Equal("unit-model", await session.EvaluateAsync(new("answer.Model")));
        Assert.Equal("{\"Select\":42}", await session.EvaluateAsync(new("answer.Text")));
        Assert.Contains("JSON", fake.Request!.SystemPrompt); Assert.Equal("prompt", fake.Request.Prompt);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.EvaluateAsync(new("Ralf(\"x\")"), cts.Token)); Assert.Equal(1, fake.Calls);
    }
    [Theory]
    [InlineData("bad")]
    [InlineData("{\"x\":1,\"X\":2}")]
    [InlineData("```json\n{}\n```")]
    public async Task InvalidJsonResponseHasSanitizedError(string response)
    {
        var error = await Assert.ThrowsAsync<RalfException>(async () => await Host(Reply(response)).Service.AskAsync("prompt", RalfFormat.Json));
        Assert.Contains("JSON", error.Message); Assert.DoesNotContain(response, error.Message);
    }
    [Fact]
    public async Task ProviderErrorsCannotLeakKeyPromptOrStack()
    {
        var fake = new Fake((_, _) => throw new IOException("test-secret-not-a-real-key private-user-prompt"));
        var error = await Assert.ThrowsAsync<RalfException>(async () => await Host(fake).Service.AskAsync("private-user-prompt"));
        Assert.Null(error.InnerException); Assert.DoesNotContain("test-secret", error.ToString()); Assert.DoesNotContain("private-user", error.ToString());
    }
    [Fact]
    public async Task DisabledAndPromptResponseLimitsAreEnforced()
    {
        var fake = Reply("answer");
        await Assert.ThrowsAsync<RalfException>(async () => await Host(fake, new()).Service.AskAsync("x")); Assert.Equal(0, fake.Calls);
        await Assert.ThrowsAsync<RalfException>(async () => await Host(fake).Service.AskAsync(new string('x', 8193))); Assert.Equal(0, fake.Calls);
        await Assert.ThrowsAsync<RalfException>(async () => await Host(Reply(new string('x', 65537))).Service.AskAsync("x"));
        await Assert.ThrowsAsync<RalfException>(async () => await Host(Reply(" ")).Service.AskAsync("x"));
    }
    [Fact]
    public async Task TimeoutAndHostCancellationAreDistinctEvenIfProviderIgnoresToken()
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new Fake((_, _) => new(pending.Task));
        try
        {
            var error = await Assert.ThrowsAsync<RalfException>(async () => await Host(fake, Settings with { Timeout = TimeSpan.FromSeconds(1) }).Service.AskAsync("x")); Assert.Contains("timed out", error.Message);
            using var cts = new CancellationTokenSource(30);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Host(fake).Service.AskAsync("x", token: cts.Token));
        }
        finally { pending.TrySetResult("done"); }
    }
    [Fact]
    public async Task CliBasFixtureRunsWithInjectedOfflineHostAndDiRegistration()
    {
        var fake = Reply("{\"Select\":42}"); using var output = new StringWriter(); using var errors = new StringWriter();
        var console = AnsiConsole.Create(new() { Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(output) });
        var stderr = AnsiConsole.Create(new() { Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(errors) });
        var app = new CliApplication(console, stderr, ralfHost: Host(fake));
        Assert.Equal(0, await app.RunAsync(["test", Path.Combine(AppContext.BaseDirectory, "samples", "ralf-test.bas")]));
        Assert.Contains("PASS", output.ToString()); Assert.Empty(errors.ToString()); Assert.Equal(1, fake.Calls);
        var services = new ServiceCollection(); services.AddSingleton<IRalfClient>(fake); services.AddQBSharpAI().AddQBSharpAI();
        using var provider = services.BuildServiceProvider(); Assert.Same(fake, provider.GetRequiredService<IRalfClient>()); Assert.NotNull(provider.GetRequiredService<RalfHost>());
    }
    [Theory]
    [InlineData("Print(42)", "Print(42)")]
    [InlineData("```bas\nPrint(42)\n```", "Print(42)")]
    [InlineData("```vbnet\nPrint(42)\n```", "Print(42)")]
    public void CodeFencesAreNormalizedWithoutExecutingAnything(string source, string expected) => Assert.Equal(expected, EditorAiWorkflow.NormalizeCode(source));
    [Theory]
    [InlineData("```python\nprint(42)\n```")]
    [InlineData("```bas\nPrint(42)")]
    [InlineData("")]
    public void InvalidCodeFormatsAreRejected(string source) => Assert.Throws<RalfException>(() => EditorAiWorkflow.NormalizeCode(source));
    [Fact]
    public async Task EditorDisclosesDataAndRequiresPreviewApplyThenExplicitSaveOrRun()
    {
        var fake = Reply("Print(99)"); var host = Host(fake);
        await using var runner = new EditorRunner(host); await using var ai = new EditorAiWorkflow(host.Service);
        var document = new EditorDocument(); document.Insert("Print(42)"); var controller = new EditorController(document, runner, ai);
        await controller.HandleAsync(new(EditorKey.F7)); Assert.Equal("RalfAI disclosure", controller.Dialog!.Title); Assert.Equal(0, fake.Calls);
        await controller.HandleAsync(new(EditorKey.Enter)); Assert.Null(controller.Dialog); Assert.Equal(0, fake.Calls); // Default Cancel
        await controller.HandleAsync(new(EditorKey.F7)); await controller.HandleAsync(new(EditorKey.Down)); await controller.HandleAsync(new(EditorKey.Enter));
        await controller.HandleAsync(new(EditorKey.Text, "change 42 to 99")); await controller.HandleAsync(new(EditorKey.Enter));
        var result = await ai.WaitAsync(); controller.Tick(); Assert.NotNull(result.Proposal); Assert.NotNull(controller.AiPreview);
        Assert.Equal("Print(42)", document.Text); Assert.False(runner.Running); Assert.Empty(controller.Output);
        var frame = EditorFrame.Build(controller); Assert.Contains("RalfAI PREVIEW", string.Concat(frame.Cells.SelectMany(row => row.Select(cell => cell.Character))));
        await controller.HandleAsync(new(EditorKey.Enter)); Assert.Equal("Print(99)", document.Text); Assert.True(document.Dirty); Assert.Null(document.FilePath);
        document.Undo(); Assert.Equal("Print(42)", document.Text);
    }
    [Fact]
    public async Task EditorRejectsInvalidSyntaxAndChangedBufferAndCanDiscardOrCancel()
    {
        var fake = Reply("Dim = 1"); await using var bad = new EditorAiWorkflow(Host(fake).Service); bad.Start("change", "", null);
        Assert.Null((await bad.WaitAsync()).Proposal);
        await using var good = new EditorAiWorkflow(Host(Reply("Print(99)")).Service); good.Start("change", "Print(42)", null);
        var proposal = (await good.WaitAsync()).Proposal!; var doc = new EditorDocument(); doc.Insert("changed");
        Assert.Throws<RalfException>(() => proposal.Apply(doc)); Assert.Equal("changed", doc.Text);
        await using var runner = new EditorRunner(); var c = new EditorController(new EditorDocument(), runner, good); c.Tick();
        Assert.NotNull(c.AiPreview); await c.HandleAsync(new(EditorKey.Escape)); Assert.Null(c.AiPreview); Assert.Equal("", c.Document.Text);
        var pending = new Fake(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return ""; });
        await using var wait = new EditorAiWorkflow(Host(pending).Service); wait.Start("change", "", null); wait.Cancel(); Assert.Equal("AI canceled", (await wait.WaitAsync()).Status);
    }
    [Fact]
    public async Task EditorContextLimitsAndPromptExcludeFilePathsAndSecrets()
    {
        var fake = Reply("Print(42)"); await using var ai = new EditorAiWorkflow(Host(fake).Service);
        Assert.Throws<RalfException>(() => ai.Start("change", new string('x', 65537), null)); Assert.Equal(0, fake.Calls);
        ai.Start("change", "' code", "/private/location.bas"); await ai.WaitAsync();
        Assert.DoesNotContain("/private", fake.Request!.Prompt); Assert.DoesNotContain(Settings.ApiKey, fake.Request.Prompt); Assert.Contains("no tools", fake.Request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task RealCliMissingAiConfigurationFailsSafelyWithoutNetwork()
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in info.Environment.Keys.Where(key => key.StartsWith("QBS_AI_", StringComparison.OrdinalIgnoreCase)).ToList()) info.Environment.Remove(key);
        info.Environment["QBS_AI_PROVIDER"] = ""; info.Environment["NO_COLOR"] = "1";
        info.ArgumentList.Add(typeof(CliApplication).Assembly.Location); info.ArgumentList.Add("run"); info.ArgumentList.Add("-");
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync("Ralf(\"test\")"); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); try { await process.WaitForExitAsync(timeout.Token); } finally { if (!process.HasExited) process.Kill(true); }
        Assert.Equal(4, process.ExitCode); Assert.Contains("RalfAI disabled", await error); Assert.Empty(await output);
    }
    private sealed class ChatFake : IChatClient
    {
        public IEnumerable<ChatMessage>? Messages; public ChatOptions? Options; public bool Disposed;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        { Messages = messages.ToList(); Options = options; return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))); }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.CompletedTask; yield break; }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public async Task ProviderAdapterSendsOnlySystemAndUserWithoutToolsAndDisposesClient()
    {
        var client = new ChatFake(); var provider = new RalfProviderClient(_ => client);
        Assert.Equal("answer", await provider.CompleteAsync(new("prompt", "system", Settings), default));
        Assert.Equal(2, client.Messages!.Count()); Assert.Null(client.Options!.Tools); Assert.Equal(2048, client.Options.MaxOutputTokens); Assert.True(client.Disposed);
    }
}
