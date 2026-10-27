import test from 'node:test';
import assert from 'node:assert/strict';
import { readabilityPlacement, READABILITY_TARGET_PX } from '../src/hover-readability.ts';

test('small text grows to one screen-pixel target independently of camera scale', () => {
  for (const textPx of [.002, .3, 3, 8, 13.5]) {
    const rect = { left: 400, top: 250, width: textPx * 5, height: textPx * 2 };
    const result = readabilityPlacement(rect, { left: 0, top: 0, width: 1200, height: 800 }, textPx)!;
    assert.equal(result.factor * textPx, READABILITY_TARGET_PX);
    assert.equal(result.width, 80); assert.equal(result.height, 32);
  }
  assert.equal(readabilityPlacement({ left: 10, top: 10, width: 200, height: 70 }, { left: 0, top: 0, width: 1200, height: 800 }, 14), undefined);
  assert.equal(readabilityPlacement({ left: 10, top: 10, width: 200, height: 70 }, { left: 0, top: 0, width: 1200, height: 800 }, 24), undefined);
});

test('enlargement remains inside the viewport without changing geometry inputs', () => {
  const rect = { left: 890, top: 470, width: 100, height: 20 }, viewport = { left: 0, top: 0, width: 1000, height: 500 };
  const before = structuredClone({ rect, viewport });
  const result = readabilityPlacement(rect, viewport, 2, { x: 980, y: 480 })!;
  assert.equal(result.factor, 8); assert.equal(result.width, 800);
  assert(result.left >= 12 && result.top >= 12);
  assert(result.left + result.width <= 988 && result.top + result.height <= 488);
  assert.deepEqual({ rect, viewport }, before);
});

test('non-text controls have a modest usable target and invalid/capped frames do not expand', () => {
  const viewport = { left: 0, top: 0, width: 1000, height: 600 };
  const icon = readabilityPlacement({ left: 100, top: 100, width: 8, height: 8 }, viewport)!;
  assert.equal(icon.width, 48); assert.equal(icon.height, 48);
  assert.equal(readabilityPlacement({ left: 100, top: 100, width: 40, height: 40 }, viewport), undefined);
  assert.equal(readabilityPlacement({ left: 0, top: 0, width: 0, height: 20 }, viewport, 2), undefined);
  assert.equal(readabilityPlacement({ left: 0, top: 0, width: 1000, height: 600 }, viewport, 2), undefined);
});
