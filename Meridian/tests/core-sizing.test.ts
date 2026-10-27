import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { assetImageSize, boundedChildInterior, effectiveNodeBorderWidth, galleryObjectLayout, intrinsicContentSize, nodeContentBounds, nodeContentLayout, nodeSizingMode, nodeSpacing, nodeBorderWidth, sizingBounds, wrappedTextHeight } from '../src/core/sizing.ts';
import { gridGeometry } from '../src/core/cells.ts';
import { reflowContainedChildren } from '../src/core/grid.ts';
import type { MeridianAsset, MeridianNode } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Content sizing'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'My canvases');
  Object.assign(parent, { sizeMode: 'auto', x: 300, y: 200, width: 1800, height: 1500, childLayout: 'spatial', collapsed: false });
  const child = addNode(project, canvasId, 'canvas-link', 'A short canvas');
  Object.assign(child, { sizeMode: 'auto', parentId: parent.id, x: 1200, y: 600, width: 240, height: 400 });
  // These regression fixtures represent existing authored Works. New-node
  // balanced typography is exercised separately in balanced-typography.test.ts.
  delete parent.style; delete child.style;
  return { project, canvasId, parent, child };
}
function png(width: number, height: number): MeridianAsset {
  const bytes = Buffer.alloc(24); Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]).copy(bytes);
  bytes.writeUInt32BE(width, 16); bytes.writeUInt32BE(height, 20);
  return { id: 'dimensions', name: 'Dimensions.png', mime: 'image/png', base64: bytes.toString('base64') };
}

test('tiny fixed vector frames retain positive geometry and proportional borders without changing authored style', () => {
  const { project, child } = fixture();
  Object.assign(child, { kind: 'media', parentId: null, sizeMode: 'fixed', width: 1, height: 2, style: { borderWidth: 8, shape: 'rectangle', padding: 0 } });
  const before = structuredClone(child), box = sizingBounds(project, child);
  assert.equal(box.width, 1); assert.equal(box.height, 2);
  assert.equal(effectiveNodeBorderWidth(child, box.width, box.height), .1);
  assert.equal(effectiveNodeBorderWidth(child, 70, 70), 8);
  assert.equal(effectiveNodeBorderWidth(child, 500, 200), 8);
  const content = nodeContentBounds(child, box);
  assert.ok(content.width > 0 && content.height > 0); assert.ok(content.x > box.x && content.y > box.y);
  assert.ok(content.x + content.width < box.x + box.width && content.y + content.height < box.y + box.height);
  child.style!.shape = 'circle'; assert.equal(effectiveNodeBorderWidth(child, 2, 1), .1);
  child.style!.shape = 'rectangle'; assert.deepEqual(child, before);
});

test('legacy Spatial containers become free enclosures and collapse to their own contents without rewriting authored geometry', () => {
  const { project, parent, child } = fixture(), before = structuredClone(project);
  const expanded = navigationIndex(project).bounds(parent);
  assert.ok(expanded.width >= parent.width && expanded.height >= parent.height, 'An enclosure keeps its intentionally large workspace.');
  parent.collapsed = true;
  assert.deepEqual(navigationIndex(project).bounds(parent), { id: parent.id, x: parent.x, y: parent.y, ...intrinsicContentSize(project, parent) });
  assert.ok(navigationIndex(project).bounds(parent).height < 80);
  assert.equal(navigationIndex(project).isNodeVisible(child.id), false);
  parent.collapsed = false; assert.deepEqual(project, before);
});

