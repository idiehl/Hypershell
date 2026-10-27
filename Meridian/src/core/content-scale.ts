/** Authored font sizes are reading units at this ordinary canvas-node frame.
 * A frame, rather than its shortest label, determines the default scale. */
export const CONTENT_READING_FRAME = { width: 300, height: 180 } as const;

export function contentFrameScale(width: number, height: number): number {
  return Math.max(Number.MIN_VALUE, Math.min(width / CONTENT_READING_FRAME.width, height / CONTENT_READING_FRAME.height));
}
