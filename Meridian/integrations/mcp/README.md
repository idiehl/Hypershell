# Meridian private MCP companion

The current ngrok-free migration uses OpenAI Secure MCP Tunnel with a private
stdio transport. See [Secure Tunnel setup and verification](../../docs/SECURE-MCP-TUNNEL.md)
for its explicit local consent, scoped project access, encrypted runtime key,
inline PNG previews, and separate Windows recovery task. The older HTTP/OAuth
profiles below remain supported historical transports; the quarantined ngrok
profile is disabled and must not be restarted by this migration.

The installed backend watchdog and new Secure Tunnel task use the fixed
no-console `Meridian.TaskLauncher.exe`; they no longer launch console
PowerShell directly on each minute trigger. The native `setup` password dialog
stores the restricted runtime key with Windows CurrentUser DPAPI and does not
require Windows Terminal. The final post-browser-update serial core run contains 81 tests
(80 pass, one platform-specific Windows skip, no failures); the launcher/lifecycle
suites now pass 74 checks including browser-profile preservation. The dedicated guide also records the earlier parallel
legacy-fixture timeout and unchanged focused/serial reruns. Runtime authentication, healthy/ready state, automatic
runtime and supervisor recovery, and explicit-stop persistence have now been
verified. The new private app **Meridian Authoring Secure** is installed,
connected and functionally verified through ordinary ChatGPT: discovery,
inspection, existing-Work draft edit, identity preservation, validation,
rendered PNG preview/retrieval and test-draft cleanup. Saved live Works were
left unchanged; save/checkpoint/reopen and revision-conflict checks use isolated
storage. The final post-browser-patch regression receipt records the 81-test run.
Cleanup also verified identical saved project bytes and unchanged original
draft/artifact hashes. A full Windows reboot/recovery test was not performed.

Native previews use an explicit, validated `browserExecutablePath` for the
standalone pinned Playwright browser; they do not depend on a coding app's
browser cache. Reconfiguration preserves the optional browser path and existing
nonsecret profile fields. Preview fault audits contain fixed error/stage
metadata, never raw worker exceptions or document/credential contents.

A scoped Meridian authoring companion for ordinary ChatGPT. Version `0.2.0`
implements guarded authoring when `config.authoringEnabled` is true. The
original read-only configuration exposes these three discovery tools:

| Tool | Result |
| --- | --- |
| `meridian_capabilities` | Verifies the local Meridian connection and reports the companion's supported operations. |
| `meridian_list_works` | Paginated names and identifiers from explicitly shared projects. |
| `meridian_authoring_rules` | Versioned accepted and inherited composition principles. |

The enabled HTTP authoring profile adds these operations, all requiring the
separate `meridian.author` OAuth scope. The private stdio profile instead
requires explicit local owner authoring consent and OpenAI tunnel access:

| Tool | Result |
| --- | --- |
| `meridian_inspect` | Paginated saved Work or draft contents, identities, relationships and geometry. |
| `meridian_authoring_schema` | The supported declarative recipe contract and examples. |
| `meridian_create_draft` | A private draft of an existing Work or a new Work in a shared project. |
| `meridian_apply_recipe` | Atomic identity-preserving recipe changes within the draft Work. |
| `meridian_validate` | Graph, geometry, route and current-rule checks for the exact draft version. |
| `meridian_preview` | Actual rendered PNG and private interactive preview. |
| `meridian_preview_image` | A previously rendered PNG identified by its returned artifact ID. |
| `meridian_save_work` | Checkpointed, revision/recovery-guarded save after validation and visual review. |
| `meridian_drafts` | Private drafts available to resume, including after restart. |
| `meridian_discard_draft` | Removes a private staged draft; does not delete a saved Work. |

Existing read-only grants cannot read Work contents or use draft/save tools.
Authoring needs additional explicit owner consent. No tool executes arbitrary
commands, accepts filesystem paths, or proxies arbitrary Meridian API requests.
Public publication, multi-user service accounts, and a separate general-purpose
export tool are not part of this private companion. A successful local/SDK test
does not establish an actual ChatGPT authoring call; record that separately.

The first actual ordinary ChatGPT desktop connection was verified on October 23,
2026: the user's conversation returned the nine C++ Works and the companion audit
recorded a separate successful `meridian_list_works` invocation. Evidence is in
`../../output/mcp-chatgpt-verified-20261023`. This establishes this device's private
read-only route; it does not establish creation/editing tools or multi-user
distribution. The settings screen's "No app tools available yet" text was
inconsistent with the successful call; its cause has not been determined.

