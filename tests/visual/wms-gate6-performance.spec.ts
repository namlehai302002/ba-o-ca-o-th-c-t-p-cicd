import { expect, test } from '@playwright/test';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const journeys = [
  { name: 'Trang chính và menu', path: '/', thresholdMs: 2_000 },
  { name: 'Báo cáo tồn kho', path: '/Reports/Inventory?page=1&pageSize=25', thresholdMs: 2_000 },
  { name: 'Lịch sử nhập xuất', path: '/Reports/StockMovement?page=1&pageSize=50', thresholdMs: 3_000 },
  { name: 'Sổ giao dịch tồn kho', path: '/Reports/InventoryTransactions?page=1&pageSize=50', thresholdMs: 3_000 },
  { name: 'Tổng quan kho', path: '/Reports/WarehouseOverview', thresholdMs: 3_000 },
  { name: 'Sơ đồ kho', path: '/Warehouses/InventoryMap', thresholdMs: 3_000 }
];

function percentile(values: number[], ratio: number): number {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.max(0, Math.ceil(sorted.length * ratio) - 1)] ?? 0;
}

test('browser journeys expose cold and warm timing without runtime errors', async ({ page }) => {
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

  const rows: Array<Record<string, unknown>> = [];
  for (const journey of journeys) {
    const coldStart = Date.now();
    const coldResponse = await page.goto(journey.path, { waitUntil: 'domcontentloaded' });
    const coldMs = Date.now() - coldStart;
    expect(coldResponse?.status(), journey.name).toBeLessThan(400);
    await expect(page).not.toHaveURL(/\/Account\/Login/i);
    await expect(page.locator('body')).toBeVisible();

    const warmMs: number[] = [];
    for (let run = 0; run < 5; run++) {
      const started = Date.now();
      const response = await page.goto(journey.path, { waitUntil: 'domcontentloaded' });
      warmMs.push(Date.now() - started);
      expect(response?.status(), `${journey.name} run ${run + 1}`).toBeLessThan(400);
      await expect(page).not.toHaveURL(/\/Account\/Login/i);
    }

    const p95Ms = percentile(warmMs, 0.95);
    rows.push({
      name: journey.name,
      path: journey.path,
      coldMs,
      samples: warmMs,
      p50Ms: percentile(warmMs, 0.50),
      p95Ms,
      p99Ms: percentile(warmMs, 0.99),
      thresholdMs: journey.thresholdMs,
      passed: p95Ms <= journey.thresholdMs
    });
    expect(p95Ms, `${journey.name} warm p95`).toBeLessThanOrEqual(journey.thresholdMs);
  }

  expect(consoleErrors, 'console.error').toEqual([]);
  expect(pageErrors, 'pageerror').toEqual([]);
  expect(failedRequests, 'requestfailed').toEqual([]);
  expect(serverErrors, 'HTTP 5xx').toEqual([]);

  const outputDirectory = resolve(process.cwd(), 'artifacts', 'performance');
  mkdirSync(outputDirectory, { recursive: true });
  writeFileSync(
    resolve(outputDirectory, 'gate6-browser-journey-timing.json'),
    JSON.stringify({
      auditId: 'AUDIT_TEST_GATE6_READ_ONLY',
      measuredAt: new Date().toISOString(),
      browser: 'chromium',
      viewport: '1440x900',
      method: 'GET navigation only',
      rows,
      runtimeErrors: { consoleErrors, pageErrors, failedRequests, serverErrors }
    }, null, 2),
    'utf8');
});
