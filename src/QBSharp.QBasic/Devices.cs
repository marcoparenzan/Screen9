using Screen9;
using QBSharp.Core.Syntax;

namespace QBSharp.QBasic;

/// <summary>Session-local 20-bit real-mode address space, including the virtual BIOS keyboard flags.
/// With a graphics canvas, its CPU window (A000 EGA/VGA, B800 CGA) is routed to PCCanvas video memory.</summary>
public sealed class QBasicMemory
{
    private readonly byte[] bytes = new byte[1 << 20];
    public ushort Segment { get; private set; }
    public void SetSegment(int? segment = null)
    {
        if (segment is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(segment));
        Segment = (ushort)(segment ?? 0);
    }
    private int Address(int offset)
    {
        if (offset is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(offset));
        return ((Segment << 4) + offset) & 0xfffff;
    }
    public byte Peek(int offset, PCCanvas? video = null)
    {
        int address = Address(offset);
        return VideoOffset(video, address) is { } local ? video!.ReadVideoMemory(local) : bytes[address];
    }
    public void Poke(int offset, int value, PCCanvas? video = null)
    {
        if (value is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(value));
        int address = Address(offset);
        if (VideoOffset(video, address) is { } local) video!.WriteVideoMemory(local, (byte)value); else bytes[address] = (byte)value;
    }
    private static int? VideoOffset(PCCanvas? video, int address)
    {
        if (video is null) return null;
        int local = address - (video.VideoSegment << 4);
        return (uint)local < (uint)video.VideoWindowBytes ? local : null;
    }
}

