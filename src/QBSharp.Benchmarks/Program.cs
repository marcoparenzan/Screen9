using System.Diagnostics;
using System.Text;
using System.Text.Json;
using QBSharp.Cli.Editor;
using QBSharp.QBasic;
using QBSharp.Core.Hosting;
using Spectre.Console;

Environment.SetEnvironmentVariable("QBS_AI_PROVIDER","");
const string script="DIM i AS LONG\nSCREEN 9\nFOR i=0 TO 999\nPSET (i MOD 640,(i\\640) MOD 350),i MOD 16\nNEXT i\nPSET (0,0),14";
List<Measurement> measurements=[];
async Task Measure(string name,Func<Task> action)
{
    await action(); // warmup, not measured
    List<(double Time,long Bytes)> trials=[];
    for(int i=0;i<3;i++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long allocated=GC.GetTotalAllocatedBytes(true);var timer=Stopwatch.StartNew();await action();timer.Stop();
        trials.Add((timer.Elapsed.TotalMilliseconds,GC.GetTotalAllocatedBytes(true)-allocated));
    }
    var ordered=trials.OrderBy(t=>t.Time).ToArray();var value=ordered[1];
    measurements.Add(new(name,value.Time,value.Bytes));Console.WriteLine($"{name}: median {value.Time:F2} ms, {value.Bytes:N0} bytes");
}
await Measure("qbasic-headless-1000-pixels",async()=>
{
    Screen9.PCCanvas? canvas=null;
    await using var session=new QBasicEngine(new(){PresentGraphics=c=>canvas=c}).CreateSession();
    await session.ExecuteAsync(new(script));
    if(canvas?.Point(0,0)!=14)throw new InvalidOperationException("Framebuffer check failed.");
});
await Measure("editor-runner-1000-pixels",async()=>
{
    await using var runner=new EditorRunner();runner.Start(script,null,dialect:"qbasic");
    var result=await runner.WaitAsync();if(result.Status!="Completed")throw new InvalidOperationException(result.Status);
    if(!runner.TryTakeFrame(out var frame)||frame?[0,0].Background!=ConsoleColor.Yellow)throw new InvalidOperationException("Final preview check failed.");
});
var document=new EditorDocument();document.Insert(string.Join('\n',Enumerable.Range(0,2000).Select(i=>$"Dim value{i} As Integer = {i}")));
await using(var runner=new EditorRunner())
{
    var controller=new EditorController(document,runner,initialDialect:"qbasic");controller.Resize(80,30);
    runner.Start("SCREEN 0\nPRINT \"FRAME BENCH\";",null,dialect:"qbasic");await runner.WaitAsync();controller.Tick();await controller.HandleAsync(new(EditorKey.F8));
    await Measure("program-view-build-200-frames",()=>
    {for(int i=0;i<200;i++)_ = EditorFrame.Build(controller);return Task.CompletedTask;});
    using var writer=new CountingWriter();var renderer=new Screen9.AnsiCanvasRenderer(writer);
    await Measure("program-view-build-render-200-frames",()=>
    {for(int i=0;i<200;i++){renderer.Invalidate();renderer.Render(EditorFrame.Build(controller).ToCanvas());}return Task.CompletedTask;});
}
if(args.Length>0)
{
    string path=Path.GetFullPath(args[0]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{Runtime=Environment.Version.ToString(),Pixels=1000,Frames=200,Trials=3,measurements},new JsonSerializerOptions{WriteIndented=true}));
}
record Measurement(string Name,double MedianMilliseconds,long AllocatedBytes);
sealed class CountingWriter:TextWriter
{
    public override Encoding Encoding=>Encoding.UTF8;
    public override void Write(char value){}
    public override void Write(string? value){}
    public override void WriteLine(string? value){}
}
