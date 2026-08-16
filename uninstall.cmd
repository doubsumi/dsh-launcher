@echo off
rem ============================================================================
rem  DeepSeek Harness launcher - COMPLETE UNINSTALL
rem  ----------------------------------------------------------------------------
rem  1. stop a running DSH server (only if it is node.exe on port 3080)
rem  2. remove the right-click verb from the registry (restore system state)
rem  3. remove the desktop shortcut "DeepSeek Harness"
rem  4. delete ALL files in this folder (including this script)
rem ============================================================================
setlocal EnableExtensions
set "APP_DIR=%~dp0"
if "%APP_DIR:~-1%"=="\" set "APP_DIR=%APP_DIR:~0,-1%"
set "PS=%APP_DIR%\setup-helpers.ps1"
set "ICON=%APP_DIR%\assets\deepseek-whale.ico"

echo ============================================================
echo   DeepSeek Harness launcher - uninstall
echo ============================================================
echo This will:
echo   1. stop a running DSH server (port 3080, only if node.exe)
echo   2. remove the right-click menu item from the registry
echo   3. remove the desktop shortcut "DeepSeek Harness"
echo   4. delete ALL files in: %APP_DIR%
echo.
set /p CONFIRM="Type Y and press Enter to continue (anything else cancels): "
if /i not "%CONFIRM%"=="Y" (
    echo.
    echo Cancelled. Nothing was changed.
    exit /b 0
)

echo.
echo [1/4] Stopping DSH server (if running)...
call "%APP_DIR%\stop-dsh.cmd"

echo [2/4] Removing right-click verb from the registry...
echo [3/4] Removing desktop shortcut...
if exist "%PS%" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS%" -Action uninstall -AppDir "%APP_DIR%" -IconPath "%ICON%"
)

echo [4/4] Scheduling removal of this folder...
set "CLEANER=%TEMP%\dsh_uninstall_cleanup.cmd"
(
    echo @echo off
    echo timeout /t 2 /nobreak ^>nul
    echo rmdir /s /q "%APP_DIR%" 2^>nul
    echo timeout /t 2 /nobreak ^>nul
    echo rmdir /s /q "%APP_DIR%" 2^>nul
    echo timeout /t 2 /nobreak ^>nul
    echo rmdir /s /q "%APP_DIR%" 2^>nul
    echo del "%TEMP%\dsh_uninstall_cleanup.cmd" ^>nul 2^>nul
) > "%CLEANER%"
start "" /min cmd /c "%CLEANER%"

echo.
echo Uninstall complete.
echo If any file was in use, the remainder is removed a few seconds after the
echo server stops; otherwise the folder is already gone.
exit /b 0
