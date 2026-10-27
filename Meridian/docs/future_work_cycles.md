# Future work cycles

Recorded: 2026-10-24

Status: **Deferred concept and proposed implementation sequence.**

The user shared this direction to preserve the idea before taking Meridian in a
different direction for the immediate future. Implementation remains deferred.
Resume this proposal when the user chooses to return to it. The recommendations
below remain proposals; they do not change the accepted authoring rules, create
standing backlog items, or establish a release commitment.

## 1. The direction the user described

Meridian has reached the basic framework envisioned for the original project.
Future cycles will introduce more node types and expand that foundation. Before
doing so, the export and publication model deserves deliberate consideration:
the Meridian Editor produces artifacts for recipients who may never use the
editor themselves.

A Work can combine several kinds of media and interaction in one space. The
user's example was a Markdown document reader beside a video player, with a CSV
panel and an image Gallery in the same assembly. A presentation could travel
with its accompanying video, spreadsheet or data table, documents, and images.
The useful assembly could be selected, packaged as a cluster or module, and
distributed for a specific purpose.

The recipient would encounter that assembly as its own interface. At publication
time, the author could omit the Work backdrop, grid, editing panels, and other
surrounding furniture. On the desktop, the nodes could appear to be the windows
themselves, without a conventional enclosing application frame.

The underlying opportunity is to let people assemble a custom tool or media
experience for a particular use case without having to develop a separate
application for every arrangement. The editor remains the place where the
experience is composed; publication determines how it is delivered and used.

## 2. Export and publication options

These options preserve the variety of destinations discussed. They are different
presentations of an authored composition, with different interaction and hosting
requirements.

### 2.1 Interactive publication embedded in a website

A whole Work or selected assembly could appear inside atlasuniversalis.com or
another website. Recipients should be able to use the interactions that the
author exposes: reveal and collapse content, read documents, play supported
media, scroll data, page through a Gallery, and follow hyperlinks.

A clean composition mode would allow the author to omit the surrounding viewer
interface and Work background. The host page would provide the surrounding
context. Bounds, sizing, scrolling, optional pan/zoom, and behavior on smaller
screens would need explicit publication settings.

Browser transparency can reveal the webpage behind the composition. Passing
desktop clicks through empty areas is a separate native-host capability.

### 2.2 Standalone interactive web publication

A recipient could open a portable publication in a browser without opening the
Meridian Editor. A small publication could remain a single HTML file containing
its runtime and included media. A larger multimedia publication could use an
asset bundle with a manifest and separate media files.

The conventional full viewer and a clean composition viewer should be available
as presentation choices. Their content and node behavior should use the same
underlying runtime.

Three meanings of “standalone” must remain distinct:

- **Without the editor:** the recipient uses a viewer or published interface.
- **Without Meridian installed:** a supported browser or separately delivered
  runtime opens the artifact.
- **Fully offline:** every required resource is available locally, with no
  undeclared network dependency.

Satisfying one does not automatically satisfy the other two. Included content can
work offline while an ordinary external hyperlink still requires a connection.

### 2.3 A floating desktop composition

The proposed first desktop model is one transparent, frameless native host for
the complete composition. Visually, recipients would see the nodes sitting on
their desktop. The objects could move together, with optional internal
rearrangement governed by the publication's interaction policy.

An OS window would still exist underneath the composition. Removing its visible
title bar and backdrop does not remove its responsibility for focus, input,
positioning, lifetime, and screen boundaries.

This host would need deliberate handling of transparent gaps, click-through hit
regions, dragging, resizing, keyboard navigation, close/manage access, pause or
Stop All access, high-DPI displays, multiple monitors, and saved placement.
Expanding a node must have a defined effect on the enclosing host's bounds so
that new content is not unexpectedly clipped.

Starting with one host for the assembly was the assistant's recommendation. It
remains a proposed implementation choice rather than a finalized desktop-host
specification.

### 2.4 Independent desktop windows for individual nodes

A later host option could let a video, document, data panel, and Gallery occupy
separate positions among other desktop applications. This requires additional
grouping, focus, close/reopen, position-restoration, and lifecycle behavior.

For example, closing one panel would need a clear meaning: hide that panel,
remove it from the recipient's arrangement, or close the entire composition.
Keeping related windows together would also require an explicit interaction
model. The recommendation was to defer this model until a concrete workflow
justifies it.

### 2.5 PDF publication

A Work, section, or selected composition could be rendered as a document suitable
for reading, printing, and sharing. The proposal treats PDF as a deliberate
static rendition of the composition, preserving useful text and links where the
chosen exporter supports them.