internal sealed partial class QBasicSession
{
    private readonly QBasicMemory memory = new();
    private readonly PCMusic music = new();
    private readonly PCTextDisplay textDisplay = new();
    private PCCanvas? graphics;
    private PCCanvas Graphics => graphics ?? throw new BFault(5,"Graphics operation requires SCREEN graphics mode.");
    private int currentX, currentY, foreground = 15, background;
    private int textWidth = 80, textHeight = 25;
    private async ValueTask Device(BInstruction node, BScope scope)
    {
        var values = await Args(node.Args,scope); cancellation.ThrowIfCancellationRequested();
        int I(int i, int fallback = 0) => i >= values.Count || values[i] is null ? fallback : BValues.Integer(values[i]);
        double N(int i, double fallback = 0) => i >= values.Count || values[i] is null ? fallback : BValues.Number(values[i]);
        string S(int i) => BValues.Text(values[i]);
        int X(int i) => I(i,currentX); int Y(int i) => I(i,currentY);
        switch(node.Name)
        {
            case "DEF SEG":memory.SetSegment(values.Count==0?null:I(0));break;
            case "POKE":memory.Poke(I(0),I(1),graphics);break;
            case "OUT":
                int port=I(0),portValue=I(1);
                if(port is <0 or >65535||portValue is <0 or >255)throw new BFault(5,"OUT port 0..65535, value 0..255.");
                Port(()=>Graphics.WritePort(port,(byte)portValue),port);break;
            case "BEEP":await Tone(new(800,TimeSpan.FromMilliseconds(200)));break;
            case "SOUND":
                double frequency=N(0),ticks=N(1);
                if(frequency != 0 && frequency is <37 or >32767 || ticks is <0 or >65535)throw new BFault(5,"Invalid SOUND frequency/duration.");
                await Tone(new(frequency,TimeSpan.FromSeconds(ticks/18.2)));break;
            case "PLAY":foreach(var tone in music.Parse(S(0)))await Tone(tone);break;
            case "SLEEP":
                double seconds=N(0);
                if(seconds is <0 or >86400)throw new BFault(5,"Invalid SLEEP duration.");
                if(seconds==0){while(options.ReadKey()=="")await options.Delay(TimeSpan.FromMilliseconds(10),cancellation);}
                else
                {
                    var remaining=TimeSpan.FromSeconds(seconds);
                    while(remaining>TimeSpan.Zero && options.ReadKey()=="")
                    {var slice=remaining<TimeSpan.FromMilliseconds(20)?remaining:TimeSpan.FromMilliseconds(20);await options.Delay(slice,cancellation);remaining-=slice;}
                }
                break;
            case "SCREEN":
                int mode=I(0);graphics=mode==0?null:new PCCanvas(mode);currentX=currentY=0;background=0;printColumn=0;
                if(graphics is not null){graphics.ActivePage=I(2);graphics.VisualPage=I(3);textWidth=graphics.Width/8;textHeight=mode==9?25:mode is 11 or 12?30:25;foreground=Math.Min(15,PCVideoMode.FromNumber(mode).Colors-1);}else{textWidth=80;textHeight=25;foreground=7;background=0;}textDisplay.Resize(textWidth,textHeight);break;
            case "PALETTE":if(values.Count==0)Graphics.ResetPalette();else Graphics.SetPalette(I(0),I(1));break;
            case "PSET":case "PRESET":currentX=X(0);currentY=Y(1);Graphics.PSet(currentX,currentY,I(2,node.Name=="PRESET"?background:foreground));break;
            case "LINE":
                int x1=X(0),y1=Y(1),x2=I(2),y2=I(3),color=I(4,foreground);
                // PCCanvas.Box takes width/height (right/bottom exclusive); QBasic B/BF corners are both inclusive.
                if(node.Data is string style && style is "B" or "BF")Graphics.Box(Math.Min(x1,x2),Math.Min(y1,y2),Math.Abs(x2-x1)+1,Math.Abs(y2-y1)+1,color,style=="BF");
                else Graphics.Line(x1,y1,x2,y2,color);currentX=x2;currentY=y2;break;
            case "CIRCLE":
                currentX=X(0);currentY=Y(1);Graphics.Circle(currentX,currentY,N(2),I(3,foreground),N(4),N(5,Math.PI*2),Math.Abs(N(6,1)));break;
            case "PAINT":currentX=X(0);currentY=Y(1);Graphics.Paint(currentX,currentY,I(2,foreground),I(3,I(2,foreground)),cancellation);break;
            case "GET":case "PUT":
                var array=values[^1] as QBasicArray ?? throw new BFault(13,"GET/PUT requires a LONG array.");
                if(node.Name=="GET")QBasicSpriteCodec.Capture(Graphics,array,I(0),I(1),I(2),I(3));
                else QBasicSpriteCodec.Draw(Graphics,array,I(0),I(1),node.Data as string ?? "XOR",cancellation);break;
            case "COLOR":
                int newForeground=I(0,foreground),newBackground=I(1,background);
                if(newForeground is <0 or >15||newBackground is <0 or >15)throw new BFault(5,"COLOR range 0..15.");
                foreground=newForeground;background=newBackground;break;
            case "WIDTH":
                int newWidth=I(0,textWidth),newHeight=I(1,textHeight);
                if(newWidth is <1 or >300 || newHeight is <1 or >100)throw new BFault(5,"Invalid text dimensions.");
                textDisplay.Resize(newWidth,newHeight);textWidth=newWidth;textHeight=newHeight;printColumn=0;break;
            case "LOCATE":textDisplay.Locate(I(0,textDisplay.Row),I(1,textDisplay.Column));printColumn=textDisplay.Column-1;break;
            case "CLS":if(I(0) is <0 or >2)throw new BFault(5,"CLS supports 0,1,2.");if(I(0)!=2&&graphics is not null)graphics.ClearIndex(background);if(I(0)!=1)textDisplay.Clear(foreground,background,graphics,I(0)==2,cancellation);printColumn=textDisplay.Column-1;break;
            case "VIEW PRINT":textDisplay.View(values.Count==0?null:I(0),values.Count==0?null:I(1));printColumn=textDisplay.Column-1;break;
            default:throw new BFault(5,"QBasic runtime feature not implemented: "+node.Name);
        }
        if(graphics is not null)options.PresentGraphics?.Invoke(graphics);else options.PresentText?.Invoke(textDisplay.Cells);
    }
    // OUT/INP: solo le porte EGA/VGA modellate da PCCanvas; le altre sono un errore QBasic esplicito.
    private static T Port<T>(Func<T> access,int port)
    {
        try{return access();}
        catch(ArgumentOutOfRangeException){throw new BFault(5,$"Port &H{port:X} is not emulated (supported: &H3C4/&H3C5, &H3CE/&H3CF, &H3C6-&H3C9).");}
        catch(InvalidOperationException e){throw new BFault(5,$"Port &H{port:X}: {e.Message}");}
    }
    private static void Port(Action access,int port)=>Port(()=>{access();return 0;},port);
    private PCSpeaker? speaker;
    private async ValueTask Tone(PCTone tone)
    {
        cancellation.ThrowIfCancellationRequested();
        if(options.Audio is null)throw new BFault(5,"Audio backend not configured by host.");
        speaker?.ThrowIfFailed();
        // Fast path: with an empty buffer a foreground tone plays here, as before; only the interpreter enqueues, so order holds.
        if(!tone.Background&&(speaker is null||speaker.Idle)){await options.Audio(tone,cancellation);return;}
        speaker??=new(options.Audio);
        await speaker.PlayAsync(tone,cancellation);
    }
}

