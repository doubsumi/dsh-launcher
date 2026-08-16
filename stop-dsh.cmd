@echo off
rem ============================================================================
rem  Stop a running DeepSeek Harness instance.
rem  Finds the process listening on the DSH port (default 3080), verifies it is
rem  node.exe (dsh runs on Node), and kills it together with its child tree.
rem  Optional: set DSH_PORT to override the port.
rem ============================================================================
setlocal EnableExtensions
set "APP_DIR=%~dp0"
set "PORT=3080"
if defined DSH_PORT set "PORT=%DSH_PORT%"

rem --- find the PID listening on the port ---
set "PID="
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT% " ^| findstr "LISTENING"') do if not defined PID set "PID=%%p"

if not defined PID (
    echo No process is listening on port %PORT% - DSH does not seem to be running.
    if exist "%APP_DIR%logs\dsh.pid" del "%APP_DIR%logs\dsh.pid" >nul 2>nul
    exit /b 0
)

rem --- safety: only kill it if it is node.exe ---
tasklist /fi "PID eq %PID%" /fo csv /nh 2>nul | findstr /i "node.exe" >nul
if errorlevel 1 (
    echo [WARN] PID %PID% listening on port %PORT% is NOT node.exe - refusing to kill it.
    echo        Check it manually:  netstat -ano ^| findstr :%PORT%
    exit /b 1
)

taskkill /pid %PID% /t /f >nul 2>nul
if errorlevel 1 (
    echo [WARN] Could not kill PID %PID% (access denied?). Try running as administrator.
    exit /b 1
)
echo Stopped DSH (PID %PID%, port %PORT%).
if exist "%APP_DIR%logs\dsh.pid" del "%APP_DIR%logs\dsh.pid" >nul 2>nul
exit /b 0
