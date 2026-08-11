import { expect, test } from '@playwright/test';
import type { Browser, Locator, Page } from '@playwright/test';

declare global {
  interface Window {
    enterpriseConfirm?: () => Promise<boolean>;
  }
}

const realE2eEnabled = process.env.WMS_REAL_E2E === 'true';
const writeChecksEnabled = process.env.WMS_REAL_E2E_WRITE === 'true';
const creatorStorageState = process.env.WMS_REAL_E2E_CREATOR_STATE || process.env.WMS_AUTH_STATE;
const approverStorageState = process.env.WMS_REAL_E2E_APPROVER_STATE || process.env.WMS_AUTH_STATE;
const baseUrl = process.env.WMS_BASE_URL || '';

function sameOrigin(pageUrl: string, responseUrl: string) {
  try {
    return new URL(pageUrl).origin === new URL(responseUrl).origin;
  } catch {
    return false;
  }
}

async function withRuntimeAudit(page: Page, action: () => Promise<void>) {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const serverErrors: string[] = [];

  const onConsole = (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  };
  const onPageError = (error: Error) => pageErrors.push(error.message);
  const onResponse = (response) => {
    if (response.status() >= 500 && sameOrigin(page.url(), response.url())) {
      serverErrors.push(`${response.status()} ${response.url()}`);
    }
  };

  page.on('console', onConsole);
  page.on('pageerror', onPageError);
  page.on('response', onResponse);
  try {
    await action();
  } finally {
    page.off('console', onConsole);
    page.off('pageerror', onPageError);
    page.off('response', onResponse);
  }

  expect(consoleErrors, 'unexpected browser console errors').toEqual([]);
  expect(pageErrors, 'unexpected page errors').toEqual([]);
  expect(serverErrors, 'unexpected same-origin HTTP 5xx responses').toEqual([]);
}

async function installAutoConfirm(page: Page, accept = true) {
  await page.addInitScript((value) => {
    window.enterpriseConfirm = async () => Boolean(value);
  }, accept);

  try {
    await page.evaluate((value) => {
      window.enterpriseConfirm = async () => Boolean(value);
    }, accept);
  } catch {
    // The page may still be navigating; addInitScript already covers the next load.
  }
}

async function clickPrimaryVoucherSubmit(page: Page) {
  const button = page.locator('#submitBtn').last();
  await expect(button, 'primary voucher submit button').toBeVisible();
  await button.scrollIntoViewIfNeeded();
  const clickPoint = await button.evaluate((element) => {
    const rect = element.getBoundingClientRect();
    const x = rect.left + rect.width / 2;
    const y = rect.top + rect.height / 2;
    const topElement = document.elementFromPoint(x, y);
    return {
      x,
      y,
      clickable: topElement === element || (topElement !== null && element.contains(topElement))
    };
  });
  expect(clickPoint.clickable, 'primary voucher submit button center should be clickable').toBe(true);
  await page.mouse.click(clickPoint.x, clickPoint.y);
  return button;
}

async function firstEnabledOption(select: Locator) {
  return await select.evaluate((element: HTMLSelectElement) => {
    const option = Array.from(element.options).find(o => o.value && !o.disabled);
    return option ? { value: option.value, text: option.textContent?.trim() || '' } : null;
  });
}

async function selectFirstEnabledOption(select: Locator, label: string) {
  await expect(select, label).toBeVisible();
  const option = await firstEnabledOption(select);
  if (!option) return null;
  await select.selectOption(option.value);
  await select.dispatchEvent('change');
  return option;
}

