import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';

let script: string, css: string;
test.beforeAll(async()=>{
  script=(await build({stdin:{contents:"import {canvasLauncher,disposePortalLaunchers} from './src/canvas-launcher.ts';import {renderGalleryTile} from './src/gallery-scene.ts';window.portalHarness={canvasLauncher,disposePortalLaunchers,renderGalleryTile};",resolveDir:process.cwd(),loader:'ts'},bundle:true,write:false,format:'iife',platform:'browser',target:'es2022'})).outputFiles[0]!.text;
  css=(await Promise.all(['canvas-launcher.css','gallery-scene.css'].map(file=>readFile(`src/${file}`,'utf8')))).join('\n');
});
async function mount(page:Page){
  await page.setContent('<style>html,body{margin:0;background:#101d29}.meridian-scene{min-height:600px;display:flex;align-items:center;justify-content:center;gap:30px}.sample{display:grid;place-items:center;width:260px;height:280px;border:1px solid #40586b;background:#172a36}.sample:nth-child(2){background:#fff8e7;border-color:#c4b897}.sample:nth-child(3){background:#193e34;border-color:#45806f}</style><main class="meridian-scene"><section class="sample"></section><section class="sample"></section><section class="sample"></section><section class="sample"></section></main>');
  await page.addStyleTag({content:css});await page.addScriptTag({content:script});
  await page.evaluate(()=>{
    (window as any).portalActivations=[];
    for(const [index,host]of [...document.querySelectorAll('.sample')].entries()){
      const style=['doorway','passage','wormhole','vortex'][index];
      const portal=(window as any).portalHarness.canvasLauncher(`Open destination ${index+1}`,()=> (window as any).portalActivations.push(index),{style,destinationName:`The destination canvas ${index+1}`});
      Object.assign(portal.style,{width:'170px',height:'170px',minWidth:'0',minHeight:'0'});host.append(portal);
    }
  });
}

for(const [index,kind]of ['doorway','passage','wormhole','vortex'].entries())test(`${kind} continuously travels, accelerates without a phase restart, and shows the destination above the button`,async({page},info)=>{
  await page.emulateMedia({reducedMotion:'no-preference'});await mount(page);
  const portal=page.locator(`.ms-canvas-open[data-portal-style="${kind}"]`),art=portal.locator('svg');
  await expect(portal).toHaveAttribute('aria-label',`Open The destination canvas ${index+1}`);await expect(art).toHaveAttribute('aria-hidden','true');
  await expect(portal).toHaveAttribute('data-portal-rate','1.0000');
  const clock=()=>portal.evaluate(button=>button.getAnimations({subtree:true}).filter(a=>a.effect?.getTiming().iterations===Infinity).map(a=>({time:Number(a.currentTime),state:a.playState,rate:a.playbackRate})));
  const before=await clock();expect(before.length).toBeGreaterThan(5);expect(before.every(a=>a.state==='running')).toBe(true);
  await page.waitForTimeout(130);const moving=await clock();expect(moving[0]!.time).toBeGreaterThan(before[0]!.time);
  await portal.hover();await expect.poll(async()=>Number(await portal.getAttribute('data-portal-rate'))).toBe(4);
  await expect.poll(()=>portal.locator('.ms-portal-approach').evaluate(element=>new DOMMatrixReadOnly(getComputedStyle(element).transform).a)).toBeCloseTo(1.18,3);
  const label=page.locator('.ms-canvas-destination');await expect(label).toHaveText(`The destination canvas ${index+1}`);
  const text=await label.boundingBox(),box=await portal.boundingBox();expect(text!.y+text!.height).toBeLessThan(box!.y);
  expect((await clock())[0]!.time).toBeGreaterThan(moving[0]!.time);expect((await clock())[0]!.rate).toBe(4);
  await page.screenshot({path:info.outputPath(`${kind}-approaching.png`)});
  await page.mouse.move(5,5);await portal.focus();await expect(portal).toHaveAttribute('data-portal-active','true');await page.keyboard.press('Enter');
  expect(await page.evaluate(()=>(window as any).portalActivations)).toEqual([index]);
});

