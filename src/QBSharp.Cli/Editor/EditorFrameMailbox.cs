using Screen9;

namespace QBSharp.Cli.Editor;

/// <summary>One script producer, one UI consumer. Projection happens only when a frame is due, never on every graphic statement.</summary>
public sealed class EditorFrameMailbox(TimeProvider? timeProvider=null)
{
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;
    private readonly object gate=new();
    private EditorProgramFrame? pending;
    private Canvas? text;
    private PCCanvas? graphics;
    private bool dirty,published;
    private long lastTimestamp,projections,frames;
    public long GraphicsProjections=>Interlocked.Read(ref projections);
    public long PublishedFrames=>Interlocked.Read(ref frames);
    public void Reset()
    {
        text=null;graphics=null;dirty=published=false;lastTimestamp=0;
        Interlocked.Exchange(ref projections,0);Interlocked.Exchange(ref frames,0);
        lock(gate)pending=null;
    }
    public void SubmitText(Canvas canvas){text=canvas;graphics=null;dirty=true;Refresh();}
    public void SubmitGraphics(PCCanvas canvas){graphics=canvas;text=null;dirty=true;Refresh();}
    public void Refresh(bool force=false)
    {
        if(!dirty)return;long now=clock.GetTimestamp();
        if(!force && published && clock.GetElapsedTime(lastTimestamp,now)<TimeSpan.FromMilliseconds(33))return;
        Canvas canvas;
        if(graphics is { } source){canvas=source.ToCellCanvas(80,25);Interlocked.Increment(ref projections);}
        else if(text is { } sourceText)canvas=sourceText;
        else return;
        var frame=EditorProgramFrame.Capture(canvas);
        lock(gate)pending=frame;
        dirty=false;published=true;lastTimestamp=now;Interlocked.Increment(ref frames);
    }
    public bool TryTake(out EditorProgramFrame? frame)
    {lock(gate){frame=pending;pending=null;return frame is not null;}}
}
