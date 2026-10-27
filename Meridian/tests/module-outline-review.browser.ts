import { test, expect } from '@playwright/test';
import { writeFile } from 'node:fs/promises';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
test('read-only review records selected outlines for default border and explicit silhouettes', async ({ page, request }, info) => {
  const project = newProject('Module outline inspection');
  const add = (kind: Parameters<typeof addNode>[2], title: string, x: number, y: number, width: number, height: number) => {
    const node = addNode(project, project.entryCanvasId, kind, title); Object.assign(node, { x, y, width, height, sizeMode: 'fixed', childPosition: 'manual' }); return node;
  };
  const parent = add('container', 'Default bordered parent', 60, 70, 720, 360); Object.assign(parent, { collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free' });
  const a = add('media', 'Default bordered child', 80, 145, 180, 180), b = add('media', 'Explicit rectangle', 280, 145, 180, 180), c = add('media', 'Explicit circle', 480, 145, 180, 180);
  for (const node of [a,b,c]) { node.parentId = parent.id; node.text = 'Selected'; }
  b.style = { shape: 'rectangle' }; c.style = { shape: 'circle' };
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.status()).toBe(201); const saved = await response.json();
  await page.goto(`/?project=${saved.id}`); await page.getByRole('button', { name: 'New Node', exact: true }).click(); await page.getByRole('menuitem', { name: 'Create Module', exact: true }).click();
  await page.locator(`.ms-node[data-node-id="${parent.id}"] > .ms-node-header > .ms-node-title`).click();
  await expect(page.locator('.ms-module-selected')).toHaveCount(4);
  const details = await page.locator('.ms-module-selected').evaluateAll(cards => cards.map(card => {
    const outline = card.querySelector(':scope > .ms-node-outline')!, path = outline.querySelector('path')!, rect = card.getBoundingClientRect(), svg = outline.getBoundingClientRect();
    const cardStyle = getComputedStyle(card), outlineStyle = getComputedStyle(outline), pathStyle = getComputedStyle(path), box = (path as SVGPathElement).getBBox();
    return { title: card.querySelector('.ms-node-title')?.textContent, class: card.className, shape: (card as HTMLElement).dataset.shape, card: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }, svg: { x: svg.x, y: svg.y, width: svg.width, height: svg.height },
      overflow: cardStyle.overflow, borderWidth: cardStyle.borderWidth, outline: { display: outlineStyle.display, opacity: outlineStyle.opacity, visibility: outlineStyle.visibility, position: outlineStyle.position, zIndex: outlineStyle.zIndex },
      path: { d: path.getAttribute('d'), stroke: pathStyle.stroke, strokeWidth: pathStyle.strokeWidth, opacity: pathStyle.opacity, strokeOpacity: pathStyle.strokeOpacity, animation: pathStyle.animationName, bbox: { x: box.x, y: box.y, width: box.width, height: box.height } } };
  }));
  await writeFile(info.outputPath('outline-details.json'), JSON.stringify(details, null, 2));
  await page.screenshot({ path: info.outputPath('selected-default-and-shaped-nodes.png') });
  await page.emulateMedia({ reducedMotion: 'reduce' }); await page.screenshot({ path: info.outputPath('selected-reduced-motion.png') });
  // Read-only layout probe: remove clipping in this fixture's DOM only.
  await page.addStyleTag({ content: '.ms-module-selected{overflow:visible!important}' }); await page.screenshot({ path: info.outputPath('selected-overflow-visible-probe.png') });
});
