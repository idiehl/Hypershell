# Local Engineer runtime

## Role

`qwen3.5-9b-engineer` is Hypershell's vision-capable local Engineer. It is the
local model used for coding, tool-driven workspace work, and Contexts that
grant Hypershell or Desktop computer control.

Engineer is now Hypershell's only conversational local model. The retired
Operator manifest, picker option, and runtime verifier have been removed.
Provider settings, Contexts, and saved conversations that still contain the
legacy Operator identifier migrate to Engineer when loaded instead of being
disabled.

Qwen Librarian remains an internal embedding model for Hyperlib search. It is
not a conversational model or a model-picker option.

This is capability routing, not weight training.

## Conversation-scoped model selection

Every new AI conversation starts on Engineer. The model selector belongs to
that conversation only; changing it never rewrites device-wide provider state
or another open conversation. The selection is saved with local conversation
history and restored when that conversation is reopened.

Settings deliberately exposes only local-runtime status, the protected OpenAI
credential, and permission to send OpenAI requests. It does not expose a
provider, model, endpoint, reasoning, or verbosity default. Selecting OpenAI in
a conversation also does not enable cloud access by itself.

## Grounding supplied by the host

Every Context-backed request receives a host-generated runtime card in the
provider instructions. It identifies the active Context, resolved model and
role, vision availability, effective computer-control scope, review policy,
file and terminal scope, internet policy, the conversation's exact working
directory, the detected task route, and the tools exposed for that turn. File
contents are not copied into the card.

The card exists to prevent a local model from relying on generic pretrained
assumptions such as "I cannot use the desktop" when the selected Context and
host tools explicitly allow it.

## Conversation project root and task routing

The project working directory is selected beneath the model and Context
controls in the AI pane. It is saved on this device with that conversation;
it is not a global provider setting and local filesystem paths do not roam to
another PC. Every relative file, Git, document, and terminal operation is
resolved inside this root. A relative path without a selected root and any
attempt to escape the selected root are rejected by the host.

Engineer remains one model with one complete host capability catalog. For each
prompt, Hypershell presents only the task-relevant subset (coding, files,
terminal, Git, writing, web, or desktop). Short continuations such as "try
again" retain the preceding task route. This is execution-time tool routing,
not separate Engineer profiles and not a reduction in what a later prompt may
use.

## Windows interaction layers

Engineer has two complementary desktop tools when the selected Context grants
computer control without per-action review:

1. `control_windows_ui` uses Windows UI Automation. Engineer first calls
   `inspect`, receives a bounded semantic map of standard windows and controls,
   then can focus, invoke, set a non-password value, toggle, expand, collapse,
   select, or scroll an element into view.
2. `control_computer` captures the authorized surface and performs bounded
   cursor, keyboard, scrolling, drag, and wait actions in screenshot pixel
   coordinates.

Semantic element IDs belong only to the latest snapshot. Every action returns
a new snapshot and invalidates the previous IDs. Standard named controls should
use UI Automation first; custom-rendered, inaccessible, elevated, or otherwise
unexposed controls use the screenshot/cursor fallback.

The UI Automation layer never reads or returns field values, refuses
`set_value` for password fields, inherits the selected Hypershell/Desktop
scope, and remains subject to the existing global Stop control.

## Action-loop latency

The first Engineer response retains the selected reasoning setting and a
4,096-token output ceiling. A continuation containing a host screenshot or
semantic UI snapshot uses a 1,536-token ceiling and disables the model's
extended thinking template. This keeps initial planning available while making
repeated inspect-act-verify steps substantially lighter. Stale screenshots are
removed when a new user request starts.

