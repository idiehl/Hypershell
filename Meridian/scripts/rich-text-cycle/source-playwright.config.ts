import { defineConfig } from '@playwright/test';
import path from 'node:path';
const workspace = 'I:/dev/Meridian';
export default defineConfig({
  testDir: path.join(workspace, 'tests'), workers: 1,
  timeout: 60000, expect: { timeout: 10000 },
  outputDir: path.join(workspace, 'output/rich-text-20261011/source-qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.join(workspace, 'output/rich-text-20261011/source-qa/results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5318', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [
    { name: 'chromium', testMatch: ['rich-text.browser.ts','typography.browser.ts','authoring.browser.ts'], use: { browserName: 'chromium' } },
  ],
  webServer: { command: `dotnet run --project output/rich-text-20261011/source-host -- --urls http://127.0.0.1:5318 --data-root "${path.join(workspace, '.test-data', `rich-text-source-${Date.now()}`)}"`, cwd: workspace, url: 'http://127.0.0.1:5318/__meridian_status', reuseExistingServer: false, timeout: 60000 },
});

