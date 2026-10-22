namespace Screen9.Tests;

/// <summary>PCFont (ROM grezze, .fon OEM di Windows) e testo nei modi grafici in 2.3.0.</summary>
public sealed class FontTests
{
    private static PCFont Rom(int height, params (int Code, int Row, byte Bits)[] set)
    {
        var data = new byte[256 * height]; foreach (var (code, row, bits) in set) data[code * height + row] = bits; return PCFont.FromRom(data, height);
    }

    [Theory]
    [InlineData(1, 8, 40, 25)][InlineData(2, 8, 80, 25)][InlineData(7, 8, 40, 25)][InlineData(8, 8, 80, 25)]
    [InlineData(9, 14, 80, 25)][InlineData(11, 16, 80, 30)][InlineData(12, 16, 80, 30)][InlineData(13, 8, 40, 25)]
    public void VideoModesExposeTheBiosTextGrid(int mode, int height, int columns, int rows)
    {
        var m = PCVideoMode.FromNumber(mode); Assert.Equal((height, columns, rows), (m.CharacterHeight, m.TextColumns, m.TextRows));
        Assert.Same(PCFont.ForHeight(height), m.Font); Assert.Equal(height, m.Font.Height);
    }

    [Fact]
    public void IncludedRomFontsHaveTheIbmGlyphs()
    {
        // Righe note delle ROM: 8x8 'A' riga 0 "..##....", EGA 8x14 'A' riga 2 "...#....", VGA 8x16 'g' riga 14 ".####...".
        Assert.Equal(0x30, PCFont.Ega8x8.Row('A', 0)); Assert.Equal(0x10, PCFont.Ega8x14.Row('A', 2)); Assert.Equal(0x78, PCFont.Vga8x16.Row('g', 14));
        foreach (var font in new[] { PCFont.Ega8x8, PCFont.Ega8x14, PCFont.Vga8x16 })
        {
            Assert.Equal(0xFF, font.Row(0xDB, font.Height / 2)); Assert.Equal(0, font.Row(' ', font.Height / 2));
            Assert.All(Enumerable.Range(0, font.Height), y => Assert.NotEqual(0, font.Row(0xBA, y))); // doppia verticale continua
        }
        Assert.Same(PCFont.Vga8x16, PCFont.ForHeight(16)); Assert.Throws<ArgumentOutOfRangeException>(() => PCFont.ForHeight(12));
        var pc = new PCCanvas(12); pc.DrawGlyph(0, 0, 'g', 15, 0, pc.VideoMode.Font); Assert.Equal(15, pc.Point(1, 14)); Assert.Equal(0, pc.Point(0, 14));
    }

    [Fact]
    public void RomRowsAreMsbFirstAndInvalidDumpsAreRejected()
    {
        var font = Rom(8, ('A', 2, 0b1000_0001));
        Assert.True(font.Pixel('A', 0, 2)); Assert.True(font.Pixel('A', 7, 2)); Assert.False(font.Pixel('A', 1, 2)); Assert.False(font.Pixel('A', 8, 2));
        Assert.Equal(0b1000_0001, font.Row('A', 2)); Assert.Throws<ArgumentOutOfRangeException>(() => font.Row(256, 0));
        Assert.Throws<ArgumentException>(() => PCFont.FromRom(new byte[100], 8)); Assert.Throws<ArgumentException>(() => PCFont.FromRom(new byte[256 * 33], 33));
        Assert.Null(PCFont.TryParseFon(new byte[10])); Assert.Null(PCFont.TryParseFnt(new byte[200]));
        Assert.Null(PCFont.TryParseFon([(byte)'M', (byte)'Z', .. new byte[0x80]]));
    }

