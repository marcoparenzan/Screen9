using QBSharp.Cli;

namespace QBSharp.Tests;

public sealed class CliInputTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("unknown")]
    [InlineData("--version extra")]
    [InlineData("run a.bas b.bas")]
    [InlineData("run a.bas --bad")]
    [InlineData("run a.bas --max-steps")]
    [InlineData("run a.bas --max-steps 0")]
    [InlineData("run a.bas --max-steps -1")]
    [InlineData("run a.bas --max-steps 1 --max-steps 2")]
    [InlineData("repl extra")]
    [InlineData("new --max-steps 2")]
    [InlineData("complete a b")]
    [InlineData("repl -- x")]
    [InlineData("help unknown")]
    public void InvalidCommandLinesFailClearly(string command) => Assert.Throws<CliUsageException>(() => CommandLine.Parse(command.Split(' ')));
    [Fact]
    public void ArgumentsAfterDelimiterAreLiteral()
    {
        var result = CommandLine.Parse(["run", "space name.bas", "--max-steps", "10", "--", "--help", "a b"]);
        Assert.Equal("space name.bas", result.File); Assert.Equal(10, result.MaximumSteps); Assert.False(result.Help); Assert.Equal(new[] { "--help", "a b" }, result.ScriptArguments);
        Assert.Equal("main.bas", CommandLine.Parse(["new", "main.bas"]).File);
    }
    [Fact]
    public void BlockContinuationIgnoresStringsAndComments()
    {
        var buffer = new ReplBuffer();
        Assert.False(buffer.Append("If True Then ' comment End If"));
        Assert.False(buffer.Append("Print(\"End If\")"));
        Assert.False(buffer.Append("While False"));
        Assert.False(buffer.Append("End While"));
        Assert.True(buffer.Append("End If")); Assert.True(buffer.Pending); buffer.Clear(); Assert.False(buffer.Pending);
        Assert.True(buffer.Append("\"unterminated")); buffer.Clear(); Assert.True(buffer.Append("End While")); buffer.Clear();
        Assert.False(buffer.Append("Function X()")); Assert.True(buffer.Append(""));
        buffer.Clear(); Assert.Throws<CliUsageException>(() => buffer.Append(new string('x', 65_536))); Assert.False(buffer.Pending);
    }
    [Fact]
    public void LineEditorSupportsHistoryBackspaceAndTokenCompletion()
    {
        var buffer = new ReplLineBuffer(); buffer.Start(2);
        buffer.Type('P'); buffer.Type('r'); buffer.Type('\t'); buffer.Complete([]); Assert.Equal("Print", buffer.Text);
        buffer.Backspace(); Assert.Equal("Prin", buffer.Text);
        buffer.History(-1, ["first", "second"]); Assert.Equal("second", buffer.Text);
        buffer.History(-1, ["first", "second"]); Assert.Equal("first", buffer.Text);
        buffer.History(2, ["first", "second"]); Assert.Equal("", buffer.Text);
        foreach (char c in "Print(ans") buffer.Type(c); buffer.Complete(["answer"]); Assert.Equal("Print(answer", buffer.Text);
        buffer.Start(0); buffer.Backspace(); Assert.Equal("", buffer.Text);
    }
    [Fact]
    public void CompletionHandlesCaseDuplicatesAndFilesystemPaths()
    {
        Assert.Equal(new[] { "answer" }, Completion.Repl("ans", ["answer", "ANSWER"]));
        string folder = Path.Combine(Path.GetTempPath(), "qbs-completion-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "space name.bas"), ""); File.WriteAllText(Path.Combine(folder, "space.txt"), ""); Directory.CreateDirectory(Path.Combine(folder, "scripts"));
            Assert.Equal(new[] { "space name.bas" }, Completion.Cli("space", folder));
            Assert.Contains("scripts" + Path.DirectorySeparatorChar, Completion.Cli("scr", folder));
            Assert.Empty(Completion.Cli("does-not-exist/path", folder));
            Assert.Contains("--max-steps", Completion.Cli("--m", folder));
        }
        finally { Directory.Delete(folder, true); }
    }
}
