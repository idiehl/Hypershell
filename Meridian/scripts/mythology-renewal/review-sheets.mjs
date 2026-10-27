/** Compose contact sheets from actual reader captures; never changes project data. */
import {readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {chromium} from '@playwright/test';
const directory=path.resolve(process.argv[2]);
const report=JSON.parse(await readFile(path.join(directory,'review.json'),'utf8'));
const browser=await chromium.launch({headless:true});
try {
 const page=await browser.newPage({viewport:{width:1800,height:1300},deviceScaleFactor:1});
 for(let i=0;i<report.canvases.length;i+=6){
  const group=report.canvases.slice(i,i+6),file=path.join(directory,`sheet-${Math.floor(i/6)+1}.html`);
  const escape=s=>s.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('"','&quot;');
  await writeFile(file,`<!doctype html><meta charset="utf-8"><style>body{margin:0;background:#283c41;color:white;font:18px system-ui;display:grid;grid-template-columns:repeat(3,600px)}figure{margin:12px 8px}figcaption{height:32px}img{width:584px;border:1px solid #6a8183}</style>${group.map(c=>`<figure><figcaption>${escape(c.title)}</figcaption><img src="${escape(pathToFileURL(path.join(directory,c.screenshot)).href)}"></figure>`).join('')}`);
  await page.goto(pathToFileURL(file).href);await page.locator('img').evaluateAll(items=>Promise.all(items.map(i=>i.decode())));
  await page.screenshot({path:path.join(directory,`sheet-${Math.floor(i/6)+1}.png`),fullPage:true});
 }
}finally{await browser.close();}
console.log(JSON.stringify({sheets:Math.ceil(report.canvases.length/6)}));
