import { defineConfig } from '@playwright/test';
import path from 'node:path';
export default defineConfig({
  testDir: path.resolve(import.meta.dirname, '../../tests'),
  testMatch: ['container-hover.browser.ts', 'expansion-tray-scene.browser.ts', 'gallery-navigation-width.browser.ts', 'gallery-frame-stability.browser.ts', 'tray-polish.browser.ts', 'tray-resize.browser.ts'],
  workers: 1, timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.resolve(import.meta.dirname, '../../output/tray-alignment-20261018/package/qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.resolve(import.meta.dirname, '../../output/tray-alignment-20261018/package/qa/browser-results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5418', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
