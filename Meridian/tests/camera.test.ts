import test from 'node:test';
import assert from 'node:assert/strict';
import { clampCameraScale, formatCameraZoom, MAX_CAMERA_SCALE, MIN_CAMERA_SCALE, scaleCameraAt } from '../src/core/camera.ts';

test('deep zoom keeps the pointed world coordinate stable across both camera limits', () => {
  const point = { x: 641, y: 398 }, original = { x: -220, y: 112, scale: 1 };
  const world = { x: (point.x - original.x) / original.scale, y: (point.y - original.y) / original.scale };
  for (const factor of [0.001, 500, 100000, .000001]) {
    const zoomed = scaleCameraAt(original, point, factor);
    assert.ok(zoomed.scale >= MIN_CAMERA_SCALE && zoomed.scale <= MAX_CAMERA_SCALE);
    assert.ok(Math.abs((point.x - zoomed.x) / zoomed.scale - world.x) < 1e-6);
    assert.ok(Math.abs((point.y - zoomed.y) / zoomed.scale - world.y) < 1e-6);
  }
});

test('deep zoom remains legible and invalid remembered scales normalize safely', () => {
  assert.equal(formatCameraZoom(MIN_CAMERA_SCALE), '0.01%');
  assert.equal(formatCameraZoom(.0025), '0.25%');
  assert.equal(formatCameraZoom(.035), '3.5%');
  assert.equal(formatCameraZoom(MAX_CAMERA_SCALE), '102400%');
  assert.equal(clampCameraScale(NaN), 1);
  assert.equal(clampCameraScale(-1), MIN_CAMERA_SCALE);
});
