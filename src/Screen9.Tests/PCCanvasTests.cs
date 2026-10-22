namespace Screen9.Tests;

public sealed class PCCanvasTests
{
    [Theory]
    [InlineData(1,320,200,4,1)] [InlineData(2,640,200,2,1)]
    [InlineData(7,320,200,16,8)] [InlineData(8,640,200,16,4)] [InlineData(9,640,350,16,2)]
    [InlineData(11,640,480,2,1)] [InlineData(12,640,480,16,1)] [InlineData(13,320,200,256,1)]
    public void ModeDimensionsDepthPagesAndInheritance(int mode,int width,int height,int colors,int pages)
    {
        var pc = new PCCanvas(mode);
        Assert.Equal((width,height,colors,pages),(pc.Width,pc.Height,pc.ColorCount,pc.PageCount));
        Assert.Equal(0,pc.Point(0,0)); Assert.Equal(-1,pc.Point(-1,0));
    }
    [Theory] [InlineData(0)] [InlineData(3)] [InlineData(10)] [InlineData(99)]
    public void UnsupportedModesAreRejected(int mode) => Assert.Throws<ArgumentOutOfRangeException>(()=>new PCCanvas(mode));
    [Fact]
    public void CgaFamiliesIntensityBackgroundAndMonochromePalette()
    {
        var pc=new PCCanvas(1); Assert.Equal(new PCRgb(0,170,170),pc.GetRgb(1));
        pc.SetCgaPalette(0,true,4); Assert.Equal(new PCRgb(170,0,0),pc.GetRgb(0)); Assert.Equal(new PCRgb(85,255,85),pc.GetRgb(1));
        Assert.Equal(new PCRgb(255,255,85),pc.GetRgb(3)); pc.PSet(0,0,2); pc.SetCgaPalette(1); Assert.Equal(2,pc.Point(0,0));
        var mono=new PCCanvas(2); Assert.Equal(new PCRgb(255,255,255),mono.GetRgb(1)); mono.SetPalette(1,4); Assert.Equal(new PCRgb(170,0,0),mono.GetRgb(1));
        Assert.Throws<InvalidOperationException>(()=>mono.SetCgaPalette(0));
    }
    [Fact]
    public void EgaHardwarePaletteAndVgaDacPreservePixelIndices()
    {
        var ega=new PCCanvas(9); ega.PSet(0,0,6); ega.SetPalette(6,20); Assert.Equal(new PCRgb(170,85,0),ega.GetRgb(6));
        ega.SetPalette(6,63); Assert.Equal(new PCRgb(255,255,255),ega.GetRgb(6)); Assert.Equal(6,ega.Point(0,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ega.SetPalette(0,64)); Assert.Throws<InvalidOperationException>(()=>ega.SetDac(0,1,2,3));
        var vga=new PCCanvas(13); vga.PSet(0,0,255); vga.SetDac(255,63,0,21); Assert.Equal(new PCRgb(255,0,85),vga.GetRgb(255));
        Assert.Equal(255,vga.Point(0,0)); Assert.Throws<ArgumentOutOfRangeException>(()=>vga.SetDac(255,64,0,0)); Assert.Throws<InvalidOperationException>(()=>vga.SetPalette(0,1));
    }
    [Fact]
    public void IndexedWritesValidateBeforeMutationAndPCCanvasIsNotACellCanvas()
    {
        var pc=new PCCanvas(1); Assert.False(pc.PSet(-1,0,1)); Assert.Throws<ArgumentOutOfRangeException>(()=>pc.PSet(0,0,4)); Assert.Equal(0,pc.Point(0,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>pc.Box(0,0,-1,1,1)); Assert.Throws<ArgumentOutOfRangeException>(()=>pc.Ellipse(0,0,-1,1,1)); Assert.Equal(0,pc.Point(0,0));
        Assert.False(typeof(Canvas).IsAssignableFrom(typeof(PCCanvas))); // 2.0: composizione, la proiezione a celle è esplicita
    }
    [Fact]
    public void CgaInterleavedMemoryEncodes2BppAnd1BppAndPadding()
    {
        var pc=new PCCanvas(1); pc.PSet(0,0,3); pc.PSet(1,0,1); pc.PSet(0,1,2);
        Assert.Equal(0xD0,pc.ReadByte(0)); Assert.Equal(0x80,pc.ReadByte(8192));
        pc.WriteByte(0,0x1B); Assert.Equal(new[]{0,1,2,3},Enumerable.Range(0,4).Select(x=>pc.Point(x,0)).ToArray());
        var before=pc.PixelSnapshot(); pc.WriteByte(8100,99); Assert.Equal(99,pc.ReadByte(8100)); Assert.Equal(before,pc.PixelSnapshot());
        var mono=new PCCanvas(2); mono.PSet(0,1,1); Assert.Equal(128,mono.ReadByte(8192)); mono.WriteByte(0,0xA0); Assert.Equal(1,mono.Point(2,0)); Assert.Equal(0,mono.Point(1,0));
    }
    [Fact]
    public void PlanarMemoryAndIndependentActiveVisualPages()
    {
        var pc=new PCCanvas(7); pc.PSet(0,0,15); for(int p=0;p<4;p++)Assert.Equal(128,pc.ReadByte(0,p));
        pc.ActivePage=1; Assert.Equal(0,pc.Point(0,0)); pc.PSet(0,0,5); Assert.Equal(128,pc.ReadByte(8000,0)); Assert.Equal(0,pc.ReadByte(8000,1));
        Assert.Equal(15,pc.Point(0,0,0)); Assert.Equal(ConsoleColor.White,pc.ToCellCanvas(1,1)[0,0].Background);
        pc.VisualPage=1; Assert.Equal(ConsoleColor.DarkMagenta,pc.ToCellCanvas(1,1)[0,0].Background);
        pc.WriteByte(0,0,3); Assert.Equal(7,pc.Point(0,0,0)); Assert.Equal(5,pc.Point(0,0,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>pc.ActivePage=8); Assert.Throws<ArgumentOutOfRangeException>(()=>pc.ReadByte(65536)); Assert.Throws<ArgumentOutOfRangeException>(()=>pc.ReadByte(0,4));
    }
    [Fact]
    public void VgaLinearMemoryAndSnapshotIsolation()
    {
        var pc=new PCCanvas(13); pc.PSet(319,199,255); Assert.Equal(255,pc.ReadByte(63999));
        pc.WriteByte(320,77); Assert.Equal(77,pc.Point(0,1)); pc.WriteByte(65000,99); Assert.Equal(99,pc.ReadByte(65000));
        var snapshot=pc.PixelSnapshot(); snapshot[320]=0; Assert.Equal(77,pc.Point(0,1)); Assert.Throws<ArgumentOutOfRangeException>(()=>pc.PSet(0,0,256));
    }
    [Fact]
    public void ClippedLinesBoxesEllipsesCirclesAndArcsAreBounded()
    {
        var pc=new PCCanvas(9); pc.Line(int.MinValue,2,int.MaxValue,2,1); Assert.Equal(1,pc.Point(0,2)); Assert.Equal(1,pc.Point(639,2));
        pc.Box(10,10,10,10,2,true); Assert.Equal(2,pc.Point(15,15));
        pc.Ellipse(30,30,5,3,3,true); Assert.Equal(3,pc.Point(30,30));
        pc.Circle(50,50,5,4); Assert.Equal(4,pc.Point(55,50)); Assert.Equal(0,pc.Point(50,50));
        pc.Circle(80,80,5,5,0,Math.PI/2); Assert.Equal(5,pc.Point(85,80)); Assert.Equal(5,pc.Point(80,75)); Assert.Equal(0,pc.Point(75,80));
        pc.Circle(100,100,5,6,Math.PI*1.5,Math.PI/2); Assert.Equal(6,pc.Point(105,100)); Assert.Equal(0,pc.Point(95,100));
        Assert.Throws<ArgumentOutOfRangeException>(()=>pc.Circle(0,0,double.NaN,1)); Assert.Throws<ArgumentOutOfRangeException>(()=>pc.Circle(0,0,1,1,-1,0));
    }
    [Fact]
    public void PaintRespectsBorderRegionsAndCancellation()
    {
        var pc=new PCCanvas(9); pc.Box(10,10,5,5,1); Assert.Equal(9,pc.Paint(12,12,2,1)); Assert.Equal(2,pc.Point(12,12)); Assert.Equal(0,pc.Point(9,12));
        Assert.Equal(9,pc.Paint(12,12,3)); Assert.Equal(0,pc.Paint(-1,0,3));
        using var cts=new CancellationTokenSource(); cts.Cancel(); Assert.ThrowsAny<OperationCanceledException>(()=>pc.Paint(0,0,4,token:cts.Token)); Assert.Equal(0,pc.Point(0,0));
    }
    [Theory]
    [InlineData(PCPixelOperation.Copy,3)] [InlineData(PCPixelOperation.Preset,12)] [InlineData(PCPixelOperation.Xor,6)] [InlineData(PCPixelOperation.And,1)] [InlineData(PCPixelOperation.Or,7)]
    public void SpriteRasterOperations(PCPixelOperation operation,int expected)
    {
        var pc=new PCCanvas(9); pc.PSet(0,0,3); var image=pc.GetImage(0,0,1,1); pc.PSet(1,1,5); pc.PutImage(image,1,1,operation); Assert.Equal(expected,pc.Point(1,1));
    }
    [Fact]
    public void XorTwiceRestoresPixelsAndSpriteClippingTransparencyAndDepthAreStrict()
    {
        var pc=new PCCanvas(9); pc.Box(0,0,2,2,3,true); var image=pc.GetImage(0,0,2,2); pc.ClearIndex(5); var before=pc.PixelSnapshot();
        pc.PutImage(image,10,10,PCPixelOperation.Xor); pc.PutImage(image,10,10,PCPixelOperation.Xor); Assert.Equal(before,pc.PixelSnapshot());
        pc.PutImage(image,-1,-1); Assert.Equal(3,pc.Point(0,0)); Assert.Equal(5,pc.Point(1,1));
        pc.PutImage(image,1,1,transparent:3); Assert.Equal(5,pc.Point(1,1)); pc.PutImage(image,int.MaxValue,int.MinValue);
        var snapshot=image.Snapshot(); snapshot[0]=15; Assert.Equal(3,image[0,0]);
        Assert.Throws<ArgumentOutOfRangeException>(()=>pc.GetImage(-1,0,1,1)); Assert.Throws<ArgumentException>(()=>pc.PutImage(new PCCanvas(13).GetImage(0,0,1,1),0,0));
    }
    [Fact]
    public void ProjectionIsIndependentBoundedAndUsesCurrentPalette()
    {
        var pc=new PCCanvas(13); pc.ClearIndex(42); pc.SetDac(42,63,21,21); var frame=pc.ToCellCanvas(20,10); Assert.Equal(ConsoleColor.Red,frame[0,0].Background);
        frame.Clear(); Assert.Equal(42,pc.Point(0,0)); Assert.Throws<ArgumentOutOfRangeException>(()=>pc.ToCellCanvas(301,1));
        using var cts=new CancellationTokenSource(); cts.Cancel(); Assert.ThrowsAny<OperationCanceledException>(()=>pc.ToCellCanvas(1,1,cts.Token));
    }
}
