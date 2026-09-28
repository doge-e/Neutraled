# Build the Neutraled plugin SDK and both sample plugins, then package sample-clock.
# Usage:  powershell -ExecutionPolicy Bypass -File sdk\build-samples.ps1
# Pure ASCII on purpose (PowerShell 5.1 reads BOM-less UTF-8 as ANSI).
$ErrorActionPreference = 'Stop'

$sdk    = Split-Path -Parent $MyInvocation.MyCommand.Path
$root   = Split-Path -Parent $sdk
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }

function Build([string]$proj) {
    Write-Host ('=== build ' + $proj)
    & $dotnet build $proj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw ('build failed: ' + $proj) }
}

Build (Join-Path $sdk  'Neutraled.PluginSdk\Neutraled.PluginSdk.csproj')
Build (Join-Path $sdk  'samples\SampleHello\SampleHello.csproj')
Build (Join-Path $sdk  'samples\SampleClock\SampleClock.csproj')

# --- package sample-clock as .ntlplugin (a plain zip with plugin.json + dll at its root) ---
$stage = Join-Path $root '.tmp\pkg-sample-clock'
$dist  = Join-Path $sdk  'samples\SampleClock\dist'
$out   = Join-Path $stage 'sample-clock.ntlplugin'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
New-Item -ItemType Directory -Force -Path $dist  | Out-Null
Get-ChildItem -Path $stage -Force | Remove-Item -Recurse -Force
Copy-Item (Join-Path $root 'plugins\sample-clock\plugin.json') $stage -Force
Copy-Item (Join-Path $sdk  'samples\SampleClock\bin\Release\net9.0\SampleClock.dll') $stage -Force
# Compress-Archive only accepts a .zip destination name, so zip first and rename after.
$zip = Join-Path $stage 'sample-clock.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Move-Item $zip $out -Force
Copy-Item $out $dist -Force
Write-Host ('packaged: ' + $out)
Write-Host ('          ' + (Join-Path $dist 'sample-clock.ntlplugin'))
Write-Host 'install with:  ntl-builder.exe --plugin-install <path-to-.ntlplugin>'