# Hypershell extension panel message API

Status: host API version 1 is implemented for approved local WebView panels.

The message API is a narrow bridge, not general application access. It gives a
panel a versioned handshake, bounded device-local JSON state, and optional
read-only workspace context. It does not expose files, document contents,
terminal input/output, command execution, processes, network access, secrets,
native code, or AI tools.

## Declaring the API

A panel opts in explicitly through its manifest contribution:

```json
{
  "id": "goalTracker",
  "displayName": "Goal Tracker",
  "viewResource": "panel/index.html",
  "messageApiVersion": 1,
  "placement": {}
}
```

Omitting `messageApiVersion` leaves WebView messaging disabled. Version 1 is
enabled only after the exact extension ID, version, and package SHA-256 hash
receive device-local `WorkspacePanel` approval. Read-only workspace context
also requires requested and approved `WorkspaceMetadata` capability.

## Request and response envelope

Panels send structured objects through `window.chrome.webview.postMessage`:

```json
{
  "protocol": "hypershell.panel",
  "version": 1,
  "type": "request",
  "id": "panel-1",
  "method": "host.ready",
  "params": {}
}
```

The host returns a response with the same request ID:

```json
{
  "protocol": "hypershell.panel",
  "version": 1,
  "type": "response",
  "id": "panel-1",
  "ok": true,
  "result": {}
}
```

Rejected requests use `ok: false` and an `error` object containing a stable
`code` plus a safe user-facing `message`. Request IDs must be unique within one
navigation. Unknown and duplicate envelope or parameter fields are rejected.
The authoring schema is
`doc/schemas/hypershell-panel-message.schema.json`.

## Version 1 methods

| Method | Required approval | Purpose |
| --- | --- | --- |
| `host.ready` | `WorkspacePanel` | Mandatory first request. Returns the extension identity, actually approved capabilities, supported methods/events, persistence scope, and limits. |
| `state.get` | `WorkspacePanel` | Reads this extension panel's device-local JSON object. Missing state returns an empty object. |
| `state.set` | `WorkspacePanel` | Atomically replaces this extension panel's device-local JSON object. |
| `host.getContext` | `WorkspaceMetadata` | Returns bounded theme, active-project identity/name, open-tab count, and safe active-tab metadata. |
| `events.subscribe` | `WorkspaceMetadata` | Replaces the current event subscription set. Version 1 supports only `host.contextChanged`. |

`host.ready` may be called once per navigation. Calling another method before
the handshake returns `HANDSHAKE_REQUIRED`.

## Read-only context

The context has its own schema version and deliberately excludes paths,
working directories, document text, terminal transcripts, and commands:

```json
{
  "schemaVersion": 1,
  "workspace": {
    "activeProject": { "id": "project-id", "name": "Project name" },
    "openTabCount": 2,
    "activeTab": {
      "kind": "document",
      "title": "Program.cs",
      "hasUnsavedChanges": true,
      "languageId": "csharp",
      "shellKind": ""
    }
  },
  "theme": {
    "colorScheme": "dark",
    "background": "#0D1117",
    "panel": "#161B22",
    "foreground": "#E6EDF3",
    "mutedForeground": "#8B949E",
    "accent": "#58A6FF"
  }
}
```

After subscribing, `host.contextChanged` carries the same context object in
its `data` field. The host suppresses duplicate events when the serialized
context has not changed.

## State boundary

`state.set` accepts one `value` property whose value must be a JSON object.
State is capped at 48 KiB, written atomically, and isolated by extension and
host-qualified panel ID beneath the device's Hypershell application-data
directory. It survives panel closure and package updates, but it is not part of
a portable profile in this slice. Revoking approval immediately removes panel
access without silently deleting the saved state. This store is not a
credential vault; packages must not place passwords, access tokens, or other
secrets in it.

Panels should include their own state schema version so they can migrate data
across releases:

```json
{
  "value": {
    "schemaVersion": 1,
    "selectedStageIndex": 2
  }
}
```

## Enforced limits and isolation

- 64 KiB maximum inbound message;
- 48 KiB maximum state object;
- 120 requests per rolling minute per panel session;
- JSON depth limited to 32;
- exact HTTPS virtual-host origin check on every message;
- one-use request IDs with a bounded replay cache;
- only the package directory is mapped into the WebView;
- external navigation/resources, downloads, browser permissions, new windows,
  host objects, developer tools, and undeclared messaging remain blocked.

Common error codes include `JSON_INVALID`, `REQUEST_INVALID`,
`VERSION_UNSUPPORTED`, `HANDSHAKE_REQUIRED`, `REQUEST_ID_REUSED`,
`CAPABILITY_DENIED`, `METHOD_NOT_FOUND`, `PARAMS_INVALID`,
`EVENT_UNSUPPORTED`, `MESSAGE_TOO_LARGE`, `STATE_INVALID`,
`STATE_TOO_LARGE`, `STATE_UNAVAILABLE`, and `RATE_LIMITED`.

The complete client example is in `samples/extensions/goal-tracker`. It
handshakes, restores and saves progress, uses the host color-scheme signal,
reads safe workspace context, and subscribes to context changes without
receiving any file or terminal capability.
