# Meridian Authoring through OpenAI Secure MCP Tunnel

Migration work cycle: 23 October 2026. This is a private connection for the
owner's existing ChatGPT account, not a public plugin-directory submission.

## Connection boundary

The official OpenAI `tunnel-client` polls OpenAI over outbound HTTPS and owns a
private stdio child running `integrations/mcp/src/stdio.mjs`. That child uses
the installed numeric-loopback Meridian backend. Neither the editor API nor
an unauthenticated HTTP MCP port is exposed. Ngrok is not required or started.
Managed Cloudflare forwarding is explicitly disabled for this profile.

The private configuration requires `transport: openai-secure-tunnel-stdio`,
`authoringEnabled: true`, `authoringConsent: true`, and an explicit project
allowlist. Keep that allowlist at the previously approved scope. Tool calls
cannot change these settings. All 13 tools reuse the existing authoring
service, including existing-Work edits, identity preservation, draft versions,
current rules, validation, preview review, checkpoint saves, revision conflicts,
and unsaved-edit recovery guards. Legacy public HTTP profiles retain OAuth;
their grants are not transferred into the stdio process.

The OpenAI tunnel and its organization/workspace permissions authenticate the
private connection. The stdio tool descriptors therefore declare `noauth`
rather than advertising unreachable public OAuth pages. This is not a public
anonymous endpoint. The local runtime key is restricted to Tunnels Read and
Use, with no model-generation permissions. The MCP child removes inherited
OpenAI control-plane keys before starting authoring workers.

## Previews and existing drafts

Rendered PNG previews travel inline in MCP responses, with a 4 MiB PNG limit.
`meridian_preview_image` retrieves the same private artifact by ID. The private
transport does not return an internet-accessible interactive HTML link; local
Meridian still provides interactive editing. Keep the shared viewer renderer
and the preview-before-save checks intact.

The native tunnel runtime uses a standalone pinned Playwright browser instead
of relying on a coding application's per-user browser cache. On this device,
Playwright 1.63 headless Chromium 153, revision 1243, is installed under
`I:/dev/Meridian/output/mcp-browser-runtime`. The private optional
`browserExecutablePath` points to its exact executable. The configured path must
be an absolute local path, exist as a regular file and traverse no symlinks or
junctions. UNC/device paths, URLs and control characters are rejected before
filesystem access; stdio and lifecycle validation reject unsafe values. Reconfiguration preserves
this browser selection and existing nonsecret profile metadata unless an
explicit replacement is supplied. No global browser environment setting or
Defender exclusion is needed.

The first native preview attempt exposed a browser-cache visibility problem;
it did not mean draft edits or the MCP connection had failed. With the explicit
browser path, ordinary ChatGPT successfully called both `meridian_preview` and
`meridian_preview_image`, received a PNG with no browser errors, and inspected
the actual rendered branch. `browser-runtime-verification.json`,
`browser-config-receipt.json` and `chatgpt-preview-success.png` record the browser
selection and successful native preview.

Preview-failure audit records use fixed diagnostic codes and a bounded capture
stage vocabulary. They do not record raw exception text, local executable paths,
tool arguments, results, document contents or credentials. Detailed rendering
faults must be investigated locally rather than forwarding arbitrary worker
stderr into an MCP response or audit log.

Existing private drafts can be copied from `Stable/authoring` into
`SecureTunnel/authoring` while both companion profiles are stopped. Preserve
the original and copy hashes. Draft metadata and relative artifact paths are
portable; do not copy old OAuth grants, ngrok credentials, or tunnel configs.
An old base revision still conflicts with a newer saved Work and must not be
silently rebased or saved.

## Windows lifecycle and secrets

Private state is under
`%LOCALAPPDATA%/AtlasMeridian/McpCompanion/SecureTunnel`, with owner/SYSTEM-only
directory permissions. The runtime key is stored as Windows DPAPI CurrentUser
ciphertext and decrypted only into the official client's subprocess environment.
Do not place literal keys in YAML, JSON, command arguments, screenshots, chat,
source, or logs. The generated native client profile stores an environment
reference. The authoring protocol's stdout contains only MCP messages.

Use the independent Microsoft-signed PowerShell runtime copied to
`SecureTunnel/bin/PowerShell7/pwsh.exe` for the lifecycle commands. It does not
depend on the coding application's runtime cache. The official OpenAI client
is hash-pinned before each invocation. A matching public release digest proves
archive provenance, not a malware-analysis verdict or an Authenticode signature.

