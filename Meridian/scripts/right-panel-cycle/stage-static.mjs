import { cp, mkdir, readdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { gzipSync, brotliCompressSync, constants } from 'node:zlib';

const root = path.resolve('.');
if (root.toLowerCase() !== 'i:\\dev\\meridian') throw new Error('Unexpected workspace.');
const stage = path.join(root, '.cache', 'right-panel-package', 'wwwroot');
await mkdir(stage, { recursive: true });
await cp(path.join(root, 'dist'), stage, { recursive: true });
const files = await readdir(stage, { recursive: true, withFileTypes: true });
let compressed = 0;
for (const entry of files) {
  if (!entry.isFile() || !/\.(css|js|map|html|ico)$/.test(entry.name)) continue;
  const target = path.join(entry.parentPath, entry.name), bytes = await readFile(target);
  await writeFile(`${target}.gz`, gzipSync(bytes, { level: 9 }));
  await writeFile(`${target}.br`, brotliCompressSync(bytes, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }));
  compressed++;
}
console.log(`Staged fresh static assets with ${compressed} matching gzip/Brotli pairs.`);
