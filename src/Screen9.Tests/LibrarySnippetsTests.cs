namespace Screen9.Tests;

/// <summary>The code snippets of the Library page (src/Screen9.Web/Components/Pages/Library.razor), compiled and run here so the
/// Coding Guide never shows code that does not build. The window and speaker parts, which need Windows, are not run.</summary>
public sealed class LibrarySnippetsTests
{
    [Fact]
    public void FourLinesForAScreen()
    {
        var pc = new PCCanvas(9);                 // EGA 640×350, 16 colours
        pc.Box(0, 0, pc.Width, pc.Height, 1, true);
        pc.Circle(320, 175, 40, 14);
        pc.Paint(320, 175, 14, 14);
        Assert.Equal((640, 350), (pc.Width, pc.Height));
        Assert.Equal(14, pc.Point(320, 175));
        Assert.Equal(1, pc.Point(5, 5));
    }

    [Fact]
    public void ModeNineAtAGlance()
    {
        var pc = new PCCanvas(9);
        Assert.Equal((640, 350, 16, 2), (pc.Width, pc.Height, pc.ColorCount, pc.PageCount));
        Assert.Equal((80, 25, 14), (pc.VideoMode.TextColumns, pc.VideoMode.TextRows, pc.VideoMode.CharacterHeight));
        double aspect = 4.0 / 3 * 350 / 640;      // the default aspect of CIRCLE in SCREEN 9 (pixels are not square)
        Assert.InRange(aspect, 0.72, 0.73);
    }

    [Fact]
    public void ThePaletteMapsSixtyFourHardwareColoursToSixteen()
    {
        var pc = new PCCanvas(9);
        pc.SetPalette(1, 0x39);                   // colour 1 -> hardware code 57
        pc.SetPalette(2, 0x14);                   // colour 2 -> hardware code 20 (brown)
        pc.Box(0, 0, 640, 350, 1, filled: true);
        Assert.Equal(new PCRgb(85, 85, 255), pc.GetRgb(1));   // bright blue
        Assert.Equal(new PCRgb(170, 85, 0), pc.GetRgb(2));    // brown
        Assert.Equal(1, pc.Point(10, 10));
    }

    [Fact]
    public void TextInsideTheGraphicsMode()
    {
        var pc = new PCCanvas(9);
        var text = new PCTextDisplay { UseIbmFonts = true };
        text.Locate(2, 10);                       // row 2, column 10 (1-based): an 80×25 grid of 8×14 pixel cells
        text.Write("SCORE 000120", 14, 0, pc);    // yellow on black, with the IBM EGA 8x14 ROM font
        bool anyYellow = Enumerable.Range(14, 14).Any(y => Enumerable.Range(72, 96).Any(x => pc.Point(x, y) == 14));
        Assert.True(anyYellow);
    }

    [Fact]
    public void SpritesWithGetImagePutImageAndTheQBasicBuffers()
    {
        var pc = new PCCanvas(9);
        pc.Box(0, 0, 8, 8, 14, true);
        var image = pc.GetImage(0, 0, 8, 8);                          // PCImage: indexed pixels
        pc.PutImage(image, 100, 50);                                  // copy
        pc.PutImage(image, 100, 50, PCPixelOperation.Xor);            // XOR twice restores the background
        Assert.Equal(0, pc.Point(104, 54));

        var buffer = new int[64];                                     // the LONG array of GET/PUT
        PCSprite.Capture(pc, buffer, 0, 0, 7, 7);                     // GET (0,0)-(7,7), buffer&
        PCSprite.Draw(pc, buffer, 200, 100, PCSpriteOperation.Xor);   // PUT (200,100), buffer&, XOR
        Assert.Equal(14, pc.Point(204, 104));
    }

    [Fact]
    public void DoubleBufferingAndPages()
    {
        var pc = new PCCanvas(9) { ManualPresent = true };
        for (int frame = 0; frame < 3; frame++)
        {
            pc.ClearIndex(0);
            pc.Circle(40 + frame * 9, 175, 20, 14);
            pc.Paint(40 + frame * 9, 175, 14, 14);
            pc.Present();                                             // the whole frame appears at once
        }
        Assert.Equal(14, pc.Point(40 + 2 * 9, 175));

        pc.ActivePage = 1;                                            // EGA mode 9 has two pages, as SCREEN 9,,1,0
        pc.Box(0, 0, 100, 100, 3, true);
        Assert.Equal(3, pc.Point(10, 10));
        pc.ActivePage = 0;
        Assert.NotEqual(3, pc.Point(10, 10));
        pc.VisualPage = 1;                                            // show page 1 (page flipping)
        Assert.Equal(1, pc.VisualPage);
    }

    [Fact]
    public void AFrameAsAnIndexedPng()
    {
        var pc = new PCCanvas(9);
        pc.Box(10, 10, 50, 30, 6, true);
        byte[] png = PngFrame.Encode(pc);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
    }

    [Fact]
    public async Task MusicWithPlayStrings()
    {
        var tones = new PCMusic().Parse("T180 O3 L8 C D E F G");
        Assert.Equal(5, tones.Count(t => t.Frequency > 0));          // five notes; each is followed by its articulation pause (frequency 0)
        var speaker = new PCSpeaker(PCSpeaker.Silent);                // or PCSpeaker.Beep on Windows
        foreach (var tone in tones.Select(t => t with { Duration = TimeSpan.FromMilliseconds(1) })) await speaker.PlayAsync(tone, default);
        await speaker.DrainAsync(default);
        speaker.Stop();
    }

    [Fact]
    public void IntegerScaleOnlyForSquarePixelModes()
    {
        // Mode 9 pixels are not square (640×350 is shown at 4:3), so it keeps the 4:3 fit; mode 12 (640×480) can use whole multiples.
        var fit = PixelWindow.Fit(1280, 960);
        Assert.Equal((1280, 960), (fit.Width, fit.Height));
        var integer = PixelWindow.FitInteger(1280, 960, 640, 480);
        Assert.Equal((1280, 960), (integer.Width, integer.Height));
    }
}
