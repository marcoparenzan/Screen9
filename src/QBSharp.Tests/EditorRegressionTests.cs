using System.Diagnostics;
using System.Text;
using QBSharp.Cli;
using QBSharp.Cli.Editor;

namespace QBSharp.Tests;

public sealed class EditorRegressionTests
{
    [Fact]
    public async Task EditorBasFixtureUsesAllUiAdaptersAndCompletesDialogs()
    {
        await using var runner = new EditorRunner();
        var controller = new EditorController(new EditorDocument(), runner);
        string fixture = Path.Combine(AppContext.BaseDirectory, "samples", "editor.bas");
        runner.Start(await File.ReadAllTextAsync(fixture), fixture);
        await Until(() => { controller.Tick(); return controller.Dialog?.Input == true; });
        await controller.HandleAsync(new(EditorKey.Enter)); // Input default deploy
        await Until(() => { controller.Tick(); return controller.Dialog?.Title == "Ambiente"; });
        await controller.HandleAsync(new(EditorKey.Down)); await controller.HandleAsync(new(EditorKey.Enter));
        await Until(() => { controller.Tick(); return controller.Dialog?.Multi == true; });
        await controller.HandleAsync(new(EditorKey.Text, " ")); await controller.HandleAsync(new(EditorKey.Enter));
        await Until(() => { controller.Tick(); return controller.Dialog?.Title.Contains("Conferma job", StringComparison.Ordinal) == true; });
        await controller.HandleAsync(new(EditorKey.Enter));
        var result = await runner.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)); controller.Tick();
        Assert.Equal("Completed", result.Status); Assert.Null(controller.Dialog);
        Assert.Contains("deploy -> prod", controller.Output); Assert.Contains("1", controller.Output);
        Assert.Contains("Risultato: Job pronto", controller.Output); Assert.Contains("Editor demo: 100%", controller.Output);
        Assert.Contains(controller.Output, line => line.Contains("Count", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("edit")]
    [InlineData("edit", "sample.bas")]
    public async Task RedirectedEditorRefusesWithoutAnsiOrCreatingFiles(params string[] arguments)
    {
        string root = Path.Combine(Path.GetTempPath(), "qbs-edit-cli-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            info.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
            foreach (var arg in arguments) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(true); }
            Assert.Equal(2, process.ExitCode); Assert.Empty(await output); Assert.Contains("interactive ANSI terminal", await error);
            Assert.DoesNotContain('\u001b', await error); Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task ImportedDiagnosticOpensItsDefiningFileAfterDirtyDiscardConfirmation()
    {
        string root = Path.Combine(Path.GetTempPath(), "qbs-edit-source-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            string imported = Path.Combine(root, "bad.bas"), main = Path.Combine(root, "main.bas");
            await File.WriteAllTextAsync(imported, "Dim = 1");
            var document = new EditorDocument(); document.NameNew(main); document.Insert("Import \"bad\"");
            await using var runner = new EditorRunner(); var controller = new EditorController(document, runner);
            await controller.HandleAsync(new(EditorKey.F5)); var result = await runner.WaitAsync(); controller.Tick();
            Assert.Equal(imported, result.Diagnostic!.Span.File);
            await controller.HandleAsync(new(EditorKey.F4)); Assert.Equal("Unsaved changes", controller.Dialog!.Title);
            await controller.HandleAsync(new(EditorKey.Down)); await controller.HandleAsync(new(EditorKey.Enter));
            await controller.FlushOpenAsync(); Assert.Equal(imported, document.FilePath); Assert.Equal(4, document.Position.Column); Assert.False(document.Dirty);
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
}
