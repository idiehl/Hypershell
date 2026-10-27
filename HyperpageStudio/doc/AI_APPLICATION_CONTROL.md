# AI application-control direction

## Current contract

Hypershell now exposes named, device-local AI roots independently of the File
Explorer's current folder. Setup can register every ready local fixed drive or
one selected folder, and Explorer-derived candidates are optional. Each root has
separate read, write, file/editor auto-apply, live-terminal-read, and
terminal-execution capabilities. Multiple roots can remain approved, but one
root is selected for a conversation at a time; before a conversation starts,
the active file or terminal automatically selects the most-specific matching
approved root.

The AI can currently:

- list and read bounded text beneath the selected root;
- read the active file-backed editor document, including unsaved content;
- replace a typed-provider editor document after exact review or under standing
  auto-apply permission;
- create or replace workspace text after exact review or under standing
  auto-apply permission, with revision checks in both paths;
- read an on-demand snapshot of the active terminal draft and bounded recent
  transcript when that distinct capability is approved; and
- stage a terminal command after local review; and
- execute a terminal command under a per-root policy: review plus a second
  confirmation, one review, or standing auto-execution.

These are live snapshots and typed host operations. They are not continuous
observation and they do not give the provider raw WPF view models, window
handles, process objects, or filesystem handles.

## Why terminal networking and host networking are different

A shell command runs with the Windows user's normal permissions under every
terminal policy and can therefore use networking available to that shell. This does not give the
provider a direct HTTP client or browser. A host-network capability still needs
its own contract so Hypershell can display destinations, methods, uploads,
downloads, redirects, size limits, credentials, and audit metadata before a
request leaves the device.

## Planned control layers

### 1. Multi-root access sets

Create named sets such as `Developer Machine` that contain several approved
roots. Provider tools will address a stable root ID, every result and review
will show the root label plus full path, and per-root capabilities will remain
independent. Switching sets will start a clean context unless the user
explicitly carries conversation state forward.

### 2. Event-driven workspace awareness

Add a bounded host event stream for active-tab changes, editor revision
changes, terminal draft changes, command completion, diagnostics, and selected
workspace changes. Events will be debounced and summarized locally. User modes
will be:

- **Chat**: respond only when prompted;
- **Observe**: receive approved state events but never initiate an action;
- **Suggest**: proactively post explanations or proposed actions;
- **Act**: use preapproved tools according to host-owned policies. Standing
  file/editor auto-apply and terminal auto-execution are implemented foundations,
  while event-driven initiation and a full mode switch remain future work.

A global pause control, visible activity indicator, per-source disclosure,
context-size limit, and cancellation are required before Observe or Act can be
enabled.

### 3. Host network tools

Add explicit fetch/download tools with domain rules, request and response size
limits, redirect rejection, secret isolation, destination disclosure, and
review for uploads or state-changing methods. Terminal networking remains
possible through the separately granted command path and follows that root's
confirmed, review-only, or auto-execute policy.

### 4. Embedded browser tabs

Add **New browser** to the existing `+` menu using a host-owned WebView2 tab.
The first browser slice should provide navigation, back/forward/reload, an
address bar, profile isolation, and download review. AI browser tools will be
typed operations such as inspect visible page, navigate, click, type, and take
a screenshot. Browser access will have origin-level permissions and a visible
AI-control indicator; it will not expose cookies or raw credentials.

### 5. Split and docked AI surfaces

Replace the single right-panel page switcher with a vertical panel stack. Any
approved panel can occupy the upper or lower region, a `GridSplitter` controls
the ratio, minimum heights prevent unusable panes, and the ratio persists in
the workspace layout. The AI surface can therefore remain visible beneath the
Command Builder, an extension panel, or another sidebar without replacing it.

## Direct control principle

The intended experience can feel like direct control of Hypershell, but the
implementation should remain a catalog of typed application commands and
observable state. This gives extensions and additional model providers the
same stable contract, keeps review/autonomy behavior consistent, and makes user-selected
Chat, Observe, Suggest, and Act modes enforceable by the host rather than by a
prompt alone.
