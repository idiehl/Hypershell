# Collapsible Cluster package verification

These scripts stage and verify the complete Meridian application without reading
saved projects, library documents, or recovery drafts. The fixed candidate is
`output/collapsible-clusters-20261020/app`; receipts and fixture artifacts are in
`output/collapsible-clusters-20261020/qa`.

Finish the frontend build and source verification before preparing the candidate.
Run all commands from `I:\dev\Meridian` in PowerShell. No native MyNet PID or
browser case count is hardcoded: preparation records the current process
identities, and verification enumerates the planned Chromium cases.

```powershell
$liveBackend = @(Get-CimInstance Win32_Process -Filter "Name='Meridian.Host.exe'" |
    Where-Object ExecutablePath -EQ 'I:\dev\Meridian\output\app\Meridian.Host.exe')
if ($liveBackend.Count -ne 1) { throw 'Inspect the live backend identity first.' }
./scripts/collapsible-cluster-cycle/prepare-package.ps1 -ExpectedProcessId $liveBackend[0].ProcessId
./scripts/collapsible-cluster-cycle/verify-package.ps1 -ExpectedProcessId $liveBackend[0].ProcessId -ReviewPort 5433
./scripts/collapsible-cluster-cycle/install-package.ps1 -ExpectedProcessId $liveBackend[0].ProcessId -ValidateOnly
```

Verification starts only its own candidate process on the review port, with a new
fixture storage directory under `.test-data`, then stops that owned process.
The Chromium editor/viewer checks cover disclosure Clusters, existing edge
Clusters, and Modules. The optional `disclosure-cluster-scene.browser.ts` cases are included
when present. Every enumerated case must pass, with no skips or flaky results.
The plan and results are hashed into the package review receipt.

An initial Firefox attempt could not launch the bundled browser: Windows
SideBySide event 33 identifies its missing `mozglue` assembly. The failed
cross-browser attempt is retained in `qa/attempt-01-chromium-firefox`; the exact
environment diagnosis is in `qa/browser-environment.json`, also hashed into the
final review. No browser runtime or system component was changed. The final
release plan explicitly uses Chromium, matching the native Meridian host; it
does not report unexecuted Firefox cases as passing or skipped planned tests.

`-ValidateOnly` checks the entire reviewed package, source and test hashes,
browser proof, installed files, launcher manifest, and live/native identities.
It does not stop the backend or replace installed application files.

After review is complete and the user has freshly confirmed all open Meridian
windows are saved, the final application command is:

```powershell
$review = Get-Content -LiteralPath 'output/collapsible-clusters-20261020/qa/package-review.json' -Raw | ConvertFrom-Json
./scripts/collapsible-cluster-cycle/install-package.ps1 -ExpectedProcessId $review.liveHost.ProcessId -SavedCheckpointVerified
```

The installer backs up application bytes and restarts only the identity-checked
backend. It leaves native windows open and never copies, restores, deletes, or
enumerates saved data. Rollback restores application bytes only. A changed
native process, launcher, source file, test, package, or installed file invalidates
the receipt and stops installation.

Use `prepare-package.ps1 -RebuildCandidate` only when intentionally replacing a
candidate after source changes; the previous candidate is archived inside the
same output directory and its old review is invalidated.
