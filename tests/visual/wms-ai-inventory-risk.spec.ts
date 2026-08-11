import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';

test('AI-2 inventory-risk shadow screen is read-only and reflows safely', async ({ page }, testInfo) => {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedRequests: string[] = [];
  const serverErrors: string[] = [];

  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => pageErrors.push(error.message));
  page.on('requestfailed', request => failedRequests.push(`${request.method()} ${request.url()}`));
  page.on('response', response => {
    if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`);
  });

  const response = await page.goto('/Reports/InventoryRisk?pageSize=10', { waitUntil: 'networkidle' });
  expect(response?.status(), 'inventory-risk HTTP status').toBeLessThan(400);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
  await expect(page.getByRole('heading', { name: 'Kiểm kê thông minh', exact: true })).toBeVisible();
  await expect(page.getByTestId('risk-mode')).toHaveText('THỬ NGHIỆM');
  await expect(page.getByTestId('risk-contract')).toContainText('Đây không phải xác suất');
  await expect(page.getByTestId('risk-contract')).toContainText('không làm thay đổi tồn kho');
  await expect(page.getByTestId('risk-filters')).toBeVisible();
  await expect(page.getByTestId('risk-metrics')).toBeVisible();
  await expect(page.getByTestId('risk-results')).toBeVisible();

  const shadowButton = page.getByRole('button', { name: /Lưu kết quả thử nghiệm/i });
  if (await shadowButton.count()) {
    await expect(shadowButton).toBeDisabled();
  }

  const layout = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    duplicateIds: Array.from(document.querySelectorAll('[id]'))
      .map(element => element.id)
      .filter((id, index, ids) => id && ids.indexOf(id) !== index),
    clippedControls: Array.from(document.querySelectorAll<HTMLElement>('button,a.btn,input,select'))
      .filter(element => element.offsetParent !== null)
      .filter(element => element.clientWidth > 0 && element.scrollWidth > element.clientWidth + 2)
      .map(element => (element.textContent || element.getAttribute('aria-label') || element.tagName).trim()),
    invalidText: (document.body.textContent || '').includes('System.Collections')
      || (document.body.textContent || '').includes('Microsoft.EntityFrameworkCore')
      || (document.body.textContent || '').includes('InventoryRiskDataQualityStatusEnum'),
    leakedDataQualityCode: /\b(?:PARTIAL|BLOCKED)_[A-Z0-9_]+\b/.test(document.body.textContent || '')
  }));

  expect(layout.scrollWidth, 'body horizontal overflow').toBeLessThanOrEqual(layout.clientWidth + 1);
  expect(layout.duplicateIds, 'duplicate IDs').toEqual([]);
  expect(layout.clippedControls, 'clipped controls').toEqual([]);
  expect(layout.invalidText, 'runtime implementation text').toBe(false);
  expect(layout.leakedDataQualityCode, 'technical data-quality code leaked to operators').toBe(false);

  const tableRegion = page.locator('.inventory-risk-table-wrap');
  const nestedScroll = await tableRegion.evaluate(element => ({
    scrollHeight: element.scrollHeight,
    clientHeight: element.clientHeight,
    overflowY: getComputedStyle(element).overflowY,
    overscrollY: getComputedStyle(element).overscrollBehaviorY
  }));
  expect(nestedScroll.scrollHeight, 'smart-count table must not create a nested vertical scroll range')
    .toBeLessThanOrEqual(nestedScroll.clientHeight + 1);
  expect(nestedScroll.overflowY, 'smart-count table vertical overflow').not.toBe('auto');
  expect(nestedScroll.overscrollY, 'smart-count wheel chaining').not.toBe('contain');

  await page.evaluate(() => window.scrollTo(0, 0));
  await tableRegion.hover();
  await page.mouse.wheel(0, 420);
  await expect.poll(() => page.evaluate(() => window.scrollY), {
    message: 'page must scroll while the pointer is over the smart-count table'
  }).toBeGreaterThan(0);
  expect(consoleErrors, 'console.error').toEqual([]);
  expect(pageErrors, 'pageerror').toEqual([]);
  expect(failedRequests, 'requestfailed').toEqual([]);
  expect(serverErrors, 'HTTP 5xx').toEqual([]);

  const screenshotDirectory = resolve(process.cwd(), 'artifacts', 'ai-smart-cycle-count', 'AI2', 'screenshots');
  mkdirSync(screenshotDirectory, { recursive: true });
  await page.screenshot({
    path: resolve(screenshotDirectory, `${testInfo.project.name}-inventory-risk.png`),
    fullPage: true
  });
});
