using Microsoft.Extensions.DependencyInjection;

namespace Screen9.Tests;

public sealed class CanvasTests
{
    private static readonly Cell Ink = new('*', ConsoleColor.Yellow, ConsoleColor.Blue);

    [Fact]
    public void InitializationAndClear()
    {
        var canvas = new Canvas(4, 3);
        Assert.All(canvas.Snapshot(), c => Assert.Equal(Cell.Empty, c));
        canvas.Clear(Ink);
        Assert.All(canvas.Snapshot(), c => Assert.Equal(Ink, c));
        canvas.Clear();
        Assert.Equal(Cell.Empty, canvas[0, 0]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 2)]
    public void InvalidDimensions(int width, int height)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas(width, height));

    [Fact]
    public void DimensionOverflowIsRejected()
        => Assert.Throws<OverflowException>(() => new Canvas(int.MaxValue, 2));

    [Fact]
    public void IndexerIsStrictButPixelClips()
    {
        var canvas = new Canvas(3, 2);
        Assert.True(canvas.SetPixel(2, 1, Ink));
        Assert.Equal(Ink, canvas[2, 1]);
        Assert.False(canvas.SetPixel(-1, 0, Ink));
        Assert.False(canvas.SetPixel(0, 2, Ink));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas[-1, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas[0, 2] = Ink);
    }

    [Fact]
    public void SnapshotIsIndependentAndRowMajor()
    {
        var canvas = new Canvas(3, 2);
        canvas[1, 1] = Ink;
        var snapshot = canvas.Snapshot();
        Assert.Equal(Ink, snapshot[4]);
        snapshot[4] = Cell.Empty;
        Assert.Equal(Ink, canvas[1, 1]);
    }

    [Fact]
    public void TextClipsAndHandlesCrLfWithoutWrapping()
    {
        var canvas = new Canvas(3, 3);
        canvas.DrawText(-1, 0, "ABCD\r\nEF\nGHIJK", ConsoleColor.Red);
        Assert.Equal("BCD", Row(canvas, 0));
        Assert.Equal("F  ", Row(canvas, 1));
        Assert.Equal("HIJ", Row(canvas, 2));
        Assert.Equal(ConsoleColor.Red, canvas[0, 0].Foreground);
    }

    [Fact]
    public void InvalidTextDoesNotPartiallyModifyCanvas()
    {
        var canvas = new Canvas(5, 2);
        Assert.Throws<ArgumentException>(() => canvas.DrawText(0, 0, "ok\t"));
        Assert.Throws<ArgumentException>(() => canvas.DrawText(0, 0, "ok\r"));
        Assert.Throws<ArgumentException>(() => canvas.DrawText(0, 0, "😀"));
        Assert.Throws<ArgumentNullException>(() => canvas.DrawText(0, 0, null!));
        Assert.All(canvas.Snapshot(), c => Assert.Equal(Cell.Empty, c));
    }

    [Fact]
    public void TextExtremeCoordinatesDoNotOverflow()
    {
        var canvas = new Canvas(2, 2);
        canvas.DrawText(int.MaxValue, int.MaxValue, "AB\nCD");
        Assert.All(canvas.Snapshot(), c => Assert.Equal(Cell.Empty, c));
    }

    [Theory]
    [InlineData('\n')]
    [InlineData('\0')]
    [InlineData('\ud800')]
    public void CellRejectsUnsupportedCharacters(char value)
        => Assert.Throws<ArgumentException>(() => new Cell(value));

    [Fact]
    public void CellRejectsInvalidColors()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cell('a', (ConsoleColor)16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cell('a', ConsoleColor.Gray, (ConsoleColor)(-1)));
    }

    [Fact]
    public void FilledRectangleClips()
    {
        var canvas = new Canvas(4, 3);
        canvas.FillRectangle(-2, -1, 4, 3, Ink);
        Assert.Equal("**  ", Row(canvas, 0));
        Assert.Equal("**  ", Row(canvas, 1));
        Assert.Equal("    ", Row(canvas, 2));
        canvas.FillRectangle(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, Ink);
        Assert.Equal("    ", Row(canvas, 2));
    }

    [Fact]
    public void OutlineDoesNotInventBorderAtClippingEdge()
    {
        var canvas = new Canvas(4, 4);
        canvas.DrawRectangle(-1, -1, 4, 4, Ink);
        Assert.Equal("  * ", Row(canvas, 0));
        Assert.Equal("*** ", Row(canvas, 2));
        Assert.Equal(Cell.Empty, canvas[0, 0]);
    }

    [Fact]
    public void EmptyAndInvalidRectangles()
    {
        var canvas = new Canvas(2, 2);
        canvas.DrawRectangle(0, 0, 0, 2, Ink);
        canvas.FillRectangle(0, 0, 2, 0, Ink);
        Assert.All(canvas.Snapshot(), c => Assert.Equal(Cell.Empty, c));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.DrawRectangle(0, 0, -1, 2, Ink));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.FillRectangle(0, 0, 2, -1, Ink));
        canvas.DrawRectangle(1, 1, 1, 1, Ink);
        Assert.Equal(Ink, canvas[1, 1]);
    }

    [Theory]
    [InlineData(0, 0, 4, 4)]
    [InlineData(4, 4, 0, 0)]
    [InlineData(0, 4, 4, 0)]
    [InlineData(4, 0, 0, 4)]
    [InlineData(0, 2, 4, 2)]
    [InlineData(2, 4, 2, 0)]
    [InlineData(1, 0, 2, 4)]
    [InlineData(0, 1, 4, 2)]
    public void LinesIncludeEndpointsAndRemainConnected(int x1, int y1, int x2, int y2)
    {
        var canvas = new Canvas(5, 5);
        canvas.DrawLine(x1, y1, x2, y2, Ink);
        Assert.Equal(Ink, canvas[x1, y1]);
        Assert.Equal(Ink, canvas[x2, y2]);
        Assert.Equal(Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) + 1,
            canvas.Snapshot().Count(c => c == Ink));
    }

    [Fact]
    public void LinesClipExtremeCoordinatesAndRejectInvisibleSegments()
    {
        var canvas = new Canvas(5, 5);
        canvas.DrawLine(int.MinValue, 2, int.MaxValue, 2, Ink);
        Assert.Equal("*****", Row(canvas, 2));
        canvas.DrawLine(-10, -10, -1, -1, Ink);
        canvas.DrawLine(8, 0, 8, 8, Ink);
        Assert.Equal(5, canvas.Snapshot().Count(c => c == Ink));
        canvas.DrawLine(0, 0, 0, 0, Ink);
        Assert.Equal(Ink, canvas[0, 0]);
    }

    [Fact]
    public void DependencyRegistrationValidatesPlatformAndRegistersRenderer()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddWindowsConsoleCanvas(null!));
        var services = new ServiceCollection();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() => services.AddWindowsConsoleCanvas());
            return;
        }
        Assert.Same(services, services.AddWindowsConsoleCanvas());
        var registration = Assert.Single(services);
        Assert.Equal(typeof(ICanvasRenderer), registration.ServiceType);
        Assert.Equal(typeof(WindowsConsoleRenderer), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    private static string Row(Canvas canvas, int y)
        => new(Enumerable.Range(0, canvas.Width).Select(x => canvas[x, y].Character).ToArray());
}
