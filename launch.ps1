# Neutraled 安全启动脚本
# 自动：关闭游戏 → 部署 → 通过 Steam 启动
# 用法: .\launch.ps1 [-Chapter chapter4] [-NoCache]
param(
    [string]$Chapter = 'chapter4',
    [switch]$NoCache
)

$ErrorActionPreference = 'Continue'
$exe = Join-Path $PSScriptRoot '..\builder\bin\Release\net9.0\ntl-builder.exe'
if (-not (Test-Path $exe)) { Write-Output "找不到 builder: $exe"; exit 1 }

Write-Output '=== 1) 关闭游戏（必须！否则 data.win 被占用，部署会静默失败）==='
$p = Get-Process -Name 'DELTARUNE' -ErrorAction SilentlyContinue
if ($p) {
    $p | Stop-Process -Force
    Start-Sleep -Seconds 4
    Write-Output '  已关闭'
} else { Write-Output '  游戏未运行' }
$still = Get-Process -Name 'DELTARUNE' -ErrorAction SilentlyContinue
if ($still) { Write-Output '  [错误] 游戏仍在运行，请手动关闭'; exit 1 }

if ($NoCache) {
    Write-Output '=== 2) 清空缓存 ==='
    & $exe --cache-clear | Select-Object -Last 1
}

Write-Output "=== 3) 部署 $Chapter ==="
& $exe --launch $Chapter

$target = Join-Path (Split-Path $PSScriptRoot -Parent) '..'
Write-Output '=== 4) 完成 ==='
Write-Output '  进游戏后：F2 打开控制台 / 方向键移动 / E 交互'