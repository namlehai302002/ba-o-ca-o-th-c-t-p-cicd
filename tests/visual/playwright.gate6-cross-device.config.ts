import { defineConfig } from '@playwright/test';
import { existsSync } from 'node:fs';

const baseURL = process.env.WMS_BASE_URL;
const defaultStorageState = 'tests/visual/.auth/gate6-local-admin.json';
const storageState = process.env.WMS_AUTH_STATE || (existsSync(defaultStorageState) ? defaultStorageState : undefined);

if (!baseURL) throw new Error('WMS_BASE_URL is required for Gate 6 cross-device checks.');
if (!storageState) throw new Error('WMS_AUTH_STATE or tests/visual/.auth/gate6-local-admin.json is required.');

const viewports = [
  ['desktop-1280x720', 1280, 720, 1],
  ['desktop-1366x768', 1366, 768, 1],
  ['desktop-1440x900', 1440, 900, 1],
  ['desktop-1536x864', 1536, 864, 1],
  ['desktop-1920x1080', 1920, 1080, 1],
  ['tablet-768x1024', 768, 1024, 1],
  ['tablet-820x1180', 820, 1180, 1],
  ['tablet-1024x768', 1024, 768, 1],
  ['tablet-1180x820', 1180, 820, 1],
  ['mobile-320x568', 320, 568, 1],
  ['mobile-360x800', 360, 800, 1],
  ['mobile-375x812', 375, 812, 1],
  ['mobile-390x844-dpr2', 390, 844, 2],
  ['mobile-412x915', 412, 915, 1],
  ['mobile-430x932', 430, 932, 1],
  ['mobile-landscape-844x390', 844, 390, 2]
] as const;

export default defineConfig({
  testDir: '.',
  testMatch: 'wms-gate6-cross-device.spec.ts',
  timeout: 180_000,
  workers: 2,
  use: {
    baseURL,
    storageState,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  outputDir: '../../test-results/gate6-cross-device',
  projects: viewports.map(([name, width, height, deviceScaleFactor]) => ({
    name,
    use: {
      viewport: { width, height },
      deviceScaleFactor,
      hasTouch: width < 1024
    }
  })),
  reporter: [
    ['list'],
    ['json', { outputFile: '../../artifacts/ui-cross-device/gate6-cross-device-results.json' }],
    ['html', { outputFolder: '../../artifacts/ui-cross-device/gate6-cross-device-report', open: 'never' }]
  ]
});
