using System.Globalization;
using System.Runtime.InteropServices;

namespace QBSharp.Cli.Editor;

[Flags] public enum EditorModifiers { None = 0, Shift = 1, Control = 2, Alt = 4 }
public enum EditorKey { None, Text, Escape, Enter, Backspace, Delete, Left, Right, Up, Down, Home, End, PageUp, PageDown, Tab, F1, F2, F3, F4, F5, F6, F7, F8, F10, Insert }
public sealed record EditorInput(EditorKey Key = EditorKey.None, string Text = "", EditorModifiers Modifiers = EditorModifiers.None, int X = -1, int Y = -1, int Wheel = 0, bool Click = false);

public static class EditorInputDecoder
{
    public static EditorInput FromKey(ConsoleKeyInfo key)
    {
        EditorModifiers modifiers = EditorModifiers.None;
        if (key.Modifiers.HasFlag(ConsoleModifiers.Shift)) modifiers |= EditorModifiers.Shift;
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control)) modifiers |= EditorModifiers.Control;
        if (key.Modifiers.HasFlag(ConsoleModifiers.Alt)) modifiers |= EditorModifiers.Alt;
        EditorKey code = Enum.TryParse<EditorKey>(key.Key.ToString().Replace("Arrow", "", StringComparison.Ordinal), out var parsed) ? parsed : EditorKey.Text;
        if (key.Key == ConsoleKey.Escape) code = EditorKey.Escape;
        string text = modifiers.HasFlag(EditorModifiers.Control) || modifiers.HasFlag(EditorModifiers.Alt) ? key.Key.ToString() : key.KeyChar == '\0' ? "" : key.KeyChar.ToString();
        return new(code, text, modifiers);
    }
    public static EditorInput? DecodeVt(string sequence)
    {
        if (sequence.StartsWith("\u001b[<", StringComparison.Ordinal) && sequence.Length < 64 && sequence[^1] is 'M' or 'm')
        {
            string[] parts = sequence[3..^1].Split(';');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int b) || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y) || b < 0 || x < 1 || y < 1) return null;
            EditorModifiers mods = (b & 4) != 0 ? EditorModifiers.Shift : EditorModifiers.None;
            if ((b & 8) != 0) mods |= EditorModifiers.Alt;
            if ((b & 16) != 0) mods |= EditorModifiers.Control;
            return new(Modifiers: mods, X: x - 1, Y: y - 1, Wheel: (b & 64) != 0 ? (b & 1) == 0 ? -3 : 3 : 0, Click: sequence[^1] == 'M' && (b & 99) == 0);
        }
        var simple = sequence switch
        {
            "\u001b[A" => EditorKey.Up, "\u001b[B" => EditorKey.Down, "\u001b[C" => EditorKey.Right, "\u001b[D" => EditorKey.Left,
            "\u001b[H" => EditorKey.Home, "\u001b[F" => EditorKey.End, "\u001b[3~" => EditorKey.Delete,
            "\u001b[5~" => EditorKey.PageUp, "\u001b[6~" => EditorKey.PageDown,
            "\u001bOP" => EditorKey.F1, "\u001bOQ" => EditorKey.F2, "\u001bOR" => EditorKey.F3, "\u001bOS" => EditorKey.F4,
            "\u001b[15~" => EditorKey.F5, "\u001b[17~" => EditorKey.F6, "\u001b[18~" => EditorKey.F7, "\u001b[19~" => EditorKey.F8, "\u001b[21~" => EditorKey.F10, _ => EditorKey.None
        };
        return simple == EditorKey.None ? null : new(simple);
    }
}

