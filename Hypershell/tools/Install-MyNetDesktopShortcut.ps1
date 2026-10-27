[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppDirectory,
    [string]$ShortcutDirectory = [Environment]::GetFolderPath('Programs'),
    [string]$ApplicationDataDirectory = $(if ($env:HYPERSHELL_APP_DATA) { $env:HYPERSHELL_APP_DATA } else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Hypershell' })
)
$ErrorActionPreference = 'Stop'
$appRoot = (Resolve-Path -LiteralPath $AppDirectory).Path
$executable = Join-Path $appRoot 'Hypershell.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $appRoot 'Hypershell.dll') -PathType Leaf)) {
    throw 'Choose a published Hypershell app directory containing the MyNet desktop build.'
}
# MyNet uses an explicit standalone target. Never rewrite Hypershell's stable shim.
$shortcutRoot = [IO.Path]::GetFullPath($ShortcutDirectory)
[IO.Directory]::CreateDirectory($shortcutRoot) | Out-Null
$shortcutPath = Join-Path $shortcutRoot 'MyNet.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $shortcutBackupRoot = Join-Path ([IO.Path]::GetFullPath($ApplicationDataDirectory)) 'AtlasMyNet\backups'
    [IO.Directory]::CreateDirectory($shortcutBackupRoot) | Out-Null
    $shortcutBackupPath = Join-Path $shortcutBackupRoot ('MyNet.lnk.before-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '-' + [Guid]::NewGuid().ToString('N') + '.bak')
    Copy-Item -LiteralPath $shortcutPath -Destination $shortcutBackupPath
}
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.Arguments = '--mynet-desktop'
$shortcut.WorkingDirectory = $appRoot
$myNetIcon = Join-Path $appRoot 'Assets\MyNet\mynet.ico'
$shortcut.IconLocation = if (Test-Path -LiteralPath $myNetIcon -PathType Leaf) { "$myNetIcon,0" } else { "$executable,0" }
$shortcut.Description = 'MyNet local apps and system tray launcher'
$shortcut.Save()
$saved = $shell.CreateShortcut($shortcutPath)
if ($saved.TargetPath -ne $executable -or $saved.Arguments -ne '--mynet-desktop' -or $saved.WorkingDirectory -ne $appRoot) {
    throw 'The saved MyNet shortcut did not match the requested standalone target.'
}
# The in-app button uses the same explicit target, even when Hypershell and MyNet
# have separate published builds. This file contains no commands or arguments.
$registrationRoot = Join-Path ([IO.Path]::GetFullPath($ApplicationDataDirectory)) 'AtlasMyNet'
[IO.Directory]::CreateDirectory($registrationRoot) | Out-Null
$registrationPath = Join-Path $registrationRoot 'desktop-launcher.device.json'
if (Test-Path -LiteralPath $registrationPath) {
    Copy-Item -LiteralPath $registrationPath -Destination ($registrationPath + '.before-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.bak')
}
$registration = [ordered]@{ schemaVersion = 1; executablePath = $executable }
$temporaryPath = $registrationPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporaryPath, ($registration | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $registrationPath) {
        [IO.File]::Replace($temporaryPath, $registrationPath, [NullString]::Value)
    }
    else {
        [IO.File]::Move($temporaryPath, $registrationPath)
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath }
}
[pscustomobject]@{ Shortcut = $shortcutPath; Executable = $saved.TargetPath; Arguments = $saved.Arguments; Registration = $registrationPath }
