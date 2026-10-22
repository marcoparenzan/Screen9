namespace Screen9.Tests;

/// <summary>Celle in truecolor con la palette CGA esatta, verificate su un terminale VT emulato.</summary>
public sealed class AnsiCanvasRendererTests
{
    private static uint Rgb(ConsoleColor color) => (uint)CgaPalette.Rgb(color).Packed;

    [Fact]
    public void PaletteIsExactCga()
    {
        Assert.Equal(0x0000AA, CgaPalette.Rgb(ConsoleColor.DarkBlue).Packed);
        Assert.Equal(0xAA5500, CgaPalette.Rgb(ConsoleColor.DarkYellow).Packed);
        Assert.Equal(0xAAAAAA, CgaPalette.Rgb(ConsoleColor.Gray).Packed);
        Assert.Equal(0xFFFF55, CgaPalette.Rgb(ConsoleColor.Yellow).Packed);
    }

    [Fact]
    public void CellsUseCgaRgbAndOnlyChangesAreRewritten()
    {
        var canvas = new Canvas(6, 2);
        canvas.Clear(new Cell(' ', ConsoleColor.Gray, ConsoleColor.DarkBlue));
        canvas.DrawText(0, 0, "PRINT", ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        canvas.SetPixel(5, 1, new Cell('█', ConsoleColor.DarkYellow, ConsoleColor.Black));
        var writer = new StringWriter(); var renderer = new AnsiCanvasRenderer(writer, () => new(6, 2));
        var screen = new AnsiFrameRendererTests.VtScreen(6, 2);
        Assert.Equal(12, renderer.Write(canvas)); screen.Apply(writer.ToString());
        Assert.Equal(0, screen.Synchronized);
        for (int y = 0; y < 2; y++) for (int x = 0; x < 6; x++)
        {
            var cell = canvas[x, y]; var shown = screen.Cells[y * 6 + x];
            Assert.Equal((cell.Character, Rgb(cell.Foreground), Rgb(cell.Background)), (shown.Glyph, shown.Fg, shown.Bg));
        }
        writer.GetStringBuilder().Clear();
        Assert.Equal(0, renderer.Write(canvas)); Assert.Equal("", writer.ToString());
        canvas.SetPixel(2, 1, new Cell('x', ConsoleColor.White, ConsoleColor.Red));
        Assert.Equal(1, renderer.Write(canvas)); screen.Apply(writer.ToString());
        Assert.Equal(('x', 0xFFFFFFu, 0xFF5555u), screen.Cells[1 * 6 + 2]);
        writer.GetStringBuilder().Clear(); renderer.Invalidate();
        Assert.Equal(12, renderer.Write(canvas));
    }

    [Fact]
    public void OffsetOrSizeChangeRewritesTheWholeFrame()
    {
        var canvas = new Canvas(3, 1); canvas.DrawText(0, 0, "abc", ConsoleColor.Gray);
        var writer = new StringWriter(); var renderer = new AnsiCanvasRenderer(writer);
        Assert.Equal(3, renderer.Write(canvas));
        writer.GetStringBuilder().Clear();
        Assert.Equal(3, renderer.Write(canvas, 2, 1));
        var screen = new AnsiFrameRendererTests.VtScreen(5, 2); screen.Apply(writer.ToString());
        Assert.Equal("abc", new string([screen.Cells[7].Glyph, screen.Cells[8].Glyph, screen.Cells[9].Glyph]));
        Assert.Equal(3, renderer.Write(new Canvas(3, 1)));
    }
}
