/** Native tutorial fixtures. These declarations contain data only; no embedded HTML,
 * custom scripts, or simulated controls. The caller owns composition and saving. */
import { OPERATION_DEFINITIONS, applyOperation } from '../../src/core/operations.ts';

export function addTutorialLabs(api) {
  const { recipe, add, theme = {}, workIds = {} } = api;
  const w = key => workIds[key] ?? key;
  const key = (work, local) => `${w(work)}/${local}`;
  const notes = [], fixtures = [], expected = [];
  const full = (work, local, title, text, x, y, width, height, extra = {}) => {
    const id = add(w(work), local, title, text, x, y, width, height, extra);
    fixtures.push(id); return id;
  };
  const heading = (work, local, title, text, x, y, width = 1200, height = 120) =>
    full(work, local, title, text, x, y, width, height, { style: { variant: 'label', shape: 'none', borderWidth: 0, padding: 0, titleSize: 28, fontSize: 18 } });
  const note = (work, local, title, text, tryIt, featureIds) => notes.push({ work: w(work), key: local, title, text, tryIt, featureIds });
  const wire = (work, local, source, target, extra = {}) => {
    recipe.links.push({ key: key(work, local), canvas: w(work), source, target,
      kind: 'visual', relation: 'generic', direction: 'forward',
      style: { color: theme.line ?? '#71AAA7', width: 2, lineStyle: 'solid' }, ...extra });
  };
  const block = (work, local, x, y, width, height, pattern = 'contained', extra = {}) =>
    full(work, local, '', '', x, y, width, height, {
      kind: 'container', tag: `Tutorial · ${local}`, collapsed: false, containerVariant: pattern,
      childLayout: 'contained', childArrangement: pattern === 'horizontal' ? 'row' : pattern === 'vertical' ? 'column' : ['grid', 'spreadsheet', 'window'].includes(pattern) ? 'grid' : 'free', sizeMode: 'fixed',
      containerSizes: { expanded: { width, height, sizeMode: 'fixed' }, collapsed: { width: 320, height: 190, sizeMode: 'fixed' } },
      style: { fill: theme.soft ?? '#122D35', childPadding: 8, padding: 8, radius: 0, shape: 'rectangle' }, ...extra,
    });
  const child = (work, local, parent, title, text, x, y, width, height, extra = {}) =>
    full(work, local, title, text, x, y, width, height, { parent, placement: 'free', childPosition: 'manual', style: { titleSize: 23, fontSize: 18, padding: 12 }, ...extra });
  const fixed = (width, height) => ({ expanded: { width, height, sizeMode: 'fixed' }, collapsed: { width: 320, height: 190, sizeMode: 'fixed' } });

  // PATTERNS — five arrangements, nested interiors, and a true exterior tray.
  heading('patterns', 'freeform-heading', 'Freeform · authored space', 'Unequal objects retain an intentional composition. A named node can have an empty interior.', 800, 360, 1050);
  const free = block('patterns', 'freeform', 800, 500, 1050, 450);
  child('patterns', 'freeform-a', free, 'Focal idea', 'This larger tile establishes the local hierarchy.', 830, 535, 450, 290);
  child('patterns', 'freeform-b', free, 'Supporting detail', 'A narrower companion preserves its own proportions.', 1320, 535, 480, 170);
  child('patterns', 'freeform-empty', free, '', '', 1400, 745, 290, 150, { tag: 'Tutorial · Empty named shape', style: { fill: theme.accent ?? '#7BCAC4', backgroundOpacity: .25, borderWidth: 2 } });
  heading('patterns', 'horizontal-heading', 'Horizontal · one row', 'Three equal peers divide the available width and fill the interior height.', 2050, 360, 1050);
  const horizontal = block('patterns', 'horizontal', 2050, 500, 1050, 450, 'horizontal', { grid: { columns: 3, rows: 1, cellWidth: 340, cellHeight: 430, gap: 0, showHeaders: false, fillOrder: 'row' } });
  ['Before', 'During', 'After'].forEach((title, i) => child('patterns', `horizontal-${i + 1}`, horizontal, title, ['Establish context.', 'Follow the action.', 'Review the outcome.'][i], 2060 + i * 343, 510, 343, 430, { cell: { column: i + 1, row: 1 } }));
  heading('patterns', 'vertical-heading', 'Vertical · one column', 'Equal tiles create a downward reading path.', 800, 1040, 550);
  const vertical = block('patterns', 'vertical', 800, 1180, 550, 740, 'vertical', { grid: { columns: 1, rows: 3, cellWidth: 530, cellHeight: 240, gap: 0, showHeaders: false, fillOrder: 'column' } });
  ['Observe', 'Explain', 'Apply'].forEach((title, i) => child('patterns', `vertical-${i + 1}`, vertical, title, ['Identify the evidence.', 'Connect the evidence to an idea.', 'Test the idea in another case.'][i], 810, 1190 + i * 240, 530, 240, { cell: { column: 1, row: i + 1 } }));
  heading('patterns', 'grid-heading', 'Grid · comparable peers', 'Four objects form a 2 × 2 set. Grid capacity rounds up to a square.', 1500, 1040, 1595);
  const grid = block('patterns', 'grid', 1500, 1180, 1595, 740, 'grid', { grid: { columns: 2, rows: 2, cellWidth: 785, cellHeight: 360, gap: 0, showHeaders: false, fillOrder: 'row' } });
  ['Compare A', 'Compare B', 'Compare C', 'Compare D'].forEach((title, i) => child('patterns', `grid-${i + 1}`, grid, title, 'Same visual allocation; a different item. Row fill order proceeds across, then down.', 1510 + (i % 2) * 787, 1190 + Math.floor(i / 2) * 360, 787, 360, { cell: { column: i % 2 + 1, row: Math.floor(i / 2) + 1 } }));
  heading('patterns', 'nested-heading', 'Inside or outside? · two kinds of disclosure', 'Open the bounded child inside its own frame. Open the Media child into an exterior Expansion Tray.', 800, 2040, 2295);
  const outer = block('patterns', 'disclosure-parent', 800, 2200, 2295, 500, 'horizontal', { grid: { columns: 3, rows: 1, cellWidth: 750, cellHeight: 470, gap: 0, showHeaders: false, fillOrder: 'row' } });
  const bounded = child('patterns', 'bounded-child', outer, 'Bounded child', 'Double-click the backdrop. My descendants belong inside my own border.', 810, 2210, 750, 480, { kind: 'container', collapsed: true, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', cell: { column: 1, row: 1 }, containerSizes: fixed(750, 480) });
  child('patterns', 'bounded-descendant', bounded, 'Original inner node', 'Reopening reveals this same object, with the same GUID.', 835, 2240, 700, 410);
  const exterior = child('patterns', 'exterior-child', outer, 'Unbounded Media child', 'Double-click my backdrop. My two original descendants open outside this outer enclosure.', 1570, 2210, 750, 480, { kind: 'media', collapsed: true, containerVariant: 'horizontal', childLayout: 'spatial', cell: { column: 2, row: 1 }, grid: { columns: 2, rows: 1, cellWidth: 360, cellHeight: 300, gap: 0, showHeaders: false }, expansionTray: { offset: { x: -40, y: 600 }, size: { width: 1000, height: 400 }, layoutVersion: 3 } });
  child('patterns', 'tray-original-1', exterior, 'First original', 'The tray is presentation, not a proxy or duplicate.', 1570, 2810, 460, 350, { cell: { column: 1, row: 1 } });
  const deep = child('patterns', 'tray-original-2', exterior, 'Nested bounded branch', 'I can expand again inside the tray.', 2030, 2810, 460, 350, { kind: 'container', collapsed: true, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', cell: { column: 2, row: 1 }, containerSizes: fixed(460, 350) });
  child('patterns', 'tray-nested-original', deep, 'One more generation', 'There is no fixed authoring limit of two generations. The hierarchy must remain acyclic and valid.', 2050, 2840, 420, 290);
  child('patterns', 'disclosure-context', outer, 'A stable peer', 'My tile remains where the ordered parent allocates it while a neighboring tray is explored.', 2330, 2210, 750, 480, { cell: { column: 3, row: 1 } });
  note('patterns', 'pattern-choice', 'Role and Display Pattern are independent', 'Container means bounded children; Media means exterior children while its own content remains visible. Freeform preserves unequal geometry. Horizontal, Vertical and Grid allocate uniform tiles. A Work portal, reference or functional node may also own children.', 'Inspect each parent in Properties. In Single Select, double-click a node to collapse or reopen it, and drag its backdrop or text to move the branch. Choose Text only to edit words; no hover name strip is needed.', ['object.container', 'object.media', 'pattern.freeform', 'pattern.horizontal', 'pattern.vertical', 'pattern.grid', 'disclosure.nested', 'disclosure.frames']);
  note('patterns', 'tray-practice', 'The tray carries the originals', 'An unbounded child inside a bounded ancestor discloses into a detached surface with one visual tether. The tray remembers source-relative position and size; descendants retain identity, parents and individual expansion states. Resizing scales their presentation to fit.', 'Open Unbounded Media child. Open Nested bounded branch inside it. Move and resize the tray, close the outer parent, then reopen. Inspect the same remembered arrangement. Drag close to the outer border to test edge snapping.', ['disclosure.tray', 'disclosure.tray-size', 'disclosure.tray-snap', 'disclosure.state']);

  // TABLES — addressable cells, a clipped native Window and cell references.
  heading('tables', 'spreadsheet-heading', 'Spreadsheet · explicit addresses', 'Cells contain ordinary nodes. Names and coordinates are local to this parent, rather than replacements for GUIDs.', 800, 360, 1280);
  const sheet = block('tables', 'addressable-sheet', 800, 520, 1280, 620, 'spreadsheet', { kind: 'spreadsheet', grid: { columns: 2, rows: 2, cellWidth: 620, cellHeight: 290, gap: 0, showHeaders: false, fillOrder: 'row' } });
  child('tables', 'cell-context', sheet, '1:1 · Context', 'A text entry can live in a cell.', 810, 530, 620, 290, { cell: { column: 1, row: 1, name: 'context' } });
  const cellNumber = child('tables', 'cell-quantity', sheet, '2:1 · Quantity', '', 1440, 530, 620, 290, { kind: 'number', value: 7, valueType: 'number', exposed: true, cell: { column: 2, row: 1, name: 'quantity' } });
  child('tables', 'cell-practice', sheet, '1:2 · Practice', 'Select the numeric cell, then inspect its address and name in Relations.', 810, 830, 620, 290, { cell: { column: 1, row: 2, name: 'practice' } });
  child('tables', 'cell-caution', sheet, '2:2 · Boundary', 'This is an addressable node surface. It is not an Excel formula engine or an XLSX editor.', 1440, 830, 620, 290, { cell: { column: 2, row: 2, name: 'boundary' } });
  heading('tables', 'window-heading', 'Window · a viewport over a larger set', 'Only two rows fit at once. Scroll to reach the last address without expanding the Work footprint.', 2250, 360, 1000);
  const window = block('tables', 'reading-window', 2250, 520, 1000, 1080, 'window', { grid: { columns: 1, rows: 6, cellWidth: 960, cellHeight: 490, gap: 0, showHeaders: false, fillOrder: 'row', windowColumns: 1, windowRows: 2, scrollDirection: 'vertical' } });
  const reading = [
    ['1:1 · Start at the visible surface', 'A Window shows a bounded portion of a larger ordered child workspace. Its scroll position is local viewing state. Every entry remains an ordinary node with its own GUID.'],
    ['1:2 · Keep text readable', 'Window is useful for registers, ordered excerpts and many small records. It scrolls the child surface. It does not automatically add body-text scrolling to arbitrary text in one oversized node.'],
    ['1:3 · Full axes and viewport', 'Full columns and rows set the address space; visible columns and rows set the viewport. Vertical or horizontal direction determines the primary scrolling axis.'],
    ['1:4 · Shared tile allocation', 'All ordered child tiles use the parent’s common cell dimensions and shape. Freeform is the choice when unequal sizes are part of the composition.'],
    ['1:5 · Reach a hidden record', 'Search a child GUID or Tag and use its target action. Meridian can reveal the ancestors and activate or scroll to the containing ordered surface.'],
    ['1:6 · You reached the last row', 'This final entry is deliberately outside the initial viewport. It is a useful save/reopen and publication check: its complete text must remain reachable.'],
  ];
  reading.forEach(([title, text], i) => child('tables', `window-row-${i + 1}`, window, title, text, 2260, 530 + i * 500, 960, 490, { cell: { column: 1, row: i + 1, name: `record_${i + 1}` } }));
  heading('tables', 'cell-reference-heading', 'Read by identity, address or name', 'These same-Work references observe the live numeric cell. Editing a reference never writes through to its original.', 800, 1260, 1280);
  const localRef = full('tables', 'cell-ref-name', 'Named cell · quantity', '', 800, 1420, 580, 270, { kind: 'reference', target: sheet, targetCell: 'quantity', valueType: 'number' });
  const coordinateRef = full('tables', 'cell-ref-coordinate', 'Address · 2:1', '', 1500, 1420, 580, 270, { kind: 'reference', target: sheet, targetCell: '2:1', valueType: 'number' });
  expected.push({ kind: 'same-work-reference', source: cellNumber, references: [localRef, coordinateRef], value: 7 });
  note('tables', 'cells', 'Spreadsheet is a native node arrangement', 'A Spreadsheet Display Pattern contains real nodes with addresses such as 1:1 and optional unique names. References follow a cell name, address, or original GUID. The Original CSV attachment below is a different object: Read document opens its complete source table without creating cell nodes. Its reader pages 50 rows at a time, exposes every column through scrolling and column pages, and searches all rows locally. It is not subject to the Spreadsheet grid dimensions. First row is a header can be changed; Download original retains the complete bytes.', 'Run and change Quantity from 7 to 12; both cell References should show 12. Then read the original CSV, search for Pivot, toggle its header option, and download it. Compare reading a source file with editing native cells.', ['pattern.spreadsheet', 'cell.coordinate', 'cell.name', 'reference.cell', 'object.spreadsheet']);
  note('tables', 'window', 'Window clips a larger child workspace', 'The visible viewport and full address space are different. Offscreen entries retain their addresses and identities. A Window can also be unbounded, creating an exterior clipped child surface; the example here is bounded.', 'Move the pointer into the Window and scroll until row 1:6 appears. Search its exact Tag and target it. Review its complete text at reading scale.', ['pattern.window', 'window.scroll', 'window.search']);

  // MEDIA — actual Gallery paging with deliberately different text lengths.
  heading('media', 'gallery-heading', 'Gallery · one original at a time', 'These three objects have different amounts of text but share one viewing frame and navigation width.', 800, 1750, 2000);
  const gallery = block('media', 'gallery-demo', 800, 1910, 1620, 900, 'gallery');
  [
    ['First object', 'A short entry.'],
    ['Second object · a longer reading passage', 'A Gallery is useful when attention should stay on one object at a time. Each object remains an ordinary child, including text, images, documents, references or nested containers. Navigation changes the active view; it does not rewrite the collection, add an Undo entry or modify the project revision. The frame is deliberately fixed here so that content length cannot set the outer size.'],
    ['Third object', 'You reached the final item. Previous and Next stop at the ends; they do not wrap. Home and End navigate the focused Gallery.'],
  ].forEach(([title, text], i) => child('media', `gallery-item-${i + 1}`, gallery, title, text, 815, 1925, 1590, 780, { style: { titleSize: 30, fontSize: 24, padding: 24 } }));
  note('media', 'gallery', 'One frame, several ordinary objects', 'A Gallery displays one immediate child, with Previous and Next arrows. Browsing is local state; adding, reordering or deleting objects changes the document. The displayed object and navigation share the same width. A child’s content length does not set a separate Gallery tile size.', 'Use both arrows. With the Gallery focused, test Left, Right, Home and End. Search the third object’s Tag and target it to activate that page. Double-click the parent’s backdrop to hide or reveal the collection.', ['pattern.gallery', 'gallery.navigation', 'gallery.uniform-size']);

  // LOGIC — exposed controls, typed wires, switches and all registry operations.
  heading('logic', 'numeric-heading', 'A complete running system · 2 + 3 = 5', 'Run the Work first. Commit numeric edits with Enter or blur. Opening the switch makes the downstream output Unknown.', 800, 360, 2250);
  const a = full('logic', 'sum-a', 'A', '', 800, 550, 320, 210, { kind: 'number', value: 2, valueType: 'number', exposed: true });
  const b = full('logic', 'sum-b', 'B', '', 800, 900, 320, 210, { kind: 'number', value: 3, valueType: 'number', exposed: true });
  const plus = full('logic', 'sum-operation', 'Add', '', 1340, 730, 310, 210, { kind: 'joint', operation: 'ADD', valueType: 'number' });
  const gate = full('logic', 'sum-switch', 'Pass when closed', '', 1840, 730, 370, 210, { kind: 'switch', closed: true, valueType: 'number', exposed: true });
  const out = full('logic', 'sum-output', 'Numeric result', '', 2460, 730, 440, 210, { kind: 'output', valueType: 'number' });
  wire('logic', 'sum-a-wire', a, plus, { kind: 'numeric', targetPort: 0, sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .27 } });
  wire('logic', 'sum-b-wire', b, plus, { kind: 'numeric', targetPort: 1, sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .73 } });
  wire('logic', 'sum-pass', plus, gate, { kind: 'numeric' }); wire('logic', 'sum-result', gate, out, { kind: 'numeric' });
  expected.push({ kind: 'sum-switch', a, b, operation: plus, switch: gate, output: out, value: 5, openValue: null });
  heading('logic', 'boolean-heading', 'Boolean controls · both conditions must be true', 'Boolean wires and numeric wires are different types. Their colors are runtime cues; they do not convert values.', 800, 1210, 2250);
  const ba = full('logic', 'boolean-a', 'Ready', '', 800, 1380, 320, 210, { kind: 'input', value: true, exposed: true });
  const bb = full('logic', 'boolean-b', 'Approved', '', 800, 1720, 320, 210, { kind: 'input', value: false, exposed: true });
  const and = full('logic', 'boolean-operation', 'AND', '', 1340, 1550, 310, 210, { kind: 'joint', operation: 'AND', valueType: 'boolean' });
  const bo = full('logic', 'boolean-output', 'Both true?', '', 1840, 1550, 440, 210, { kind: 'output', valueType: 'boolean' });
  wire('logic', 'boolean-a-wire', ba, and, { kind: 'functional', targetPort: 0 });
  wire('logic', 'boolean-b-wire', bb, and, { kind: 'functional', targetPort: 1 });
  wire('logic', 'boolean-out-wire', and, bo, { kind: 'functional' });
  expected.push({ kind: 'boolean-and', a: ba, b: bb, operation: and, output: bo, value: false });
  heading('logic', 'operations-heading', 'Operation desk · every implemented operation', 'Scroll through 21 native working systems. Arithmetic, Boolean, comparison and identity examples each use their real input ports.', 800, 2070, 2250);
  const opsGallery = block('logic', 'operations-window', 800, 2230, 2250, 1100, 'window', { grid: { columns: 1, rows: OPERATION_DEFINITIONS.length, cellWidth: 2220, cellHeight: 960, windowColumns: 1, windowRows: 1, scrollDirection: 'vertical', gap: 0, showHeaders: false } });
  OPERATION_DEFINITIONS.forEach((definition, index) => {
    const local = `operation-${definition.operation.toLowerCase()}`;
    const dy = index * 960;
    const page = child('logic', local, opsGallery, '', '', 815, 2245 + dy, 2220, 960, { kind: 'container', tag: `Tutorial · Operation · ${definition.operation}`, collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', cell: { column: 1, row: index + 1, name: definition.operation.toLowerCase() }, style: { padding: 12, childPadding: 12 } });
    const isBool = definition.inputs[0]?.type === 'boolean';
    const values = definition.inputs.map((_port, p) => isBool ? p === 0 : p === 0 ? 2 : 3);
    const result = applyOperation(definition.operation, values, isBool ? 'boolean' : 'number');
    child('logic', `${local}-title`, page, `${definition.label} · ${definition.symbol}`, `${definition.family} · ${definition.inputs.map(port => `${port.name}: ${port.type}`).join(', ')} → ${definition.output}\nAuthored result: ${String(result)}. Change an operand while running to test the actual operation.`, 850, 2285 + dy, 2110, 180, { style: { variant: 'label', shape: 'none', borderWidth: 0, titleSize: 28, fontSize: 20, padding: 0 } });
    const inputs = definition.inputs.map((port, p) => child('logic', `${local}-input-${p}`, page, port.name, '', 880, 2500 + p * 360 + dy, 360, 240, { kind: port.type === 'number' ? 'number' : 'input', value: values[p], valueType: port.type, exposed: true }));
    const operation = child('logic', `${local}-joint`, page, definition.label, '', 1600, 2675 + dy, 380, 240, { kind: 'joint', operation: definition.operation, valueType: isBool ? 'boolean' : 'number' });
    const output = child('logic', `${local}-output`, page, `${definition.output} result`, '', 2500, 2675 + dy, 440, 240, { kind: 'output', valueType: definition.output });
    inputs.forEach((input, p) => wire('logic', `${local}-wire-${p}`, input, operation, { parent: page, kind: definition.inputs[p].type === 'number' ? 'numeric' : 'functional', targetPort: p, sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: inputs.length === 1 ? .5 : p === 0 ? .27 : .73 } }));
    wire('logic', `${local}-wire-result`, operation, output, { parent: page, kind: definition.output === 'number' ? 'numeric' : 'functional' });
    expected.push({ kind: 'operation', operationId: definition.operation, page, inputs, output, value: result });
  });
  heading('logic', 'junction-heading', 'A typed routing Joint fans one value out', 'One numeric input passes through the Utility and feeds two independent readouts. It does not add, combine or coerce values.', 800, 3490, 2250);
  const fanValue = full('logic', 'fan-value', 'One source', '', 800, 3720, 340, 210, { kind: 'number', value: 8, valueType: 'number', exposed: true });
  const fan = full('logic', 'fan-junction', '', '', 1550, 3820, 18, 18, { kind: 'junction', tag: 'Tutorial · Numeric routing Joint', utilityVariant: 'joint', valueType: 'number', style: { shape: 'circle', padding: 0, borderWidth: 1 } });
  const fanA = full('logic', 'fan-output-a', 'Readout A', '', 2140, 3610, 420, 210, { kind: 'output', valueType: 'number' });
  const fanB = full('logic', 'fan-output-b', 'Readout B', '', 2140, 3970, 420, 210, { kind: 'output', valueType: 'number' });
  wire('logic', 'fan-in', fanValue, fan, { kind: 'numeric' });
  wire('logic', 'fan-a', fan, fanA, { kind: 'numeric' }); wire('logic', 'fan-b', fan, fanB, { kind: 'numeric' });
  expected.push({ kind: 'fan-out', source: fanValue, junction: fan, outputs: [fanA, fanB], value: 8 });
  note('logic', 'types', 'Real controls, real ports', 'Number holds a finite number. Boolean input holds On or Off. An Operation has named typed ports; A and B are not inferred from line position. A Switch passes one input while closed and produces undefined while open. An Output displays one value. Visual relationships perform no computation.', 'Start Run. Change A to 10 and B to −4, committing each edit. The sum should be 6. Open the switch: result becomes Unknown. Close it to restore 6. Toggle Approved to make the Boolean AND output true.', ['object.number', 'object.input', 'object.switch', 'object.output', 'object.operation', 'wire.numeric', 'wire.boolean', 'runtime.run', 'runtime.unknown']);
  note('logic', 'operation-registry', 'Twenty-one supported operations', 'Boolean: AND, OR, NOT, NAND, NOR, XOR, XNOR. Arithmetic: ADD, SUBTRACT, MULTIPLY, DIVIDE, MODULO, POWER, MIN, MAX, ABS, NEGATE. Comparison: EQUAL, GREATER, LESS. Routing: IDENTITY. Comparisons consume numbers and emit Boolean values. Identity in this desk is Boolean; it can also be configured for numbers.', 'Browse all operation pages. On DIVIDE, set B to 0; the output must become Unknown rather than zero or its previous value. On LESS, compare 2 and 3; the result is true. Use Reset to restore authored inputs and stop.', ['operation.registry', 'operation.boolean', 'operation.arithmetic', 'operation.comparison', 'operation.identity', 'runtime.reset', 'runtime.divide-by-zero']);
  note('logic', 'runtime-boundary', 'The viewer runs a temporary copy', 'The viewer begins stopped. Run evaluates the typed acyclic graph. Hidden calculations continue running. Stop clears runtime activity; Reset restores authored settings. Runtime edits in a publication do not rewrite the original or become the authored payload downloaded by Save a copy.', 'Run, edit an exposed operand, then Stop and Reset. Compare the temporary result with the authored result in the operation page. In the editor, authored edits and runtime controls have different persistence roles.', ['runtime.stop', 'runtime.transient', 'runtime.hidden']);

  // ROUTES — a meaningful bend and a real shared meeting point.
  heading('routes', 'route-heading', 'Pivot · route around occupied space', 'This saved semantic route turns above an unrelated explanation. No segment passes through that explanation or its text.', 800, 360, 2250);
  const from = full('routes', 'route-source', 'Evidence', 'The source claim.', 800, 720, 400, 240);
  const obstacle = full('routes', 'route-obstacle', 'Unrelated reading surface', 'This is intentionally on the direct path. The saved Pivots carry the connection through clear space above it.', 1450, 650, 650, 450);
  const to = full('routes', 'route-target', 'Conclusion', 'The destination interpretation.', 2500, 720, 450, 240);
  recipe.routes.push({ key: key('routes', 'pivot-route'), canvas: w('routes'), source: from, target: to, relation: 'causal', direction: 'forward', kind: 'visual', label: 'supports', sourceAnchor: { x: .5, y: 0 }, targetAnchor: { x: .5, y: 0 },
    style: { color: theme.line ?? '#71AAA7', width: 3, lineStyle: 'solid' }, via: [
      { key: key('routes', 'turn-left'), variant: 'pivot', x: 1000, y: 570, width: 14, height: 14 },
      { key: key('routes', 'turn-right'), variant: 'pivot', x: 2725, y: 570, width: 14, height: 14 },
    ] });
  heading('routes', 'joint-heading', 'Utility Joint · an actual shared meeting point', 'Two source records support one synthesis. The visible dot is a saved node with a GUID, rather than a coincidental line crossing.', 800, 1290, 2250);
  const evidenceA = full('routes', 'joint-evidence-a', 'Record A', 'A first observation.', 800, 1540, 400, 240);
  const evidenceB = full('routes', 'joint-evidence-b', 'Record B', 'A second observation.', 800, 1960, 400, 240);
  const junction = full('routes', 'shared-joint', '', '', 1670, 1850, 20, 20, { kind: 'junction', tag: 'Tutorial · Shared evidence Joint', utilityVariant: 'joint', style: { shape: 'circle', padding: 0 } });
  const synthesis = full('routes', 'joint-synthesis', 'Synthesis', 'Both records meet at the same explicitly authored evidence junction.', 2380, 1740, 570, 240);
  wire('routes', 'evidence-a', evidenceA, junction, { relation: 'generic', label: 'contributes' });
  wire('routes', 'evidence-b', evidenceB, junction, { relation: 'generic', label: 'contributes' });
  wire('routes', 'evidence-synthesis', junction, synthesis, { relation: 'causal', label: 'supports' });
  note('routes', 'route-tools', "Distinguish relationships from dynamic placement", "Ordinary visual links express relationships and direction. A Pivot bends a route; a Utility Joint makes a shared meeting point. A Smart Link manages placement of its member branches. Child chains center on their parent and use both directions of the sibling axis: downward branches spread left and right, while rightward branches spread above and below. Revealed descendants reserve space; collapse releases it. Position locks and real obstacles remain constraints. The practice chain below is a native Smart Link.", "Compare Planning, Delivery, and Review on both sides of the Smart Link center. Expand Planning and watch the chain make room; collapse it again. In a copy, choose Node and click the link to insert a member.", ['wire.visual', 'wire.relationship', 'wire.direction', 'wire.appearance', 'utility.pivot', 'utility.joint', 'route.saved', 'smart-link-layout']);
  note('routes', 'joint-editing', 'Insert or join explicitly', 'Right-click an existing line to insert a Pivot or Joint. Drawing a new link to an existing line creates a real Joint there and splits the old line. Choosing an existing Joint reuses that original. Utility points have their own identity and connected routes move when that point is moved.', 'Select the shared evidence dot. Move it slightly while preserving clearance. Inspect its GUID. Contrast it with Add in the logic laboratory: only the operation combines typed inputs into a result.', ['route.insert', 'route.join-segment', 'utility.identity', 'utility-vs-operation']);

  // SMART LINK — visible branches reserve space only while disclosed.
  full('routes', 'smart-link-heading', 'Smart Link', 'Explore both sides of the Smart Link center; expand Planning to reserve space, then collapse it to compact the chain.', 800, 2420, 2250, 120, {style:{textLayout:'balanced',textAlign:'left',variant:'label',shape:'none',borderWidth:0,padding:0,titleSize:28,fontSize:18}});
  const smartRail=full('routes','smart-link-rail','Smart Link','',844,2994,12,12,{kind:'junction',sizeMode:'fixed',utilityVariant:'joint',showTitle:false,collapsed:false,smartLink:{direction:'down',gap:48},childLayout:'spatial',childArrangement:'free',style:{shape:'circle',padding:0}});
  const smartStyle={"textLayout":"balanced","textAlign":"center","fontSize":22,"titleSize":32,"fill":"#16333B","border":"#507C82","textColor":"#E2EBEA","accent":"#E6BA73","radius":0,"padding":5};
  const smartPlanning=full('routes','smart-link-planning','Planning','',898,3048,240,120,{parent:smartRail,sizeMode:'auto',collapsed:true,childPosition:'manual',childLayout:'spatial',childArrangement:'free',style:smartStyle});
  const smartDelivery=full('routes','smart-link-delivery','Delivery','',898,3160,240,120,{parent:smartRail,sizeMode:'auto',childPosition:'manual',style:smartStyle});
  const smartReview=full('routes','smart-link-review','Review','',898,3272,240,120,{parent:smartRail,sizeMode:'auto',childPosition:'manual',style:smartStyle});
  const smartOutline=child('routes','smart-link-outline',smartPlanning,'Outline','',1248,2908,240,120,{sizeMode:'auto',style:smartStyle});
  const smartScope=child('routes','smart-link-scope',smartPlanning,'Scope','',1248,3188,240,120,{sizeMode:'auto',style:smartStyle});
  wire('routes','smart-link-outline-relationship',smartPlanning,smartOutline,{label:'includes'});
  wire('routes','smart-link-scope-relationship',smartPlanning,smartScope,{label:'includes'});
  expected.push({kind:'smart-link',rail:smartRail,expand:smartPlanning,neighbor:smartDelivery,members:[smartPlanning,smartDelivery,smartReview]});

  // REUSE — live and saved observation, native packing assembly and restrictions.
  heading('reuse', 'references-heading', 'References · observe an original', 'Same-Work reads are live. A reference to another Work reads its committed saved observation.', 800, 360, 2250);
  const original = full('reuse', 'reference-original', 'Original value', '', 800, 550, 450, 250, { kind: 'number', value: 11, valueType: 'number', exposed: true });
  const live = full('reuse', 'reference-local', 'Local live observation', '', 1460, 550, 600, 250, { kind: 'reference', target: original, valueType: 'number' });
  const saved = full('reuse', 'reference-saved', 'Saved other-Work observation', '', 2260, 550, 700, 250, { kind: 'reference', target: key('tables', 'addressable-sheet'), targetCell: 'quantity', valueType: 'number' });
  expected.push({ kind: 'references', original, local: live, saved, localValue: 11, savedValue: 7 });
  heading('reuse', 'module-heading', 'Module · a reusable independent assembly', 'This is a native packing node with a complete numeric system. Save the branch to the Module Library, then insert it twice.', 800, 980, 2250);
  const module = block('reuse', 'module-packing', 800, 1160, 2150, 740, 'contained', { kind: 'module', tag: 'Tutorial · Reusable add-one Module' });
  const mi = child('reuse', 'module-input', module, 'Input', '', 840, 1340, 330, 230, { kind: 'number', value: 4, valueType: 'number', exposed: true });
  const one = child('reuse', 'module-one', module, 'Fixed increment', '', 840, 1620, 330, 230, { kind: 'number', value: 1, valueType: 'number', exposed: false });
  const mo = child('reuse', 'module-operation', module, 'Add one', '', 1590, 1480, 330, 230, { kind: 'joint', operation: 'ADD', valueType: 'number' });
  const mout = child('reuse', 'module-output', module, 'Result', '', 2440, 1480, 410, 230, { kind: 'output', valueType: 'number' });
  wire('reuse', 'module-wire-input', mi, mo, { kind: 'numeric', targetPort: 0, parent: module });
  wire('reuse', 'module-wire-one', one, mo, { kind: 'numeric', targetPort: 1, parent: module });
  wire('reuse', 'module-wire-output', mo, mout, { kind: 'numeric', parent: module });
  expected.push({ kind: 'module', root: module, input: mi, output: mout, value: 5 });
  heading('reuse', 'rules-heading', 'Inherited rules · preview the consequences', 'This deliberately limited parent permits Media and Container descendants, visual links, and no operations.', 800, 2080, 2250);
  const limited = block('reuse', 'rules-parent', 800, 2250, 1300, 550, 'contained', { tag: 'Tutorial · Restricted visual parent', rules: { nodeKinds: ['media', 'container'], linkKinds: ['visual'], operations: [] } });
  child('reuse', 'rules-child', limited, 'Permitted Media child', 'My type is allowed by the parent. Try adding a Number inside this enclosure: inherited rules must reject it.', 840, 2310, 650, 400);
  child('reuse', 'rules-child-2', limited, 'A second permitted child', 'An explicit empty operation list means none. It does not mean inherit.', 1530, 2310, 520, 400);
  note('reuse', 'references', 'Observation is not duplication or write-through', 'Same-Work references resolve the live original. Other-Work references use saved snapshots, including cell contents and evaluated values. Unsaved changes and temporary runtime edits do not leak into a cross-Work observation. Saving the original updates its committed observation. Missing context remains visibly unresolved.', 'Run and change Original value to 20; the local reference should follow. The saved other-Work observation starts at 7 from the named Quantity cell. Change that source in its Work, save it, then revisit the observation. Do not expect a viewer runtime change to update the saved reference.', ['object.reference', 'reference.local', 'reference.saved', 'reference.identity']);
  note('reuse', 'modules', 'Stored definitions and inserted copies are independent', 'A Module stores selected nodes, internal links, attachments, appearance, rules and configured values in an immutable local definition. Each insertion receives fresh identities. Editing one insertion does not update the source, another insertion or the stored entry. External wires do not silently reconnect.', 'Select Reusable add-one Module, open Relations and Save as module. Open Tools → Modules and insert two copies. Run, edit one input, and verify the other is unchanged. One Undo removes one insertion; Redo restores it. Review Dependencies before reuse.', ['object.module', 'module.library', 'module.copy', 'module.dependencies', 'module.undo']);
  note('reuse', 'rules', 'Inheritance constrains descendants', 'A parent’s own declarations constrain descendants, while its ancestors constrain the parent itself. Missing categories inherit; an explicit empty category permits nothing. Preview lists affected and hidden descendants and concrete conflicts. A rejected or cancelled preview changes no Work content.', 'Select Restricted visual parent, then Relations → Inherited rules → Edit inherited rules. Compare permissions. Preview adding or removing allowed kinds, then Cancel. Do not turn off inheritance expecting to escape a stricter ancestor.', ['rules.inheritance', 'rules.preview', 'rules.conflicts', 'rules.cancel']);
  note('reuse', 'edge-clusters', 'Choose edge locks or a collapsible Cluster', 'Sticky touching edges can be locked so their nodes move together. A named Cluster instead groups selected sibling branches, even when their edges do not touch. Create Cluster offers a square or circle animated button; hover reveals its name and one click restores the group. Member identities, internal links, and each branch’s prior disclosure are retained. These are separate from reusable Modules.', 'In a practice copy, select two sibling branches and choose Create Cluster. Name it, choose its button shape, collapse, hover, and reopen it. Compare that with locking two touching edges.', ['cluster.edge-lock', 'cluster.movement', 'cluster.disclosure']);

  return { notes, fixtureKeys: fixtures, featureIds: [...new Set(notes.flatMap(note => note.featureIds))], expected,
    inventory: { operationCount: OPERATION_DEFINITIONS.length, patterns: ['Freeform', 'Horizontal', 'Vertical', 'Grid', 'Spreadsheet', 'Window', 'Gallery'], installedLimitations: [] } };
}
