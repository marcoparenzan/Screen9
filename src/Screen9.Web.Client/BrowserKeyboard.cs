namespace Screen9.Web;

/// <summary>Browser keydown events to QBasic INKEY$ strings, the same convention as
/// <see cref="Screen9.PCKeyboard.Encode"/> (NUL + scan code for extended keys) — just driven by a JS
/// KeyboardEvent instead of a .NET ConsoleKeyInfo.</summary>
public static class BrowserKeyboard
{
    public static string Encode(BrowserKeyEvent e)
    {
        int scan = e.Code switch
        {
            "ArrowUp" => 72, "ArrowDown" => 80, "ArrowLeft" => 75, "ArrowRight" => 77,
            "Home" => 71, "End" => 79, "PageUp" => 73, "PageDown" => 81,
            "Insert" => 82, "Delete" => 83,
            "F1" => 59, "F2" => 60, "F3" => 61, "F4" => 62, "F5" => 63,
            "F6" => 64, "F7" => 65, "F8" => 66, "F9" => 67, "F10" => 68,
            "F11" => 133, "F12" => 134,
            _ => 0
        };
        if (scan != 0) return "\0" + (char)scan;
        return e.Key switch
        {
            "Enter" => "\r", "Tab" => "\t", "Backspace" => "\b", "Escape" => "\x1b",
            " " or "Spacebar" => " ",
            _ => e.Key.Length == 1 && e.Key[0] is > '\0' and <= '\xff' ? e.Key : ""
        };
    }
}
