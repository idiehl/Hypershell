# Meridian local host and MyNet package

Meridian uses ASP.NET Core on .NET 10 to serve its browser editor and a local
project API. The scene model, viewer and HTML publication runtime do not require
the host. Hypershell/MyNet starts a published executable through its existing app
manifest; Meridian does not modify Hypershell source or its trust registry.

## Development and packaging

Prerequisites: Node 24, npm and .NET SDK 10. Run `npm ci` before the first build.

```powershell
npm run build
dotnet run --project src/host -- --urls http://127.0.0.1:5240
```

`scripts/start.ps1` builds the development app when there is no published package,
then starts it in a hidden process. It prints the URL, process ID and log path.
Use `-Port 5241` for a different port or `-DataRoot <directory>` for isolated
data. `-Foreground` explicitly runs the host in the current terminal. An occupied
port is reported without stopping its owner.

```powershell
./scripts/package.ps1
```

Packaging rebuilds browser assets and publishes a self-contained Windows x64
host to `output/app`. `output/meridian.app.json` is an importable MyNet app
document. In MyNet, import it, review and trust its exact launch command, then
open Atlas Meridian. This is MyNet's existing command-trust workflow; packaging
does not edit the trust file. Generate the manifest again after moving the
package because it contains absolute paths. Package into an unused output
directory when an older packaged executable is running.

The manifest uses `atlas.meridian`, ports 5240–5249, a loopback URL containing
`{port}`, a managed-process stop policy and local resources only. Its launch
arguments are `--urls http://127.0.0.1:{port}`. The health endpoint is
`GET /__meridian_status`, returning status 200 and `X-Atlas-Meridian: true`.

Current stop limitation: MyNet's `ManagedProcess` policy first tries to close a
native main window. This headless ASP.NET host has no such window, so MyNet offers
its separate, process-verified force-stop action. Save editor work before stopping
the host. A future graceful stop can use MyNet's existing `Command` policy with a
Meridian-owned shutdown signal; it would require reviewing the changed command
fingerprint through MyNet's normal trust flow.

## Durable data and concurrency

The default data directory is `%LOCALAPPDATA%/AtlasMeridian`. Override it with
`--data-root <directory>`. Managed project files, recovery drafts and checkpoints
live beneath this root; request data never chooses arbitrary filesystem paths.
One host can own a data directory at a time. Browser storage is not used as the
source of truth, since MyNet app windows may use InPrivate WebView2.

Writes use a temporary file in the destination directory, flush it to disk and
atomically replace the destination. Explicit saves use optimistic project
revisions: a stale save returns 409 and leaves the committed version intact.
The client must retain the user's draft on errors. Project IDs are bounded ASCII
letters/digits/underscore/hyphen, starting with a letter or digit.

Authoring tools can send `requireNoRecovery: true` on a project PUT. The store
checks that condition under the same write lock as the revision check and atomic
save; an existing or concurrently created recovery returns 409. The health
response advertises `authoring-save-guard-v1` so tooling refuses older hosts that
cannot enforce this condition. Ordinary editor saves keep their existing behavior.

Recovery is separate from the committed project. Its `savedAt` timestamp is also
an optimistic concurrency token: an autosave must send the token it last read or
received. A first autosave sends null or omits the token and cannot overwrite an
existing draft. Discarding a recovery requires its exact base revision and token.
An existing draft may continue autosaving after another window advances the
committed project, but it must retain its original base revision and exact token.
It cannot silently rebase itself or erase the conflict guard. Creating a new
recovery at an already stale base is rejected.
Normal save removes recovery only when its authored contents match the saved
project; generated snapshots and the revision number are ignored for this
comparison. Different recovery content is retained for review.

Checkpoints store a complete named snapshot, including uncommitted content based
on the current project revision. Reading a checkpoint does not mutate anything.
To restore one, put its content into the editor, retain the current project
revision and use a normal revision-checked save.

## HTTP API

All JSON uses camelCase. `Project` is `MeridianProject` in `src/core/types.ts`.
Errors return `{ "error": "message" }` with an appropriate HTTP status.

