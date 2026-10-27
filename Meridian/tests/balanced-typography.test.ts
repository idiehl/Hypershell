import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { DEFAULT_NODE_FONT_SIZE, DEFAULT_NODE_TITLE_SIZE, DEFAULT_TEXT_OCCUPANCY, NODE_STYLE_PRESETS, isNodeStyle } from '../src/core/appearance.ts';
import { balancedTextLayout, intrinsicContentSize, longestRichTextLine, nodeContentLayout, nodeTypography, sizingBounds } from '../src/core/sizing.ts';
import { buildPublication, readPublication } from '../src/publication.ts';

function fixture(title = 'Zeus') {
  const project = newProject('Typography'), node = addNode(project, project.entryCanvasId, 'media', title);
  return { project, node };
}

test('new-node typography is explicitly centered and larger while old unmarked nodes retain their defaults', () => {
  const { project, node } = fixture();
  assert.equal(node.style?.textLayout, 'balanced'); assert.equal(node.style?.textAlign, 'center');
  assert.deepEqual(nodeTypography(node), { fontSize: DEFAULT_NODE_FONT_SIZE, titleSize: DEFAULT_NODE_TITLE_SIZE, textAlign: 'center' });
  assert.deepEqual(NODE_STYLE_PRESETS.find(preset => preset.id === 'default')?.style, node.style, 'An explicit reset adopts the current defaults.');
  const older = { ...node, style: undefined }, before = structuredClone(older);
  assert.deepEqual(nodeTypography(older), { fontSize: 12, titleSize: 13, textAlign: 'center' });
  assert.equal(balancedTextLayout(project, older), false); intrinsicContentSize(project, older); assert.deepEqual(older, before);
  assert.ok(isNodeStyle({ textLayout: 'balanced' })); assert.equal(isNodeStyle({ textLayout: 'stretch' }), false);
  for (const kind of ['joint', 'junction', 'block'] as const) assert.equal(addNode(project, project.entryCanvasId, kind).style?.textLayout, undefined);
});

test('automatic visible titles set the complete unwrapped width beyond the old 420-unit cap', () => {
  const { project, node } = fixture('System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>');
  const before = structuredClone(node), typography = nodeTypography(node), layout = nodeContentLayout(project, node);
  const textWidth = longestRichTextLine(node.title, node.titleRuns, { fontSize: typography.titleSize, family: node.style?.fontFamily, bold: true });
  assert.ok(layout.width > 420); assert.ok(layout.contentWidth >= textWidth - .01);
  assert.equal(layout.headerHeight, typography.titleSize * 1.25 + 5, 'The complete title occupies one line.');
  assert.deepEqual(node, before);
  node.title = 'Map'; assert.ok(intrinsicContentSize(project, node).width < layout.width / 5);
});

test('automatic title, body-only and combined textual compositions occupy approximately65percent of their frames', () => {
  const { project, node } = fixture();
  const cases = [
    { title: 'Zeus', text: '', expected: DEFAULT_NODE_TITLE_SIZE * 1.25 },
    { title: '', text: 'Thunder', expected: DEFAULT_NODE_FONT_SIZE * 1.5 },
    { title: 'Olympus', text: 'Sky', expected: DEFAULT_NODE_TITLE_SIZE * 1.25 + DEFAULT_NODE_FONT_SIZE * 1.5 + 5 },
  ];
  for (const item of cases) {
    Object.assign(node, item); const box = sizingBounds(project, node), occupancy = item.expected / (box.height - 2);
    assert.ok(occupancy >= DEFAULT_TEXT_OCCUPANCY - .015 && occupancy <= DEFAULT_TEXT_OCCUPANCY + .001, `${item.title || 'Body'}: ${occupancy}`);
  }
});

test('hidden or missing titles do not drive width and manual geometry remains authoritative', () => {
  const { project, node } = fixture('A title that should never be compressed into a small old content cap');
  node.text = 'Sky'; const visible = sizingBounds(project, node);
  node.showTitle = false; const hidden = sizingBounds(project, node); assert.ok(hidden.width < visible.width / 5);
  node.showTitle = true; node.sizeMode = 'fixed'; node.width = 143; node.height = 91;
  assert.deepEqual(sizingBounds(project, node), { id: node.id, x: node.x, y: node.y, width: 143, height: 91 });
  assert.deepEqual(nodeTypography(node), { fontSize: 22, titleSize: 32, textAlign: 'center' });
});

test('explicit typography, alignment, multiline titles and media presentation keep their authored meaning', () => {
  const { project, node } = fixture('A long first line\nSecond');
  node.style = { ...node.style, titleSize: 44, fontSize: 28, textAlign: 'right' };
  assert.deepEqual(nodeTypography(node), { fontSize: 28, titleSize: 44, textAlign: 'right' });
  assert.equal(nodeContentLayout(project, node).headerHeight, 44 * 1.25 * 2 + 5);
  project.assets.push({ id: 'source', name: 'Account.md', mime: 'text/markdown', base64: btoa('# Zeus') });
  node.contentItems = [{ id: 'document', type: 'asset', assetId: 'source' }];
  assert.equal(balancedTextLayout(project, node), false);
  node.contentItems = [{ id: 'short', type: 'text', text: 'A concise account.' }]; assert.equal(balancedTextLayout(project, node), true);
  node.kind = 'container'; node.collapsed = false; assert.equal(balancedTextLayout(project, node), false);
});

test('balanced typography is portable and regular text measurement does not scan unrelated Work nodes', () => {
  const { project, node } = fixture(); const saved = commitSnapshots(project);
  const reopened = readPublication(buildPublication(saved, '', ''));
  assert.deepEqual(reopened.nodes.find(item => item.title === node.title)?.style, node.style);
  assert.deepEqual(forkProject(saved).nodes.find(item => item.title === 'Zeus')?.style, node.style);
  let visits = 0;
  const unrelated = Array.from({ length: 50_000 }, (_, index) => ({ ...node, id: `unrelated-${index}` }));
  project.nodes = new Proxy([...project.nodes, ...unrelated], { get(target, property, receiver) {
    if (typeof property === 'string' && /^\d+$/.test(property)) visits++;
    return Reflect.get(target, property, receiver);
  } });
  const measured = nodeContentLayout(project, node); assert.ok(measured.width > 0); assert.equal(visits, 0);
});
