# rebuild builder safely: stop watcher -> build -> restart watcher
$N = 'E:\steam\steamapps\common\DELTARUNE\Neutraled'
Get-Process -Name ntl-builder -EA 0 | Stop-Process -Force
Start-Sleep -Seconds 2
& "C:\Program Files\dotnet\dotnet.exe" build "$N\builder\Neutraled.Builder.csproj" -c Release -v q --nologo
Write-Host ("BUILD EXIT: " + $LASTEXITCODE)
& wscript.exe //B //Nologo "$N\scripts\watch-external.vbs"
Start-Sleep -Seconds 2
Get-Process -Name ntl-builder -EA 0 | ForEach-Object { "watcher restarted: pid " + $_.Id }