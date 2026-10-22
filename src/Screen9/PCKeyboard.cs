namespace Screen9;

public static class PCKeyboard
{
    /// <summary>Encodes console keys as QBasic INKEY$ strings (NUL + scan code for extended keys).</summary>
    public static string Encode(ConsoleKeyInfo key)
    {
        int scan=key.Key switch
        {
            ConsoleKey.UpArrow=>72,ConsoleKey.DownArrow=>80,ConsoleKey.LeftArrow=>75,ConsoleKey.RightArrow=>77,
            ConsoleKey.Home=>71,ConsoleKey.End=>79,ConsoleKey.PageUp=>73,ConsoleKey.PageDown=>81,
            ConsoleKey.Insert=>82,ConsoleKey.Delete=>83,
            >=ConsoleKey.F1 and <=ConsoleKey.F10=>59+key.Key-ConsoleKey.F1,
            ConsoleKey.F11=>133,ConsoleKey.F12=>134,_=>0
        };
        if(scan!=0)return "\0"+(char)scan;
        return key.Key switch {ConsoleKey.Enter=>"\r",ConsoleKey.Tab=>"\t",ConsoleKey.Backspace=>"\b",ConsoleKey.Escape=>"\x1b",_=>key.KeyChar is >'\0' and <='\xff'?key.KeyChar.ToString():""};
    }
}
