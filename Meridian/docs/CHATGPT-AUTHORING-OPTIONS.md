# Meridian authoring from ordinary ChatGPT

Status audit and implementation options, checked 23 October 2026. This document
describes the authoring foundation, the private MCP authoring companion, and
the remaining distribution and client acceptance work. It does not claim that a Meridian plugin
has been published or that every ChatGPT account has the same capabilities.

## Intended experience

A person uses the ChatGPT desktop app, installs Meridian, and asks for a Work.
ChatGPT reads the current composition principles, prepares a valid graph, sees
the real rendered result, improves it, and creates or updates the person's
Meridian library entry. They can continue editing it in Meridian. The person
does not need Codex, a source checkout, or knowledge of command-line tools.

The principles are an authoring contract and review criteria. Saving them does
not train or fine-tune a model. Reliable results require those principles to be
available to each authoring session, supported by examples, deterministic
validation, a preview, and a revision-aware save operation.

## What exists now

**23 October transport update:** ngrok is stopped and its startup task is
disabled following the Defender alert. A private OpenAI Secure MCP Tunnel
adapter now reuses the complete authoring toolset over stdio, with inline
previews and a separate encrypted-key/startup profile. Local SDK and installed
host fixture tests pass. A fixed Windows GUI launcher now runs the backend and
Secure Tunnel task actions without activating Windows Terminal; its native
password dialog stores the runtime key with CurrentUser DPAPI. The final post-browser-update serial
core run contains 81 tests (80 passed, one platform-specific Windows skip,
no failures), and 74 launcher/lifecycle/browser-profile checks pass. The dedicated guide records
the earlier parallel legacy-fixture timeout and unchanged passing reruns. Runtime
authentication, healthy/ready state, automatic runtime/supervisor recovery and
explicit-stop persistence have now been verified. **Meridian Authoring Secure**
is installed, connected and functionally verified in ordinary ChatGPT:
discovery, inspection, an identity-preserving draft edit of an existing Work,
validation, rendered PNG preview/retrieval and test-draft cleanup. The saved
Work stayed unchanged. Save/checkpoint/reopen and conflict behavior were tested
against isolated storage, not by saving a test change into the live library.
Native rendering uses a pinned standalone browser through the validated,
preserved optional `browserExecutablePath`; faults expose fixed error/stage
metadata rather than raw worker output. The final post-browser-patch regression
receipt records the 81-test passing run. Detailed evidence is tracked in
[SECURE-MCP-TUNNEL.md](SECURE-MCP-TUNNEL.md).
Cleanup confirms unchanged live project bytes and original draft/artifact hashes.
A full Windows reboot/recovery test has not been performed.
The ngrok rows and incident receipts below describe the prior installation.

The following was checked against maintained source, the personal skill entry,
and the running host at `http://127.0.0.1:5240`:

