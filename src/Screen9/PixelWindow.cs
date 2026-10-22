using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Screen9;

/// <summary>Windows pixel presenter for Screen9 PCCanvas and SCREEN 0 text cells (VGA 8x16 font, 16 PC colors);
/// no interpreter or hardware emulation.</summary>
public sealed class PixelWindow : IPixelDisplay
{
    public readonly record struct Viewport(int X, int Y, int Width, int Height);
    public static Viewport Fit(int width, int height)
    {
        if(width <= 0 || height <= 0)return default;
        int w=Math.Min(width,(int)((long)height*4/3));
        int h=Math.Min(height,(int)((long)w*3/4));
        return new((width-w)/2,(height-h)/2,w,h);
    }
    /// <summary>Come <see cref="Fit"/> ma solo a multipli interi della sorgente (1x, 2x, 3x…): i pixel restano tutti uguali,
    /// il resto è nero. Se la finestra è più piccola della sorgente ricade su <see cref="Fit"/>.</summary>
    public static Viewport FitInteger(int width,int height,int sourceWidth,int sourceHeight)
    {
        if(width<=0 || height<=0 || sourceWidth<=0 || sourceHeight<=0)return default;
        int scale=Math.Min(width/sourceWidth,height/sourceHeight);
        if(scale<1)return Fit(width,height);
        int w=sourceWidth*scale,h=sourceHeight*scale;
        return new((width-w)/2,(height-h)/2,w,h);
    }
    public static byte[] Capture(PCCanvas canvas)
    {
        byte[] pixels=canvas.PixelSnapshot(canvas.VisualPage);
        var palette=Enumerable.Range(0,canvas.VideoMode.Colors).Select(i=>canvas.GetRgb(i).Packed).ToArray();
        byte[] rgb=new byte[checked(pixels.Length*4)];
        for(int i=0;i<pixels.Length;i++)
        {
            int color=palette[pixels[i]];
            rgb[i*4]=(byte)color;rgb[i*4+1]=(byte)(color>>8);rgb[i*4+2]=(byte)(color>>16);
        }
        return rgb;
    }
    /// <summary>Text mode as the VGA showed it: each cell drawn with the ROM 8x16 font, 80x25 cells = 640x400 pixels.</summary>
    public static (int Width,int Height,byte[] Pixels) Capture(Canvas cells)
    {
        var font=PCFont.Vga8x16;int width=cells.Width*8,height=cells.Height*font.Height;byte[] rgb=new byte[checked(width*height*4)];
        for(int row=0;row<cells.Height;row++)for(int column=0;column<cells.Width;column++)
        {
            var cell=cells[column,row];int code=cell.Character<256?cell.Character:'?';
            int ink=CgaPalette.Rgb(cell.Foreground).Packed,paper=CgaPalette.Rgb(cell.Background).Packed;
            for(int y=0;y<font.Height;y++)
            {
                byte bits=font.Row(code,y);int offset=((row*font.Height+y)*width+column*8)*4;
                for(int x=0;x<8;x++,offset+=4){int color=(bits&(0x80>>x))!=0?ink:paper;rgb[offset]=(byte)color;rgb[offset+1]=(byte)(color>>8);rgb[offset+2]=(byte)(color>>16);}
            }
        }
        return (width,height,rgb);
    }
    private sealed record Frame(int Width,int Height,byte[] Pixels);
    private readonly ConcurrentQueue<string> keys=new();
    private readonly Thread thread;
    private readonly WndProc procedure;
    private readonly TaskCompletionSource<nint> ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string className="Screen9Pixels_"+Guid.NewGuid().ToString("N");
    private readonly string title;
    private Frame? frame;
    private object? pending; // PCCanvas or text Canvas, whichever was presented last
    private long lastCapture;
    private nint window;
    private bool disposed,dirty,shown;
    private volatile bool closed;
    private readonly PixelWindowBackBuffer backBuffer=new();
    private readonly Func<long> ticks;
    private readonly int windowWidth,windowHeight;
    private static bool dpiAware;
    /// <summary>Senza, su uno schermo con scala al 125-200% Windows ingrandisce la finestra come bitmap (sfocata, pixel irregolari):
    /// dichiarata la consapevolezza del DPI, il client è in pixel fisici e la scala intera ha senso.</summary>
    private static void MakeDpiAware()
    {
        if(dpiAware)return;
        dpiAware=true;
        try{SetProcessDPIAware();}catch(EntryPointNotFoundException){}
    }
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    private Exception? renderFailure;
    private int captures,compositions;
    public bool Closed => closed;
    public int CaptureCount => Volatile.Read(ref captures);
    public int CompositionCount => Volatile.Read(ref compositions);
    /// <summary>Se true l'immagine è ingrandita solo di multipli interi (pixel tutti uguali, bande nere); di default riempie la
    /// finestra a 4:3, giusto per i modi EGA con pixel non quadrati.</summary>
    public bool IntegerScale { get => Volatile.Read(ref integerScale); set { Volatile.Write(ref integerScale,value); if(!closed)InvalidateRect(window,0,false); } }
    private bool integerScale;
    /// <param name="title">Titolo della finestra.</param>
    /// <param name="ticks">Orologio in millisecondi (iniettabile nelle prove).</param>
    /// <param name="windowWidth">Larghezza esterna iniziale della finestra, bordi compresi.</param>
    /// <param name="windowHeight">Altezza esterna iniziale, barra del titolo compresa.</param>
    public PixelWindow(string title="Screen9 (close to stop)",Func<long>? ticks=null,int windowWidth=1000,int windowHeight=780)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Pixel window requires Windows.");
        MakeDpiAware();
        this.title=title;this.windowWidth=windowWidth;this.windowHeight=windowHeight;
        this.ticks=ticks??(()=>Environment.TickCount64);
        procedure=WindowProc;
        thread=new Thread(MessageLoop){IsBackground=true,Name="Screen9 pixel window"};
        thread.Start();
        window=ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }
    public void Present(PCCanvas canvas)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        pending=canvas;dirty=true;Refresh();
    }
    public void Present(Canvas cells)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        pending=cells;dirty=true;Refresh();
    }
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(Volatile.Read(ref renderFailure) is { } failure)throw new IOException("Pixel presentation failed.",failure);
        if(Closed)throw new OperationCanceledException("Program window closed.");
        if(!dirty || pending is null)return;
        long now=ticks();
        if(frame is not null && now-lastCapture<33)return;
        if(pending is PCCanvas pc)Volatile.Write(ref frame,new(pc.Width,pc.Height,Capture(pc)));
        else{var (width,height,rgb)=Capture((Canvas)pending);Volatile.Write(ref frame,new(width,height,rgb));}
        lastCapture=now;dirty=false;Interlocked.Increment(ref captures);
        // The first ShowWindow of a process started hidden (e.g. without console window) uses SW_HIDE from STARTUPINFO: repeat it.
        if(!shown){ShowWindow(window,5);if(!IsWindowVisible(window))ShowWindow(window,5);shown=true;}
        InvalidateRect(window,0,false);
    }
    public string ReadKey()
    {
        Refresh();return keys.TryDequeue(out string? key)?key:"";
    }
    private void MessageLoop()
    {
        nint instance=GetModuleHandle(null);
        try
        {
            var wc=new WindowClass { Procedure=Marshal.GetFunctionPointerForDelegate(procedure),Instance=instance,ClassName=className,Cursor=LoadCursor(0,(nint)32512) };
            if(RegisterClass(ref wc)==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            nint hwnd=CreateWindowEx(0,className,title,0x00cf0000,100,100,windowWidth,windowHeight,0,0,instance,0);
            if(hwnd==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            ready.TrySetResult(hwnd);
            while(GetMessage(out var message,0,0,0)>0){TranslateMessage(ref message);DispatchMessage(ref message);}
        }
        catch(Exception e){ready.TrySetException(e);}
        finally{backBuffer.Dispose();UnregisterClass(className,instance);}
    }
    private nint WindowProc(nint hwnd,uint message,nuint wparam,nint lparam)
    {
        switch(message)
        {
            case 0x000f: // WM_PAINT: immutable latest snapshot, no interpreter thread access
                nint dc=BeginPaint(hwnd,out var paint);
                try
                {
                    GetClientRect(hwnd,out var rect);
                    var current=Volatile.Read(ref frame);
                    backBuffer.Present(dc,rect.Right,rect.Bottom,current?.Pixels,current?.Width??0,current?.Height??0,IntegerScale);
                    Volatile.Write(ref compositions,backBuffer.Compositions);
                }
                catch(Exception failure){Volatile.Write(ref renderFailure,failure);}
                finally{EndPaint(hwnd,ref paint);}
                return 0;
            case 0x0014: return 1; // WM_ERASEBKGND: the complete backbuffer includes the black bands
            case 0x0005: InvalidateRect(hwnd,0,false);return 0;
            case 0x0102:
                if(wparam<=char.MaxValue && keys.Count<65536)keys.Enqueue(((char)wparam).ToString());return 0;
            case 0x0100:
                int scan=(int)wparam switch {0x25=>75,0x26=>72,0x27=>77,0x28=>80,0x24=>71,0x23=>79,0x21=>73,0x22=>81,0x2d=>82,0x2e=>83,_=>0};
                if(scan!=0 && keys.Count<65536)keys.Enqueue("\0"+(char)scan);return 0;
            case 0x0010:closed=true;DestroyWindow(hwnd);return 0;
            case 0x0002:backBuffer.Dispose();PostQuitMessage(0);return 0;
            default:return DefWindowProc(hwnd,message,wparam,lparam);
        }
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        PostMessage(window,0x0010,0,0);thread.Join(TimeSpan.FromSeconds(3));GC.KeepAlive(procedure);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WndProc(nint hwnd,uint message,nuint wparam,nint lparam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct WindowClass
    {public uint Style;public nint Procedure;public int ClassExtra,WindowExtra;public nint Instance,Icon,Cursor,Background;public string? MenuName;public string ClassName;}
    [StructLayout(LayoutKind.Sequential)] private struct Message {public nint Window;public uint Id;public nuint WParam;public nint LParam;public uint Time;public int X,Y;public uint Private;}
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct Paint
    {public nint Dc;public int Erase;public Rect Rectangle;public int Restore,Update;public long Reserved1,Reserved2,Reserved3,Reserved4;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern ushort RegisterClass(ref WindowClass wc);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool UnregisterClass(string name,nint instance);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern nint CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int h,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll")] private static extern nint LoadCursor(nint instance,nint name);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd,int show);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd,nint rect,bool erase);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd,out Paint paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd,ref Paint paint);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetMessage(out Message msg,nint hwnd,uint min,uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message msg);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint DispatchMessage(ref Message msg);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd,uint msg,nuint wp,nint lp);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool PostMessage(nint hwnd,uint msg,nuint wp,nint lp);
}

/// <summary>UI-thread-owned GDI backbuffer. Never clears or stretches directly onto the visible window.</summary>
internal sealed class PixelWindowBackBuffer : IDisposable
{
    private nint memoryDc,bitmap,previousBitmap;
    private int width,height;
    private byte[]? composedPixels;
    private bool composed,composedInteger;
    public int Compositions { get; private set; }
    public void Present(nint target,int clientWidth,int clientHeight,byte[]? pixels,int sourceWidth,int sourceHeight,bool integerScale=false)
    {
        if(clientWidth<=0 || clientHeight<=0)return;
        if(memoryDc==0 || width!=clientWidth || height!=clientHeight)
        {
            Release();
            memoryDc=CreateCompatibleDC(target);
            if(memoryDc==0)throw new IOException("Cannot allocate graphics backbuffer DC.");
            bitmap=CreateCompatibleBitmap(target,clientWidth,clientHeight);
            if(bitmap==0){Release();throw new IOException("Cannot allocate graphics backbuffer bitmap.");}
            previousBitmap=SelectObject(memoryDc,bitmap);
            if(previousBitmap==0 || previousBitmap==(nint)(-1)){Release();throw new IOException("Cannot select graphics backbuffer.");}
            width=clientWidth;height=clientHeight;composed=false;
        }
        if(integerScale!=composedInteger){composed=false;composedInteger=integerScale;}
        if(!composed || !ReferenceEquals(composedPixels,pixels))
        {
            var bounds=new Rect { Right=width,Bottom=height };
            FillRect(memoryDc,ref bounds,GetStockObject(4));
            if(pixels is not null)
            {
                var fit=integerScale?PixelWindow.FitInteger(width,height,sourceWidth,sourceHeight):PixelWindow.Fit(width,height);
                if(fit.Width>0 && fit.Height>0)
                {
                    var header=new Header { Size=40,Width=sourceWidth,Height=-sourceHeight,Planes=1,BitCount=32 };
                    var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                    try
                    {
                        SetStretchBltMode(memoryDc,3);
                        int result=StretchDIBits(memoryDc,fit.X,fit.Y,fit.Width,fit.Height,0,0,sourceWidth,sourceHeight,pinned.AddrOfPinnedObject(),ref header,0,0x00cc0020);
                        if(result==-1)throw new IOException("Cannot compose PC framebuffer.");
                    }
                    finally{pinned.Free();}
                }
            }
            composedPixels=pixels;composed=true;Compositions++;
        }
        if(!BitBlt(target,0,0,width,height,memoryDc,0,0,0x00cc0020))throw new IOException("Cannot present graphics backbuffer.");
    }
    private void Release()
    {
        if(memoryDc!=0 && previousBitmap!=0 && previousBitmap!=(nint)(-1))SelectObject(memoryDc,previousBitmap);
        if(bitmap!=0)DeleteObject(bitmap);
        if(memoryDc!=0)DeleteDC(memoryDc);
        memoryDc=bitmap=previousBitmap=0;composedPixels=null;composed=false;
    }
    public void Dispose()=>Release();
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct Header
    {public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,ImageSize;public int XPixels,YPixels;public uint Colors,Important;}
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc,int width,int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target,int x,int y,int width,int height,nint source,int sx,int sy,uint operation);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectId);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc,ref Rect rect,nint brush);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc,int mode);
    [DllImport("gdi32.dll")] private static extern int StretchDIBits(nint dc,int x,int y,int w,int h,int sx,int sy,int sw,int sh,nint bits,ref Header header,uint usage,uint operation);
}
