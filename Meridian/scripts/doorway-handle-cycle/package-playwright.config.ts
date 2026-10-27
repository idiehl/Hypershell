import { defineConfig } from '@playwright/test';
import path from 'node:path';
export default defineConfig({
  testDir: path.resolve(import.meta.dirname, '../../tests'),
  testMatch: [
    'container-hover.browser.ts', 'hover-handle.browser.ts',
    'canvas-launcher.browser.ts', 'door-launcher.browser.ts',
    'hover-outline.browser.ts', 'stable-content-fit.browser.ts',
    'expansion-tray-scene.browser.ts', 'gallery-navigation-width.browser.ts',
    'gallery-frame-stability.browser.ts', 'tray-polish.browser.ts',
    'tray-resize.browser.ts', 'inline-text.browser.ts', 'edge-resize-text.browser.ts',
  ],
  workers: 1, timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.resolve(import.meta.dirname, '../../output/doorway-handle-20261018/package/qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.resolve(import.meta.dirname, '../../output/doorway-handle-20261018/package/qa/browser-results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5419', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } },
    { name: 'chromium-native-gestures', use: { browserName: 'chromium' }, testMatch: ['inline-text-scene.browser.ts'],
      grep: /the first text drag|text double clicks|double clicking a selected resize edge|tool clicks and additive|Shift-click adds inactive/ },
  ],
});
