using System.Collections.Concurrent;
using QBSharp.AI;
using System.Globalization;
using System.Threading.Channels;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.UI;
using Screen9;
using System.Text;

namespace QBSharp.Cli.Editor;

public enum EditorPromptKind { Input, Message, Select, MultiSelect }
public sealed record EditorPrompt(EditorPromptKind Kind, string Title, string Default, IReadOnlyList<string> Choices)
{
    public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
public sealed record EditorRunResult(string SourceText, string SourceFile, ScriptDiagnostic? Diagnostic, string Status)
{
    public string Dialect { get; init; }="qbs";
}

/// <summary>Runs the buffer on a worker task. With <paramref name="window"/> (a PixelWindow on the desktop, a browser canvas on the web), QBasic programs get the whole screen (text, graphics,
/// INPUT echo, keys) as in DOS; otherwise frames go to the editor program view.</summary>
public sealed class EditorRunner(RalfHost? ralfHost = null, Func<PCTone,CancellationToken,ValueTask>? audio=null, Func<IPixelDisplay>? window=null, double timerWaitScale=1) : IAsyncDisposable
{
    public bool UsesWindow => window is not null;
    private readonly ConcurrentQueue<string> output = new();
    private readonly ConcurrentQueue<string> keys = new();
    private readonly Channel<EditorPrompt> prompts = Channel.CreateUnbounded<EditorPrompt>(new() { SingleReader = true, SingleWriter = true });
    private readonly EditorFrameMailbox frames=new();
    public long GraphicsProjections=>frames.GraphicsProjections;
    private CancellationTokenSource? cancellation;
    private Task<EditorRunResult>? task;
    private int characters;
    public bool Running => task is { IsCompleted: false };
    public bool TryReadOutput(out string? line) => output.TryDequeue(out line);
    public bool TryReadPrompt(out EditorPrompt? prompt) => prompts.Reader.TryRead(out prompt);
    public bool TryTakeFrame(out EditorProgramFrame? frame)=>frames.TryTake(out frame);
    public void SendKey(EditorInput input)
    {string key=EditorProgramFrame.EncodeKey(input);if(Running && key.Length>0 && keys.Count<1024)keys.Enqueue(key);}
    private string ReadKey(){frames.Refresh();return keys.TryDequeue(out var key)?key:"";}
    public void Start(string text, string? path, long maximumSteps = 1_000_000,string dialect="qbs")
    {
        if(dialect is not("qbs" or "qbasic"))throw new ArgumentException("Dialect must be qbs or qbasic.",nameof(dialect));
        if(maximumSteps<1)throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        if (Running) throw new InvalidOperationException("A script is already running.");
        cancellation?.Dispose(); cancellation = new(); characters = 0;
        while (output.TryDequeue(out _)) { }
        while(keys.TryDequeue(out _)){}
        frames.Reset();
        
        while (prompts.Reader.TryRead(out var old)) old.Completion.TrySetCanceled();
        string sourceFile = path ?? "<editor>";
        var token = cancellation.Token;
        task = Task.Run(async () =>
        {
            EditorRunResult Result(ScriptDiagnostic? diagnostic,string status)=>new(text,sourceFile,diagnostic,status){Dialect=dialect};
            IPixelDisplay? pixels=null;
            try
            {
                if(dialect=="qbasic")pixels=window?.Invoke();
                using var sink = new RunWriter(this);
                var scriptConsole = new TextConsole(sink);
                var bindings = await new StandardLibraryHost(ui: new RunUi(this), renderer: new TextCanvasRenderer(sink, new(300, 100))).CreateBindingsAsync(token);
                (ralfHost ?? RalfHost.FromConfiguration()).AddBindings(bindings);
                bindings.SetValue("Args", Array.Empty<string>());
                bindings.RegisterFunction("Print", (_, args) =>
                { scriptConsole.WriteLine(string.Join(" ", args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? ""))); return ValueTask.FromResult<object?>(null); });
                IScriptEngine engine=dialect=="qbasic"
                    ?new QBasicEngine(new()
                    {
                        MaximumSteps=maximumSteps,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,TimerWaitScale=timerWaitScale,
                        InputPollingInterval=TimeSpan.FromMilliseconds(10),Output=sink,Input=new RunReader(this,sink,token),Audio=audio,
                        ReadKey=pixels is null?ReadKey:()=>{string key=pixels.ReadKey();return key.Length>0?key:ReadKey();},ScreenInput=pixels is not null,
                        PresentText=pixels is null?frames.SubmitText:pixels.Present,PresentGraphics=pixels is null?frames.SubmitGraphics:pixels.Present,
                        RefreshDisplay=pixels is null?()=>frames.Refresh():pixels.Refresh
                    })
                    :new ScriptEngine(options:new(){MaximumSteps=maximumSteps});
                await using var session = engine.CreateSession(bindings);
                await session.ExecuteAsync(new ScriptSource(text, sourceFile), token);
                if(pixels is not null)await WaitForKeyAsync(pixels,token);
                return Result(null,"Completed");
            }
            catch (OperationCanceledException) { return Result(null,"Canceled"); }
            catch (ScriptRuntimeException error) { Write(error.Diagnostic.ToString()); return Result(error.Diagnostic,"Error"); }
            catch (Exception error) { Write(error.Message); return Result(null,"Error: " + error.Message); }
            finally{frames.Refresh(true);pixels?.Dispose();}
        }, CancellationToken.None);
    }
    /// <summary>As QBasic's "Press any key to continue": the final screen stays until a key, F6 or closing the window.</summary>
    private async Task WaitForKeyAsync(IPixelDisplay pixels,CancellationToken token)
    {
        Write("Press any key in the program window to continue.");
        while(!pixels.Closed && keys.IsEmpty)
        {
            try{if(pixels.ReadKey().Length>0)return;await Task.Delay(20,token);}catch(OperationCanceledException){return;}
        }
    }
    public void Cancel() => cancellation?.Cancel();
    public EditorRunResult? TakeResult()
    {
        if (task is not { IsCompleted: true }) return null;
        var result = task.GetAwaiter().GetResult(); task = null; return result;
    }
    public async Task<EditorRunResult> WaitAsync() => task is { } work ? await work : throw new InvalidOperationException("No run started.");
    private void Write(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (Interlocked.Add(ref characters, line.Length + 1) > 262144) return;
            output.Enqueue(line); while (output.Count > 1000) output.TryDequeue(out _);
        }
    }
    private async ValueTask<object?> Ask(EditorPromptKind kind, string title, string initial, IReadOnlyList<string> items, CancellationToken token)
    {
        var prompt = new EditorPrompt(kind, title, initial, items);
        await prompts.Writer.WriteAsync(prompt, token);
        try { return await prompt.Completion.Task.WaitAsync(token); }
        finally { prompt.Completion.TrySetCanceled(); }
    }
    public async ValueTask DisposeAsync()
    {
        Cancel(); if (task is { } work) await work; cancellation?.Dispose();
        while (prompts.Reader.TryRead(out var prompt)) prompt.Completion.TrySetCanceled();
    }
    private sealed class RunReader(EditorRunner owner,RunWriter writer,CancellationToken token):TextReader
    {
        public override string? ReadLine()
        {
            string title=writer.PendingText;writer.Flush();
            return (string?)owner.Ask(EditorPromptKind.Input,string.IsNullOrWhiteSpace(title)?"QBasic INPUT":title,"",[],token).AsTask().GetAwaiter().GetResult();
        }
    }
    private sealed class RunWriter(EditorRunner owner) : TextWriter
    {
        private readonly StringBuilder pending = new();
        public string PendingText=>pending.ToString();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) { if (value == '\n') { owner.Write(pending.ToString()); pending.Clear(); } else if (value != '\r' && pending.Length<262144) pending.Append(value); }
        public override void WriteLine(string? value) { owner.Write(pending.ToString() + (value ?? "")); pending.Clear(); }
        public override void Flush(){if(pending.Length>0){owner.Write(pending.ToString());pending.Clear();}}
        protected override void Dispose(bool disposing){if(disposing)Flush();base.Dispose(disposing);}
    }
    private sealed class RunUi(EditorRunner owner) : ITextUi
    {
        public ValueTask WindowAsync(string title, string text, CancellationToken token)
        { token.ThrowIfCancellationRequested(); new TextConsole(new RunWriter(owner)).WriteLine(title + ": " + text); return ValueTask.CompletedTask; }
        public async ValueTask<string> MessageBoxAsync(string title, string text, CancellationToken token)
        { await owner.Ask(EditorPromptKind.Message, title + ": " + text, "", [], token); return "OK"; }
        public async ValueTask<string> InputBoxAsync(string prompt, string defaultValue, CancellationToken token) => (string)(await owner.Ask(EditorPromptKind.Input, prompt, defaultValue, [], token))!;
        public async ValueTask<string> SelectAsync(string title, IReadOnlyList<string> items, CancellationToken token) => (string)(await owner.Ask(EditorPromptKind.Select, title, "", items, token))!;
        public async ValueTask<IReadOnlyList<string>> MultiSelectAsync(string title, IReadOnlyList<string> items, CancellationToken token) => (IReadOnlyList<string>)(await owner.Ask(EditorPromptKind.MultiSelect, title, "", items, token))!;
        public async ValueTask TableAsync(object? rows, CancellationToken token)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            await new ConsoleLibTextUi(new TextConsole(writer)).TableAsync(rows, token); owner.Write(writer.ToString());
        }
        public ValueTask ProgressAsync(double percent, string description, CancellationToken token)
        { token.ThrowIfCancellationRequested(); new TextConsole(new RunWriter(owner)).Progress(percent, description); return ValueTask.CompletedTask; }
    }
}
