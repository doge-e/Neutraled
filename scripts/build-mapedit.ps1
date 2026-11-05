# Build the map / battle / story editor.
# Since 1.1.0 the editor (mapedit/src/ntl-mapedit) is a LIBRARY merged into ntl-gui.exe:
#   ntl-gui.exe mapedit <cmd>   -> editor CLI
#   ntl-gui.exe mapedit         -> editor window
# so building the GUI project builds it. A running ntl-gui.exe locks the output exe
# (MSB3021/MSB3027), hence we stop it first.
#   - UTMT dependency DLLs default to <game root>\UTMT; override with NTL_UTMT_DIR
# Usage: powershell -ExecutionPolicy Bypass -File scripts\build-mapedit.ps1
$ErrorActionPreference = 'Stop'
$N = Split-Path $PSScriptRoot -Parent
Get-Process -Name ntl-gui -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$proj = Join-Path $N 'gui\Neutraled.Gui.csproj'
& $dotnet build $proj -c Release -v q --nologo
Write-Host ('BUILD EXIT: ' + $LASTEXITCODE)
exit $LASTEXITCODE
