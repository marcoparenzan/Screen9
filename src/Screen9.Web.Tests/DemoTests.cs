using Screen9.Demos;

namespace Screen9.Web.Tests;

/// <summary>The two demos shown on the site (and run in a console by samples/csharp/Demos), without any screen.</summary>
public class DemoTests
{
    [Fact]
    public void ThePlasmaCyclesItsPaletteBetweenFramesWhileThePicturesStayTheSame()
    {
        var demo = new PlasmaDemo();
        demo.Step();
        var first = demo.Canvas.PixelSnapshot();
        var colorBefore = demo.Canvas.GetRgb(100);
        demo.Step();
        var second = demo.Canvas.PixelSnapshot();
        Assert.Equal(2, demo.Frame);
        Assert.NotEqual(colorBefore, demo.Canvas.GetRgb(100));            // the palette moved
        int differing = first.Zip(second).Count(p => p.First != p.Second);
        Assert.InRange(differing, 1, 2 * 24 * 24 * 2);                    // only the ball changed place (two sprites' worth of pixels)
    }

    [Fact]
    public void ThePlasmaUsesTheWholeVgaPaletteAndTheBallStaysOnScreen()
    {
        var demo = new PlasmaDemo();
        for (int i = 0; i < 600; i++) demo.Step();
        Assert.Equal((320, 200, 256), (demo.Canvas.Width, demo.Canvas.Height, demo.Canvas.ColorCount));
        var colors = demo.Canvas.PixelSnapshot().Distinct().Count();
        Assert.True(colors > 100, $"only {colors} colours");
    }

    [Fact]
    public void TheSceneAnimatesABallAndACounterOverAStaticBackground()
    {
        var demo = new SceneDemo(80, 25, "footer");
        demo.Step();
        string Row(int y) => new(Enumerable.Range(0, 80).Select(x => demo.Canvas[x, y].Character).ToArray());
        Assert.Contains("Screen9 - XY Canvas", Row(2));
        Assert.Contains("footer", Row(23));
        Assert.Contains("frame      0", Row(1));
        for (int i = 0; i < 40; i++) demo.Step();
        Assert.Contains("frame     40", Row(1));
        Assert.Contains("Screen9 - XY Canvas", Row(2));                    // the background survives the animation
        Assert.Single(Enumerable.Range(0, 25).SelectMany(y => Enumerable.Range(0, 80).Where(x => demo.Canvas[x, y].Character == '@')));
    }
}
