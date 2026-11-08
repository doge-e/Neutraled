@echo off
rem ===========================================================================
rem  Neutraled ONE-CLICK TEST - thin forwarder (C4 P1-11 / plan section 5)
rem
rem  Static stages only (no game launch):
rem    - SkipPackage / -OutDir / -ImportDir are passed straight through
rem
rem  Game tests are separate (they DO launch the game):
rem    powershell -ExecutionPolicy Bypass -File _test\fulltest-game.ps1
rem    powershell -ExecutionPolicy Bypass -File _test\fulltest-external.ps1
rem
rem  Usage:
rem    test.bat                 full static suite
rem    test.bat -SkipPackage    skip the packaging stage (faster)
rem    test.bat --help          show this help
rem ===========================================================================
setlocal
chcp 65001 >nul

if /i "%~1"=="--help" goto :usage
if /i "%~1"=="-h" goto :usage

if not exist "%~dp0_test\fulltest-static.ps1" (
  echo [ERROR] _test\fulltest-static.ps1 not found next to test.bat
  pause
  exit /b 1
)

echo.
echo ============================================================
echo   Neutraled static regression ^(no game launch^)
echo ============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0_test\fulltest-static.ps1" %*
set RC=%ERRORLEVEL%
echo.
if not "%RC%"=="0" (
  echo Result: FAILED ^(exit %RC%^) - see the output dir passed via -OutDir
) else (
  echo Result: all static checks passed
)
pause
exit /b %RC%

:usage
echo Neutraled test entry
echo.
echo   test.bat                 run _test\fulltest-static.ps1 ^(static, no game^)
echo   test.bat -SkipPackage    same, skip packaging (faster)
echo.
echo Game-dependent suites ^(launch DELTARUNE; run them yourself, not from CI^):
echo   _test\fulltest-game.ps1
echo   _test\fulltest-external.ps1
echo.
echo Rationale: this file used to inline lint/doctor/smoke and to deploy chapter4 +
echo launch the game. That duplicated the PS suites and silently picked a chapter;
echo it is now a thin forwarder only.
exit /b 0