PDF export needs decisions about page size, orientation, pagination, scale,
cropping, text readability, and how a large spatial Work is divided into pages.
It also needs a chosen state for collapsed branches, scrolling documents, data
tables, Galleries, and time-based media. General interactive node behavior
belongs to the interactive publication; a PDF export should clearly describe
the representation it produces.

### 2.6 High-resolution PNG and JPG export

Authors could export an entire Work, a selected region, or a chosen assembly as
an image. Output dimensions should be explicit and independent of the editor's
current screen resolution or zoom.

The export needs deliberate selection bounds, padding, background, resolution,
and visible-state choices. PNG can retain transparency when requested. JPG
needs an opaque background. Media should use an intentional poster frame or
selected timestamp, and scrolling or paged content needs an explicit capture
policy. Large images may require tiled rendering to avoid oversized temporary
surfaces.

## 3. Keep the existing grouping concepts distinct

| Concept | Current purpose | Relationship to future publication |
| --- | --- | --- |
| Edge-locked Cluster | Keeps touching objects and their descendants moving as a rigid assembly. | A useful movement group that could be included in an export. |
| Named collapsible Cluster | Groups sibling branches behind a square or circular launcher while retaining their identities, layout, connections, and disclosure states. | A useful author-facing assembly and publication selection boundary. |
| Stored Module | Captures an immutable reusable definition; insertion creates an independent copy with fresh identities. | A foundation for reusable compositions, with additional publication and hosting work still required. |
| Published composition | Proposed distribution and presentation of a selected useful assembly. | Could originate from a Cluster, Module instance, ordinary container, or explicit selection. |

Publishing an assembly should not require inventing a second graph engine or
redefining every Cluster as an application package. Source editing, independent
module reuse, publication, and recipient state are separate operations.

## 4. What the current implementation already provides

The preceding review inspected source code. It did not perform a new release,
deployment, playback test, or cross-device certification.

The verified source-level foundation includes:

- A shared scene renderer used by the editor and published viewer.
- Interactive single-file HTML publication with trusted bundled viewer code,
  allowlisted graph fields, used attachments, and inert JSON data.
- An iframe embed snippet in the publication interface.
- Selected-node extraction with choices for descendants and available saved
  references, preserved internal connections, inherited rules, and independent
  identities for the extracted copy.
- Local immutable module definitions and independent insertion into Works.
- Explicit dependency reporting, including missing or excluded context.
- Markdown, text, CSV/TSV, and PDF reader paths, supported video media, Gallery
  paging, Window scrolling, disclosure, and exposed supported runtime controls.
- Separation between the publication's authored data and a runtime copy that
  recipients interact with.

Current boundaries must remain visible in future planning:

- The published viewer currently constructs its own header, controls, selection
  tools, and footer. A clean composition publication option is additional work.
- Current MyNet application hosting provides a custom application frame around
  a rectangular WebView. The inspected code does not establish a transparent,
  click-through Meridian composition host.
- Product-facing PDF and high-resolution PNG/JPG export were not found in the
  inspected editor/viewer/host paths. Existing authoring and QA screenshots do
  not establish those end-user export features.
- A CSV table reader is not a full editable spreadsheet application. Spreadsheet
  editing and calculations would require the corresponding runtime features.
- Browser PDF support and supported media codecs affect portability. Supporting
  a container format does not guarantee every possible embedded codec.
- Available saved reference observations do not reproduce an external Work's
  complete operating context. Arbitrary external context is not recursively
  fetched and bundled today.
- Module definitions are not currently live-linked, automatically updating
  installations or a versioned distribution system.

## 5. Architectural recommendations

### 5.1 One composition, multiple presentation hosts

Preserve a shared authored graph, content model, and runtime. Provide host
adapters for the editor, conventional viewer, clean web/embed composition,
static-output capture, and eventually a desktop composition.

Presentation settings should determine entry roots, initial bounds, backdrop,
surrounding controls, navigation, pan/zoom, permitted movement, and download or
external-link behavior. A shared mount/update/dispose interface would let hosts
use the same content components without requiring the full editor interface.

A portable manifest could record the entry assembly, layout, asset inventory,
dependencies, supported node/runtime versions, and required capabilities. Its
exact schema and relationship to existing publication metadata remain open.
Preserve existing graph and package compatibility deliberately.

### 5.2 Stable node lifecycle and simultaneous media

