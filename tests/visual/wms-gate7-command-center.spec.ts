import { expect, test } from '@playwright/test';
import { mkdirSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

type RuntimeIssues = {
  consoleErrors: string[];
  pageErrors: string[];
  failedRequests: string[];
  serverErrors: string[];
};

function observeRuntime(page: import('@playwright/test').Page): RuntimeIssues {
  const issues: RuntimeIssues = {
    consoleErrors: [],
    pageErrors: [],
    failedRequests: [],
    serverErrors: []
  };
  page.on('console', message => {
    if (message.type() === 'error') issues.consoleErrors.push(message.text());
  });
  page.on('pageerror', error => issues.pageErrors.push(error.message));
  page.on('requestfailed', request => issues.failedRequests.push(`${request.method()} ${request.url()}`));
  page.on('response', response => {
    if (response.status() >= 500) issues.serverErrors.push(`${response.status()} ${response.url()}`);
  });
  return issues;
}

async function expectNoPageOverflow(page: import('@playwright/test').Page, label: string) {
  const layout = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    duplicateIds: Array.from(document.querySelectorAll('[id]'))
      .map(element => element.id)
      .filter((id, index, ids) => id && ids.indexOf(id) !== index),
    clippedControls: Array.from(document.querySelectorAll<HTMLElement>('button,a.btn,input,select'))
      .filter(element => element.offsetParent !== null)
      .filter(element => element.clientWidth > 0 && element.scrollWidth > element.clientWidth + 2)
      .map(element => (element.textContent || element.getAttribute('aria-label') || element.tagName).trim())
  }));

  expect(layout.scrollWidth, `${label}: body overflow`).toBeLessThanOrEqual(layout.clientWidth + 1);
  expect(layout.duplicateIds, `${label}: duplicate ids`).toEqual([]);
  expect(layout.clippedControls, `${label}: clipped controls`).toEqual([]);
}

function percentile(values: number[], ratio: number): number {
  const sorted = [...values].sort((left, right) => left - right);
  return sorted[Math.max(0, Math.ceil(sorted.length * ratio) - 1)] ?? 0;
}

test('Management Command Center warm p95 meets the dashboard contract', async ({ page }, testInfo) => {
  const issues = observeRuntime(page);
  const thresholdMs = 3_000;
  const warmSamples: number[] = [];
  let successfulRequests = 0;

  const coldStartedAt = Date.now();
  const coldResponse = await page.goto('/', { waitUntil: 'domcontentloaded' });
  const coldMs = Date.now() - coldStartedAt;
  expect(coldResponse?.status()).toBeLessThan(400);
  await expect(page.locator('[data-dashboard-command-center]')).toBeVisible();

  const measurementStartedAt = Date.now();
  for (let run = 0; run < 10; run++) {
    const startedAt = Date.now();
    const response = await page.goto('/', { waitUntil: 'domcontentloaded' });
    warmSamples.push(Date.now() - startedAt);
    expect(response?.status(), `dashboard warm run ${run + 1}`).toBeLessThan(400);
    await expect(page.locator('[data-dashboard-command-center]')).toBeVisible();
    successfulRequests++;
  }
  const measurementMs = Date.now() - measurementStartedAt;

  const result = {
    auditId: 'AUDIT_TEST_GATE7_COMMAND_CENTER_READ_ONLY',
    measuredAt: new Date().toISOString(),
    route: '/',
    browser: 'chromium',
    viewport: '1440x900',
    method: 'Một lượt cold và 10 lượt GET tuần tự sau warm-up; không tạo tải đồng thời',
    coldMs,
    warmSamples,
    p50Ms: percentile(warmSamples, 0.50),
    p95Ms: percentile(warmSamples, 0.95),
    p99Ms: percentile(warmSamples, 0.99),
    throughputRequestsPerSecond: measurementMs > 0
      ? Number((successfulRequests * 1_000 / measurementMs).toFixed(2))
      : 0,
    errorRatePercent: Number((((warmSamples.length - successfulRequests) * 100) / warmSamples.length).toFixed(2)),
    thresholdMs,
    passed: percentile(warmSamples, 0.95) <= thresholdMs,
    runtimeErrors: issues
  };

  const outputDirectory = resolve(process.cwd(), 'artifacts', 'dashboard-command-center');
  mkdirSync(outputDirectory, { recursive: true });
  writeFileSync(
    resolve(outputDirectory, 'gate7-dashboard-performance.json'),
    JSON.stringify(result, null, 2),
    'utf8');
  await testInfo.attach('gate7-dashboard-performance', {
    body: Buffer.from(JSON.stringify(result, null, 2)),
    contentType: 'application/json'
  });

  expect(result.p95Ms, 'Management Command Center warm p95').toBeLessThanOrEqual(thresholdMs);
  expect(result.errorRatePercent, 'dashboard error rate').toBe(0);
  expect(issues.consoleErrors, 'console.error').toEqual([]);
  expect(issues.pageErrors, 'pageerror').toEqual([]);
  expect(issues.failedRequests, 'requestfailed').toEqual([]);
  expect(issues.serverErrors, 'HTTP 5xx').toEqual([]);
});

