using Spectre.Console;
using Spectre.Console.Rendering;

namespace QBSharp.Cli;

/// <summary>Literal stream output without paragraph reflow at terminal width (important for pipes).</summary>
internal sealed class LiteralOutput(string text) : Renderable
{
    protected override Measurement Measure(RenderOptions options, int maxWidth) => new(0, text.Length);
    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        yield return new Segment(text, Style.Plain);
    }
}
