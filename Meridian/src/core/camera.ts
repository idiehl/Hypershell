import type { Point } from './types.ts';

/** Practical browser bounds: retain enough range for deep, small-scale hierarchies. */
export const MIN_CAMERA_SCALE = 0.0001;
export const MAX_CAMERA_SCALE = 1024;
export interface CameraView { x: number; y: number; scale: number }

export function clampCameraScale(scale: number): number {
  return Math.max(MIN_CAMERA_SCALE, Math.min(MAX_CAMERA_SCALE, Number.isFinite(scale) ? scale : 1));
}

/** Keep the same world point beneath the cursor or viewport center when zooming. */
export function scaleCameraAt(view: CameraView, point: Point, factor: number): CameraView {
  const old = clampCameraScale(view.scale), scale = clampCameraScale(old * factor);
  return { scale, x: point.x - (point.x - view.x) * scale / old,
    y: point.y - (point.y - view.y) * scale / old };
}

export function formatCameraZoom(scale: number): string {
  const percent = clampCameraScale(scale) * 100;
  return `${percent < 1 ? Number(percent.toFixed(3)) : percent < 10 ? Number(percent.toFixed(2)) : Math.round(percent)}%`;
}