Source files use a separate low-friction continuation. Engineer first calls
`prepare_workspace_write` with only the path, `allow_create`, and the current
file hash when replacing an existing file. Hypershell then disables tools and
extended thinking for one 8,192-token response and captures the raw file body
as ordinary response text. A single Markdown fence and the legacy one-use
markers remain tolerated, but neither is required. HTML, CSS, JavaScript, and
other multiline source no longer have to survive as one model-authored JSON
string. The host still
re-authorizes the path, enforces the 256 KiB text limit, checks the existing
hash, creates an exact review when required, writes atomically, and returns the
actual result before Engineer continues.

Existing files now have a more reliable narrow-edit path. Engineer reads the
relevant range and current SHA, calls `prepare_workspace_edit`, then returns
only exact `SEARCH`/`REPLACE` blocks outside JSON. Hypershell requires each
SEARCH section to match exactly once, preserves the file's line endings,
rechecks the SHA, and sends the resulting before/after change through the same
review or standing auto-apply policy. Ambiguous, stale, malformed, and no-op
edits fail without touching the file.

The coding route also exposes `search_workspace_text`, a bounded recursive
source search for local and linked-SSH projects. It skips dependency, VCS,
generated, symlink, binary, oversized, and excessive content and returns paths,
line/column locations, and short previews. This lets Engineer locate symbols
before reading or changing a file rather than guessing repository structure.

For recognized .NET, Node, Rust, Go, Python, Maven, Gradle, and configured
CMake roots, `run_project_check` maps build, test, lint, or typecheck intent to
a host-selected conventional command. It uses the existing terminal policy,
waits for the command to finish, and returns a bounded transcript delta to
Engineer. Ordinary terminal execution now returns the completed transcript
delta too, so the next model step can diagnose actual compiler/test output.

`expected_sha256` is deliberately an existing-file precondition: it is `null`
for creation and comes from `read_workspace_text` for replacement. Engineer is
explicitly told never to calculate a hash of content it is about to write.

Terminal actions use distinct execute and stage tools. The execute tool defers
to the selected Context's host-enforced review policy; the stage tool is only
for an explicit request to place a command without running it. If llama.cpp
rejects a generated function call because its arguments are malformed JSON,
Hypershell adds one bounded corrective turn with thinking disabled and routes
multiline content to the prepared full-file or exact-edit transfer path. A second failure stops safely
with a concise error and no tool execution. If a raw source response omits its
file content or instead returns commentary or another tool call, Hypershell
rejects it without writing and requests one corrected raw response before
stopping.

## Inspectable work-cycle details

When a local working directory is selected, every prompt creates a temporary
diagnostic bundle under `.hypershell/ai-runs/`. Each run contains `prompt.md`,
`response.md`, `summary.json`, and chronological `events.jsonl` documents with
the task route, exposed tools, provider turns, tool requests, host results, and
completion state. Private-key blocks, common API-token patterns, bearer tokens,
and embedded screenshot payloads are redacted; individual events and files are
bounded. Diagnostics never block the assistant if they cannot be written.

Hypershell keeps the newest 30 bundles and removes bundles older than 14 days.
The recorder places a nested `.gitignore` beside them so runs remain visible on
disk without appearing as source changes.
This is the observable evidence layer for future HyperEval replay and scoring;
it is not yet an automated quality score. Remote SSH working directories do
not currently receive these local diagnostic files.

## Verification

The deterministic provider/coordinator contract is covered by:

```powershell
dotnet run --project tools/Hypershell.CSharpVerifier/Hypershell.CSharpVerifier.csproj -c Release -- --workspace-ai-provider-only
```

The installed llama.cpp/Qwen runtime, strict semantic and screenshot tool
calls, image understanding, and cursor grounding are covered by:

```powershell
dotnet run --project tools/Hypershell.CSharpVerifier/Hypershell.CSharpVerifier.csproj -c Release -- --workspace-ai-local-runtime-only
```

Working-directory isolation, dynamic task routing, follow-up continuity, and
diagnostic redaction are covered by:

```powershell
dotnet run --project tools/Hypershell.CSharpVerifier/Hypershell.CSharpVerifier.csproj -c Release -- --workspace-ai-execution-only
```
