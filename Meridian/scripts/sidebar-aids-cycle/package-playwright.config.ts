import { defineConfig } from '@playwright/test';
import path from 'node:path';
const workspace = 'I:/dev/Meridian';
export default defineConfig({
  testDir: path.join(workspace, 'tests'), testMatch: ['sidebar-aids.browser.ts'], workers: 1,
  timeout: 45000, expect: { timeout: 8000 },
  outputDir: path.join(workspace,'output/sidebar-aids-20261016/package/qa/browser'),
  reporter: [['list'], ['json', { outputFile:path.join(workspace,'output/sidebar-aids-20261016/package/qa/browser-results.json') }]],
  use: { baseURL:'http://127.0.0.1:5325',viewport:{width:1500,height:950},trace:'retain-on-failure',screenshot:'only-on-failure' },
  projects: [{name:'chromium',use:{browserName:'chromium'}}],
});
