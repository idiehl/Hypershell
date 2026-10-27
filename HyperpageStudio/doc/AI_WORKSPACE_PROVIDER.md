# AI workspace provider

## Implemented slice

Hypershell now has a provider-connected AI workspace page in the right panel.
The first adapter uses OpenAI's Responses API, while the conversation
coordinator and host-tool protocol remain provider-neutral.

The provider is disabled by default. A user must separately:

1. save an OpenAI API key for the current Windows user;
2. enable cloud AI requests in **Settings > AI Access**; and
3. choose full-device or single-folder setup, or add a named local root
   manually, and approve its capabilities.

Provider consent never creates a workspace approval, and a workspace approval
never enables the provider. Both are device-local and excluded from portable
profiles and profile sync.

## Data and credential boundary

- The only allowed endpoint is `https://api.openai.com/v1/responses`.
- The default model alias is `gpt-5.6`, with medium reasoning and medium text
  verbosity.
- Requests use `store: false`. Response output items, including reasoning and
  function-call items, are replayed from bounded in-memory conversation state.
- Prompts, model replies, requested file or editor excerpts, and tool results
  leave the device only after cloud requests are explicitly enabled.
- The OpenAI API key is protected with Windows DPAPI at `CurrentUser` scope.
  It is never written into the JSON provider configuration, a profile bundle,
  the audit log, or conversation messages.
- A random, privacy-preserving `safety_identifier` is stored in the local
  provider configuration. It is not an account name, path, or profile ID.
- The host rejects redirects, non-HTTPS endpoints, oversized requests and
  responses, malformed provider output, multiple tool calls when parallel
  tools are disabled, and conversations over the local item or byte limits.

## Host-owned tools

The provider receives seventeen strict base tools. Every schema has
`additionalProperties: false`, and the host validates the arguments again.

| Tool | Behavior |
| --- | --- |
| `search_hyperlib` | Searches the validated static Hyperlib catalog without executing its content. |
| `list_workspace` | Lists at most 512 entries beneath the selected approved root. |
| `search_workspace_text` | Recursively searches bounded source text beneath the conversation root or a narrower path. It skips VCS, dependency, generated, large, binary, and symlink content and returns at most 120 line matches. Local and linked-SSH projects use the same Context authorization. |
| `read_workspace_text` | Reads a requested line window, returns the current SHA-256, and limits provider-visible text to 128 KiB. |
| `read_active_document` | Reads live unsaved editor content through the document's typed provider, only when its file path is inside the selected approved root. |
| `read_active_terminal` | Reads an on-demand bounded snapshot of the current draft command and recent transcript for a terminal inside the selected root. This requires its own terminal-state permission and is not continuous observation. |
| `propose_workspace_write` | Requests an atomic create or SHA-guarded replacement for short text that is safe to carry in one JSON argument. |
| `prepare_workspace_write` | Validates a source-file or multiline-text target using only its path and existing-file precondition, then captures the next bounded ordinary-text response outside function-call JSON. The host still reviews or applies the final content through the same atomic write path. |
| `prepare_workspace_edit` | Validates an existing file and current SHA, then captures exact `SEARCH`/`REPLACE` blocks outside JSON. Every search must match once; ambiguous, stale, malformed, or no-op edits fail before review or mutation. |
| `propose_workspace_delete` | Requests a SHA-guarded text-file deletion inside the selected Context. |
| `propose_active_document_replace` | Requests a full typed-provider editor replacement. The host either creates an exact review or applies it under the root's standing auto-apply permission. |
| `execute_terminal_command` | Requests execution. The saved Context policy decides whether the command runs automatically or pauses for review. |
| `stage_terminal_command` | Places a command without executing it and is reserved for an explicit staging or preview request. |
| `run_project_check` | Detects a conventional project manifest and maps `build`, `test`, `lint`, or `typecheck` to a host-selected .NET, Node, Rust, Go, Python, Maven, Gradle, or configured CMake command. It uses the normal terminal policy, waits for completion, and returns bounded output. |
| `fetch_url_text` | Fetches bounded public HTTPS text when the Context allows internet access. |
| `inspect_git_repository` | Inspects a local or linked-SSH Git repository through the semantic adapter. |
| `propose_git_operation` | Requests a validated semantic Git mutation under the Context's permissions and review policy. |

There is no unrestricted-filesystem, process, secret, extension-install, or
permission-management tool. Internet fetches are bounded and Context-gated. An executed terminal
command can still use the Windows user's ambient filesystem, process, and
network permissions.

## Review lifecycle

```text
provider function call
        |
        v
host validates schema + selected workspace approval
        |
        +---- read-only tool ----> bounded result ----> provider continues
        |
        +---- file/editor request -> saved root policy
                                      |
                       +--------------+--------------------+
                       |                                   |
                 exact review                        standing auto-apply
                       |                                   |
              approve / reject                   authorize + compare SHA
                       |                                   |
                       +----------- function result -------+
                                           |
                                           v
                                  provider continues
```

