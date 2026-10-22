using System.Diagnostics;
using System.Text;

namespace Screen9.Tests;

/// <summary>Il renderer ANSI viene verificato applicando il suo output a un terminale VT emulato:
/// dopo ogni frame lo schermo deve coincidere con la proiezione a mezzo blocco attesa.</summary>
public sealed class AnsiFrameRendererTests
{
    /// <summary>Sottoinsieme VT usato dal renderer: CUP, SGR 0/38;2/48;2, DECSC/DECRC, modo privato 2026.</summary>
    internal sealed class VtScreen(int columns, int rows)
    {
        public readonly (char Glyph, uint Fg, uint Bg)[] Cells = Enumerable.Repeat((' ', 0xFFFFFFu, 0u), columns * rows).ToArray();
        public int Row, Column, Synchronized;
        private int savedRow, savedColumn; private uint fg = 0xFFFFFF, bg;

        public void Apply(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\e') { Put(text[i]); continue; }
                if (text[i + 1] == '7') { (savedRow, savedColumn) = (Row, Column); i++; continue; }
                if (text[i + 1] == '8') { (Row, Column) = (savedRow, savedColumn); i++; continue; }
                Assert.Equal('[', text[i + 1]);
                int end = i + 2; while (!char.IsLetter(text[end])) end++;
                string args = text[(i + 2)..end]; char command = text[end]; i = end;
                if (args == "?2026") { Synchronized += command == 'h' ? 1 : -1; continue; }
                var numbers = args.Split(';').Select(int.Parse).ToArray();
                if (command == 'H') { Row = numbers[0] - 1; Column = numbers[1] - 1; continue; }
                Assert.Equal('m', command);
                for (int n = 0; n < numbers.Length; n++)
                {
                    if (numbers[n] == 0) { fg = 0xFFFFFF; bg = 0; continue; }
                    Assert.Equal(2, numbers[n + 1]);
                    uint rgb = (uint)(numbers[n + 2] << 16 | numbers[n + 3] << 8 | numbers[n + 4]);
                    if (numbers[n] == 38) fg = rgb; else { Assert.Equal(48, numbers[n]); bg = rgb; }
                    n += 4;
                }
            }
        }

