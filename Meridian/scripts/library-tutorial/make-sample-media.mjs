/** Small original, offline media for the import/preview exercises. */
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';
const directory = fileURLToPath(new URL('../../output/library-tutorial-20261021/tutorial/', import.meta.url));
await mkdir(directory, { recursive: true });
const rate = 16000, length = rate, wav = Buffer.alloc(44 + length * 2);
wav.write('RIFF'); wav.writeUInt32LE(wav.length - 8, 4); wav.write('WAVEfmt ', 8);
wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22);
wav.writeUInt32LE(rate, 24); wav.writeUInt32LE(rate * 2, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34); wav.write('data', 36); wav.writeUInt32LE(length * 2, 40);
for (let index = 0; index < length; index++) wav.writeInt16LE(Math.round(Math.sin(index / rate * Math.PI * 2 * 220) * Math.sin(Math.PI * index / length) * 1800), 44 + index * 2);
await writeFile(path.join(directory, 'tutorial-tone.wav'), wav);
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage(); await page.setContent('<canvas width="640" height="360"></canvas>');
  const bytes = await page.evaluate(async () => {
    const canvas = document.querySelector('canvas'), ctx = canvas.getContext('2d', { alpha: false });
    ctx.fillStyle = '#101D25'; ctx.fillRect(0, 0, 640, 360);
    const stream = canvas.captureStream(0), track = stream.getVideoTracks()[0], chunks = [], recorder = new MediaRecorder(stream, { mimeType: 'video/webm;codecs=vp8', videoBitsPerSecond: 300000 });
    recorder.ondataavailable = event => { if (event.data.size) chunks.push(event.data); };
    const completed = new Promise(resolve => { recorder.onstop = async () => resolve([...new Uint8Array(await new Blob(chunks, { type: 'video/webm' }).arrayBuffer())]); });
    const started = performance.now(); recorder.start(100);
    await new Promise(resolve => {
      function draw(now) {
        const t = Math.min(1, (now - started) / 2400);
        ctx.fillStyle = '#101D25'; ctx.fillRect(0, 0, 640, 360);
        ctx.fillStyle = '#E6BA73'; ctx.font = 'bold 32px sans-serif'; ctx.fillText('A local video attachment', 38, 64);
        ctx.fillStyle = '#B6C8CE'; ctx.font = '20px sans-serif'; ctx.fillText('Play, pause, seek — original bytes stay embedded', 38, 105);
        ctx.strokeStyle = '#728F96'; ctx.lineWidth = 3; ctx.strokeRect(38, 245, 564, 28);
        ctx.fillStyle = '#82CFC5'; ctx.fillRect(42, 249, 556 * t, 20);
        ctx.beginPath(); ctx.arc(70 + t * 500, 175, 20, 0, Math.PI * 2); ctx.fill();
        ctx.fillStyle = '#E5EEF1'; ctx.font = '16px monospace'; ctx.fillText(`${(t * 2.4).toFixed(1)} seconds`, 38, 315);
        track.requestFrame();
        if (t < 1) requestAnimationFrame(draw); else resolve();
      }
      requestAnimationFrame(draw);
    });
    recorder.stop(); const result = await completed; stream.getTracks().forEach(track => track.stop()); return result;
  });
  await writeFile(path.join(directory, 'tutorial-motion.webm'), Buffer.from(bytes));
  console.log(JSON.stringify({ directory, videoBytes: bytes.length, audioBytes: wav.length }));
} finally { await browser.close(); }
