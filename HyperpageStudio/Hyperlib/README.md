# HyperLib

A local library of living pages. Hyperpage Studio owns the design and code; HyperLib owns the content.

## Open the application

Open **Hypershell** from the Start menu or run `Hypershell` in a terminal, then choose **MyNet → Apps → HyperLib**. The stable launcher uses the current published app with your normal Hypershell settings. MyNet starts and supervises the local host.

For the separate preview profile, run `./Open-HyperLib-Hypershell.ps1`. It uses the same stable launcher while retaining the preview settings at `I:\dev\.codex-build\hyperlib-authoring\native-profile`. Choose **Continue without signing in** if prompted, then **MyNet → Apps → HyperLib**. AI Access and the AI Context must be configured in that profile before using its assistant. HyperLib also works in a normal browser on this computer:

```powershell
./Start-HyperLib.ps1 -OpenBrowser
```

The default address is `http://127.0.0.1:54180`. Pass `-Port` to choose another port. The .NET 10 SDK is needed to compile new or changed work designs. Editing existing page content does not compile the work.

Use `./Stop-HyperLib.ps1` to stop a standalone published host. When switching from standalone use to native integration, finish saving and stop the standalone host before opening HyperLib through MyNet. Native capabilities are granted only when MyNet launches and supervises this library.

## Read, write, and organize

- Start on the collection overview. Every page belongs to a work.
- Toggle **Edit** in the header to add content and reveal the authoring panel.
- Click text on a page to write directly. Select an element to expose its typed properties, media controls, and ordering controls. Selection stays visible while you edit its properties.
- Add elements from the side **Elements** panel. Headers, banners, and footers are ordinary elements: select, edit, reorder, remove, or add them through the active template.
- Template-exposed properties include font, alignment, text color, and full, half, one-third, or two-thirds width. Unset style fields inherit the template design.
- Changes save locally. **Undo** and **Redo** sit beside the upper appearance controls; keyboard shortcuts remain available. Pending inline text is captured before navigation or an undo operation.
- Use **Appearance** for template-exposed design choices, including the whole-page background. Use **Organize** to rename, reorder, move within a work, or move a whole work between collections/series.
- Deletion is recoverable through undo. A stale editor reports a conflict and preserves its unsaved draft; it never silently overwrites another editor.
- Library, Collection, Series, Work, Volume, Part, Chapter, Page are structural types. **Customize names** changes their display labels without changing their meaning.

Optional levels can be skipped. The smallest structure is Library → Collection → Work → Page. Page transfers between different work templates are intentionally not provided.

## Base Template Library

Creating a **Work** opens a chooser with searchable previews, descriptions, and category filters. All 17 starters can be used for any subject: the two original designs, **Woodland Editorial** and **Wave Laboratory**, plus 15 additional designs:

Portfolio Grid, Field Journal, Architecture Studio, Course Notebook, Recipe Kitchen, Event Poster, Travel Atlas, Product Launch, Podcast Studio, Gallery Wall, Documentation Desk, Nonprofit Story, Wellness Retreat, Maker Workbench, and Insight Dashboard.

The **Base Template Library** collection contains 15 demonstration works, one for each additional design. The two original examples remain in their original collections. Demonstrations include text, images, forms, maps, embeds, media, and interactive tools. Forms show a local confirmation without sending or storing entries; maps and web embeds load only when activated.

Each new work receives its own copied source and authoring schema. **Add Page** starts from that work's current default blocks. Edit the work's default composition in Studio to control how its future pages begin; existing page content remains independent.

## Hyperpage Studio

Open this directory with Studio’s **Open** command, or use **Open in Studio** from HyperLib in Hypershell. The root opens the library presentation; each linked work opens its own Studio project.

Studio’s **HyperLib** panel shows linked projects and opens the **Authoring contract** editor. Define reusable blocks, compiled component names, editable fields and defaults, insertion regions, allowed element types, and exposed appearance controls there. Normal Studio code documents remain available for HTML, CSS, JavaScript, Razor and C#.

The root project uses `Presentation/authoring.template.json`. Each work uses `Works/<work-id>/authoring.template.json`. Templates use stable IDs so renaming a displayed field does not lose its content. Removing a field that contains values, removing a used block, or changing a field incompatibly prevents activation until resolved in Studio.

Reusable starters live at `BaseTemplates/<starter-id>/Project`. Open that folder as a Studio project to edit the reusable design. **`Project/authoring.template.json` is the canonical reusable schema**, including appearance defaults and default blocks; the adjacent top-level `authoring.template.json` and `default-page.json` are shipped reference snapshots. New works copy the canonical project. Existing `Works/<work-id>` projects keep their independent source and schemas when a reusable starter changes.

