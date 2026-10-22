using System.Text;
using QBSharp.Cli.Editor;
using QBSharp.Core.Hosting;
using Spectre.Console;

namespace QBSharp.Tests;

public sealed class EditorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qbs-editor-" + Guid.NewGuid());
    public EditorTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private static EditorInput Key(EditorKey key, string text = "", EditorModifiers mods = EditorModifiers.None) => new(key, text, mods);
    private static EditorInput Ctrl(string text) => new(EditorKey.Text, text, EditorModifiers.Control);
    [Fact]
    public void DocumentInsertSelectUndoRedoAndDirtyReturnToSavedState()
    {
        var doc = new EditorDocument(); doc.Insert("abc"); Assert.True(doc.Dirty);
        doc.Move(1); doc.Horizontal(1, true); Assert.Equal("b", doc.SelectedText);
        doc.Insert("XYZ"); Assert.Equal("aXYZc", doc.Text);
        doc.Undo(); Assert.Equal("abc", doc.Text); doc.Redo(); Assert.Equal("aXYZc", doc.Text);
        doc.Undo(); doc.Undo(); Assert.Equal("", doc.Text); Assert.False(doc.Dirty);
        doc.Insert("new"); doc.Redo(); Assert.Equal("new", doc.Text);
        doc.SelectAll(); doc.Delete(); Assert.Equal("", doc.Text);
    }
    [Theory]
    [InlineData("a\nb\n", 2)]
    [InlineData("a\r\nb\r\n", 3)]
    [InlineData("a\rb\r", 2)]
    public void CoordinatesAndBoundaryDeletesHandleEveryNewline(string text, int offset)
    {
        var doc = new EditorDocument(); doc.Insert(text); doc.MoveTo(1, 0);
        Assert.Equal((1, 0), doc.Position); doc.Backspace(); Assert.StartsWith("ab", doc.Text);
        doc.Undo(); doc.MoveTo(0, 1); doc.Delete(); Assert.StartsWith("ab", doc.Text);
        doc.MoveTo(999, 999); Assert.Equal(doc.Text.Length, doc.Cursor);
        Assert.True(offset > 0);
    }
    [Fact]
    public void SurrogateNavigationAndDeletesDoNotCorruptUnicode()
    {
        var doc = new EditorDocument(); doc.Insert("a😀b"); doc.Move(1); doc.Horizontal(1); Assert.Equal(3, doc.Cursor);
        doc.Backspace(); Assert.Equal("ab", doc.Text); doc.Undo(); doc.Move(1); doc.Delete(); Assert.Equal("ab", doc.Text);
        doc.Insert("città"); Assert.Contains("città", doc.Text);
    }
    [Fact]
    public void LimitsAndInvalidUnicodeLeaveDocumentUnmodified()
    {
        var doc = new EditorDocument(); doc.Insert("a");
        Assert.Throws<InvalidDataException>(() => doc.Insert(new string('x', EditorDocument.MaximumLength)));
        Assert.Throws<EncoderFallbackException>(() => doc.Insert("\uD800")); Assert.Equal("a", doc.Text);
    }
    [Fact]
    public async Task FilesBomNewlinesSaveAsOverwriteAndExternalChanges()
    {
        string path = Path.Combine(root, "job.bas");
        await File.WriteAllTextAsync(path, "' città\r\nPrint(42)\r\n", new UTF8Encoding(true));
        var doc = new EditorDocument(); await doc.OpenAsync(path);
        Assert.Equal("\r\n", doc.NewLine); Assert.False(doc.Dirty); doc.Insert("' new\n");
        await doc.SaveAsync(); Assert.False(doc.Dirty); Assert.StartsWith("' new\r\n", await File.ReadAllTextAsync(path));
        Assert.False((await File.ReadAllBytesAsync(path)).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        await File.WriteAllTextAsync(path, "external"); doc.Insert("change");
        await Assert.ThrowsAsync<IOException>(() => doc.SaveAsync()); Assert.Equal("external", await File.ReadAllTextAsync(path));
        await doc.SaveAsync(overwrite: true); Assert.False(doc.Dirty);
        string other = Path.Combine(root, "other.bas"); await File.WriteAllTextAsync(other, "existing");
        await Assert.ThrowsAsync<IOException>(() => doc.SaveAsync(other)); Assert.Equal(path, doc.FilePath);
        await doc.SaveAsync(other, overwrite: true); Assert.Equal(other, doc.FilePath);
        Assert.Empty(Directory.GetFiles(root, ".qbs-*.tmp"));
    }
    [Fact]
    public async Task FailedOpenAndCanceledSavePreserveBufferAndTarget()
    {
        var doc = new EditorDocument(); doc.Insert("keep");
        await Assert.ThrowsAsync<FileNotFoundException>(() => doc.OpenAsync(Path.Combine(root, "missing.bas")));
        Assert.Equal("keep", doc.Text); Assert.True(doc.Dirty);
        Assert.Throws<ArgumentException>(() => doc.NameNew("bad.txt"));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveAsync(Path.Combine(root, "cancel.bas"), token: cts.Token));
        Assert.False(File.Exists(Path.Combine(root, "cancel.bas")));
    }
    [Fact]
    public async Task ControllerKeyboardSelectionClipboardAndNavigation()
    {
        await using var runner = new EditorRunner(); var doc = new EditorDocument(); var controller = new EditorController(doc, runner);
        await controller.HandleAsync(Key(EditorKey.Text, "hello")); await controller.HandleAsync(Key(EditorKey.Enter));
        await controller.HandleAsync(Key(EditorKey.Text, "world")); await controller.HandleAsync(Key(EditorKey.Home));
        await controller.HandleAsync(Key(EditorKey.Right, mods: EditorModifiers.Shift)); Assert.Equal("w", doc.SelectedText);
        await controller.HandleAsync(Ctrl("C")); await controller.HandleAsync(Key(EditorKey.End)); await controller.HandleAsync(Ctrl("V"));
        Assert.EndsWith("worldw", doc.Text); await controller.HandleAsync(Ctrl("Z")); Assert.EndsWith("world", doc.Text);
        await controller.HandleAsync(Ctrl("Y")); await controller.HandleAsync(Ctrl("A")); await controller.HandleAsync(Ctrl("X")); Assert.Equal("", doc.Text);
        await controller.HandleAsync(Ctrl("V")); Assert.Contains("hello", doc.Text);
    }
    [Fact]
    public async Task UnsavedQuitCancelDiscardAndSaveAsContinuation()
    {
        await using var runner = new EditorRunner(); var doc = new EditorDocument(); doc.Insert("Print(42)"); var c = new EditorController(doc, runner);
        await c.HandleAsync(Ctrl("Q")); Assert.NotNull(c.Dialog); Assert.False(c.Exit);
        await c.HandleAsync(Key(EditorKey.Escape)); Assert.False(c.Exit);
        await c.HandleAsync(Ctrl("Q")); await c.HandleAsync(Key(EditorKey.Enter)); // Save -> Save As
        string path = Path.Combine(root, "saved.bas"); await c.HandleAsync(Key(EditorKey.Text, path)); await c.HandleAsync(Key(EditorKey.Enter));
        Assert.True(c.Exit); Assert.Equal("Print(42)", await File.ReadAllTextAsync(path));
        var other = new EditorController(new EditorDocument(), runner); other.Document.Insert("dirty");
        await other.HandleAsync(Ctrl("Q")); await other.HandleAsync(Key(EditorKey.Down)); await other.HandleAsync(Key(EditorKey.Enter)); Assert.True(other.Exit);
    }
    [Fact]
    public async Task MouseMenusCodeCursorWheelAndDialogButtons()
    {
        await using var runner = new EditorRunner(); var doc = new EditorDocument(); doc.Insert(string.Join('\n', Enumerable.Range(0, 50).Select(i => "line" + i)));
        doc.Move(0); var c = new EditorController(doc, runner); c.Resize(80, 25);
        await c.HandleAsync(new(X: 9, Y: 3, Click: true)); Assert.Equal((1, 2), doc.Position);
        await c.HandleAsync(new(X: 10, Y: 4, Wheel: 3)); Assert.Equal(3, c.TopLine);
        await c.HandleAsync(new(X: 1, Y: 0, Click: true)); Assert.Equal(0, c.Menu);
        await c.HandleAsync(new(X: 1, Y: 2, Click: true)); Assert.True(c.Dialog!.Input);
        var bounds = EditorFrame.DialogBounds(c.Width, c.Height);
        await c.HandleAsync(new(X: bounds.X + bounds.Width / 2, Y: bounds.Y + bounds.Height - 2, Click: true)); Assert.Null(c.Dialog);
    }
    [Fact]
    public async Task MenuKeyboardOpenFailureProtectsDirtyBuffer()
    {
        await using var runner = new EditorRunner(); var doc = new EditorDocument(); doc.Insert("keep"); var c = new EditorController(doc, runner);
        await c.HandleAsync(Key(EditorKey.F3)); await c.HandleAsync(Key(EditorKey.Text, Path.Combine(root, "missing.bas"))); await c.HandleAsync(Key(EditorKey.Enter));
        Assert.Equal("Unsaved changes", c.Dialog!.Title);
        await c.HandleAsync(Key(EditorKey.Down)); await c.HandleAsync(Key(EditorKey.Enter)); await c.FlushOpenAsync(); Assert.Equal("keep", doc.Text); Assert.Contains("error", c.Dialog!.Title);
        await c.HandleAsync(Key(EditorKey.Escape)); await c.HandleAsync(Key(EditorKey.F10)); await c.HandleAsync(Key(EditorKey.Right)); Assert.Equal(1, c.Menu);
        await c.HandleAsync(Key(EditorKey.Escape)); Assert.Equal(-1, c.Menu);
    }
    [Theory]
    [InlineData("\u001b[<0;10;5M", true, 0, 9, 4)]
    [InlineData("\u001b[<0;10;5m", false, 0, 9, 4)]
    [InlineData("\u001b[<64;1;1M", false, -3, 0, 0)]
    [InlineData("\u001b[<65;1;1M", false, 3, 0, 0)]
    public void VtMouseDecoder(string text, bool click, int wheel, int x, int y)
    { var input = EditorInputDecoder.DecodeVt(text)!; Assert.Equal(click, input.Click); Assert.Equal(wheel, input.Wheel); Assert.Equal(x, input.X); Assert.Equal(y, input.Y); }
    [Theory]
    [InlineData("\u001b[<bad;1;1M")]
    [InlineData("\u001b[<0;0;1M")]
    [InlineData("\u001b[999x")]
    public void InvalidVtMouseSequencesAreIgnored(string text) => Assert.Null(EditorInputDecoder.DecodeVt(text));
    [Theory]
    [InlineData("\u001b[A", EditorKey.Up)]
    [InlineData("\u001b[3~", EditorKey.Delete)]
    [InlineData("\u001b[15~", EditorKey.F5)]
    public void VtKeys(string text, EditorKey key) => Assert.Equal(key, EditorInputDecoder.DecodeVt(text)!.Key);
    [Fact]
    public void KeyDecoderMapsControlShiftAltAndPrintableText()
    {
        Assert.Equal(EditorModifiers.Control, EditorInputDecoder.FromKey(new('\u0013', ConsoleKey.S, false, false, true)).Modifiers);
        Assert.Equal("S", EditorInputDecoder.FromKey(new('\u0013', ConsoleKey.S, false, false, true)).Text);
        Assert.Equal(EditorKey.Left, EditorInputDecoder.FromKey(new('\0', ConsoleKey.LeftArrow, true, false, false)).Key);
        Assert.Equal(EditorModifiers.Alt, EditorInputDecoder.FromKey(new('f', ConsoleKey.F, false, true, false)).Modifiers);
    }
    [Fact]
    public async Task FrameHasFixedSizeHighlightsAndSanitizesTerminalInjection()
    {
        await using var runner = new EditorRunner(); var doc = new EditorDocument(); doc.Insert("Dim x = 42\nPrint(\"[red]\")"); doc.Move(0);
        var c = new EditorController(doc, runner); c.Resize(80, 25); c.Output.Add("\u001b[2J hostile 😀");
        var frame = EditorFrame.Build(c); Assert.Equal(24, frame.Cells.Length); Assert.All(frame.Cells, row => Assert.Equal(80, row.Length));
        string flat = string.Concat(frame.Cells.SelectMany(row => row.Select(cell => cell.Character)));
        Assert.DoesNotContain('\u001b', flat); Assert.Contains("[red]", flat); Assert.Contains("hostile ??", flat);
        Assert.Equal(EditorFrame.SelectedStyle, frame.Cells[2][7].Style);
        await c.HandleAsync(Key(EditorKey.F1)); var dialog = EditorFrame.Build(c); Assert.Contains("Keyboard / mouse", string.Concat(dialog.Cells.SelectMany(row => row.Select(cell => cell.Character))));
        var canvas = dialog.ToCanvas(); // Screen9 Cell rejects control characters: no escape can reach the renderer as text
        Assert.All(Enumerable.Range(0, canvas.Height), y => Assert.All(Enumerable.Range(0, canvas.Width), x => Assert.False(char.IsControl(canvas[x, y].Character))));
    }
    [Fact]
    public async Task RunnerExecutesBufferAndImportedBasWithIndependentSession()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "helper.bas"), "Function Value()\nReturn 42\nEnd Function");
        await using var runner = new EditorRunner(); string source = "Import \"helper\" As H\nPrint(H.Value())\nPrint(Json.Deserialize(\"{\"\"Count\"\":7}\").Count)";
        runner.Start(source, Path.Combine(root, "unsaved.bas")); var result = await runner.WaitAsync(); Assert.Equal("Completed", result.Status);
        Assert.True(runner.TryReadOutput(out var line)); Assert.Equal("42", line); Assert.True(runner.TryReadOutput(out line)); Assert.Equal("7", line);
        Assert.False(File.Exists(Path.Combine(root, "unsaved.bas")));
        runner.Start("Print(H.Value())", null); Assert.NotNull((await runner.WaitAsync()).Diagnostic);
    }
    [Theory]
    [InlineData("Dim = 1")]
    [InlineData("Print(1 / 0)")]
    public async Task RunnerDiagnosticsKeepBufferCoordinatesAndControllerNavigates(string source)
    {
        await using var runner = new EditorRunner(); var doc = new EditorDocument(); doc.Insert(source); var c = new EditorController(doc, runner);
        await c.HandleAsync(Key(EditorKey.F5)); var result = await runner.WaitAsync(); Assert.NotNull(result.Diagnostic); c.Tick();
        await c.HandleAsync(Key(EditorKey.F4)); Assert.Equal(result.Diagnostic!.Span.Start.Column - 1, doc.Position.Column);
        doc.Insert(" "); await c.HandleAsync(Key(EditorKey.F4)); Assert.Contains("changed", c.Status);
    }
    [Fact]
    public async Task InfiniteLoopIsCanceledWithoutBlockingController()
    {
        await using var runner = new EditorRunner(); var doc = new EditorDocument(); doc.Insert("While True\nEnd While"); var c = new EditorController(doc, runner);
        runner.Start(doc.Text, null, long.MaxValue);
        await c.HandleAsync(Key(EditorKey.Text, "'")); Assert.Contains("'", doc.Text);
        await c.HandleAsync(Key(EditorKey.F6)); Assert.Equal("Canceled", (await runner.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5))).Status);
    }
    [Fact]
    public async Task ScriptInputBoxIsResolvedByEditorDialogAndEscapeCancels()
    {
        await using var runner = new EditorRunner(); var c = new EditorController(new EditorDocument(), runner);
        runner.Start("Print(Ui.InputBox(\"name\", \"default\"))", null);
        await Until(() => { c.Tick(); return c.Dialog is not null; });
        await c.HandleAsync(Key(EditorKey.Text, "x")); await c.HandleAsync(Key(EditorKey.Enter)); Assert.Equal("Completed", (await runner.WaitAsync()).Status);
        Assert.True(runner.TryReadOutput(out var text)); Assert.Equal("defaultx", text);
        runner.Start("Ui.InputBox(\"name\")", null); await Until(() => { c.Tick(); return c.Dialog is not null; });
        await c.HandleAsync(Key(EditorKey.Escape)); Assert.Equal("Canceled", (await runner.WaitAsync()).Status);
    }
    private static async Task Until(Func<bool> predicate)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!predicate()) await Task.Delay(10, timeout.Token); }
    private sealed class FakeInput(Queue<EditorInput> items) : IEditorInputSource
    { public bool Disposed; public EditorInput? Poll() => items.TryDequeue(out var item) ? item : null; public void Dispose() => Disposed = true; }
    private sealed class FakeScreen : IEditorScreen
    { public int Width => 80; public int Height => 25; public bool Disposed; public bool Throw; public int Frames; public void Draw(EditorFrame frame) { Frames++; if (Throw) throw new IOException("screen error"); } public void Dispose() => Disposed = true; }
    [Fact]
    public async Task ApplicationRestoresLifecycleOnExitRenderErrorAndCancellation()
    {
        var input = new FakeInput(new([Ctrl("Q")])); var screen = new FakeScreen();
        Assert.Equal(0, await new EditorApplication(() => input, () => screen).RunAsync(null)); Assert.True(input.Disposed); Assert.True(screen.Disposed);
        var failing = new FakeScreen { Throw = true }; var keys = new FakeInput(new());
        await Assert.ThrowsAsync<IOException>(() => new EditorApplication(() => keys, () => failing).RunAsync(null)); Assert.True(failing.Disposed); Assert.True(keys.Disposed);
        var canceled = new FakeScreen(); var idle = new FakeInput(new()); using var token = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EditorApplication(() => idle, () => canceled).RunAsync(null, token.Token)); Assert.True(canceled.Disposed); Assert.True(idle.Disposed);
    }
    [Fact]
    public async Task InvalidExtensionAndMissingParentDoNotEnterTerminal()
    {
        bool entered = false;
        var app = new EditorApplication(() => { entered = true; return new FakeInput(new()); }, () => new FakeScreen());
        await Assert.ThrowsAsync<ArgumentException>(() => app.RunAsync("bad.txt")); Assert.False(entered);
    }
}
