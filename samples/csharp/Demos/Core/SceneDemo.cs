namespace Screen9.Demos;

/// <summary>The cell canvas (<see cref="Canvas"/>: a character and two colours per cell) in a small animation: a static scene
/// captured once, and a ball and a frame counter redrawn on top of it at every <see cref="Step"/>. No device: the host shows
/// <see cref="Canvas"/> (a console renderer that writes only the changed cells, or a PNG in a browser).</summary>
public sealed class SceneDemo
{
    private readonly Canvas canvas;
    private readonly CanvasRegion background;
    private readonly Cell ball = new('@', ConsoleColor.Yellow, ConsoleColor.DarkBlue);
    private double x = 3, y = 3, speedX = 0.7, speedY = 0.4;

    public SceneDemo(int width, int height, string footer)
    {
        canvas = new Canvas(width, height);
        canvas.Clear(new Cell(' ', ConsoleColor.White, ConsoleColor.DarkBlue));
        canvas.DrawRectangle(0, 0, canvas.Width, canvas.Height, new Cell('#', ConsoleColor.Cyan, ConsoleColor.DarkBlue));
        canvas.DrawText(2, 2, "Screen9 - XY Canvas\nOrigin (0,0) at the top left", ConsoleColor.White, ConsoleColor.DarkBlue);
        canvas.FillRectangle(4, 6, 18, 4, new Cell(' ', ConsoleColor.Black, ConsoleColor.DarkCyan));
        canvas.DrawText(5, 7, "A rectangle", ConsoleColor.White, ConsoleColor.DarkCyan);
        canvas.DrawLine(2, 12, 35, 17, new Cell('*', ConsoleColor.Yellow, ConsoleColor.DarkBlue));
        canvas.DrawText(2, canvas.Height - 2, footer, ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        background = canvas.Capture(0, 0, canvas.Width, canvas.Height); // the scene, redrawn under the ball at every frame
    }

    public Canvas Canvas => canvas;

    public int Frame { get; private set; }

    public void Step()
    {
        canvas.Blit(background, 0, 0);
        x += speedX; y += speedY;
        if (x < 1 || x > canvas.Width - 2) { speedX = -speedX; x = Math.Clamp(x, 1, canvas.Width - 2); }
        if (y < 1 || y > canvas.Height - 2) { speedY = -speedY; y = Math.Clamp(y, 1, canvas.Height - 2); }
        canvas.SetPixel((int)x, (int)y, ball);
        canvas.DrawText(canvas.Width - 16, 1, $"frame {Frame,6}", ConsoleColor.White, ConsoleColor.DarkBlue);
        Frame++;
    }
}