The inspected scene renderer clears and rebuilds the scene DOM during a scene
render. Video elements are constructed afresh. Viewer disclosure, input, and
Gallery actions can trigger rendering. The review found no corresponding
retention of video playback position, play/pause, or volume across those
rebuilds.

This creates an interruption risk for the proposed simultaneous-media use case.
It is a source-level finding, not a measured playback regression result.

Before promising a rich composition, establish stable node instances and a
lifecycle for initialization, updates, suspension, and disposal. Unrelated node
changes should preserve playback, focus, text selection, scroll position, and
other applicable session state. Cleanup should release media, object URLs,
listeners, and computational resources when an instance is actually removed.

### 5.3 Scalable asset packaging

Keep small self-contained HTML publications as a convenient option. Add a bundle
path for larger media: a manifest plus separate, verifiable media files, with
deduplication and loading on demand. Consider streaming or random access for
time-based media and avoid copying or decoding every attachment before it is
needed.

The source-review snapshot recorded these current constraints:

| Constraint | Observed boundary |
| --- | --- |
| Host request size | 100 MiB |
| Expanded native package | 80 MiB |
| Individual attachment | 16 MiB |
| Total reconstructed attachment data | 64 MiB |
| Distinct native-package attachments | 1,023 |

These are dated implementation facts, not proposed permanent product limits.
Current publication JSON embeds assets as base64, and the viewer clones authored
and runtime project data. Substantial video can quickly make these boundaries
restrictive. Increasing limits alone would leave loading and memory costs
unresolved.

### 5.4 Separate authored definition from recipient state

The authored definition contains content, media, connections, initial layout,
initial disclosure, and declared dependencies. Recipient instance state may
contain playback position, selected Gallery item, document scroll position,
input values, local rearrangement, collapsed panels, and desktop placement.

Define which recipient changes are temporary, locally persistent, or explicitly
exportable. They must not silently modify the author's source. Decide whether
instances share state or remain independent, and how an updated publication
would interact with previously saved recipient state.

The current viewer already uses a runtime clone. Its whole-publication Save a
copy action retains the authored payload. Selected-copy downloads can retain
explored geometry and disclosure while omitting temporary simulation results.
These existing distinctions should be preserved or changed explicitly.

### 5.5 Publication dependencies and capabilities

When a selection refers outside its boundary, proposed policies include:

- Bundle the necessary context when it is available and permitted.
- Freeze an explicitly identified saved observation.
- Retain a declared live connection with an explicit network/capability
  requirement.

These are policy choices; all three are not implemented today. A saved
observation must not be presented as a complete functioning source system, and
cross-boundary functional connections must not acquire fabricated behavior.

Preflight should report missing requirements, unsupported node capabilities,
network-required resources, and substitutions made for static output. Declaring
a dependency must not itself grant access, fetch arbitrary content, or execute
code. Preserve the current boundary between inert authored data and trusted
runtime behavior.

## 6. Proposed contract for future node types

Before introducing each new type, specify:

1. **Saved content:** serialization, schema/runtime version expectations,
   TypeScript validation, host validation, and publication allowlisting.
2. **Assets and dependencies:** resources it needs, how they travel, and which
   external requirements remain after extraction.
3. **Published behavior:** recipient interactions, exposed controls, supported
   inputs/outputs, and the distinction between viewing and authoring.
4. **Lifecycle and state:** initialization, updates, suspension, cleanup, and
   state that survives unrelated changes or reopening.
5. **Host capabilities:** supported environments, required permissions, network
   assumptions, and a clear fallback when a capability is unavailable.
6. **Static representation:** what PDF/image export captures, including media
   poster frames, document pages, table rows, and Gallery items.
7. **Accessible operation:** keyboard/focus behavior, usable sizing, reduced
   motion where appropriate, and accessible pause/stop/close controls.

This is a proposed engineering contract for future implementation. It has not
been added to the accepted AI-authoring rulebook.

## 7. Proposed work-cycle sequence

The following preserves the four-cycle proposition from the discussion. The
sequence is deferred and can be revised when this direction resumes. It is not
a calendar schedule or an instruction to begin immediately.

### Cycle 1: Portability foundation

Establish the composition/presentation contract, node lifecycle, recipient state,
dependency policies, and larger-media package path. Define a shared runtime-host
interface and deterministic state/capture hooks. Preserve the existing graph
engine and renderer rather than creating separate implementations for each
destination.

Expected outcome: an agreed foundation and a working demonstration that node
state survives unrelated interactions, with bounded resource use and explicit
dependency handling.

### Cycle 2: Clean composition publication

