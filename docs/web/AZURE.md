# Publishing Screen9 to Azure App Service

How the site `screen9-marcoparenzan` is deployed, what was done the first time the WebAssembly build went live (2026-10-23), the numbers measured on the live site, and what to watch out for.
Written for the maintainer. The design of the WebAssembly build itself is in [WASM.md](WASM.md).

## 1. What is configured

The App Service (all of this was read from Azure, nothing was changed to obtain it):

| | |
|---|---|
| Name / resource group | `screen9-marcoparenzan` / `marcoparenzan` |
| Subscription | **`Azure subscription 1`** (not the default one of the Azure CLI: the account has several and the default, *Microsoft Azure Sponsorship*, does not contain this app) |
| URL | `https://screen9-marcoparenzan-dpb3e3gvgqebf3dy.westeurope-01.azurewebsites.net` |
| OS and stack | Windows, ASP.NET Core **.NET v10.0** (in-process under IIS) |
| Worker | **32-bit** (`use32BitWorkerProcess: true`), Always On, HTTP/2 off, HTTPS only |
| App settings | none |

The 32-bit worker is fine here: the server only renders pages and serves files, the WebAssembly runs in the visitors' browsers. It would matter if the server held the games again.

The project already has three publish profiles in `src/Screen9.Web/Properties/PublishProfiles/` (the `.pubxml` files are in git, their `.pubxml.user` files, which hold the saved credentials, are ignored by git):

| Profile | Method | Needs |
|---|---|---|
| `screen9-marcoparenzan - Web Deploy` | MSDeploy (WMSVC) over 443, with app-offline and backup | the saved password (Visual Studio) |
| `screen9-marcoparenzan - Zip Deploy` | zip to the Kudu site (`…scm…azurewebsites.net`) | the saved password (Visual Studio) |
| `screen9-marcoparenzan - FTP` | FTPS to `site/wwwroot` | the saved password (Visual Studio) |

## 2. The usual way: Visual Studio

*Build → Publish* (or right-click `Screen9.Web` → Publish), pick one of the profiles, **Publish**. Visual Studio uses the password it saved in the `.pubxml.user` file. For the WebAssembly build:

- Close the running site first (it locks `Screen9.Web.Client.dll`).
- Delete `src\Screen9.Web.Client\obj\Release` before publishing: the AOT incremental cache can leave a site whose runtime will not start (see WASM.md, "The AOT incremental cache is a trap").
- The publish compiles the whole runtime ahead of time: from about 3½ to 9 minutes before anything is uploaded. Configuration **Release**.

## 3. From the command line

`dotnet publish … -p:PublishProfile="screen9-marcoparenzan - Zip Deploy"` **builds correctly but cannot upload**: it fails with

```
Microsoft.NET.Sdk.Publish.ZipDeploy.targets: error : Failed to retrieve credentials.
```

because the saved password is encrypted by Visual Studio for its own use and the command line cannot read it. (The same build, with the `NETSDK1198` warning that the profile is not found in the client project, is harmless.) Two ways around it:

**a) The Azure CLI** (what was used; needs `az login`):

```powershell
# 1. build the site into a folder (AOT, Release, production settings: compressed assets, full link)
Remove-Item -Recurse -Force src\Screen9.Web.Client\obj\Release -ErrorAction SilentlyContinue
dotnet publish src\Screen9.Web\Screen9.Web.csproj -c Release -o ops\artifacts\azure

# 2. zip it
Compress-Archive -Path ops\artifacts\azure\* -DestinationPath ops\artifacts\screen9-site.zip -CompressionLevel Fastest -Force

# 3. deploy; --clean true empties wwwroot first, so no file of an older version survives
az webapp deploy --name screen9-marcoparenzan --resource-group marcoparenzan --subscription "Azure subscription 1" `
    --src-path ops\artifacts\screen9-site.zip --type zip --clean true
