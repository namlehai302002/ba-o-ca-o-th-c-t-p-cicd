import { expect, test } from '@playwright/test';

const baseUrl = process.env.WMS_BASE_URL || '';
const testPassword = process.env.WMS_ROLE_TEST_PASSWORD || '';
const runId = process.env.WMS_ROLE_TEST_RUN_ID || '';
const warehouseCode = 'AUDIT_TEST_RWH12';

const roleAccounts = [
  { userName: `AUDIT_TEST_MGR_${runId}`, fullName: `AUDIT TEST Quản lý ${runId}`, roleLabel: 'Quản lý kho' },
  { userName: `AUDIT_TEST_IN_${runId}`, fullName: `AUDIT TEST Nhập kho ${runId}`, roleLabel: 'Nhân viên nhập kho' },
  { userName: `AUDIT_TEST_OUT_${runId}`, fullName: `AUDIT TEST Xuất kho ${runId}`, roleLabel: 'Nhân viên xuất kho' },
  { userName: `AUDIT_TEST_INV_${runId}`, fullName: `AUDIT TEST Kiểm kê ${runId}`, roleLabel: 'Nhân viên tồn kho/kiểm kê' },
  { userName: `AUDIT_TEST_TRN_${runId}`, fullName: `AUDIT TEST Vận chuyển ${runId}`, roleLabel: 'Nhân viên vận chuyển' },
  { userName: `AUDIT_TEST_RPT_${runId}`, fullName: `AUDIT TEST Báo cáo ${runId}`, roleLabel: 'Nhân viên báo cáo' },
  { userName: `AUDIT_TEST_VIEW_${runId}`, fullName: `AUDIT TEST Chỉ xem ${runId}`, roleLabel: 'Chỉ xem' }
] as const;

function isLoopback(value: string) {
  try {
    const host = new URL(value).hostname.toLowerCase();
    return host === 'localhost' || host === '::1' || host === '[::1]' || host.startsWith('127.');
  } catch {
    return false;
  }
}

test('create isolated role audit fixture through application workflows', async ({ page }) => {
  if (!isLoopback(baseUrl)) {
    throw new Error('Role fixture creation is restricted to a loopback WMS instance.');
  }
  if (!testPassword) {
    throw new Error('WMS_ROLE_TEST_PASSWORD is required for isolated role fixture creation.');
  }
  if (!/^\d{17}$/.test(runId)) {
    throw new Error('WMS_ROLE_TEST_RUN_ID must be a 17-digit isolated correlation id.');
  }
  if (roleAccounts.some(account => !account.userName.startsWith('AUDIT_TEST_')) || !warehouseCode.startsWith('AUDIT_TEST_')) {
    throw new Error('Role fixture identifiers must use the AUDIT_TEST_ prefix.');
  }

  await page.goto('/Warehouses', { waitUntil: 'networkidle' });
  await expect(page.locator('#sidebar')).toBeVisible();

  if (!(await page.getByText(warehouseCode, { exact: false }).count())) {
    await page.goto('/Warehouses/Create', { waitUntil: 'networkidle' });
    await page.locator('input[name="WarehouseCode"]').fill(warehouseCode);
    await page.locator('input[name="WarehouseName"]').fill('Kho kiểm thử phân quyền cô lập');
    await page.locator('input[name="Address"]').fill('Môi trường local cô lập - không dùng vận hành thật');
    await page.locator('.enterprise-form-card form button[type="submit"]').click();
    await page.waitForLoadState('networkidle');
    await expect(page).toHaveURL(/\/Warehouses(?:\?|$)/i);
    await expect(page.getByText(warehouseCode, { exact: false })).toBeVisible();
  }

  for (const account of roleAccounts) {
    await page.goto('/Users', { waitUntil: 'networkidle' });
    if (await page.getByText(account.userName, { exact: true }).count()) continue;

    await page.locator('[data-wms-call="openUserModal"]').click();
    const form = page.locator('#modalUser form');
    await expect(form).toBeVisible();

    const roleOption = form.locator('select[name="roleId"] option').filter({ hasText: account.roleLabel }).first();
    const warehouseOption = form.locator('select[name="warehouseId"] option').filter({ hasText: warehouseCode }).first();
    const roleId = await roleOption.getAttribute('value');
    const warehouseId = await warehouseOption.getAttribute('value');
    expect(roleId, `role id for ${account.roleLabel}`).toBeTruthy();
    expect(warehouseId, `warehouse id for ${warehouseCode}`).toBeTruthy();

    await form.locator('input[name="userName"]').fill(account.userName);
    await form.locator('input[name="password"]').fill(testPassword);
    await form.locator('input[name="fullName"]').fill(account.fullName);
    await form.locator('select[name="roleId"]').selectOption(roleId!);
    await form.locator('select[name="warehouseId"]').selectOption(warehouseId!);
    await form.locator('button[type="submit"]').click();
    await page.waitForLoadState('networkidle');

    await expect(page).toHaveURL(/\/Users(?:\?|$)/i);
    await expect(page.getByText(account.userName, { exact: true })).toBeVisible();
    await expect(page.locator('.alert-danger, .validation-summary-errors')).toHaveCount(0);
  }
});