/// <summary>Windows INPUT_RECORD backend; VT terminals use .NET key input plus SGR mouse decoding.</summary>
public sealed class EditorTerminal : IEditorInputSource
{
    private readonly IntPtr handle;
    private readonly uint originalMode;
    private readonly IntPtr outputHandle;
    private readonly uint outputMode;
    private readonly bool originalControlC;
    private bool disposed;
    private string pending = "";
    private long pendingSince;
    public EditorTerminal()
    {
        originalControlC = Console.TreatControlCAsInput;
        if (OperatingSystem.IsWindows())
        {
            handle = GetStdHandle(-10);
            outputHandle = GetStdHandle(-11);
            if (!GetConsoleMode(outputHandle, out outputMode) || !SetConsoleMode(outputHandle, outputMode | 4u)) throw new IOException("Cannot enable ANSI screen mode.");
            if (!GetConsoleMode(handle, out originalMode)) { SetConsoleMode(outputHandle, outputMode); throw new IOException("Cannot read console input mode."); }
            // Disable line/echo/processed/quick-edit; enable window/mouse/extended flags.
            if (!SetConsoleMode(handle, (originalMode & ~(1u | 2u | 4u | 64u | 512u)) | 8u | 16u | 128u)) { SetConsoleMode(outputHandle, outputMode); throw new IOException("Cannot enable editor input mode."); }
        }
        else Console.TreatControlCAsInput = true;
    }
    public EditorInput? Poll()
    {
        if (OperatingSystem.IsWindows()) return PollWindows();
        if (!Console.KeyAvailable)
        {
            if (pending.Length != 0 && Environment.TickCount64 - pendingSince > 80) { pending = ""; return new(EditorKey.Escape); }
            return null;
        }
        var key = Console.ReadKey(true);
        if (pending.Length == 0 && key.Key != ConsoleKey.Escape) return EditorInputDecoder.FromKey(key);
        if (pending.Length == 0) { pending = "\u001b"; pendingSince = Environment.TickCount64; return null; }
        pending += key.KeyChar;
        var decoded = EditorInputDecoder.DecodeVt(pending);
        if (decoded is not null) { pending = ""; return decoded; }
        if (pending.Length == 2 && pending[1] is not ('[' or 'O')) { pending = ""; return EditorInputDecoder.FromKey(new(key.KeyChar, key.Key, false, true, false)); }
        if (pending.Length > 64) pending = "";
        return null;
    }
    private EditorInput? PollWindows()
    {
        if (!GetNumberOfConsoleInputEvents(handle, out uint count) || count == 0) return null;
        var records = new InputRecord[1];
        if (!ReadConsoleInputW(handle, records, 1, out uint read) || read == 0) return null;
        var record = records[0];
        if (record.Type == 1 && record.Down != 0)
        {
            var mods = Modifiers(record.Control);
            var key = new ConsoleKeyInfo(record.Character, (ConsoleKey)record.VirtualKey, mods.HasFlag(EditorModifiers.Shift), mods.HasFlag(EditorModifiers.Alt), mods.HasFlag(EditorModifiers.Control));
            if (char.IsHighSurrogate(key.KeyChar)) { pending = key.KeyChar.ToString(); return null; }
            if (char.IsLowSurrogate(key.KeyChar) && pending.Length == 1) { string text = pending + key.KeyChar; pending = ""; return new(EditorKey.Text, text); }
            pending = ""; return EditorInputDecoder.FromKey(key);
        }
        if (record.Type == 2)
        {
            int wheel = (record.MouseFlags & 4) != 0 ? unchecked((short)(record.Buttons >> 16)) > 0 ? -3 : 3 : 0;
            return new(Modifiers: Modifiers(record.MouseControl), X: record.X, Y: record.Y, Wheel: wheel, Click: (record.MouseFlags is 0 or 2) && (record.Buttons & 1) != 0);
        }
        return null;
    }
    private static EditorModifiers Modifiers(uint state) => ((state & 16) != 0 ? EditorModifiers.Shift : 0) | ((state & 12) != 0 ? EditorModifiers.Control : 0) | ((state & 3) != 0 ? EditorModifiers.Alt : 0);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (OperatingSystem.IsWindows()) { SetConsoleMode(handle, originalMode); SetConsoleMode(outputHandle, outputMode); } else Console.TreatControlCAsInput = originalControlC;
    }
    [StructLayout(LayoutKind.Explicit, Size = 20)] private struct InputRecord
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(4)] public int Down;
        [FieldOffset(8)] public ushort Repeat;
        [FieldOffset(10)] public ushort VirtualKey;
        [FieldOffset(14)] public char Character;
        [FieldOffset(16)] public uint Control;
        [FieldOffset(4)] public short X;
        [FieldOffset(6)] public short Y;
        [FieldOffset(8)] public uint Buttons;
        [FieldOffset(12)] public uint MouseControl;
        [FieldOffset(16)] public uint MouseFlags;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleMode(IntPtr h, out uint mode);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleMode(IntPtr h, uint mode);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNumberOfConsoleInputEvents(IntPtr h, out uint count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadConsoleInputW(IntPtr h, [Out] InputRecord[] records, uint length, out uint count);
}
