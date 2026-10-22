using Screen9;
using QBSharp.QBasic;
using QBSharp.Core.Hosting;

namespace QBSharp.Tests;

public sealed class QBasicTextContinuationTests
{
    [Fact]
    public async Task TabWithinSamePrintTracksWrap()
    {
        Canvas? frame=null;await using var session=new QBasicEngine(new(){PresentText=c=>frame=c}).CreateSession();
        await session.ExecuteAsync(new("WIDTH 4,3\nPRINT \"ABCDE\";TAB(3);\"Z\";"));
        Assert.NotNull(frame);Assert.Equal('E',frame[0,1].Character);Assert.Equal('Z',frame[2,1].Character);
    }
    [Fact]
    public async Task ControlCharactersAreSanitizedInTextCells()
    {
        Canvas? frame=null;await using var session=new QBasicEngine(new(){PresentText=c=>frame=c}).CreateSession();
        await session.ExecuteAsync(new("PRINT CHR$(27);\"[red]\";"));
        Assert.NotNull(frame);Assert.Equal('?',frame[0,0].Character);Assert.Equal('[',frame[1,0].Character);
    }
}
