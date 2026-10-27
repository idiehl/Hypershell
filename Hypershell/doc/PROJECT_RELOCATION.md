# Project location and relocation

Hypershell is path-independent and can be developed from any local directory.
The current workspace convention keeps related projects as siblings under
`I:\dev`:

```text
I:\dev\Hypershell
I:\dev\Hypershell-atlas-mynet
I:\dev\ping_lab
I:\dev\PingLab_Offline
```

Build and verification commands should be run from the repository root and use
relative project paths. Do not encode the checkout root in source, scripts, or
new documentation.

Atlas MyNet discovers the Ping Lab offline package by walking up from the
current checkout/application directory and looking for a sibling
`PingLab_Offline\app\PingLab.OfflineHost.exe`. Set `PINGLAB_OFFLINE_ROOT` when
the package is stored elsewhere. The variable may point to the
`PingLab_Offline` root, its `app` directory, or the host executable itself.

When a previously saved built-in Ping Lab registration points to a missing
package and a relocated sibling package is available, Hypershell replaces the
stale command path and establishes trust for the new built-in definition. User
registrations with a working executable are not rewritten.

Linked worktrees must be moved or repaired with Git-aware commands. After any
checkout relocation, verify `git worktree list`, build the application, and run
the C# verifier before resuming development.
