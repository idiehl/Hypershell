# Canvas library API

> The schema-one examples below document migration compatibility. New clients
> save a full ordinary canvas graph in schema two and use the same creation and
> editing interface as every canvas. See [One canvas interface](UNIFIED-LIBRARY.md)
> for schema two and the recovery/checkpoint endpoint contracts. There is no
> current Section node type or subsection action.

The landing canvas has its own persisted hierarchy. Its entries point to saved
canvases; organizing, moving, renaming, or collapsing library entries does not
change the saved canvas content, recovery drafts, or checkpoints. The internal
`projectId` field identifies a saved portable workspace; the interface calls that
workspace a **Canvas**.

Hosts supporting these endpoints advertise `library-v1` in
`GET /__meridian_status`. The endpoints inherit the host's loopback-only access,
same-origin write checks, JSON content-type requirement, and no-store responses.

## Read and save the hierarchy

`GET /api/library` returns:

```json
{
  "schemaVersion": 1,
  "revision": 0,
  "entries": []
}
```

An absent library is an empty revision-zero document. Reading does not create a
file. `PUT /api/library` takes the full replacement document and the revision
that the caller read:

```json
{
  "expectedRevision": 0,
  "library": {
    "schemaVersion": 1,
    "revision": 0,
    "entries": [
      {
        "id": "mythology", "kind": "section", "title": "Mythology",
        "parentId": null, "collapsed": false, "x": 80, "y": 100
      },
      {
        "id": "greek-atlas", "kind": "canvas", "title": "Greek mythology",
        "parentId": "mythology", "collapsed": false, "x": 120, "y": 180,
        "projectId": "saved-workspace-id"
      },
      {
        "id": "greek-atlas-olympus", "kind": "canvas", "title": "Olympus",
        "parentId": "greek-atlas", "collapsed": true, "x": 160, "y": 280,
        "projectId": "saved-workspace-id", "canvasId": "inner-canvas-id"
      }
    ]
  }
}
```

Both sections and canvas entries can contain other entries at any depth. An
entry's `x` and `y` are absolute world coordinates, including nested entries.
`collapsed` controls descendant visibility without deleting those descendants.
A canvas entry with no `canvasId` opens its saved workspace's entry canvas.

The host validates the entire replacement before writing. IDs use 1–96 letters,
digits, underscores, or hyphens, beginning with a letter or digit. Titles contain
1–256 characters and cannot be blank. There may be at most 10,000 entries. Each
entry requires a Boolean `collapsed`, an explicit `parentId` (null for roots),
and finite coordinates between −1e9 and 1e9. Unknown fields, duplicate IDs,
missing parents, and cyclic parent chains are rejected with HTTP 400. Canvas
entries require `projectId` and optionally accept `canvasId`; section entries
cannot carry either reference field. References use valid ID syntax, but may
point to a workspace or inner canvas that has become unavailable. Clients should
show that unavailable state while retaining the user's library organization.

The `library.revision` and `expectedRevision` must match. The host checks the
current persisted revision and replaces the file under a semaphore. A successful
save returns the complete document with its revision increased by one. A stale
revision returns HTTP 409 and preserves the current document; keep the local
draft and reload explicitly before retrying. Concurrent writes at the same
revision have exactly one winner.

Storage is `<data-root>/library.json`. Atomic replacement retains the preceding
committed file as `library.json.bak`; the first save has no preceding file.
Malformed persisted storage returns HTTP 503 and is never silently replaced by
an empty hierarchy. The backup remains available for deliberate recovery.

## Read the catalog

`GET /api/library/catalog` returns an array of lightweight saved-workspace
metadata:

```json
[
  {
    "id": "saved-workspace-id", "title": "Greek mythology", "revision": 4,
    "updatedAt": "2026-10-05T12:00:00.0000000+00:00",
    "entryCanvasId": "overview",
    "canvases": [
      { "id": "overview", "rootId": "overview-root", "title": "Overview", "nodeCount": 12 },
      { "id": "olympus", "rootId": "olympus-root", "title": "Olympus", "nodeCount": 8 }
    ],
    "links": [{ "sourceCanvasId": "overview", "targetCanvasId": "olympus" }]
  }
]
```

Node counts exclude canvas roots. Links summarize `canvas-link` navigation nodes
and include each source/target canvas pair once; unavailable external targets are
omitted. The catalog returns no node content, attachments, snapshots, or recovery
drafts. It derives metadata from committed saved workspaces and never writes to
them. Clients can reconcile newly discovered saved canvases into library entries
and choose their visible hierarchy independently of cross-canvas navigation.

## Verification

Run `node --experimental-strip-types --test tests/library-host.test.ts` from the
repository root. The integration suite builds the host into temporary output,
runs it against a temporary data directory on a free loopback port, and checks
catalog projection, recursive containment, stale saves, simultaneous saves,
backup rotation, restart persistence, malformed trees, unavailable references,
same-origin protections, and damaged-storage preservation. It does not start,
stop, or write to an installed Meridian host.
