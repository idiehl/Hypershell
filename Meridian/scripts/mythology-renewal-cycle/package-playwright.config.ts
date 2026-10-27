import { defineConfig } from '@playwright/test';
import path from 'node:path';
export default defineConfig({
  testDir: path.resolve(import.meta.dirname, '../../tests'),
  testMatch: [
    'authoring-capture.browser.ts', 'portal-appearance.browser.ts', 'portal-color-scene.browser.ts', 'canvas-launcher.browser.ts', 'door-launcher.browser.ts',
    'stable-content-fit.browser.ts', 'container-hover.browser.ts', 'hover-handle.browser.ts',
    'hover-outline.browser.ts', 'gallery-navigation-width.browser.ts', 'gallery-frame-stability.browser.ts',
    'tray-resize.browser.ts', 'inline-text.browser.ts', 'edge-resize-text.browser.ts',
    'background-display.browser.ts', 'background-display-scene.browser.ts', 'backgrounds.browser.ts', 'backgrounds-scene.browser.ts',
    'shape-surface.browser.ts', 'shape-collision.browser.ts',
  ],
  workers: 1, timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.resolve(import.meta.dirname, '../../output/mythology-renewal-20261018/package/qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.resolve(import.meta.dirname, '../../output/mythology-renewal-20261018/package/qa/browser-results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5431', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});

