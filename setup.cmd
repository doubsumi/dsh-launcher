@echo off
rem ============================================================================
rem  DeepSeek Harness launcher - setup / repair / uninstall
rem  ----------------------------------------------------------------------------
rem  setup.cmd            create the desktop shortcut + SCOPED right-click verb
rem  setup.cmd /uninstall full restore (remove verb + desktop shortcut)
rem  setup.cmd /clean     same as /uninstall
rem  NOTE: pure ASCII file (Chinese strings are base64-encoded in the .ps1).
rem ============================================================================
setlocal EnableExtensions
set "APP_DIR=%~dp0"
if "%APP_DIR:~-1%"=="\" set "APP_DIR=%APP_DIR:~0,-1%"
set "ICON=%APP_DIR%\assets\deepseek-whale.ico"
set "PS=%APP_DIR%\setup-helpers.ps1"

if /i "%~1"=="/uninstall" goto uninstall
if /i "%~1"=="/clean" goto uninstall
goto install

:install
echo === DeepSeek Harness launcher setup ===
if not exist "%ICON%" (
    echo [ERROR] Icon file not found: %ICON%
    exit /b 1
)
if not exist "%APP_DIR%\DeepSeekHarnessLauncher.exe" (
    echo [ERROR] Launcher exe not found: %APP_DIR%\DeepSeekHarnessLauncher.exe
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS%" -Action install -AppDir "%APP_DIR%" -IconPath "%ICON%"
if errorlevel 1 (
    echo [ERROR] Setup failed.
    exit /b 1
)
echo.
echo Done!
echo   - Desktop shortcut created: "DeepSeek Harness.lnk" -^> DeepSeekHarnessLauncher.exe
echo   - Right-click THIS shortcut -^> "Keep cmd window running" (visible window)
echo   - Only this shortcut gets the extra menu item; others are untouched.
echo   - To fully restore your system: setup.cmd /uninstall
exit /b 0

:uninstall
echo === Full restore: removing scoped verb and desktop shortcut ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS%" -Action uninstall -AppDir "%APP_DIR%" -IconPath "%ICON%"
exit /b %errorlevel%