File and live-document replacements are re-authorized and re-check their
requested SHA immediately before application, whether reviewed or automatic. A
changed file or editor buffer causes a stale-action failure. Active-document edits use
`ReplaceThroughProvider`, so structured providers can reject invalid output;
successful edits remain visibly unsaved until the user saves the document.

For a new file, `expected_sha256` must be `null`. For a replacement, it must be
the current hash returned by `read_workspace_text`; it is never a hash of the
new content. Multiline source takes a two-step path: the model first submits a
small `prepare_workspace_write` JSON call, then returns HTML, CSS, JavaScript,
or other source as ordinary response text. A single Markdown fence and legacy
host markers are accepted for compatibility but are not required. The server
serializes that text rather than asking the model to JSON-escape every quote,
backslash, and newline. Hypershell captures the payload, re-authorizes the
target, re-checks the hash, and uses the normal review or auto-apply policy.

For narrow changes to an existing file, `prepare_workspace_edit` uses the same
two-step transport but accepts only exact blocks delimited by the three marker
lines `<<<<<<< SEARCH`, `=======`, and `>>>>>>> REPLACE`.

The host normalizes transfer line endings for matching, requires every SEARCH
section to be non-empty and unique in the evolving file, preserves the file's
line-ending convention, and then runs the ordinary SHA-guarded review/write
path. This reduces how much source a small model must regenerate while
retaining stale-write protection.

Terminal staging pauses in the review surface. Execution is controlled by a
device-local Context or per-root policy, not by model instructions. The default pauses in
the review surface and then requires a native confirmation. **Review once**
uses the review-card approval as the execution grant. **Auto-execute** performs
host authorization and dispatch immediately, waits for command completion,
then returns the bounded terminal transcript delta to the provider. Semantic
project checks use the same behavior. The latter two modes never manufacture
`userConfirmed: true`; the broker authorizes them from the saved policy.

All three paths use a normal shell with the Windows user's permissions. The
approved root selects the terminal's starting directory but is not an OS
sandbox; commands can address other paths and use available networking.

Local llama.cpp responses receive one automatic corrective retry when the
server reports malformed JSON function arguments. The retry explicitly routes
source-file content to `prepare_workspace_write`, whose JSON contains no file
body, instead of repeating a large escaped argument or embedding a multiline
document in a shell command. If the retry also fails, Hypershell
stops the turn, executes no tool, and reports a bounded error without echoing
the generated file body.

## Audit behavior

The existing rotating workspace AI audit records authorization decisions,
bounded targets, capabilities, and completion outcomes. It does not record:

- prompts or model responses;
- file or editor contents;
- proposed replacement text;
- API keys; or
- terminal command text.

It also does not record live terminal draft or transcript content. Terminal
entries retain only a short command-hash prefix.

This security audit is separate from the temporary, user-inspectable work-cycle
bundle under a selected local project's `.hypershell/ai-runs/` directory. That
bundle deliberately contains the prompt, final response, route/tool metadata,
and bounded chronological provider/tool events for debugging. Hypershell
redacts private-key blocks, common API-token patterns, bearer tokens, and
embedded screenshot payloads; keeps at most 30 recent bundles; and removes
bundles older than 14 days. A nested `.gitignore` prevents the bundles from
polluting repository status. Diagnostic write failures never interrupt the AI
cycle.

## Verification

The deterministic verifier uses a fake HTTP handler and a scripted provider; it
never performs a paid or live OpenAI request. Run:

```powershell
dotnet run --project tools/Hypershell.CSharpVerifier/Hypershell.CSharpVerifier.csproj -c Release -- --workspace-ai-provider-only
```

It verifies fixed-endpoint request construction, `store: false`, seventeen
strict base tools, DPAPI credential round-trip, provider response parsing,
raw full-file and exact-edit transfer modes, repository search, one
malformed-source correction, exact reviewed and standing-auto HTML/JavaScript
writes, completed terminal-output feedback, live terminal snapshot routing,
and all three host-enforced terminal execution paths.

## Deliberate next boundaries

- streaming response rendering and cancellation controls;
- token and cost estimates before sending;
- optional local-model adapters using the same host protocol;
- named access sets that expose multiple approved roots to one conversation;
- bounded host-network and embedded-browser tools with separate consent;
- event-driven observe, suggest, and act modes for editor/terminal changes;
- vertically stacked workspace panels with a persisted AI split ratio;
- compact diff rendering for large exact before/after reviews;
- automated HyperEval replay, assertions, and quality scoring over the exported run details; and
- the separate `ICommandDefinitionAgent` workflow for generated library-guide
  drafts and human review.
