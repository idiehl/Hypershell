#requires -Version 5.1
<#
.SYNOPSIS
Installs or updates the stable per-user Hypershell launcher without changing user settings.
.DESCRIPTION
Uses an explicit, verified app executable. Existing files replaced by this installation are
backed up beside bin; unrelated bin files, profiles, PATH and pinned items are untouched.
Shortcuts target the WinExe launcher directly. The existing PowerShell Hypershell function
continues to use bin\Hypershell.cmd, which forwards arguments to that same launcher.
.EXAMPLE
.\tools\Install-HypershellLauncher.ps1 -LauncherPublishDirectory C:\build\launcher `
    -AppExecutable I:\dev\Hypershell\output\release\Hypershell.exe `
    -HyperlibRoot I:\dev\Hypershell\Hyperlib -DesktopShortcut
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory)] [string] $LauncherPublishDirectory,
    [Parameter(Mandatory)] [string] $AppExecutable,
    [string] $HyperlibRoot,
    [string] $ProfileDirectory,
    [switch] $DesktopShortcut
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This installer requires Windows.' }

function Get-FullPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A required path is empty.' }
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.Length -gt [IO.Path]::GetPathRoot($full).Length) { return $full.TrimEnd([IO.Path]::DirectorySeparatorChar) }
    return $full
}
function Get-LocalAbsolutePath([string] $Path, [string] $Setting) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 32760 -or $Path -notmatch '^[A-Za-z]:[\\/]' -or
        $Path.IndexOf(':', 2) -ge 0 -or $Path -match '["%*?<>|\p{Cc}]') {
        throw "$Setting must be an explicit absolute local path without UNC/device, URL, expansion, wildcard or alternate-stream syntax."
    }
    return Get-FullPath $Path
}
function Assert-ChildPath([string] $Path, [string] $Root) {
    $full = Get-FullPath $Path
    $prefix = (Get-FullPath $Root) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes its installation directory: $full" }
}
function Assert-NoLinks([string] $Path) {
    for ($itemPath = Get-FullPath $Path; $itemPath; $itemPath = [IO.Path]::GetDirectoryName($itemPath)) {
        if (Test-Path -LiteralPath $itemPath) {
            if ((Get-Item -LiteralPath $itemPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Installation paths cannot traverse symbolic links or junctions: $itemPath"
            }
        }
    }
}
function Get-ExecutableSubsystem([string] $Path) {
    if (-not [IO.File]::Exists($Path)) { throw "Executable not found: $Path" }
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 96 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "Not a Windows executable: $Path" }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        if ($peOffset -gt $stream.Length - 96) { throw "Invalid executable header: $Path" }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $Path" }
        $stream.Position = $peOffset + 92
        return $reader.ReadUInt16()
    }
    finally { $reader.Dispose() }
}
function Get-PublishFiles([string] $Directory) {
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Publish output contains a link: $($item.FullName)" }
        if ($item.PSIsContainer) { Get-PublishFiles $item.FullName } else { $item }
    }
}
function Copy-Atomic([string] $Source, [string] $Destination) {
    Assert-NoLinks $Destination
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
    $temporary = $Destination + '.install-' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::Copy($Source, $temporary, $false)
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Flush($true) } finally { $stream.Dispose() }
        if ([IO.File]::Exists($Destination)) { [IO.File]::Replace($temporary, $Destination, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $Destination) }
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

