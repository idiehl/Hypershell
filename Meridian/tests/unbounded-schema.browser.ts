import { test, expect, type APIRequestContext } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianProject, MeridianNode } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Unbounded host schema QA');
  const parent = addNode(project, project.entryCanvasId, 'media', 'Unbounded window');
  Object.assign(parent, { contentType: 'asset', assetId: 'host-photo', text: 'Preserved inactive caption', containerVariant: 'window', childLayout: 'spatial', childArrangement: 'grid', childOffset: { x: 230, y: 40 },
    grid: { columns: 3, rows: 4, cellWidth: 180, cellHeight: 100, windowColumns: 2, windowRows: 2, scrollDirection: 'vertical' }, rules: { nodeKinds: ['media'] } });
  const child = addNode(project, project.entryCanvasId, 'media', 'Manual cell');
  Object.assign(child, { parentId: parent.id, contentType: 'link', childPosition: 'manual', hyperlinks: [{ url: 'https://example.org/document', label: 'One document' }], cell: { column: 1, row: 1 } });
  const automatic = addNode(project, project.entryCanvasId, 'media', 'Automatic cell');
  Object.assign(automatic, { parentId: parent.id, contentType: 'text', text: 'One text item', childPosition: 'auto', cell: { column: 2, row: 1 } });
  project.assets.push({ id: 'host-photo', name: 'image.png', mime: 'image/png', base64: 'AQID' });
  return { project: commitSnapshots(project), parent, child, automatic };
}

async function stored(request: APIRequestContext, id: string): Promise<MeridianProject> {
  const response = await request.get(`/api/projects/${id}`);
  expect(response.ok()).toBeTruthy();
  return await response.json();
}

test('host saves and reopens unbounded display patterns and single-content choices without discarding legacy inactive fields', async ({ request }) => {
  const f = fixture();
  const response = await request.post('/api/projects', { data: f.project });
  expect(response.ok(), await response.text()).toBeTruthy();
  const original = await stored(request, f.project.id);
  expect(original.nodes.find(node => node.id === f.parent.id)).toMatchObject({ kind: 'media', contentType: 'asset', text: 'Preserved inactive caption', childLayout: 'spatial', containerVariant: 'window', childOffset: { x: 230, y: 40 } });
  expect(original.nodes.find(node => node.id === f.child.id)).toMatchObject({ contentType: 'link', childPosition: 'manual', cell: { column: 1, row: 1 } });
  expect(original.snapshots[f.automatic.id].node).toMatchObject({ childPosition: 'auto', contentType: 'text' });
  expect(original.assets).toEqual(f.project.assets);
  const updated = structuredClone(original);
  updated.nodes.find(node => node.id === f.child.id)!.contentType = 'text';
  updated.nodes.find(node => node.id === f.child.id)!.text = 'A replacement chosen explicitly';
  const put = await request.put(`/api/projects/${original.id}`, { data: { project: updated, expectedRevision: original.revision } });
  expect(put.ok(), await put.text()).toBeTruthy();
  const saved = await stored(request, original.id);
  expect(saved.nodes.find(node => node.id === f.child.id)).toMatchObject({ contentType: 'text', text: 'A replacement chosen explicitly', hyperlinks: f.child.hyperlinks, childPosition: 'manual' });
  expect(saved.assets).toEqual(original.assets);
});

test('host rejects malformed pattern metadata, hierarchy and permissions atomically for live nodes and saved observations', async ({ request }) => {
  const f = fixture();
  const created = await request.post('/api/projects', { data: f.project });
  expect(created.ok(), await created.text()).toBeTruthy();
  const original = await stored(request, f.project.id);
  const parent = (candidate: MeridianProject): MeridianNode => candidate.nodes.find(node => node.id === f.parent.id)!;
  const child = (candidate: MeridianProject): MeridianNode => candidate.nodes.find(node => node.id === f.child.id)!;
  const invalid: Array<(candidate: MeridianProject) => void> = [
    candidate => { Object.assign(parent(candidate), { contentType: 'mixed' }); },
    candidate => { Object.assign(parent(candidate), { contentType: ['asset'] }); },
    candidate => { Object.assign(child(candidate), { childPosition: 'drag' }); },
    candidate => { Object.assign(parent(candidate), { childOffset: { x: 0 } }); },
    candidate => { Object.assign(parent(candidate), { childOffset: { x: 0, y: 0, script: 'bad' } }); },
    candidate => { Object.assign(parent(candidate), { childOffset: { x: 1e10, y: 0 } }); },
    candidate => { Object.assign(parent(candidate), { childLayout: 'contained' }); },
    candidate => { Object.assign(parent(candidate), { containerVariant: 'unknown' }); },
    candidate => { candidate.nodes[0].containerVariant = 'horizontal'; },
    candidate => { delete parent(candidate).grid!.windowRows; },
    candidate => { parent(candidate).parentId = f.child.id; },
    candidate => { child(candidate).kind = 'number'; },
    candidate => { candidate.nodes.find(node => node.id === f.automatic.id)!.cell = { column: 1, row: 1 }; },
    candidate => { Object.assign(candidate.snapshots[f.child.id].node, { childPosition: null }); },
    candidate => { Object.assign(candidate.snapshots[f.parent.id].node, { contentType: 'both' }); },
  ];
  for (const mutate of invalid) {
    const candidate = structuredClone(original); mutate(candidate);
    const rejected = await request.put(`/api/projects/${original.id}`, { data: { project: candidate, expectedRevision: original.revision } });
    expect(rejected.status(), await rejected.text()).toBe(400);
    expect(await stored(request, original.id)).toEqual(original);
  }
});
