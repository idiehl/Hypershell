import { defineConfig } from '@playwright/test';
import path from 'node:path';
const workspace = 'I:/dev/Meridian';
const stage = path.join(workspace, 'output/details-toggle-20261016/package/app');
export default defineConfig({
  testDir: path.join(workspace, 'scripts/details-toggle-cycle'), workers: 1,
  timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.join(workspace, 'output/details-toggle-20261016/source-qa/browser'),
  reporter: [['list'], ['json', { outputFile: path.join(workspace, 'output/details-toggle-20261016/source-qa/results.json') }]],
  use: { baseURL: 'http://127.0.0.1:5324', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name: 'chromium', testMatch: ['details-toggle.browser.ts'], use: { browserName: 'chromium' } }],
  webServer: { command: `"${path.join(stage,'Meridian.Host.exe')}" --urls http://127.0.0.1:5324 --data-root "${path.join(workspace,'.test-data',`details-toggle-source-${Date.now()}`)}"`, cwd: stage, url:'http://127.0.0.1:5324/__meridian_status',reuseExistingServer:false,timeout:60000 },
});
