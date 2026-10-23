using Screen9.Web;
using Screen9.Web.Components;

// The builder comes first, and the order matters: the content root (wwwroot, appsettings.json) is taken from the working
// directory at this moment. It used to be changed before this line, and a published site then looked for its static files in
// the scratch folder below and served every one of them empty (200 with no body, so no styles and no scripts).
// Started from another folder (`dotnet ops/artifacts/site/Screen9.Web.dll` from the repository root), the working directory has no wwwroot but the
// folder of the published DLL does: use that one as the content root. In development, and when started from the site's own folder, nothing changes.
var options = new WebApplicationOptions { Args = args };
if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")) && Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")))
    options = new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory };
var builder = WebApplication.CreateBuilder(options);

// The TUI editor's Open dialog starts with an empty path box (QBSharp.Cli.Editor.EditorController.OpenDialog):
// a bare filename a user types there resolves via Path.GetFullPath against this process-wide working
// directory, not their own session folder (SessionWorkspace). Pointing it at a dedicated, otherwise-empty
// scratch root means that sharp edge's worst case is an orphaned file here, never another session's content.
//Directory.CreateDirectory(SessionWorkspace.CwdScratchRoot);
//Environment.CurrentDirectory = SessionWorkspace.CwdScratchRoot;

// QBasic-era games (GORILLA.BAS) time their pauses by how fast an empty loop runs: on this interpreter every
// pause would be ~100x too long. Measure the correction once, now, while the server is idle (QBasicRunPolicy).
_ = Task.Run(() => QBSharp.Cli.QBasicRunPolicy.CalibratedTimerWaitScale);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

// A published site serves its static files from wwwroot under the content root. If that folder is not there, fail now with a
// clear message rather than answer every script and stylesheet with an empty body. (In Development the static files come from
// absolute paths in the build manifest, so the check does not apply.)
if (!app.Environment.IsDevelopment() && !Directory.Exists(app.Environment.WebRootPath))
    throw new InvalidOperationException($"wwwroot not found at '{app.Environment.WebRootPath}' (content root '{app.Environment.ContentRootPath}'). Was the working directory changed before the host was built?");

// The games run as WebAssembly in the browser: in Development the server also serves what the debugger needs to attach to them.
if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();
else
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Screen9.Web.Client.Components.PlasmaWasm).Assembly);

app.Run();
