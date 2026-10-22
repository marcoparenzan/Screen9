using System.Security.Cryptography;
using System.Text;
using QBSharp.StandardLibrary.Files;

namespace QBSharp.Cli.Editor;

public sealed class EditorDocument
{
    public const int MaximumLength = 1_048_576;
    private sealed record Snapshot(string Text, int Cursor, int? Anchor);
    private readonly List<Snapshot> undo = [], redo = [];
    private string savedText = "";
    private string? indexedText;
    private string[] indexedLines=[];
    private int[] lineStarts=[];
    private string[] IndexLines()
    {
        if(ReferenceEquals(indexedText,Text))return indexedLines;
        indexedLines=Text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        lineStarts=new int[indexedLines.Length];int offset=0;
        for(int i=0;i<indexedLines.Length;i++)
        {
            lineStarts[i]=offset;offset+=indexedLines[i].Length;
            if(i+1<indexedLines.Length)offset+=Text[offset]=='\r' && offset+1<Text.Length && Text[offset+1]=='\n'?2:1;
        }
        indexedText=Text;return indexedLines;
    }
    internal IReadOnlyList<string> LineView=>IndexLines();
    internal int LineCount=>IndexLines().Length;
    
    public string Text { get; private set; } = "";
    public string? FilePath { get; private set; }
    public string NewLine { get; private set; } = Environment.NewLine;
    public int Cursor { get; private set; }
    public int? Anchor { get; private set; }
    public bool Dirty => Text != savedText;
    public int SelectionStart => Math.Min(Anchor ?? Cursor, Cursor);
    public int SelectionLength => Math.Abs((Anchor ?? Cursor) - Cursor);
    public string SelectedText => Text.Substring(SelectionStart, SelectionLength);
    public string[] Lines => (string[])IndexLines().Clone();
    public (int Line, int Column) Position
    {
        get
        {
            IndexLines();int line=Array.BinarySearch(lineStarts,Cursor);
            if(line<0)line=~line-1;
            return(line,Cursor-lineStarts[line]);
        }
    }
    public void New()
    { Text = savedText = ""; FilePath = null;  Cursor = 0; Anchor = null; NewLine = Environment.NewLine; undo.Clear(); redo.Clear(); }
    public async Task OpenAsync(string path, CancellationToken token = default)
    {
        path = ValidatePath(path);
        var files = new FileServices(new() { MaximumFileBytes = MaximumLength * 4 });
        string text = await files.ReadTextAsync(path, token); Check(text);
        
        Text = savedText = text; FilePath = path; 
        NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
        Cursor = 0; Anchor = null; undo.Clear(); redo.Clear();
    }
    public void NameNew(string path) { path = ValidatePath(path); New(); FilePath = path; }
    public static string ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.GetExtension(path).Equals(".bas", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Editor files must have extension .bas.");
        return Path.GetFullPath(path);
    }
    public async Task SaveAsync(string? path = null, bool overwrite = false, CancellationToken token = default)
    {
        string target = ValidatePath(path ?? FilePath ?? throw new InvalidOperationException("Choose a file name with Save As."));
        bool same = string.Equals(target, FilePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (!overwrite && File.Exists(target))
        {
            string current = await new FileServices(new() { MaximumFileBytes = MaximumLength * 4 }).ReadTextAsync(target, token);
            if (!same || current != savedText) throw new IOException("File exists or changed on disk: confirm overwrite or choose another name.");
        }
        await new FileServices(new() { MaximumFileBytes = MaximumLength * 4 }).WriteTextAsync(target, Text, token);
        FilePath = target; savedText = Text;
    }
    public void Move(int offset, bool select = false)
    {
        int next = Math.Clamp(offset, 0, Text.Length);
        if (next > 0 && next < Text.Length && (char.IsLowSurrogate(Text[next]) && char.IsHighSurrogate(Text[next - 1]) || Text[next] == '\n' && Text[next - 1] == '\r')) next--;
        if (select) Anchor ??= Cursor; else Anchor = null;
        Cursor = next;
    }
    public void MoveTo(int line, int column, bool select = false)
    {
        var lines=IndexLines();line=Math.Clamp(line,0,lines.Length-1);int offset=lineStarts[line];
        Move(offset + Math.Clamp(column, 0, lines[line].Length), select);
    }
    public void Horizontal(int direction, bool select = false)
    {
        int next = Cursor;
        if (direction < 0 && next > 0) { next--; if (next > 0 && (char.IsLowSurrogate(Text[next]) || Text[next] == '\n' && Text[next - 1] == '\r')) next--; }
        if (direction > 0 && next < Text.Length) { if (char.IsHighSurrogate(Text[next]) || Text[next] == '\r' && next + 1 < Text.Length && Text[next + 1] == '\n') next++; next++; }
        Move(next, select);
    }
    public void SelectAll() { Anchor = 0; Cursor = Text.Length; }
    public void Insert(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", NewLine, StringComparison.Ordinal);
        string updated = Text.Remove(SelectionStart, SelectionLength).Insert(SelectionStart, text); Check(updated);
        Remember(); Cursor = SelectionStart + text.Length; Text = updated; Anchor = null;
    }
    public void Backspace()
    {
        if (SelectionLength != 0) { Insert(""); return; }
        if (Cursor == 0) return;
        int end = Cursor; Horizontal(-1); Anchor = end; Insert("");
    }
    public void Delete()
    {
        if (SelectionLength != 0) { Insert(""); return; }
        if (Cursor == Text.Length) return;
        int start = Cursor; Horizontal(1); Anchor = start; Insert("");
    }
    private Snapshot Current() => new(Text, Cursor, Anchor);
    private void Remember() { undo.Add(Current()); if (undo.Count > 64) undo.RemoveAt(0); redo.Clear(); }
    public void Undo() => Restore(undo, redo);
    public void Redo() => Restore(redo, undo);
    private void Restore(List<Snapshot> source, List<Snapshot> destination)
    {
        if (source.Count == 0) return;
        destination.Add(Current()); var snapshot = source[^1]; source.RemoveAt(source.Count - 1);
        Text = snapshot.Text; Cursor = snapshot.Cursor; Anchor = snapshot.Anchor;
    }
    private static void Check(string text)
    {
        if (text.Length > MaximumLength) throw new InvalidDataException("Editor document limit: 1 Mi UTF-16 characters.");
        _ = new UTF8Encoding(false, true).GetByteCount(text);
    }
}