$publishRoot = Get-FullPath $LauncherPublishDirectory
$appPath = Get-LocalAbsolutePath $AppExecutable 'AppExecutable'
$installRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Hypershell'
$binRoot = Join-Path $installRoot 'bin'
$stableExecutable = Join-Path $binRoot 'Hypershell.exe'
$manifestPath = Join-Path $binRoot 'launch-target.json'
$programsShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Hypershell.lnk'
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Hypershell.lnk'
if (-not [IO.Directory]::Exists($publishRoot)) { throw "Launcher publish directory not found: $publishRoot" }
if ($publishRoot.Equals($binRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish the launcher outside the stable installation directory first.' }
if ($appPath.Equals($stableExecutable, [StringComparison]::OrdinalIgnoreCase) -or $appPath.Equals((Join-Path $publishRoot 'Hypershell.exe'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'AppExecutable must identify the actual Hypershell application, not the launcher.'
}
if ([IO.Path]::GetExtension($appPath) -ne '.exe') { throw 'AppExecutable must identify the published application .exe file.' }
if ([IO.File]::Exists((Join-Path ([IO.Path]::GetDirectoryName($appPath)) 'launch-target.json'))) {
    throw 'AppExecutable points into another launcher installation. Select the actual published application.'
}
Assert-NoLinks $publishRoot
Assert-NoLinks $binRoot
Assert-NoLinks $appPath
Assert-NoLinks $programsShortcut
if ($DesktopShortcut) { Assert-NoLinks $desktopShortcutPath }
if ((Get-ExecutableSubsystem (Join-Path $publishRoot 'Hypershell.exe')) -ne 2) { throw 'The launcher must be a Windows GUI (WinExe) publish, not a console executable.' }
Get-ExecutableSubsystem $appPath | Out-Null
$sourceIcon = Join-Path $publishRoot 'Hypershell.ico'
if (-not [IO.File]::Exists($sourceIcon)) { throw 'The launcher publish must include Hypershell.ico.' }
$iconHeader = [IO.File]::ReadAllBytes($sourceIcon)
if ($iconHeader.Length -lt 6 -or $iconHeader[0] -ne 0 -or $iconHeader[1] -ne 0 -or $iconHeader[2] -ne 1 -or $iconHeader[3] -ne 0) { throw 'Hypershell.ico is not a valid icon container.' }
$hyperlibPath = $null
if (-not [string]::IsNullOrWhiteSpace($HyperlibRoot)) {
    $hyperlibPath = Get-LocalAbsolutePath $HyperlibRoot 'HyperlibRoot'
    if (-not [IO.File]::Exists((Join-Path $hyperlibPath 'hyperlib.app.json'))) { throw 'HyperlibRoot must contain hyperlib.app.json.' }
}
$profilePath = $null
if (-not [string]::IsNullOrWhiteSpace($ProfileDirectory)) {
    $profilePath = Get-LocalAbsolutePath $ProfileDirectory 'ProfileDirectory'
    if ([IO.File]::Exists($profilePath)) { throw 'ProfileDirectory must name a directory, not a file.' }
}
$publishFiles = @(Get-PublishFiles $publishRoot)
$manifest = [ordered]@{ schemaVersion = 1; executablePath = $appPath; workingDirectory = [IO.Path]::GetDirectoryName($appPath); hyperlibRoot = $hyperlibPath; profileDirectory = $profilePath }
if (-not $PSCmdlet.ShouldProcess($binRoot, "Install stable Hypershell launcher targeting '$appPath' and create per-user shortcuts")) {
    [pscustomobject]@{ LauncherPath = $stableExecutable; TargetManifest = $manifest; StartMenuShortcut = $programsShortcut; DesktopShortcut = $(if ($DesktopShortcut) { $desktopShortcutPath } else { $null }) }
    return
}

# The shell-link APIs preserve the stable application identity used by the native app.
# https://learn.microsoft.com/windows/win32/properties/props-system-appusermodel-id
$shortcutInterop = @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace Hypershell.LauncherInstall {
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] internal class ShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr data, uint flags);
        void GetIDList(out IntPtr list); void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short key); void SetHotkey(short key); void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] internal struct PropVariant {
        public ushort Type, Reserved1, Reserved2, Reserved3; public IntPtr Pointer, Remainder;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value); void SetValue(ref PropertyKey key, ref PropVariant value); void Commit();
    }
    public static class Shortcut {
        private static PropertyKey AppIdKey = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(int eventId, uint flags, string path, IntPtr unused);
        public static void NotifyChanged(string path, bool created) {
            // Refresh only the installed item; do not restart Explorer or clear its cache.
            SHChangeNotify(created ? 0x00000002 : 0x00002000, 0x00002005, path, IntPtr.Zero);
        }
        public static void Create(string path, string target, string workingDirectory, string icon) {
            object value = new ShellLink();
            try {
                var link = (IShellLinkW)value;
                link.SetPath(target); link.SetArguments(""); link.SetWorkingDirectory(workingDirectory);
                link.SetDescription("Open Hypershell"); link.SetIconLocation(icon, 0); link.SetShowCmd(1);
                var property = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni("Hypershell.Desktop") };
                try { var store = (IPropertyStore)value; store.SetValue(ref AppIdKey, ref property); store.Commit(); }
                finally { PropVariantClear(ref property); }
                ((IPersistFile)value).Save(path, true);
            }
            finally { Marshal.FinalReleaseComObject(value); }
        }
        public static void Verify(string path, string expectedTarget, string expectedIcon) {
            object value = new ShellLink();
            try {
                ((IPersistFile)value).Load(path, 0); var link = (IShellLinkW)value;
                var target = new StringBuilder(32768); var icon = new StringBuilder(32768); int iconIndex;
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4); link.GetIconLocation(icon, icon.Capacity, out iconIndex);
                if (!String.Equals(target.ToString(), expectedTarget, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(icon.ToString(), expectedIcon, StringComparison.OrdinalIgnoreCase) || iconIndex != 0)
                    throw new InvalidOperationException("Shortcut target or icon did not persist correctly.");
                PropVariant property; ((IPropertyStore)value).GetValue(ref AppIdKey, out property);
                try { if (property.Type != 31 || Marshal.PtrToStringUni(property.Pointer) != "Hypershell.Desktop")
                    throw new InvalidOperationException("Shortcut application identity did not persist correctly."); }
                finally { PropVariantClear(ref property); }
            }
            finally { Marshal.FinalReleaseComObject(value); }
        }
    }
}
'@
if (-not ('Hypershell.LauncherInstall.Shortcut' -as [type])) { Add-Type -TypeDefinition $shortcutInterop }

