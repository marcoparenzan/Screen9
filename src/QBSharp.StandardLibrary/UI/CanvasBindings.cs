using Screen9;
using QBSharp.StandardLibrary.Serialization;

namespace QBSharp.StandardLibrary.UI;

internal static class CanvasBindings
{
    private static void Count(IReadOnlyList<object?> args, int min, int max) { if (args.Count < min || args.Count > max) throw new ArgumentException($"Expected {min}..{max} arguments."); }
    private static int Int(IReadOnlyList<object?> args, int i) => args[i] is int n ? n : throw new ArgumentException("Expected Integer coordinate or color.");
    private static string Text(IReadOnlyList<object?> args, int i) => args[i] as string ?? throw new ArgumentException("Expected String.");
    private static Canvas Canvas(IReadOnlyList<object?> args) => args[0] as Canvas ?? throw new ArgumentException("Expected Screen9 Canvas.");
    private static Cell Ink(IReadOnlyList<object?> args, int start)
    {
        string glyph = args.Count > start ? Text(args, start) : " ";
        if (glyph.Length != 1) throw new ArgumentException("A glyph must be one printable BMP character.");
        return new(glyph[0], args.Count > start + 1 ? (ConsoleColor)Int(args, start + 1) : ConsoleColor.Gray, args.Count > start + 2 ? (ConsoleColor)Int(args, start + 2) : ConsoleColor.Black);
    }
    private static bool Filled(IReadOnlyList<object?> args, int index) => args.Count > index ? args[index] is bool b ? b : throw new ArgumentException("Expected Boolean fill flag.") : false;
    private static ValueTask<object?> Value(object? value = null) => ValueTask.FromResult(value);
    public static StandardNamespace Create(ICanvasRenderer? renderer)
    {
        var group = new StandardNamespace();
        group.Add("Create", (_, args) =>
        {
            Count(args, 2, 2); int width = Int(args, 0), height = Int(args, 1);
            if (width is < 1 or > 300 || height is < 1 or > 100) throw new ArgumentException("Cell canvas limits: width 1..300, height 1..100.");
            return Value(new Canvas(width, height));
        });
        group.Add("Clear", (_, args) => { Count(args, 1, 4); Canvas(args).Clear(Ink(args, 1)); return Value(); });
        group.Add("Text", (_, args) =>
        { Count(args, 4, 6); var ink = Ink(new object?[] { " ", args.Count > 4 ? args[4] : 7, args.Count > 5 ? args[5] : 0 }, 0); Canvas(args).DrawText(Int(args, 1), Int(args, 2), Text(args, 3), ink.Foreground, ink.Background); return Value(); });
        group.Add("Pixel", (_, args) => { Count(args, 4, 6); return Value(Canvas(args).SetPixel(Int(args, 1), Int(args, 2), Ink(args, 3))); });
        group.Add("Line", (_, args) => { Count(args, 6, 8); Canvas(args).DrawLine(Int(args, 1), Int(args, 2), Int(args, 3), Int(args, 4), Ink(args, 5)); return Value(); });
        group.Add("Rectangle", (_, args) =>
        {
            Count(args, 6, 9); var canvas = Canvas(args); var ink = Ink(args, 5);
            if (Filled(args, 8)) canvas.FillRectangle(Int(args, 1), Int(args, 2), Int(args, 3), Int(args, 4), ink);
            else canvas.DrawRectangle(Int(args, 1), Int(args, 2), Int(args, 3), Int(args, 4), ink);
            return Value();
        });
        group.Add("Ellipse", (_, args) => { Count(args, 6, 9); Canvas(args).DrawEllipse(Int(args, 1), Int(args, 2), Int(args, 3), Int(args, 4), Ink(args, 5), Filled(args, 8)); return Value(); });
        group.Add("Circle", (_, args) => { Count(args, 5, 8); Canvas(args).DrawEllipse(Int(args, 1), Int(args, 2), Int(args, 3), Int(args, 3), Ink(args, 4), Filled(args, 7)); return Value(); });
        group.Add("Get", (_, args) => { Count(args, 5, 5); return Value(Canvas(args).Capture(Int(args, 1), Int(args, 2), Int(args, 3), Int(args, 4))); });
        group.Add("Put", (_, args) =>
        {
            Count(args, 4, 5); var region = args[1] as CanvasRegion ?? throw new ArgumentException("Expected Screen9 CanvasRegion.");
            char? transparent = null;
            if (args.Count == 5) { string glyph = Text(args, 4); if (glyph.Length != 1) throw new ArgumentException("Expected one transparency character."); transparent = glyph[0]; }
            Canvas(args).Blit(region, Int(args, 2), Int(args, 3), transparent); return Value();
        });
        group.Add("Point", (_, args) =>
        {
            Count(args, 3, 3); var cell = Canvas(args)[Int(args, 1), Int(args, 2)];
            return Value(new DynamicRecord(new Dictionary<string, object?> { ["Character"] = (cell.Character == '\0' ? ' ' : cell.Character).ToString(), ["Foreground"] = (int)cell.Foreground, ["Background"] = (int)cell.Background }));
        });
        group.Add("Snapshot", (_, args) =>
        {
            Count(args, 1, 1); var canvas = Canvas(args);
            return Value(Enumerable.Range(0, canvas.Height).Select(y => new string(Enumerable.Range(0, canvas.Width).Select(x => canvas[x, y].Character == '\0' ? ' ' : canvas[x, y].Character).ToArray())).ToArray());
        });
        group.Add("Viewport", (_, args) =>
        {
            Count(args, 0, 0); var size = (renderer ?? throw new InvalidOperationException("No Screen9 renderer configured by host.")).GetViewportSize();
            return Value(new DynamicRecord(new Dictionary<string, object?> { ["Width"] = size.Width, ["Height"] = size.Height }));
        });
        group.Add("Render", (_, args) => { Count(args, 1, 3); (renderer ?? throw new InvalidOperationException("No Screen9 renderer configured by host.")).Render(Canvas(args), args.Count > 1 ? Int(args, 1) : 0, args.Count > 2 ? Int(args, 2) : 0); return Value(); });
        return group;
    }
}
