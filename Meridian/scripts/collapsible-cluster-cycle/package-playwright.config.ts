import { defineConfig } from '@playwright/test';
import path from 'node:path';
export default defineConfig({
  testDir: path.resolve(import.meta.dirname, '../../tests'),
  testMatch: [
    'disclosure-clusters.browser.ts', 'disclosure-cluster-scene.browser.ts',
    'clusters.browser.ts', 'clusters-scene.browser.ts', 'modules.browser.ts',
  ],
  workers: 1, timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.resolve(import.meta.dirname, '../../output/collapsible-clusters-20261020/qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.resolve(import.meta.dirname, '../../output/collapsible-clusters-20261020/qa/browser-results.json') }]],
  use: { baseURL: process.env.MERIDIAN_CLUSTER_REVIEW_URL ?? 'http://127.0.0.1:5433', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'on' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});


