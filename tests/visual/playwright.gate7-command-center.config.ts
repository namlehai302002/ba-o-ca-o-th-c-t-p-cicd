import { defineConfig, devices } from '@playwright/test';
import { existsSync } from 'node:fs';

const baseURL = process.env.WMS_BASE_URL;
const defaultStorageState = 'tests/visual/.auth/wms-auth-state.json';
const storageState = process.env.WMS_AUTH_STATE || (existsSync(defaultStorageState) ? defaultStorageState : undefined);

if (!baseURL) {
  throw new Error('WMS_BASE_URL is required for Gate 7 Command Center verification.');
}

if (!storageState) {
  throw new Error('WMS_AUTH_STATE is required, or run npm run visual:auth first.');
}

export default defineConfig({
  testDir: '.',
  testMatch: 'wms-gate7-command-center.spec.ts',
  timeout: 90_000,
  expect: { timeout: 12_000 },
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL,
    storageState,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  outputDir: '../../artifacts/dashboard-command-center/playwright-results',
  projects: [
    {
      name: 'desktop',
      use: { viewport: { width: 1440, height: 900 } }
    },
    {
      name: 'laptop',
      grepInvert: /warm p95 meets the dashboard contract/,
      use: { viewport: { width: 1280, height: 800 } }
    },
    {
      name: 'tablet',
      grepInvert: /warm p95 meets the dashboard contract/,
      use: { ...devices['iPad (gen 7)'], browserName: 'chromium', viewport: { width: 768, height: 1024 } }
    },
    {
      name: 'mobile',
      grepInvert: /warm p95 meets the dashboard contract/,
      use: { ...devices['Pixel 7'], viewport: { width: 390, height: 844 } }
    }
  ],
  reporter: [
    ['list'],
    ['html', { outputFolder: '../../artifacts/dashboard-command-center/playwright-report', open: 'never' }],
    ['json', { outputFile: '../../artifacts/dashboard-command-center/playwright-report.json' }]
  ]
});
