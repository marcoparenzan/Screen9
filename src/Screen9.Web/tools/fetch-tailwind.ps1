# One-time setup: downloads the standalone Tailwind CSS CLI (no Node/npm needed) into .tailwind/,
# where Screen9.Web.csproj's BuildTailwindCss target picks it up. Not committed to source control
# (.tailwind/ is gitignored) — re-run this after a fresh clone, or after deleting the binary.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root '.tailwind'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$exe = Join-Path $dest 'tailwindcss.exe'
Write-Host "Downloading the Tailwind CSS CLI to $exe ..."
Invoke-WebRequest -Uri 'https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-windows-x64.exe' -OutFile $exe
Write-Host 'Done. The next `dotnet build` of Screen9.Web rebuilds wwwroot/css/app.css from tailwind.css.'