test('offscreen travel pauses, resumes its phase, and scene removal cancels animations and the screen-space label',async({page})=>{
  await mount(page);const portal=page.locator('.ms-canvas-open').first();
  await portal.hover();await expect(page.locator('.ms-canvas-destination')).toBeVisible();
  await page.evaluate(()=>{const button=document.querySelector('.ms-canvas-open')!;(window as any).heldAnimations=button.getAnimations({subtree:true}).filter(a=>a.effect?.getTiming().iterations===Infinity);(button.parentElement as HTMLElement).style.transform='translateY(1800px)';});
  await expect(portal).toHaveAttribute('data-portal-paused','true');await expect(page.locator('.ms-canvas-destination')).toHaveCount(0);
  // pause() schedules a pending pause task; the browser settles its hold time on
  // the next rendering opportunity. Wait for ready before comparing exact phase.
  const times=()=>page.evaluate(async()=>{const animations=(window as any).heldAnimations as Animation[];await Promise.all(animations.map(a=>a.ready));return animations.map(a=>a.currentTime);});
  await page.evaluate(async()=>{const animations=(window as any).heldAnimations as Animation[];await Promise.all(animations.map(a=>a.ready));if(animations.some(a=>a.playState!=='paused'))throw new Error('Offscreen travel did not finish pausing.');});
  const paused=await times();await page.waitForTimeout(100);expect(await times()).toEqual(paused);
  await portal.evaluate(button=>{button.parentElement!.style.transform='none';});await expect(portal).toHaveAttribute('data-portal-paused','false');
  await expect.poll(times).not.toEqual(paused);
  await portal.focus();await expect(page.locator('.ms-canvas-destination')).toBeVisible();
  await portal.evaluate(button=>{button.parentElement!.style.visibility='hidden';});
  await expect(portal).toHaveAttribute('data-portal-paused','true');await expect(page.locator('.ms-canvas-destination')).toHaveCount(0);
  await portal.evaluate(button=>{button.parentElement!.style.visibility='visible';});await expect(portal).toHaveAttribute('data-portal-paused','false');
  await portal.evaluate(button=>{button.parentElement!.style.display='none';});await expect(portal).toHaveAttribute('data-portal-paused','true');
  await portal.evaluate(button=>{button.parentElement!.style.display='grid';});await expect(portal).toHaveAttribute('data-portal-paused','false');
  await portal.hover();await expect(page.locator('.ms-canvas-destination')).toBeVisible();await page.evaluate(()=>document.querySelector('.meridian-scene')!.remove());
  await expect(page.locator('.ms-canvas-destination')).toHaveCount(0);
  await expect.poll(()=>page.evaluate(()=>(window as any).heldAnimations.every((a:Animation)=>a.playState==='idle'))).toBe(true);
});

test('Gallery replacement cancels removed portals, and reduced motion and hidden documents stay static',async({page})=>{
  await page.emulateMedia({reducedMotion:'reduce'});await mount(page);const portal=page.locator('.ms-canvas-open').first();
  await portal.hover();await expect(portal).toHaveAttribute('data-portal-reduced','true');await expect(portal).toHaveAttribute('data-portal-paused','true');
  await expect(portal.locator('.ms-portal-approach')).toHaveCSS('transform','none');await expect(page.locator('.ms-canvas-destination')).toBeVisible();
  const times=()=>portal.evaluate(async button=>{const animations=button.getAnimations({subtree:true}).filter(a=>a.effect?.getTiming().iterations===Infinity);await Promise.all(animations.map(a=>a.ready));return animations.map(a=>a.currentTime);});const paused=await times();await page.waitForTimeout(100);expect(await times()).toEqual(paused);
  await page.emulateMedia({reducedMotion:'no-preference'});await expect(portal).toHaveAttribute('data-portal-paused','false');
  await page.evaluate(()=>{Object.defineProperty(document,'hidden',{configurable:true,get:()=>true});document.dispatchEvent(new Event('visibilitychange'));});
  await expect(portal).toHaveAttribute('data-portal-paused','true');await expect(page.locator('.ms-canvas-destination')).toHaveCount(0);
  await page.evaluate(()=>{delete (document as any).hidden;document.dispatchEvent(new Event('visibilitychange'));});
  const project=newProject('Gallery lifecycle'),gallery=addNode(project,project.entryCanvasId,'container','Pages');gallery.containerVariant='gallery';gallery.collapsed=false;
  for(const style of ['wormhole','vortex']as const){const item=addNode(project,project.entryCanvasId,'canvas-link',style);item.parentId=gallery.id;item.style={portalStyle:style};}
  await page.evaluate(({project,gallery})=>{const host=document.createElement('div');host.id='gallery-lifecycle';document.body.append(host);(window as any).portalHarness.renderGalleryTile(host,project,gallery,{localItems:new Map(),onCanvasOpen:()=>{},imageGesture:()=>{}});},{project,gallery});
  const scope=page.locator('#gallery-lifecycle');await expect(scope.locator('.ms-canvas-open')).toHaveAttribute('data-portal-style','wormhole');
  await scope.locator('.ms-canvas-open').evaluate(button=>{(window as any).oldGalleryMotion=button.getAnimations({subtree:true});});
  await scope.getByRole('button',{name:'Next object in Pages'}).click();await expect(scope.locator('.ms-canvas-open')).toHaveAttribute('data-portal-style','vortex');
  expect(await page.evaluate(()=>(window as any).oldGalleryMotion.every((a:Animation)=>a.playState==='idle'))).toBe(true);
});
