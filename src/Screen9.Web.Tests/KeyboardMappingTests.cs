using QBSharp.Cli.Editor;
using Screen9.Web;

namespace Screen9.Web.Tests;

public sealed class KeyboardMappingTests
{
    private static BrowserKeyEvent Key(string key, string code, bool ctrl = false, bool alt = false, bool shift = false)
        => new(key, code, ctrl, alt, shift);

    [Theory]
    [InlineData("ArrowUp", 72)] [InlineData("ArrowDown", 80)] [InlineData("ArrowLeft", 75)] [InlineData("ArrowRight", 77)]
    [InlineData("Home", 71)] [InlineData("End", 79)] [InlineData("PageUp", 73)] [InlineData("PageDown", 81)]
    [InlineData("Insert", 82)] [InlineData("Delete", 83)] [InlineData("F1", 59)] [InlineData("F10", 68)] [InlineData("F12", 134)]
    public void ExtendedKeysEncodeAsNulPlusScanCode(string code, int scan)
        => Assert.Equal("\0" + (char)scan, BrowserKeyboard.Encode(Key(code, code)));

    [Theory]
    [InlineData("Enter", "\r")] [InlineData("Tab", "\t")] [InlineData("Backspace", "\b")] [InlineData("Escape", "\x1b")]
    public void ControlKeysMatchQBasicInkeyConvention(string key, string expected)
        => Assert.Equal(expected, BrowserKeyboard.Encode(Key(key, key)));

    [Fact]
    public void PlainCharacterPassesThrough()
    {
        Assert.Equal("a", BrowserKeyboard.Encode(Key("a", "KeyA")));
        Assert.Equal("A", BrowserKeyboard.Encode(Key("A", "KeyA", shift: true)));
        Assert.Equal(" ", BrowserKeyboard.Encode(Key(" ", "Space")));
    }

    [Fact]
    public void ModifierOnlyKeysEncodeAsEmpty()
        => Assert.Equal("", BrowserKeyboard.Encode(Key("Shift", "ShiftLeft")));

    [Theory]
    [InlineData("ArrowUp", EditorKey.Up)] [InlineData("ArrowDown", EditorKey.Down)]
    [InlineData("ArrowLeft", EditorKey.Left)] [InlineData("ArrowRight", EditorKey.Right)]
    [InlineData("Home", EditorKey.Home)] [InlineData("End", EditorKey.End)]
    [InlineData("PageUp", EditorKey.PageUp)] [InlineData("PageDown", EditorKey.PageDown)]
    [InlineData("Enter", EditorKey.Enter)] [InlineData("Tab", EditorKey.Tab)]
    [InlineData("Backspace", EditorKey.Backspace)] [InlineData("Delete", EditorKey.Delete)]
    [InlineData("Insert", EditorKey.Insert)] [InlineData("Escape", EditorKey.Escape)]
    [InlineData("F1", EditorKey.F1)] [InlineData("F5", EditorKey.F5)] [InlineData("F10", EditorKey.F10)]
    public void NamedKeysDecodeToTheMatchingEditorKey(string code, EditorKey expected)
    {
        var input = BlazorEditorInput.Decode(Key(code, code));
        Assert.Equal(expected, input.Key);
        Assert.Equal(EditorModifiers.None, input.Modifiers);
    }

    [Fact]
    public void TypedCharacterBecomesTextKey()
    {
        var input = BlazorEditorInput.Decode(Key("x", "KeyX"));
        Assert.Equal((EditorKey.Text, "x"), (input.Key, input.Text));
    }

    [Fact]
    public void CtrlLetterKeepsTheLetterForEditorControllerToDispatch()
    {
        // EditorController reads Modifiers+Text for Ctrl+S/O/N/Q/Z/Y/A/C/X/V, not a dedicated EditorKey.
        var input = BlazorEditorInput.Decode(Key("s", "KeyS", ctrl: true));
        Assert.Equal((EditorKey.Text, "s", EditorModifiers.Control), (input.Key, input.Text, input.Modifiers));
    }

    [Fact]
    public void CtrlHomeKeepsBothTheKeyAndTheModifier()
    {
        // EditorController's Ctrl branch checks input.Key==Home/End directly, so both must survive together.
        var input = BlazorEditorInput.Decode(Key("Home", "Home", ctrl: true, shift: true));
        Assert.Equal((EditorKey.Home, EditorModifiers.Control | EditorModifiers.Shift), (input.Key, input.Modifiers));
    }

    [Fact]
    public void AltLetterKeepsTheLetterForMenuShortcuts()
    {
        var input = BlazorEditorInput.Decode(Key("f", "KeyF", alt: true));
        Assert.Equal((EditorKey.Text, "f", EditorModifiers.Alt), (input.Key, input.Text, input.Modifiers));
    }

    [Fact]
    public void SpaceDecodesAsTextSpace()
    {
        var input = BlazorEditorInput.Decode(Key(" ", "Space"));
        Assert.Equal((EditorKey.Text, " "), (input.Key, input.Text));
    }

    [Fact]
    public void UnmappedKeysDecodeToNone()
    {
        var input = BlazorEditorInput.Decode(Key("Shift", "ShiftLeft", shift: true));
        Assert.Equal(EditorKey.None, input.Key);
    }
}
