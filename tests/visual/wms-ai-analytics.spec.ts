import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';

const routes = [
  {
    name: 'analytics',
    path: '/Reports/Analytics?warehouseId=1&days=30',
    markers: ['Phân tích vận hành', 'Trung vị ngày cung ứng', 'Mẫu:']
  },
  {
    name: 'slow-moving',
    path: '/Reports/SlowMovingReport?warehouseId=1&days=90',
    markers: ['Hàng chậm luân chuyển', 'Nhập gần nhất', 'Xuất gần nhất']
  },
  {
    name: 'abc-inventory-value',
    path: '/Reports/AbcAnalysis?warehouseId=1',
    markers: ['ABC theo giá trị tồn kho', 'Pareto A đến 80%', 'không phải ABC theo tốc độ xuất']
  },
  {
    name: 'space-utilization',
    path: '/Reports/SpaceUtilization?warehouseId=1',
    markers: ['Hiệu suất không gian kho', 'Tỷ lệ vị trí có hàng', 'Thiếu dữ liệu công suất']
  },
  {
    name: 'dock-to-stock',
    path: '/Reports/DockToStock?warehouseId=1&days=30',
    markers: ['Thời gian nhập kho', 'Trung vị tổng thời gian', 'P90 tổng thời gian', 'Phiếu thiếu/sai mốc']
  },
  {
    name: 'supplier-inbound-scorecard',
    path: '/Reports/SupplierInboundScorecard?warehouseId=1&days=90',
    markers: ['Hiệu suất nhà cung cấp', 'Độ chính xác hồ sơ/lô/HSD', 'Không suy diễn hư hỏng']
  }
];

test('AI-1 analytics routes keep source labels and reflow safely', async ({ page }, testInfo) => {
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

  const screenshotDirectory = resolve(process.cwd(), 'artifacts', 'ai-smart-cycle-count', 'AI1', 'screenshots');
  mkdirSync(screenshotDirectory, { recursive: true });

  for (const route of routes) {
    const response = await page.goto(route.path, { waitUntil: 'networkidle' });
    expect(response?.status(), `${route.name}: HTTP status`).toBeLessThan(400);
    await expect(page).not.toHaveURL(/\/Account\/Login/i);
    await expect(page.locator('#sidebar')).toBeVisible();

    const main = page.locator('main');
    for (const marker of route.markers) {
      await expect(main.getByText(marker, { exact: false }).first(), `${route.name}: ${marker}`).toBeVisible();
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
    }));

    expect(layout.scrollWidth, `${route.name}: body overflow`).toBeLessThanOrEqual(layout.clientWidth + 1);
    expect(layout.duplicateIds, `${route.name}: duplicate IDs`).toEqual([]);
    expect(layout.clippedControls, `${route.name}: clipped controls`).toEqual([]);
    expect(layout.invalidText, `${route.name}: runtime implementation text`).toBe(false);

    await page.screenshot({
      path: resolve(screenshotDirectory, `${testInfo.project.name}-${route.name}.png`),
      fullPage: true
    });
  }

  expect(consoleErrors, 'console.error').toEqual([]);
  expect(pageErrors, 'pageerror').toEqual([]);
  expect(failedRequests, 'requestfailed').toEqual([]);
  expect(serverErrors, 'HTTP 5xx').toEqual([]);
});
