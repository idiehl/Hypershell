# Atlas Meridian

Meridian is a 2D workspace for connected information and interactive systems.
Create a canvas containing nested canvases, arrange and collapse containers, and
connect Boolean or numeric inputs through operation joints and routing junctions. Publish a finished canvas as
one HTML file containing its viewer and selected assets.

This repository contains the first local development milestone. The editor runs
on a loopback host and is packaged for Hypershell MyNet. Hosting the editor and a
public project repository at atlasuniversalis.com is future work.

## Run the editor

Development requires **Node.js 24**, **npm**, and **.NET SDK 10**. From this folder:

```powershell
npm ci
npm run build
npm start
```

Open `http://127.0.0.1:5240`. Home is an editable Canvas library using the same
node, link, selection and inspector tools as other canvases. Use the top-right
**+** dropdown to choose **Node** or **Link**, then click a position or node to
open its creation form. See [the workspace tools guide](docs/WORKSPACE-TOOLS.md)
and [the unified library contract](docs/UNIFIED-LIBRARY.md). The right-side selector
chooses **Canvas**, **Properties**, **Appearance**, **Content**, **Relations**, or
**Links**; see [the inspector guide](docs/INSPECTOR-PANELS.md).

For an isolated data directory or another port:

```powershell
dotnet run --project src/host -- --urls http://127.0.0.1:5241 --data-root .test-data/manual
```

The default project store is `%LOCALAPPDATA%\AtlasMeridian`. The host stores
projects, recovery drafts and named checkpoints on disk; browser storage is not
the source of truth. Only one host can own a data directory at a time.

## Create, publish and reuse

1. Use **+ → Node** or **+ → Link** to create objects through a shared adaptive
   form. The tool remains active; choose **Hand** to select and move without
   creation or deletion. Right-click offers creation and other object actions.
   Links can be **Visual**, **Boolean** or **Numeric**.
   Boolean, arithmetic and comparison operations belong to joint nodes. Inputs and outputs are separate
   nodes. A node has one containment parent; references provide other appearances.
   Select a connection and choose **Insert junction** to share a route. A crossing
   stays separate until explicitly joined. See [Node systems](docs/NODE-SYSTEMS.md).
2. Changes save automatically. The **⋯ Workspace actions** menu also provides
   **Save**, and **History** contains named checkpoints;
   automatic recovery drafts and revision checks protect unsaved work when a save
   fails or another window changes the project.
3. **Export canvas** creates a `.meridian` file containing editable project data
   and original asset bytes. It is a ZIP package with a versioned manifest.
4. **Preview** or **Publish** creates a fixed HTML publication. Its viewer starts
   stopped; Run enables logic. Exposed controls and navigation affect only that
   viewing session. Stop preserves the current input values, switch positions and wiring.
5. Open the HTML directly in a browser, including offline, or upload it to a web
   server and embed it with an iframe. No Meridian installation, local host or
   Atlas service is required to view the published HTML.
6. **Import** can open a `.meridian` project or a Meridian-published HTML. HTML
   imports always create an independent editable project with fresh identities.
   A project-file import preserves identity unless that project already exists
   locally, in which case the editor offers an independent copy.

### Canvas and node snapping

Use **Alignment behavior** in **Properties** to choose Sticky, Align to edges,
Free, or inheritance. With no node selected, this edits the canvas's default.
New canvases and canvases without an explicit preference default to Sticky;
individual nodes initially inherit that behavior. Existing explicit preferences
remain in effect. A reference appearance has its own movement settings.

Sticky contact follows the actual backdrop boundary, including polygon and
curved edges. Hold **Alt** during a drag to temporarily bypass snapping.
Collision prevention remains active in every mode: movement, keyboard nudges,
resizing, and appearance edits prevent separate tiles from overlapping. Enclosed
ancestor/child content is intentionally allowed inside its parent. Existing
overlaps are separated on opening. Undo and redo retain collision prevention.

