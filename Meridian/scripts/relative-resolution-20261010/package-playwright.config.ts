import { defineConfig } from '@playwright/test';
import path from 'node:path';

export default defineConfig({
  testDir: path.resolve(import.meta.dirname, '../../tests'),
  testMatch: process.env.MERIDIAN_RELATIVE_RESOLUTION_TEST_MATCH ?? 'relative-resolution.browser.ts',
  workers: 1, timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.resolve(import.meta.dirname, '../../output/relative-resolution-20261010/qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.resolve(import.meta.dirname, '../../output/relative-resolution-20261010/qa/browser-results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5310', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
