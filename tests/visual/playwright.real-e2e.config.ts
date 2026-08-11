import { defineConfig, devices } from '@playwright/test';
import { existsSync } from 'node:fs';

const baseURL = process.env.WMS_BASE_URL;
const defaultStorageState = 'tests/visual/.auth/wms-auth-state.json';
const storageState = process.env.WMS_AUTH_STATE || (existsSync(defaultStorageState) ? defaultStorageState : undefined);

if (!baseURL) {
  throw new Error('WMS_BASE_URL is required for real WMS E2E checks.');
}

if (!storageState) {
  throw new Error('WMS_AUTH_STATE is required, or run npm run visual:auth to create tests/visual/.auth/wms-auth-state.json.');
}

export default defineConfig({
  testDir: '.',
  testMatch: 'wms-core-real-e2e.spec.ts',
  timeout: 90_000,
  expect: { timeout: 10_000 },
  outputDir: '../../artifacts/real-e2e/test-results',
  reporter: [
    ['list'],
    ['html', { outputFolder: '../../artifacts/real-e2e/playwright-report', open: 'never' }]
  ],
  use: {
    baseURL,
    storageState,
    actionTimeout: 15_000,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  projects: [
    {
      name: 'desktop-real-e2e',
      use: { viewport: { width: 1440, height: 900 } }
    },
    {
      name: 'mobile-real-e2e-readonly',
      use: { ...devices['Pixel 7'], viewport: { width: 390, height: 844 } }
    }
  ]
});