Selected outlines follow the backdrop. Resize at its edge without a visible
corner grip. Defaults and overrides survive save, recovery, export, checkpoints,
and publication import. Published viewers retain these settings for later editing.

### Visual authoring and composition

Select a node and choose the **Appearance** panel to choose a preset, font family,
title/body size, colors, border, or image fit. Text nodes can use unframed text for
headings. Select a connection to give it a visible label and line style. Select
the canvas background to adjust its theme and grid visibility.

The **⋯ More canvas tools → Composition tools** action inspects the canvas and accepts a JSON
composition recipe. Preview renders it with the shared published viewer; Apply
adds the entire composition as one undoable edit. A recipe can use readable keys,
forward references, embedded attachments, and row, column, grid, or radial layouts.

The local authoring toolkit supports scripts and agent workflows:

```powershell
npm run author -- help
npm run author -- inspect --input examples/visual-authoring/visual-atlas.project.json
npm run author -- capture --input examples/visual-authoring/visual-atlas.project.json --out output/visual-review
```

Capture produces real viewer screenshots and browser measurements. Inspect the
images as well as the report: automated checks do not assess visual storytelling.
See [the authoring guide](docs/AUTHORING.md) for the recipe contract, safe host
updates, and the five-canvas [visual atlas example](examples/visual-authoring/README.md).

### Embed a publication

An embed needs only the uploaded HTML file:

```html
<iframe src="my-canvas.html" title="Meridian canvas"
        width="100%" height="640" style="border:0" allowfullscreen></iframe>
```

References within one canvas update live. References to another canvas use its
last saved snapshot, including when both canvases belong to the same project.
Saving refreshes the saved snapshots. Publication and import preserve the
selected pinned snapshots; neither follows a remote source automatically.

## Package for MyNet

```powershell
./scripts/package.ps1
```

This generates a self-contained Windows x64 host in `output/app` and an importable
MyNet manifest at `output/meridian.app.json`. End users of this package do not
need Node.js or a separately installed .NET runtime.

In the **main Hypershell window**, open its **MyNet** panel and choose **Import**.
Select `output/meridian.app.json`, review the import, then review/trust the exact
launch command before starting Atlas Meridian. The separate MyNet desktop has
an Add app form; its current menu does not provide manifest import. Packaging
does not register or trust the app automatically.

The manifest uses ports 5240–5249 and blocks external resources by default.
Regenerate it after moving the package because its launch paths are absolute.
If an existing packaged executable is running, publish to a different directory:

```powershell
./scripts/package.ps1 -OutputDirectory output/candidate
```

See [host and package documentation](docs/HOST.md) for the API, storage layout,
revision/recovery behavior, startup options and package limits.

## Demonstration files

- [Editable project](examples/first-connections.meridian)
- [Offline viewer](examples/first-connections.html)
- [Iframe example](examples/embed-example.html)

Regenerate these after building with `node scripts/examples.mjs`.

## Verify

```powershell
npm run typecheck
npm test
npm run build
dotnet build src/host
npx playwright install chromium firefox
npm run test:browser
```

`npm test` covers the model, logic, publication and real local host. Browser tests
use isolated project data on port 5291 and exercise editor persistence, offline
HTML, iframe interaction, recovery conflicts and geometry/history controls.
The browser suite requires that port to be available. It rebuilds the host before
starting so its copied web assets match the current `dist` build.

On this machine, Firefox validation used a fresh official isolated runtime:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/.cache/playwright-diagnostic"
npx playwright test --project=firefox
Remove-Item Env:PLAYWRIGHT_BROWSERS_PATH
```

The installed MyNet entry is ready to open. Its headless host currently requires
MyNet's separate verified force-stop step when stopping it; saved work and recovery
are disk-backed and were verified across a full MyNet restart.

See [first-cycle implementation and validation](docs/FIRST-CYCLE.md) for what has
been verified and the remaining boundaries. Audio, electrical simulation,
computational feedback, snippet export, reusable module tooling, collaboration,
and public-repository publishing are not implemented in this milestone.
