using QBSharp.Cli.Editor;
using Screen9.Web.Components;

namespace Screen9.Web;

/// <summary>IEditorScreen over a browser canvas: a fixed 80x25 character grid (no terminal to query a real
/// size from — resizing to the browser window is a possible later addition, not done here), drawn through
/// the same <see cref="PixelDisplay"/>/PngFrame pipeline the game pages use.</summary>
public sealed class BlazorEditorScreen(PixelDisplay display, Func<bool>? hidden = null) : IEditorScreen
{
    public int Width => 80;
    public int Height => 25;
    /// <summary>While a program runs in the modal dialog the editor is hidden behind it: its redraws (a PNG encode and a
    /// message to the browser, ten times a second) would only compete with the program's own frames on the same circuit.
    /// The editor redraws as soon as the dialog closes (its state is unchanged, so the next frame is a full one).</summary>
    public void Draw(EditorFrame frame)
    {
        if (hidden?.Invoke() == true) return;
        display.PresentText(frame.ToCanvas());
    }
    public void Dispose() { }
}
