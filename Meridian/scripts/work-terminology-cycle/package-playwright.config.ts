import { defineConfig } from '@playwright/test';
import path from 'node:path';
export default defineConfig({
  testDir: path.resolve(import.meta.dirname, '../../tests'),
  testMatch: [
    'work-terminology.browser.ts', 'authoring-capture.browser.ts',
    'portal-appearance.browser.ts', 'portal-color-scene.browser.ts',
    'canvas-launcher.browser.ts', 'door-launcher.browser.ts',
    'sidebar-aids.browser.ts', 'toolbar-layout.browser.ts', 'tool-shortcuts.browser.ts',
  ],
  workers: 1, timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.resolve(import.meta.dirname, '../../output/work-terminology-20261019/package/qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.resolve(import.meta.dirname, '../../output/work-terminology-20261019/package/qa/browser-results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5432', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});


