# Private Meridian MCP connection

## Current installed status

**Transport migration in progress, 23 October 2026:** the ngrok profile below
has been stopped and its scheduled task disabled after the Defender alert.
Its quarantine remains untouched. The replacement private stdio authoring
adapter and separate Windows lifecycle pass local isolated tests; see
[Secure MCP Tunnel](SECURE-MCP-TUNNEL.md) for current setup and acceptance
requirements. The following ngrok installation and recovery details are the
historical pre-incident state, not a claim that it is currently online.

Version `0.2.0` is installed at
`https://prelaunch-juice-scoreless.ngrok-free.dev/mcp`, forwarding to the
dedicated Stable companion on `127.0.0.1:5242`. The profile exposes 13 tools,
including private drafts, existing-Work edits, validation, installed-viewer
previews, and checkpointed saves. Authoring requires `meridian.author` consent.
The connection now uses a current-user, limited-privilege Windows scheduled
task, with logon and one-minute watchdog triggers. This replaces the earlier
Startup shortcut and starts the supervisor outside the coding app's process
tree. The installed local backend has its own limited-user scheduled task.
A private, signature-verified PowerShell runtime removes the launch dependency
on Codex's runtime cache. The public HTTPS SDK proof verifies retained
authorization and the same hostname after forced helper restarts; the later
full-supervisor-loss test also passes. A full Windows reboot has not been tested.

The latest full run passes 65 tests with one POSIX permission test skipped on Windows; both
Chromium approval tests pass. A complete official-MCP-to-installed-host
create/edit/preview/validate/save flow passes against isolated project storage.
Evidence is in `output/mcp-authoring-20261023/installation.json`,
`e2e-receipt.json`, and `stability-receipt.json`. These operator checks are
separate from ordinary ChatGPT use: the original read-only Work-listing call
is verified. The user subsequently confirmed the new stable connection and
authoring tools. Separate successful audit events at `2026-10-23T16:11:00.958Z`
through `16:11:04.112Z` record rules, the author-scope-protected schema, and
capabilities. The receipt is `chatgpt-authoring-acceptance.json` in that evidence
folder. This verifies connection, rules/schema access, and authoring permission;
an actual create/edit/save conversation in ChatGPT remains a separate check.
The original Quick Tunnel and its in-memory companion are no longer running.
Use the stable Meridian Authoring connection rather than the legacy Meridian
read-only entry.

## October 23 runtime-loss repair

The stable heartbeat stopped at `2026-10-23T16:27:29Z`, immediately before new
coding-app processes started at approximately `16:27:34Z`. The companion,
supervisor, tunnel, original probe helpers, and local backend were all absent;
Windows had not rebooted. Local port 5242 refused connections and public ngrok
discovery returned `ERR_NGROK_3200`. No fatal supervisor log or graceful-stop
receipt established the exact termination source. The timing supports an app
restart/process-tree loss, but does not prove which actor terminated the helpers.

The operational defect was independently clear: the old supervisor restarted
its helpers, while the sign-in-only Startup shortcut could not recover a dead
supervisor. A stale `run.json` could continue to say `local-ready`. The repaired
task-based lifecycle has an independent Windows watchdog. `status-stable.ps1`
requires exact live process identities, a fresh heartbeat, an owned listener,
and valid local metadata; a dead supervisor now reports offline even if the
stored receipt says ready. Explicit stop requests suppress watchdog relaunch.

The actual supervisor was deliberately terminated after the repair. Windows
restored the full profile in 38.5 seconds, with the same public endpoint,
unchanged grant store, and unchanged backend PID. Actual Meridian Authoring
rules, protected schema, and capability calls worked again without user
reconsent. All seven saved project revisions remained unchanged. Evidence is
in `output/mcp-lifecycle-repair-20261023/scheduler-recovery.json`. Recovery is
bounded by task scheduling and startup; this does not promise uninterrupted
requests while a process is being recovered or availability while signed out,
asleep, or offline.

## Stable transport choices

