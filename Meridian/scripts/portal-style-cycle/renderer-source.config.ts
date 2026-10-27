import { defineConfig } from '@playwright/test';
export default defineConfig({testDir:'../../tests',testMatch:['door-launcher.browser.ts'],workers:1,timeout:45000,expect:{timeout:8000},use:{viewport:{width:1500,height:950}},projects:[{name:'chromium',use:{browserName:'chromium'}}],outputDir:'../../output/portal-designs-20261018/source-qa'});
