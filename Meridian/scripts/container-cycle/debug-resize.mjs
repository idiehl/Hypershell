import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { chromium } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../../src/core/model.ts';

const base='http://127.0.0.1:5294',folder=await mkdtemp(path.join(tmpdir(),'meridian-resize-debug-'));
let host,browser;
try {
  try { const response=await fetch(base+'/__meridian_status'); if(response.ok) throw new Error('Port 5294 is already in use.'); } catch(error) { if(error.message==='Port 5294 is already in use.')throw error; }
  host=spawn('dotnet',[path.resolve('src/host/bin/Debug/net10.0/Meridian.Host.dll'),'--data-root',folder,'--urls',base],{cwd:process.cwd(),windowsHide:true,stdio:['ignore','pipe','pipe']});
  let logs='';host.stdout.on('data',part=>logs=(logs+part).slice(-5000));host.stderr.on('data',part=>logs=(logs+part).slice(-5000));
  for(let at=0;at<100;at++){if(host.exitCode!==null)throw new Error(logs);try{if((await fetch(base+'/__meridian_status')).ok)break;}catch{}await new Promise(r=>setTimeout(r,100));}
  const project=newProject('Isolated resize debug'),node=addNode(project,project.entryCanvasId,'text','A long title that needs room for every word');
  Object.assign(node,{x:80,y:80,width:640,height:500,sizeMode:'fixed',text:'A readable canvas keeps every part of this passage visible. '.repeat(6),style:{fontSize:24,titleSize:32}});
  const response=await fetch(base+'/api/projects',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(commitSnapshots(project))});assert.equal(response.status,201,await response.clone().text());
  browser=await chromium.launch();const page=await browser.newPage({viewport:{width:1500,height:950}});await page.goto(base+'/?project='+project.id);
  const card=page.locator(`.ms-node[data-node-id="${node.id}"]`);await card.locator('.ms-node-title').click();await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('appearance');
  for(const inset of [1,4,7]){
    await page.getByRole('button',{name:'Fit selected branch',exact:true}).click();const bounds=await card.boundingBox(),x=bounds.x+bounds.width-inset,y=bounds.y+bounds.height/2;
    console.log('before',JSON.stringify(await page.evaluate(({x,y,id})=>{const card=document.querySelector(`[data-node-id="${id}"]`);return{hit:[1,2,3,4,5,7].map(at=>{const hit=document.elementFromPoint(x+1-at,y);return{inset:at,tag:hit?.tagName,cls:hit?.getAttribute('class'),nearest:hit?.closest('[data-node-id]')?.getAttribute('data-node-id')};}),selected:card.getAttribute('aria-selected'),cursor:card.style.cursor,transform:document.querySelector('.ms-world').style.transform,svg:card.querySelector('.ms-node-hit-surface')?.outerHTML};},{x,y,id:node.id})));
    await page.mouse.move(x,y);console.log('hover',await card.evaluate(e=>e.style.cursor));
    await page.evaluate(()=>{window.events=[];for(const name of ['pointerdown','pointermove','pointerup'])window.addEventListener(name,e=>{window.events.push({name,target:e.target.tagName,cls:e.target.getAttribute('class'),node:e.target.closest('[data-node-id]')?.getAttribute('data-node-id')});},{capture:true,once:name!=='pointermove'});});
    await page.mouse.down();await page.mouse.move(x+40,y,{steps:10});console.log('during',await card.evaluate(e=>({width:e.style.width,left:e.style.left})));await page.mouse.up();await page.waitForTimeout(2300);
    const actual=await(await fetch(base+'/api/projects/'+project.id)).json();console.log('after',JSON.stringify({inset,node:actual.nodes.find(n=>n.id===node.id),events:await page.evaluate(()=>window.events),transform:await page.locator('.ms-world').evaluate(e=>e.style.transform)}));
  }
} finally { if(browser)await browser.close();if(host&&host.exitCode===null){const exit=once(host,'exit');host.kill();await exit;}assert.ok(path.resolve(folder).startsWith(path.resolve(tmpdir())+path.sep+'meridian-resize-debug-'));await rm(folder,{recursive:true,force:true}); }
