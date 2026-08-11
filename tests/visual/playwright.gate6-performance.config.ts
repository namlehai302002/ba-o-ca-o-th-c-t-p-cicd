import { defineConfig } from '@playwright/test';
import { existsSync } from 'node:fs';

const baseURL = process.env.WMS_BASE_URL;
const defaultStorageState = 'tests/visual/.auth/gate6-local-admin.json';
const storageState = process.env.WMS_AUTH_STATE || (existsSync(defaultStorageState) ? defaultStorageState : undefined);

if (!baseURL) throw new Error('WMS_BASE_URL is required for Gate 6 browser timing.');
if (!storageState) throw new Error('WMS_AUTH_STATE or tests/visual/.auth/gate6-local-admin.json is required.');

export default defineConfig({
  testDir: '.',
  testMatch: 'wms-gate6-performance.spec.ts',
  timeout: 120_000,
  workers: 1,
  use: {
    baseURL,
    storageState,
    viewport: { width: 1440, height: 900 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  outputDir: '../../test-results/gate6-performance',
  reporter: [
    ['list'],
    ['html', { outputFolder: '../../artifacts/performance/playwright-report', open: 'never' }]
  ]
});
