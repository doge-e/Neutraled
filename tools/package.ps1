# Neutraled 发布打包器 -> <Out>\Neutraled-<Version>\{install,src}
#   install/ = 单文件自包含安装器（ntl-builder.exe + Magick 原生库）
#   src/     = 源码 + 运行期载荷（也是 --install 的复制源）
# 用法: powershell -ExecutionPolicy Bypass -File tools\package.ps1 [-Out <输出目录>] [-Version 1.0.7] [-Flavor full|trim] [-NoPublish]
#   默认 full（约 60 MB：exe 38.39 + Magick 原生库 21.41）。
#   ⚠ 不要用 trim 出正式包：实测 -p:PublishTrimmed=true 会把 System.Text.Json 的反射序列化剪掉，
#     导致 mod.json 读取报 'Reflection-based serialization has been disabled' 而静默丢 mod、冲突分析失真
#     （c2 电池：--conflicts 从 exit 2 变 exit 0）。裁剪路线要等 JSON 改成 source-generated 后再评估。
param(
    [string]$Out = '',
    [string]$Version = '1.0.7',
    [ValidateSet('trim', 'full')][string]$Flavor = 'full',
    [switch]$NoPublish
)
$ErrorActionPreference = 'Stop'
$N = Split-Path $PSScriptRoot -Parent
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }

# 会话输出约定：默认 <E:\aiwork\out\<会话名>>\release
if ([string]::IsNullOrEmpty($Out)) {
    $sessFile = 'E:\aiwork\out\.current-session'
    $sess = 'default'
    if (Test-Path -LiteralPath $sessFile) { $sess = (Get-Content -LiteralPath $sessFile -Raw).Trim() }
    if ([string]::IsNullOrEmpty($sess)) { $sess = 'default' }
    $Out = Join-Path (Join-Path 'E:\aiwork\out' $sess) 'release'
}
$rel        = Join-Path $Out ('Neutraled-' + $Version)
$installDir = Join-Path $rel 'install'
$srcDir     = Join-Path $rel 'src'

Write-Host '===== Neutraled release packager =====' -ForegroundColor Cyan
Write-Host ('  project : ' + $N)
Write-Host ('  release : ' + $rel)
Write-Host ('  flavor  : ' + $Flavor)

if (Test-Path -LiteralPath $rel) { Remove-Item -LiteralPath $rel -Recurse -Force }
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
New-Item -ItemType Directory -Force -Path $srcDir | Out-Null

# ---- 1) install/：自包含单文件安装器 ----
if (-not $NoPublish) {
    $trimArgs = @()
    if ($Flavor -eq 'trim') {
        $trimArgs = @('-p:PublishTrimmed=true', '-p:TrimMode=partial')
        Write-Warning 'Flavor=trim：System.Text.Json 反射会被裁掉（mod.json 解析失败），仅供实验，勿用于发布！'
    }
    Write-Host '  publishing ntl-builder (self-contained single file) ...'
    & $dotnet publish (Join-Path $N 'builder\Neutraled.Builder.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true @trimArgs -o $installDir -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
}
# 只留运行必需：pdb / xml 文档 / deps 之类不进发布包
foreach ($pat in @('*.pdb', '*.xml', '*.deps.json', '*.runtimeconfig.json')) {
    Get-ChildItem -LiteralPath $installDir -Filter $pat -File -ErrorAction SilentlyContinue | Remove-Item -Force
}
$exe = Join-Path $installDir 'ntl-builder.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw ('installer not found: ' + $exe) }

# ---- 2) src/：源码 + 运行期载荷 ----
$copyDirs = @('api', 'docs', 'fonts', 'tools', 'bside', 'scripts', 'plugins', 'web', 'themes', 'templates', 'kristal', 'kristal-maps', 'live', 'sdk', 'builder', 'gui', 'studio')
foreach ($d in $copyDirs) {
    $s = Join-Path $N $d
    if (Test-Path -LiteralPath $s) { Copy-Item -LiteralPath $s -Destination (Join-Path $srcDir $d) -Recurse -Force; Write-Host ('  + ' + $d + '/') }
}
# bside/：分两层，靠**物理位置**区分个人数据与随包数据 ——
#   bside/templates/*.sav = **随包默认模板**（用户已抹掉个人数据，随发布包分发）
#   bside/*.sav（根层）   = 玩家用 --import-bside 导入的个人存档 → 绝不进发布包
$bsideDst = Join-Path $srcDir 'bside'
if (Test-Path -LiteralPath $bsideDst) {
    Get-ChildItem -LiteralPath $bsideDst -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -ne '.txt' } | Remove-Item -Force -ErrorAction SilentlyContinue
    $tmplDst = Join-Path $bsideDst 'templates'
    if (Test-Path -LiteralPath $tmplDst) {
        Get-ChildItem -LiteralPath $tmplDst -File -Force -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -ne '.sav' -and $_.Extension -ne '.txt' } | Remove-Item -Force -ErrorAction SilentlyContinue
        $shipped = @(Get-ChildItem -LiteralPath $tmplDst -File -Filter '*.sav' -ErrorAction SilentlyContinue)
        if ($shipped.Count -gt 0) {
            Write-Host ('  bside/ (README + ' + $shipped.Count + ' shipped template(s))')
            foreach ($t in $shipped) {
                $h = (Get-FileHash -LiteralPath $t.FullName -Algorithm SHA256).Hash.Substring(0, 16).ToLower()
                Write-Host ('        templates/' + $t.Name + '  ' + $t.Length + ' B  sha256=' + $h)
            }
        } else {
            Write-Warning 'bside/templates/ 里没有 *.sav：发布包不会带 B 面默认模板'
        }
    } else {
        Write-Warning 'bside/templates/ 不存在：发布包不会带 B 面默认模板'
    }
    Write-Host '  bside/ : personal *.sav (root level) excluded'
}