test('auto leaf dimensions grow with text and custom typography while short content removes blank space', () => {
  const { project, child } = fixture(); Object.assign(child, { kind: 'text', title: 'Reading', text: 'A small passage.', width: 800, height: 900 });
  const small = intrinsicContentSize(project, child);
  assert.ok(small.width < 200 && small.height < 120);
  child.text = 'A complete passage should remain available on the infinite canvas. '.repeat(40);
  const long = intrinsicContentSize(project, child);
  assert.ok(long.width <= 420 && long.height > small.height * 4);
  child.style = { fontSize: 28, titleSize: 45, fontFamily: 'serif' };
  const large = intrinsicContentSize(project, child);
  assert.ok(large.height > long.height); assert.equal(child.style.fontSize, 28); assert.equal(child.style.titleSize, 45);
  assert.ok(wrappedTextHeight('One\n\nThree', 1000, { fontSize: 12, family: 'mono' }) >= 54);
});

test('raster dimensions preserve media proportions and do not enlarge a background-only tile', () => {
  const { project, child } = fixture(), asset = png(120, 300); project.assets.push(asset);
  Object.assign(child, { kind: 'image', title: 'Portrait', assetId: asset.id, text: 'A complete caption.', width: 300 });
  assert.deepEqual(assetImageSize(asset), { width: 120, height: 300 });
  const layout = nodeContentLayout(project, child);
  assert.deepEqual(layout.media, { width: 120, height: 300 }); assert.ok(layout.height > 300 && layout.height < 350);
  assert.equal(child.text, 'A complete caption.');
  child.contentType = 'text'; const textOnly = nodeContentLayout(project, child);
  assert.equal(textOnly.media, undefined); assert.ok(textOnly.height < layout.height); assert.equal(child.assetId, asset.id);
  child.contentType = 'asset'; assert.deepEqual(nodeContentLayout(project, child), layout);
  child.assetId = undefined; child.kind = 'container'; child.text = undefined;
  const plain = intrinsicContentSize(project, child); child.style = { backgroundAssetId: asset.id };
  assert.deepEqual(intrinsicContentSize(project, child), plain);
  const invalid = { ...asset, base64: Buffer.from('invalid').toString('base64') }; assert.equal(assetImageSize(invalid), undefined);
});

test('contained bounds retain their authored frame while descendants fit it and moves never enlarge it', () => {
  const { project, parent, child } = fixture(); Object.assign(parent, { childLayout: 'contained', width: 80, height: 50 });
  child.x = parent.x + 16; child.y = parent.y + intrinsicContentSize(project, parent).height + 16;
  const index = navigationIndex(project), opened = index.bounds(parent), own = intrinsicContentSize(project, parent);
  assert.deepEqual(opened, { id: parent.id, x: parent.x, y: parent.y, width: 80, height: 50 });
  child.x += 350; assert.deepEqual(index.bounds(parent), opened);
  const fitted = reflowContainedChildren(project, parent.id), fittedParent = fitted.nodes.find(node => node.id === parent.id)!, fittedChild = fitted.nodes.find(node => node.id === child.id)!;
  const interior = boundedChildInterior(fitted, fittedParent), tile = sizingBounds(fitted, fittedChild);
  assert.ok(tile.x >= interior.x - 1e-7 && tile.y >= interior.y - 1e-7);
  assert.ok(tile.x + tile.width <= interior.x + interior.width + 1e-7 && tile.y + tile.height <= interior.y + interior.height + 1e-7);
  assert.deepEqual(sizingBounds(fitted, fittedParent), opened);
  parent.collapsed = true; assert.deepEqual(index.bounds(parent), { id: parent.id, x: parent.x, y: parent.y, ...own });
  // A collapsed outer ancestor does not change an independent child's own fitting bounds.
  const inner = addNode(project, parent.canvasId, 'container', 'Nested frame'); Object.assign(inner, { sizeMode: 'auto', parentId: child.id, childLayout: 'contained', x: child.x + 16, y: child.y + 90 });
  child.childLayout = 'contained'; const nested = navigationIndex(project).bounds(child); parent.collapsed = false;
  assert.deepEqual(navigationIndex(project).bounds(child), nested);
});

