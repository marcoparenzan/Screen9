#:project ../src/QBSharp.Cli/QBSharp.Cli.csproj
using System.Diagnostics;
using Screen9;
if(!OperatingSystem.IsWindows()){Console.WriteLine("Native presenter benchmark requires Windows.");return;}
var pc=new PCCanvas(9);for(int i=0;i<3;i++)PixelWindow.Capture(pc);
long before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
for(int i=0;i<200;i++)PixelWindow.Capture(pc);
watch.Stop();Console.WriteLine($"200 redundant EGA snapshots: {watch.Elapsed.TotalMilliseconds:F2} ms, {GC.GetAllocatedBytesForCurrentThread()-before:N0} bytes allocated.");
long ticks=100;using var presenter=new PixelWindow(ticks:()=>ticks);presenter.Present(pc);
before=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
for(int i=0;i<200;i++){ticks+=33;presenter.Refresh();}
watch.Stop();Console.WriteLine($"200 unchanged refreshes: {watch.Elapsed.TotalMilliseconds:F2} ms, {GC.GetAllocatedBytesForCurrentThread()-before:N0} bytes allocated; total captures {presenter.CaptureCount} (includes initial frame).");
Console.WriteLine("This isolates eliminated idle snapshot work, not game FPS or total rendering performance.");
