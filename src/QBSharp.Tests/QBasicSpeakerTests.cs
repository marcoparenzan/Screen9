using System.Collections.Concurrent;
using QBSharp.Core.Diagnostics;
using QBSharp.QBasic;
using Screen9;

namespace QBSharp.Tests;

/// <summary>Buffer musicale in background (PLAY "MB"), primo piano, PLAY(n), fine programma e interruzione.</summary>
public sealed class QBasicSpeakerTests
{
    /// <summary>Backend finto: registra i toni in ordine e resta bloccato finché il test non apre il cancello.</summary>
    private sealed class Speaker
    {
        public readonly ConcurrentQueue<double> Played=new();
        public readonly TaskCompletionSource Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask Audio(PCTone tone,CancellationToken token)
        {
            Started.TrySetResult();await Gate.Task.WaitAsync(token);if(tone.Frequency>0)Played.Enqueue(Math.Round(tone.Frequency));
        }
    }
    private sealed class Lines:StringWriter
    {
        private readonly object sync=new();
        public override void Write(string? value){lock(sync)base.Write(value);}
        public override void Write(char value){lock(sync)base.Write(value);}
        public string Text{get{lock(sync)return ToString();}}
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while(!condition())await Task.Delay(5,timeout.Token);
    }
    [Fact]
    public async Task BackgroundMusicLetsTheProgramContinueAndFinishesBeforeEnd()
    {
        var speaker=new Speaker();var output=new Lines();
        await using var session=new QBasicEngine(new(){Audio=speaker.Audio,Output=output}).CreateSession();
        var run=session.ExecuteAsync(new("PLAY \"MBL8CDE\"\nPRINT \"after\"; PLAY(0)")).AsTask();
        await Until(()=>output.Text.Contains("after",StringComparison.Ordinal));
        Assert.Contains(" 3",output.Text,StringComparison.Ordinal);Assert.False(run.IsCompleted); // il programma attende la musica prima di finire
        Assert.Empty(speaker.Played);speaker.Gate.SetResult();await run;
        Assert.Equal(new[]{262d,294d,330d},speaker.Played.ToArray());
    }
    [Fact]
    public async Task ForegroundSoundWaitsForQueuedMusicAndItself()
    {
        var speaker=new Speaker();var output=new Lines();
        await using var session=new QBasicEngine(new(){Audio=speaker.Audio,Output=output}).CreateSession();
        var run=session.ExecuteAsync(new("PLAY \"MBC\"\nSOUND 1000, 1\nPRINT \"done\"")).AsTask();
        await speaker.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));await Task.Delay(100);
        Assert.Equal("",output.Text);speaker.Gate.SetResult();await run;
        Assert.Equal(new[]{262d,1000d},speaker.Played.ToArray());Assert.Contains("done",output.Text,StringComparison.Ordinal);
    }
    [Fact]
    public async Task FullBufferBlocksBackgroundPlay()
    {
        var speaker=new Speaker();var output=new Lines();
        await using var session=new QBasicEngine(new(){Audio=speaker.Audio,Output=output}).CreateSession();
        // 40 note con L64 e legato (ML): niente pause di articolazione, 40 toni oltre i 32 del buffer.
        var run=session.ExecuteAsync(new("PLAY \"MBMLL64"+string.Concat(Enumerable.Repeat("C",40))+"\"\nPRINT \"queued\"")).AsTask();
        await speaker.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));await Task.Delay(100);
        Assert.Equal("",output.Text);speaker.Gate.SetResult();await run;
        Assert.Equal(40,speaker.Played.Count);Assert.Contains("queued",output.Text,StringComparison.Ordinal);
    }
    [Fact]
    public async Task CancellationSilencesQueuedMusicAtOnce()
    {
        var speaker=new Speaker();using var cancel=new CancellationTokenSource();
        await using var session=new QBasicEngine(new(){Audio=speaker.Audio,MaximumSteps=long.MaxValue}).CreateSession();
        // Task.Run: il ciclo vuoto non ha await, ExecuteAsync non restituirebbe il controllo al test.
        var run=Task.Run(async()=>await session.ExecuteAsync(new("PLAY \"MBCDEFGAB\"\nDO\nLOOP"),cancel.Token));
        await speaker.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run.WaitAsync(TimeSpan.FromSeconds(10)));
        speaker.Gate.SetResult();await Task.Delay(50);Assert.Empty(speaker.Played);
    }
    [Fact]
    public async Task BackendFailureInBackgroundIsReportedAsRuntimeError()
    {
        await using var session=new QBasicEngine(new(){Audio=(_,_)=>throw new InvalidOperationException("speaker broken")}).CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("PLAY \"MBC\"\nPRINT 1")));
        Assert.Equal("QBS2400",error.Diagnostic.Code); // eccezioni dell'host sanificate come per l'audio in primo piano
    }
    [Fact]
    public async Task PlayFunctionIsZeroWithoutMusic()
    {
        await using var session=new QBasicEngine(new()).CreateSession();
        Assert.Equal(0,Convert.ToInt32(await session.EvaluateAsync(new("PLAY(0)"))));
    }
}
