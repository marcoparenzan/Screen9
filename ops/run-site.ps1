<#
.SYNOPSIS
  Publishes the site with AOT-compiled WebAssembly and runs it: the fast way to try Screen9 in a browser.

.DESCRIPTION
  `dotnet run` runs the browser programs interpreted (about 25 times slower). A published build compiles them ahead of time (AOT).
  This script publishes for LOCAL use, tuned for build time, and then starts the site:
    - CompressionEnabled=false     no Brotli/gzip of the 30 MB of assets (saves ~55 s; a real deployment should keep compression)
    - EmccLinkOptimizationFlag=-O1 lighter final link (84 s -> 10 s); the AOT-compiled code is the same, measured at the same speed
  It always starts from a clean AOT cache: the compiler's incremental cache produced a runtime that would not start
  (mono_wasm_load_runtime failed in aot-runtime.c) after the set of assemblies changed.
  The publish is skipped when no source file is newer than the published site. See docs/web/WASM.md.

.PARAMETER Port       Port to listen on (default 5280).
.PARAMETER NoPublish  Run what is already in ops\artifacts\site, without publishing.
.PARAMETER Force      Publish even if nothing changed.
.PARAMETER Output     Where to publish and run from (default ops\artifacts\site, which git ignores). The folder is emptied before a publish.

.EXAMPLE
  pwsh ops\run-site.ps1
  pwsh ops\run-site.ps1 -NoPublish
#>
param(
    [int]$Port = 5280,
    [switch]$NoPublish,
    [switch]$Force,
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Screen9.Web\Screen9.Web.csproj'
$site = if ($Output) { $Output } else { Join-Path $root 'ops\artifacts\site' }
$dll = Join-Path $site 'Screen9.Web.dll'
$aotIntermediate = Join-Path $root 'src\Screen9.Web.Client\obj\Release'

# True when a source file is newer than the published site (bin/obj folders and the published site itself do not count).
function Test-SourcesChanged {
    if (-not (Test-Path $dll)) { return $true }
    $published = (Get-Item $dll).LastWriteTime
    $extensions = '*.cs', '*.razor', '*.csproj', '*.props', '*.targets', '*.js', '*.css', '*.json', '*.nupkg', '*.assets', '*.bas', '*.py', '*.csx'
    $newer = Get-ChildItem -Path (Join-Path $root 'src'), (Join-Path $root 'samples'), (Join-Path $root 'LocalFeed') -Recurse -File -Include $extensions -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules|\.tailwind)\\' -and $_.LastWriteTime -gt $published } |
        Select-Object -First 1
    return $null -ne $newer
}

if (-not $NoPublish) {
    if ($Force -or (Test-SourcesChanged)) {
        if (-not (dotnet workload list 2>$null | Select-String 'wasm-tools')) {
            Write-Host 'The wasm-tools workload is missing (it compiles .NET to WebAssembly ahead of time). Installing it...' -ForegroundColor Yellow
            dotnet workload install wasm-tools
        }

        # A running copy of the site (this script's, or Visual Studio's) locks Screen9.Web.Client.dll and the publish fails with MSB3027.
        $running = Get-Process -Name 'Screen9.Web' -ErrorAction SilentlyContinue
        if ($running) {
            Write-Host "Screen9.Web is running (PID $($running.Id -join ', ')): stop it first, it locks the files the publish needs." -ForegroundColor Red
            exit 1
        }

        Remove-Item -Recurse -Force $site -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force $aotIntermediate -ErrorAction SilentlyContinue

        $started = Get-Date
        Write-Host 'Publishing (AOT, tuned for local use). It compiles the whole runtime: a few minutes...' -ForegroundColor Cyan
        dotnet publish $project -c Release -o $site -p:CompressionEnabled=false -p:EmccLinkOptimizationFlag=-O1
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host ("Published in {0:n0} s." -f ((Get-Date) - $started).TotalSeconds) -ForegroundColor Green
    }
    else {
        Write-Host 'Nothing changed since the last publish: running it. (-Force to publish again.)' -ForegroundColor Green
    }
}

if (-not (Test-Path $dll)) {
    Write-Host "Nothing published at ${site}: run without -NoPublish." -ForegroundColor Red
    exit 1
}

Write-Host "Screen9 on http://localhost:$Port (Ctrl+C to stop)" -ForegroundColor Green
dotnet $dll --urls "http://localhost:$Port"