test('Management Command Center is actionable, scoped and responsive', async ({ page }, testInfo) => {
  const issues = observeRuntime(page);
  const response = await page.goto('/', { waitUntil: 'domcontentloaded' });
  expect(response?.status()).toBeLessThan(400);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);

  const commandCenter = page.locator('[data-dashboard-command-center]');
  await expect(commandCenter).toBeVisible();
  await expect(commandCenter.locator('h2')).toContainText(/hôm nay/i);
  await expect(commandCenter.locator('.command-center-context span')).toHaveCount(4);
  await expect(commandCenter.locator('.command-center-updated time')).toBeVisible();
  await expect(commandCenter.locator('.command-center-kpi')).toHaveCount(6);
  await expect(commandCenter.locator('select[name="severity"]')).toBeVisible();
  await expect(commandCenter.locator('select[name="workState"]')).toBeVisible();
  await expect(commandCenter.locator('input[name="assignee"]')).toBeVisible();

  const commandBox = await commandCenter.boundingBox();
  const nextWorkspaceBox = await page.locator('.role-workspace-panel, .app-springboard').first().boundingBox();
  expect(commandBox).not.toBeNull();
  expect(nextWorkspaceBox).not.toBeNull();
  expect(commandBox!.y, 'Command Center must precede the legacy workspace').toBeLessThan(nextWorkspaceBox!.y);

  const rows = commandCenter.locator('tbody tr');
  const rowCount = await rows.count();
  if (rowCount > 0) {
    const severityOrder: Record<string, number> = { critical: 0, high: 1, medium: 2, low: 3 };
    const severities = await rows.evaluateAll(elements => elements.map(element => element.getAttribute('data-severity') || 'low'));
    const ranks = severities.map(value => severityOrder[value] ?? 4);
    expect(ranks, 'queue severity ordering').toEqual([...ranks].sort((a, b) => a - b));

    const progressBars = commandCenter.locator('progress.command-center-progress');
    expect(await progressBars.count(), 'work rows expose native progress indicators').toBeGreaterThan(0);
    for (const progress of await progressBars.all()) {
      const value = Number(await progress.getAttribute('value'));
      expect(value).toBeGreaterThanOrEqual(0);
      expect(value).toBeLessThanOrEqual(100);
    }

    const firstAction = commandCenter.locator('.command-center-action-cell a').first();
    const actionUrl = await firstAction.getAttribute('href');
    expect(actionUrl).toBeTruthy();
    const drillDown = await page.goto(actionUrl!, { waitUntil: 'domcontentloaded' });
    expect(drillDown?.status(), `drill-down ${actionUrl}`).toBeLessThan(400);
    await expect(page).not.toHaveURL(/\/Account\/Login/i);
    await page.goto('/', { waitUntil: 'domcontentloaded' });
  } else {
    await expect(commandCenter.locator('.command-center-empty')).toBeVisible();
  }

  await commandCenter.locator('select[name="workState"]').selectOption('overdue');
  await commandCenter.locator('button[type="submit"]').click();
  await page.waitForLoadState('domcontentloaded');
  await expect(page).toHaveURL(/workState=overdue/i);
  const filteredRows = page.locator('[data-dashboard-command-center] tbody tr');
  for (const row of await filteredRows.all()) {
    await expect(row).toHaveAttribute('data-state', 'overdue');
  }

  await expectNoPageOverflow(page, `${testInfo.project.name} command center`);
  await testInfo.attach(`command-center-${testInfo.project.name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png'
  });

  expect(issues.consoleErrors, 'console.error').toEqual([]);
  expect(issues.pageErrors, 'pageerror').toEqual([]);
  expect(issues.failedRequests, 'requestfailed').toEqual([]);
  expect(issues.serverErrors, 'HTTP 5xx').toEqual([]);
});

test('Exception Center GET is read-only and its controls reflow', async ({ page }, testInfo) => {
  const issues = observeRuntime(page);
  const mutatingRequests: string[] = [];
  page.on('request', request => {
    if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method())) {
      mutatingRequests.push(`${request.method()} ${request.url()}`);
    }
  });

  const response = await page.goto('/Operations/ExceptionCenter', { waitUntil: 'networkidle' });
  expect(response?.status()).toBeLessThan(400);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
  const filters = page.locator('form.filter-bar');
  await expect(filters.locator('select[name="caseStatus"]')).toBeVisible();
  await expect(filters.locator('select[name="assignedTo"]')).toBeVisible();
  await expect(filters.locator('select[name="due"]')).toBeVisible();
  expect(mutatingRequests, 'GET page must not issue a hidden write request').toEqual([]);

  await expectNoPageOverflow(page, `${testInfo.project.name} exception center`);
  await testInfo.attach(`exception-center-${testInfo.project.name}`, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png'
  });

  expect(issues.consoleErrors, 'console.error').toEqual([]);
  expect(issues.pageErrors, 'pageerror').toEqual([]);
  expect(issues.failedRequests, 'requestfailed').toEqual([]);
  expect(issues.serverErrors, 'HTTP 5xx').toEqual([]);
});
