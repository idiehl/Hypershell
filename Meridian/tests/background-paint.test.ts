import { test } from 'node:test';
import assert from 'node:assert/strict';
import { backgroundPaint, moveCanvasBackground } from '../src/backgrounds.ts';
import type { MeridianAsset } from '../src/core/types.ts';

test('all background patterns render inert local paint and preserve grid geometry', () => {
  for (const backgroundPattern of ['dots','grid','isometric','linen','contours','aurora','stars'] as const) {
    const paint=backgroundPaint({backgroundPattern,backgroundTint:'#ABCDEF',backgroundScale:64});
    assert.notEqual(paint.image,'none'); assert.equal(paint.pattern,backgroundPattern);
    assert.ok(!paint.image.includes('https:')); assert.ok(!paint.image.includes('javascript:'));
  }
  assert.equal(backgroundPaint({}).image,'none');
  assert.equal(backgroundPaint({},[],'dots').position,'-12px -12px');
  const iso=backgroundPaint({backgroundPattern:'isometric',backgroundScale:100});
  assert.equal(iso.size,'100px 57.735px');
});

test('custom images accept raster bytes only and tile without distorting the aspect ratio', () => {
  const png:MeridianAsset={id:'picture',name:'custom.png',mime:'image/png',base64:'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfyoAAAAASUVORK5CYII='};
  const paint=backgroundPaint({backgroundAssetId:png.id,backgroundFit:'tile',backgroundScale:80},[png]);
  assert.equal(paint.pattern,'image'); assert.equal(paint.size,'80px auto');
  assert.equal(backgroundPaint({backgroundAssetId:png.id,backgroundFit:'contain'},[png]).size,'contain');
  assert.equal(backgroundPaint({backgroundAssetId:png.id},[{...png,mime:'image/svg+xml'}]).image,'none');
  assert.equal(backgroundPaint({backgroundAssetId:png.id},[{...png,base64:btoa('<script>unsafe</script>')}]).image,'none');
});

test('world anchored paint follows pan and zoom, while a photograph fills the viewport', () => {
  const element={style:{backgroundSize:'',backgroundPosition:'',opacity:''}} as unknown as HTMLElement;
  moveCanvasBackground(element,backgroundPaint({backgroundPattern:'grid',backgroundScale:64}),{x:87,y:-22,scale:.5});
  assert.equal(element.style.backgroundSize,'32px 32px'); assert.equal(element.style.backgroundPosition,'87px -22px');
  moveCanvasBackground(element,backgroundPaint({backgroundPattern:'dots',backgroundScale:64}),{x:87,y:-22,scale:.5});
  assert.equal(element.style.backgroundPosition,'71px -38px');
  moveCanvasBackground(element,backgroundPaint({backgroundPattern:'isometric',backgroundScale:100}),{x:0,y:0,scale:.5});
  assert.equal(element.style.backgroundSize,'50px 28.8675px');
  const photograph={...backgroundPaint({}),pattern:'image' as const,repeat:'no-repeat',size:'cover'};
  element.style.backgroundSize='cover'; element.style.backgroundPosition='center';
  moveCanvasBackground(element,photograph,{x:99,y:88,scale:2});
  assert.equal(element.style.backgroundSize,'cover'); assert.equal(element.style.backgroundPosition,'center');
});

test('untrusted colors never become CSS and numeric paint stays bounded', () => {
  const paint=backgroundPaint({backgroundPattern:'grid',backgroundTint:'url(https://invalid.test)',backgroundScale:Infinity,backgroundOpacity:4});
  assert.ok(!paint.image.includes('url(')); assert.equal(paint.scale,24); assert.equal(paint.opacity,1);
});