test('Grid reserves a square address space and Spreadsheet retains explicitly empty cells', () => {
  const { project, parent, child } = fixture(); Object.assign(parent, { childLayout: 'contained', containerVariant: 'grid', childArrangement: 'grid', grid: { columns: 8, rows: 10, cellWidth: 140, cellHeight: 90, gap: 0, showHeaders: false } });
  child.cell = { column: 1, row: 1 }; Object.assign(child, { x: parent.x + 16, y: parent.y + intrinsicContentSize(project, parent).height + 16 });
  const grid = navigationIndex(project).bounds(parent);
  assert.deepEqual(grid, { id: parent.id, x: parent.x, y: parent.y, width: 1800, height: 1500 });
  assert.deepEqual([gridGeometry(parent, project).columns, gridGeometry(parent, project).rows], [1, 1]);
  parent.kind = 'spreadsheet'; parent.containerVariant = 'spreadsheet';
  const sheet = navigationIndex(project).bounds(parent); assert.deepEqual(sheet, grid);
  const geometry = gridGeometry(parent, project), interior = boundedChildInterior(project, parent);
  assert.deepEqual([geometry.columns, geometry.rows], [8, 10]);
  assert.ok(Math.abs(geometry.cellWidth * 8 - interior.width) < 1e-7 && Math.abs(geometry.cellHeight * 10 - interior.height) < 1e-7);
  parent.collapsed = true; assert.deepEqual(navigationIndex(project).bounds(parent), { id: parent.id, x: parent.x, y: parent.y, ...intrinsicContentSize(project, parent) });
});

test('empty ordered containers retain their authored frame while Spreadsheet preserves its explicit address capacity', () => {
  const { project, parent } = fixture(); project.nodes = project.nodes.filter(node => node.parentId !== parent.id);
  Object.assign(parent, { childLayout: 'contained', containerVariant: 'grid', childArrangement: 'grid', grid: { columns: 12, rows: 20, cellWidth: 400, cellHeight: 300 } });
  const frame = { id: parent.id, x: parent.x, y: parent.y, width: 1800, height: 1500 };
  assert.deepEqual(navigationIndex(project).bounds(parent), frame);
  parent.containerVariant = 'spreadsheet'; assert.deepEqual(navigationIndex(project).bounds(parent), frame);
  assert.deepEqual([gridGeometry(parent, project).columns, gridGeometry(parent, project).rows], [12, 20]);
});

test('Gallery pages share one authored viewport regardless of active content without changing saved geometry', () => {
  const { project, parent, child } = fixture(); Object.assign(parent, { containerVariant: 'gallery', childLayout: 'contained', childArrangement: 'free' });
  const long = addNode(project, parent.canvasId, 'text', 'Complete gallery passage'); Object.assign(long, { sizeMode: 'auto', parentId: parent.id, width: 280, text: 'Full text remains visible. '.repeat(30) });
  const before = structuredClone(project), first = navigationIndex(project, { galleryItems: { [parent.id]: child.id } }).bounds(parent);
  const second = navigationIndex(project, { galleryItems: new Map([[parent.id, long.id]]) }).bounds(parent), envelope = navigationIndex(project).bounds(parent);
  assert.deepEqual(first, { id: parent.id, x: parent.x, y: parent.y, width: parent.width, height: parent.height });
  assert.deepEqual(second, first); assert.deepEqual(envelope, first);
  assert.ok(galleryObjectLayout(project, parent, long).height > galleryObjectLayout(project, parent, child).height * 2, 'Different intrinsic passages fit the same page viewport.');
  assert.equal(navigationIndex(project).isNodeVisible(child.id), false); assert.deepEqual(project, before);
});

