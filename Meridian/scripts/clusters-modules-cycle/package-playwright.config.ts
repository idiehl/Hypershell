import { defineConfig } from '@playwright/test';
import path from 'node:path';
export default defineConfig({
  testDir: path.resolve(import.meta.dirname, '../../tests'), testMatch: ['module-selection.browser.ts', 'clusters.browser.ts'], workers: 1,
  timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.resolve(import.meta.dirname, '../../output/clusters-modules-20261010/package/qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.resolve(import.meta.dirname, '../../output/clusters-modules-20261010/package/qa/browser-results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5315', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
