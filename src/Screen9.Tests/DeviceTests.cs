using System.Reflection;
using System.Runtime.InteropServices;

namespace Screen9.Tests;

/// <summary>Dispositivi scorporati da QBSharp: finestra a pixel, testo raster, tastiera, PLAY e altoparlante, sprite GET/PUT.</summary>
public sealed class DeviceTests
{
    [Theory]
    [InlineData(1000,500)] [InlineData(500,1000)] [InlineData(640,480)] [InlineData(1,1)]
    public void WholeImageFitsWithoutClipping(int width,int height)
    {
        var fit=PixelWindow.Fit(width,height);
        Assert.InRange(fit.X,0,width);Assert.InRange(fit.Y,0,height);
        Assert.InRange(fit.X+fit.Width,0,width);Assert.InRange(fit.Y+fit.Height,0,height);
        if(fit.Width>3)Assert.InRange(Math.Abs(fit.Width*3-fit.Height*4),0,4);
    }
    [Theory]
    [InlineData(1230, 830, 640, 480, 640, 480)] [InlineData(1280, 960, 640, 480, 1280, 960)] [InlineData(1000, 2000, 320, 200, 960, 600)]
    [InlineData(500, 400, 640, 480, 500, 375)] // più piccola della sorgente: ricade sull'adattamento a 4:3
    public void IntegerFitUsesWholeMultiplesCentersAndFallsBackWhenSmaller(int width, int height, int sourceWidth, int sourceHeight, int expectedWidth, int expectedHeight)
    {
        var fit = PixelWindow.FitInteger(width, height, sourceWidth, sourceHeight);
        Assert.Equal((expectedWidth, expectedHeight), (fit.Width, fit.Height));
        Assert.Equal(((width - expectedWidth) / 2, (height - expectedHeight) / 2), (fit.X, fit.Y));
        if (width >= sourceWidth && height >= sourceHeight) Assert.Equal(0, fit.Width % sourceWidth);
    }
    [Fact]
    public void SnapshotHasNativeSizeVisiblePageAndBgrPalette()
    {
        var pc=new PCCanvas(7);pc.PSet(0,0,1);pc.ActivePage=1;pc.VisualPage=1;pc.PSet(0,0,4);
        var image=PixelWindow.Capture(pc);int rgb=pc.GetRgb(4).Packed;
        Assert.Equal(pc.Width*pc.Height*4,image.Length);
        Assert.Equal((byte)rgb,image[0]);Assert.Equal((byte)(rgb>>8),image[1]);Assert.Equal((byte)(rgb>>16),image[2]);
        pc.PSet(0,0,2);Assert.Equal((byte)(rgb>>16),image[2]);
    }
    [Fact]
    public void TextCaptureUsesVgaFontAt640x400()
    {
        var cells=new Canvas(80,25);cells.SetPixel(0,0,new('g',ConsoleColor.Yellow,ConsoleColor.DarkBlue));
        var (width,height,rgb)=PixelWindow.Capture(cells);
        Assert.Equal((640,400),(width,height));
        int Pixel(int x,int y){int i=(y*width+x)*4;return rgb[i+2]<<16|rgb[i+1]<<8|rgb[i];}
        Assert.Equal(0xFFFF55,Pixel(1,14));Assert.Equal(0x0000AA,Pixel(0,14));Assert.Equal(0x000000,Pixel(8,0));
    }
    [Fact]
    public void NativeWindowCanReceiveFrameResizeAndClose()
    {
        if(!OperatingSystem.IsWindows())return;
        using var window=new PixelWindow();
        window.Present(new PCCanvas(13));
        Assert.False(window.Closed);
        window.Dispose();Assert.True(window.Closed);
    }
    [Fact]
    public void IdleRefreshDoesNotCaptureAndDirtyFrameFlushesOnce()
    {
        if(!OperatingSystem.IsWindows())return;
        long ticks=100;
        using var window=new PixelWindow(ticks:()=>ticks);
        var pc=new PCCanvas(9);window.Present(pc);Assert.Equal(1,window.CaptureCount);
        for(int i=0;i<200;i++){ticks+=33;window.Refresh();}
        Assert.Equal(1,window.CaptureCount);
        pc.PSet(0,0,4);window.Present(pc);Assert.Equal(2,window.CaptureCount);
        pc.PSet(0,0,2);window.Present(pc);Assert.Equal(2,window.CaptureCount);
        ticks+=33;window.Refresh();Assert.Equal(3,window.CaptureCount);
        window.Refresh();Assert.Equal(3,window.CaptureCount);
    }
    [Fact]
    public void NativeBackbufferDisplaysPixelsReusesExposeAndRebuildsOnResize()
    {
        if(!OperatingSystem.IsWindows())return;
        using var window=new PixelWindow();
        var pc=new PCCanvas(9);pc.ClearIndex(4);window.Present(pc);
        nint hwnd=(nint)typeof(PixelWindow).GetField("window",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        PaintNow(hwnd);window.Refresh();
        int first=window.CompositionCount;Assert.True(first>=1);
        PaintNow(hwnd);Assert.Equal(first,window.CompositionCount);
        Assert.True(SetWindowPos(hwnd,0,0,0,1000,400,0x0002|0x0004));
        Assert.True(SpinWait.SpinUntil(()=>{PaintNow(hwnd);return window.CompositionCount>first;},TimeSpan.FromSeconds(5)),"Resize must rebuild the backbuffer on the UI thread.");window.Refresh();
        Assert.True(GetClientRect(hwnd,out var rect));
        nint dc=GetDC(hwnd);
        try
        {
            int rgb=pc.GetRgb(4).Packed;
            uint expected=(uint)(((rgb>>16)&255)|(rgb&0xff00)|((rgb&255)<<16));
            Assert.Equal(expected,GetPixel(dc,rect.Right/2,rect.Bottom/2));
            Assert.Equal(0u,GetPixel(dc,0,rect.Bottom/2)); // letterbox, not clipped image
        }
        finally{ReleaseDC(hwnd,dc);}
        window.Dispose();Assert.True(window.Closed);
    }
    [Fact]
    public void TextDisplayUsesBuiltInRomWithoutFontDirectories()
    {
        // Senza cartelle non deve cercare nella cartella Fonts di Windows: il glifo è quello della ROM EGA 8x14 inclusa.
        var graphics=new PCCanvas(9);var text=new PCTextDisplay{UseIbmFonts=true};
        text.Write("A",15,0,graphics);
        var expected=new PCCanvas(9);expected.DrawGlyph(0,0,'A',15,0,PCFont.Ega8x14);
        for(int y=0;y<14;y++)for(int x=0;x<8;x++)Assert.Equal(expected.Point(x,y),graphics.Point(x,y));
        Assert.Equal('A',text.Cells[0,0].Character);Assert.Equal((1,2),(text.Row,text.Column));
    }
    [Theory]
    [InlineData(ConsoleKey.UpArrow,72)][InlineData(ConsoleKey.Delete,83)][InlineData(ConsoleKey.F1,59)][InlineData(ConsoleKey.F12,134)]
    public void KeyboardExtendedCodes(ConsoleKey key,int scan)=>Assert.Equal("\0"+(char)scan,PCKeyboard.Encode(new('\0',key,false,false,false)));
    [Theory]
    [InlineData("O9A")][InlineData("T0A")][InlineData("L0A")][InlineData("Xfoo")][InlineData("N85")]
    public void MusicRejectsUnsupportedOrInvalidCommands(string text)=>Assert.Throws<ArgumentException>(()=>new PCMusic().Parse(text));
    [Fact]
    public void MusicDottedRestsAccidentalsAndBackground()
    {
        var tones=new PCMusic().Parse("MBMLO4T120L4A.P8A+");Assert.Equal(3,tones.Count);
        Assert.Equal(.75,tones[0].Duration.TotalSeconds,3);Assert.Equal(0,tones[1].Frequency);Assert.True(tones.All(t=>t.Background));Assert.True(tones[2].Frequency>440);
    }
    [Fact]
    public async Task SpeakerPlaysBackgroundTonesInOrderAndDrains()
    {
        List<double> played=[];var speaker=new PCSpeaker((tone,_)=>{lock(played)played.Add(tone.Frequency);return ValueTask.CompletedTask;});
        try
        {
            foreach(var tone in new PCMusic().Parse("MBMLL64CDE"))await speaker.PlayAsync(tone,default);
            await speaker.DrainAsync(default);
            Assert.Equal(3,played.Count);Assert.True(played[0]<played[1]&&played[1]<played[2]);Assert.Equal(0,speaker.Notes);Assert.True(speaker.Idle);
        }
        finally{speaker.Stop();}
    }
    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(9)][InlineData(12)][InlineData(13)]
    public void SpriteCaptureAndXorRoundtrip(int mode)
    {
        var pc=new PCCanvas(mode);pc.Box(0,0,8,8,1,false);pc.PSet(3,3,1);
        var words=new int[201];words[200]=12345;
        int count=PCSprite.Capture(pc,words,0,0,7,7);
        Assert.InRange(count,2,200);Assert.Equal(12345,words[200]);Assert.Equal(8,words[0]>>16);
        PCSprite.Draw(pc,words,20,20);Assert.Equal(1,pc.Point(23,23));Assert.Equal(1,pc.Point(20,20));
        PCSprite.Draw(pc,words,20,20,PCSpriteOperation.Xor);Assert.Equal(0,pc.Point(23,23));
    }
    [Fact]
    public void ArrayOverloadsForReflectionHostsMatchSpans()
    {
        // Gli host di scripting (PySharp) legano per reflection: niente Span, quindi GET/PUT anche su int[].
        var pc=new PCCanvas(9);pc.Box(0,0,8,8,5,true);var words=new int[40];
        var capture=typeof(PCSprite).GetMethod(nameof(PCSprite.Capture),[typeof(PCCanvas),typeof(int[]),typeof(int),typeof(int),typeof(int),typeof(int)])!;
        Assert.Equal(PCSprite.Capture(pc,new int[40].AsSpan(),0,0,7,7),(int)capture.Invoke(null,[pc,words,0,0,7,7])!);
        var draw=typeof(PCSprite).GetMethod(nameof(PCSprite.Draw),[typeof(PCCanvas),typeof(int[]),typeof(int),typeof(int),typeof(PCSpriteOperation),typeof(CancellationToken)])!;
        draw.Invoke(null,[pc,words,30,30,PCSpriteOperation.PSet,CancellationToken.None]);Assert.Equal(5,pc.Point(37,37));
    }
    [Fact]
    public void OriginalGorillasBananaDecodesInCgaAndEga()
    {
        var cga=new PCCanvas(1);PCSprite.Draw(cga,[327686,-252645316,60],0,0,PCSpriteOperation.PSet);Assert.Equal(3,cga.Point(1,0));
        var ega=new PCCanvas(9);PCSprite.Draw(ega,[458758,202116096,471604224,943208448,943208448,943208448,471604224,202116096,0],0,0,PCSpriteOperation.PSet);
        Assert.Equal(14,ega.Point(4,0));
    }
    [Theory]
    [InlineData("pset",PCSpriteOperation.PSet)][InlineData("PRESET",PCSpriteOperation.PReset)][InlineData("Xor",PCSpriteOperation.Xor)][InlineData("AND",PCSpriteOperation.And)][InlineData("or",PCSpriteOperation.Or)]
    public void PutOperationNames(string name,PCSpriteOperation operation)=>Assert.Equal(operation,PCSprite.ParseOperation(name));
    [Fact]
    public void InvalidSpritesAreRejected()
    {
        var pc=new PCCanvas(9);
        Assert.Throws<ArgumentException>(()=>PCSprite.Capture(pc,new int[1],0,0,10,10));
        Assert.Throws<ArgumentException>(()=>PCSprite.Draw(pc,[0x00010008],0,0));
        Assert.Throws<ArgumentException>(()=>PCSprite.ParseOperation("NOT"));
    }
    private static void PaintNow(nint hwnd){InvalidateRect(hwnd,0,false);UpdateWindow(hwnd);}
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd,nint rect,bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc,int x,int y);
}