test('an empty automatic Gallery retains its authored viewport without synthetic content or content-driven growth', () => {
  const { project, parent } = fixture(); project.nodes = project.nodes.filter(node => node.parentId !== parent.id);
  Object.assign(parent, { containerVariant: 'gallery', childLayout: 'contained', width: 420, title: 'Gallery' });
  const before = structuredClone(project), empty = navigationIndex(project).bounds(parent);
  assert.deepEqual(empty, { id: parent.id, x: parent.x, y: parent.y, width: 420, height: 1500 });
  assert.equal(nodeContentLayout(project, parent).bodyHeight, 5, 'Only authored title spacing is reserved, with no synthetic empty-state text.');
  parent.style = { fontSize: 24 }; const large = navigationIndex(project).bounds(parent);
  assert.equal(large.height, empty.height, 'Changing an unused body font does not manufacture an empty-state line.');
  parent.text = 'A useful existing collection description. '.repeat(12);
  assert.deepEqual(navigationIndex(project).bounds(parent), large, 'Adding content never expands the authored Gallery viewport.');
  delete parent.text; delete parent.style; parent.collapsed = true;
  assert.deepEqual(navigationIndex(project).bounds(parent), { id: parent.id, x: parent.x, y: parent.y, ...intrinsicContentSize(project, parent) });
  parent.collapsed = false; assert.deepEqual(project, before);
});

test('empty objects in a Gallery reserve only their authored title for each node type', () => {
  const { project, parent, child } = fixture(); Object.assign(parent, { containerVariant: 'gallery', childLayout: 'contained', style: { fontSize: 24 } });
  Object.assign(child, { width: 130, title: 'Empty object', targetId: undefined, text: undefined, assetId: undefined });
  for (const kind of ['container', 'text', 'image', 'document'] as const) {
    child.kind = kind;
    const content = galleryObjectLayout(project, parent, child), frame = navigationIndex(project, { galleryItems: { [parent.id]: child.id } }).bounds(parent);
    assert.equal(content.bodyHeight, 0, `${kind}: no synthetic content message contributes a body.`);
    assert.ok(content.headerHeight > 0, `${kind}: the authored title remains visible.`);
    assert.ok(frame.height >= content.height + 59, `${kind}: collection title, five-pixel gaps and browsing controls fit without a spare footer.`);
  }
});

test('cell identity and names never add visible badge or footer sizing', () => {
  const { project, parent, child } = fixture(); Object.assign(child, { kind: 'text', title: 'A title', text: 'A complete final line.', width: 300, style: { titleSize: 30, fontSize: 20 } });
  const plain = intrinsicContentSize(project, child);
  child.cell = { column: 1, row: 1 }; assert.deepEqual(intrinsicContentSize(project, child), plain);
  child.cell.name = 'Closing line'; assert.deepEqual(intrinsicContentSize(project, child), plain);
  const embedded = galleryObjectLayout(project, parent, child); child.cell = undefined;
  assert.deepEqual(galleryObjectLayout(project, parent, child), embedded);
  child.style!.shape = 'none'; const floating = intrinsicContentSize(project, child); child.cell = { column: 2, row: 3 };
  assert.deepEqual(intrinsicContentSize(project, child), floating);
  Object.assign(parent, { containerVariant: 'gallery', childLayout: 'contained' });
  const gallery = navigationIndex(project).bounds(parent); parent.cell = { column: 3, row: 1 };
  assert.deepEqual(navigationIndex(project).bounds(parent), gallery);
  child.sizeMode = 'fixed'; assert.equal(navigationIndex(project).bounds(child).height, child.height);
});

test('automatic nonrectangle backdrops reserve their content insets without altering font sizes', () => {
  const { project, child } = fixture(); Object.assign(child, { kind: 'text', title: 'Readable', text: 'A shaped passage.', style: { fontSize: 20, titleSize: 30 } });
  const plain = intrinsicContentSize(project, child);
  for (const shape of ['circle', 'square', 'triangle', 'diamond', 'pentagon', 'hexagon', 'octagon'] as const) {
    child.style!.shape = shape; const size = intrinsicContentSize(project, child);
    assert.ok(size.width >= plain.width && size.height >= plain.height, shape);
    if (shape === 'circle' || shape === 'square') assert.equal(size.width, size.height);
    assert.equal(child.style!.fontSize, 20);
  }
});

