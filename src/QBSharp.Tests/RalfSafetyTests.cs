using QBSharp.AI;
using QBSharp.Cli.Editor;

namespace QBSharp.Tests;

public sealed class RalfSafetyTests
{
    private static RalfSettings Settings => new() { Provider = "openai", Model = "unit-model", ApiKey = "not-a-real-key" };
    private sealed class Client(string response) : IRalfClient
    { public ValueTask<string> CompleteAsync(RalfRequest request, CancellationToken token) => ValueTask.FromResult(response); }
    [Fact]
    public async Task ApplyingExecutableProposalDoesNotExecuteItOrSaveFileAndUndoRestoresBuffer()
    {
        string root = Path.Combine(Path.GetTempPath(), "qbs-ai-safety-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            string marker = Path.Combine(root, "must-not-exist.txt"), file = Path.Combine(root, "job.bas");
            string original = "Print(42)"; await File.WriteAllTextAsync(file, original);
            var document = new EditorDocument(); await document.OpenAsync(file);
            string proposal = "File.WriteText(\"" + marker.Replace("\"", "\"\"") + "\", \"untrusted\")";
            var service = new RalfService(new Client(proposal), Settings);
            await using var workflow = new EditorAiWorkflow(service); await using var runner = new EditorRunner();
            var controller = new EditorController(document, runner, workflow);
            workflow.Start("make a change", document.Text, document.FilePath); await workflow.WaitAsync(); controller.Tick();
            Assert.NotNull(controller.AiPreview); Assert.False(File.Exists(marker));
            await controller.HandleAsync(new(EditorKey.Right)); Assert.Equal(8, controller.AiPreviewLeft);
            await controller.HandleAsync(new(EditorKey.Home)); Assert.Equal(0, controller.AiPreviewLeft);
            await controller.HandleAsync(new(EditorKey.Enter)); Assert.Equal(proposal, document.Text); Assert.False(File.Exists(marker));
            Assert.Equal(original, await File.ReadAllTextAsync(file)); document.Undo(); Assert.Equal(original, document.Text); Assert.False(document.Dirty);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[1,2]")]
    public async Task JsonResponsesSupportScalarNullAndListShapes(string text)
    {
        var answer = await new RalfService(new Client(text), Settings).AskAsync("return JSON", RalfFormat.Json);
        Assert.Equal(text, answer.Text); if (text == "null") Assert.Null(answer.Data); else Assert.NotNull(answer.Data);
    }
    [Fact]
    public async Task HugeResponseAndInvalidInstructionCannotProduceAnEditorProposal()
    {
        await using var workflow = new EditorAiWorkflow(new(new Client(new string('x', 65537)), Settings));
        Assert.Throws<RalfException>(() => workflow.Start("", "", null));
        workflow.Start("change", "", null); Assert.Null((await workflow.WaitAsync()).Proposal);
    }
}
