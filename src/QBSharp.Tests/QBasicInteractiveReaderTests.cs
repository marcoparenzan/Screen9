using QBSharp.Cli;

namespace QBSharp.Tests;

public sealed class QBasicInteractiveReaderTests
{
    [Fact]
    public void NativeStyleLineEditingUsesSharedKeyEvents()
    {
        var keys=new Queue<string>(["A","B","\b","\0H","C","\r"]);using var output=new StringWriter();
        using var reader=new QBasicInteractiveReader(()=>keys.Dequeue(),output,CancellationToken.None,false);
        Assert.Equal("AC",reader.ReadLine());Assert.Contains("\b \b",output.ToString(),StringComparison.Ordinal);
    }
    [Fact]
    public void ControlZReturnsEofForAnEmptyLine()
    {
        using var reader=new QBasicInteractiveReader(()=>"\x1a",TextWriter.Null,CancellationToken.None,false);Assert.Null(reader.ReadLine());
    }
    [Fact]
    public void CancellationRestoresCursorAndWrap()
    {
        using var token=new CancellationTokenSource();using var output=new StringWriter();
        using var reader=new QBasicInteractiveReader(()=>{token.Cancel();return "";},output,token.Token);
        Assert.ThrowsAny<OperationCanceledException>(()=>reader.ReadLine());Assert.EndsWith("\x1b[?25l\x1b[?7l",output.ToString(),StringComparison.Ordinal);
    }
}