test('older leaves and manually sized collapsed containers retain their authored frame while automatic covers fit content', () => {
  const { project, parent, child } = fixture(); delete child.sizeMode; child.kind = 'image';
  assert.equal(nodeSizingMode(child), 'fixed'); assert.equal(sizingBounds(project, child).height, child.height);
  delete parent.sizeMode; assert.equal(nodeSizingMode(parent), 'auto');
  parent.sizeMode = 'fixed'; parent.childLayout = 'contained'; parent.collapsed = true;
  assert.deepEqual(sizingBounds(project, parent), { id: parent.id, x: parent.x, y: parent.y, width: 1800, height: 1500 });
  parent.collapsed = false; assert.equal(sizingBounds(project, parent).width, 1800); assert.equal(sizingBounds(project, parent).height, 1500);
  parent.childLayout = 'spatial'; parent.collapsed = true;
  assert.deepEqual(sizingBounds(project, parent), { id: parent.id, x: parent.x, y: parent.y, width: 1800, height: 1500 });
  parent.sizeMode = 'auto'; assert.deepEqual(sizingBounds(project, parent), { id: parent.id, x: parent.x, y: parent.y, ...intrinsicContentSize(project, parent) });
});

test('local and pinned reference fitting uses the same source content and attachment observation as rendering', () => {
  const { project, canvasId, child } = fixture(); child.kind = 'text'; child.text = 'Saved source content.';
  const other = addCanvas(project, 'Other'), reference = addNode(project, other.id, 'reference', 'A source observation');
  Object.assign(reference, { sizeMode: 'auto', targetId: child.id });
  const saved = commitSnapshots(project), originalHeight = intrinsicContentSize(saved, reference).height;
  saved.nodes.find(node => node.id === child.id)!.text = 'Unsaved change. '.repeat(300);
  assert.equal(intrinsicContentSize(saved, reference).height, originalHeight);
  reference.canvasId = canvasId; assert.ok(intrinsicContentSize(saved, reference).height > originalHeight);
});


test('content spacing stays five pixels while child inset leaves breathing room and inactive media text stays hidden', () => {
  const { project, child } = fixture(); Object.assign(child, { kind: 'text', title: 'Title', text: 'Body', width: 300 });
  assert.deepEqual(nodeSpacing(undefined), { padding: 5, contentGap: 5, textMediaGap: 5, childPadding: 5 });
  const base = intrinsicContentSize(project, child);
  child.style = { padding: 12 }; const padded = intrinsicContentSize(project, child);
  assert.equal(padded.width, base.width + 14); assert.equal(padded.height, base.height + 14);
  child.style = { contentGap: 15 }; assert.equal(intrinsicContentSize(project, child).height, base.height + 10);
  const asset = png(100, 60); project.assets.push(asset); Object.assign(child, { kind: 'image', assetId: asset.id, style: {} });
  const media = intrinsicContentSize(project, child);
  child.style = { textMediaGap: 19 }; assert.equal(intrinsicContentSize(project, child).height, media.height);
  assert.equal(child.text, 'Body'); child.contentType = 'text'; assert.equal(nodeContentLayout(project, child).media, undefined);
  child.contentType = 'asset'; assert.deepEqual(intrinsicContentSize(project, child), media);
  child.style = { shape: 'none' }; const floating = intrinsicContentSize(project, child);
  child.style.padding = 12; assert.equal(intrinsicContentSize(project, child).height, floating.height + 14);
});

