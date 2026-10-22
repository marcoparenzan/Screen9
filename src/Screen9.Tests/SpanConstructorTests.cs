namespace Screen9.Tests;

/// <summary>Costruttori ReadOnlySpan (2.1.0) accanto a quelli IReadOnlyList: stessa validazione, copia sempre indipendente.</summary>
public sealed class SpanConstructorTests
{
    private static readonly Cell A = new('A', ConsoleColor.Yellow), B = new('B', ConsoleColor.Cyan);

    [Fact]
    public void RegionAcceptsSlicesStackallocArraysAndLists()
    {
        Cell[] buffer = [B, A, B, A, B];
        var slice = new CanvasRegion(2, 2, buffer.AsSpan(1, 4)); Assert.Equal(new[] { A, B, A, B }, slice.Snapshot());
        Span<Cell> stack = stackalloc Cell[2]; stack[0] = A; stack[1] = B;
        Assert.Equal(new[] { A, B }, new CanvasRegion(2, 1, stack).Snapshot());
        var fromArray = new CanvasRegion(1, 1, buffer[..1]); var list = new List<Cell> { A }; var fromList = new CanvasRegion(1, 1, list);
        buffer[0] = A; list[0] = B; Assert.Equal(B, fromArray[0, 0]); Assert.Equal(A, fromList[0, 0]); // copie indipendenti
    }

    [Fact]
    public void RegionOverloadsValidateTheSameWay()
    {
        Assert.Throws<ArgumentException>(() => new CanvasRegion(2, 2, new[] { A }.AsSpan()));
        Assert.Throws<ArgumentException>(() => new CanvasRegion(2, 2, new List<Cell> { A }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanvasRegion(0, 1, ReadOnlySpan<Cell>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanvasRegion(1, 0, new List<Cell>()));
        Assert.Throws<ArgumentNullException>(() => new CanvasRegion(1, 1, (IReadOnlyList<Cell>)null!));
    }

    [Fact]
    public void ImageAcceptsSlicesStackallocArraysAndLists()
    {
        byte[] buffer = [9, 1, 2, 3, 4, 9];
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, new PCImage(2, 2, 16, buffer.AsSpan(1, 4)).Snapshot());
        Span<byte> stack = stackalloc byte[] { 3, 0 }; Assert.Equal(new byte[] { 3, 0 }, new PCImage(2, 1, 4, stack).Snapshot());
        var list = new List<byte> { 1, 0 }; var fromList = new PCImage(2, 1, 2, list); list[0] = 0; Assert.Equal(1, fromList[0, 0]);
        var fromArray = new PCImage(1, 1, 256, new byte[] { 255 }); Assert.Equal(255, fromArray[0, 0]);
    }

    [Theory] [InlineData(2, 2)] [InlineData(4, 4)] [InlineData(16, 16)] [InlineData(16, 200)]
    public void ImageOverloadsRejectIndicesBeyondTheColorDepth(int colors, int value)
    {
        byte[] data = [0, (byte)value, 0, 1];
        Assert.Throws<ArgumentException>(() => new PCImage(2, 2, colors, data.AsSpan()));
        Assert.Throws<ArgumentException>(() => new PCImage(2, 2, colors, data.ToList()));
        data[1] = (byte)(colors - 1); _ = new PCImage(2, 2, colors, data.AsSpan()); _ = new PCImage(2, 2, colors, data.ToList());
    }

    [Fact]
    public void ImageOverloadsValidateDimensionsAndDepthTheSameWay()
    {
        Assert.Throws<ArgumentException>(() => new PCImage(2, 2, 16, new byte[3].AsSpan()));
        Assert.Throws<ArgumentException>(() => new PCImage(2, 2, 16, new List<byte> { 0, 0, 0 }));
        Assert.Throws<ArgumentException>(() => new PCImage(1, 1, 8, new byte[1].AsSpan()));
        Assert.Throws<ArgumentException>(() => new PCImage(640, 481, 16, new byte[640 * 481].AsSpan()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PCImage(0, 1, 16, ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => new PCImage(1, 1, 16, (IReadOnlyList<byte>)null!));
    }

    [Fact]
    public void GetImageAndCaptureCopyTheSourceOnlyOnce()
    {
        var pc = new PCCanvas(13); var canvas = new Canvas(100, 50);
        _ = pc.GetImage(0, 0, 100, 100); _ = canvas.Capture(0, 0, 100, 50); // riscaldamento
        long before = GC.GetAllocatedBytesForCurrentThread(); var image = pc.GetImage(0, 0, 100, 100);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 10_000, 10_000 + 200); // un solo byte[10000] + oggetto
        before = GC.GetAllocatedBytesForCurrentThread(); var region = canvas.Capture(0, 0, 100, 50);
        int cellBytes = System.Runtime.CompilerServices.Unsafe.SizeOf<Cell>() * 5000;
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, cellBytes, cellBytes + 200);
        pc.PSet(0, 0, 7); Assert.Equal(0, image[0, 0]); canvas.SetPixel(0, 0, A); Assert.Equal(Cell.Empty, region[0, 0]); // restano copie indipendenti
    }
}
