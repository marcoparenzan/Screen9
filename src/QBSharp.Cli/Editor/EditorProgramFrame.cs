using System.Collections.ObjectModel;
using Screen9;

namespace QBSharp.Cli.Editor;

/// <summary>Copied Screen9 cells passed from the script worker to the editor UI; never a live canvas.</summary>
public sealed class EditorProgramFrame
{
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyCollection<Cell> Cells { get; }
    private EditorProgramFrame(int width,int height,Cell[] cells){Width=width;Height=height;Cells=Array.AsReadOnly(cells);}
    public Cell this[int x,int y]=>Cells[y*Width+x];
    public static EditorProgramFrame Capture(Canvas canvas)
    {
        int width=Math.Min(canvas.Width,300),height=Math.Min(canvas.Height,100);var cells=new Cell[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)cells[y*width+x]=canvas[x,y];
        return new(width,height,cells);
    }
    public static string EncodeKey(EditorInput input)
    {
        if(input.X>=0)return "";
        if(input.Modifiers.HasFlag(EditorModifiers.Control) && input.Text.Length==1 && char.IsAsciiLetter(input.Text[0]))
            return ((char)(char.ToUpperInvariant(input.Text[0])-'A'+1)).ToString();
        return input.Key switch
        {
            EditorKey.Enter=>"\r",EditorKey.Tab=>"\t",EditorKey.Backspace=>"\b",EditorKey.Escape=>"\x1b",
            EditorKey.Up=>"\0H",EditorKey.Down=>"\0P",EditorKey.Left=>"\0K",EditorKey.Right=>"\0M",
            EditorKey.Home=>"\0G",EditorKey.End=>"\0O",EditorKey.PageUp=>"\0I",EditorKey.PageDown=>"\0Q",
            EditorKey.Insert=>"\0R",EditorKey.Delete=>"\0S",
            EditorKey.Text=>input.Text.Length==1 && input.Text[0]<='\xff'?input.Text:"",_=>""
        };
    }
}
