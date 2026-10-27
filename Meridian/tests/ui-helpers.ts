import { expect, type Page } from '@playwright/test';

/** Document actions share the application File menu in every canvas. */
export async function workspaceAction(page:Page,name:string){await page.getByRole('button',{name:'File',exact:true}).click();await page.getByRole('menuitem',{name:name==='Save a copy'?'Save as':name,exact:true}).click();}

/** Composition and reusable assemblies share the application Tools menu. */
export async function toolsAction(page:Page,name:'Modules'|'Composition tools'){await page.getByRole('button',{name:'Tools',exact:true}).click();await page.getByRole('menuitem',{name,exact:true}).click();}

export async function inspectorPanel(page:Page,name:string){await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption(name);}

/** Choose a navigation tool without assuming the dropdown's previous choice. */
export async function interactionTool(page:Page,name:'Single select'|'Select Area'|'Hand'='Single select'){
  await page.getByRole('button',{name:/^(?:Single select|Select Area)$/}).click();
  await page.getByRole('menuitem',{name:name==='Hand'?'Select Area':name,exact:true}).click();
}

/** Select current-canvas tiles, then open the ordinary content editor. */
export async function selectEditorNode(page:Page,id:string,title:string){
  const tile=page.locator(`.ms-node[data-node-id="${id}"] > .ms-node-header > .ms-node-title`);
  const box=await tile.count()?await tile.boundingBox():null,stage=await page.locator('.canvas-stage').boundingBox();
  if(box&&stage&&box.x+box.width/2>stage.x&&box.x+box.width/2<stage.x+stage.width&&box.y+box.height/2>stage.y&&box.y+box.height/2<stage.y+stage.height)await tile.click();
  else{
    await page.keyboard.press('Control+f');
    const search=page.getByRole('searchbox',{name:'Search Work nodes',exact:true});await search.fill(id);
    const result=page.locator(`[data-outline-node="${id}"]`);await expect(result).toBeVisible();await result.getByRole('button',{name:/^Jump to Node:/}).click();await search.fill('');
  }
  await inspectorPanel(page,'content');
}
