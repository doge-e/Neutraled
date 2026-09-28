# Neutraled multi-chapter deploy + runtime verification
# Deploys each chapter, launches the game, uses the AutoTest mod to jump into that
# chapter automatically, then collects log/screenshot evidence into a report.

$ErrorActionPreference = 'Continue'
$game = 'E:\steam\steamapps\common\DELTARUNE'
$exe  = Join-Path $game 'Neutraled\builder\bin\Release\net9.0\ntl-builder.exe'
$log  = Join-Path $env:LOCALAPPDATA 'DELTARUNE\Neutraled\dr-api.log'
$shotDir = Join-Path $env:LOCALAPPDATA 'DELTARUNE'
$reportPath = Join-Path $game 'Neutraled\_test\verify_report.txt'
$rootGml = Join-Path $game 'Neutraled\mods\AutoTest\Neutraled\root\gml\scr_root_auto.gml'

$report = New-Object System.Collections.ArrayList
function Say([string]$s) { Write-Output $s; [void]$report.Add($s) }

Say ('=== Neutraled multi-chapter verification ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ===')

foreach ($ch in 1..5) {
    Say ('--- chapter' + $ch + ' ---')

    # point AutoTest at this chapter
    $txt = Get-Content $rootGml -Raw -Encoding UTF8
    $txt = [regex]::Replace($txt, 'ntl_goto_chapter\(\d+\)', ('ntl_goto_chapter(' + $ch + ')'))
    Set-Content $rootGml -Value $txt -Encoding UTF8 -NoNewline

    Get-Process -Name 'DELTARUNE' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    & $exe --deploy --chapter ('chapter' + $ch) 2>&1 | Out-Null
    $rc1 = $LASTEXITCODE
    Say ('  deploy chapter' + $ch + ': ' + $(if ($rc1 -eq 0) { 'OK' } else { 'FAIL(' + $rc1 + ')' }))
    & $exe --deploy --chapter root 2>&1 | Out-Null
    $rc2 = $LASTEXITCODE
    Say ('  deploy root: ' + $(if ($rc2 -eq 0) { 'OK' } else { 'FAIL(' + $rc2 + ')' }))

    if (Test-Path $log) { Remove-Item $log -Force }
    Get-ChildItem $shotDir -Filter 'ntl_ch*.png' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    Start-Process 'E:\DELTARUNE.url'
    Start-Sleep -Seconds 38

    $proc = Get-Process -Name 'DELTARUNE' -ErrorAction SilentlyContinue
    $title = if ($proc) { ($proc | Select-Object -First 1).MainWindowTitle } else { '(not running)' }
    Say ('  process: ' + $title)

    if (Test-Path $log) {
        $lines = Get-Content $log -Encoding UTF8
        $core = ($lines | Select-String 'controller created').Count
        $modsLoaded = ($lines | Select-String '\[mod\] load').Count
        $modsRun = ($lines | Select-String 'mods run').Count
        $err = ($lines | Select-String 'Error|error').Count
        Say ('  core init=' + $core + ' mods loaded=' + $modsLoaded + ' run records=' + $modsRun + ' errors=' + $err)
        $gotoLine = ($lines | Select-String 'ntl_goto_chapter').Line
        if ($gotoLine) { Say ('  ' + $gotoLine.Trim()) }
    } else { Say '  log: (missing)' }

    $shots = @(Get-ChildItem $shotDir -Filter 'ntl_ch*.png' -ErrorAction SilentlyContinue)
    Say ('  chapter screenshots: ' + $shots.Count + ' ' + (($shots | ForEach-Object { $_.Name }) -join ', '))
    $rootShot = Get-ChildItem $shotDir -Filter 'ntl_root_*.png' -ErrorAction SilentlyContinue
    Say ('  root screenshot: ' + $(if ($rootShot) { $rootShot.Name } else { '(none)' }))

    Get-Process -Name 'DELTARUNE' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# restore AutoTest to chapter 4
$txt = Get-Content $rootGml -Raw -Encoding UTF8
$txt = [regex]::Replace($txt, 'ntl_goto_chapter\(\d+\)', 'ntl_goto_chapter(4)')
Set-Content $rootGml -Value $txt -Encoding UTF8 -NoNewline

Say '=== done ==='
$report | Set-Content $reportPath -Encoding UTF8
Write-Output ('report: ' + $reportPath)
