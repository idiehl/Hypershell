import { defineConfig } from '@playwright/test';
import path from 'node:path';
const workspace = 'I:/dev/Meridian';
export default defineConfig({
  testDir: path.join(workspace, 'tests'), workers: 1,
  timeout: 60000, expect: { timeout: 10000 },
  outputDir: path.join(workspace, 'output/nested-disclosure-20261017/source-qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.join(workspace, 'output/nested-disclosure-20261017/source-qa/results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5344', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [
    { name: 'chromium', testMatch: ['container-frames.browser.ts', 'nested-disclosure.browser.ts'], use: { browserName: 'chromium' } },
  ],
  webServer: { command: `"${path.join(workspace,'output/nested-disclosure-20261017/package/app/Meridian.Host.exe')}" --urls http://127.0.0.1:5344 --data-root "${path.join(workspace, '.test-data', `nested-disclosure-source-${Date.now()}`)}"`, cwd: path.join(workspace,'output/nested-disclosure-20261017/package/app'), url: 'http://127.0.0.1:5344/__meridian_status', reuseExistingServer: false, timeout: 60000 },
});



