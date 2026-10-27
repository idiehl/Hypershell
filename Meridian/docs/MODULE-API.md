# Local module library API

Meridian stores reusable assemblies as immutable local snapshots. The module's
ordinary `module` node remains the definition and packing container. Saving a
definition captures its complete descendant branch, internal connections, assets,
and explicit dependency declarations. Instantiation is a separate editor operation
that creates fresh identities; reading this API never mutates an existing canvas
or resolves missing external requirements.

The local host advertises `modules-v1` in `/__meridian_status`. Endpoints inherit
the loopback host restriction, same-origin write guard, JSON content-type guard,
100 MiB request limit, and `Cache-Control: no-store` policy. The existing
`ProjectStore` exclusively owns the data root for the host process.

## Stored shape

```ts
interface StoredModule {
  schemaVersion: 1;
  id: string;       // Hyphenated GUID used for immutable library lookup.
  name: string;     // Trimmed, nonempty, at most 160 characters.
  createdAt: string; // ISO timestamp with a timezone.
  rootId: string;   // The definition's ordinary module node inside project.
  project: MeridianProject;
}
```

The project contains exactly one canvas. Its canvas root has exactly one direct
child: the module definition identified by `rootId`. Every other node belongs to
that module branch. Nested module nodes are ordinary descendants. No saved
reference snapshots are included. Node/link/asset identities must be unique
throughout the project. The module has no implicit execution behavior.

An optional `dependencyIds: string[]` property on an ordinary node declares the
node identities it requires. Each list has at most 1000 unique, trimmed, nonempty
identifiers of at most 96 characters and excludes the declaring node's own ID.
Identifiers start with a letter or digit and contain only letters, digits,
underscores, or hyphens, matching the host's safe identity syntax.
Unresolved identifiers and cycles across different declarations are preserved;
declarations do not grant access, fetch objects, or create functional links. The
same field is validated on regular project, recovery, import, and export paths.

The first implementation uses a separate immutable lookup GUID and definition
node GUID. `rootId` keeps that relationship explicit; it does not change node
identity or introduce a non-node definition mechanism.

## Routes

| Method and path | Request | Successful response |
| --- | --- | --- |
| `GET /api/modules` | None | `200` catalog array |
| `GET /api/modules/{id}` | Module lookup GUID | `200` full `StoredModule` |
| `POST /api/modules` | `{ "module": StoredModule }` | `201` full saved `StoredModule` |

Catalog entries contain only `id`, `name`, `createdAt`, `rootId`, `nodeCount`,
`linkCount`, and `contents: { kind, count }[]`. Counts include the module definition
node and its descendants, excluding the synthetic canvas root. Catalog entries
never return document text or attachment base64. Entries sort by descending
creation timestamp.

The client supplies a fresh lookup GUID when saving. There is no replacement or
delete route. A repeated GUID returns `409`, including when an existing file is
damaged; a save cannot silently overwrite it. Concurrent create requests for one
GUID have exactly one winner.

## Validation and storage

The host validates both the project shape and the stored module graph: one valid
canvas root, connected acyclic containment, valid module root, supported Boolean
joints, inherited restrictions, internal connection endpoints, valid functional
ports, one connection per input, and absence of functional feedback. Module node
references and declarations cannot depend on their storage project, canvas, or
canvas root because insertion does not copy that storage infrastructure. Explicit
dependency declarations can remain unresolved. Purely visual cycles are valid.

Files reside at `<data-root>/modules/<lookup-guid>.json`. Writes use a unique
temporary file, flush to disk, and atomically move into an unused destination.
The module library accepts at most 1000 stored modules. A module file is limited
to 80 MiB, with the project's existing 10,000-node, 20,000-link, 1000-asset,
16 MiB per-asset, and 64 MiB total-asset limits. Catalog reads process one bounded
module at a time and return metadata only.

| Status | Meaning |
| --- | --- |
| `400` | Invalid module, graph, field, or lookup identifier |
| `403` | Non-loopback access or cross-origin write |
| `404` | Valid lookup GUID has no stored module |
| `409` | Immutable GUID already exists or the library is full |
| `413` | Request, module, or attachment is too large |
| `415` | Write did not use JSON |
| `503` | Storage unavailable, damaged stored module, or oversized on-disk library |

Damaged modules cause an explicit `503` on retrieval or catalog listing. Their
files remain untouched. The response identifies the affected filename without
exposing the data-root path. A missing module is not represented as an empty
successful result.

## Verification

`node --experimental-strip-types --test tests/modules-host.test.ts` builds into
a fresh temporary output and runs an isolated host on a free loopback port with
independent storage. It covers exact content round trips, bounded catalog fields,
new node fields through project/export routes, immutable concurrent creation,
restart durability, malformed graphs and inheritance, unresolved and cyclic
declarations, write-origin protection, and preservation of corrupt storage.