test('square Grid capacity rounds up at ten objects and ordered rows and columns use uniform cell geometry', () => {
  const { project, parent, child } = fixture(); Object.assign(parent, { containerVariant: 'grid', childLayout: 'contained', grid: { columns: 99, rows: 99, cellWidth: 120, cellHeight: 80 }, width: 2000, height: 2000 });
  for (let at = 1; at < 9; at++) { const item = addNode(project, parent.canvasId, 'text', `Object ${at}`); item.parentId = parent.id; }
  const nine = sizingBounds(project, parent); assert.equal(nine.width, 2000); assert.deepEqual([gridGeometry(parent, project).columns, gridGeometry(parent, project).rows], [3, 3]);
  const tenth = addNode(project, parent.canvasId, 'text', 'Tenth'); tenth.parentId = parent.id;
  const ten = sizingBounds(project, parent); assert.equal(ten.width, 2000); assert.equal(ten.height, 2000); assert.deepEqual([gridGeometry(parent, project).columns, gridGeometry(parent, project).rows], [4, 4]);
  child.collapsed = true; child.kind = 'container'; child.width = 500; child.height = 700;
  assert.equal(sizingBounds(project, child).width, gridGeometry(parent, project).cellWidth); assert.equal(sizingBounds(project, child).height, gridGeometry(parent, project).cellHeight);
  parent.containerVariant = 'horizontal'; assert.deepEqual(sizingBounds(project, parent), ten); assert.deepEqual([gridGeometry(parent, project).columns, gridGeometry(parent, project).rows], [10, 1]);
  parent.containerVariant = 'vertical'; assert.deepEqual(sizingBounds(project, parent), ten); assert.deepEqual([gridGeometry(parent, project).columns, gridGeometry(parent, project).rows], [1, 10]);
});

test('Window dimensions fit only the chosen visible cell count while retaining its full ordered address space', () => {
  const { project, parent } = fixture(); Object.assign(parent, { containerVariant: 'window', childLayout: 'contained', grid: { columns: 10, rows: 20, cellWidth: 120, cellHeight: 80, windowColumns: 3, windowRows: 5, scrollDirection: 'vertical' } });
  const before = structuredClone(project), window = sizingBounds(project, parent);
  assert.deepEqual(window, { id: parent.id, x: parent.x, y: parent.y, width: 1800, height: 1500 });
  const visible = gridGeometry(parent, project), interior = boundedChildInterior(project, parent);
  assert.deepEqual([visible.visibleColumns, visible.visibleRows], [3, 5]); assert.deepEqual([visible.columns, visible.rows], [10, 20]);
  assert.ok(Math.abs(visible.cellWidth * 3 - interior.width) < 1e-7 && Math.abs(visible.cellHeight * 5 - interior.height) < 1e-7);
  parent.style = { childPadding: 32 }; const padded = sizingBounds(project, parent); assert.deepEqual(padded, window); assert.ok(gridGeometry(parent, project).cellWidth < visible.cellWidth);
  parent.grid!.windowColumns = 999; parent.grid!.windowRows = 999;
  const clamped = sizingBounds(project, parent); assert.deepEqual(clamped, window); assert.deepEqual([gridGeometry(parent, project).visibleColumns, gridGeometry(parent, project).visibleRows], [10, 20]);
  delete parent.style; parent.grid = before.nodes.find(node => node.id === parent.id)!.grid; assert.deepEqual(project, before);
});


test('ordered child padding measures from the inner edge of a variable-width border', () => {
  const { project, parent } = fixture(); Object.assign(parent, { containerVariant: 'spreadsheet', childLayout: 'contained', style: { borderWidth: 7, padding: 9, contentGap: 11, childPadding: 13 }, grid: { columns: 2, rows: 3, cellWidth: 100, cellHeight: 60 } });
  const bounds = sizingBounds(project, parent), interior = boundedChildInterior(project, parent), grid = gridGeometry(parent, project);
  assert.equal(nodeBorderWidth(parent), 7); assert.equal(bounds.width, parent.width); assert.equal(bounds.height, parent.height);
  assert.equal(interior.x - parent.x, 7 + 13); assert.equal(grid.left, interior.x); assert.equal(grid.top, interior.y);
  assert.equal(grid.cellWidth * 2, interior.width); assert.equal(grid.cellHeight * 3, interior.height);
  parent.style!.shape = 'none'; assert.equal(nodeBorderWidth(parent), 0); assert.equal(sizingBounds(project, parent).width, bounds.width); assert.equal(boundedChildInterior(project, parent).x - parent.x, 13);
});
