import { test, expect } from '@playwright/test';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import path from 'node:path';
import { applyRecipe } from '../src/core/authoring.ts';
import { addNode, newProject } from '../src/core/model.ts';

function cli(args: string[]) {
  return new Promise<{ code: number | null; output: string }>((resolve, reject) => {
    const child = spawn(process.execPath, ['--experimental-strip-types', 'scripts/author.mjs', ...args], { windowsHide: true });
    let output = ''; child.stdout.on('data', chunk => output += chunk); child.stderr.on('data', chunk => output += chunk);
    child.on('error', reject); child.on('close', code => resolve({ code, output }));
  });
}

test('author capture reveals a hidden Gallery page through real viewer navigation and records a requested reading scale', async ({}, info) => {
  const base = newProject('Focused authoring review'), canvas = base.entryCanvasId;
  const result = applyRecipe(base, { version: 1, nodes: [
    { key: 'parent', canvas, kind: 'container', title: 'Hidden branch', x: 100, y: 100, width: 1000, height: 800, collapsed: true },
    { key: 'gallery', canvas, parent: 'parent', kind: 'container', title: '', tag: 'Gallery collection', containerVariant: 'gallery', x: 130, y: 130, width: 760, height: 600 },
    { key: 'first', canvas, parent: 'gallery', kind: 'media', title: 'First page', tag: 'First handle', text: 'Page one', x: 150, y: 150 },
    { key: 'focus', canvas, parent: 'gallery', kind: 'media', title: 'Second page target', tag: 'Precise target', text: 'Readable text on the requested page.', x: 150, y: 150 },
  ] });
  // Large source packets legitimately mention many graph GUIDs. The requested
  // identity must stay ahead of >100 attachment matches in actual viewer search.
  result.project.assets.push({ id: 'complete-source-packet', name: 'Complete source packet.md', mime: 'text/markdown', base64: Buffer.from(`A source note about ${result.aliases.nodes.focus}`).toString('base64') });
  for (let index = 0; index < 125; index++) {
    const packet = addNode(result.project, canvas, 'media', `Source packet ${index + 1}`);
    Object.assign(packet, { contentType: 'asset', assetId: 'complete-source-packet', x: 5000 + index * 240, y: 5000, width: 200, height: 120, sizeMode: 'fixed' });
  }
  const input = info.outputPath('source.json'), directory = info.outputPath('focused-review');
  await mkdir(path.dirname(input), { recursive: true }); await writeFile(input, JSON.stringify(result.project));
  const captured = await cli(['capture', '--input', input, '--out', directory, '--work', canvas, '--node', result.aliases.nodes.focus, '--scale', '1']);
  expect(captured.code, captured.output).toBe(0);
  const review = JSON.parse(await readFile(path.join(directory, 'review.json'), 'utf8'));
  expect(review.browserErrors).toEqual([]); expect(review.canvases).toHaveLength(1);
  expect(review.canvases[0]).toMatchObject({ focusNodeId: result.aliases.nodes.focus, requestedScale: 1, zoom: '100%' });
  expect(JSON.stringify(review.canvases[0].nodes)).toContain('Second page target');
  const png = await readFile(path.join(directory, review.canvases[0].screenshot)); expect(png.subarray(1, 4).toString()).toBe('PNG');
  expect(JSON.parse(await readFile(input, 'utf8'))).toEqual(result.project);
  await info.attach('Focused standalone reader', { path: path.join(directory, review.canvases[0].screenshot), contentType: 'image/png' });
});

test('author capture fits a selected branch and refuses ambiguous Tags before writing any preview', async ({}, info) => {
  const base = newProject('Branch review'), canvas = base.entryCanvasId;
  const result = applyRecipe(base, { version: 1, nodes: [
    { key: 'parent', canvas, kind: 'media', title: 'Branch parent', tag: 'Branch', x: 100, y: 100, width: 300, height: 180 },
    { key: 'child', canvas, parent: 'parent', kind: 'media', title: 'An exterior child', tag: 'Shared', x: 600, y: 180 },
    { key: 'peer', canvas, kind: 'media', title: 'A distant peer', tag: 'Shared', x: 4000, y: 1500 },
  ] });
  const input = info.outputPath('source.json'), directory = info.outputPath('branch-review');
  await mkdir(path.dirname(input), { recursive: true }); await writeFile(input, JSON.stringify(result.project));
  let captured = await cli(['capture', '--input', input, '--out', directory, '--canvas', canvas, '--node', 'Branch', '--scope', 'branch']);
  expect(captured.code, captured.output).toBe(0);
  const review = JSON.parse(await readFile(path.join(directory, 'review.json'), 'utf8'));
  expect(review.canvases[0]).toMatchObject({ focusNodeId: result.aliases.nodes.parent, scope: 'branch' });
  const nodes = review.canvases[0].nodes, parent = nodes.find((node: { id: string }) => node.id === result.aliases.nodes.parent), child = nodes.find((node: { id: string }) => node.id === result.aliases.nodes.child);
  for (const node of [parent, child]) { expect(node.screen.x).toBeGreaterThanOrEqual(0); expect(node.screen.x + node.screen.width).toBeLessThanOrEqual(1600); }
  captured = await cli(['capture', '--input', input, '--out', info.outputPath('ambiguous'), '--node', 'Shared']);
  expect(captured.code).toBe(1); expect(captured.output).toContain('must match exactly one ID or Tag; found 2');
});
