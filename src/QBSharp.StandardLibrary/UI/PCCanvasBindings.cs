using Screen9;
using QBSharp.StandardLibrary.Serialization;

namespace QBSharp.StandardLibrary.UI;

internal static class PCCanvasBindings
{
    private static void Count(IReadOnlyList<object?> args, int min, int max) { if (args.Count < min || args.Count > max) throw new ArgumentException($"Expected {min}..{max} arguments."); }
    private static int Int(IReadOnlyList<object?> args, int i) => args[i] is int value ? value : throw new ArgumentException("Expected Integer coordinate, palette index or byte.");
    private static double Number(IReadOnlyList<object?> args, int i) => args[i] is int or long or double or decimal ? Convert.ToDouble(args[i], System.Globalization.CultureInfo.InvariantCulture) : throw new ArgumentException("Expected numeric radius/angle/aspect.");
    private static bool Bool(IReadOnlyList<object?> args, int i) => args[i] is bool value ? value : throw new ArgumentException("Expected Boolean.");
    private static PCCanvas PC(IReadOnlyList<object?> args) => args[0] as PCCanvas ?? throw new ArgumentException("Expected Screen9 PCCanvas from PC.Create.");
    private static ValueTask<object?> Value(object? value = null) => ValueTask.FromResult(value);
    public static StandardNamespace Create(ICanvasRenderer? renderer)
    {
        var group = new StandardNamespace();
        group.Add("Create", (_, args) => { Count(args, 1, 1); return Value(new PCCanvas(Int(args,0))); });
        group.Add("Modes", (_, args) =>
        {
            Count(args,0,0);
            return Value(new[]{1,2,7,8,9,11,12,13}.Select(PCVideoMode.FromNumber).Select(mode => new DynamicRecord(new Dictionary<string,object?>
            { ["Mode"]=mode.Number,["Adapter"]=mode.Adapter.ToString(),["Width"]=mode.Width,["Height"]=mode.Height,["Colors"]=mode.Colors,["Pages"]=mode.Pages })).ToList());
        });
        group.Add("Clear", (_,args) => { Count(args,1,2); PC(args).ClearIndex(args.Count==2?Int(args,1):0); return Value(); });
        group.Add("PSet", (_,args) => { Count(args,4,4); return Value(PC(args).PSet(Int(args,1),Int(args,2),Int(args,3))); });
        group.Add("Point", (_,args) => { Count(args,3,4); var pc=PC(args); return Value(pc.Point(Int(args,1),Int(args,2),args.Count==4?Int(args,3):pc.ActivePage)); });
        group.Add("Line", (_,args) => { Count(args,6,6); PC(args).Line(Int(args,1),Int(args,2),Int(args,3),Int(args,4),Int(args,5)); return Value(); });
        group.Add("Box", (_,args) => { Count(args,6,7); PC(args).Box(Int(args,1),Int(args,2),Int(args,3),Int(args,4),Int(args,5),args.Count==7&&Bool(args,6)); return Value(); });
        group.Add("Ellipse", (_,args) => { Count(args,6,7); PC(args).Ellipse(Int(args,1),Int(args,2),Int(args,3),Int(args,4),Int(args,5),args.Count==7&&Bool(args,6)); return Value(); });
        group.Add("Circle", (_,args) => { Count(args,5,8); PC(args).Circle(Int(args,1),Int(args,2),Number(args,3),Int(args,4),args.Count>5?Number(args,5):0,args.Count>6?Number(args,6):Math.PI*2,args.Count>7?Number(args,7):1); return Value(); });
        group.Add("Paint", (context,args) => { Count(args,4,5); return Value(PC(args).Paint(Int(args,1),Int(args,2),Int(args,3),args.Count==5?Int(args,4):null,context.CancellationToken)); });
        group.Add("Palette", (_,args) => { Count(args,3,3); PC(args).SetPalette(Int(args,1),Int(args,2)); return Value(); });
        group.Add("CgaPalette", (_,args) => { Count(args,2,4); PC(args).SetCgaPalette(Int(args,1),args.Count>2&&Bool(args,2),args.Count>3?Int(args,3):0); return Value(); });
        group.Add("Dac", (_,args) => { Count(args,5,5); PC(args).SetDac(Int(args,1),Int(args,2),Int(args,3),Int(args,4)); return Value(); });
        group.Add("Rgb", (_,args) => { Count(args,2,2); return Value(PC(args).GetRgb(Int(args,1)).Packed); });
        group.Add("ResetPalette", (_,args) => { Count(args,1,1); PC(args).ResetPalette(); return Value(); });
        group.Add("Active", (_,args) => { Count(args,1,2); var pc=PC(args); if(args.Count==2)pc.ActivePage=Int(args,1); return Value(pc.ActivePage); });
        group.Add("Visual", (_,args) => { Count(args,1,2); var pc=PC(args); if(args.Count==2)pc.VisualPage=Int(args,1); return Value(pc.VisualPage); });
        group.Add("ReadByte", (_,args) => { Count(args,2,3); return Value((int)PC(args).ReadByte(Int(args,1),args.Count==3?Int(args,2):0)); });
        group.Add("WriteByte", (_,args) => { Count(args,3,4); int value=Int(args,2); if(value is <0 or >255)throw new ArgumentOutOfRangeException(nameof(value)); PC(args).WriteByte(Int(args,1),(byte)value,args.Count==4?Int(args,3):0); return Value(); });
        group.Add("Get", (_,args) => { Count(args,5,5); return Value(PC(args).GetImage(Int(args,1),Int(args,2),Int(args,3),Int(args,4))); });
        group.Add("Put", (context,args) =>
        {
            Count(args,4,6); var image=args[1] as PCImage??throw new ArgumentException("Expected PCImage from PC.Get.");
            var op=args.Count>4 && args[4] is string name ? name.ToUpperInvariant() switch
            { "COPY" or "PSET"=>PCPixelOperation.Copy,"PRESET"=>PCPixelOperation.Preset,"XOR"=>PCPixelOperation.Xor,"AND"=>PCPixelOperation.And,"OR"=>PCPixelOperation.Or,_=>throw new ArgumentException("Raster operation must be copy/pset/preset/xor/and/or.") }
                : args.Count>4?throw new ArgumentException("Expected String raster operation."):PCPixelOperation.Copy;
            PC(args).PutImage(image,Int(args,2),Int(args,3),op,args.Count==6?Int(args,5):null,context.CancellationToken); return Value();
        });
        group.Add("Snapshot", (_,args) => { Count(args,1,2); return Value(PC(args).PixelSnapshot(args.Count==2?Int(args,1):-1).Select(value=>(int)value).ToArray()); });
        group.Add("Preview", (context,args) => { Count(args,3,3); return Value(PC(args).ToCellCanvas(Int(args,1),Int(args,2),context.CancellationToken)); });
        group.Add("Ascii", (context,args) =>
        {
            Count(args,3,3); var pc=PC(args); int width=Int(args,1),height=Int(args,2);
            if(width is <1 or >300 || height is <1 or >100)throw new ArgumentOutOfRangeException(nameof(width));
            var rows=new string[height];
            for(int y=0;y<height;y++)
            {
                context.CancellationToken.ThrowIfCancellationRequested(); var chars=new char[width];
                for(int x=0;x<width;x++){int color=pc.Point(x*pc.Width/width,y*pc.Height/height,pc.VisualPage); chars[x]=color==0?'.':color<16?"0123456789ABCDEF"[color]:'#';}
                rows[y]=new(chars);
            }
            return Value(rows);
        });
        group.Add("Render", (context,args) =>
        {
            if(args.Count is not (1 or 3))throw new ArgumentException("PC.Render requires canvas and optional columns,rows.");
            var target=renderer??throw new InvalidOperationException("No Screen9 renderer configured by host."); var size=target.GetViewportSize();
            var frame=PC(args).ToCellCanvas(args.Count==3?Int(args,1):Math.Min(size.Width,300),args.Count==3?Int(args,2):Math.Min(size.Height,100),context.CancellationToken);
            target.Render(frame); return Value();
        });
        return group;
    }
}
