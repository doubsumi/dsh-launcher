@echo off
rem ============================================================================
rem  Stop the DeepSeek Harness instance the launcher started.
rem
rem  Identity is checked before anything is killed: the target must be node.exe
rem  AND its command line must reference the dsh package
rem  (...\@deepseek-ai\dsh\lib\bin.js). Other node programs (e.g. a dev server
rem  that happens to listen on the same port) are never touched.
rem
rem  Target selection: logs\dsh.pid (written by the launcher when the port came
rem  up) first, then the process listening on the configured port (DSH_PORT).
rem ============================================================================
setlocal EnableExtensions
set "APP_DIR=%~dp0"
set "PORT=3080"
if defined DSH_PORT set "PORT=%DSH_PORT%"

set "PID="
set "VIA="
if exist "%APP_DIR%logs\dsh.pid" (
    for /f "usebackq delims=" %%p in ("%APP_DIR%logs\dsh.pid") do set "PID=%%p"
)
if defined PID (
    call :verify %PID%
    if defined IS_DSH set "VIA=pid"
    if not defined IS_DSH set "PID="
)

if not defined PID (
    for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT% " ^| findstr "LISTENING"') do if not defined PID set "PID=%%p"
    if defined PID (
        call :verify %PID%
        if defined IS_DSH set "VIA=port"
        if not defined IS_DSH set "PID="
    )
)

if not defined PID (
    echo No DSH process to stop ^(port %PORT% has no dsh listener^).
    if exist "%APP_DIR%logs\dsh.pid" del "%APP_DIR%logs\dsh.pid" >nul 2>nul
    exit /b 0
)

taskkill /pid %PID% /t /f >nul 2>nul
if errorlevel 1 (
    echo [WARN] Could not kill PID %PID% ^(access denied?^). Try running as administrator.
    exit /b 1
)
if "%VIA%"=="port" (
    echo Stopped DSH ^(PID %PID%, port %PORT%^).
) else (
    echo Stopped DSH ^(PID %PID%^).
)
del "%APP_DIR%logs\dsh.pid" >nul 2>nul
exit /b 0

rem --- sets IS_DSH=1 when the pid is node.exe and its command line is a dsh path ---
:verify
set "IS_DSH="
tasklist /fi "PID eq %~1" /fo csv /nh 2>nul | findstr /i "node.exe" >nul
if errorlevel 1 exit /b 0
for /f "delims=" %%c in ('wmic process where "processid=%~1" get commandline /value 2^>nul ^| findstr /i /c:"@deepseek-ai\dsh"') do set "IS_DSH=1"
exit /b 0