The stable v0.2 authoring connection was subsequently confirmed by the user.
Separate successful audit events at `2026-10-23T16:11:00.958Z` through
`16:11:04.112Z` record `meridian_authoring_rules`, the author-scope-protected
`meridian_authoring_schema`, and `meridian_capabilities`. This verifies ordinary
ChatGPT rules/schema/capability access and the authoring permission, distinct
from the operator SDK tests. It does not yet establish an ordinary ChatGPT
create/edit/save conversation. The sanitized receipt is
`../../output/mcp-authoring-20261023/chatgpt-authoring-acceptance.json`.

## Prerequisites

- The local Meridian backend ready at `http://127.0.0.1:5240`. On this device,
  the installed backend task maintains it without requiring an editor window.
- Node.js 24 or later for authoring; PowerShell 7 or later for the private launcher scripts.
- This package's isolated dependencies installed with `npm ci` from
  `integrations/mcp`.
- For the current private stdio transport, the owner's OpenAI Secure MCP Tunnel
  and restricted runtime key; follow the dedicated Secure Tunnel guide above.
- For the historical public HTTP profiles documented below, the ordinary
  ChatGPT account's **Add custom MCP server** / **Server URL** option. Those
  routes do not require public plugin listing or the separate Tunnel option.

## Original Quick Tunnel proof

These commands describe the first temporary connection. Use the stable-profile
instructions below for ongoing authoring. Do not stop the verified prototype
until its replacement has been tested.

From the Meridian source root, use an explicit allowlist of saved project IDs:

```powershell
pwsh -NoProfile -File .\integrations\mcp\scripts\start-private.ps1 `
  -ProjectIds '<saved-project-id>'
```

The launcher verifies the pinned Cloudflare helper's size and SHA-256 before
execution, creates a temporary HTTPS tunnel to the dedicated companion port,
and launches both helpers with hidden windows. It writes private state under
`%LOCALAPPDATA%\AtlasMeridian\McpCompanion`; it does not change Meridian's
projects, recovery files, native windows, backend, or Docker setup.

Open the generated `ChatGPT-Setup.html` locally. It contains the Server URL,
static OAuth Client ID and Client Secret, and private owner pairing code.
These values belong only in ChatGPT's connection settings and the companion's
authorization form. Do not paste them into a conversation, upload the setup
file, or include its contents in screenshots.

1. Open `https://chatgpt.com/plugins` and choose **+ → Add custom MCP server**.
   If the **New Plugin** dialog initially offers archive upload, choose
   **Create MCP App** to open its MCP configuration form.
2. Enter the generated `/mcp` Server URL and choose **OAuth**.
3. Enter the provided Client ID and Client Secret in the manual OAuth fields.
4. On the **Connect Meridian** page, enter the private pairing code and approve
   the read-only connection.
5. Finish installing the personal plugin. In a new ordinary ChatGPT
   conversation, select Meridian and ask it to list your Works and read the
   authoring rules.

Do not switch to **No authentication** if linking fails. Record the nonsensitive
error and diagnose discovery, callback, resource, or static-client settings.
Only an authenticated tool call returning the expected scoped result completes
the ChatGPT connection test.

## Stop or restart

```powershell
pwsh -NoProfile -File .\integrations\mcp\scripts\stop-private.ps1
```

The stop script checks each recorded PID's executable path and process start
time before stopping the tunnel and companion. It does not stop Meridian.
Stopping the original companion revokes its in-memory grants. The private setup files
remain on disk; a subsequent launch generates fresh credentials.

Quick Tunnel addresses change on restart and have no uptime guarantee. A new
address requires updating the ChatGPT connection and its authorization context.
The original proof used one-hour access tokens without durable refresh grants.
The new stable profile uses rotating refresh tokens and the private grant store,
so credentials and valid grants survive ordinary helper restarts.

To apply a companion-only source fix while keeping the same tunnel URL and
credentials, run `pwsh -NoProfile -File .\integrations\mcp\scripts\restart-companion.ps1`
from the repository root. It verifies process identity and restarts only the
Node companion. The original in-memory proof requires a fresh sign-in afterward.
The stable supervisor is the supported lifecycle for the durable profile.
Leave Meridian and the original tunnel running until migration is verified.

## Tests and boundaries

