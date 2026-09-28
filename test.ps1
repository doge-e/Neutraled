# Neutraled 一键测试（test.ps1）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File test.ps1            快速检查（不启动游戏）
#   powershell -ExecutionPolicy Bypass -File test.ps1 -Full      完整测试（含启动游戏）
#   powershell -ExecutionPolicy Bypass -File test.ps1 -Deploy    含部署

param(
    [switch]$Full,
    [switch]$Deploy,
    [switch]$Quiet
)

$ErrorActionPreference = "Continue"
$root = $PSScriptRoot
$exe  = Join-Path $root "builder\bin\Release\net9.0\ntl-builder.exe"

$pass = 0; $fail = 0; $warn = 0

function Step($name, $block) {
    Write-Host ""
    Write-Host "──── $name ────" -ForegroundColor Cyan
    try {
        # 块里既有命令输出、又有末行的退出码，所以 & $block 拿到的是数组。
        # ⚠ 旧写法 `$r = & $block; if ($r -eq 0)` 在数组 [0] 上恒为假 ⇒ 所有块都被误判 FAIL（2026-09-27 修）。
        $out = @(& $block)
        if ($out.Count -gt 1) { $out[0..($out.Count - 2)] | ForEach-Object { Write-Host ('  ' + $_) } }
        $r = if ($out.Count -gt 0) { $out[-1] } else { 0 }
        if ($r -eq $null) { $r = 0 }
        if ($r -eq 0) { $script:pass++; Write-Host "  PASS" -ForegroundColor Green }
        else { $script:fail++; Write-Host "  FAIL (exit $r)" -ForegroundColor Red }
        return $r
    } catch {
        $script:fail++
        Write-Host "  ERROR: $_" -ForegroundColor Red
        return 1
    }
}

if (-not (Test-Path $exe)) {
    Write-Host "[错误] 找不到 ntl-builder.exe" -ForegroundColor Red
    Write-Host "       先编译: dotnet build builder/Neutraled.Builder.csproj -c Release"
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "  Neutraled 测试套件" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow

# ---------- 1. 静态检查 ----------
Step "1. 静态检查 (lint)" {
    & $exe --lint 2>&1 | Select-Object -Last 3
    $LASTEXITCODE
}

# ---------- 2. 自检 ----------
Step "2. 自检 (doctor)" {
    & $exe --doctor 2>&1 | Select-String -Pattern "统计|错误|警告|提示" | Select-Object -First 6
    $LASTEXITCODE
}

# ---------- 3. API 冒烟 ----------
Step "3. API 冒烟 (smoke)" {
    & $exe --smoke 2>&1 | Select-String -Pattern "统计|可疑|找到" | Select-Object -First 5
    $LASTEXITCODE
}

# ---------- 4. mod 列表 ----------
Step "4. mod 列表" {
    & $exe --mod-list 2>&1 | Select-Object -Last 3
    0
}

# ---------- 5. 存档快照 ----------
Step "5. 存档快照" {
    & $exe --save-list 2>&1 | Select-Object -Last 4
    0
}

# ---------- 6. Kristal 兼容性（可选） ----------
$kristal = "E:\steam\steamapps\common\DELTARUNE\Kristal-main\mods\chapter5wr_windows"
if (Test-Path $kristal) {
    Step "6. Kristal 脚本验证" {
        & $exe --validate-kristal $kristal 2>&1 | Select-String -Pattern "文件:|语法检查通过|未实现" | Select-Object -First 4
        0
    }
} else {
    Write-Host ""; Write-Host "──── 6. Kristal 脚本验证 ────" -ForegroundColor DarkGray
    Write-Host "  SKIP（找不到 Kristal mod）" -ForegroundColor DarkGray
}

# ---------- 7. 部署（可选） ----------
if ($Deploy) {
    Step "7. 部署 root + chapter4" {
        Get-Process -Name "DELTARUNE" -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 3
        & $exe --deploy --chapter root 2>&1 | Select-String "部署完成" | Select-Object -First 1
        $r1 = $LASTEXITCODE
        & $exe --deploy --chapter chapter4 2>&1 | Select-String "部署完成" | Select-Object -First 1
        $r2 = $LASTEXITCODE
        if ($r1 -eq 0 -and $r2 -eq 0) { 0 } else { 1 }
    }
}

# ---------- 8. 游戏内测试（可选） ----------
if ($Full) {
    Step "8. 启动游戏 + mod 联动验收" {
        Get-Process -Name "DELTARUNE" -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 3
        & $exe --deploy --chapter root 2>&1 | Out-Null
        Start-Sleep -Seconds 2

        $log = Join-Path $env:LOCALAPPDATA "DELTARUNE\Neutraled\dr-api.log"
        if (Test-Path $log) { Remove-Item $log -Force }
        Start-Process "E:\DELTARUNE.url"
        Write-Host "  等待游戏启动（50 秒）..." -ForegroundColor DarkGray
        Start-Sleep -Seconds 50

        Write-Host ""
        Write-Host "  === mod 联动验收 ===" -ForegroundColor Cyan
        $lines = Get-Content $log -Encoding UTF8 | Select-String -Pattern "InteropB\] \[|运行错误|异常" | Select-Object -First 25
        $lines | ForEach-Object { $_.Line }

        $ok = ($lines | Select-String -Pattern "\[1\]|\[2\]|\[6\]|\[12\]").Count
        if ($ok -ge 3) { Write-Host "  验收通过（$ok 项）" -ForegroundColor Green; 0 }
        else { Write-Host "  验收疑似失败" -ForegroundColor Red; 1 }
    }
} else {
    Write-Host ""; Write-Host "──── 8. 游戏内测试 ────" -ForegroundColor DarkGray
    Write-Host "  SKIP（加 -Full 启用）" -ForegroundColor DarkGray
}

# ---------- 汇总 ----------
Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ("  结果: {0} 通过 / {1} 失败" -f $pass, $fail) -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Red" })
Write-Host "========================================" -ForegroundColor Yellow
Write-Host ""

if ($fail -eq 0) { exit 0 } else { exit 1 }