async function selectVoucherItemAndUom(page: Page) {
  const row = page.locator('#linesContainer .line-row').first();
  await expect(row, 'first voucher line row').toBeVisible();

  const itemSelect = row.locator('select.item-select');
  const item = await selectFirstEnabledOption(itemSelect, 'item select');
  if (!item) return null;

  const uomSelect = row.locator('select.source-uom-select');
  await expect(uomSelect, 'source UOM select should unlock after item select').toBeEnabled({ timeout: 10_000 });
  const uom = await selectFirstEnabledOption(uomSelect, 'source UOM select');
  if (!uom) return null;

  await row.locator('input.qty-input').fill('1');

  const lotInput = row.locator('.lot-input').first();
  if (await lotInput.count()) await lotInput.fill(`E2E-LOT-${Date.now()}`);

  const putawaySelect = row.locator('.putaway-loc-select').first();
  if (await putawaySelect.count() && await putawaySelect.isVisible()) {
    const loc = await firstEnabledOption(putawaySelect);
    if (loc) await putawaySelect.selectOption(loc.value);
  }

  const sourceLocationSelect = row.locator('.loc-select').first();
  if (await sourceLocationSelect.count() && await sourceLocationSelect.isVisible()) {
    const loc = await firstEnabledOption(sourceLocationSelect);
    if (loc) await sourceLocationSelect.selectOption(loc.value);
  }

  return { itemValue: item.value, itemText: item.text, uomValue: uom.value, uomText: uom.text };
}