        private void Put(char glyph)
        {
            Assert.InRange(Row, 0, rows - 1); Assert.InRange(Column, 0, columns - 1);
            Cells[Row * columns + Column] = (glyph, fg, bg); Column++;
        }
    }

    private static uint Rgb(PCCanvas pc, int x, int y) => (uint)pc.GetRgb(pc.Point(x, y, pc.VisualPage)).Packed;

    /// <summary>Proiezione attesa senza ManualPresent: VisualPage e palette correnti.</summary>
    private static void AssertScreen(VtScreen screen, PCCanvas pc, int columns, int rows, int x = 0, int y = 0, int screenColumns = 0)
    {
        screenColumns = screenColumns == 0 ? columns : screenColumns;
        for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
        {
            int px = column * pc.Width / columns;
            uint up = Rgb(pc, px, row * pc.Height / rows), down = Rgb(pc, px, (2 * row + 1) * pc.Height / (2 * rows));
            var cell = screen.Cells[(y + row) * screenColumns + x + column];
            Assert.Equal(down, cell.Bg);
            if (up == down) Assert.Equal(' ', cell.Glyph); else { Assert.Equal('▀', cell.Glyph); Assert.Equal(up, cell.Fg); }
        }
    }

    private static (AnsiFrameRenderer Renderer, StringWriter Writer, VtScreen Screen) Create(int columns, int rows)
    {
        var writer = new StringWriter(); return (new AnsiFrameRenderer(writer), writer, new VtScreen(columns, rows));
    }

    private static string Take(StringWriter writer) { string text = writer.ToString(); writer.GetStringBuilder().Clear(); return text; }

    [Fact]
    public void IncrementalFramesAlwaysReproduceTheHalfBlockProjection()
    {
        var pc = new PCCanvas(13); var (renderer, writer, screen) = Create(80, 25); var random = new Random(3);
        for (int frame = 0; frame < 30; frame++)
        {
            if (frame % 10 == 0) pc.ClearIndex(random.Next(256));
            pc.Ellipse(random.Next(320), random.Next(200), random.Next(1, 60), random.Next(1, 40), random.Next(256), true);
            pc.Line(random.Next(320), random.Next(200), random.Next(320), random.Next(200), random.Next(256));
            if (frame % 7 == 0) pc.SetDac(random.Next(256), random.Next(64), random.Next(64), random.Next(64));
            renderer.Render(pc, 80, 25); string text = Take(writer);
            screen.Apply(text); AssertScreen(screen, pc, 80, 25);
            Assert.Equal(0, screen.Synchronized); Assert.Equal((0, 0), (screen.Row, screen.Column)); // cursore ripristinato
            if (text.Length > 0) { Assert.StartsWith("\e[?2026h", text); Assert.EndsWith("\e[0m\e8\e[?2026l", text); }
        }
    }

    [Fact]
    public void IdenticalFrameWritesNothingAndSmallChangesWriteFewCells()
    {
        var pc = new PCCanvas(13); var (renderer, writer, _) = Create(160, 50);
        Assert.Equal(160 * 50, renderer.Render(pc, 160, 50)); Take(writer);
        Assert.Equal(0, renderer.Render(pc, 160, 50)); Assert.Equal("", writer.ToString());
        pc.PSet(100, 60, 14); Assert.Equal(1, renderer.Render(pc, 160, 50)); Assert.InRange(Take(writer).Length, 1, 80); // cornice ~24 + posizione + due colori truecolor
        pc.Box(0, 0, 320, 4, 4, true); Assert.Equal(160, renderer.Render(pc, 160, 50)); // una riga di celle
        pc.SetDac(9, 0, 0, 0); Assert.Equal(0, renderer.Render(pc, 160, 50)); // indice non usato
        pc.SetDac(4, 63, 63, 0); Assert.Equal(160, renderer.Render(pc, 160, 50)); // solo le celle con quell'indice
    }

    [Fact]
    public void OffsetTargetChangeAndInvalidateForceAFullFrame()
    {
        var pc = new PCCanvas(9); pc.Ellipse(320, 175, 200, 100, 12, true); var (renderer, writer, screen) = Create(100, 40);
        renderer.Render(pc, 60, 20, 30, 10); screen.Apply(Take(writer)); AssertScreen(screen, pc, 60, 20, 30, 10, 100);
        Assert.Equal(' ', screen.Cells[0].Glyph); Assert.Equal(0u, screen.Cells[9 * 100 + 29].Bg); // fuori dal target: intatto
        Assert.Equal(60 * 20, renderer.Render(pc, 60, 20, 31, 10)); Take(writer);
        Assert.Equal(0, renderer.Render(pc, 60, 20, 31, 10));
        renderer.Invalidate(); Assert.Equal(60 * 20, renderer.Render(pc, 60, 20, 31, 10));
        Assert.Equal(30 * 10, renderer.Render(pc, 30, 10, 31, 10));
    }

    [Fact]
    public void RendersTheVisibleFrameRespectingVisualPageAndManualPresent()
    {
        var pc = new PCCanvas(7); var (renderer, writer, screen) = Create(40, 25);
        pc.ActivePage = 1; pc.ClearIndex(14); Assert.Equal(40 * 25, renderer.Render(pc, 40, 25)); screen.Apply(Take(writer));
        Assert.All(screen.Cells, c => Assert.Equal(0u, c.Bg)); // VisualPage 0: nero
        pc.ManualPresent = true; pc.VisualPage = 1; Assert.Equal(0, renderer.Render(pc, 40, 25)); // flip non ancora presentato
        pc.Present(); renderer.Render(pc, 40, 25); screen.Apply(Take(writer)); AssertScreen(screen, pc, 40, 25);
        Assert.All(screen.Cells, c => Assert.Equal((uint)pc.GetRgb(14).Packed, c.Bg));
    }

    [Fact]
    public void All256VgaColorsAreEmittedExactly()
    {
        var pc = new PCCanvas(13); for (int i = 0; i < 256; i++) pc.Box(i % 16 * 20, i / 16 * 12, 20, 12, i, true);
        for (int i = 0; i < 256; i++) pc.SetDac(i, i % 64, (i * 5) % 64, (i * 11) % 64);
        var (renderer, writer, screen) = Create(16, 16); renderer.Render(pc, 16, 16); screen.Apply(Take(writer));
        var colors = screen.Cells.Select(c => c.Bg).ToHashSet(); Assert.Equal(Enumerable.Range(0, 256).Select(i => (uint)pc.GetRgb(i).Packed).ToHashSet().Count, colors.Count);
        AssertScreen(screen, pc, 16, 16);
    }

    [Fact]
    public void ArgumentsAreValidatedBeforeWriting()
    {
        var (renderer, writer, _) = Create(1, 1); var pc = new PCCanvas(13);
        Assert.Throws<ArgumentNullException>(() => new AnsiFrameRenderer(null!));
        Assert.Throws<ArgumentNullException>(() => renderer.Render(null!, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(pc, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(pc, 1, AnsiFrameRenderer.MaxRows + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(pc, 1, 1, -1));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => renderer.Render(pc, 1, 1, token: cts.Token)); Assert.Equal("", writer.ToString());
    }
}

[Collection(nameof(PerformanceCollection))] [Trait("Category", "Performance")]
public sealed class AnsiFrameRendererPerformanceTests
{
    /// <summary>Scrittore che conta i caratteri senza conservarli.</summary>
    private sealed class CountingWriter : TextWriter
    {
        public long Characters;
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => Characters++;
        public override void Write(ReadOnlySpan<char> buffer) => Characters += buffer.Length;
    }

    [Fact]
    public void Mode13AnimationDoesNotAllocateAndFitsInsideAFrameBudget()
    {
        var pc = new PCCanvas(13) { ManualPresent = true }; var writer = new CountingWriter(); var renderer = new AnsiFrameRenderer(writer); int tick = 0;
        void Step()
        {
            pc.ClearIndex(1); pc.Ellipse(tick % 320, 100, 30, 30, 14, true); pc.SetDac(14, tick % 64, 63, 0); tick++;
            pc.Present(); renderer.Render(pc, 320, 100);
        }
        for (int i = 0; i < 1000; i++) Step(); // oltre la ricompilazione a livelli del JIT
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 100; i++) Step();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        double best = double.MaxValue;
        for (int i = 0; i < 5; i++) { var watch = Stopwatch.StartNew(); Step(); best = Math.Min(best, watch.Elapsed.TotalMilliseconds); }
        Assert.InRange(best, 0, TimeBudget.Milliseconds(4));
    }
}
