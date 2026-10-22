namespace Screen9.Web;

/// <summary>A snippet of the Library page. The code of each one (except the two that open a Windows window) is compiled and
/// run in <c>src/Screen9.Tests/LibrarySnippetsTests.cs</c>: keep the two in step.</summary>
public sealed record LibrarySnippet(string Title, string Text, string Code);

public static class LibrarySnippets
{
    public static IReadOnlyList<LibrarySnippet> All { get; } =
    [
        new("Four lines for a screen",
            "SCREEN 9 is mode 9: EGA, 640×350, 16 colours. Everything draws into an indexed framebuffer; the numbers are palette indices.",
            """
            using Screen9;

            var pc = new PCCanvas(9);                 // EGA 640×350, 16 colours
            pc.Box(0, 0, pc.Width, pc.Height, 1, true);
            pc.Circle(320, 175, 40, 14);
            pc.Paint(320, 175, 14, 14);
            """),

        new("Show it in a window (Windows)",
            "PixelWindow shows a canvas at 4:3, as an EGA monitor did: mode 9 pixels are not square. ReadKey is INKEY$ — an empty string when no key is waiting.",
            """
            using var window = new PixelWindow("My game");
            window.Present(pc);
            while (!window.Closed)
            {
                string key = window.ReadKey();        // "" if none; "\0H" is the up arrow, as in QBasic
                if (key == "\x1b") break;
                Thread.Sleep(15);
            }
            """),

        new("The EGA palette",
            "Mode 9 shows 16 colours at a time out of 64. SetPalette maps a colour index to a hardware code (0–63, two bits per channel); GetRgb tells what it looks like.",
            """
            var pc = new PCCanvas(9);
            pc.SetPalette(1, 0x39);                   // colour 1 -> hardware code 57: bright blue (85, 85, 255)
            pc.SetPalette(2, 0x14);                   // colour 2 -> hardware code 20: brown (170, 85, 0)
            pc.Box(0, 0, 640, 350, 1, filled: true);
            PCRgb blue = pc.GetRgb(1);
            """),

        new("Text inside the graphics mode",
            "A text cell in mode 9 is 8×14 pixels, so the screen is an 80×25 grid, drawn with the IBM EGA ROM font that ships with the engine.",
            """
            var pc = new PCCanvas(9);
            var text = new PCTextDisplay { UseIbmFonts = true };
            text.Locate(2, 10);                       // row 2, column 10 (1-based, as LOCATE)
            text.Write("SCORE 000120", 14, 0, pc);    // yellow on black
            """),

        new("Sprites: GET and PUT",
            "GetImage/PutImage work on indexed images with the raster operations of PUT (copy, PRESET, XOR, AND, OR). PCSprite reads and writes the LONG-array buffers of QBasic's GET and PUT.",
            """
            pc.Box(0, 0, 8, 8, 14, true);
            var image = pc.GetImage(0, 0, 8, 8);                          // PCImage: indexed pixels
            pc.PutImage(image, 100, 50);                                  // copy
            pc.PutImage(image, 100, 50, PCPixelOperation.Xor);            // XOR twice restores the background

            var buffer = new int[64];                                     // the LONG array of GET/PUT
            PCSprite.Capture(pc, buffer, 0, 0, 7, 7);                     // GET (0,0)-(7,7), buffer&
            PCSprite.Draw(pc, buffer, 200, 100, PCSpriteOperation.Xor);   // PUT (200,100), buffer&, XOR
            """),

        new("Double buffering and pages",
            "With ManualPresent the screen only changes on Present(), so a half-drawn frame is never seen. Mode 9 also has two real pages, as SCREEN 9,,1,0 in QBasic: draw on one, show the other.",
            """
            var pc = new PCCanvas(9) { ManualPresent = true };
            for (int frame = 0; frame < 60; frame++)
            {
                pc.ClearIndex(0);
                pc.Circle(40 + frame * 9, 175, 20, 14);
                pc.Paint(40 + frame * 9, 175, 14, 14);
                pc.Present();                                             // the whole frame appears at once
            }

            pc.ActivePage = 1;                                            // draw on page 1...
            pc.Box(0, 0, 100, 100, 3, true);
            pc.ActivePage = 0;
            pc.VisualPage = 1;                                            // ...and show it (page flipping)
            """),

        new("A frame for the browser",
            "PngFrame turns a canvas into an indexed PNG: it is what this site sends to your browser for every frame.",
            """
            byte[] png = PngFrame.Encode(pc);
            """),

        new("Music with PLAY",
            "PCMusic parses the PLAY language into tones; PCSpeaker plays them in a queue. Each note comes with its articulation pause, as in QBasic.",
            """
            var tones = new PCMusic().Parse("T180 O3 L8 C D E F G");
            var speaker = new PCSpeaker(PCSpeaker.Beep);              // PCSpeaker.Silent for no sound
            foreach (var tone in tones) await speaker.PlayAsync(tone, default);
            await speaker.DrainAsync(default);
            """),

        new("Mode 12: a whole frame at once",
            "A game that composes its own frame as an array of colour indices writes it with a single call — PutImage goes through PSet pixel by pixel. Mode 12 (640×480) has square pixels, so the window can enlarge the picture by whole multiples only and every pixel keeps the same size. Rally-X works this way.",
            """
            var pc = new PCCanvas(12);                       // 640×480, 16 colours
            pc.PutPixels(frame, 0, 0, 640, 480);             // clipped, ~5 ms per frame in Release

            using var window = new PixelWindow("My game", windowWidth: 1296, windowHeight: 1000) { IntegerScale = true };
            """),
    ];
}
