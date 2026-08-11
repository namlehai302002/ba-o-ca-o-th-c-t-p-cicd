import { expect, test } from '@playwright/test';

const routes = [
  '/',
  '/Help',
  '/Reports/Inventory?page=1&pageSize=25',
  '/Reports/StockMovement?page=1&pageSize=50',
  '/Reports/InventoryTransactions?page=1&pageSize=50',
  '/Reports/WarehouseOverview',
  '/Warehouses/InventoryMap'
];

test('core read-only routes reflow without browser or accessibility smoke defects', async ({ page }) => {
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

  for (const route of routes) {
    const response = await page.goto(route, { waitUntil: 'domcontentloaded' });
    expect(response?.status(), route).toBeLessThan(400);
    await expect(page).not.toHaveURL(/\/Account\/Login/i);
    await expect(page.locator('body')).toBeVisible();

    const layout = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
      duplicateIds: Array.from(document.querySelectorAll('[id]'))
        .map(element => element.id)
        .filter((id, index, ids) => id && ids.indexOf(id) !== index),
      zeroSizedControls: Array.from(document.querySelectorAll<HTMLElement>('a[href],button,input,select,textarea'))
        .filter(element => {
          const style = getComputedStyle(element);
          if ((element instanceof HTMLInputElement && element.type === 'hidden')
            || style.display === 'none'
            || style.visibility === 'hidden'
            || element.getClientRects().length === 0) return false;
          const box = element.getBoundingClientRect();
          return box.width < 1 || box.height < 1;
        }).length,
      unnamedButtons: Array.from(document.querySelectorAll<HTMLButtonElement>('button'))
        .filter(button => button.offsetParent !== null)
        .filter(button => !((button.textContent || '').trim() || button.getAttribute('aria-label') || button.getAttribute('title')))
        .length,
      unlabeledFields: Array.from(document.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('input,select,textarea'))
        .filter(field => field.offsetParent !== null && field.type !== 'hidden')
        .filter(field => {
          const explicitLabel = field.id ? document.querySelector(`label[for="${CSS.escape(field.id)}"]`) : null;
          const wrappingLabel = field.closest('label');
          return !(explicitLabel || wrappingLabel || field.getAttribute('aria-label') || field.getAttribute('aria-labelledby') || field.getAttribute('placeholder') || field.getAttribute('title'));
        })
        .map(field => ({
          tag: field.tagName.toLowerCase(),
          type: field.type,
          id: field.id,
          name: field.getAttribute('name') || '',
          className: field.className
        }))
    }));

    expect(layout.scrollWidth, `${route} body overflow`).toBeLessThanOrEqual(layout.clientWidth + 1);
    expect(layout.duplicateIds, `${route} duplicate IDs`).toEqual([]);
    expect(layout.zeroSizedControls, `${route} zero-size controls`).toBe(0);
    expect(layout.unnamedButtons, `${route} unnamed buttons`).toBe(0);
    expect(layout.unlabeledFields, `${route} unlabeled fields`).toEqual([]);
  }

  expect(consoleErrors, 'console.error').toEqual([]);
  expect(pageErrors, 'pageerror').toEqual([]);
  expect(failedRequests, 'requestfailed').toEqual([]);
  expect(serverErrors, 'HTTP 5xx').toEqual([]);
});
