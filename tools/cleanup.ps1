# Neutraled C-drive cleanup
# Removes temp/build/verify leftovers so the C: drive does not fill up.
# Usage: powershell -ExecutionPolicy Bypass -File cleanup.ps1 [-ShowReport]

param([switch]$ShowReport)

$ErrorActionPreference = 'SilentlyContinue'
$script:freed = 0
$script:report = New-Object System.Collections.ArrayList

function Remove-PathSafe {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path $Path)) { return }
    $size = 0
    try {
        $item = Get-Item $Path -Force
        if ($item.PSIsContainer) {
            $sum = (Get-ChildItem $Path -Recurse -File -Force | Measure-Object -Property Length -Sum).Sum
            if ($sum) { $size = $sum }
        } else {
            $size = $item.Length
        }
    } catch { }
    try {
        Remove-Item $Path -Recurse -Force
        $script:freed += $size
        [void]$script:report.Add(("{0,-44} {1,9:N1} MB" -f $Label, ($size / 1MB)))
    } catch { }
}

# 1) xdelta conversion work dirs
Get-ChildItem $env:TEMP -Directory -Filter 'ntl_conv_*' -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-PathSafe -Path $_.FullName -Label ('conv-temp/' + $_.Name)
}

# 2) downloaded mod zips older than 1 day
Get-ChildItem $env:TEMP -File -Filter '*.zip' -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-1) } | ForEach-Object {
    Remove-PathSafe -Path $_.FullName -Label ('old-download/' + $_.Name)
}

# 3) game screenshots - keep newest 6
$shotDir = Join-Path $env:LOCALAPPDATA 'DELTARUNE'
Get-ChildItem $shotDir -Filter 'ntl_*.png' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -Skip 6 | ForEach-Object {
    Remove-PathSafe -Path $_.FullName -Label ('old-shot/' + $_.Name)
}

# 4) runtime logs older than 3 days
$logDir = Join-Path $env:LOCALAPPDATA 'DELTARUNE\Neutraled'
Get-ChildItem $logDir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-3) } | ForEach-Object {
    Remove-PathSafe -Path $_.FullName -Label ('old-log/' + $_.Name)
}

# 5) unpacked mod cache older than 3 days
$unpacked = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.unpacked'
Get-ChildItem $unpacked -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-3) } | ForEach-Object {
    Remove-PathSafe -Path $_.FullName -Label ('.unpacked/' + $_.Name)
}

# 6) NuGet http cache older than 7 days
$nugetHttp = Join-Path $env:LOCALAPPDATA 'NuGet\v3-cache'
Get-ChildItem $nugetHttp -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) } | ForEach-Object {
    Remove-PathSafe -Path $_.FullName -Label ('nuget-cache/' + $_.Name)
}

# 7) common installer leftovers in TEMP (thunder / choco / updaters), older than 3 days
$leftoverNames = @('ThunderInstall','ThunderUninstall','ThunderEv','Thunder','XLLiveUD','OnlineInstall','chocolatey','baiduyunguanjia','BaiduYunGuanjia','NVIDIA','Office')
foreach ($n in $leftoverNames) {
    Get-ChildItem $env:TEMP -Directory -Filter $n -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-3) } | ForEach-Object {
        Remove-PathSafe -Path $_.FullName -Label ('leftover/' + $_.Name)
    }
    Get-ChildItem $env:TEMP -File -Filter ($n + '.*') -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-3) } | ForEach-Object {
        Remove-PathSafe -Path $_.FullName -Label ('leftover/' + $_.Name)
    }
}

# 8) stale node/dotnet temp files older than 1 day
Get-ChildItem $env:TEMP -File -ErrorAction SilentlyContinue |
    Where-Object { ($_.Name -like '*.tmp.node' -or $_.Name -like '*.tmp') -and $_.LastWriteTime -lt (Get-Date).AddDays(-1) } |
    ForEach-Object { Remove-PathSafe -Path $_.FullName -Label ('tmpnode/' + $_.Name) }

# 9) orphan GUID folders in TEMP older than 7 days
Get-ChildItem $env:TEMP -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-' -and $_.LastWriteTime -lt (Get-Date).AddDays(-7) } |
    ForEach-Object { Remove-PathSafe -Path $_.FullName -Label ('guid-temp/' + $_.Name) }

# 10) large temp files (>200MB, older than 1 day)
Get-ChildItem $env:TEMP -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Length -gt 200MB -and $_.LastWriteTime -lt (Get-Date).AddDays(-1) } | ForEach-Object {
    Remove-PathSafe -Path $_.FullName -Label ('big-temp/' + $_.Name)
}

$freeGB = [math]::Round((Get-PSDrive C).Free / 1GB, 2)
if ($ShowReport -or $script:freed -gt 0) {
    Write-Output '=== Neutraled cleanup ==='
    foreach ($line in $script:report) { Write-Output $line }
    Write-Output ('freed total: {0:N1} MB' -f ($script:freed / 1MB))
}
Write-Output ('C: free space: {0} GB' -f $freeGB)
