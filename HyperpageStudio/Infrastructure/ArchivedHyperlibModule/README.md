# Hyperlib PowerShell module prototype

Status: parked for the future standalone Hyperlib application.

This directory preserves the PowerShell module work removed from Hypershell on
2026-07-26. It is intentionally outside the Hypershell project, so these files
are not compiled, loaded, scanned, or shown by the current application.

## Preserved components

- `PowerShellModuleCatalogService.cs` — discovers locally available modules,
  caches command metadata, and resolves parameters lazily.
- `PowerShellModuleReference.cs` — identifies the exact module name, version,
  and manifest path required by a composed command.
- `PowerShellModuleSessionLoader.cs` — checks a persistent shell session and
  conditionally imports the exact catalogued module path before execution.
- `PowerShellModulePanel.xaml` — the removed module picker and readiness-status
  UI, retained as a visual prototype.

## Prototype behavior

The module picker distinguished installation from session state:

1. `Get-Module -ListAvailable` discovered installed modules.
2. `Get-Module` checked whether the exact module path was loaded in a tab.
3. `Import-Module` loaded the exact manifest only when a command required it.
4. Session status was reported as installed, checking, ready, loaded, or failed.

When Hyperlib development begins, these files should move into a dedicated
project with its own models and tests rather than being reconnected directly to
Hypershell.
