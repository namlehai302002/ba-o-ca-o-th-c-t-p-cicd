import { defineConfig } from '@playwright/test';

const baseURL = process.env.WMS_BASE_URL;
const storageState = process.env.WMS_ADMIN_AUTH_STATE || process.env.WMS_AUTH_STATE;

if (!baseURL) throw new Error('WMS_BASE_URL is required for role fixture setup.');
if (!storageState) throw new Error('WMS_ADMIN_AUTH_STATE or WMS_AUTH_STATE is required for role fixture setup.');

export default defineConfig({
  testDir: '.',
  testMatch: 'wms-role-fixture.setup.ts',
  timeout: 120_000,
  workers: 1,
  use: {
    baseURL,
    storageState,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  outputDir: '../../artifacts/role-e2e/fixture-results',
  reporter: [['list'], ['html', { outputFolder: '../../artifacts/role-e2e/fixture-report', open: 'never' }]]
});
