@echo off
chcp 65001 >nul
rem Neutraled 一键测试 —— 双击即可运行
rem 参数: --full (含游戏内测试)  --deploy (含部署)

setlocal
set ROOT=%~dp0
set EXE=%ROOT%builder\bin\Release\net9.0\ntl-builder.exe

if not exist "%EXE%" (
  echo [错误] 找不到 ntl-builder.exe
  echo        请先编译: dotnet build builder\Neutraled.Builder.csproj -c Release
  pause
  exit /b 1
)

echo.
echo ========================================
echo   Neutraled 测试套件
echo ========================================

echo.
echo ---- 1. 静态检查 (lint) ----
"%EXE%" --lint

echo.
echo ---- 2. 自检 (doctor) ----
"%EXE%" --doctor

echo.
echo ---- 3. API 冒烟 (smoke) ----
"%EXE%" --smoke

if /i "%1"=="--deploy" goto :deploy
if /i "%1"=="--full" goto :full
goto :extras

:deploy
echo.
echo ---- 4. 部署 ----
taskkill /f /im DELTARUNE.exe >nul 2>&1
timeout /t 3 /nobreak >nul
"%EXE%" --deploy --chapter root
"%EXE%" --deploy --chapter chapter4
goto :extras

:full
echo.
echo ---- 4. 部署 + 游戏内测试 ----
taskkill /f /im DELTARUNE.exe >nul 2>&1
timeout /t 3 /nobreak >nul
"%EXE%" --deploy --chapter root
timeout /t 2 /nobreak >nul
del /q "%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log" >nul 2>&1
start "" "E:\DELTARUNE.url"
echo   等待游戏启动^(50 秒^)...
timeout /t 50 /nobreak >nul
echo.
echo   === mod 联动验收 ===
findstr /c:"InteropB] [" "%LOCALAPPDATA%\DELTARUNE\Neutraled\dr-api.log"
goto :extras

:extras
echo.
echo ---- 5. mod 列表 ----
"%EXE%" --mod-list

echo.
echo ---- 6. 存档快照 ----
"%EXE%" --save-list

echo.
echo ---- 7. Kristal 脚本验证 ----
if exist "E:\steam\steamapps\common\DELTARUNE\Kristal-main\mods\chapter5wr_windows" (
  "%EXE%" --validate-kristal "E:\steam\steamapps\common\DELTARUNE\Kristal-main\mods\chapter5wr_windows"
) else (
  echo   SKIP^(找不到 Kristal mod^)
)

echo.
echo ---- 8. API 文档生成 ----
"%EXE%" --api-doc

echo.
echo ========================================
echo   测试完成
echo ========================================
echo.
pause