# One canvas interface, including home

The landing library is an ordinary editable Meridian canvas. It uses the same
node graph, scene, selection, inspector, creation tools, links, media, layouts,
appearance, undo, autosave and history as other canvases. There is no Section
node type, subsection operation, or separate library authoring mode.

Existing named library groups become ordinary Container nodes. Existing saved
canvas tiles become ordinary Canvas launchers (`canvas-link` in the portable
model). Their identifiers, titles, absolute positions, containment hierarchy and
collapsed state are retained. The older library metadata never stored variable
tile sizes, content or media; after migration these are ordinary editable node
properties. Previously generated parent connections retain their stable
identifiers and belong to the parent, so collapse hides them with the contents.

New home content can be Text, Container, Media, Document, Spreadsheet, Joint or
any other supported ordinary node. Containers can contain other nodes at any
depth, including Canvas launchers. A Canvas created within home belongs to the
home document and uses the same local canvas identity and launcher relationship
as a Canvas created elsewhere. Opening a launcher can use a dedicated window
without changing the home window's canvas.

The home graph remains independently saved from separately stored workspaces.
Editing a home launcher does not edit the canvas it opens. Home's references to
separately stored workspaces use the ordinary node field `canvasTarget`, with a
saved workspace identifier and optional inner canvas identifier. This field also
travels through exports, publications and independent copies. Local Canvas
launchers use `targetId`. The home envelope retains a compatible `canvasTargets`
lookup derived from those node destinations.

## Migration and discovery

`GET /api/library` reads the committed document without writing anything. Version
one documents are migrated in memory by `normalizeLibraryDocument`. The next
successful normal canvas save persists version two. The first replacement keeps
the exact preceding version-one document in `library.json.bak`.

Migration never requires users to rebuild their library or copies saved canvas
content into it. Unavailable external Canvas launch locations remain retained.
Reserved home root and label identifiers are changed only when they collide with
an existing user entry; that existing entry keeps its identifier.

Opening or explicitly refreshing home can discover saved canvases through the
lightweight catalog. Discovery adds only unseen launchers, follows existing saved
canvas launcher relationships as an acyclic initial hierarchy, and positions only
new nodes. It preserves authored titles, positions, other nodes, media and links.
No synthetic Section node is created. The `discoveredTargets` history records
seen locations independently of nodes: deleting a launcher remains a deletion
after reopen instead of catalog discovery recreating it.

## Persistence contract

The version-two document has this shape:

```ts
interface LibraryCanvasDocument {
  schemaVersion: 2;
  revision: number;
  project: MeridianProject;
  canvasTargets: Record<string, { projectId: string; canvasId?: string }>;
  discoveredTargets: string[];
}
```

`project` is the ordinary schema-one Meridian graph, including canvases, nodes,
links, attachments, saved reference snapshots and dependencies. Its revision
equals the envelope revision. An external location key is its launcher's node
identifier. A discovery entry is `projectId:canvasId`; `projectId:*` retains a
legacy entry-canvas reference whose inner canvas was unavailable at migration.
Older version-two documents that stored destinations only in the envelope are
migrated into node `canvasTarget` fields without changing those nodes' identities.
The envelope lookup must agree with any canonical destination on the node.

The host validates the complete ordinary graph and each external location. The
ordinary limit is 10,000 project nodes. Home admits 10,002 so migration can retain
a previously valid library's 10,000 entries plus its root and introductory label.
External locations are capped at 10,000 and discovery history at 20,000.

`PUT /api/library` uses `{library, expectedRevision}`. The submitted envelope and
project must carry that revision. Under the library writer lock, the host checks
the committed revision and atomically replaces the document, advancing both
revisions together. Competing saves have one winner; a stale save returns 409
and preserves its draft. The previous committed file remains the backup.
Once version two has been committed, an older version-one writer cannot downgrade
it and discard ordinary node content. Such a write returns 409 and requires the
current interface.

## Recovery and history

Home has the same persisted recovery and named checkpoint capabilities as other
canvases. These retain the full envelope, including external canvas locations,
attachments and discovery history. They do not rely on browser storage capacity.

| Endpoint | Contract |
| --- | --- |
| `GET /api/library/recovery` | A recovery draft, or 404. |
| `PUT /api/library/recovery` | `{library, baseRevision, expectedSavedAt}`; exact token guards replacement. |
| `DELETE /api/library/recovery` | `{baseRevision, savedAt}`; exact token guards discard. |
| `GET /api/library/checkpoints` | Named checkpoint metadata. |
| `POST /api/library/checkpoints` | `{library, name}` at the current saved revision. |
| `GET /api/library/checkpoints/:id` | The complete checkpoint library document. |

A recovery response contains `{library, project, baseRevision, savedAt}`; `project`
is the ordinary editor view of `library.project`. A stale recovery keeps its
original base revision, and a changed token cannot be overwritten or discarded
implicitly. Committing the same authored envelope consumes its matching draft;
a newer recovery draft survives committing an earlier candidate. Generated
reference snapshots and revision increments do not falsely make the same authored
content appear different.

All endpoints retain loopback access, same-origin write checks, JSON request
requirements and no-store responses. Corrupt committed or recovery storage is
reported and preserved instead of silently reset. Hosts advertise both
`library-v1` migration compatibility and `library-canvas-v2` support.

## Verification

`tests/library-canvas.test.ts` verifies migration identity and geometry, ordinary
home media/links/internal canvases, discovery stability, intentional deletion,
unavailable targets and corrupt-envelope rejection. `tests/library-host.test.ts`
builds and runs an isolated host, verifies version-one compatibility and
version-two graph saves, revision/token conflicts, media persistence, checkpoints,
restart, atomic backups and unchanged separately saved canvas bytes.
