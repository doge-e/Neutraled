# Build the map/battle editor (mapedit/src/ntl-mapedit) safely.
#   - a running ntl-mapedit.exe locks the output exe -> stop it first (MSB3021/MSB3027 otherwise)
#   - UTMT dependency DLLs default to <game root>\UTMT; override with NTL_UTMT_DIR
# Usage: powershell -ExecutionPolicy Bypass -File scripts\build-mapedit.ps1
$ErrorActionPreference = 'Stop'
$N = Split-Path $PSScriptRoot -Parent
Get-Process -Name ntl-mapedit -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$proj = Join-Path $N 'mapedit\src\ntl-mapedit\ntl-mapedit.csproj'
& $dotnet build $proj -c Release -v q --nologo
Write-Host ('BUILD EXIT: ' + $LASTEXITCODE)
exit $LASTEXITCODE
