# Typed node systems

This pass extends the shared editor and standalone viewer with finite numbers,
arithmetic and comparison joints, and explicit junctions. It implements a bounded
next step toward the handoff's input/link/joint distinction and its ordinary
calculation behavior. Audio, continuous signals, electric circuit simulation,
arbitrary code execution and feedback scheduling remain outside this pass.

## Roles

| Object | Purpose |
| --- | --- |
| Boolean input | An authored On/Off value, optionally adjustable by a viewer. |
| Number | A finite numeric operand, optionally adjustable by a viewer. |
| Joint | An explicitly selected operation on typed inputs. Its symbol and input names remain visible. |
| Switch | Passes one Boolean or numeric input when closed; opening produces undefined. |
| Output | Displays one Boolean or numeric result. It remains an ordinary node and can be a sibling of a collapsed calculation container. |
| Junction | A deliberately inserted route point. A typed junction passes one incoming value to any compatible outgoing links; it does not silently combine several values. An untyped junction is visual only. |
| Reference | Observes an original in its existing context. Same-canvas sources are live; other canvases use a saved snapshot. References never provide a write-through numeric control. |

Visual links describe relationships and perform no computation. Existing
`functional` links carry Boolean values; `numeric` links carry finite numbers.
Boolean/number conversion is never implicit. Crossing routes stay independent
until an explicit junction is inserted. A routing junction differs from an
operation joint: merging operands into an arithmetic or Boolean result requires
the chosen joint operation.

## Operations

`src/core/operations.ts` is the common registry used by validation, evaluation and
the renderer. It supplies names, symbols, port names and value types.

- Boolean: AND, OR, NOT, NAND, NOR, XOR and XNOR.
- Arithmetic: add, subtract, multiply, divide, remainder, power, minimum, maximum,
  absolute value and negate.
- Comparisons: numeric equality, greater than and less than; each produces a
  Boolean result.
- Routing: identity, configured for a Boolean or number input.

The first and second inputs are named A and B. Single-input destinations use In.
Port labels follow authored attachment anchors, or default to separate positions
on the receiving node's left edge. The link inspector's input selection identifies
the actual target port; visual proximity does not select or combine operands.

## Reading and interacting

Numeric controls accept a finite value when Enter is pressed or the field loses
focus. This preserves the user's incomplete typing while they enter a negative
number, decimal or exponent. A committed change immediately reevaluates a running
system. Fixed controls are disabled in the viewer and identified by their tooltip.

Numeric links use violet, Boolean activity retains the existing mint/gray colors,
and undefined results are amber. Outputs display `Unknown` for undefined values;
an open switch, missing operand, invalid operation result or division by zero does
not leave the last result displayed or substitute zero. Numeric readouts show up
to twelve significant digits; their tooltip retains the complete JavaScript
number. This formatting does not round the stored operand or calculation.

Junction dots identify actual connections. Hovering, selecting or focusing a dot
reveals its name and current state. The editor can insert a junction into a selected
connection. Visual route tools can explicitly join a chosen source to an existing
visual segment. These actions create nodes and links; neither visual intersection
nor shared coordinates creates a connection automatically.

Collapsed ancestors hide their descendants' display only. Hidden calculations
continue while running, and visible sibling outputs continue displaying their
results. Expanded text-bearing containers reserve a summary area above their
first direct child. Their Read description control opens the complete original
text without moving or changing any authored node.

## Publication and reuse

The viewer starts stopped and runs only after its Run action. Exposed numeric and
Boolean controls affect a runtime copy. Stop clears displayed runtime activity;
Reset restores authored settings and returns to stopped. Downloading a publication
retains the original authored payload, never the viewer's temporary inputs.

Number values, value types, operations and numeric routes are portable document
data. Saved snapshots preserve numeric results. Module creation and independent
insertion preserve these fields under the existing remapping and inherited-rule
checks. Required external context is still disclosed; this pass does not fetch
outside sources or reinterpret the handoff's unresolved variable-identity policy.

## Validation

`tests/systems.browser.ts` covers editable operands and comparisons, a typed
junction inserted through the inspector, open-switch propagation, hidden
calculations with sibling outputs, non-overlapping container summaries, isolated
save/reopen, numeric module retention, offline publication, transient controls,
division by zero, pinned numeric references, explicit visual segment joining and
toolbar accessibility at 1500- and 1280-pixel widths. Browser verification must follow
a fresh build so editor and viewer assets match the source.