Scheduled tasks now start the fixed Windows GUI executable
`SecureTunnel/bin/TaskLauncher/Meridian.TaskLauncher.exe`, rather than starting
a console PowerShell executable directly. This is a .NET Windows GUI subsystem
single-file application; its managed assembly, runtime configuration and
dependency metadata are embedded in the hashed executable. It requires the
installed Microsoft Windows Desktop .NET 8 runtime. `build-manifest.json`
records the executable hash, source root and exact PowerShell locations/hash.
Installation checks reject a changed binary, unexpected loose managed sidecars,
reparse paths and a console-subsystem executable.

Only fixed `backend`, `secure-tunnel`, and interactive `setup` modes exist.
The two task modes accept no arbitrary command or path, pin the matching
PowerShell binary, and execute their fixed source scripts with
`UseShellExecute=false` and `CreateNoWindow=true`. The GUI launcher waits for the
child and returns its exit code. Windows Terminal is not involved. The previous
backend watchdog started a console process every minute even when the backend
was healthy; its health-check early exit made that process disappear quickly.
The guarded backend-task upgrade keeps the existing backend process alive and
replaces only the exact owned scheduled action.

### Entering the runtime key without Terminal

The Tunnel ID identifies the tunnel; it is not a secret API key. Create a
restricted runtime API key with Tunnels Read and Use permissions in the owner's
existing OpenAI account. The owner creates and copies that new secret locally.
Do not paste it into this chat or any terminal command.

For an initialized private SecureTunnel profile, open the installed launcher's
`setup` mode. It displays a native Windows password field instead of opening
PowerShell or Windows Terminal:

```powershell
Start-Process -FilePath (Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion/SecureTunnel/bin/TaskLauncher/Meridian.TaskLauncher.exe') -ArgumentList 'setup'
```

The dialog verifies the private state marker, owner/SYSTEM-only directory ACL
and regular paths before storing CurrentUser DPAPI ciphertext in the fixed
`runtime-key.dpapi` file. It rejects Tunnel IDs, tracking IDs, short values,
whitespace and quoted keys. A key's shape does not prove that OpenAI accepts
its permissions or account binding; a real authenticated check is still
required. The GUI's DPAPI format is tested against PowerShell's actual
`ConvertTo-SecureString`, using a disposable fake sentinel rather than a real
credential. The older console configuration entry point also validates the
key format before replacing existing ciphertext.

After the dialog reports that the key is stored, the operator can run
`configure-secure-tunnel.ps1 -KeepExistingKey` with the already approved
nonsecret profile settings. Key entry and configuration do not start the
tunnel. The one-off `Complete-Setup.ps1` helper has been retired as a console
credential prompt and replaced with a fixed GUI-launch shim; its previous
version is retained privately for recovery. The current user-facing key-entry
path is the native dialog.

The **Atlas Meridian MCP Secure Tunnel** task uses the current user's limited
interactive token, logon plus one-minute watchdog triggers, no execution time
limit, and exact executable/action/working-directory ownership checks. It uses
official `runtimes connect/status/stop` operations. The official Windows native
runtime manages the child identity but is not a Windows auto-start service;
the scheduled task supplies startup and supervisor-loss recovery.

Source scripts are in `integrations/mcp/scripts`:

- `configure-secure-tunnel.ps1`: explicit local consent, shared projects, binary
  pin and encrypted runtime-key storage. `-KeepExistingKey` retains a key entered
  in the native setup dialog. Configuration does not start the tunnel.
- `build-task-launcher.ps1` and `test-task-launcher.ps1`: isolated single-file
  Windows GUI launcher build and checks. Build/test do not install tasks.
- `task-launcher-common.ps1`: exact canonical installation, binary pin, PE
  subsystem and backend task ownership checks.
- `install-secure-tunnel-startup.ps1`: registers the separate task, disabled by
  default. It does not change the old ngrok task.
- `start-secure-tunnel.ps1`: clears this profile's stop marker and requests the
  owned scheduled task.
- `status-secure-tunnel.ps1`: backend, task, native process, health and readiness.
  Local readiness alone does not establish a successful ChatGPT tool call.
- `stop-secure-tunnel.ps1`: writes a persistent stop marker, disables this
  watchdog, and stops its named native runtime. It does not stop Meridian.
- `test-secure-tunnel.ps1`: isolated lifecycle/security checks, no live writes.

Keep Meridian's own backend task separate. A sleeping, signed-out, offline, or
powered-off computer cannot serve its local Works. Recovery is bounded by
Windows scheduling and network availability; do not promise zero downtime.

## Verification and current migration evidence

