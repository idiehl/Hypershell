#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$root = Get-StableStateRoot
if (Test-Path -LiteralPath (Join-Path $root 'stop.request')) { exit 0 }
$config = Get-StableConfig
$runPath = Join-Path $root 'run.json'
$probe = $null
try { $probe = [IO.File]::Open((Join-Path $root 'supervisor.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { exit 0 } # An existing supervisor still owns the lock.
try {
    if (Test-Path -LiteralPath $runPath) {
        $run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json -AsHashtable
        if (Get-OwnedProcess $run.supervisor) { exit 0 }
        # Recover only exact orphan helper identities from this profile. Never
        # select processes by name or touch the native Meridian backend.
        $expectedTunnel = if ($config.launcher.ContainsKey('tunnelPath')) { $config.launcher.tunnelPath } else { $null }
        foreach ($entry in @(@{ identity = $run.tunnel; expected = $expectedTunnel }, @{ identity = $run.companion; expected = $config.launcher.nodePath })) {
            if (-not $entry.identity) { continue }
            if ($entry.identity.path -ne $entry.expected) { throw 'An orphan helper does not match the configured profile executable.' }
            $owned = Get-OwnedProcess $entry.identity
            if ($owned) { Stop-Process -Id $owned.Id -ErrorAction Stop; $owned.WaitForExit(5000) | Out-Null }
        }
    }
} finally { $probe.Dispose() }
# Task Scheduler owns this long-lived process, outside a transient command job.
& (Join-Path $PSScriptRoot 'supervise-stable.ps1')
