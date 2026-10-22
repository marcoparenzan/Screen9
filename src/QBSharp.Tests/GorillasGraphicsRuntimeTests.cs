using Screen9;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class GorillasGraphicsRuntimeTests
{
    [Fact]
    public async Task OriginalDrawingProceduresBuildSpritesCityAndSunWithoutChangingSample()
    {
        string root=Path.Combine(Path.GetTempPath(),"qbs-city-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            PCCanvas? frame=null;
            await using var session=new QBasicEngine(new(){BaseDirectory=root,MaximumSteps=200000,Clock=()=>new DateTime(2026,1,1),PresentGraphics=f=>frame=f,Audio=(_,_)=>ValueTask.CompletedTask}).CreateSession();
            string source=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"samples","GORILLA.BAS"));
            var limit=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new(source,"GORILLA.BAS")));
            Assert.Equal("QBS2401",limit.Diagnostic.Code);
            await session.ExecuteAsync(new("""
                SCREEN 9
                ScrWidth=640:ScrHeight=350:GHeight=25:SunHt=39:MaxCol=80
                DrawGorilla 286,175,3
                DrawGorilla 286,175,2
                DrawGorilla 286,175,1
                DIM buildings(0 TO 30) AS XYPoint
                RANDOMIZE 42
                SetScreen
                CLS
                MakeCityScape buildings()
                PlaceGorillas buildings()
                DoSun 0
                ""","graphics-probe.bas"));
            Assert.NotNull(frame);Assert.Contains(frame.PixelSnapshot(),p=>p!=0);
            var sprite=Assert.IsType<QBasicArray>(session.Variables["GorD&"]);
            Assert.Equal(30,Convert.ToInt32(sprite.GetValue(0))&65535);
            Assert.InRange(Convert.ToInt32(session.Variables["LastBuilding"]),1,30);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
