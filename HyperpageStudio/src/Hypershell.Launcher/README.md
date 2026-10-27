# Hypershell desktop launcher

This small Windows GUI executable is the stable entry point for terminal commands,
Start menu shortcuts, and taskbar pins. It starts exactly the installed application
listed in `launch-target.json` beside it. It does not scan build directories, build
the application, restart existing sessions, or download updates.

```json
{
  "schemaVersion": 1,
  "executablePath": "I:\\dev\\Hypershell\\output\\release-name\\Hypershell.exe",
  "workingDirectory": "I:\\dev\\Hypershell\\output\\release-name",
  "hyperlibRoot": "I:\\dev\\Hypershell\\Hyperlib",
  "profileDirectory": null
}
```

The executable and working directory must exist. All paths are explicit local
absolute paths. `hyperlibRoot` must exist when supplied; `profileDirectory` can be
created by the application. Omitted or null optional settings preserve the caller's
environment. The launcher rejects missing or malformed configuration, duplicate or
unknown fields, network and URL paths, self-targets (including hard links), and
targets inside another launcher installation. A dialog explains launch failures;
the launcher never silently selects a different build.

Publish to a staging directory separate from the native application's output:

```powershell
dotnet publish src/Hypershell.Launcher/Hypershell.Launcher.csproj -c Release -r win-x64 --self-contained false --artifacts-path I:/dev/.codex-build/hypershell-launcher/artifacts -o I:/dev/.codex-build/hypershell-launcher/publish
```

Install all published files together in the stable per-user launcher directory
(normally `%LOCALAPPDATA%\Hypershell\bin`). The installer validates the chosen
native application and atomically replaces `launch-target.json` after installing
it. Use `tools/Install-HypershellLauncher.ps1`; do not copy a native application's
files over this launcher. `Hypershell.ico` is included beside the executable, and
the same icon is embedded in it.

Point shortcuts and the terminal shim at this stable `Hypershell.exe`. Shortcuts
use AppUserModelID `Hypershell.Desktop`; their icon can be the launcher executable's
resource index 0. The launcher supplies the matching ID and relaunch command,
display name, and icon through `HYPERSHELL_APP_USER_MODEL_ID` and
`HYPERSHELL_RELAUNCH_COMMAND`, `HYPERSHELL_RELAUNCH_DISPLAY_NAME`,
`HYPERSHELL_RELAUNCH_ICON_RESOURCE`. The native app applies those values to its
window. `HYPERSHELL_LAUNCHER_ACTIVE` guards accidental recursive launcher chains;
the native app clears it on startup so its terminals can launch Hypershell normally.

Focused verification (creates and cleans its own temporary fixtures, never starts
the real application or changes installed launchers):

```powershell
dotnet run --project tools/Hypershell.LauncherVerifier/Hypershell.LauncherVerifier.csproj --artifacts-path I:/dev/.codex-build/hypershell-launcher/verify
```
