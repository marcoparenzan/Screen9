#:project ../src/QBSharp.QBasic/QBSharp.QBasic.csproj
using System.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;
string text=File.ReadAllText("samples/qbsharp/Gorilla/GORILLA.BAS");int begin=text.LastIndexOf("FUNCTION CalcDelay!",StringComparison.OrdinalIgnoreCase);int end=text.IndexOf("END FUNCTION",begin,StringComparison.OrdinalIgnoreCase)+12;
await using var session=new QBasicEngine(new(){MaximumSteps=10_000_000,AllowFileIO=false}).CreateSession();
var watch=Stopwatch.StartNew();await session.ExecuteAsync(new("result=CalcDelay\n"+text[begin..end]));watch.Stop();
double count=Convert.ToDouble(session.Variables["result"]);Console.WriteLine($"Calibration iterations: {count}; elapsed: {watch.Elapsed.TotalMilliseconds:F1} ms; predicted EGA Rest(.02): {count*1.3*.02/500*1000:F1} ms per trajectory step.");
