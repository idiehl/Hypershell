# Spatial disclosure storage

Spatial disclosure uses optional fields in the existing version 1 project document. Omitted fields retain the previous contained layout and authored positions; saving does not migrate or rearrange existing geometry on the server.

| Object | Field | Accepted values |
| --- | --- | --- |
| Node | `childLayout` | `contained`, `spatial` |
| Node | `placement` | `free`, `near`, `edge` |
| Node | `attachSide` | `auto`, `top`, `right`, `bottom`, `left` |
| Link | `relation` | `causal`, `chronological`, `mutual`, `cyclical`, `generic` |
| Link | `direction` | `forward`, `reverse`, `both`, `none` |
| Library entry | `placement` | `near`, `free` |

These fields store authored presentation and placement preferences. They do not change `parentId` ownership, node identities, dependency rules, reference behavior, or the directed evaluation of computed connections. A node can retain its preferred attachment side while its placement changes. Any node kind can own children, so the host does not limit disclosure settings to structural containers.

`NodeSystems.ValidateNode` applies the same validation to live nodes and saved reference snapshots. `NodeSystems.ValidateLinks` validates relationship and direction enums. Stored module definitions also pass full project validation and use the shared relation list. Native package import and export pass the same project validator, so malformed spatial values are rejected at each persistence boundary.

Library entries remain an independent document. Their optional `placement` preference participates in the existing revision-guarded save and atomic backup. Omitted placement remains valid; the client interprets it as nearby automatic placement. Unknown strings, non-text values, and explicit null values are rejected without replacing the saved hierarchy.

`tests/spatial-host.test.ts` builds an isolated host, uses temporary storage and a random port, and verifies persistence through project saves, snapshots, native packages, module definitions and independent module insertion. It also verifies that invalid settings cannot overwrite saved project or library data. The tests do not use installed app outputs or user collections.