Local tests passed against the actual installed host with isolated storage:
all 13 tools, original node identity, unrelated Works preserved, new and existing
Work saves, checkpoint/reopen, inline viewer PNG, artifact retrieval, persisted
draft after process restart, idempotent save, and concurrent revision rejection.
After the browser-path, profile-preservation and metadata-only diagnostic
updates, the final core suite contains 81 tests: 80 passed, one
platform-specific Windows skip and no failures. It completed serially in
61.46 seconds. `core-tests-post-browser-final.json` and its log record this
final source verification using `node --test --test-concurrency=1 tests/*.test.mjs`.
Earlier 75-test and 76-test baselines remain in `stdio-hardening-receipt.json`
and `core-tests-final.json`. The earlier parallel run had a legacy recovery
fixture timeout at its existing 15-second wait under concurrent render load;
that unchanged test passed alone in 11.916 seconds and in the full serial run.
Keep `core-tests-parallel.json` and `legacy-stability-focused.json` as historical
evidence; the passing reruns do not erase the initial parallel failure.
The no-console and lifecycle checks pass
74 checks: 26 launcher/runtime/key-format checks, six backend ownership/guarded
upgrade checks and 42 Secure Tunnel lifecycle/key/browser-profile checks. The launcher tests
perform no live task changes or real-key writes.

Evidence lives in `output/mcp-secure-tunnel-20261023`. The old unsaved authoring
draft was copied without changing the original; `draft-migration.json` records
the hash checks. `client-verification.json` records the official release/archive
digest, executable digest, and actual signature status.
`task-launcher-build.txt`, `task-launcher-tests.txt` and
`task-launcher-secure-tests.json` record the original GUI launcher build/checks;
the later `secure-lifecycle-final-tests.json` records 42 lifecycle checks and
adds browser-path/profile-preservation coverage to the combined 74-check total.
The two-file release is under `task-launcher/`. The deployed launcher hash is
`0D2A6EC9553C8ACDF931A49E9D5E9BB4424EF32448A408C8466120D93F7C8433`.
`no-console-install-receipt.json` records the verified backend task upgrade with
the existing backend process unchanged. That check establishes backend task
continuity, not a live OpenAI tunnel.

The restricted runtime key has now authenticated successfully against OpenAI;
`runtime-key-auth-receipt.json` records the successful metadata check without
recording the secret. The official runtime has reported healthy and ready.
Live recovery checks are recorded separately:

- `runtime-recovery-receipt.json`: stopping the owned runtime was followed by
  automatic recovery to a new runtime process, healthy and ready.
- `supervisor-recovery-receipt.json`: stopping the owned PowerShell supervisor
  was followed by watchdog recovery to a new supervisor while the runtime
  remained singular and ready.
- `explicit-stop-receipt.json`: an explicit stop stayed stopped with the task
  disabled and stop marker present for 86 seconds; the local backend remained
  running. An explicit start subsequently restored readiness without entering
  the key again.

The new private ChatGPT app **Meridian Authoring Secure** is installed,
connected and functionally verified through an ordinary ChatGPT conversation.
It read capabilities, current rules and schema, listed the nine C++ Works,
inspected saved nodes, and created a private draft of an existing C++ Work.
It changed `Choices` to `Value Choices` in that draft while preserving the node
identity and unrelated fields, validated with zero errors and 13 pre-existing
warnings, and discarded only the test draft. The saved project stayed at
revision 69 and the saved node remained unchanged. Native PNG rendering and
artifact retrieval were also verified after the browser-path correction above.
`production-preview-cleanup.json` additionally confirms the saved project bytes
and original draft/artifact hashes stayed unchanged after test cleanup.
The former ngrok app is named
**Meridian Authoring (Retired ngrok)** and remains a historical entry; it is not
the current connection.

Actual ChatGPT draft editing, validation, preview and draft cleanup are distinct
from the isolated save tests. Checkpointed new/existing Work saves, reopen,
idempotency and revision conflicts were verified against isolated storage;
the acceptance conversation deliberately did not save a test edit into the
owner's live Work. Do not claim an ordinary ChatGPT live-library save occurred.
The final post-browser-patch regression receipt above is separate from the
functional connection evidence; both now pass within their stated scope.
`final-acceptance-receipt.json` collects the final acceptance boundaries and
`chatgpt-preview-success.png` records the ordinary ChatGPT preview result.
A full Windows reboot/recovery test has not been performed; the verified
recovery checks cover the runtime and supervisor processes and explicit stop/start.
No paid plan or payment details were changed.
OpenAI's published tunnel guide/pricing page did
not establish a separate tunnel rate; do not describe that uncertainty as a
guaranteed free service.

Official setup reference:
https://developers.openai.com/api/docs/guides/secure-mcp-tunnels