Build selected-composition publication using the document + video + CSV + Gallery
example. Provide intentional bounds, fit/scroll behavior, configurable viewer
controls, and an optional transparent or absent Work backdrop. Preserve internal
connections, disclosure, media, and useful relative layout.

Use the same runtime for a clean standalone web presentation and a website embed.
Exercise the larger-media bundle path established in Cycle 1. Verify supported
offline behavior and visible handling of network-required content.

Expected outcome: a recipient can open and use the selected assembly without
entering the editing environment.

### Cycle 3: Deliberate PDF and image rendition

Implement explicit capture state, export bounds, output resolution, background,
and PDF pagination. Decide how to represent collapsed branches, scrolled or paged
documents, table content, Galleries, and time-based media. Keep authored source
and current recipient state protected throughout capture.

Expected outcome: repeatable PDF and high-resolution PNG/JPG outputs with
predictable content, rather than a screenshot whose quality depends on the
author's current viewport.

### Cycle 4: Floating desktop host

Reuse the composition manifest and runtime in one native host for the assembly.
Implement and test transparency, empty-gap hit testing, movement, resizing,
keyboard focus, accessible management/close controls, pause/Stop All, and saved
screen placement.

Expected outcome: the nodes can appear as a grouped desktop experience without
conventional surrounding window furniture. Independent windows for individual
nodes remain a possible later cycle, driven by a concrete use case.

## 8. Proposed acceptance example and verification

The first acceptance example should be simple and demanding:

> Play the video continuously while opening the document, scrolling the table,
> paging the Gallery, and collapsing another panel. Then reopen the package on a
> second device with its dependencies accounted for.

Useful verification includes:

- Selected extraction and reimport preserve intended descendants, internal
  links, layout, media, inherited rules, and independence from the source.
- Publication and recipient interactions leave the authored source unchanged.
- Missing external context and unsupported capabilities remain visible.
- Offline tests include required assets; network-dependent content behaves as
  declared rather than failing silently.
- Document, video, table, and Gallery interactions work together without losing
  unrelated node state.
- The same composition is checked in the full viewer, clean presentation, and
  embed, including smaller viewports and high-DPI displays.
- Disposal releases active media and other resources.
- Static outputs use the selected state, dimensions, resolution, and pages.
- Desktop checks cover transparent gaps, click-through, deliberate drag regions,
  keyboard focus, multiple monitors, display scaling, close/reopen, and saved
  position.

## 9. Decisions to revisit when the proposal resumes

- Which publication modes should ship first, and what does each promise about
  installation and offline operation?
- Should the default preserve exact authored geometry, fit the composition, or
  permit responsive rearrangement? How does the author choose?
- Which controls can the recipient use, and may they rearrange the composition?
- Which recipient state persists, where is it stored, and can it be shared?
- What happens to required context outside the selection: bundle, snapshot,
  declared live connection, or a blocking preflight result?
- How are package/runtime versions and unsupported node types handled?
- What do PDF and image exports capture for scrolling, paged, collapsed, or
  time-based content?
- For desktop publication, what is the distribution/runtime requirement and
  which lifecycle and click-through behavior is the first supported contract?
- Does a future update replace a publication, create an independent version, or
  migrate recipient state? Current immutable Modules do not settle that policy.

## 10. Source references and related planning

Implementation findings are a snapshot of the source review associated with this
discussion. Recheck them when work resumes; they are not a claim that every host
or interaction has been tested.

- [Publication payload and HTML builder](../src/publication.ts)
- [Published viewer and authored/runtime state](../src/viewer.ts)
- [Shared scene and media lifecycle](../src/scene.ts)
- [Selection extraction](../src/core/selection.ts)
- [Module extraction and insertion](../src/core/modules.ts)
- [Disclosure Clusters](../src/core/disclosure-clusters.ts)
- [Module and Cluster concepts](MODULES.md)
- [Dependency reporting](../src/core/dependencies.ts)
- [Document preview support](../src/core/document-preview.ts)
- [Host validation limits](../src/host/ProjectValidator.cs)
- [Native package handling](../src/host/ProjectPackage.cs)
- [Existing MyNet application host](../../Hypershell/src/Hypershell/MyNetAppWindow.xaml)
- [Earlier deferred website/account/library/collaboration direction](DEFERRED-WEB-EMBEDDING-AND-ONLINE-HUB.md)

The earlier website and online-hub proposal remains a related, separately
deferred direction. Preserving this composition/publication idea does not resume
website deployment, account systems, online storage, or collaboration work.
