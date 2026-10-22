using Screen9;
using QBSharp.Core.Diagnostics;
using QBSharp.QBasic;

namespace QBSharp.Tests;

/// <summary>DEF SEG/PEEK/POKE sulla finestra video e OUT/INP sulle porte EGA/VGA (Screen9 2.2).</summary>
public sealed class QBasicVideoPortTests
{
    private static async Task<(QBasicSessionHandle Session, PCCanvas Frame)> Run(string program)
    {
        PCCanvas? frame = null;
        var session = new QBasicEngine(new() { PresentGraphics = f => frame = f }).CreateSession();
        await session.ExecuteAsync(new(program, "video.bas"));
        return (new(session), frame ?? throw new InvalidOperationException("No graphics frame presented."));
    }

    private sealed class QBasicSessionHandle(QBSharp.Core.Hosting.IScriptSession session) : IAsyncDisposable
    {
        public object? this[string name] => session.Variables[name];
        public ValueTask DisposeAsync() => session.DisposeAsync();
    }

    [Fact]
    public async Task Mode13PokeAndPeekReachTheFramebuffer()
    {
        var (s, frame) = await Run("""
            SCREEN 13
            PSET (7, 0), 9
            DEF SEG = &HA000
            POKE 320 * 10 + 5, 42
            p% = PEEK(320 * 10 + 5)
            q% = PEEK(7)
            DEF SEG
            c% = POINT(5, 10)
            """);
        await using var _ = s;
        Assert.Equal((short)42, s["p%"]); Assert.Equal((short)9, s["q%"]); Assert.Equal(42, Convert.ToInt32(s["c%"]));
        Assert.Equal(42, frame.Point(5, 10));
    }

    [Fact]
    public async Task VideoSegmentIsOrdinaryMemoryInTextMode()
    {
        var (s, frame) = await Run("""
            DEF SEG = &HA000
            POKE 0, 5
            t% = PEEK(0)
            SCREEN 13
            v% = PEEK(0)
            """);
        await using var _ = s;
        Assert.Equal((short)5, s["t%"]); Assert.Equal((short)0, s["v%"]); Assert.Equal(0, frame.Point(0, 0));
    }

    [Fact]
    public async Task Mode12PlanarPokeHonoursTheMapMask()
    {
        var (s, frame) = await Run("""
            SCREEN 12
            DEF SEG = &HA000
            POKE 0, &HFF
            OUT &H3C4, 2
            OUT &H3C5, 4
            POKE 80, &HF0
            OUT &H3C5, &HF
            OUT &H3CE, 4
            OUT &H3CF, 2
            plane2% = PEEK(80)
            """);
        await using var _ = s;
        Assert.Equal(15, frame.Point(0, 0)); Assert.Equal(4, frame.Point(0, 1)); Assert.Equal(0, frame.Point(4, 1));
        Assert.Equal((short)0xF0, s["plane2%"]);
    }

    [Fact]
    public async Task DacPortsChangeThePaletteAndReadBack()
    {
        var (s, frame) = await Run("""
            SCREEN 13
            PSET (0, 0), 1
            OUT &H3C8, 1
            OUT &H3C9, 63
            OUT &H3C9, 0
            OUT &H3C9, 0
            OUT &H3C7, 1
            r% = INP(&H3C9)
            g% = INP(&H3C9)
            b% = INP(&H3C9)
            """);
        await using var _ = s;
        Assert.Equal(new PCRgb(255, 0, 0), frame.GetRgb(1)); Assert.Equal(1, frame.Point(0, 0));
        Assert.Equal(((short)63, (short)0, (short)0), ((short)s["r%"]!, (short)s["g%"]!, (short)s["b%"]!));
    }

    [Fact]
    public async Task Screen12DacRegisterTwentyIsBrown()
    {
        var (s, frame) = await Run("""
            SCREEN 12
            OUT &H3C8, 20
            OUT &H3C9, 0
            OUT &H3C9, 0
            OUT &H3C9, 63
            """);
        await using var _ = s;
        Assert.Equal(new PCRgb(0, 0, 255), frame.GetRgb(6)); Assert.Equal(new PCRgb(170, 0, 170), frame.GetRgb(5));
    }

    [Theory]
    [InlineData("SCREEN 13\nOUT &H3DA, 0", "not emulated")]
    [InlineData("SCREEN 13\nx% = INP(&H60)", "not emulated")]
    [InlineData("SCREEN 9\nOUT &H3C8, 0", "VGA mode")]
    [InlineData("SCREEN 1\nOUT &H3C4, 2", "EGA or VGA")]
    [InlineData("OUT &H3C8, 0", "SCREEN graphics mode")]
    [InlineData("SCREEN 13\nOUT &H3C9, 256", "value 0..255")]
    [InlineData("SCREEN 13\nOUT -1, 0", "port 0..65535")]
    public async Task UnsupportedPortAccessIsAQBasicError(string program, string message)
    {
        await using var session = new QBasicEngine().CreateSession();
        var e = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new(program, "ports.bas")));
        Assert.Contains(message, e.Message); Assert.Equal("ports.bas", e.Diagnostic.Span.File);
    }
}