| Component | Implemented boundary |
| --- | --- |
| [Rulebook](MERIDIAN-RULES.txt), [standards](WORK-AUTHORING-STANDARDS.md), examples and `AGENTS.md` | Persistent local authoring guidance. Accepted user principles guide future authors; proposals remain separate. These files do not distribute themselves to other users. |
| [Meridian Authoring skill](../.agents/skills/meridian-authoring/SKILL.md) | Reusable inspect, compose, render, review and revise workflow. The personal discovery entry at `C:/Users/ihigg/.agents/skills/meridian-authoring` is a junction to this project-owned skill. This is a local installation, not a public plugin. |
| [Declarative authoring API](../src/core/authoring.ts) | `applyRecipe`, `inspectProject`, `inventoryProject`; typed data, identity bindings, explicit changes/removals, routing utilities, and complete graph validation. Recipes are data rather than arbitrary code. |
| [Authoring CLI](../scripts/author.mjs) | `inventory`, `inspect`, `compose`, `capture`, `publish`, `install`, and guarded `apply`. Compose writes a candidate and aliases. Capture uses the actual published viewer. Install makes an independent project; apply updates a known project at the expected revision. |
| Editor Composition tools | Load or paste a recipe, preview it in the shared viewer, inspect warnings, and apply it as one undoable draft edit. This already provides a manual handoff from ChatGPT-generated recipe JSON. |
| Local host | Project, library, recovery, checkpoint and import/export endpoints. The running health response advertises both `authoring-save-guard-v1` and `authoring-recovery-guard-v1`. The authoring endpoint checks saved revision and the reviewed recovery token, with checkpoints. |
| Portable output | Interactive self-contained HTML publication and editable Meridian project interchange. Existing viewer and publication components can supply preview/export building blocks. |
| [Private MCP companion](../integrations/mcp/README.md) | Version 0.2 exposes 13 tools for discovery, accepted rules, scoped inspection, recipes, persistent drafts, validation, rendered PNG/artifact retrieval, checkpointed save and draft cleanup. Existing Work edits retain IDs and unrelated content. HTTP authoring requires `meridian.author` OAuth consent; private stdio requires explicit local owner consent and tunnel access. Ordinary ChatGPT verified existing-Work draft editing, validation, preview and cleanup through the new Secure Tunnel. Save/checkpoint tests use isolated storage; no test edit was saved to the live Work. |
| Private Secure Tunnel connection | Restricted encrypted runtime key; owner/workspace tunnel access; no public MCP listener; fixed no-console task actions. Runtime and supervisor recovery, explicit-stop persistence and subsequent restart without key entry pass. The pinned standalone preview browser has an explicit, validated configuration path. The computer must remain online and signed in. |
| Prior stable HTTP connection, currently disabled | The owner's assigned ngrok HTTPS domain, private persistent OAuth/rotating refresh grants, and a current-user Windows scheduled task with logon/watchdog triggers. Its earlier supervisor-loss and protected-call checks passed; those results do not establish the replacement tunnel. A separate no-console task maintains the installed local backend. This computer must remain online and signed in. |

The implementation now has a private MCP authoring service. There is still
**no public marketplace package/directory listing or hosted multi-user authoring
service**. The main application has no OpenAI SDK dependency
or model-backed authoring agent; the companion does not call a model or require
an OpenAI API key. An ordinary ChatGPT conversation has now invoked
`meridian_list_works` successfully. That proof is recorded separately from the
operator SDK test. Version 0.2 also passes a full official-MCP-to-installed-host
test in isolated storage: edit an existing Work, preserve identities and other
Works, validate, render the installed viewer, checkpoint-save, reopen, create a
new Work, recover an uncertain save, and reject conflicts. That establishes the
implemented workflow and actual host contract, not an ordinary ChatGPT authoring
conversation or clean installation for another user. Evidence is in
`output/mcp-authoring-20261023/e2e-receipt.json` and `stability-receipt.json`.

The user subsequently confirmed connection and authoring-tool availability at
the stable endpoint. Distinct successful audit events at
`2026-10-23T16:11:00.958Z` through `16:11:04.112Z` verify rules, the protected
authoring schema, and capabilities. This acceptance and its attribution limits
are recorded in `chatgpt-authoring-acceptance.json` in the same evidence folder.
No ordinary ChatGPT draft mutation or save has yet been observed.

The stable profile runs at
`https://prelaunch-juice-scoreless.ngrok-free.dev/mcp`. Client credentials and
grant digests remain private under the dedicated local Stable folder. The
one-time move from the temporary issuer and read-only permission requires a
new connection/consent; future ordinary helper restarts retain the issuer and
authorization. Access tokens last one hour and rotate through refresh grants;
defaults require reauthorization after 30 days of inactivity or 90 days absolute.

