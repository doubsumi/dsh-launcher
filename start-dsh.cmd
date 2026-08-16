@echo off
rem ============================================================================
rem  DeepSeek Harness (DSH) launcher
rem  ----------------------------------------------------------------------------
rem  Usage:
rem    start-dsh.cmd            -> start DSH in the background (hidden window,
rem                                 output recorded to a log file)
rem    start-dsh.cmd visible    -> start DSH in THIS visible cmd window and keep
rem                                 the window open after exit (for debugging)
rem
rem  Optional environment overrides:
rem    DSH_PORT=3080            -> port (default 3080)
rem    DSH_NO_BROWSER=1         -> do not auto-open the browser
rem
rem  Logs: <this folder>\logs\dsh_<timestamp>.log
rem  PID  : <this folder>\logs\dsh.pid   (of the process listening on the port)
rem ============================================================================
setlocal EnableExtensions
title DeepSeek Harness Launcher

set "APP_DIR=%~dp0"
cd /d "%APP_DIR%"

set "PORT=3080"
if defined DSH_PORT set "PORT=%DSH_PORT%"
set "URL=http://127.0.0.1:%PORT%"
set "MODE=%~1"
if /i "%MODE%"=="" set "MODE=hidden"

set "LOG_DIR=%APP_DIR%logs"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>nul

rem --- timestamps (locale-independent, via PowerShell) ---
for /f "tokens=1,2,3" %%a in ('powershell -NoProfile -Command "Get-Date -Format 'yyyyMMdd_HHmmss yyyy-MM-dd HH:mm:ss'"') do set "TS=%%a" & set "TSH=%%b %%c"
set "LOG=%LOG_DIR%\dsh_%TS%.log"

rem --- locate dsh (npm global bin, with PATH fallback) ---
set "DSH_CMD="
for /f "delims=" %%p in ('where dsh.cmd 2^>nul') do if not defined DSH_CMD set "DSH_CMD=%%p"
if not defined DSH_CMD if exist "%APPDATA%\npm\dsh.cmd" set "DSH_CMD=%APPDATA%\npm\dsh.cmd"
if not defined DSH_CMD (
    echo [ERROR] dsh command not found. Install it with:  npm install -g @deepseek-ai/dsh
    if /i "%MODE%"=="visible" pause
    exit /b 1
)

echo [%TSH%] DSH launcher: mode=%MODE% url=%URL% dsh=%DSH_CMD% >> "%LOG%"

rem --- already running? (port in LISTEN state; netstat works even without WinRM/CIM) ---
netstat -ano | findstr ":%PORT% " | findstr "LISTENING" >nul 2>nul
if not errorlevel 1 goto already_running

rem --- not running: start it ---
if /i "%MODE%"=="visible" goto start_visible

:start_hidden
rem Launch dsh inside this (hidden) console; all output goes to the log file.
rem Note: once the server holds the log handle, other processes cannot append
rem to it, so post-start state goes to logs\status.txt instead.
start "" /b cmd /c ""%DSH_CMD%" web >>"%LOG%" 2>&1"
goto wait_ready

:start_visible
echo.
echo ============================================================
echo   DeepSeek Harness is starting...
echo   URL : %URL%
echo   Log : %LOG%
echo   Press Ctrl+C in this window to stop the server.
echo ============================================================
echo.
call "%DSH_CMD%" web
set "RC=%ERRORLEVEL%"
echo.
echo DSH server exited with code %RC%.
echo Log file: %LOG%
pause
exit /b %RC%

rem --- wait until the port is up, then record PID and open the browser ---
:wait_ready
set /a TRIES=0
:waitloop
netstat -ano | findstr ":%PORT% " | findstr "LISTENING" >nul 2>nul
if not errorlevel 1 goto port_up
set /a TRIES+=1
if %TRIES% GEQ 60 goto wait_timeout
timeout /t 1 /nobreak >nul 2>nul
goto waitloop

:port_up
set "PID="
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT% " ^| findstr "LISTENING"') do if not defined PID set "PID=%%p"
if defined PID echo %PID%>"%LOG_DIR%\dsh.pid"
(
echo state=running
echo time=%TSH%
echo url=%URL%
echo pid=%PID%
echo log=%LOG%
) > "%LOG_DIR%\status.txt"
if /i not "%DSH_NO_BROWSER%"=="1" start "" "%URL%"
exit /b 0

:wait_timeout
(
echo state=failed
echo time=%TSH%
echo url=%URL%
echo pid=
echo log=%LOG%
echo error=port %PORT% did not open within 60s
) > "%LOG_DIR%\status.txt"
exit /b 1

:already_running
(
echo state=already-running
echo time=%TSH%
echo url=%URL%
echo pid=
echo log=%LOG%
) > "%LOG_DIR%\status.txt"
if /i "%MODE%"=="visible" (
    echo.
    echo DSH is already running at %URL%
    echo.
)
if /i not "%DSH_NO_BROWSER%"=="1" start "" "%URL%"
if /i "%MODE%"=="visible" pause
exit /b 0
