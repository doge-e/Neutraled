# Neutraled 启动脚本（薄转发：真正的逻辑都在 ntl-builder --launch 里）
# 用法: .\launch.ps1 [-Chapter root|chapter1..chapter5|all] [-NoCache] [-PrintOnly]
# 说明: 不再自己关游戏（--launch 会检测 data.win 被占用并报错），也不再重复实现部署。
param(
    [string]$Chapter = 'root',
    [switch]$NoCache,
    [switch]$PrintOnly
)

$ErrorActionPreference = 'Stop'
$candidates = @(
    (Join-Path $PSScriptRoot 'builder\bin\Release\net9.0\ntl-builder.exe'),
    (Join-Path $PSScriptRoot '..\builder\bin\Release\net9.0\ntl-builder.exe')
)
$exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) {
    Write-Output '[错误] 找不到 ntl-builder.exe，尝试过：'
    $candidates | ForEach-Object { Write-Output ('  ' + $_) }
    exit 1
}

$argv = @('--launch', $Chapter)
if ($NoCache) { $argv = @('--cache-clear') + $argv }
if ($PrintOnly) {
    Write-Output ('builder: ' + $exe)
    Write-Output ('argv: ' + ($argv -join ' '))
    exit 0
}

& $exe @argv
exit $LASTEXITCODE
