using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace Screen9;

/// <summary>
/// PC-speaker music buffer as in QBasic: one queue of at most 32 tones played in order on a separate task through an audio backend such as <see cref="Beep"/> or <see cref="Silent"/>.
/// Background tones (PLAY "MB") return once queued, waiting only when the buffer is full; foreground tones (PLAY "MF", SOUND, BEEP)
/// wait until they have been played, after anything already queued. The host backend may be called from another thread.
/// </summary>
public sealed class PCSpeaker
{
    public const int Capacity=32;
    private readonly Func<PCTone,CancellationToken,ValueTask> audio;
    private readonly Channel<(PCTone? Tone,TaskCompletionSource? Done)> queue=
        Channel.CreateBounded<(PCTone?,TaskCompletionSource?)>(new BoundedChannelOptions(Capacity){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
    private readonly CancellationTokenSource stop=new();
    private readonly Task player;
    private Exception? failure;
    private int notes,items;
    /// <summary>Nothing queued or playing: a foreground tone may be played directly by the caller, keeping the order.</summary>
    public bool Idle=>Volatile.Read(ref items)==0;
    /// <summary>Sounding notes queued or playing (not rests or articulation gaps): the value of PLAY(n).</summary>
    public int Notes=>Volatile.Read(ref notes);
    public PCSpeaker(Func<PCTone,CancellationToken,ValueTask> audio){this.audio=audio;player=Task.Run(RunAsync);}

    public async ValueTask PlayAsync(PCTone tone,CancellationToken token)
    {
        ThrowIfFailed();
        var done=tone.Background?null:new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if(tone.Frequency>0)Interlocked.Increment(ref notes);
        Interlocked.Increment(ref items);
        try{await queue.Writer.WriteAsync((tone,done),token);}
        catch(Exception e) when(e is ChannelClosedException or OperationCanceledException)
        {
            if(tone.Frequency>0)Interlocked.Decrement(ref notes);Interlocked.Decrement(ref items);
            if(e is OperationCanceledException)throw;ThrowIfFailed();throw new OperationCanceledException(token);
        }
        if(done is not null)await done.Task.WaitAsync(token);
    }
    /// <summary>Synchronous <see cref="PlayAsync"/> for game loops and script hosts (PySharp): a background tone returns
    /// once queued, a foreground tone after it has been played.</summary>
    public void Play(PCTone tone,CancellationToken token=default)=>PlayAsync(tone,token).AsTask().GetAwaiter().GetResult();
    /// <summary>Synchronous <see cref="DrainAsync"/>.</summary>
    public void Drain(CancellationToken token=default)=>DrainAsync(token).AsTask().GetAwaiter().GetResult();
    /// <summary>Waits until every queued tone has been played (normal end of program).</summary>
    public async ValueTask DrainAsync(CancellationToken token)
    {
        ThrowIfFailed();var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try{await queue.Writer.WriteAsync((null,done),token);}catch(ChannelClosedException){ThrowIfFailed();return;}
        await done.Task.WaitAsync(token);ThrowIfFailed();
    }
    /// <summary>Silences the speaker at once: queued tones are dropped and waiting statements are canceled.</summary>
    public void Stop()
    {
        stop.Cancel();queue.Writer.TryComplete();
        try{player.Wait(TimeSpan.FromSeconds(1));}catch(AggregateException){}
    }
    private async Task RunAsync()
    {
        try
        {
            await foreach(var (tone,done) in queue.Reader.ReadAllAsync(stop.Token))
            {
                if(tone is not null)
                {
                    try{await audio(tone,stop.Token);}
                    catch(OperationCanceledException) when(stop.IsCancellationRequested){done?.TrySetCanceled();throw;}
                    catch(Exception e){done?.TrySetException(e);throw;}
                    finally{if(tone.Frequency>0)Interlocked.Decrement(ref notes);Interlocked.Decrement(ref items);}
                }
                done?.TrySetResult();
            }
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested){}
        catch(Exception e){Volatile.Write(ref failure,e);}
        finally
        {
            queue.Writer.TryComplete();
            var reason=Volatile.Read(ref failure);
            while(queue.Reader.TryRead(out var item))
            {
                if(item.Tone is { Frequency: >0 })Interlocked.Decrement(ref notes);
                if(item.Tone is not null)Interlocked.Decrement(ref items);
                if(reason is null)item.Done?.TrySetCanceled();else item.Done?.TrySetException(reason);
            }
        }
    }
    public void ThrowIfFailed(){if(Volatile.Read(ref failure) is { } e)ExceptionDispatchInfo.Capture(e).Throw();}
    /// <summary>Silent backend: keeps duration and cancellation, rather than swallowing the tone.</summary>
    public static async ValueTask Silent(PCTone tone,CancellationToken token)=>await Task.Delay(tone.Duration,token);
    /// <summary>Windows backend on <see cref="Console.Beep(int,int)"/>, in slices of at most 50 ms so cancellation is prompt.</summary>
    public static async ValueTask Beep(PCTone tone,CancellationToken token)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("PC speaker adapter requires Windows.");
        double remaining=tone.Duration.TotalMilliseconds;
        while(remaining>0)
        {
            token.ThrowIfCancellationRequested();int slice=(int)Math.Min(50,Math.Ceiling(remaining));
            if(tone.Frequency==0)await Task.Delay(slice,token);
            else Console.Beep(Math.Clamp((int)Math.Round(tone.Frequency),37,32767),slice);
            remaining-=slice;
        }
    }
}
