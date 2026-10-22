using Screen9.Web;
using Screen9.Web.Components;

// The TUI editor's Open dialog starts with an empty path box (QBSharp.Cli.Editor.EditorController.OpenDialog):
// a bare filename a user types there resolves via Path.GetFullPath against this process-wide working
// directory, not their own session folder (SessionWorkspace). Pointing it at a dedicated, otherwise-empty
// scratch root means that sharp edge's worst case is an orphaned file here, never another session's content.
Directory.CreateDirectory(SessionWorkspace.CwdScratchRoot);
Environment.CurrentDirectory = SessionWorkspace.CwdScratchRoot;

// QBasic-era games (GORILLA.BAS) time their pauses by how fast an empty loop runs: on this interpreter every
// pause would be ~100x too long. Measure the correction once, now, while the server is idle (QBasicRunPolicy).
_ = Task.Run(() => QBSharp.Cli.QBasicRunPolicy.CalibratedTimerWaitScale);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
