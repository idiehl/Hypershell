import { defineConfig } from '@playwright/test';
import path from 'node:path';
const workspace = 'I:/dev/Meridian';
export default defineConfig({
  testDir: path.join(workspace, 'tests'), workers: 1,
  timeout: 60000, expect: { timeout: 10000 },
  outputDir: path.join(workspace, 'output/inline-text-20261011/source-qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.join(workspace, 'output/inline-text-20261011/source-qa/results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5320', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [
    { name: 'chromium', testMatch: ['inline-text.browser.ts','rich-text.browser.ts','typography.browser.ts','authoring.browser.ts','backgrounds.browser.ts','clusters.browser.ts','module-selection.browser.ts','selection-editor.browser.ts'], use: { browserName: 'chromium' } },
  ],
  webServer: { command: `dotnet run --project output/inline-text-20261011/source-host -- --urls http://127.0.0.1:5320 --data-root "${path.join(workspace, '.test-data', `inline-text-source-${Date.now()}`)}"`, cwd: workspace, url: 'http://127.0.0.1:5320/__meridian_status', reuseExistingServer: false, timeout: 60000 },
});



