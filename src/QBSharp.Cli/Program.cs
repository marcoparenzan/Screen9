using System.Text;
using Screen9;
using QBSharp.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QBSharp.Cli;
using QBSharp.Core.Hosting;
using Spectre.Console;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.UI;

Console.OutputEncoding = new UTF8Encoding(false);
Console.InputEncoding = new UTF8Encoding(false);
using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Services.AddQBSharp();
    builder.Services.AddQBSharpAI();
    // The Windows editor enables VT output in its terminal scope. Spectre's cached
    // auto-detection may run before that scope and incorrectly reject a valid console.
    // Never force ANSI for redirected input/output or ordinary CLI commands.
    bool editorTerminal = OperatingSystem.IsWindows() && args.FirstOrDefault() == "edit"
        && !Console.IsInputRedirected && !Console.IsOutputRedirected;
    var output = editorTerminal
        ? AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes })
        : AnsiConsole.Console;
    var input = new BomTextReader(Console.In);
    var scriptConsole = new TextConsole(Console.Out, input, !Console.IsInputRedirected && !Console.IsOutputRedirected);
    builder.Services.AddSingleton(scriptConsole);
    builder.Services.AddSingleton<ITextUi>(new ConsoleLibTextUi(scriptConsole));
    ICanvasRenderer renderer = OperatingSystem.IsWindows() && !Console.IsOutputRedirected ? new WindowsConsoleRenderer() : new TextCanvasRenderer(Console.Out, new(300, 100));
    builder.Services.AddSingleton(renderer);
    builder.Services.AddQBSharpStandardLibrary();
    var errors = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.Detect, Out = new AnsiConsoleOutput(Console.Error) });
    builder.Services.AddSingleton<IAnsiConsole>(output);
    builder.Services.AddSingleton(provider => new CliApplication(output, errors, input,
        !Console.IsInputRedirected && !Console.IsOutputRedirected && output.Profile.Capabilities.Interactive,
        provider.GetRequiredService<IScriptParser>(), provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<ILogger<CliApplication>>(), provider.GetRequiredService<StandardLibraryHost>(), provider.GetRequiredService<RalfHost>(), provider.GetRequiredService<TextConsole>()));
    using var host = builder.Build();
    return await host.Services.GetRequiredService<CliApplication>().RunAsync(args, cancellation.Token);
}
finally { Console.CancelKeyPress -= cancel; }
