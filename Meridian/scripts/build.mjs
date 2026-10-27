import { build } from 'esbuild';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
const brandAssets = await Promise.all(['meridian.png', 'meridian.ico'].map(async name => {
  const bytes = await readFile(`assets/brand/${name}`);
  const valid = name.endsWith('.png')
    ? bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]))
    : bytes.length >= 6 && bytes.readUInt16LE(0) === 0 && bytes.readUInt16LE(2) === 1 && bytes.readUInt16LE(4) > 0;
  if (!valid) throw new Error(`Meridian brand asset has an invalid file header: ${name}`);
  return { name, bytes };
}));
await mkdir('dist', { recursive: true });
await mkdir('dist/brand', { recursive: true });
await Promise.all(brandAssets.map(({ name, bytes }) => writeFile(`dist/brand/${name}`, bytes)));
await build({ entryPoints: ['src/viewer.ts'], bundle: true, format: 'iife', target: ['es2022'], outfile: 'dist/viewer.js', minify: true, legalComments: 'none' });
await build({ entryPoints: ['src/editor.ts'], bundle: true, format: 'iife', target: ['es2022'], outfile: 'dist/editor.js', sourcemap: true });
const sceneCss = (await readFile('src/scene.css','utf8')) + '\n' + (await readFile('src/grid.css','utf8')) + '\n' + (await readFile('src/gallery-scene.css','utf8')) + '\n' + (await readFile('src/backgrounds.css','utf8')) + '\n' + (await readFile('src/window-scene.css','utf8')) + '\n' + (await readFile('src/canvas-launcher.css','utf8')) + '\n' + (await readFile('src/expansion-tray-scene.css','utf8')) + '\n' + (await readFile('src/cluster-button.css','utf8'));
const sharedSceneCss = sceneCss + '\n' + (await readFile('src/smart-links.css','utf8')) + '\n' + (await readFile('src/hover-readability.css','utf8'));
const css = sharedSceneCss + '\n' + (await readFile('src/editor.css','utf8')) + '\n' + (await readFile('src/module.css','utf8')) + '\n' + (await readFile('src/right-panel.css','utf8')) + '\n' + (await readFile('src/workspace-tools.css','utf8')) + '\n' + (await readFile('src/creation.css','utf8')) + '\n' + (await readFile('src/inspector-panels.css','utf8')) + '\n' + (await readFile('src/inspector-workspace.css','utf8')) + '\n' + (await readFile('src/gallery-ui.css','utf8'));
await writeFile('dist/app.css', css + '\n' + (await readFile('src/font-picker.css','utf8')) + '\n' + (await readFile('src/rich-text-editor.css','utf8')) + '\n' + (await readFile('src/canvas-text-editing.css','utf8')) + '\n' + (await readFile('src/application-chrome.css','utf8')) + '\n' + (await readFile('src/node-hub-layout.css','utf8')) + '\n' + (await readFile('src/node-hub.css','utf8')));
await writeFile('dist/scene.css', sharedSceneCss);
await writeFile('dist/index.html', '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="theme-color" content="#10151d"><title>Atlas Meridian</title><link rel="icon" type="image/x-icon" href="/brand/meridian.ico"><link rel="icon" type="image/png" href="/brand/meridian.png"><link rel="stylesheet" href="/app.css"></head><body><div id="app"></div><script src="/editor.js" defer></script></body></html>');
console.log('Meridian editor and standalone viewer built.');
