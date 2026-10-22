using System.Diagnostics;
using QBSharp.Cli;

namespace QBSharp.Tests;

public sealed class QBasicRasterProcessTests
{
    [Fact]
    public async Task RasterBasFixturePassesInRedirectedCliWithoutAnsiAudioOrFiles()
    {
        string root=Path.Combine(Path.GetTempPath(),"qbs-raster-cli-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var info=new ProcessStartInfo("dotnet")
            {
                UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root
            };
            info.Environment.Remove("QBS_MAX_STEPS");info.Environment["QBS_AI_PROVIDER"]="";info.Environment["QBS_QBASIC_AUDIO"]="0";info.Environment["QBS_RASTER_FONT"]="compact";
            foreach(string arg in new[]{typeof(CliApplication).Assembly.Location,"test",Path.Combine(AppContext.BaseDirectory,"samples","qbasic-raster-test.bas"),"--dialect","qbasic"})info.ArgumentList.Add(arg);
            using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();process.StandardInput.Close();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try{await process.WaitForExitAsync(timeout.Token);}finally{if(!process.HasExited)process.Kill(true);}
            Assert.Equal(0,process.ExitCode);Assert.Empty(await errors);string text=await output;
            Assert.Contains("Raster smoke passed",text);Assert.Contains("PASS",text);Assert.DoesNotContain("\x1b",text,StringComparison.Ordinal);Assert.Empty(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