Run the isolated fixture suite from this package with `npm test`. Tests use
temporary/fake Meridian backends and must not write to the user's live library.
The HTTP implementation uses stateless Streamable HTTP with JSON responses;
Cloudflare Quick Tunnels do not support SSE.

The OAuth implementation is a private, single-owner integration using pre-registered
client credentials, S256 PKCE, exact resource binding, browser-bound approval,
and an owner pairing code. It is not a general identity provider or a production
multi-user login system. Durable storage contains token hashes and grant-family
records bound to issuer/resource/client credentials. Refresh tokens rotate;
reusing an earlier refresh token revokes that family. Interrupted or failed
storage commits disable authorization until local recovery. Keep project scopes
explicit when expanding the tools.

`npm run test:browser` additionally uses the repository's existing Playwright
dev dependency and installed Chromium. It reproduces the original browser-only
`Origin: null` rejection and verifies a genuine form POST, secure cookie,
query-free referrer, permitted registered callback navigation, and token
exchange. Browser networking is offline; intercepted fixture traffic goes only
to ephemeral loopback servers, and callback attempts are observed without
contacting ChatGPT. The final serial core run contains 81 tests: 80 pass, with
one platform-specific permission test skipped on Windows and no failures. The
earlier parallel run had one legacy recovery fixture timeout; its unchanged
focused and serial reruns passed. Preserve both run receipts rather than
reporting the parallel failure as an initial clean pass. The separate HTTP browser
suite previously passed two tests. The no-console launcher/lifecycle suites
pass 74 checks, with no live task or real-key writes during testing. The actual
installed-host integration test uses isolated temporary
project storage and does not write to the user's live library.

## Updating composition rules

The default source is `../../docs/MERIDIAN-RULES.txt`. Edit and save that file;
ordinary content updates require no companion rebuild, restart, tool refresh,
or new OAuth consent. Both rules retrieval and freshness checks read it anew.
Ask the authoring conversation to call `meridian_authoring_rules` again.
Use an existing rule ID when revising, or an unused uppercase-prefix ID when
adding a rule, with a standalone header such as `LAYOUT-009 [ACCEPTED]` followed
by its prose. Only ACCEPTED and INHERITED entries are returned. Unmarked notes,
PROPOSED entries, linked files and example images are not automatically included.

The hash covers the entire rulebook. A draft validated before a file change
cannot save until it is validated again against the current version. The author
must reread and apply the new guidance before revalidation; a new hash alone
does not prove understanding. Prose does not automatically generate executable
validation logic. The current tools read the rulebook but cannot rewrite that
policy file; use the local editor or an authorized Codex edit for lasting changes.

The approval HTML must use `Referrer-Policy: strict-origin`. In a native form
navigation, `no-referrer` makes Chromium serialize the POST Origin as `null`.
Keep rejecting null and foreign origins. The approval page's CSP permits its
own form and only the exact validated callback URL; `form-action 'self'` alone
blocks Chromium from following the OAuth callback redirect. Other responses
retain `no-referrer` and the stricter default CSP.

See [Private connection design](../../docs/PRIVATE-MCP-CONNECTION.md) for pinned
helper metadata and transport limits, and [AI compatibility plan](../../docs/CHATGPT-AUTHORING-OPTIONS.md)
for the broader create → preview → revise → guarded save workflow.

## Stable endpoint and restart support

The separate stable profile does not replace or stop the verified Quick Tunnel.
Its state is fixed to `%LOCALAPPDATA%\AtlasMeridian\McpCompanion\Stable` and its
default listener is `127.0.0.1:5242`. Provision a persistent authenticated HTTPS
hostname first, then run `scripts/configure-stable.ps1 -PublicOrigin <origin>`.
The default `external` provider assumes an already managed HTTPS forwarder.
The `ngrok` and `cloudflare` providers can instead supervise an existing account's
agent configuration; supply its executable, independently verified SHA-256,
private config path, and (for Cloudflare) named tunnel UUID. These scripts do not
create provider accounts, change DNS, install a system service, or put provider
tokens on command lines.

`configure-stable.ps1` retains existing OAuth client credentials and owner
pairing code. It assigns a profile-specific durable grant store. Changing an
already configured issuer requires `-ResetAuthorization`, a fresh grant-store
path, and ChatGPT relinking; existing grants are not silently moved between
issuers. Draft operations and grant refresh depend on the companion version
actually running, not merely the presence of launcher files.
The configured stable profile enables the authoring service and keeps its
drafts under that private profile. Existing read-only tokens remain read-only;
the owner must approve the additional authoring scope once before ChatGPT can
create drafts or save changes. Publishing tools does not silently widen an
existing grant.