```

(Used here with the publish folder `src\Screen9.Web\bin\Release\net10.0\publish`, which is where `dotnet publish -p:PublishProfile=…` leaves its output.) Measured: the build took 337 s on the development machine, the zip is 53 MB (97 MB unzipped, 83 precompressed `.br` files inside), the upload and deployment took 96 s and ended with `provisioningState: Succeeded`.

**b) The password in the command**: download the publish profile from the Azure portal (*Get publish profile*) and pass its `userPWD` as `-p:Password=…`. Not used here; never commit it.

Do **not** use `ops/run-site.ps1` for this: its two settings (no compression, lighter link) are for running locally.

## 4. What was measured on the live site (2026-10-23, headless Edge, from the development machine)

| | |
|---|---|
| Pages (`/`, `/plasma-demo`, `/rally-x`, `/gorillas-python`, `/editor-monaco`, `/architecture`, `/wasm`, `/gorillas-csx`) | all `200`; the first request after the deployment took 8 s (the app starting cold), the others 0.2–0.6 s |
| `/plasma-demo` renders as | `Blazor:{"type":"webassembly"` (before the deployment: `"server"`) |
| First visit, plasma page | **90 requests, 7.53 MB transferred, 33.2 MB decoded**, about 13 s from the browser launch to the first fps reading (a headless browser on a fast line: the figure includes the browser starting and a 2-second polling step) |
| `dotnet.native.wasm` | 5.1 MB on the wire (Brotli; 22 MB decoded): the runtime with the AOT-compiled code |
| `System.Private.CoreLib` | 457 KB; `PySharpLib` 202 KB; the other assemblies 2–100 KB each |
| Frames | **61 fps** in the browser (the plasma) |

So Azure serves the precompressed `.br` files as they are produced: the layout of the publish (`_framework\*.br`, `*.gz`) is honoured by the ASP.NET Core static-files middleware, no IIS configuration was needed for `.wasm`.
Later visits use the browser cache (the files are fingerprinted).

## 5. Rolling back

The deployment above was a zip with `--clean true`: **the previous version is gone** from the site (a Web Deploy profile with *EnableMSDeployBackup* keeps a backup; Zip Deploy does not). To go back, check out the commit you want (`git log`; the Blazor-Server-only site is `56ef136` and earlier), publish and deploy it again. Keep the zip of a good deployment if you want an instant rollback (`az webapp deploy` with that zip).

## 6. Pitfalls

| Symptom | Cause and fix |
|---|---|
| `Failed to retrieve credentials` from `dotnet publish` with a profile | The saved password is Visual Studio's; use the Azure CLI (section 3) or publish from Visual Studio. |
| `az webapp list` shows many apps but not this one | Wrong subscription; add `--subscription "Azure subscription 1"` (list them with `az account list -o table`). |
| After the deployment the page loads but no program runs | A stale AOT cache (`mono_wasm_load_runtime failed` in the browser console): delete `src\Screen9.Web.Client\obj\Release`, publish and deploy again. |
| `curl https://…/plasma-demo` from Git Bash on Windows prints `C:/Program Files/Git/…` | Git Bash rewrites arguments that start with `/`; it affects only the label printed by the command, not the request. Use PowerShell or `MSYS_NO_PATHCONV=1`. |
| The first request after a deployment takes 8 s | The app starts cold; Always On keeps it warm afterwards. |
| The upload is slow or times out | The zip is about 53 MB; `az webapp deploy` takes `--timeout` (milliseconds). A publish without `--clean true` leaves old files in `wwwroot`. |

## 7. Not verified

A deployment through Visual Studio's profiles with the WebAssembly build (the three profiles themselves are unchanged and untested since the WebAssembly work); the *Web Deploy* backup and app-offline behaviour; a build pipeline (GitHub Actions) that publishes on push — it would need the `wasm-tools` workload on the runner and the AOT step (minutes) in the job; behaviour on a smaller App Service plan; first-load time on a mobile network (7.5 MB), and a custom domain or CDN in front.
