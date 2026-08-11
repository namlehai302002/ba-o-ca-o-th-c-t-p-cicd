import { defineConfig, devices } from '@playwright/test';
import { existsSync } from 'node:fs';

const baseURL = process.env.WMS_BASE_URL;
const defaultStorageState = 'tests/visual/.auth/wms-auth-state.json';
const storageState = process.env.WMS_AUTH_STATE || (existsSync(defaultStorageState) ? defaultStorageState : undefined);

if (!baseURL) throw new Error('WMS_BASE_URL is required for AI-2 inventory-risk verification.');
if (!storageState) throw new Error('WMS_AUTH_STATE or the default authenticated state is required.');

export default defineConfig({
  testDir: '.',
  testMatch: 'wms-ai-inventory-risk.spec.ts',
  timeout: 120_000,
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL,
    storageState,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  outputDir: '../../artifacts/ai-smart-cycle-count/AI2/playwright-results',
  projects: [
    {
      name: 'desktop-1440x900',
      use: { viewport: { width: 1440, height: 900 } }
    },
    {
      name: 'laptop-1366x768',
      use: { viewport: { width: 1366, height: 768 } }
    },
    {
      name: 'tablet-landscape-1024x768',
      use: { ...devices['iPad (gen 7) landscape'], browserName: 'chromium', viewport: { width: 1024, height: 768 } }
    },
    {
      name: 'tablet-portrait-768x1024',
      use: { ...devices['iPad (gen 7)'], browserName: 'chromium', viewport: { width: 768, height: 1024 } }
    },
    {
      name: 'mobile-390x844',
      use: { ...devices['Pixel 7'], browserName: 'chromium', viewport: { width: 390, height: 844 } }
    },
    {
      name: 'mobile-small-360x800',
      use: { ...devices['Galaxy S9+'], browserName: 'chromium', viewport: { width: 360, height: 800 } }
    }
  ],
  reporter: [
    ['list'],
    ['json', { outputFile: '../../artifacts/ai-smart-cycle-count/AI2/playwright-report.json' }],
    ['html', { outputFolder: '../../artifacts/ai-smart-cycle-count/AI2/playwright-report', open: 'never' }]
  ]
});
