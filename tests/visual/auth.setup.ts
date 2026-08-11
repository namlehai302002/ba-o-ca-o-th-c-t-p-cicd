import { expect, test as setup } from '@playwright/test';
import { existsSync, readFileSync } from 'node:fs';

const authStatePath = process.env.WMS_AUTH_STATE || 'tests/visual/.auth/wms-auth-state.json';
const baseUrl = process.env.WMS_BASE_URL || '';
let testUser = process.env.WMS_TEST_USER;
let testPassword = process.env.WMS_TEST_PASSWORD;
const resetToken = process.env.WMS_TEST_RESET_TOKEN;

function isLoopbackBaseUrl(value: string): boolean {
  if (!value) return false;

  try {
    const url = new URL(value);
    const host = url.hostname.toLowerCase();
    const loopbackHostName = 'local' + 'host';
    return host === loopbackHostName
      || host === '::1'
      || host === '[::1]'
      || host === '0:0:0:0:0:0:0:1'
      || host.startsWith('127.');
  } catch {
    return false;
  }
}

if ((!testUser || !testPassword) && existsSync('appsettings.json') && isLoopbackBaseUrl(baseUrl)) {
  const appsettings = JSON.parse(readFileSync('appsettings.json', 'utf-8'));
  const localVerification = appsettings.LocalVerification;
  if (localVerification?.Enabled) {
    testUser ||= localVerification.UserName;
    testPassword ||= localVerification.Password;
  }
}

if (!testUser || !testPassword) {
  throw new Error('WMS_TEST_USER and WMS_TEST_PASSWORD are required. LocalVerification fallback is allowed only when WMS_BASE_URL is a loopback URL.');
}

setup('create authenticated WMS storage state', async ({ page }) => {
  if (resetToken) {
    if (!isLoopbackBaseUrl(baseUrl)) {
      throw new Error('Development password reset is allowed only for an isolated loopback WMS instance.');
    }
    if (!testUser.startsWith('AUDIT_TEST_')) {
      throw new Error('Development password reset requires an AUDIT_TEST_ username.');
    }

    await page.goto('/Account/DevResetPassword', { waitUntil: 'networkidle' });
    await page.locator('input[name="token"]').fill(resetToken);
    await page.locator('input[name="userName"]').fill(testUser);
    await page.locator('input[name="newPassword"]').fill(testPassword);
    await page.locator('button[type="submit"]').click();
    await page.waitForLoadState('networkidle');
    await expect(page, 'development reset should return to login').toHaveURL(/\/Account\/Login/i);
  }

  await page.goto('/Account/Login', { waitUntil: 'networkidle' });

  if (page.url().includes('/Account/SetupAdmin')) {
    if (!isLoopbackBaseUrl(baseUrl)) {
      throw new Error('First-admin bootstrap is allowed only for an isolated loopback WMS instance.');
    }
    if (!testUser.startsWith('AUDIT_TEST_')) {
      throw new Error('First-admin bootstrap requires an AUDIT_TEST_ username.');
    }

    await page.locator('input[name="userName"]').fill(testUser);
    await page.locator('input[name="fullName"]').fill(process.env.WMS_TEST_FULL_NAME || 'AUDIT_TEST Playwright Admin');
    await page.locator('input[name="password"]').fill(testPassword);
    await page.locator('button[type="submit"]').click();
    await page.waitForLoadState('networkidle');
    await expect(page, 'first-admin setup should return to the login flow').toHaveURL(/\/Account\/Login/i);
  }

  await page.locator('input[name="UserName"]').fill(testUser);
  await page.locator('input[name="Password"]').fill(testPassword);
  await page.locator('button[type="submit"]').click();
  await page.waitForLoadState('networkidle');

  if (page.url().includes('/Account/VerifyMfa')) {
    throw new Error('Visual auth setup reached MFA. Use a pre-created WMS_AUTH_STATE for MFA accounts, or use a dedicated test account/session.');
  }

  await expect(page, 'login must reach an authenticated WMS route').not.toHaveURL(/\/Account\/Login/i);
  const authCookies = await page.context().cookies();
  expect(authCookies.some(cookie => cookie.name === '.AspNetCore.Cookies'), 'authenticated WMS cookie').toBe(true);
  await expect(page.locator('body')).toBeVisible();
  await page.context().storageState({ path: authStatePath });
});