# 编译中间产物不进发布包
Get-ChildItem -LiteralPath $srcDir -Recurse -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' -or $_.Name -eq '.vs' -or $_.Name -eq 'node_modules' } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# lang/：只带成品语言包（译者的临时分片 _<code>.partNN.json 不进）
$langDst = Join-Path $srcDir 'lang'
New-Item -ItemType Directory -Force -Path $langDst | Out-Null
$langCount = 0
Get-ChildItem -LiteralPath (Join-Path $N 'lang') -File -Filter '*.json' -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '_*.part*.json' -and $_.Name -notlike '*.bak' } | ForEach-Object { Copy-Item $_.FullName (Join-Path $langDst $_.Name) -Force; $langCount++ }
Write-Host ('  + lang/ (' + $langCount + ' packs)')

# mods/：只带示例 mod（开发树里可能有几个 GB 的用户 mod）
$modsDst = Join-Path $srcDir 'mods'
New-Item -ItemType Directory -Force -Path $modsDst | Out-Null
foreach ($m in @('ExampleMod', 'InteropA', 'InteropB', 'HookExample')) {
    $s = Join-Path $N ('mods\' + $m)
    if (Test-Path -LiteralPath $s) { Copy-Item -LiteralPath $s -Destination (Join-Path $modsDst $m) -Recurse -Force; Write-Host ('  + mods/' + $m + '/') }
}

foreach ($f in @('README.md', 'LICENSE', 'NOTICE', 'launch.ps1', 'test.bat', 'test.ps1', 'console-theme.json', 'api-registry.json', 'kristal-scripts.txt')) {
    $s = Join-Path $N $f
    if (Test-Path -LiteralPath $s) { Copy-Item -LiteralPath $s (Join-Path $srcDir $f) -Force; Write-Host ('  + ' + $f) }
}

# ---- 3) 自检：安装器要求 src/ 里有 api/ 与 docs/ ----
foreach ($need in @('api', 'docs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $srcDir $need))) { throw ('src/' + $need + ' 缺失，安装器找不到源目录') }
}

# ---- 4) 发布根目录的 README.txt ----
$tplPath = Join-Path $PSScriptRoot 'release-readme.txt'
if (Test-Path -LiteralPath $tplPath) {
    $enc = New-Object System.Text.UTF8Encoding($false)
    $text = [System.IO.File]::ReadAllText($tplPath, [System.Text.Encoding]::UTF8).Replace('{{VERSION}}', $Version)
    [System.IO.File]::WriteAllText((Join-Path $rel 'README.txt'), $text, $enc)
    Write-Host '  + README.txt'
}

Write-Host ''
Write-Host '===== done =====' -ForegroundColor Green
foreach ($part in @('install', 'src')) {
    $files = Get-ChildItem -LiteralPath (Join-Path $rel $part) -Recurse -File -Force -ErrorAction SilentlyContinue
    Write-Host ('  ' + $part.PadRight(8) + $files.Count.ToString().PadLeft(6) + ' files  ' + [math]::Round((($files | Measure-Object Length -Sum).Sum / 1MB), 2) + ' MB')
}
Write-Host ('  release: ' + $rel)