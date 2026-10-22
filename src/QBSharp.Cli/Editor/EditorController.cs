using QBSharp.Core.Syntax;
using QBSharp.AI;

namespace QBSharp.Cli.Editor;

public sealed class EditorDialog(string title, string text, IReadOnlyList<string> choices, Func<string, Task> accept, Action? cancel = null, bool input = false)
{
    public string Title { get; } = title;
    public string Text { get; set; } = text;
    public IReadOnlyList<string> Choices { get; } = choices;
    public int Selected { get; set; }
    public bool Input { get; } = input;
    public HashSet<int> Checked { get; } = [];
    public bool Multi { get; init; }
    public Func<string, Task> Accept { get; } = accept;
    public Action? Cancel { get; } = cancel;
}

public sealed class EditorController(EditorDocument document, EditorRunner runner, EditorAiWorkflow? ai = null,string initialDialect="qbs")
{
    public string Dialect { get; private set; }=ValidateDialect(initialDialect);
    public bool ProgramView { get; private set; }
    public EditorProgramFrame? ProgramFrame { get; private set; }
    public int ProgramTop { get; private set; }
    public int ProgramLeft { get; private set; }
    private static string ValidateDialect(string dialect)=>dialect is "qbs" or "qbasic"?dialect:throw new ArgumentException("Dialect must be qbs or qbasic.",nameof(dialect));
    public void SetDialect(string dialect)
    {
        ValidateDialect(dialect);
        if(Runner.Running || Ai?.Running==true || AiPreview is not null)throw new InvalidOperationException("Stop the current operation before changing dialect.");
        Dialect=dialect;LastResult=null;ProgramFrame=null;ProgramView=false;Status="Dialect: "+dialect;
    }
    public EditorDocument Document { get; } = document;
    public EditorRunner Runner { get; } = runner;
    public EditorAiWorkflow? Ai { get; } = ai;
    public EditorCodeProposal? AiPreview { get; private set; }
    public int AiPreviewScroll { get; private set; }
    public int AiPreviewLeft { get; private set; }
    public EditorDialog? Dialog { get; private set; }
    public int Menu { get; private set; } = -1;
    public int MenuItem { get; private set; }
    public int TopLine { get; private set; }
    public int LeftColumn { get; private set; }
    public int Width { get; private set; } = 80;
    public int Height { get; private set; } = 25;
    public int CodeHeight => Math.Max(1, Height - 10);
    public int OutputTop => CodeHeight + 3;
    public List<string> Output { get; } = [];
    public int OutputScroll { get; private set; }
    public bool OutputFocus { get; private set; }
    public bool Exit { get; private set; }
    public string Status { get; private set; } = "F1 Help | F2 Save | F3 Open | F5 Run | F6 Stop | F10 Menu";
    public string Clipboard { get; private set; } = "";
    public EditorRunResult? LastResult { get; private set; }
    private SourceSpan? openAt;
    public static readonly string[] MenuNames = ["File", "Edit", "Run", "Help"];
    public static readonly string[][] MenuItems =
    [
        ["New", "Open (F3)", "Save (F2)", "Save As", "Exit"],
        ["Undo", "Redo", "Cut", "Copy", "Paste", "Select All"],
        ["Run (F5)", "Stop (F6)", "Go to error (F4)", "Clear output", "Ralf generate/edit (F7)", "Language / dialect"],
        ["Keyboard help", "About"]
    ];
    public void Resize(int width, int height) { Width = Math.Clamp(width, 40, 300); Height = Math.Clamp(height, 12, 100); EnsureVisible(); }
    private void EnsureVisible()
    {
        var (line, column) = Document.Position;
        if (line < TopLine) TopLine = line;
        if (line >= TopLine + CodeHeight) TopLine = line - CodeHeight + 1;
        if (column < LeftColumn) LeftColumn = column;
        if (column >= LeftColumn + Width - 8) LeftColumn = column - Width + 9;
    }
    public void Tick()
    {
        if (Ai?.TakeResult() is { } aiResult) { AiPreview = aiResult.Proposal; AiPreviewScroll = 0; AiPreviewLeft = 0; Status = aiResult.Status; }
        if(Runner.TryTakeFrame(out var frame))ProgramFrame=frame;
        while (Runner.TryReadOutput(out var line)) { Output.Add(line!); if (Output.Count > 1000) Output.RemoveAt(0); }
        var result = Runner.TakeResult();
        if (result is not null) { ProgramView=false; LastResult = result; Status = result.Status + (result.Diagnostic is null ? "" : " | F4 go to error"); }
        if (Dialog is null && Runner.TryReadPrompt(out var prompt) && prompt is not null && !prompt.Completion.Task.IsCompleted)
        {
            var choices = prompt.Kind is EditorPromptKind.Select or EditorPromptKind.MultiSelect ? prompt.Choices : new[] { "OK" };
            var dialog = new EditorDialog(prompt.Title, prompt.Default, choices, value =>
            {
                object? answer = prompt.Kind == EditorPromptKind.MultiSelect ? prompt.Choices.Where((_, i) => activeChecks.Contains(i)).ToList() : value;
                prompt.Completion.TrySetResult(answer); return Task.CompletedTask;
            }, () => { prompt.Completion.TrySetCanceled(); Runner.Cancel(); }, prompt.Kind == EditorPromptKind.Input)
            { Multi = prompt.Kind == EditorPromptKind.MultiSelect };
            Dialog = dialog; promptDialog = dialog; activeChecks = dialog.Checked;
        }
        if (result is not null && ReferenceEquals(Dialog, promptDialog)) Dialog = null;
    }
    private HashSet<int> activeChecks = [];
    private EditorDialog? promptDialog;
    public async Task HandleAsync(EditorInput input)
    {
        try
        {
            if (input.Key == EditorKey.F6) { Runner.Cancel(); Ai?.Cancel(); return; }
            if (AiPreview is not null) { HandleAiPreview(input); return; }
            if (Dialog is not null) { await HandleDialogAsync(input); return; }
            if(input.Key==EditorKey.F8){ProgramView=!ProgramView && (ProgramFrame is not null || Runner.Running && Dialect=="qbasic" && !Runner.UsesWindow);return;}
            if(ProgramView && input.Modifiers.HasFlag(EditorModifiers.Control) && input.Text.Equals("Q",StringComparison.OrdinalIgnoreCase)){Quit();return;}
            if(ProgramView && Menu<0 && input.X<0 && !input.Modifiers.HasFlag(EditorModifiers.Alt) && input.Key is not(EditorKey.F1 or EditorKey.F2 or EditorKey.F3 or EditorKey.F4 or EditorKey.F5 or EditorKey.F6 or EditorKey.F7 or EditorKey.F10))
            {
                if(input.Modifiers.HasFlag(EditorModifiers.Control) && input.Key is EditorKey.PageUp or EditorKey.PageDown)
                    ProgramTop=Math.Clamp(ProgramTop+(input.Key==EditorKey.PageUp?-5:5),0,Math.Max(0,(ProgramFrame?.Height??0)-1));
                else if(input.Modifiers.HasFlag(EditorModifiers.Control) && input.Key is EditorKey.Left or EditorKey.Right)
                    ProgramLeft=Math.Clamp(ProgramLeft+(input.Key==EditorKey.Left?-5:5),0,Math.Max(0,(ProgramFrame?.Width??0)-1));
                else if(Runner.Running)Runner.SendKey(input);
                return;
            }
            if (input.X >= 0)
            {
                if(ProgramView && input.Wheel!=0){ProgramTop=Math.Clamp(ProgramTop+input.Wheel,0,Math.Max(0,(ProgramFrame?.Height??0)-1));return;}
                if (input.Wheel != 0)
                {
                    if (input.Y >= OutputTop) OutputScroll = Math.Clamp(OutputScroll - input.Wheel, 0, Math.Max(0, Output.Count - 1));
                    else TopLine = Math.Clamp(TopLine + input.Wheel, 0, Math.Max(0, Document.LineCount - 1));
                    return;
                }
                if (!input.Click) return;
                if (input.Y == 0) { Menu = Math.Clamp(input.X / 12, 0, 3); MenuItem = 0; return; }
                if (Menu >= 0 && input.X >= Menu * 12 && input.X < Menu * 12 + 28 && input.Y >= 1 && input.Y <= MenuItems[Menu].Length)
                { MenuItem = input.Y - 1; await MenuActionAsync(); return; }
                Menu = -1;
                if(ProgramView)return;
                if (input.Y >= 2 && input.Y < CodeHeight + 2)
                { OutputFocus = false; Document.MoveTo(TopLine + input.Y - 2, LeftColumn + Math.Max(0, input.X - 7), input.Modifiers.HasFlag(EditorModifiers.Shift)); }
                else if (input.Y >= OutputTop) { OutputFocus = true; if (LastResult?.Diagnostic is not null) await GoToErrorAsync(); }
                return;
            }
            if (input.Key == EditorKey.F10) { Menu = Menu < 0 ? 0 : -1; MenuItem = 0; return; }
            if (input.Modifiers.HasFlag(EditorModifiers.Alt))
            { int menu = input.Text.ToUpperInvariant() switch { "F" => 0, "E" => 1, "R" => 2, "H" => 3, _ => -1 }; if (menu >= 0) { Menu = menu; MenuItem = 0; } return; }
            if (Menu >= 0)
            {
                switch (input.Key)
                {
                    case EditorKey.Escape: Menu = -1; break;
                    case EditorKey.Left: Menu = (Menu + 3) % 4; MenuItem = 0; break;
                    case EditorKey.Right: Menu = (Menu + 1) % 4; MenuItem = 0; break;
                    case EditorKey.Up: MenuItem = (MenuItem + MenuItems[Menu].Length - 1) % MenuItems[Menu].Length; break;
                    case EditorKey.Down: MenuItem = (MenuItem + 1) % MenuItems[Menu].Length; break;
                    case EditorKey.Enter: await MenuActionAsync(); break;
                }
                return;
            }
            if (input.Modifiers.HasFlag(EditorModifiers.Control))
            {
                switch (input.Text.ToUpperInvariant())
                {
                    case "S": await SaveAsync(); break;
                    case "O": OpenDialog(); break;
                    case "N": ReplaceDocument(Document.New); break;
                    case "Q": Quit(); break;
                    case "Z": Document.Undo(); break;
                    case "Y": Document.Redo(); break;
                    case "A": Document.SelectAll(); break;
                    case "C": Clipboard = Document.SelectedText; break;
                    case "X": Clipboard = Document.SelectedText; if (Document.SelectionLength != 0) Document.Insert(""); break;
                    case "V": Document.Insert(Clipboard); break;
                }
                if (input.Key == EditorKey.Home) Document.Move(0, input.Modifiers.HasFlag(EditorModifiers.Shift));
                if (input.Key == EditorKey.End) Document.Move(Document.Text.Length, input.Modifiers.HasFlag(EditorModifiers.Shift));
                return;
            }
            bool select = input.Modifiers.HasFlag(EditorModifiers.Shift); var pos = Document.Position;
            switch (input.Key)
            {
                case EditorKey.F1: Help(); break;
                case EditorKey.F2: await SaveAsync(); break;
                case EditorKey.F3: OpenDialog(); break;
                case EditorKey.F4: ProgramView=false; await GoToErrorAsync(); break;
                case EditorKey.F5: StartRun(); break;
                case EditorKey.F7: BeginAi(); break;
                case EditorKey.Escape: if (Ai?.Running == true) Ai.Cancel(); else if (Runner.Running) Runner.Cancel(); else OutputFocus = !OutputFocus; break;
                case EditorKey.Left: Document.Horizontal(-1, select); break;
                case EditorKey.Right: Document.Horizontal(1, select); break;
                case EditorKey.Up: if (OutputFocus) OutputScroll = Math.Min(OutputScroll + 1, Output.Count); else Document.MoveTo(pos.Line - 1, pos.Column, select); break;
                case EditorKey.Down: if (OutputFocus) OutputScroll = Math.Max(0, OutputScroll - 1); else Document.MoveTo(pos.Line + 1, pos.Column, select); break;
                case EditorKey.PageUp: if (OutputFocus) OutputScroll = Math.Min(OutputScroll + 5, Output.Count); else Document.MoveTo(pos.Line - CodeHeight, pos.Column, select); break;
                case EditorKey.PageDown: if (OutputFocus) OutputScroll = Math.Max(0, OutputScroll - 5); else Document.MoveTo(pos.Line + CodeHeight, pos.Column, select); break;
                case EditorKey.Home: Document.MoveTo(pos.Line, 0, select); break;
                case EditorKey.End: Document.MoveTo(pos.Line, Document.Lines[pos.Line].Length, select); break;
                case EditorKey.Enter: if (!OutputFocus) Document.Insert(Document.NewLine); break;
                case EditorKey.Tab: if (!OutputFocus) Document.Insert("    "); break;
                case EditorKey.Backspace: if (!OutputFocus) Document.Backspace(); break;
                case EditorKey.Delete: if (!OutputFocus) Document.Delete(); break;
                case EditorKey.Text: if (!OutputFocus && input.Text.Length != 0 && !input.Text.Any(char.IsControl)) Document.Insert(input.Text); break;
            }
        }
        catch (Exception error) when (error is RalfException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Message("Editor error", error.Message); }
        finally { if (input.Wheel == 0) EnsureVisible(); }
    }
    private async Task HandleDialogAsync(EditorInput input)
    {
        var dialog = Dialog!;
        if (input.Key == EditorKey.Escape) { Dialog = null; dialog.Cancel?.Invoke(); return; }
        if (input.Click)
        {
            var bounds = EditorFrame.DialogBounds(Width, Height);
            if (input.Y == bounds.Y + bounds.Height - 2)
            { if (input.X >= bounds.X + bounds.Width / 2) { Dialog = null; dialog.Cancel?.Invoke(); } else await AcceptDialogAsync(); return; }
            int row = input.Y - bounds.Y - 3 + Math.Max(0, dialog.Selected - 3);
            if (!dialog.Input && row >= 0 && row < dialog.Choices.Count) { dialog.Selected = row; if (dialog.Multi) Toggle(dialog); }
            return;
        }
        if(ProgramView && input.Wheel!=0){ProgramTop=Math.Clamp(ProgramTop+input.Wheel,0,Math.Max(0,(ProgramFrame?.Height??0)-1));return;}
                if (input.Wheel != 0) { dialog.Selected = Math.Clamp(dialog.Selected + input.Wheel, 0, Math.Max(0, dialog.Choices.Count - 1)); return; }
        if (input.Key == EditorKey.Up) dialog.Selected = Math.Max(0, dialog.Selected - 1);
        if (input.Key is EditorKey.Down or EditorKey.Tab) dialog.Selected = Math.Min(dialog.Choices.Count - 1, dialog.Selected + 1);
        if (input.Key == EditorKey.Text && input.Text == " " && dialog.Multi) Toggle(dialog);
        else if (dialog.Input && input.Key == EditorKey.Text && !input.Modifiers.HasFlag(EditorModifiers.Control) && !input.Text.Any(char.IsControl) && dialog.Text.Length < 4096) dialog.Text += input.Text;
        if (dialog.Input && input.Key == EditorKey.Backspace && dialog.Text.Length != 0) dialog.Text = dialog.Text[..^1];
        if (input.Key == EditorKey.Enter) await AcceptDialogAsync();
    }
    private static void Toggle(EditorDialog dialog) { if (!dialog.Checked.Add(dialog.Selected)) dialog.Checked.Remove(dialog.Selected); }
    private async Task AcceptDialogAsync()
    { var dialog = Dialog!; Dialog = null; await dialog.Accept(dialog.Input ? dialog.Text : dialog.Choices[dialog.Selected]); }
    private Task MenuActionAsync()
    {
        int menu = Menu, item = MenuItem; Menu = -1;
        switch (menu, item)
        {
            case (0, 0): ReplaceDocument(Document.New); break;
            case (0, 1): OpenDialog(); break;
            case (0, 2): return SaveAsync();
            case (0, 3): SaveAsDialog(); break;
            case (0, 4): Quit(); break;
            case (1, 0): Document.Undo(); break;
            case (1, 1): Document.Redo(); break;
            case (1, 2): Clipboard = Document.SelectedText; if (Document.SelectionLength != 0) Document.Insert(""); break;
            case (1, 3): Clipboard = Document.SelectedText; break;
            case (1, 4): Document.Insert(Clipboard); break;
            case (1, 5): Document.SelectAll(); break;
            case (2, 0): StartRun(); break;
            case (2, 1): Runner.Cancel(); break;
            case (2, 2): return GoToErrorAsync();
            case (2, 3): Output.Clear(); OutputScroll = 0; break;
            case (2, 4): BeginAi(); break;
            case (2, 5):
                if(Runner.Running || Ai?.Running==true){Message("Dialect", "Stop the current operation before changing dialect.");break;}
                Dialog=new("Language / dialect", "Choose the language used by F5; no file is changed.", ["QBS (modern)","QBasic (classic)"], choice=>{SetDialect(choice.StartsWith("QBasic",StringComparison.Ordinal)?"qbasic":"qbs");return Task.CompletedTask;}){Selected=Dialect=="qbasic"?1:0};break;
            case (3, 0): Help(); break;
            case (3, 1): Message("QBSharp", "AST editor .NET 10 / Screen9. Ralf: F7, explicit consent and preview. No automatic execution."); break;
        }
        return Task.CompletedTask;
    }
    private void StartRun()
    { if (Runner.Running || Ai?.Running == true) return; Output.Clear(); OutputScroll = 0; LastResult = null; ProgramFrame=null;ProgramTop=ProgramLeft=0;ProgramView=Dialect=="qbasic" && !Runner.UsesWindow;Runner.Start(Document.Text, Document.FilePath,maximumSteps:Dialect=="qbasic"?QBasicRunPolicy.InteractiveMaximumSteps:1_000_000,dialect:Dialect); Status = Dialect=="qbasic" && Runner.UsesWindow ? "Running in the QBasic window | F6 Stop" : "Running | F6 Stop (cooperative cancellation)"; }
    private void Message(string title, string message) => Dialog = new(title, message, ["OK"], _ => Task.CompletedTask);
        private void BeginAi()
    {
        if(Dialect=="qbasic"){Message("RalfAI", "AI code editing currently supports QBS only. Switch dialect explicitly before using F7.");return;}
        if (Ai is null) { Message("RalfAI", "AI service is not configured in this editor host."); return; }
        if (Runner.Running || Ai.Running) { Status = "Stop the current operation before generating code."; return; }
        Dialog = new("RalfAI disclosure", "Sends the CURRENT BUFFER and your instruction to the configured AI provider.\nDo not send secrets. Proposal is untrusted: review before applying.\nNo execution or file changes happen automatically.", ["Cancel", "Continue"], choice =>
        {
            if (choice == "Continue") Dialog = new("Ralf instruction", "", ["OK"], instruction =>
            {
                Ai.Start(instruction, Document.Text, Document.FilePath); Status = "RalfAI generating | F6 Stop"; return Task.CompletedTask;
            }, input: true);
            return Task.CompletedTask;
        });
    }
    private void HandleAiPreview(EditorInput input)
    {
        if (input.Key == EditorKey.Escape || input.Click && input.Y >= Height - 3 && input.X >= Width / 2)
        { AiPreview = null; Status = "AI proposal discarded; no changes."; return; }
        if (input.Key == EditorKey.Enter || input.Click && input.Y >= Height - 3 && input.X < Width / 2)
        { var proposal = AiPreview!; AiPreview = null; proposal.Apply(Document); Status = "AI applied to buffer only. Review, then F2 to save; F5 runs only on request."; return; }
        if (input.Key == EditorKey.Left) AiPreviewLeft = Math.Max(0, AiPreviewLeft - 8);
        if (input.Key == EditorKey.Right) AiPreviewLeft = Math.Min(AiPreview!.PreviewLines.Max(line => line.Length), AiPreviewLeft + 8);
        if (input.Key == EditorKey.Home) AiPreviewLeft = 0;
        int delta = input.Wheel != 0 ? input.Wheel : input.Key switch { EditorKey.Down => 1, EditorKey.Up => -1, EditorKey.PageDown => Height - 5, EditorKey.PageUp => 5 - Height, _ => 0 };
        AiPreviewScroll = Math.Clamp(AiPreviewScroll + delta, 0, Math.Max(0, AiPreview!.PreviewLines.Count - 1));
    }
    private void Help() => Message("Keyboard / mouse", "F2 Save F3 Open F4 Error F5 Run F6 Stop F7 Ralf F8 Program view F10 Menu\nQBasic: Run menu selects dialect; program view sends keys. F6 stops.\nCtrl N/O/S/Q, A/C/X/V, Z/Y. Shift arrows select.\nMouse: menus, code cursor, wheel, dialogs, output.\nEsc: close dialog/menu; stop run; toggle output focus.\nClipboard internal. F7 Ralf: consent, preview, then apply. Save/Run stay explicit.");
    private void Quit() => ReplaceDocument(() => { Runner.Cancel(); Ai?.Cancel(); Exit = true; });
    private void ReplaceDocument(Action action)
    {
        if (!Document.Dirty) { action(); TopLine = LeftColumn = 0; return; }
        Dialog = new("Unsaved changes", "Save before leaving this document?", ["Save", "Discard", "Cancel"], async answer =>
        {
            if (answer == "Discard") { action(); TopLine = LeftColumn = 0; }
            if (answer == "Save") await SaveAsync(after: action);
        });
    }
    private void OpenDialog()
    {
        Dialog = new("Open .bas", "", ["OK"], value =>
        {
            ReplaceDocument(() => OpenConfirmed(value)); return Task.CompletedTask;
        }, input: true);
    }
    private void OpenConfirmed(string path)
    {
        // Keep file operations awaited by the event loop, not async void.
        pendingOpen = path;
    }
    private string? pendingOpen;
    public async Task FlushOpenAsync()
    {
        if (pendingOpen is null) return; string path = pendingOpen; pendingOpen = null;
        try { await Document.OpenAsync(path); if (openAt is { } span) Document.MoveTo(span.Start.Line - 1, span.Start.Column - 1); TopLine = LeftColumn = 0; EnsureVisible(); }
        catch (Exception error) when (error is RalfException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { Message("Open error", error.Message); }
        finally { openAt = null; }
    }
    private void SaveAsDialog(Action? after = null)
    { Dialog = new("Save As .bas", Document.FilePath ?? "", ["OK"], value => SaveAsync(value, after), input: true); }
    private async Task SaveAsync(string? path = null, Action? after = null)
    {
        if (path is null && Document.FilePath is null) { SaveAsDialog(after); return; }
        try { await Document.SaveAsync(path); Status = "Saved " + Document.FilePath; after?.Invoke(); }
        catch (IOException error)
        {
            Dialog = new("Save / overwrite", error.Message, ["Cancel", "Overwrite"], async answer =>
            { if (answer != "Overwrite") return; await Document.SaveAsync(path, overwrite: true); Status = "Saved " + Document.FilePath; after?.Invoke(); });
        }
    }
    private Task GoToErrorAsync()
    {
        var result = LastResult; var span = result?.Diagnostic?.Span;
        if (span is null) { Status = "No source diagnostic."; return Task.CompletedTask; }
        if (span.File == (Document.FilePath ?? "<editor>"))
        {
            if (Document.Text != result!.SourceText) { Status = "Buffer changed since run: rerun to locate error safely."; return Task.CompletedTask; }
            Document.MoveTo(span.Start.Line - 1, span.Start.Column - 1); OutputFocus = false; EnsureVisible();
        }
        else if (File.Exists(span.File)) { openAt = span; ReplaceDocument(() => OpenConfirmed(span.File)); }
        else Status = "Source is not available: " + span.File;
        return Task.CompletedTask;
    }
}
