import { defineConfig } from '@playwright/test';

const baseURL = process.env.WMS_BASE_URL;
if (!baseURL) throw new Error('WMS_BASE_URL is required for role access evidence.');

const state = (name: string) => `tests/visual/.auth/audit-role-${name}.json`;

export default defineConfig({
  testDir: '.',
  testMatch: 'wms-role-access.spec.ts',
  timeout: 60_000,
  // Role UAT targets the same application/database endpoint. Keep the smoke
  // sequential so authorization evidence cannot trip application rate limits.
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  outputDir: '../../artifacts/role-e2e/access-results',
  reporter: [['list'], ['html', { outputFolder: '../../artifacts/role-e2e/access-report', open: 'never' }]],
  projects: [
    { name: 'manager', use: { storageState: state('manager') } },
    { name: 'inbound', use: { storageState: state('inbound') } },
    { name: 'outbound', use: { storageState: state('outbound') } },
    { name: 'inventory', use: { storageState: state('inventory') } },
    { name: 'transport', use: { storageState: state('transport') } },
    { name: 'report', use: { storageState: state('report') } },
    { name: 'viewer', use: { storageState: state('viewer') } }
  ]
});
