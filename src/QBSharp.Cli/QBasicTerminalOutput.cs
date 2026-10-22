using System.Runtime.InteropServices;
using System.Text;
using Screen9;

namespace QBSharp.Cli;

/// <summary>Screen9 canvas presenter in a scoped VT alternate screen. Never used on redirected CLI output.</summary>
public sealed class QBasicTerminalOutput : IDisposable
{
    private readonly TextWriter writer;
    private readonly Func<(int Width,int Height)> viewport;
    private readonly TimeProvider clock;
    private bool disposed,rendered;
    private long lastFrame;
    private int lastWidth,lastHeight;
    private Canvas? pending;
    private nint handle;
    private uint originalMode;
    private bool changedMode;
    private static readonly int[] AnsiColors=[30,34,32,36,31,35,33,37,90,94,92,96,91,95,93,97];
    public QBasicTerminalOutput(TextWriter writer,Func<(int Width,int Height)> viewport,bool configureNative=true,TimeProvider? clock=null)
    {
        this.writer=writer;this.viewport=viewport;this.clock=clock??TimeProvider.System;
        if(configureNative && OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
        {
            handle=GetStdHandle(-11);
            if(!GetConsoleMode(handle,out originalMode) || !SetConsoleMode(handle,originalMode|4))
                throw new IOException("Unable to enable virtual terminal output for QBasic run.");
            changedMode=true;
        }
        try{writer.Write("\x1b[?1049h\x1b[?25l\x1b[?7l\x1b[0m\x1b[2J\x1b[H");writer.Flush();}
        catch{RestoreMode();throw;}
    }
    public void Present(Canvas canvas)
    {
        ObjectDisposedException.ThrowIf(disposed,this);pending=canvas;DrawPending(false);
    }
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(disposed,this);DrawPending(false);
    }
    private void DrawPending(bool force)
    {
        if(pending is not { } canvas)return;
        long now=clock.GetTimestamp();
        if(!force && rendered && clock.GetElapsedTime(lastFrame,now)<TimeSpan.FromMilliseconds(33))return;
        var size=viewport();int width=Math.Min(canvas.Width,Math.Clamp(size.Width,1,300)),height=Math.Min(canvas.Height,Math.Clamp(size.Height,1,100));
        var text=new StringBuilder();
        if(width!=lastWidth || height!=lastHeight)text.Append("\x1b[2J");
        int previousForeground=-1,previousBackground=-1;
        for(int y=0;y<height;y++)
        {
            text.Append("\x1b[").Append(y+1).Append(";1H");
            for(int x=0;x<width;x++)
            {
                var cell=canvas[x,y];int foreground=(int)cell.Foreground,background=(int)cell.Background;
                if(foreground is <0 or >15 || background is <0 or >15)throw new ArgumentException("Canvas color outside ConsoleColor palette.");
                if(foreground!=previousForeground || background!=previousBackground)
                {
                    text.Append("\x1b[").Append(AnsiColors[foreground]).Append(';').Append(AnsiColors[background]+10).Append('m');
                    previousForeground=foreground;previousBackground=background;
                }
                text.Append(Glyph(cell.Character));
            }
        }
        writer.Write(text.ToString());writer.Flush();lastFrame=now;rendered=true;lastWidth=width;lastHeight=height;
    }
    private static char Glyph(char glyph)=>glyph switch
    {
        'Ä'=>'─','Í'=>'═','³'=>'│','º'=>'║','Û'=>'█','É'=>'╔','»'=>'╗','È'=>'╚','¼'=>'╝',
        'Ú'=>'┌','¿'=>'┐','À'=>'└','Ù'=>'┘',_=>char.IsControl(glyph)||char.IsSurrogate(glyph)?' ':glyph
    };
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{DrawPending(true);}
        finally
        {
            try{writer.Write("\x1b[0m\x1b[?7h\x1b[?25h\x1b[?1049l");writer.Flush();}
            finally{RestoreMode();}
        }
    }
    private void RestoreMode(){if(changedMode){SetConsoleMode(handle,originalMode);changedMode=false;}}
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nint GetStdHandle(int number);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetConsoleMode(nint handle,out uint mode);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetConsoleMode(nint handle,uint mode);
}
