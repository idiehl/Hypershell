import { defineConfig } from '@playwright/test';
import path from 'node:path';
export default defineConfig({
  testDir: './tests', testMatch: '**/*.browser.ts', fullyParallel: false, workers: 1,
  timeout: 45000, expect: { timeout: 8000 }, reporter: 'list',
  use: { baseURL: 'http://127.0.0.1:5291', viewport: { width: 1500, height: 950 }, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [{ name:'chromium',use:{browserName:'chromium'} },{ name:'firefox',use:{browserName:'firefox'} }],
  webServer: {
    command: `dotnet run --project src/host -- --urls http://127.0.0.1:5291 --data-root "${path.resolve('.test-data',`browser-${Date.now()}`)}"`,
    url:'http://127.0.0.1:5291/__meridian_status', reuseExistingServer:false, timeout:60000
  }
});
