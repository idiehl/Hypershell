import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import type { MeridianAsset } from '../src/core/types.ts';

let script:string,css:string;
test.beforeAll(async()=>{
  css=await readFile('dist/scene.css','utf8');
  script=(await build({stdin:{contents:`import {renderScene} from './src/scene.ts';window.backgroundHarness={renderScene};`,resolveDir:process.cwd(),loader:'ts'},bundle:true,write:false,format:'iife',platform:'browser'})).outputFiles[0].text;
});
const png:MeridianAsset={id:'photo',name:'tiny.png',mime:'image/png',base64:'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfyoAAAAASUVORK5CYII='};
test('background paint is shared across canvases, shaped nodes and gallery objects',async({page},testInfo)=>{
  const p=newProject('Background sampler');p.canvases[0].style={background:'#11252A',backgroundPattern:'grid',backgroundScale:64,backgroundTint:'#2D565C'};
  ['dots','grid','isometric','linen','contours','aurora','stars'].forEach((pattern,index)=>{
    const n=addNode(p,p.entryCanvasId,'text',pattern);Object.assign(n,{x:60+(index%4)*295,y:80+Math.floor(index/4)*255,width:255,height:200,text:'A quiet backdrop for connected ideas.',style:{fill:'#192D34',textColor:'#D5ECEB',backgroundPattern:pattern,backgroundScale:pattern==='grid'?32:128,backgroundOpacity:.6,shape:index===2?'hexagon':index===4?'ellipse':'rounded',titleSize:21}});
  });
  p.assets.push(png);
  const gallery=addNode(p,p.entryCanvasId,'container','Gallery');Object.assign(gallery,{containerVariant:'gallery',x:945,y:335,width:255,height:200,collapsed:false});
  const child=addNode(p,p.entryCanvasId,'text','Gallery child');Object.assign(child,{parentId:gallery.id,text:'This background belongs to the current object.',style:{backgroundPattern:'aurora',backgroundScale:128}});
  await page.setContent('<style>html,body{margin:0}#scene{height:790px;width:1300px}</style><div id="scene"></div>');await page.addStyleTag({content:css});await page.addScriptTag({content:script});
  await page.evaluate(p=>(window as any).backgroundHarness.renderScene(document.getElementById('scene'),p,p.entryCanvasId,{view:{x:0,y:0,scale:1}}),p);
  await expect(page.locator('.ms-canvas-background')).toHaveAttribute('data-background-pattern','grid');
  await expect(page.locator('.ms-node-background')).toHaveCount(8);
  const hex=page.locator('.ms-node[data-shape="hexagon"]>.ms-node-background');expect(await hex.evaluate(e=>getComputedStyle(e).clipPath)).toMatch(/^polygon/);
  const ellipse=page.locator('.ms-node[data-shape="ellipse"]>.ms-node-background');expect(await ellipse.evaluate(e=>getComputedStyle(e).clipPath)).toMatch(/^ellipse/);
  await expect(page.locator('.ms-gallery-item>.ms-node-background')).toHaveAttribute('data-background-pattern','aurora');
  await page.screenshot({path:testInfo.outputPath('background-sampler.png')});
  await page.getByRole('button',{name:'Zoom out',exact:true}).click();
  expect(await page.locator('.ms-canvas-background').evaluate(e=>e.style.backgroundSize)).toBe('51.2px 51.2px');
});

test('no-backdrop node hides its image and pinned references use saved background bytes',async({page})=>{
  const p=newProject('Background reference');p.assets.push(png);
  const source=addNode(p,p.entryCanvasId,'text','Source');Object.assign(source,{style:{backgroundAssetId:png.id},x:0,y:0});
  const second=addCanvas(p,'Reference');const ref=addNode(p,second.id,'reference','Pinned');Object.assign(ref,{targetId:source.id,x:80,y:80});
  const transparent=addNode(p,second.id,'text','Floating');Object.assign(transparent,{x:450,y:80,style:{shape:'none',backgroundAssetId:png.id}});
  const gallery=addNode(p,second.id,'container','Pinned gallery');Object.assign(gallery,{containerVariant:'gallery',x:80,y:350,width:300,height:180,collapsed:false});
  const galleryRef=addNode(p,second.id,'reference','Pinned gallery item');Object.assign(galleryRef,{parentId:gallery.id,targetId:source.id});
  const ownRef=addNode(p,second.id,'reference','Local background override');Object.assign(ownRef,{targetId:source.id,x:450,y:350,style:{backgroundAssetId:png.id}});
  const saved=commitSnapshots(p);const currentBytes=await page.evaluate(()=>{const c=document.createElement('canvas');c.width=c.height=1;const ctx=c.getContext('2d')!;ctx.fillStyle='#DD4422';ctx.fillRect(0,0,1,1);return c.toDataURL().split(',')[1];});saved.assets[0]={...png,base64:currentBytes};
  await page.setContent('<div id="scene" style="height:600px;width:1100px"></div>');await page.addStyleTag({content:css});await page.addScriptTag({content:script});
  await page.evaluate(({p,id})=>(window as any).backgroundHarness.renderScene(document.getElementById('scene'),p,id,{view:{x:0,y:0,scale:1}}),{p:saved,id:second.id});
  const image=page.locator(`.ms-node[data-node-id="${ref.id}"]>.ms-node-background`);
  await expect(image).toHaveAttribute('data-background-pattern','image');
  expect(await image.evaluate(e=>getComputedStyle(e).backgroundImage)).toContain('data:image/png;base64,');
  await expect(page.locator(`.ms-gallery-item[data-gallery-item-id="${galleryRef.id}"]>.ms-node-background`)).toHaveAttribute('data-background-pattern','image');
  expect(await page.locator(`.ms-node[data-node-id="${ownRef.id}"]>.ms-node-background`).evaluate(e=>getComputedStyle(e).backgroundImage)).toContain(currentBytes);
  // Choosing floating content preserves the asset but hides its visual backdrop.
  await expect(page.locator(`.ms-node[data-node-id="${transparent.id}"]>.ms-node-background`)).toBeHidden();
});
