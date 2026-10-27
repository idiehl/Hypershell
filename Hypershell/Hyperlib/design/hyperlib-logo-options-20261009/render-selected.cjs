const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs = require('node:fs');
const path = require('node:path');

(async () => {
  const source = fs.readFileSync(path.join(__dirname, '03-shell-pages.svg'), 'utf8')
    .replace('Hyperlib concept: Shell pages', 'Hyperlib Shell pages');
  const output = path.join(__dirname, 'selected-shell-pages');
  fs.mkdirSync(output, { recursive: true });
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const browser = await chromium.launch({ headless: true, channel: 'msedge' });
  const frames = [];
  try {
    const page = await browser.newPage({ viewport: { width: 300, height: 300 }, deviceScaleFactor: 1 });
    for (const size of sizes) {
      await page.setContent(`<style>html,body{margin:0;background:transparent}svg{display:block;width:${size}px;height:${size}px}</style>${source}`);
      const frame = await page.locator('svg').screenshot({ omitBackground: true });
      if (frame.readUInt32BE(16) !== size || frame.readUInt32BE(20) !== size) throw new Error(`Wrong ${size}px frame`);
      fs.writeFileSync(path.join(output, `hyperlib-${size}.png`), frame);
      frames.push(frame);
    }
  } finally { await browser.close(); }
  // Store each directly rasterized PNG byte for byte as a 32-bit icon frame.
  const header = Buffer.alloc(6 + 16 * frames.length);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(frames.length, 4);
  let offset = header.length;
  frames.forEach((frame, index) => {
    const entry = 6 + index * 16;
    header[entry] = header[entry + 1] = sizes[index] === 256 ? 0 : sizes[index];
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frame.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += frame.length;
  });
  const ico = Buffer.concat([header, ...frames]);
  frames.forEach((frame, index) => {
    const entry = 6 + index * 16;
    const start = ico.readUInt32LE(entry + 12);
    const length = ico.readUInt32LE(entry + 8);
    if (!ico.subarray(start, start + length).equals(frame)) throw new Error('ICO frame mismatch');
  });
  const assets = path.resolve(__dirname, '../../../src/Hypershell/Assets/MyNet');
  fs.writeFileSync(path.join(assets, 'hyperlib.ico'), ico);
  fs.writeFileSync(path.join(assets, 'hyperlib.svg'), source);
  fs.copyFileSync(path.join(output, 'hyperlib-256.png'), path.join(assets, 'hyperlib.png'));
  fs.writeFileSync(path.join(output, 'hyperlib.ico'), ico);
  fs.writeFileSync(path.join(output, 'hyperlib.svg'), source);
  console.log('SELECTED_HYPERLIB_ICON_OK concept=03 sizes=' + sizes.join(',') + ' direct-vector=true png-payloads-identical=true');
})().catch(error => { console.error(error); process.exitCode = 1; });
