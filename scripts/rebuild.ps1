# rebuild the single GUI exe (ntl-gui; builder/editor/studio are libraries inside it) safely:
#   - stop the external watcher (the same exe running with --watch-external) -> otherwise MSB3021/MSB3027
#   - build gui/Neutraled.Gui.csproj (ProjectReferences builder + studio + mapedit)
#   - restart the watcher via scripts\watch-external.vbs (windowless)
$N = 'E:\steam\steamapps\common\DELTARUNE\Neutraled'

function Stop-Watcher {
    Get-CimInstance Win32_Process -Filter "Name = 'ntl-gui.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains('--watch-external') } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    # pre-1.1.0 watcher (separate ntl-builder.exe)
    Get-Process -Name ntl-builder -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Stop-Watcher
Start-Sleep -Seconds 2
& "C:\Program Files\dotnet\dotnet.exe" build "$N\gui\Neutraled.Gui.csproj" -c Release -v q --nologo
Write-Host ("BUILD EXIT: " + $LASTEXITCODE)
if ($LASTEXITCODE -eq 0) {
    & wscript.exe //B //Nologo "$N\scripts\watch-external.vbs"
    Start-Sleep -Seconds 2
    Get-CimInstance Win32_Process -Filter "Name = 'ntl-gui.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains('--watch-external') } |
        ForEach-Object { "watcher restarted: pid " + $_.ProcessId }
}
exit $LASTEXITCODE
