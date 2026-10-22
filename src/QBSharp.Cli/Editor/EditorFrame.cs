using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;
using Screen9;

namespace QBSharp.Cli.Editor;

/// <summary>Text attribute as in QBasic: 16 CGA colors (ConsoleColor order), shown in exact RGB.</summary>
public readonly record struct EditorStyle(ConsoleColor Foreground, ConsoleColor Background);
public sealed record EditorCell(char Character, EditorStyle Style);
public sealed record EditorRectangle(int X, int Y, int Width, int Height);

/// <summary>Fixed-width cell grid; no markup and no terminal control characters from script/file data.</summary>
public sealed class EditorFrame(EditorCell[][] cells)
{
    public EditorCell[][] Cells { get; } = cells;
    // QBasic IDE colors: gray code on blue, black on gray bars, white on black highlight.
    public static readonly EditorStyle CodeStyle = new(ConsoleColor.Gray, ConsoleColor.DarkBlue);
    public static readonly EditorStyle BarStyle = new(ConsoleColor.Black, ConsoleColor.Gray);
    public static readonly EditorStyle SelectedStyle = new(ConsoleColor.White, ConsoleColor.Black);
    private static readonly EditorStyle LineNumberStyle = new(ConsoleColor.DarkCyan, ConsoleColor.DarkBlue);
    private static readonly EditorStyle StringStyle = new(ConsoleColor.Green, ConsoleColor.DarkBlue);
    private static readonly EditorStyle NumberStyle = new(ConsoleColor.Cyan, ConsoleColor.DarkBlue);
    private static readonly EditorStyle KeywordStyle = new(ConsoleColor.White, ConsoleColor.DarkBlue);
    private static readonly HashSet<string> Keywords = new("Dim As If Then Else End While For Next To Step Do Loop Until Exit Function Sub Return Class Property Public Namespace New Me Import Of And Or AndAlso OrElse Not Mod True False Nothing".Split(' '), StringComparer.OrdinalIgnoreCase);
    public static char Safe(char value) => char.IsControl(value) ? value == '\t' ? ' ' : '.' : value is >= ' ' and <= '\u00FF' ? value : '?';
    public static EditorRectangle DialogBounds(int width, int height) => new(2, Math.Max(1, (height - Math.Min(12, height - 2)) / 2), width - 4, Math.Min(12, height - 2));
    public static EditorFrame Build(EditorController editor)
    {
        int width = editor.Width, height = editor.Height - 1;
        var cells = Enumerable.Range(0, height).Select(_ => Enumerable.Repeat(new EditorCell(' ', CodeStyle), width).ToArray()).ToArray();
        void Put(int x, int y, string text, EditorStyle? style = null)
        {
            if (y < 0 || y >= height) return;
            for (int i = 0; i < text.Length && x + i < width; i++) if (x + i >= 0) cells[y][x + i] = new(Safe(text[i]), style ?? CodeStyle);
        }
        void Bar(int y, string text, EditorStyle? style = null) { cells[y] = Enumerable.Repeat(new EditorCell(' ', style ?? BarStyle), width).ToArray(); Put(0, y, text, style ?? BarStyle); }
        Bar(0, "");
        for (int i = 0; i < 4; i++) Put(i * 12, 0, " " + EditorController.MenuNames[i] + " ", editor.Menu == i ? SelectedStyle : BarStyle);
        Bar(1, " QBSharp ["+editor.Dialect+"] | " + (editor.Document.FilePath ?? "Untitled.bas") + (editor.Document.Dirty ? " *" : ""));
        if(!editor.ProgramView && editor.AiPreview is null)
        {
        var lines = editor.Document.LineView;
        int selectionLineOffset = 0;
        for (int i = 0; i < editor.TopLine; i++) { selectionLineOffset += lines[i].Length; if (selectionLineOffset < editor.Document.Text.Length && editor.Document.Text[selectionLineOffset] == '\r' && selectionLineOffset + 1 < editor.Document.Text.Length && editor.Document.Text[selectionLineOffset + 1] == '\n') selectionLineOffset += 2; else selectionLineOffset++; }
        for (int row = 0; row < editor.CodeHeight && row + 2 < height; row++)
        {
            int index = editor.TopLine + row; if (index >= lines.Count) break;
            Put(0, row + 2, (index + 1).ToString().PadLeft(6) + " ", LineNumberStyle);
            string line = lines[index];
            Put(7, row + 2, line.Length > editor.LeftColumn ? line[editor.LeftColumn..Math.Min(line.Length, editor.LeftColumn + width - 7)] : "");
            var tokens = new Lexer().Scan(new ScriptSource(line[..Math.Min(4096, line.Length)]));
            foreach (var token in tokens.Tokens)
            {
                EditorStyle? style = token.Kind == TokenKind.String ? StringStyle : token.Kind == TokenKind.Number ? NumberStyle : Keywords.Contains(token.Text) ? KeywordStyle : null;
                if (style is not { } tokenStyle) continue;
                for (int i = token.Span.Start.Offset; i < token.Span.Start.Offset + token.Span.Length; i++)
                { int x = i - editor.LeftColumn + 7; if (x >= 7 && x < width) cells[row + 2][x] = new(Safe(line[i]), tokenStyle); }
            }
            for (int x = 7; x < width; x++)
            {
                int offset = selectionLineOffset + editor.LeftColumn + x - 7;
                if (offset >= editor.Document.SelectionStart && offset < editor.Document.SelectionStart + editor.Document.SelectionLength && offset < selectionLineOffset + line.Length) cells[row + 2][x] = cells[row + 2][x] with { Style = SelectedStyle };
            }
            selectionLineOffset += line.Length;
            if (selectionLineOffset < editor.Document.Text.Length && editor.Document.Text[selectionLineOffset] == '\r' && selectionLineOffset + 1 < editor.Document.Text.Length && editor.Document.Text[selectionLineOffset + 1] == '\n') selectionLineOffset += 2; else selectionLineOffset++;
        }
        Bar(editor.CodeHeight + 2, " Output | " + (editor.Runner.Running ? "Running / F6 stop" : editor.LastResult?.Status ?? "Ready") + (editor.OutputFocus ? " [focus]" : ""));
        int visible = height - editor.OutputTop - 2;
        int start = Math.Max(0, editor.Output.Count - visible - editor.OutputScroll);
        for (int i = 0; i < visible && start + i < editor.Output.Count; i++) Put(0, editor.OutputTop + i, editor.Output[start + i]);
        var position = editor.Document.Position;
        Bar(height - 2, " " + editor.Status);
        Bar(height - 1, $" Ln {position.Line + 1} Col {position.Column + 1} | UTF-8 | F1 Help F2 Save F3 Open F4 Error F5 Run F6 Stop F7 Ralf F10 Menu");
        if (!editor.OutputFocus && editor.Dialog is null && editor.Menu < 0)
        {
            int x = position.Column - editor.LeftColumn + 7, y = position.Line - editor.TopLine + 2;
            if (x >= 7 && x < width && y >= 2 && y < editor.CodeHeight + 2) cells[y][x] = cells[y][x] with { Style = SelectedStyle };
        }
        }
        if(editor.ProgramView)
        {
            
            for(int y=2;y<height-2;y++)
            {
                cells[y]=Enumerable.Repeat(new EditorCell(' ',new(ConsoleColor.Gray,ConsoleColor.Black)),width).ToArray();
                if(editor.ProgramFrame is not { } frame)continue;
                int row=y-2+editor.ProgramTop;if(row>=frame.Height)continue;
                for(int x=0;x<width && x+editor.ProgramLeft<frame.Width;x++)
                {var cell=frame[x+editor.ProgramLeft,row];cells[y][x]=new(ProgramSafe(cell.Character),new(cell.Foreground,cell.Background));}
            }
            Bar(height-2," QBasic program | F8 code/view | F6 Stop | Ctrl+Q Exit");
            Bar(height-1," Keys go to program. Wheel/Ctrl+Pg scroll; Ctrl+arrows pan. F8 to edit.");
        }
        if (editor.Menu >= 0)
        {
            int x = editor.Menu * 12; var items = EditorController.MenuItems[editor.Menu];
            for (int i = 0; i < items.Length && i + 1 < height; i++) Put(x, i + 1, items[i].PadRight(28), i == editor.MenuItem ? SelectedStyle : BarStyle);
        }
        if (editor.Dialog is { } dialog)
        {
            var bounds = DialogBounds(width, editor.Height);
            for (int y = bounds.Y; y < bounds.Y + bounds.Height && y < height; y++) Put(bounds.X, y, new string(' ', bounds.Width), BarStyle);
            Put(bounds.X, bounds.Y, "+" + new string('-', bounds.Width - 2) + "+", BarStyle);
            Put(bounds.X + 2, bounds.Y + 1, dialog.Title[..Math.Min(dialog.Title.Length, bounds.Width - 4)], SelectedStyle);
            if (dialog.Input) Put(bounds.X + 2, bounds.Y + 3, (dialog.Text.Length > bounds.Width - 6 ? dialog.Text[^(bounds.Width - 6)..] : dialog.Text) + "_", SelectedStyle);
            else if (dialog.Choices.Count == 1 && !dialog.Multi)
            {
                var textLines = dialog.Text.Replace("\r", "").Split('\n');
                for (int i = 0; i < textLines.Length && i < bounds.Height - 5; i++) Put(bounds.X + 2, bounds.Y + 3 + i, textLines[i][..Math.Min(textLines[i].Length, bounds.Width - 4)], BarStyle);
            }
            else
            {
                int first = Math.Max(0, dialog.Selected - 3);
                for (int i = first; i < dialog.Choices.Count && i - first < bounds.Height - 5; i++) Put(bounds.X + 2, bounds.Y + 3 + i - first, ((dialog.Multi ? dialog.Checked.Contains(i) ? "[x] " : "[ ] " : "") + dialog.Choices[i]).PadRight(bounds.Width - 4)[..(bounds.Width - 4)], i == dialog.Selected ? SelectedStyle : BarStyle);
            }
            Put(bounds.X + 2, bounds.Y + bounds.Height - 2, "[ Enter / OK ]", SelectedStyle);
            Put(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height - 2, "[ Esc / Cancel ]", BarStyle);
        }
                if (editor.AiPreview is { } proposal)
        {
            Bar(0, " RalfAI PREVIEW: untrusted code, not saved/executed", SelectedStyle);
            Bar(1, " Scroll: arrows/PgUp/PgDn/mouse wheel; Enter applies, Esc discards");
            var preview = proposal.PreviewLines;
            for (int y = 2; y < height - 2; y++)
            {
                cells[y] = Enumerable.Repeat(new EditorCell(' ', CodeStyle), width).ToArray();
                int index = editor.AiPreviewScroll + y - 2;
                if (index < preview.Count) Put(0, y, preview[index].Length > editor.AiPreviewLeft ? preview[index][editor.AiPreviewLeft..] : "", preview[index].StartsWith("+", StringComparison.Ordinal) ? StringStyle : CodeStyle);
            }
            Bar(height - 2, " [Enter / Apply to buffer]", SelectedStyle);
            Put(width / 2, height - 2, "[Esc / Discard]", BarStyle);
            Bar(height - 1, " Applying is one undo transaction; Save and Run remain separate actions.");
        }
        return new(cells);
    }
    public static char ProgramSafe(char value)=>value switch
    {
        'Ä'=>'─','Í'=>'═','³'=>'│','º'=>'║','Û'=>'█','É'=>'╔','»'=>'╗','È'=>'╚','¼'=>'╝',
        'Ú'=>'┌','¿'=>'┐','À'=>'└','Ù'=>'┘',_=>Safe(value)
    };
    /// <summary>The frame as a Screen9 cell canvas, drawn by <see cref="AnsiCanvasRenderer"/> with the exact CGA palette.</summary>
    public Canvas ToCanvas()
    {
        var canvas=new Canvas(Cells[0].Length,Cells.Length);
        for(int y=0;y<Cells.Length;y++)for(int x=0;x<Cells[y].Length && x<canvas.Width;x++)
        {var cell=Cells[y][x];canvas.SetPixel(x,y,new(cell.Character,cell.Style.Foreground,cell.Style.Background));}
        return canvas;
    }
}