    [Fact]
    public void PaddingCentersTextButExtendsLineDrawingGlyphs()
    {
        var font = Rom(12, ('A', 0, 0xFF), (0xB3, 0, 0x18), (0xB3, 11, 0x18)).Padded(16);
        Assert.Equal(16, font.Height); Assert.Equal(0, font.Row('A', 0)); Assert.Equal(0xFF, font.Row('A', 2));
        Assert.All(Enumerable.Range(0, 16).Where(y => y is < 2 or > 13), y => Assert.Equal(0x18, font.Row(0xB3, y)));
        Assert.Same(font, font.Padded(14));
    }

    [Fact]
    public void FindPrefersRomDumpsPerFolderAndCaches()
    {
        var empty = Directory.CreateTempSubdirectory(); var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(dir.FullName, "ibm8x14.rom"), new byte[256 * 14]);
            var font = PCFont.Find(14, empty.FullName, null, dir.FullName); Assert.NotNull(font); Assert.Equal(14, font.Height);
            Assert.Same(font, PCFont.Find(14, dir.FullName)); Assert.Null(PCFont.Find(16, dir.FullName)); Assert.Null(PCFont.Find(12, dir.FullName));
        }
        finally { empty.Delete(true); dir.Delete(true); }
    }

    [Theory]
    [InlineData(8, "cga80woa.fon")][InlineData(14, "ega80woa.fon")][InlineData(16, "vgaoem.fon")]
    public void WindowsOemFontsLoadWhenInstalled(int height, string file)
    {
        string fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        if (!File.Exists(Path.Combine(fonts, file))) return; // non Windows o font non installati
        var font = PCFont.Find(height, fonts); Assert.NotNull(font); Assert.Equal(height, font.Height);
        Assert.Equal(0xFF, font.Row(0xDB, height / 2));                                          // blocco pieno
        Assert.All(Enumerable.Range(0, height), y => Assert.NotEqual(0, font.Row(0xBA, y)));      // doppia verticale continua
        Assert.Contains(Enumerable.Range(0, height), y => font.Row('a', y) != 0);                 // minuscole presenti
    }

    [Fact]
    public void DrawGlyphWritesCellPixelsAndVideoMemory()
    {
        var pc = new PCCanvas(12); var font = Rom(16, ('A', 3, 0b1100_0000));
        pc.DrawGlyph(2, 1, 'A', 14, 1, font);
        Assert.Equal(14, pc.Point(16, 19)); Assert.Equal(14, pc.Point(17, 19)); Assert.Equal(1, pc.Point(18, 19)); Assert.Equal(1, pc.Point(16, 16));
        Assert.Equal(0, pc.Point(15, 19)); Assert.Equal(0, pc.Point(16, 32));
        pc.WritePort(0x3CE, 4); pc.WritePort(0x3CF, 1); Assert.Equal(0b1100_0000, pc.ReadVideoMemory(19 * 80 + 2)); // piano 1: 14 = 1110b
        pc.DrawGlyph(79, 29, 'A', 1, 2, font); pc.DrawGlyph(80, 30, 'A', 1, 2, font); // ultima cella e fuori schermo: nessuna eccezione
        Assert.Throws<ArgumentOutOfRangeException>(() => pc.DrawGlyph(0, 0, 256, 1, 0, font)); Assert.Throws<ArgumentOutOfRangeException>(() => pc.DrawGlyph(0, 0, 'A', 16, 0, font));
    }

    [Fact]
    public void ScrollUpMovesOnlyTheBand()
    {
        var pc = new PCCanvas(13); for (int y = 0; y < 6; y++) pc.PSet(0, y, y + 1);
        pc.ScrollUp(1, 4, 2, 9);
        Assert.Equal(new[] { 1, 4, 5, 9, 9, 6 }, Enumerable.Range(0, 6).Select(y => pc.Point(0, y)).ToArray());
        Assert.Equal(4, pc.ReadVideoMemory(320));
        Assert.Throws<ArgumentOutOfRangeException>(() => pc.ScrollUp(190, 20, 1, 0));
        pc.ScrollUp(0, 3, 10, 7); Assert.All(Enumerable.Range(0, 3), y => Assert.Equal(7, pc.Point(0, y)));
    }
}
