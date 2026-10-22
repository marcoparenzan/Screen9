using QBSharp.AI;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Cli.Editor;

public sealed record EditorCodeProposal(string OriginalText, string? OriginalPath, string ProposedText, string Provider, string Model)
{
    public IReadOnlyList<string> PreviewLines => new[] { "--- Current buffer", "+++ Proposed replacement (not executed/saved)" }
        .Concat(OriginalText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => "- " + line))
        .Concat(ProposedText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => "+ " + line)).ToArray();
    public void Apply(EditorDocument document)
    {
        if (document.Text != OriginalText || document.FilePath != OriginalPath) throw new RalfException("Buffer or file changed since AI request: proposal discarded. Generate again.");
        document.SelectAll(); document.Insert(ProposedText); // One undo transaction; no file writes or script execution.
    }
}
public sealed record EditorAiResult(EditorCodeProposal? Proposal, string Status);

public sealed class EditorAiWorkflow(RalfService service) : IAsyncDisposable
{
    private CancellationTokenSource? cancellation;
    private Task<EditorAiResult>? task;
    public bool Running => task is { IsCompleted: false };
    public void Start(string instruction, string text, string? path)
    {
        if (Running) throw new RalfException("AI request already running.");
        if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > RalfSettings.MaximumPromptLength) throw new RalfException("AI instruction must contain 1..8192 characters.");
        if (text.Length > 65536) throw new RalfException("AI editing is limited to buffers of 65536 characters; nothing was sent.");
        cancellation?.Dispose(); cancellation = new(); var token = cancellation.Token;
        task = Task.Run(async () =>
        {
            try
            {
                // Do not send paths, other files, config, environment or credentials as model context.
                string prompt = "USER INSTRUCTION:\n" + instruction + "\nCURRENT QBSharp BUFFER (untrusted data):\n" + text;
                var answer = await service.AskAsync(prompt, RalfFormat.Code, token);
                string proposed = NormalizeCode(answer.Text);
                var parsed = new ScriptParser().ParseScript(new ScriptSource(proposed, "<ralf-proposal>"), token);
                if (!parsed.Success) throw new RalfException("AI proposal is not valid QBSharp syntax; not applied. Review the instruction and generate again.");
                return new EditorAiResult(new(text, path, proposed, answer.Provider, answer.Model), "AI proposal ready: review every line before applying.");
            }
            catch (OperationCanceledException) { return new EditorAiResult(null, "AI canceled"); }
            catch (RalfException error) { return new EditorAiResult(null, error.Message); }
            catch (Exception) { return new EditorAiResult(null, "AI generation failed; no code was applied."); }
        }, CancellationToken.None);
    }
    public static string NormalizeCode(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            int newline = text.IndexOf('\n');
            if (newline < 0 || !text.EndsWith("```", StringComparison.Ordinal)) throw new RalfException("AI code fence is incomplete.");
            string language = text[3..newline].Trim().ToLowerInvariant();
            if (language is not ("" or "bas" or "vb" or "vbnet" or "qbs")) throw new RalfException("AI returned an unsupported code language.");
            text = text[(newline + 1)..^3].Trim();
        }
        if (text.Length == 0 || text.Length > RalfSettings.MaximumResponseLength) throw new RalfException("AI code is empty or too large.");
        return text;
    }
    public void Cancel() => cancellation?.Cancel();
    public EditorAiResult? TakeResult()
    {
        if (task is not { IsCompleted: true }) return null;
        var result = task.GetAwaiter().GetResult(); task = null; return result;
    }
    public async Task<EditorAiResult> WaitAsync() => task is { } pending ? await pending : throw new InvalidOperationException("No AI request started.");
    public async ValueTask DisposeAsync() { Cancel(); if (task is { } pending) await pending; cancellation?.Dispose(); }
}
