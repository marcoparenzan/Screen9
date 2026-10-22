using QBSharp.Cli.Editor;

namespace Screen9.Web;

/// <summary>Browser keydown events to <see cref="EditorInput"/>, the same target shape
/// <see cref="EditorInputDecoder.DecodeVt"/> produces from a ConPTY/VT escape sequence — just a different
/// source. Mouse (click/wheel) isn't wired from the browser yet: the editor stays fully usable from the
/// keyboard alone (F10 opens the menu, arrows navigate it), same as <c>qbs edit</c> without a mouse.</summary>
public static class BlazorEditorInput
{
    public static EditorInput Decode(BrowserKeyEvent e)
    {
        var modifiers = EditorModifiers.None;
        if (e.ShiftKey) modifiers |= EditorModifiers.Shift;
        if (e.CtrlKey) modifiers |= EditorModifiers.Control;
        if (e.AltKey) modifiers |= EditorModifiers.Alt;

        EditorKey? key = e.Code switch
        {
            "Escape" => EditorKey.Escape, "Enter" or "NumpadEnter" => EditorKey.Enter,
            "Backspace" => EditorKey.Backspace, "Delete" => EditorKey.Delete,
            "ArrowLeft" => EditorKey.Left, "ArrowRight" => EditorKey.Right,
            "ArrowUp" => EditorKey.Up, "ArrowDown" => EditorKey.Down,
            "Home" => EditorKey.Home, "End" => EditorKey.End,
            "PageUp" => EditorKey.PageUp, "PageDown" => EditorKey.PageDown,
            "Tab" => EditorKey.Tab, "Insert" => EditorKey.Insert,
            "F1" => EditorKey.F1, "F2" => EditorKey.F2, "F3" => EditorKey.F3, "F4" => EditorKey.F4,
            "F5" => EditorKey.F5, "F6" => EditorKey.F6, "F7" => EditorKey.F7, "F8" => EditorKey.F8, "F10" => EditorKey.F10,
            _ => null
        };
        if (key is { } found) return new EditorInput(Key: found, Modifiers: modifiers);

        // A typed character (letters keep the case/shift state the browser already resolved): Ctrl/Alt
        // shortcuts (Ctrl+S, Alt+F...) still arrive this way, EditorController reads Modifiers + Text together.
        if (e.Key.Length == 1) return new EditorInput(Key: EditorKey.Text, Text: e.Key, Modifiers: modifiers);
        if (e.Key is " " or "Spacebar") return new EditorInput(Key: EditorKey.Text, Text: " ", Modifiers: modifiers);
        return new EditorInput(Modifiers: modifiers);
    }
}