A valid saved template/code change automatically builds and activates a new rendering generation. The editor and content store remain running. A failed build retains the last working generation and reports diagnostics. Content and drafts survive refreshes; transient widget state may reset.

## Source organization

| Location | Contents |
|---|---|
| `src/HyperLib.Host` | Stable local HTTP host and plain HTML/CSS/JavaScript authoring interface |
| `src/HyperLib.Core` | Content contracts, validation, durable operations, undo/redo and recovery |
| `src/HyperLib.Runtime` | Renderer supervision, component SDK and frame/editor communication |
| `src/Host`, `src/Pages`, `Pages`, `Shared`, `Presentation` | Root Studio presentation project |
| `Works/<id>` | Independent Studio project, code and authoring contract for each work |
| `BaseTemplates/<id>/Project` | Canonical reusable Studio project, schema, appearance defaults and page composition for a starter |
| `BaseTemplates/<id>/authoring.template.json`, `default-page.json` | Shipped reference snapshots of each starter |
| `Starters/standard` | Shared source used to scaffold the reusable starter projects |
| `Content/library.json` | Portable snapshot of the library |
| `Library` | Directory hierarchy and readable page/document projections |
| `Media` | Local imported media and bundled demonstration assets |
| `.hyperlib/journal` | Durable content transactions and recovery history |
| `.hyperlib/runtime` | Isolated source snapshots, compiled generations and last-working pointers |

Copy the complete folder to relocate a library. Retain `.hyperlib/journal` to preserve its history. If only the portable snapshot is present, the store initializes a new journal from that snapshot. Do not edit hierarchy projections while the host is running: committed journal records are authoritative and projections are repaired from them. Use the application or content API for content edits.

The runtime trusts Studio-authored local code. Frames separate presentation lifecycles; they are not security sandboxes for downloaded code. API keys remain in Hypershell’s device-protected credential store and are never placed in this folder or sent to page scripts.

## Assistant

Inside Hypershell, the assistant uses your configured AI provider and current internet/cloud settings. Its tools can inspect the library and active schemas, list the reusable base templates, select a `starterId` for a new work, change content in validated batches, fetch sources, and use OpenAI hosted web search when supported. It preserves existing page chrome metadata when saving content. It cannot run terminal commands or edit template source.

Requests to change content apply directly with undo. Source evidence is stored with page content, including provider-returned URLs and citation associations. Local models can write and organize without cloud access; hosted search is unavailable when unsupported or disabled. No provider is silently substituted.

Outside Hypershell, all local reading and editing remain available. The assistant and native Studio-opening controls are unavailable.

## HTTP contracts

- `GET /api/state`: complete library snapshot and revision.
- `GET /api/templates/{workId|library}`: active authoring schema.
- `GET /api/base-templates`: reusable starter IDs, descriptions, categories, and preview colors.
- `POST /api/operations`: `{ expectedRevision, description, operations }`; commits the entire validated batch or nothing.
- `POST /api/undo`, `POST /api/redo`: `{ expectedRevision }`.
- `POST /api/media`: multipart `file`, with MIME/signature and size validation.
- `GET /api/runtime/{packageId}`: on-demand renderer generation; `GET /api/runtime/status` supplies diagnostics.
- `GET /api/events`: content and runtime event stream.

Writes require `X-HyperLib-Client: editor`; browser-origin checks reject cross-origin mutations. Content operations are `createNode`, `updateNode`, `moveNode`, `deleteNode`, `savePage`, `setAppearance`, and `setLabels`. A work's `createNode` accepts a catalog `starterId`; creating a page without explicit content clones its owning work's active default blocks. Read that generated page before editing it, and retain its `usesEditableChrome`, full block content, and page revision in a subsequent `savePage`. Source-code mutations are not content operations. See Core’s README for revision and recovery details.

## Verification

```powershell
dotnet run --project tests/HyperLib.Core.Tests -c Release
dotnet build src/HyperLib.Host -c Release
dotnet run --project tests/HyperLib.Runtime.Verifier -- .
dotnet run --project tests/HyperLib.Http.Tests -- http://127.0.0.1:54180
node --test tests/frontend-editor.test.cjs
```

The two original seeded pages demonstrate minimal and full hierarchies, distinct work designs, local media, typed blocks, citations, JavaScript lifecycle, and C# interactivity. The 15 additional demonstration pages exercise the reusable base-template collection. `tools/create-demo-media.py` reproduces the bundled audio/video fixtures; these assets require no external service at runtime.

See `VERIFICATION.md` for the implementation checks and the remaining live-provider verification boundary.