An October 23 runtime-loss incident exposed a gap in the original sign-in-only
Startup shortcut: it could not recover a terminated supervisor. The task-based
repair and truthful live-status checks are documented in
[the connection incident record](PRIVATE-MCP-CONNECTION.md#october-23-runtime-loss-repair).
Actual full-profile recovery took 38.5 seconds with unchanged credentials,
endpoint, backend identity, and saved Work revisions.

There is also an authoring-quality gap independent of ChatGPT connectivity.
The declarative layout choices currently exposed are `row`, `column`, `grid`
and `radial`. They are useful placement primitives, not a complete graph/tree
layout engine. Current hierarchy compositions still need additional planning
or scripts to account for whole expanded branches. A reusable subtree-aware
layout and route validator should become part of the authoring contract rather
than relying on a different ad hoc script for every Work.

The local host deliberately accepts loopback requests and restricts cross-site
writes. The CLI also limits its host option to a local HTTP origin. This is a
useful local boundary, not a public integration API. A hosted ChatGPT tool cannot
call a user's `127.0.0.1:5240` as if it were a public Meridian service. A local
executor or an explicitly paired bridge is needed to reach that user's data.

Source evidence: `src/core/authoring.ts`, `scripts/author.mjs`,
`src/editor.ts` (Composition tools), `src/host/Program.cs`,
`docs/AUTHORING.md`, `docs/HOST.md`, and `package.json`. The health check confirms
host capability names; it does not prove that an unbuilt source feature is
installed in the active renderer.

## Current ChatGPT integration options

### Execution and distribution are separate decisions

"Local" and "hosted" describe where the MCP server executes. "Personal",
"workspace", and "public directory" describe who can install/use the integration.
These are independent axes. A locally executing server can be packaged for the
public directory, subject to its additional approval requirements. A hosted HTTPS
server can remain a private custom MCP connection without a public listing.
The Plugins page also supports personal custom MCP connections; appearing there
does not by itself mean a plugin was submitted or published publicly.

For a private cloud-to-local connection, an eligible ChatGPT account can use a
Secure MCP Tunnel to a locally running HTTP or stdio server. This introduces
tunnel/account permissions and a runtime key, not public-directory submission.
A personal local plugin can instead be tested through a local marketplace in a
supported local executor. Demonstrate the requested ordinary ChatGPT surface
before treating a Codex or other MCP client's success as sufficient evidence.

Sources: [Custom MCP connection](https://developers.openai.com/api/docs/guides/custom-mcp-server),
[Plugin packaging and private installation](https://developers.openai.com/plugins/build/plugins),
[Secure MCP Tunnel](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels).

### Initial device audit and private-first recommendation

This section records the original read-only proof. The current v0.2 stable
authoring installation is described above and in
[the connection status](PRIVATE-MCP-CONNECTION.md#current-installed-status).

The user's priority is a working, precise, consistent, fast toolset. Public
listing/review should not be a dependency of the first working integration.
Docker is available as an execution option, not a prerequisite for MCP.

The user's **New Plugin** dialog first offered archive upload and
**Create MCP App**. The latter opened MCP configuration, followed by OAuth.
No **Tunnel** option was observed. The original successful private connection used a temporary
Cloudflare HTTPS tunnel to the dedicated Windows companion on port 5241.
Meridian's editor/API port 5240 remains loopback-only and is not forwarded.
Authentication uses a pre-registered confidential OAuth client, S256 PKCE,
resource-bound access tokens, and local-owner approval with a private pairing
code. Credentials and setup HTML live only in the current user's restricted
`%LOCALAPPDATA%\AtlasMeridian\McpCompanion` directory. They must not be pasted
into chat, source, or artifacts. No Docker client or server configuration was
changed. See [private connection design](PRIVATE-MCP-CONNECTION.md).

The original focused suite passed 26 tests, with two additional Chromium browser
regressions covering native form approval and the registered callback. The
approval HTML privacy/CSP headers were corrected after the actual sign-in
attempt exposed a browser-only Origin rejection. An authenticated public HTTPS
smoke test after the fix passed at the same endpoint. The subsequent ordinary
ChatGPT conversation returned nine C++ Work names, with a separate successful
`meridian_list_works` audit event at `2026-10-23T11:19:16.476Z`. Evidence is in
`output/mcp-chatgpt-verified-20261023`. A public-HTTPS operator smoke test initialized
the official SDK client and invoked all three tools against the live local
Meridian host: nine C++ Works and 30 active composition rules were returned.
Anonymous access failed with OAuth discovery; editor APIs and private file paths
returned 404. Sanitized evidence is in
`output/mcp-server-url-20261022/public-smoke.json`. The private connection and
ordinary ChatGPT Work-discovery acceptance gate are **verified**. The supplied
ChatGPT screenshot did not exercise rules retrieval or authoring operations.

A read-only audit on October 22, 2026 verified Docker 29.7.2, a running Linux
daemon in the `desktop-linux` context, MCP Toolkit v0.43.3, and a default profile
with filesystem, n8n, node-code-sandbox, Playwright and Puppeteer servers.
No containers were running at audit time. The Toolkit's auto-connect help did
not list ChatGPT. This does not establish whether a separate ChatGPT connector
exists; at that earlier Docker audit, ordinary ChatGPT connectivity had not yet
been demonstrated. The later proof uses the Windows companion, not Docker.
Sanitized evidence is in `output/mcp-feasibility-20261022/docker-audit.json`.

Meridian's current host listens on `127.0.0.1:5240` and explicitly checks both
loopback remote IP and Host hostname. A Linux Docker server cannot be assumed
to reach it directly via `host.docker.internal`. Keep the existing boundary.
The simplest first local companion can execute on Windows and call the guarded
loopback API. Containerized authoring/render/validation workers can be added
behind a narrowly scoped authenticated companion bridge when useful. Running
the MCP adapter in Docker remains possible, but the bridge adds engineering
work and should not delay the first end-to-end result merely for packaging.

Prove the connection with a harmless Work-listing call in ordinary ChatGPT,
then complete one create -> preview -> revise -> guarded save -> reopen flow
in a disposable project. Only then broaden the authoring operations and validate
larger graphs. Reuse the same authoring service across stdio, HTTP/tunnel and
later hosted distribution; keep transport decisions out of graph composition.

No Docker server was launched, no client was connected, and no Docker profile,
credential, or service was changed during this audit.

The user subsequently confirmed that Docker does not show ChatGPT/OpenAI as a
client or offer a corresponding connect option. A previously seen verification
is not sufficient evidence of ordinary ChatGPT connectivity. The next connection
checkpoint is the user's normal ChatGPT account: verify **Add custom MCP server**
and whether **Tunnel** is available. Configure from that side using a supported
private connection. Do not assume Docker client auto-configuration or a working
Codex MCP connection establishes the requested ChatGPT path.

The first end-to-end proof should be an ordinary ChatGPT call to a read-only
Meridian capability/Work-listing tool. A healthy Docker container, successful MCP
initialization, or a tools/list result in a different client is not that proof.

OpenAI's current product documentation describes a shared plugin directory for
ChatGPT and Codex. Plugins can combine skills and MCP tools, and supported
plugins can be used from ordinary ChatGPT Chat or Work. A plugin supplies a
workflow and capabilities without requiring the user to choose Codex. Tools,
execution location and permissions can still differ between surfaces and
accounts. [Plugins](https://learn.chatgpt.com/docs/plugins),
[Build plugins](https://learn.chatgpt.com/docs/build-plugins).

| Route | Person's experience | Fit and remaining work |
| --- | --- | --- |
| **Recipe handoff** | Give ChatGPT an authoring kit and ask for recipe JSON; load it into Meridian's Composition tools, preview and apply. | Fastest fallback and already possible manually. Export a small versioned kit with schema/examples/principles. File generation depends on available ChatGPT tools; copying recipe JSON also works. There is no live library access or automatic visual correction unless the person supplies feedback. |
| **Instruction-only plugin** | Install Meridian guidance and ask ChatGPT for a Work recipe or artifact. | Packages consistent judgment and examples. It does not itself grant access to Meridian, install its runtime, or guarantee that ChatGPT can render/check a Work. Useful alongside the handoff route, insufficient for seamless creation. |
| **Desktop plugin with local MCP tools** | Install Meridian plus a desktop plugin; use an eligible locally executing ChatGPT thread to create, preview and save a Work in the local library. | Closest match to the present local product. Wrap the existing API/renderer behind focused MCP tools. Bundle or locate a supported runtime and negotiate host capabilities. Public local-MCP distribution requires OpenAI partner approval; personal/repo testing is a separate path. Ordinary Chat eligibility must be checked separately. |
| **Network MCP connection with optional preview UI** | Add a private custom MCP connection, or install a publicly distributed plugin, and create/edit Works through chat. | A hosted HTTPS service or eligible private tunnel can supply the connection. Enforce authorization and operate the service; add durable storage if it owns saved Works. Public directory review applies when publicly submitting, not to every private connection. To write to local Meridian, use an explicit companion/pairing bridge. The online collaboration hub is not a prerequisite. |
| **Custom GPT with Actions** | Open a Meridian-specific GPT which calls an HTTPS REST API. | Still documented, but a weaker new default: Enterprise custom GPTs are being retired in favor of plugins, and Actions have payload/time constraints. It would require a separate wrapper and an authenticated public endpoint. A knowledge-only GPT cannot write local Works. |
| **Assistant inside Meridian using the OpenAI API** | Ask within Meridian itself; Meridian orchestrates the same authoring tools and preview cycle. | Gives us direct control over UX, model/tool orchestration and progress. It is a separate experience from the requested ordinary ChatGPT integration, with its own API usage, account and deployment decisions. Reuse the same tools if added later. |

The skill/MCP separation is intentional: the skill carries design judgment and
review procedure; the server supplies current data, validation, authorization
and controlled writes. It is not necessary to fine-tune a separate model to
start. [Build skills](https://developers.openai.com/plugins/build/skills).

### Desktop and distribution constraints

A desktop-only plugin can launch a local MCP server over stdio and can only be
used in locally executing threads. Public submissions for local MCP plugins
currently require an approved partner and an allowlisted submitting
organization. This is a real distribution constraint; a local prototype should
not be represented as available to everyone in the public directory.
[Submit desktop-only plugins](https://developers.openai.com/plugins/deploy/desktop-only).

The desktop app hosting a capability does not establish that every ordinary
Chat thread can launch a stdio server. Verify that the selected thread executes
locally and supports the installed plugin. Treat ChatGPT Work in an eligible
local thread as a prototype target; separately demonstrate ordinary Chat support
before claiming it. If the goal specifically requires broadly available regular
Chat and local-plugin eligibility or approval is unavailable, prefer a hosted
MCP plugin with an explicitly paired local Meridian bridge.

Local/repo plugin marketplaces provide a supported testing and distribution
mechanism in the desktop app. Packaging, installation and authentication are
separate from having loose skill files on our machine.
[Package your plugin](https://developers.openai.com/plugins/build/plugins).

For hosted MCP plugins, use a stable publicly reachable HTTPS MCP endpoint for
public submission. Custom UI is optional: ChatGPT can render a web component
through the MCP Apps standard, so a Meridian preview could reuse the existing
viewer rather than invent a second renderer.
[Build an MCP server](https://developers.openai.com/plugins/build/mcp-server),
[MCP server and UI quickstart](https://developers.openai.com/plugins/build/app-quickstart).

For private development, Secure MCP Tunnel can forward requests to a private
HTTP or stdio MCP server through an outbound connection. It needs Platform
tunnel permissions and appropriate ChatGPT workspace access. It does not
replace public submission requirements and is not a zero-setup consumer
distribution plan.
[Secure MCP Tunnel](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels).

The current Actions documentation requires public HTTPS/TLS, limits individual
requests and responses to fewer than 100,000 characters, and specifies a
45-second round-trip timeout. Large Work payloads and render tasks would need
chunked operations and job handles. Current Enterprise guidance explicitly
directs custom GPT workflows toward plugins.
[GPT Actions](https://developers.openai.com/api/docs/actions/introduction),
[Production notes](https://developers.openai.com/api/docs/actions/production),
[Build plugins](https://learn.chatgpt.com/docs/build-plugins).

Official documentation establishes these supported paths, but does not establish
identical plan access or custom-plugin permissions for every prospective user.
Verify account, workspace policy and execution surface during onboarding. The
target is broad access; “anyone” must not become a promise that bypasses those
conditions.

## Recommended implementation

Build a **private Meridian MCP toolset with one shared authoring contract**,
retaining the recipe-import fallback. First demonstrate its tools in the user's
ordinary ChatGPT session through an eligible local plugin or private custom
connection/tunnel. Use the Windows companion or Docker arrangement that reaches
this result with the least extra networking. Public plugin distribution is a
later independent decision. Do not build a competing agent, public hub, or
model-training pipeline first.

This roadmap includes implemented v0.2 foundations and remaining broader
distribution/layout work. Use the capability table above and the companion's
actual `meridian_capabilities` response as the current implementation boundary.

1. **Make a portable authoring kit.** Package the maintained accepted rules,
   concise recipe schema, examples/counterexamples, supported feature/version
   table, and identity-preserving update procedure. Existing documents remain
   the maintained source. Generated distribution snapshots need a version and
   upgrade policy so installed instructions do not silently become stale. Keep
   content provenance, source references, full descriptions and file metadata
   separate from concise visible object labels. Sources remain accessible in
   supporting metadata/documents without filling the primary graph with counts,
   navigation prose or file-format scaffolding.
2. **Add reusable graph composition and routing.** Measure complete subtree
   footprints across relevant expansion states; maintain one generation
   direction and modest effective scale variation. Reserve branch space,
   stagger positions and vary link lengths when useful. Persist real routing
   joints, avoid route/body intersections, and report any unresolved collision
   by identity. Expose hierarchy planning, layout and route diagnostics as
   first-class authoring tools rather than expecting the model to improvise
   reliable geometry for each Work. Preserve existing graph identities and
   user-authored routes unless the requested revision changes them.
3. **Expose focused typed MCP operations.** Wrap existing code, not shell access
   or arbitrary file edits. Start with capabilities, Work listing, scoped
   inventory, draft creation/editing, validation, preview/capture, controlled
   save, and export. Return IDs, aliases, versions and diagnostics as structured
   data. Keep attachment bytes and giant graphs out of ordinary tool responses;
   use scoped reads, assets and draft/job handles.
4. **Test in ordinary ChatGPT desktop.** Package the skill with the local MCP
   adapter, install from a personal/repo source, and test from ChatGPT Work in a
   local thread. Test Chat separately if it is a supported target; do not infer
   parity from a Codex test. A clean user should need the product installer and
   documented plugin setup, not our source tree or developer environment.
5. **Use the same renderer for preview and inspection.** The model should see
   overview and readable branch captures, plus overlap/routing diagnostics. It
   must inspect meaningful expansion combinations, including all branches
   open. Preview and revise the candidate before committing it.
6. **Preserve transactional behavior.** Save the candidate only against the
   exact reviewed project revision and recovery token, create checkpoints,
   preserve identity bindings and library destinations, and return a conflict
   rather than overwrite a newer edit. Plugin installation is not permission
   for every write; the user's request and host confirmation policy still
   govern actions.
7. **Choose broad distribution after the prototype passes.** Pursue approved
   desktop-only publication if available. Otherwise use an authenticated hosted
   MCP plugin and explicitly paired local bridge, or deliver portable Works
   for import. Confirm the distribution path before promising public access.

The v0.2 implemented names are listed in
`integrations/mcp/src/authoring-tools.mjs`. The older conceptual names below
remain a roadmap vocabulary, not additional callable endpoints; the complete
subtree-layout and export operations in this list are not implemented tools:

```text
meridian.capabilities
meridian.list_works
meridian.inspect_work(work_id, scope, cursor)
meridian.create_draft(base_work_id?, expected_revision?)
meridian.apply_recipe(draft_id, recipe, expected_draft_version)
meridian.layout_hierarchy(draft_id, roots, direction, constraints)
meridian.inspect_routes(draft_id, scope, expansion_state)
meridian.validate_draft(draft_id)
meridian.preview_draft(draft_id, scope, expansion_state)
meridian.commit_draft(draft_id, expected_revision, recovery_token)
meridian.export_work(work_id, format)
```

The acceptance test is a person creating and revising a well-composed saved Work
from ordinary ChatGPT on a clean desktop installation. Passing a schema check,
generating JSON, installing a skill locally, or having Codex run our CLI is only
part of that result.
