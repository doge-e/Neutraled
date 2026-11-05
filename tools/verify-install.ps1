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
# C4/P1: the sandbox must simulate a FRESH install -> its live data.win must be VANILLA.
# The real game root's live data.win is usually already a Neutraled product (deployed), and a
# product has injection markers -> the installer would (correctly) refuse to adopt it as the
# vanilla baseline and never create Neutraled/backup, so the uninstall round-trip would fail.
# Source the vanilla files from <Game>\backup instead of the live tree.
$vanRoot = Join-Path $Game 'backup\data.win'
$vanCh1  = Join-Path $Game 'backup\chapter1_windows\data.win'
if (Test-Path -LiteralPath $vanRoot) { Copy-Item $vanRoot (Join-Path $Sandbox 'data.win') -Force }
else { Copy-Item (Join-Path $Game 'data.win') (Join-Path $Sandbox 'data.win') -Force }
if (Test-Path -LiteralPath $vanCh1) { Copy-Item $vanCh1 (Join-Path $Sandbox 'chapter1_windows\data.win') -Force }
else { Copy-Item (Join-Path $Game 'chapter1_windows\data.win') (Join-Path $Sandbox 'chapter1_windows\data.win') -Force }
$before = (Get-FileHash (Join-Path $Sandbox 'data.win') -Algorithm SHA256).Hash
$beforeCh1 = (Get-FileHash (Join-Path $Sandbox 'chapter1_windows\data.win') -Algorithm SHA256).Hash
Write-Host '  sandbox ready (DELTARUNE.exe + data.win + chapter1_windows/data.win)'

# Layout compatibility: versioned layout -> <release root>\Neutraled-<ver>\install\ntl-gui.exe（tools\package.ps1 现在的输出）
#                      installer layout -> <release root>\install\ntl-gui.exe
#                      legacy layout    -> <release root>\bin\ntl-gui.exe
# 注意：不能写成 @( (子表达式), ... ) —— 子表达式返回多个字符串时会被当成一个数组元素，
# 后续 & $exe 就变成"路径1 路径2"一个不存在的命令。分步 += 才能保证每个候选是单个字符串。
$exeCands = @()
$exeCands += @(Get-ChildItem -LiteralPath $Out -Directory -Filter 'Neutraled-*' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    ForEach-Object { Join-Path $_.FullName 'install\ntl-gui.exe' })
$exeCands += (Join-Path $Out 'install\ntl-gui.exe')
$exeCands += (Join-Path $Out 'bin\ntl-gui.exe')
$exe = $exeCands | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $exe) { Write-Host ('  [ERROR] not found: ' + ($exeCands -join '   or   ')) -ForegroundColor Red; exit 1 }
Write-Host ('  gui: ' + $exe)

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
$installExit = $LASTEXITCODE
Write-Host ('  exit=' + $installExit)
if ($installExit -ne 0) {
    Write-Host '  [ERROR] --install failed; the uninstall round-trip would be meaningless. Fix the install step first.' -ForegroundColor Red
    exit 1
}

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