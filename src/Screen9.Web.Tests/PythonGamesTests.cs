using System.Collections.Concurrent;
using Screen9.Web;

namespace Screen9.Web.Tests;

/// <summary>gorillas.py running on PySharp embedded in the web host, with a fake screen instead of a browser:
/// the same file as the desktop (<c>pysharp run</c>), driven through <see cref="PythonGameHost"/>.</summary>
public sealed class PythonGamesTests
{
    private sealed class FakeDisplay : IPixelDisplay
    {
        public readonly ConcurrentQueue<string> Keys = new();
        public readonly ConcurrentQueue<string> Texts = new();
        public int Frames;
        public bool Closed { get; set; }
        public void Present(PCCanvas canvas) => Interlocked.Increment(ref Frames);
        public void Present(Canvas cells)
        {
            Interlocked.Increment(ref Frames);
            Texts.Enqueue(string.Concat(Enumerable.Range(0, cells.Height).Select(y => new string(Enumerable.Range(0, cells.Width).Select(x => cells[x, y].Character).ToArray()) + "\n")));
        }
        public void Refresh() { if (Closed) throw new OperationCanceledException(); }
        public string ReadKey() { Refresh(); return Keys.TryDequeue(out var key) ? key : ""; }
        public void Dispose() { }
    }

    private static string Source => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "gorillas.py"));

    [Fact]
    public async Task IntroScreenAppearsAndTheGameAsksForNames()
    {
        var display = new FakeDisplay(); var tones = new ConcurrentQueue<PCTone>(); var output = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource();
        var host = new PythonGameHost(display, tones.Enqueue, cts.Token);
        var run = PythonGames.RunAsync(Source, "gorillas.py", host, output.Enqueue);

        Assert.True(SpinWait.SpinUntil(() => display.Texts.Any(t => t.Contains("G O R I L L A S")), TimeSpan.FromSeconds(20)), "intro screen not presented");
        display.Keys.Enqueue(" "); // any key: past the intro
        Assert.True(SpinWait.SpinUntil(() => display.Texts.Any(t => t.Contains("Name of Player 1")), TimeSpan.FromSeconds(20)), "name prompt not presented");
        Assert.NotEmpty(tones); // the intro tune reached the audio callback

        cts.Cancel();
        Assert.Equal("Completed", await run.WaitAsync(TimeSpan.FromSeconds(20))); // gorillas.py ends cleanly when its screen goes away
        Assert.Empty(output); // no Python error text
    }

    [Fact]
    public async Task ClosingTheScreenEndsTheProgram()
    {
        var display = new FakeDisplay();
        var host = new PythonGameHost(display, _ => { }, CancellationToken.None);
        var run = PythonGames.RunAsync(Source, "gorillas.py", host, _ => { });
        Assert.True(SpinWait.SpinUntil(() => display.Frames > 0, TimeSpan.FromSeconds(20)));
        display.Closed = true; // the dialog's Close: the next device call throws OperationCanceledException -> KeyboardInterrupt
        Assert.Equal("Completed", await run.WaitAsync(TimeSpan.FromSeconds(20))); // gorillas.py ends cleanly when its screen goes away
    }

    [Fact]
    public async Task PythonErrorsComeBackAsText()
    {
        var output = new ConcurrentQueue<string>();
        var host = new PythonGameHost(new FakeDisplay(), _ => { }, CancellationToken.None);
        string result = await PythonGames.RunAsync("raise ValueError('boom')", "bad.py", host, output.Enqueue);
        Assert.StartsWith("Error:", result);
        Assert.Contains(output, line => line.Contains("boom"));
    }
}