[IO.Directory]::CreateDirectory($installRoot) | Out-Null
$installLock = [IO.File]::Open((Join-Path $installRoot 'launcher-install.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$backupRoot = Join-Path $installRoot ('launcher-backup-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$stageRoot = Join-Path $backupRoot 'staged'
$plan = [Collections.Generic.List[object]]::new()
$installed = [Collections.Generic.List[object]]::new()
try {
    [IO.Directory]::CreateDirectory($stageRoot) | Out-Null
    foreach ($file in $publishFiles) {
        $relative = $file.FullName.Substring($publishRoot.Length + 1)
        # These two files are owned by this installer, never taken from a publish directory.
        if ($relative -in @('Hypershell.cmd', 'launch-target.json')) { continue }
        $staged = Join-Path (Join-Path $stageRoot 'bin') $relative
        $destination = Join-Path $binRoot $relative
        Assert-ChildPath $destination $binRoot
        Assert-NoLinks $destination
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($staged)) | Out-Null
        [IO.File]::Copy($file.FullName, $staged, $false)
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $staged).Hash) { throw "Staged publish file failed verification: $relative" }
        $plan.Add([pscustomobject]@{ Source = $staged; Destination = $destination; Backup = (Join-Path (Join-Path $backupRoot 'previous\bin') $relative) })
    }
    # New artwork gets a new Shell cache key; keep previous icon files because
    # existing pins and recoverable shortcuts may still reference them.
    $stagedIcon = Join-Path $stageRoot 'bin\Hypershell.ico'
    $iconHash = (Get-FileHash -LiteralPath $stagedIcon -Algorithm SHA256).Hash.ToLowerInvariant()
    $iconName = 'Hypershell-' + $iconHash + '.ico'
    $shortcutIcon = Join-Path $binRoot $iconName
    Assert-ChildPath $shortcutIcon $binRoot
    Assert-NoLinks $shortcutIcon
    if ([IO.File]::Exists($shortcutIcon) -and (Get-FileHash -LiteralPath $shortcutIcon -Algorithm SHA256).Hash -ne $iconHash) {
        throw "The versioned icon does not match its content hash: $shortcutIcon"
    }
    $plan.Add([pscustomobject]@{ Source = $stagedIcon; Destination = $shortcutIcon; Backup = (Join-Path (Join-Path $backupRoot 'previous\bin') $iconName) })
    $cmd = "@echo off`r`nsetlocal DisableDelayedExpansion`r`n`"%~dp0Hypershell.exe`" %*`r`nexit /b %errorlevel%`r`n"
    $cmdStage = Join-Path $stageRoot 'Hypershell.cmd'
    [IO.File]::WriteAllText($cmdStage, $cmd, [Text.Encoding]::ASCII)
    $plan.Add([pscustomobject]@{ Source = $cmdStage; Destination = (Join-Path $binRoot 'Hypershell.cmd'); Backup = (Join-Path $backupRoot 'previous\bin\Hypershell.cmd') })
    $shortcutTargets = @([pscustomobject]@{ Name = 'StartMenu'; Path = $programsShortcut })
    if ($DesktopShortcut) { $shortcutTargets += [pscustomobject]@{ Name = 'Desktop'; Path = $desktopShortcutPath } }
    foreach ($shortcut in $shortcutTargets) {
        $staged = Join-Path $stageRoot ($shortcut.Name + '.lnk')
        [Hypershell.LauncherInstall.Shortcut]::Create($staged, $stableExecutable, $binRoot, $shortcutIcon)
        [Hypershell.LauncherInstall.Shortcut]::Verify($staged, $stableExecutable, $shortcutIcon)
        $plan.Add([pscustomobject]@{ Source = $staged; Destination = $shortcut.Path; Backup = (Join-Path $backupRoot ('previous\' + $shortcut.Name + '\Hypershell.lnk')) })
    }
    $manifestStage = Join-Path $stageRoot 'launch-target.json'
    [IO.File]::WriteAllText($manifestStage, ($manifest | ConvertTo-Json -Depth 3), [Text.UTF8Encoding]::new($false))
    $manifestCheck = Get-Content -LiteralPath $manifestStage -Raw | ConvertFrom-Json
    if ($manifestCheck.executablePath -ne $appPath -or $manifestCheck.schemaVersion -ne 1) { throw 'Staged target manifest failed validation.' }
    # Always install the manifest last, after all launcher dependencies and shortcuts are in place.
    $plan.Add([pscustomobject]@{ Source = $manifestStage; Destination = $manifestPath; Backup = (Join-Path $backupRoot 'previous\bin\launch-target.json') })
    foreach ($entry in $plan) {
        Assert-NoLinks $entry.Destination
        if ([IO.Directory]::Exists($entry.Destination)) { throw "An installation file path is occupied by a directory: $($entry.Destination)" }
        if ([IO.File]::Exists($entry.Destination)) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($entry.Backup)) | Out-Null
            [IO.File]::Copy($entry.Destination, $entry.Backup, $false)
        }
    }
    [IO.File]::WriteAllText((Join-Path $backupRoot 'install-plan.json'), ($plan | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    foreach ($entry in $plan) {
        Copy-Atomic $entry.Source $entry.Destination
        $installed.Add($entry)
        if ((Get-FileHash -LiteralPath $entry.Source).Hash -ne (Get-FileHash -LiteralPath $entry.Destination).Hash) { throw "Installed file failed verification: $($entry.Destination)" }
    }
    foreach ($entry in $installed) {
        if ($entry.Destination -eq $stableExecutable -or [IO.Path]::GetExtension($entry.Destination) -in @('.ico', '.lnk')) {
            [Hypershell.LauncherInstall.Shortcut]::NotifyChanged($entry.Destination, -not [IO.File]::Exists($entry.Backup))
        }
    }
    [pscustomobject]@{ LauncherPath = $stableExecutable; TargetPath = $appPath; ManifestPath = $manifestPath; BackupDirectory = $backupRoot; IconPath = $shortcutIcon; StartMenuShortcut = $programsShortcut; DesktopShortcut = $(if ($DesktopShortcut) { $desktopShortcutPath } else { $null }); ProfileDirectory = $profilePath }
}
catch {
    $failure = $_
    for ($index = $installed.Count - 1; $index -ge 0; $index--) {
        $entry = $installed[$index]
        try {
            Assert-NoLinks $entry.Destination
            if ([IO.File]::Exists($entry.Backup)) { Copy-Atomic $entry.Backup $entry.Destination }
            elseif ([IO.File]::Exists($entry.Destination)) { [IO.File]::Delete($entry.Destination) }
        }
        catch { Write-Warning "Restore '$($entry.Destination)' manually from '$backupRoot': $($_.Exception.Message)" }
    }
    throw "Launcher installation failed; completed replacements were rolled back. Recoverable backup: '$backupRoot'. $($failure.Exception.Message)"
}
finally { $installLock.Dispose() }
