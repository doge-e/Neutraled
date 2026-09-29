# Verify the packaged release: install + uninstall into a sandbox game dir
# Usage: powershell -ExecutionPolicy Bypass -File tools\verify-install.ps1
param(
    [string]$Out = 'E:\aiwork\out',
    [string]$Game = 'E:\steam\steamapps\common\DELTARUNE',
    [string]$Sandbox = 'E:\aiwork\sandbox-game',
    [string]$Src = ''
)
$ErrorActionPreference = 'Continue'
Write-Host '===== verify packaged release =====' -ForegroundColor Cyan
if (Test-Path -LiteralPath $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $Sandbox 'chapter1_windows') | Out-Null
Copy-Item (Join-Path $Game 'DELTARUNE.exe') (Join-Path $Sandbox 'DELTARUNE.exe') -Force
Copy-Item (Join-Path $Game 'data.win') (Join-Path $Sandbox 'data.win') -Force
Copy-Item (Join-Path $Game 'chapter1_windows\data.win') (Join-Path $Sandbox 'chapter1_windows\data.win') -Force
$before = (Get-FileHash (Join-Path $Sandbox 'data.win') -Algorithm SHA256).Hash
$beforeCh1 = (Get-FileHash (Join-Path $Sandbox 'chapter1_windows\data.win') -Algorithm SHA256).Hash
Write-Host '  sandbox ready (DELTARUNE.exe + data.win + chapter1_windows/data.win)'

# Layout compatibility: installer layout -> <release root>\install\ntl-builder.exe
#                      legacy layout    -> <release root>\bin\ntl-builder.exe
$exeCands = @(
    (Join-Path $Out 'install\ntl-builder.exe'),
    (Join-Path $Out 'bin\ntl-builder.exe')
)
$exe = $exeCands | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $exe) { Write-Host ('  [ERROR] not found: ' + ($exeCands -join '   or   ')) -ForegroundColor Red; exit 1 }
Write-Host ('  builder: ' + $exe)

# Source dir: an installer-only package has no src/ next to the exe, so -Src is required there.
# Fallbacks: sibling ..\src (legacy layout), then <Game>\Neutraled (dev checkout).
$srcArgs = @()
if ($Src) {
    if (-not (Test-Path -LiteralPath (Join-Path $Src 'api'))) { Write-Host ('  [ERROR] -Src has no api/: ' + $Src) -ForegroundColor Red; exit 1 }
    $srcArgs += @('--src', (Resolve-Path -LiteralPath $Src).Path)
} else {
    $sib = Join-Path (Split-Path $exe -Parent) '..\src'
    if (Test-Path -LiteralPath (Join-Path $sib 'api')) {
        $srcArgs += @('--src', (Resolve-Path -LiteralPath $sib).Path)
    } elseif (Test-Path -LiteralPath (Join-Path $Game 'Neutraled\api')) {
        Write-Host '  [info] no src/ in package (installer layout) -> using <Game>\Neutraled as source'
        $srcArgs += @('--src', (Join-Path $Game 'Neutraled'))
    } else {
        Write-Host '  [warn] source dir not found: pass -Src <dir containing api/ and docs/>' -ForegroundColor Yellow
    }
}

Write-Host ''; Write-Host '--- 1) --version ---'
& $exe --version 2>&1 | Select-Object -First 4

Write-Host ''; Write-Host '--- 2) --install ---'
& $exe --install --game $Sandbox @srcArgs 2>&1 | Select-Object -First 22
Write-Host ('  exit=' + $LASTEXITCODE)

Write-Host ''; Write-Host '--- 3) install result ---'
$ntl = Join-Path $Sandbox 'Neutraled'
if (Test-Path -LiteralPath $ntl) {
    $f = Get-ChildItem $ntl -Recurse -File
    Write-Host ('  Neutraled/: ' + $f.Count + ' files / ' + [int](($f | Measure-Object Length -Sum).Sum / 1MB) + ' MB')
    Get-ChildItem $ntl | Select-Object -First 12 | ForEach-Object { Write-Host ('     ' + $(if ($_.PSIsContainer) { '[D] ' } else { '    ' }) + $_.Name) }
    Write-Host '  backups:'
    Get-ChildItem (Join-Path $ntl 'backup') -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { Write-Host ('     ' + $_.FullName.Replace($ntl + '\', '') + '  ' + [int]($_.Length / 1KB) + ' KB') }
} else { Write-Host '  [ERROR] Neutraled/ not created' -ForegroundColor Red }

Write-Host ''; Write-Host '--- 4) --uninstall ---'
& $exe --uninstall --game $Sandbox 2>&1 | Select-Object -First 18
Write-Host ('  exit=' + $LASTEXITCODE)

$after = (Get-FileHash (Join-Path $Sandbox 'data.win') -Algorithm SHA256).Hash
$afterCh1 = (Get-FileHash (Join-Path $Sandbox 'chapter1_windows\data.win') -Algorithm SHA256).Hash
Write-Host ''
if ($before -eq $after -and $beforeCh1 -eq $afterCh1) { Write-Host '  data.win restored: YES (byte-identical)' -ForegroundColor Green }
else { Write-Host '  data.win restored: NO' -ForegroundColor Red }

# Design intent (Installer.Uninstall): restore every modified official file byte-for-byte and
# remove deployment artifacts, but KEEP Neutraled/ because mods/ + backup/ are player data.
$cacheGone    = -not (Test-Path -LiteralPath (Join-Path $ntl 'cache'))
$chaptersGone = -not (Test-Path -LiteralPath (Join-Path $ntl 'chapters.json'))
$modsKept     = Test-Path -LiteralPath (Join-Path $ntl 'mods')
$backupKept   = Test-Path -LiteralPath (Join-Path $ntl 'backup')
if ($cacheGone -and $chaptersGone) { Write-Host '  deploy artifacts removed: YES (cache/ + chapters.json gone)' -ForegroundColor Green }
else { Write-Host ('  deploy artifacts removed: NO (cacheGone=' + $cacheGone + ' chaptersGone=' + $chaptersGone + ')') -ForegroundColor Red }
if ($modsKept -and $backupKept) { Write-Host '  player data kept: YES (mods/ + backup/)' -ForegroundColor Green }
else { Write-Host ('  player data kept: NO (mods=' + $modsKept + ' backup=' + $backupKept + ')') -ForegroundColor Red }
if (Test-Path -LiteralPath (Join-Path $ntl 'api')) { Write-Host '  note: api/ kept by design (Neutraled/ stays for mods+backups)' }