Run `scripts/start-stable.ps1` to request the registered current-user Windows
Task Scheduler task. Task Scheduler owns the long-lived hidden supervisor; it
is not a child of a transient coding-agent command or app process. It
waits for Meridian, starts the companion, starts the selected tunnel agent, and
restarts only its recorded helpers after failure. It recovers an unresponsive
companion after repeated failed metadata checks. It preserves credentials and
grant files across ordinary restarts. Run `scripts/stop-stable.ps1` to stop this
profile alone; it does not revoke durable grants or stop the older prototype.

`scripts/install-startup.ps1` registers the **Atlas Meridian MCP Stable** task
for the current user's Interactive/Limited token, with no password or elevated
run level. It uses logon and one-minute watchdog triggers, `IgnoreNew` instance
policy, and no execution time limit. It replaces only this integration's old
Startup shortcut. For offline recovery, `-RegisterOnly` creates a disabled task
without contacting the endpoint; `start-stable.ps1` explicitly enables/runs it.
The normal installer additionally requires public JSON metadata and a JSON
OAuth challenge. Register-only mode preserves an existing task's enabled state.
No credentials or private tokens appear in the scheduled action.

The installed task uses an isolated, verified Microsoft-signed PowerShell
runtime under the private Stable folder rather than a coding app's runtime
cache. This avoids depending on the coding app's process lifetime or cache
updates. The native Meridian backend has its own separately owned scheduled
task; MCP lifecycle scripts do not stop or select that backend.

The watchdog honors `stop.request`, so an explicit `stop-stable.ps1` is not
undone by the next trigger. After unexpected supervisor death, the next task run
restarts only verified orphan helpers from its private receipt before starting
the supervisor. It never selects processes by name. Use `status-stable.ps1` to
check exact process identities, fresh heartbeat, owned listener and local
metadata; an old `local-ready` JSON record is not treated as live. Remove task
registration with `scripts/install-startup.ps1 -Remove`, and stop remaining
helpers explicitly. The current user still must be signed in and this computer
online; this does not keep a sleeping or signed-out computer available.

The current connection is not considered stable until the persistent hostname
and OAuth refresh/restart behavior have been verified through ChatGPT. See
[stable transport choices](../../docs/PRIVATE-MCP-CONNECTION.md#stable-transport-choices).

### Historical ngrok setup — currently disabled

The following records the former HTTP transport. Its task remains disabled
after the Defender alert. It is not the setup route for the Secure Tunnel
migration; use the dedicated guide at the top of this document.

The owner selected the free, assigned ngrok Dev Domain. The verified Windows
agent is isolated in the stable profile's `bin` directory. Its October 23
acquisition record is `ngrok-binary.json`: ngrok `3.39.12`, valid Authenticode
signature for ngrok, Inc., SHA-256
`082f0336d62544eb8a8e48fb5d48ea9b4028f90a4e20d22e71f16c542875baca`.
This hash was computed after download from the official linked distribution
and publisher verification; it is not described as a separately published
vendor checksum.

Sign in to the [ngrok dashboard](https://dashboard.ngrok.com/signup). Copy the
assigned hostname from [Domains](https://dashboard.ngrok.com/domains) and the
agent token from [Your Authtoken](https://dashboard.ngrok.com/get-started/your-authtoken)
into the two fields of the private local file
`%LOCALAPPDATA%\AtlasMeridian\McpCompanion\Stable\ngrok-setup.json`.
Do not paste the token into a conversation or shell command.

After saving that local file, run `scripts/complete-ngrok.ps1`. It reads the
values internally, writes a private v3 provider configuration, disables the
agent's local inspection UI and remote management, validates the configuration,
and prepares the stable companion profile and local `ChatGPT-Setup.html`.
It does not start a tunnel or stop the verified prototype. Then use
`install-startup.ps1 -RegisterOnly`, explicitly run `start-stable.ps1`, verify
the public endpoint and OAuth flow, and rerun the normal installer to record
the public checks. Keep the existing connection until migration is verified.

The free plan may show a **Visit** warning page during browser authorization;
continuing suppresses it for that domain for seven days. Programmatic API
requests are unaffected. Current free quotas include 1 GB outbound transfer
and 20,000 HTTP requests per month. A persistent hostname does not remove those
provider quotas. [ngrok free-plan limits](https://ngrok.com/docs/pricing-limits/free-plan-limits).
