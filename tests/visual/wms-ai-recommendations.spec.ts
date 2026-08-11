import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';

test('AI-3 recommendation screen is safe, explicit and responsive', async ({ page }, testInfo) => {
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

  const response = await page.goto('/Reports/InventoryRiskRecommendations?pageSize=10', { waitUntil: 'networkidle' });
  expect(response?.status(), 'recommendation HTTP status').toBeLessThan(400);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
  await expect(page.getByRole('heading', { name: 'Đề xuất kiểm kê', exact: true })).toBeVisible();
  await expect(page.getByTestId('recommendation-contract')).toContainText('Quản lý quyết định');
  await expect(page.getByTestId('recommendation-contract')).toContainText('không làm thay đổi tồn kho');
  await expect(page.getByTestId('recommendation-filters')).toBeVisible();
  await expect(page.getByTestId('recommendation-metrics')).toBeVisible();
  await expect(page.getByTestId('recommendation-results')).toBeVisible();

  const schemaWarning = page.getByTestId('recommendation-schema-warning');
  const generateButton = page.getByRole('button', { name: /Chuẩn bị đề xuất/i });
  if (await schemaWarning.count()) {
    await expect(schemaWarning).toContainText('không có migration nào được tự động chạy');
    await expect(generateButton).toBeDisabled();
  }

  const layout = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    recommendationTableOverflow: (() => {
      const wrapper = document.querySelector<HTMLElement>('.recommendation-table-wrap');
      const table = document.querySelector<HTMLElement>('.recommendation-table');
      if (!wrapper || !table) return null;
      return {
        wrapperClientWidth: wrapper.clientWidth,
        wrapperScrollWidth: wrapper.scrollWidth,
        tableWidth: table.getBoundingClientRect().width
      };
    })(),
    duplicateIds: Array.from(document.querySelectorAll('[id]'))
      .map(element => element.id)
      .filter((id, index, ids) => id && ids.indexOf(id) !== index),
    clippedControls: Array.from(document.querySelectorAll<HTMLElement>('button,a.btn,input,select,textarea'))
      .filter(element => element.offsetParent !== null)
      .filter(element => element.clientWidth > 0 && element.scrollWidth > element.clientWidth + 2)
      .map(element => (element.textContent || element.getAttribute('aria-label') || element.tagName).trim()),
    implementationText: /System\.Collections|Microsoft\.EntityFrameworkCore|CycleCountRecommendationStateEnum/.test(document.body.textContent || ''),
    exposedSecretMarker: /password\s*=|api[_ -]?key\s*=|connection string/i.test(document.body.textContent || '')
  }));

  expect(layout.scrollWidth, 'body horizontal overflow').toBeLessThanOrEqual(layout.clientWidth + 1);
  expect(layout.recommendationTableOverflow, 'recommendation table metrics').not.toBeNull();
  if (testInfo.project.name.startsWith('mobile')) {
    expect(
      layout.recommendationTableOverflow!.wrapperScrollWidth,
      'mobile recommendation table internal overflow'
    ).toBeLessThanOrEqual(layout.recommendationTableOverflow!.wrapperClientWidth + 1);
    expect(
      layout.recommendationTableOverflow!.tableWidth,
      'mobile recommendation table width'
    ).toBeLessThanOrEqual(layout.recommendationTableOverflow!.wrapperClientWidth + 1);
  }
  expect(layout.duplicateIds, 'duplicate IDs').toEqual([]);
  expect(layout.clippedControls, 'clipped controls').toEqual([]);
  expect(layout.implementationText, 'runtime implementation text').toBe(false);
  expect(layout.exposedSecretMarker, 'secret-like text').toBe(false);
  expect(consoleErrors, 'console.error').toEqual([]);
  expect(pageErrors, 'pageerror').toEqual([]);
  expect(failedRequests, 'requestfailed').toEqual([]);
  expect(serverErrors, 'HTTP 5xx').toEqual([]);

  const screenshotDirectory = resolve(process.cwd(), 'artifacts', 'ai-smart-cycle-count', 'AI3', 'screenshots');
  mkdirSync(screenshotDirectory, { recursive: true });
  await page.screenshot({
    path: resolve(screenshotDirectory, `${testInfo.project.name}-recommendations.png`),
    fullPage: true
  });
});