Stability audit on October 23 found no pre-existing Cloudflare account config,
ngrok config, or installed Tailscale client in the explicitly checked standard
locations. No credentials were inspected. The existing, verified Quick Tunnel
remains running until a replacement passes its own connection tests.

| Choice | Required owner setup | Suitability |
| --- | --- | --- |
| ngrok account Dev Domain | Sign in and authorize the local ngrok agent; use the account's assigned domain. | Fastest stable prototype without changing domain DNS. The assigned hostname persists between agent runs. |
| Cloudflare named tunnel | Cloudflare account with an active managed domain, named tunnel credentials and a DNS hostname. | Good branded endpoint, for example a dedicated Meridian subdomain if the owner authorizes it. |
| Tailscale Funnel | Tailscale account/client, MagicDNS, HTTPS and Funnel permission. | Persistent tailnet hostname, with platform policy and bandwidth constraints; adds a client not currently installed. |

ngrok assigns each account a stable free Dev Domain; choosing a different
hostname or using a custom domain has separate plan requirements. Cloudflare's
named-tunnel guide requires account/domain authorization and DNS routing.
Tailscale Funnel is limited to the tailnet's `ts.net` names. These are external
account decisions; do not infer account access from the installed Docker toolkit
or create accounts/change DNS on the owner's behalf without the selected setup.

