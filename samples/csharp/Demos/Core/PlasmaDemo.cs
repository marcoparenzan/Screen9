namespace Screen9.Demos;

/// <summary>A 240-colour plasma with palette cycling and a bouncing ball, on a VGA mode 13 canvas (320×200, 256 colours) with
/// logical double buffering (<see cref="PCCanvas.ManualPresent"/>). No device: <see cref="Step"/> draws the next frame, and the
/// host shows <see cref="Canvas"/> however it can (ANSI truecolor in a terminal, a PNG in a browser, a pixel window).</summary>
public sealed class PlasmaDemo
{
    public const int Width = 320, Height = 200, Colors = 240, FirstColor = 16;

    private readonly PCCanvas canvas = new(13) { ManualPresent = true };
    private readonly byte[] plasma = new byte[Width * Height];
    private readonly PCImage ball;
    private double ballX = 40, ballY = 30, speedX = 2.3, speedY = 1.7;

    public PlasmaDemo()
    {
        // A sum of three sines gives every pixel a colour index in 16..255.
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                plasma[y * Width + x] = (byte)(FirstColor + (int)(120 + 40 * Math.Sin(x / 16.0) + 40 * Math.Sin(y / 12.0) + 40 * Math.Sin((x + y) / 24.0)) % Colors);
        // The sprite is drawn on the page and captured; colour 0 is transparent in PutImage.
        canvas.Ellipse(12, 12, 11, 11, 15, true);
        canvas.Ellipse(9, 9, 3, 3, 14, true);
        ball = canvas.GetImage(0, 0, 24, 24);
    }

    /// <summary>The visible frame (the last <see cref="Step"/>).</summary>
    public PCCanvas Canvas => canvas;

    /// <summary>Frames drawn so far.</summary>
    public int Frame { get; private set; }

    public void Step()
    {
        canvas.PutPixels(plasma, 0, 0, Width, Height); // the whole plasma in one call, not 64000 PSet
        // Cycling the palette moves the colours without touching a pixel: a hue ramp over the 240 colours, shifted by the frame.
        for (int i = 0; i < Colors; i++)
        {
            double hue = (i + Frame * 2) % Colors / 40.0;
            int sector = (int)hue, ramp = (int)((hue - sector) * 63);
            var (r, g, b) = sector switch
            {
                0 => (63, ramp, 0), 1 => (63 - ramp, 63, 0), 2 => (0, 63, ramp),
                3 => (0, 63 - ramp, 63), 4 => (ramp, 0, 63), _ => (63, 0, 63 - ramp),
            };
            canvas.SetDac(FirstColor + i, r, g, b);
        }
        ballX += speedX; ballY += speedY;
        if (ballX < 0 || ballX > Width - 24) { speedX = -speedX; ballX = Math.Clamp(ballX, 0, Width - 24); }
        if (ballY < 0 || ballY > Height - 24) { speedY = -speedY; ballY = Math.Clamp(ballY, 0, Height - 24); }
        canvas.PutImage(ball, (int)ballX, (int)ballY, transparent: 0);
        canvas.Present();
        Frame++;
    }
}