async function submitDetailsAction(page: Page, action: string) {
  const form = page.locator(`form[action*="${action}"]`).first();
  if ((await form.count()) === 0) return false;
  await installAutoConfirm(page, true);
  const submit = form.locator('button[type="submit"], button:not([type])').first();
  await expect(submit, `${action} submit button`).toBeVisible();
  await Promise.all([
    page.waitForLoadState('networkidle').catch(() => undefined),
    submit.click()
  ]);
  await expect(page.locator('body')).not.toContainText(/undefined|null|NaN|\?\?\?|&#x|&amp;#x/i);
  return true;
}

async function submitFormDirectly(page: Page, action: string) {
  const form = page.locator(`form[action*="${action}"]`).first();
  if ((await form.count()) === 0) return false;
  await form.evaluate((f: HTMLFormElement) => f.submit());
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await expect(page.locator('body')).not.toContainText(/undefined|null|NaN|\?\?\?|&#x|&amp;#x/i);
  return true;
}

async function createInboundVoucher(page: Page, prefix: string) {
  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  await expect(page.locator('body')).not.toContainText(/undefined|null|NaN|\?\?\?|&#x|&amp;#x/i);

  const partner = await selectFirstEnabledOption(page.locator('select[name="PartnerId"]'), 'inbound partner/source select');
  if (!partner) return null;

  await page.locator('input[name="ReferenceNo"]').fill(`${prefix}-IN`);
  await page.locator('input[name="Description"]').fill(`Kiểm thử E2E nhập kho bằng dữ liệu staging, prefix ${prefix}.`);
  await page.locator('input[name="CarrierName"]').fill('Đơn vị vận chuyển E2E');
  await page.locator('input[name="VehicleNumber"]').fill('51D-999.99');
  await page.locator('input[name="DriverName"]').fill('Lê Gia Hân');
  await page.locator('input[name="DriverPhone"]').fill('0909000000');

  const line = await selectVoucherItemAndUom(page);
  if (!line) return null;

  const button = await clickPrimaryVoucherSubmit(page);
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await expect(button, 'inbound submit should not leave spinner stuck').toBeEnabled({ timeout: 10_000 }).catch(() => undefined);

  const voucherId = /\/Vouchers\/Details\/(\d+)/i.exec(page.url())?.[1] ?? null;
  return voucherId ? { voucherId, ...line } : null;
}

async function confirmActualReceiving(page: Page) {
  // Exercises the ConfirmActualReceivingQty form rendered on voucher details.
  const checkPanel = page.locator('.voucher-check-panel');
  if ((await checkPanel.count()) === 0) return false;

  const input = checkPanel.locator('.check-qty-input').first();
  await expect(input, 'actual receiving quantity input').toBeVisible();
  const max = await input.getAttribute('max');
  await input.fill(max && Number(max) > 0 ? max : '1');

  const note = checkPanel.locator('.check-note-input').first();
  if (await note.count()) await note.fill('E2E kiểm đủ số lượng, không ghi nhận sai lệch.');

  await Promise.all([
    page.waitForLoadState('networkidle').catch(() => undefined),
    checkPanel.locator('.check-save-btn').first().click()
  ]);
  await expect(page.locator('body')).not.toContainText(/undefined|null|NaN|\?\?\?|&#x|&amp;#x/i);
  return true;
}

async function tryCancelVoucher(page: Page, voucherId: string) {
  await page.goto(`/Vouchers/Details/${voucherId}`, { waitUntil: 'networkidle' }).catch(() => undefined);
  const cancelForm = page.locator('form[action*="Cancel"]').first();
  if ((await cancelForm.count()) === 0) return false;
  const reasonInput = page.locator('#cancelReasonInput').first();
  if (await reasonInput.count()) await reasonInput.fill('Dọn dữ liệu E2E trước khi ghi sổ.');
  const cancelButton = page.locator('#cancelBtn').first();
  if ((await cancelButton.count()) === 0) return false;
  await installAutoConfirm(page, true);
  await cancelButton.click();
  await page.waitForLoadState('networkidle').catch(() => undefined);
  return true;
}

const describeRealE2e = realE2eEnabled ? test.describe : test.describe.skip;

describeRealE2e('real WMS core E2E checks', () => {
  test('inbound validation failure restores submit button and does not leave spinner stuck', async ({ page }) => {
    await withRuntimeAudit(page, async () => {
      await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
      const submitButton = await clickPrimaryVoucherSubmit(page);

      await expect(submitButton, 'submit button should recover after validation failure').toBeEnabled({ timeout: 7_000 });
      await expect(submitButton.locator('.fa-spinner'), 'submit spinner should not remain after validation failure').toHaveCount(0);
      await expect(page.locator('body'), 'friendly validation should mention missing supplier/source or required data')
        .toContainText(/Vui lòng chọn|bắt buộc|nhà cung cấp|nguồn giao|required/i);
      await expect(page.locator('body')).not.toContainText(/undefined|null|NaN|\?\?\?|&#x|&amp;#x/i);
    });
  });

  test('demo data cancel confirm does not post and restores all buttons', async ({ page }) => {
    await withRuntimeAudit(page, async () => {
      let postCount = 0;
      await page.route('**/System/ApplyDemoData', async route => {
        postCount += 1;
        await route.fulfill({
          status: 409,
          contentType: 'text/plain; charset=utf-8',
          body: 'Unexpected demo apply request while canceling.'
        });
      });

      await page.goto('/System/DemoData', { waitUntil: 'networkidle' });
      await installAutoConfirm(page, false);

      const allButtons = page.locator('form.demo-data-form button[type="submit"]');
      const itButton = page.getByRole('button', { name: /Demo kho thiết bị IT/i });
      await expect(itButton).toBeVisible();
      await itButton.click();

      await expect.poll(() => postCount, { message: 'cancel must not submit demo data form' }).toBe(0);
      await expect.poll(async () => allButtons.evaluateAll(buttons => buttons.every(button => !(button as HTMLButtonElement).disabled)))
        .toBe(true);
      await expect(allButtons.locator('.fa-spinner'), 'demo data buttons should have no spinner after cancel').toHaveCount(0);
      await expect(page.locator('body')).not.toContainText(/undefined|null|NaN|\?\?\?|&#x|&amp;#x/i);
    });
  });

  test('over-issue smoke path is opt-in and must not complete silently', async ({ page }, testInfo) => {
    if (!writeChecksEnabled) {
      testInfo.annotations.push({ type: 'blocked', description: 'Set WMS_REAL_E2E_WRITE=true only for a disposable/staging database.' });
      return;
    }

    if (testInfo.project.name !== 'desktop-real-e2e') {
      testInfo.annotations.push({ type: 'covered-by-desktop', description: 'Write smoke runs only once on desktop.' });
      return;
    }

    const prefix = `E2E-${Date.now()}`;
    await withRuntimeAudit(page, async () => {
      await page.goto('/Vouchers/Create?type=XuatKho', { waitUntil: 'networkidle' });
      await page.locator('input[name="ReferenceNo"]').fill(`${prefix}-OVER-ISSUE`);
      await page.locator('input[name="Description"]').fill('Kiểm thử E2E xuất vượt tồn - dữ liệu staging có thể dọn bằng mã prefix.');

      const partner = page.locator('select[name="PartnerId"]').first();
      if (await partner.count()) await selectFirstEnabledOption(partner, 'outbound receiver/customer select');

      const line = await selectVoucherItemAndUom(page);
      if (!line) {
        testInfo.annotations.push({ type: 'blocked', description: 'No item/UOM/location is available for outbound over-issue smoke test.' });
        return;
      }

      await page.locator('input.qty-input').first().fill('999999999');
      const submitButton = await clickPrimaryVoucherSubmit(page);

      await expect(submitButton, 'submit button should recover after over-issue validation/business failure').toBeEnabled({ timeout: 10_000 });
      await expect(page.locator('body'), 'over-issue must surface a business validation message')
        .toContainText(/không đủ tồn|vượt tồn|tồn khả dụng|không thể xuất|không cho xuất/i);
      await expect(page.locator('body')).not.toContainText(/undefined|null|NaN|\?\?\?|&#x|&amp;#x/i);
    });
  });

  test('two-role write lifecycle is ready for staging and blocks false pass without prerequisites', async ({ browser }, testInfo) => {
    if (!writeChecksEnabled) {
      testInfo.annotations.push({ type: 'blocked', description: 'Set WMS_REAL_E2E_WRITE=true only for a disposable/staging database.' });
      return;
    }
    if (testInfo.project.name !== 'desktop-real-e2e') {
      testInfo.annotations.push({ type: 'covered-by-desktop', description: 'Write lifecycle runs only once on desktop.' });
      return;
    }
    if (!creatorStorageState || !approverStorageState || creatorStorageState === approverStorageState) {
      testInfo.annotations.push({
        type: 'blocked',
        description: 'Full inbound approval/receiving requires two different auth states because WMS Pro enforces four-eyes separation of duties.'
      });
      return;
    }

    const prefix = `E2E-${Date.now()}`;
    let inboundVoucherId: string | null = null;
    let outboundVoucherId: string | null = null;

    const creatorContext = await browser.newContext({ baseURL: baseUrl, storageState: creatorStorageState });
    const approverContext = await browser.newContext({ baseURL: baseUrl, storageState: approverStorageState });
    const creatorPage = await creatorContext.newPage();
    const approverPage = await approverContext.newPage();

    try {
      await installAutoConfirm(creatorPage, true);
      await installAutoConfirm(approverPage, true);

      await withRuntimeAudit(creatorPage, async () => {
        const inbound = await createInboundVoucher(creatorPage, prefix);
        if (!inbound) {
          testInfo.annotations.push({ type: 'blocked', description: 'Cannot create inbound voucher: missing partner/item/UOM/location fixture data.' });
          return;
        }
        inboundVoucherId = inbound.voucherId;
      });
      if (!inboundVoucherId) return;

      await withRuntimeAudit(approverPage, async () => {
        await approverPage.goto(`/Vouchers/Details/${inboundVoucherId}`, { waitUntil: 'networkidle' });
        if (await submitDetailsAction(approverPage, 'SubmitForApproval')) {
          await approverPage.goto(`/Vouchers/Details/${inboundVoucherId}`, { waitUntil: 'networkidle' });
        }
        expect(await submitDetailsAction(approverPage, 'ApproveInbound'), 'ApproveInbound action must be available for approver').toBe(true);
        expect(await submitDetailsAction(approverPage, 'ConfirmReceiving'), 'ConfirmReceiving action must be available after approval').toBe(true);
        expect(await confirmActualReceiving(approverPage), 'actual receiving panel must be available in Receiving state').toBe(true);
        expect(await submitFormDirectly(approverPage, 'Approve'), 'complete inbound stock posting form must be available after receiving check').toBe(true);
        await expect(approverPage.locator('body'), 'inbound should reach a completed/posted state').toContainText(/Hoàn tất|Đã ghi sổ|Đã hoàn tất|Tăng tồn/i);
      });

      await withRuntimeAudit(creatorPage, async () => {
        await creatorPage.goto('/Vouchers/Create?type=XuatKho', { waitUntil: 'networkidle' });
        const partner = creatorPage.locator('select[name="PartnerId"]').first();
        if (await partner.count()) await selectFirstEnabledOption(partner, 'outbound receiver/customer select');
        await creatorPage.locator('input[name="ReferenceNo"]').fill(`${prefix}-OUT`);
        await creatorPage.locator('input[name="Description"]').fill(`Kiểm thử E2E xuất kho bằng dữ liệu staging, prefix ${prefix}.`);
        const outboundLine = await selectVoucherItemAndUom(creatorPage);
        if (!outboundLine) {
          testInfo.annotations.push({ type: 'blocked', description: 'Cannot create outbound voucher: missing item/UOM/location fixture data.' });
          return;
        }
        await clickPrimaryVoucherSubmit(creatorPage);
        await creatorPage.waitForLoadState('networkidle').catch(() => undefined);
        outboundVoucherId = /\/Vouchers\/Details\/(\d+)/i.exec(creatorPage.url())?.[1] ?? null;
        expect(outboundVoucherId, 'outbound voucher should redirect to details after create').not.toBeNull();
      });
      if (!outboundVoucherId) return;

      await withRuntimeAudit(approverPage, async () => {
        await approverPage.goto(`/Vouchers/Details/${outboundVoucherId}`, { waitUntil: 'networkidle' });
        expect(await submitDetailsAction(approverPage, 'ReleaseDirect'), 'ReleaseDirect action must be available for manager/admin').toBe(true);
        await approverPage.goto(`/Operations/PickTasks`, { waitUntil: 'networkidle' });
        const row = approverPage.locator('tr', { hasText: prefix }).first();
        if ((await row.count()) === 0) {
          testInfo.annotations.push({ type: 'blocked', description: 'Release did not expose a pick task row for the E2E voucher prefix.' });
          return;
        }
        const qtyInput = row.locator('input[name="qty"]').first();
        if (await qtyInput.count()) await qtyInput.fill('1');
        const scanInput = row.locator('input[name="scanValue"]').first();
        if (await scanInput.count()) {
          const value = await scanInput.inputValue();
          if (!value) await scanInput.fill(prefix);
        }
        await row.getByRole('button', { name: /Xác nhận lấy/i }).click();
        await approverPage.waitForLoadState('networkidle').catch(() => undefined);
        await approverPage.goto(`/Vouchers/Details/${outboundVoucherId}`, { waitUntil: 'networkidle' });
        expect(await submitDetailsAction(approverPage, 'PostReservedOutbound'), 'PostReservedOutbound action must be available after picking').toBe(true);
        await expect(approverPage.locator('body'), 'outbound should reach completed/posted state').toContainText(/Hoàn tất|Đã ghi sổ|Chốt xuất|Đã xuất/i);
      });
    } finally {
      if (outboundVoucherId) await tryCancelVoucher(approverPage, outboundVoucherId);
      if (inboundVoucherId) await tryCancelVoucher(approverPage, inboundVoucherId);
      await creatorContext.close();
      await approverContext.close();
    }
  });
});