| Method and route | Request / response |
| --- | --- |
| `GET /api/projects` | Returns `[{id,title,revision,updatedAt}]`. |
| `GET /api/projects/{id}` | Returns the committed `Project`, or 404. |
| `POST /api/projects` | Body: a `Project` with revision 0. Returns the saved project at revision 1 and status 201; duplicate ID returns 409. |
| `PUT /api/projects/{id}` | Body: `{project,expectedRevision}`. Both project revision and current stored revision must equal `expectedRevision`. Returns project with revision incremented, or 409. |
| `GET /api/projects/{id}/recovery` | Returns `{project,baseRevision,savedAt}`, or 404. |
| `PUT /api/projects/{id}/recovery` | Body: `{project,baseRevision,expectedSavedAt?:string|null}`. Returns `{project,baseRevision,savedAt}`. A new recovery requires the current project revision. An existing recovery can retain its older base with its exact token; changing its base or using a stale token returns 409. |
| `DELETE /api/projects/{id}/recovery` | JSON body: `{baseRevision,savedAt}`. Returns 204; changed recovery returns 409. Already absent recovery also returns 204. |
| `POST /api/projects/{id}/checkpoints` | Body: `{name,project}`. Returns `{id,name,createdAt,revision}` and status 201. |
| `GET /api/projects/{id}/checkpoints` | Returns checkpoint metadata, newest first. |
| `GET /api/projects/{id}/checkpoints/{checkpointId}` | Returns the checkpoint's `Project`, or 404. |
| `POST /api/import` | Multipart field `file`, filename ending `.meridian`. Returns parsed `Project`; does not create or modify a managed project. |
| `POST /api/export` | Body: `Project`. Returns ZIP bytes with `application/octet-stream` and an attachment filename. |

Creating a copy from an imported project requires a new project ID and revision
zero before `POST /api/projects`. Replacing an existing managed project requires
its current identity/revision and a normal explicit save. This prevents import
from silently overwriting managed work.

The host accepts loopback connections with a loopback Host header. Writes with
an Origin must match the current host origin, and cross-site fetch writes are
rejected. Mutating JSON routes require `application/json`, including recovery
DELETE. No CORS access is enabled. Asset bytes are returned only inside JSON or
downloadable ZIP; there is no endpoint that serves arbitrary attachment content
as an active web page. The editor/viewer controls safe previews and downloads.

## `.meridian` package version 1

The project file is a ZIP with a `project.json` manifest containing
`schemaVersion: 1`. Asset descriptors, including assets held by saved reference
snapshots, replace `base64` with `entry: "assets/<sha256>.bin"`. Those entries
contain the original bytes and are deduplicated by content hash. Import restores
the in-memory base64 fields after verifying the referenced bytes' hashes.

No archive paths are extracted to the filesystem. Only `project.json` and
hash-named asset entries are accepted. Duplicate, unreferenced, unsafe, missing,
truncated or hash-mismatched entries are rejected. Unsupported schema versions
are rejected without changing managed projects. File MIME metadata is preserved
for ordinary documents; control characters and malformed types are rejected.

Limits: 100 MiB request, 80 MiB expanded ZIP, 1,024 ZIP entries, 16 MiB per asset,
64 MiB total reconstructed asset bytes including snapshots, 1,000 canvases,
10,000 nodes and 20,000 links. These are explicit first-version limits, not
performance promises. Oversized data is rejected with a readable error.

## Verification

```powershell
node --experimental-strip-types --test tests/host.test.ts
```

After packaging, set `MERIDIAN_TEST_PACKAGED=1` when running that command to test
`output/app/Meridian.Host.exe`, its bundled browser files and its MyNet manifest
instead of rebuilding the development host.

The integration test builds the actual host, launches it on a dynamically
selected loopback port with a temporary data root, and verifies saves, conflicts,
concurrent requests, recovery tokens, discard protection, checkpoints, process
restart and package import/export. It also checks archive traversal, asset
tampering, unknown versions and cross-origin rejection. Only the test's own host
process and verified temporary directory are stopped/removed.