Sources: [ngrok Dev Domains](https://ngrok.com/docs/gateway/domains#dev-domains),
[Cloudflare named tunnel](https://developers.cloudflare.com/tunnel/features/locally-managed-tunnels/create-local-tunnel/),
[Tailscale Funnel](https://tailscale.com/docs/features/tailscale-funnel).

The companion has separate stable-profile launch/configure/stop/status scripts
and a current-user scheduled-task installer. Activation and verification are
recorded above; the existence of these scripts alone would not prove either.
See the
[package instructions](../integrations/mcp/README.md#stable-endpoint-and-restart-support).
The stable profile preserves OAuth client credentials, isolates its grant store,
uses a separate listener, and supervises only its recorded helper processes.
The companion waits for Meridian. On this device, the installed backend is also
maintained by its own task, allowing authoring without an editor window open.
An HTTPS hostname does not make a sleeping local computer available; a hosted
service would be needed for that availability target.

Connection plan checked October 23, 2026. The sections below retain the original
private connection proof and its boundaries. A successful local MCP test does
not establish that ordinary ChatGPT has connected; record that result separately
after a real tool call.

## Original connection proof

The isolated package in `integrations/mcp` now implements the companion,
single-owner OAuth, guarded launcher/stop scripts, and sanitized protocol audit.
Its original 26 focused tests and two Chromium regressions passed, including official SDK
OAuth discovery and calls to
all three tools against isolated fixture backends. The authenticated public
HTTPS operator smoke also passes against the live loopback Meridian host; the
receipt is `output/mcp-server-url-20261022/public-smoke.json`. That test returned
nine C++ Works and 30 active authoring rules. It did not call a model or complete
a ChatGPT account callback.

**Ordinary ChatGPT Work discovery is now verified.** On October 23, the user
completed OAuth in the ordinary ChatGPT desktop application and asked Meridian
to list the C++ Works. ChatGPT returned the expected nine Work names. A distinct
successful `meridian_list_works` call appears in the companion audit at
`2026-10-23T11:19:16.476Z`, after the separately recorded operator smoke test.
The user's connected-account and conversation screenshots plus the sanitized
audit/receipt are retained in `output/mcp-chatgpt-verified-20261023`.
The settings screen simultaneously displayed "No app tools available yet";
that UI text did not prevent the observed tool invocation. Its cause is not
established. Rules retrieval is verified by the operator SDK test, not yet by
a supplied ordinary ChatGPT conversation. Creation/editing/render/save was not
part of that initial proof. Version `0.2.0` now implements scoped inspection,
private drafts, recipe application, validation, rendered previews, checkpointed
guarded saves, and draft resumption. Those tools require the separate
`meridian.author` scope; the verified read-only grant is not silently widened.
Keep actual ChatGPT authoring and stable-endpoint verification evidence distinct
from the initial Work-discovery receipt.

The first real browser approval exposed two HTML response-header bugs. An
inherited `no-referrer` policy caused a native form POST to carry `Origin: null`,
which the correct exact-Origin guard rejected. The authorization HTML now uses
`strict-origin`; null and foreign origins remain rejected. Chromium also needed
the exact registered ChatGPT callback in the approval page's `form-action` CSP
to follow its redirect. Only that validated callback and `self` are permitted.
Other responses retain the stricter defaults. The browser tests reproduce the
old rejection and verify the corrected flow with external networking offline.
Updated public HTTPS evidence is in
`output/mcp-origin-fix-20261023/public-smoke.json`.

The launcher creates user/SYSTEM-only private state in
`%LOCALAPPDATA%\AtlasMeridian\McpCompanion`, rejects arbitrary state directories
and junctions, verifies the pinned helper binary, and starts hidden helper
processes. It leaves the native Meridian windows and backend running. See the
[companion README](../integrations/mcp/README.md) for exact commands.

## Account capability and chosen transport

The user's **New Plugin** dialog first showed archive upload and a
**Create MCP App** button. That button opened the MCP configuration route; a
Meridian entry was created and its OAuth approval page was reached. The user
later observed a **Tunnel** option while creating the authoring entry. Its
Platform permissions, runtime key, and local client have not been configured
or verified. The selected working route remains Server URL. ChatGPT's documented quickstart accepts an HTTPS
`/mcp` address supplied by an external tunnel or deployment.

Use a dedicated Windows-host companion at `http://127.0.0.1:5241`, calling
Meridian's existing loopback host at `http://127.0.0.1:5240`. Expose only the
companion through the tunnel. Do not forward the Meridian editor/host port,
Docker socket, arbitrary filesystem access, or a general reverse proxy.

The first acceptance test is authenticated, read-only Work discovery from an
ordinary ChatGPT conversation. Draft creation and guarded saves are later
capabilities, not implied by a successful discovery test.

Sources: [ChatGPT MCP quickstart](https://developers.openai.com/plugins/build/app-quickstart#connect-your-mcp-server-in-chatgpt),
[Custom MCP connections](https://developers.openai.com/api/docs/guides/custom-mcp-server).

## Temporary HTTPS transport

Cloudflare Quick Tunnel provides an account-free temporary HTTPS hostname for
local development. Its hostname changes on restart, it has no uptime guarantee,
and it supports at most 200 concurrent requests. It does not support SSE.
Its email/PIN access gate requires an interactive browser and cannot protect
non-interactive MCP requests. Use application OAuth instead. A random hostname
is not an authorization mechanism.

For this prototype use Streamable HTTP with JSON responses. SSE is optional in
that transport: respond to POST requests with `application/json`, accept
notifications with HTTP 202, and answer an optional GET with HTTP 405 when no
SSE stream is offered. Verify this against the actual ChatGPT client before
claiming compatibility. Long-running work will need bounded jobs/polling or a
different tunnel that supports streaming.

Sources: [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/),
[MCP Streamable HTTP](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#streamable-http).

## Binary acquisition

Prefer an isolated, version-pinned Windows executable over changing system PATH
or installing a service. Cloudflare's official downloads page links its GitHub
releases and also provides an official Docker image. A Windows executable can
reach the companion's loopback listener directly, avoiding container-to-host
networking changes.

The following release metadata was verified through the official GitHub API;
it is a reproducibility record, not a claim that a binary has been installed:

| Field | Value |
| --- | --- |
| Version | `2026.10.3` |
| Published | `2026-10-21T13:17:53Z` |
| Asset | `cloudflared-windows-amd64.exe` |
| Bytes | `55329336` |
| SHA-256 | `76b2805253d0d6ffacb092a7acfbee99e7af6917835c0c5410e23f3ef5052da2` |
| Download | `https://github.com/cloudflare/cloudflared/releases/download/2026.10.3/cloudflared-windows-amd64.exe` |

Before execution, compare `Get-FileHash -Algorithm SHA256` against the pinned
digest. The GitHub asset's `digest` and the release body's published SHA-256
both matched the value above. Do not use a moving `latest` executable without
recording and verifying its resolved version and digest.

Sources: [Cloudflare downloads](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/downloads/),
[Cloudflare release](https://github.com/cloudflare/cloudflared/releases/tag/2026.10.3),
[Release metadata](https://api.github.com/repos/cloudflare/cloudflared/releases/tags/2026.10.3),
[Official container](https://hub.docker.com/r/cloudflare/cloudflared).

## Startup and authentication

1. Start and test the companion locally. Its health response must reveal no Work
   names or content. Unauthenticated discovery calls must fail closed.
2. Start the verified tunnel executable with the companion as its sole origin:
   `cloudflared tunnel --no-autoupdate --url http://127.0.0.1:5241`.
   A background launcher must hide its window, record its PID and logs, and
   provide a matching stop command. Keep logs free of tokens and pairing codes.
3. Record the assigned HTTPS origin. Configure that exact origin in the
   companion's OAuth issuer, audience/resource, discovery documents and origin
   allowlist before accepting authorization. Fail closed while unconfigured.
4. In ChatGPT, add the HTTPS origin plus `/mcp`, select OAuth, complete the
   companion's local-owner authorization, then install the personal plugin.
   Copy the static Client ID and Client Secret from the private local setup file
   into ChatGPT's manual OAuth fields. Use its pairing code only on the companion
   authorization page. Do not embed user credentials
   or tokens in the URL, documentation, prompts or repository.
5. In a new ordinary ChatGPT conversation, select the plugin and request Work
   discovery. Confirm a successful authenticated tool call in both the ChatGPT
   result and sanitized companion logs. A health check or tool list alone is
   insufficient.

The origin must enforce token audience, expiry, scope and authorization for
every data request. Use PKCE and one-time, short-lived authorization codes.
Authorization consent must be controlled by the local owner rather than an
unauthenticated remote visitor. Keep the first granted scope read-only and
advertise read-only tool annotations. Request bodies and tool results are data,
not shell commands or instructions to evaluate.

Source: [MCP authorization](https://modelcontextprotocol.io/specification/latest/basic/authorization).

## Lifetime and next step

The Windows companion, tunnel and Meridian host must be running for the
connection to work. Restarting a Quick Tunnel produces a new address, requiring
the OAuth/public-origin configuration and ChatGPT connection to be updated.
Stopping the tunnel withdraws external access; revocation must additionally
invalidate the companion's grants and tokens. Do not silently install startup
services or publish the integration publicly.

The original read-only proof used one-hour access tokens without refresh tokens.
The v0.2 stable profile instead persists hash-only grant families bound to its
unchanging issuer/client, with rotating refresh tokens. Ordinary helper restarts
preserve grants and draft state. Defaults are one hour for access, 30 days of
refresh inactivity, and 90 days absolute grant lifetime; exceptional expiry,
revocation, or credential/issuer replacement still requires fresh consent.
A Quick Tunnel URL change would still invalidate its old issuer context, which
is why ongoing use now moves to the owner's assigned ngrok domain.
The sanitized audit records only protocol phase, tool name,
timestamp, and success. The operator smoke's audit events are not evidence of a
ChatGPT conversation; correlate any later real ChatGPT test separately.

The actual ChatGPT discovery test succeeded. The stable ngrok endpoint now
passes public HTTPS OAuth, authoring metadata, refresh rotation, and forced
companion/tunnel recovery tests, including standalone Node.js 24.14.0. The
current-user task lifecycle has been installed and full-supervisor recovery
tested; a full Windows reboot has not been claimed as tested. Evidence is
`output/mcp-authoring-20261023/stability-receipt.json`. Actual ChatGPT rules,
protected schema, and capability calls at the new stable endpoint are recorded
separately in `chatgpt-authoring-acceptance.json`; actual mutation/save calls in
ChatGPT have not yet been observed.
A named Cloudflare Tunnel, another HTTPS
reverse proxy, or a hosted gateway can reuse the same MCP contract. Public
directory submission remains a separate distribution choice.
