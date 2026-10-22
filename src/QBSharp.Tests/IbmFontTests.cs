using Screen9;
using QBSharp.QBasic;

namespace QBSharp.Tests;

/// <summary>Integrazione QBasic del font IBM di Screen9 (PCFont/DrawGlyph). Parser e ricerca dei font sono testati in Screen9.</summary>
public sealed class IbmFontTests
{
    private static async Task<PCCanvas> Run(string source,QBasicOptions options)
    {
        PCCanvas? frame=null;
        await using var s=new QBasicEngine(new(){PresentGraphics=c=>frame=c,RasterFont=options.RasterFont,FontDirectory=options.FontDirectory}).CreateSession();
        await s.ExecuteAsync(new(source));Assert.NotNull(frame);return frame;
    }
    private static DirectoryInfo RomFolder(int height,params (int Code,int Row,byte Bits)[] set)
    {
        var dir=Directory.CreateTempSubdirectory();var rom=new byte[256*height];foreach(var (code,row,bits) in set)rom[code*height+row]=bits;
        File.WriteAllBytes(Path.Combine(dir.FullName,$"ibm8x{height}.rom"),rom);return dir;
    }
    [Fact]
    public async Task FontDirectoryRomDrivesPrintInGraphicsModes()
    {
        var dir=RomFolder(14,('a',0,0x80));
        try
        {
            var frame=await Run("SCREEN 9\nCOLOR 1,0\nLOCATE 2,2\nPRINT \"a\";",new(){FontDirectory=dir.FullName});
            Assert.Equal(1,frame.Point(8,14));Assert.Equal(0,frame.Point(9,14));Assert.Equal(0,frame.Point(8,15));
        }
        finally{dir.Delete(true);}
    }
    [Fact]
    public async Task ControlCodesUseCp437SymbolsInRasterButInterpretedOnesDoNot()
    {
        var dir=RomFolder(16,(1,0,0xFF),('?',15,0xFF));
        try
        {
            var frame=await Run("SCREEN 12\nCOLOR 1,0\nPRINT CHR$(1);CHR$(7);",new(){FontDirectory=dir.FullName});
            Assert.Equal(1,frame.Point(0,0));Assert.Equal(0,frame.Point(0,15));Assert.Equal(0,frame.Point(8,0));Assert.Equal(1,frame.Point(8,15));
        }
        finally{dir.Delete(true);}
    }
    [Fact]
    public async Task ScrollInsideViewPrintUsesDeviceScroll()
    {
        var dir=RomFolder(8,('X',0,0x80));
        try
        {
            var frame=await Run("SCREEN 13\nCOLOR 1,0\nLOCATE 1,1\nPRINT \"X\";\nVIEW PRINT 3 TO 4\nPRINT \"\"\nPRINT \"X\"",new(){FontDirectory=dir.FullName});
            Assert.Equal(1,frame.Point(0,0));Assert.Equal(1,frame.Point(0,16));Assert.Equal(0,frame.Point(0,24));
        }
        finally{dir.Delete(true);}
    }
    [Fact]
    public async Task EmbeddedRomFontsMatchEgaAndVgaGlyphs()
    {
        // EGA 8x14 'A': riga 2 "...#....". VGA 8x16 'g': discendente alla riga 14 ".####...".
        var ega=await Run("SCREEN 9\nCOLOR 1,0\nPRINT \"A\";",new());
        Assert.Equal(1,ega.Point(3,2));Assert.Equal(0,ega.Point(2,2));Assert.Equal(0,ega.Point(3,1));
        var vga=await Run("SCREEN 12\nCOLOR 1,0\nPRINT \"g\";",new());
        Assert.Equal(1,vga.Point(1,14));Assert.Equal(0,vga.Point(0,14));Assert.All(Enumerable.Range(0,8),x=>Assert.Equal(0,vga.Point(x,0)));
        var cga=await Run("SCREEN 13\nCOLOR 1,0\nPRINT \"A\";",new());
        Assert.Equal(1,cga.Point(2,0));Assert.Equal(0,cga.Point(1,0));
    }
    [Fact]
    public async Task UnusualCellHeightsFallBackToCompactFont()
    {
        // SCREEN 12 con 40 righe: celle 8x12, nessuna ROM IBM di quell'altezza.
        var frame=await Run("SCREEN 12\nWIDTH 80,40\nCOLOR 1,0\nPRINT \"A\";",new());
        Assert.Contains(frame.PixelSnapshot(),v=>v==1);
    }
    [Fact]
    public async Task CompactFallbackIgnoresIbmFonts()
    {
        var dir=RomFolder(16);
        try
        {
            var ibm=await Run("SCREEN 12\nCOLOR 1,0\nPRINT \"A\";",new(){FontDirectory=dir.FullName});
            var compact=await Run("SCREEN 12\nCOLOR 1,0\nPRINT \"A\";",new(){FontDirectory=dir.FullName,RasterFont=QBasicRasterFont.Compact});
            Assert.DoesNotContain(ibm.PixelSnapshot(),v=>v!=0);Assert.Contains(compact.PixelSnapshot(),v=>v==1);
        }
        finally{dir.Delete(true);}
    }
}
