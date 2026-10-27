# Stable Hypershell launcher

Publish `src/Hypershell.Launcher` as a Windows GUI application first. Its publish directory must contain `Hypershell.exe`, `Hypershell.ico`, and all framework-dependent companion files. Publish the real Hypershell app separately.

```powershell
.\tools\Install-HypershellLauncher.ps1 `
  -LauncherPublishDirectory C:\build\hypershell-launcher `
  -AppExecutable I:\dev\Hypershell\output\verified-release\Hypershell.exe `
  -HyperlibRoot I:\dev\Hypershell\Hyperlib `
  -DesktopShortcut
```

Add `-WhatIf` to validate inputs and display the selected target without installing. Omit `-DesktopShortcut` to create/update only the per-user Start menu shortcut. `-ProfileDirectory` is optional; omission writes `null`, preserving the app's normal profile selection and settings. No profile files, PowerShell profiles, registry, PATH entries, or pinned shortcuts are changed.

The stable entry point is `%LOCALAPPDATA%\Hypershell\bin\Hypershell.exe`. Both shortcuts and the existing `Hypershell.cmd` command lead there. Shortcuts use the `Hypershell.Desktop` AppUserModelID and a content-addressed `Hypershell-<SHA256>.ico`; Windows can pin them normally. Earlier icon files and the legacy `Hypershell.ico` remain available. The installer never forces a taskbar pin. The command shim forwards its arguments directly to the launcher.

After installation, targeted Windows Shell notifications announce the changed executable, icon, and shortcuts. Using a distinct path for new artwork avoids reusing the cached shortcut icon. The taskbar relaunch icon references resource 32512 in the selected app executable, while the relaunch command and application identity stay stable. Always publish icon changes into a new, immutable application directory. Already running windows consume the new relaunch icon when reopened. An existing pin may need unpinning and pinning again if Windows retains its old shortcut metadata. The installer does not restart Explorer or clear its icon cache.

Updates always receive an explicit application executable. There is no newest-folder search. Manifest paths must be absolute local-drive paths; UNC/device paths, URLs, alternate streams, expansion and wildcard syntax are rejected. The launcher manifest is `launch-target.json` and is replaced atomically only after the staged launcher payload and shortcuts validate. Framework-dependent multi-file publishes are supported; unrelated installed files are retained.

Every installation keeps a timestamped sibling `launcher-backup-*` folder containing `previous/`, `staged/`, and `install-plan.json`. Existing files are backed up before replacement. If installation fails, completed replacements are restored automatically; any failed restoration reports its path and the retained backup. The app's currently running processes are not stopped. Retain the backup until the new command and shortcut have been verified.

After installation, verify `Get-Command Hypershell -All`, the stable shim and manifest, and a newly launched process's executable path. The reported `TargetPath` is the intended real application; the stable launcher exits after starting it.
