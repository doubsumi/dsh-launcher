# Helper for setup.cmd: create a REAL .lnk desktop shortcut (a normal program
# launcher) and register a SCOPED right-click verb that appears ONLY on that one
# shortcut.
#
# The verb lives under the lnkfile PROGID (the key Explorer actually reads for
# .lnk files) with an "AppliesTo" (Advanced Query Syntax) filter matching the
# exact shortcut file name. Windows therefore ADDS it to the standard shortcut
# context menu (open / open file location / run as administrator / run as
# different user / properties ...) without touching any other shortcut.
#
# NOTE: pure ASCII file; Chinese strings are base64-encoded UTF-8.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('install', 'uninstall')]
    [string]$Action,

    [string]$AppDir,
    [string]$IconPath
)

$ErrorActionPreference = 'Stop'

function Decode-Utf8B64([string]$b64) {
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
}

$verbName = Decode-Utf8B64 '5L+d5oyBY21k56qX5Y+j6L+Q6KGM'
$lnkDesc  = Decode-Utf8B64 'RGVlcFNlZWsgSGFybmVzczog5ZCO5Y+w6ZqQ6JeP56qX5Y+j5ZCv5Yqo77yM5pel5b+X6KeBIGxhdW5jaGVyIOeahCBsb2dzIOebruW9lQ=='

# Derive launcher folder from this script's own location when not supplied, so
# the script is safe to call without -AppDir/-IconPath (e.g. setup.cmd /uninstall).
if ([string]::IsNullOrWhiteSpace($AppDir)) {
    $AppDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrWhiteSpace($IconPath)) {
    $IconPath = Join-Path $AppDir 'assets\deepseek-whale.ico'
}

$shortcutName = 'DeepSeek Harness.lnk'
$desktop   = [Environment]::GetFolderPath('Desktop')
$lnkPath   = Join-Path $desktop $shortcutName
$exePath   = Join-Path $AppDir 'DeepSeekHarnessLauncher.exe'

# scoped verb (per-user, under the lnkfile PROGID, filtered by exact file name)
$verbKey    = 'HKCU:\Software\Classes\lnkfile\shell\DSHKeepWindow'
$verbCmdKey = "$verbKey\command"
$appliesTo  = 'System.FileName:="DeepSeek Harness.lnk"'

# remnants of earlier approaches, cleaned defensively on install/uninstall
$oldExtVerbKey = 'HKCU:\Software\Classes\.lnk\shell\DSHKeepWindow'    # v2 wrong placement
$oldClassKey   = 'HKCU:\Software\Classes\DeepSeekHarnessLauncher'      # v2 custom type
$oldExtKey     = 'HKCU:\Software\Classes\.dshlauncher'                 # v2 custom type
$oldDskFile    = Join-Path $desktop 'DeepSeek Harness.dshlauncher'     # v2 file

function Remove-Remnants {
    foreach ($k in @($oldExtVerbKey, $oldClassKey, $oldExtKey)) {
        if (Test-Path $k) { Remove-Item -Path $k -Recurse -Force; Write-Output "Removed stale registration: $k" }
    }
    if (Test-Path $oldDskFile) { Remove-Item -Path $oldDskFile -Force; Write-Output "Removed stale file: $oldDskFile" }
}

switch ($Action) {
    'install' {
        if (-not (Test-Path $exePath))  { throw "Missing launcher exe: $exePath" }
        if (-not (Test-Path $IconPath)) { throw "Missing icon: $IconPath" }

        Remove-Remnants

        # 1) real .lnk shortcut -> launcher exe (normal program; standard menu)
        $ws = New-Object -ComObject WScript.Shell
        $lnk = $ws.CreateShortcut($lnkPath)
        $lnk.TargetPath = $exePath
        $lnk.Arguments = ''
        $lnk.WorkingDirectory = $AppDir
        $lnk.IconLocation = "$exePath,0"   # exe's embedded whale icon
        $lnk.Description = $lnkDesc
        $lnk.Save()
        Write-Output "Shortcut created: $lnkPath"

        # 2) ADD one verb to this shortcut only (AppliesTo filter)
        New-Item -Path $verbKey -Force | Out-Null
        Set-ItemProperty -Path $verbKey -Name '(default)' -Value $verbName
        Set-ItemProperty -Path $verbKey -Name 'AppliesTo' -Value $appliesTo
        Set-ItemProperty -Path $verbKey -Name 'Icon' -Value "$exePath,0"
        New-Item -Path $verbCmdKey -Force | Out-Null
        Set-ItemProperty -Path $verbCmdKey -Name '(default)' -Value ('"{0}" --visible' -f $exePath)
        Write-Output "Scoped verb added to: $shortcutName (only this shortcut)."
    }
    'uninstall' {
        Remove-Remnants
        if (Test-Path $verbKey) { Remove-Item -Path $verbKey -Recurse -Force; Write-Output 'Removed scoped verb.' }
        if (Test-Path $lnkPath) { Remove-Item -Path $lnkPath -Force; Write-Output "Removed shortcut: $lnkPath" }
        Write-Output 'Restore complete.'
    }
}
