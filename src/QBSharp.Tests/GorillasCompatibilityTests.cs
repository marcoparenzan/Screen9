using System.Diagnostics;
using System.Text;
using QBSharp.Cli;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Tests;

/// <summary>The historical sample is deliberately not a claim of QBasic/DOS compatibility.</summary>
public sealed class GorillasCompatibilityTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "samples", "GORILLA.BAS");
    [Fact]
    public async Task HistoricalSampleHasSourceDiagnosticAndDoesNotExecutePartially()
    {
        string text = await File.ReadAllTextAsync(Fixture, new UTF8Encoding(false, true));
        Assert.Contains("Copyright (C) Microsoft Corporation 1990", text); Assert.Contains("DEFINT A-Z", text);
        Assert.Contains("SCREEN Mode", text); Assert.Contains("PEEK(1047)", text);
        var parsed = new ScriptParser().ParseScript(new(text, Fixture));
        Assert.False(parsed.Success);
        Assert.Contains(parsed.Diagnostics, d => d.Code == "QBS1003" && d.Span.Start.Offset == text.IndexOf('#'));
        int calls = 0; var bindings = new HostBindings();
        bindings.RegisterFunction("Print", (_, _) => { calls++; return ValueTask.FromResult<object?>(null); });
        await using var session = new ScriptEngine().CreateSession(bindings);
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new(text, Fixture)));
        Assert.Equal("QBS1003", error.Diagnostic.Code); Assert.Equal(Fixture, error.Diagnostic.Span.File); Assert.Equal(0, calls);
    }
    [Theory]
    [InlineData("DEFINT A-Z")]
    [InlineData("DECLARE SUB RestReal (t#)")]
    [InlineData("TYPE XYPoint\nXCoor AS INTEGER\nEND TYPE")]
    [InlineData("CONST NPLAYERS = 20")]
    [InlineData("DIM SHARED GorillaX(1 TO 2)")]
    [InlineData("ON ERROR GOTO IsThereNoFile")]
    [InlineData("GOSUB InitVars")]
    [InlineData("PRINT \"Hello\"")]
    [InlineData("SCREEN 9")]
    [InlineData("DATA 327686, 60")]
    public void IndependentLegacyConstructsAreStillUnsupported(string snippet)
    {
        // Shows that ignoring the first suffix error cannot make the original game work.
        var result = new ScriptParser().ParseScript(new(snippet, "<gorillas-feature>"));
        Assert.False(result.Success); Assert.NotEmpty(result.Diagnostics);
    }
    [Fact]
    public async Task RealCliRejectsOriginalWithExit3AndNoFilesOrOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "qbs-gorillas-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            info.Environment.Remove("QBS_MAX_STEPS"); info.Environment["QBS_AI_PROVIDER"] = ""; info.Environment["NO_COLOR"] = "1";
            foreach (var argument in new[] { typeof(CliApplication).Assembly.Location, "run", Fixture, "--max-steps", "10000" }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync(); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            Assert.Equal(CliExitCodes.Syntax, process.ExitCode); Assert.Empty(await output);
            string diagnostic = await errors; Assert.Contains("GORILLA.BAS(", new string(diagnostic.Where(c => !char.IsWhiteSpace(c)).ToArray())); Assert.Contains("QBS1003", diagnostic);
            Assert.Empty(Directory.GetFileSystemEntries(root)); // No Gorillas.ini/.lge or other side effects.
        }
        finally { Directory.Delete(root, true); }
    }
}
