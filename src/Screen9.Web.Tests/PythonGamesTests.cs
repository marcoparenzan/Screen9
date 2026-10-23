using System.Collections.Concurrent;
using Screen9.Web.Client.Components;

namespace Screen9.Web.Tests;

/// <summary>gorillas.py on PySharp's <c>RunAsync</c> (the way the browser runs it), with a fake host instead of a screen:
/// the same file as the desktop (<c>pysharp run</c>), driven through <see cref="Screen9.Web.Client.Components.BrowserPython"/>.</summary>
public sealed class PythonGamesTests
{
    /// <summary>The <c>web_host</c> of <c>gorillas.py</c> over plain queues instead of a browser screen: the same members as
    /// <see cref="BrowserPythonHost"/>, with <c>sleep</c> and <c>tone</c> returning tasks that <c>PyEngine.RunAsync</c> awaits.</summary>
#pragma warning disable IDE1006 // snake_case: this is a Python-facing API
    private sealed class FakeHost(CancellationToken token = default)
    {
        public readonly ConcurrentQueue<string> Keys = new();
        public readonly ConcurrentQueue<string> Texts = new();
        public readonly ConcurrentQueue<PCTone> Tones = new();
        public int Frames;
        public bool Closed { get; set; }

        public void present(PCCanvas? graphics, Canvas cells)
        {
            Interlocked.Increment(ref Frames);
            if (graphics is not null) return;
            Texts.Enqueue(string.Concat(Enumerable.Range(0, cells.Height).Select(y => new string(Enumerable.Range(0, cells.Width).Select(x => cells[x, y].Character).ToArray()) + "\n")));
        }
        public string read_key()
        {
            Check();
            return Keys.TryDequeue(out var key) ? key : "";
        }
        public void flush_keys() => Keys.Clear();
        public async Task sleep(double seconds) { Check(); await Task.Delay(TimeSpan.FromSeconds(seconds), token); }
        public async Task tone(PCTone tone) { Tones.Enqueue(tone); await sleep(tone.Duration.TotalSeconds); }
        public void background(PCTone[] tones) { foreach (var tone in tones) Tones.Enqueue(tone); }
        public int seed() => 1990;
        public void checkpoint(string name, Canvas cells) { }
        private void Check() { token.ThrowIfCancellationRequested(); if (Closed) throw new OperationCanceledException(); }
    }
#pragma warning restore IDE1006

    private static string Source => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "gorillas.py"));

    [Fact]
    public async Task IntroScreenAppearsAndTheGameAsksForNames()
    {
        var output = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource();
        var display = new FakeHost(cts.Token);
        var tones = display.Tones;
        var run = BrowserPython.RunAsync(Source, "gorillas.py", display, output.Enqueue);

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
        var display = new FakeHost();
        var run = BrowserPython.RunAsync(Source, "gorillas.py", display, _ => { });
        Assert.True(SpinWait.SpinUntil(() => display.Frames > 0, TimeSpan.FromSeconds(20)));
        display.Closed = true; // the dialog's Close: the next device call throws OperationCanceledException -> KeyboardInterrupt
        Assert.Equal("Completed", await run.WaitAsync(TimeSpan.FromSeconds(20))); // gorillas.py ends cleanly when its screen goes away
    }

    [Fact]
    public async Task PythonErrorsComeBackAsText()
    {
        var output = new ConcurrentQueue<string>();
        string result = await BrowserPython.RunAsync("raise ValueError('boom')", "bad.py", new FakeHost(), output.Enqueue);
        Assert.StartsWith("Error:", result);
        Assert.Contains(output, line => line.Contains("boom"));
    }
}