internal sealed partial class BParser
{
    private void Coordinate(List<BExpr> args)
    {Need("(");args.Add(Expr());Need(",");args.Add(Expr());Need(")");}
    private void GraphicsLine(SourceSpan span)
    {
        List<BExpr> args=[];
        if(At("-")){args.Add(new BLiteral(null,span));args.Add(new BLiteral(null,span));}else Coordinate(args);
        Need("-");Coordinate(args);string style="";
        if(Eat(",")){args.Add(At(",")||EndStatement?new BLiteral(null,span):Expr());if(Eat(","))style=Name().ToUpperInvariant();}
        if(style is not ("" or "B" or "BF"))throw BLexer.Error("LINE supports B/BF, not line style masks.",span);
        Emit("Device",span,"LINE",args,style);
    }
    private void DeviceStatement(string word,SourceSpan span)
    {
        List<BExpr> args=[];string style="";
        if(word=="VIEW"){Need("PRINT");if(!EndStatement){args.Add(Expr());Need("TO");args.Add(Expr());}Emit("Device",span,"VIEW PRINT",args);return;}
        if(word is "PSET" or "PRESET" or "CIRCLE" or "PAINT" or "PUT" or "GET")
        {
            Coordinate(args);
            if(word=="GET"){Need("-");Coordinate(args);Need(",");args.Add(Postfix());}
            else if(word=="PUT"){Need(",");args.Add(Postfix());if(Eat(","))style=Name().ToUpperInvariant();}
            else if(Eat(","))OptionalArgs(args,span);
        }
        else if(!EndStatement)OptionalArgs(args,span);
        int min=word switch{"POKE" or "SOUND" or "OUT"=>2,"PLAY" or "SCREEN"=>1,"CIRCLE"=>3,_=>0};
        int max=word switch{"DEF SEG" or "PLAY" or "SLEEP" or "CLS"=>1,"BEEP"=>0,"POKE" or "SOUND" or "OUT" or "WIDTH" or "COLOR" or "PALETTE"=>2,"SCREEN"=>4,"LOCATE"=>5,"CIRCLE"=>7,"PAINT"=>4,"PSET" or "PRESET"=>3,"GET"=>5,"PUT"=>3,_=>0};
        if(args.Count<min||args.Count>max)throw BLexer.Error("Invalid device statement arguments.",span);
        Emit("Device",span,word,args,style);
    }
    private void OptionalArgs(List<BExpr> args,SourceSpan span)
    {
        do{args.Add(At(",")||EndStatement?new BLiteral(null,span):Expr());}while(Eat(","));
    }
}
