import { expect, test } from '@playwright/test';
import type { Locator, Page, TestInfo } from '@playwright/test';

const routes = [
  { name: 'home', path: '/', snapshot: false },
  { name: 'help', path: '/Help' },
  { name: 'users', path: '/Users', snapshot: false },
  { name: 'voucher-create', path: '/Vouchers/Create?type=NhapKho' },
  { name: 'receiving', path: '/Operations/Receiving', snapshot: false },
  { name: 'rf-receiving', path: '/Operations/RfReceiving', snapshot: false },
  { name: 'quality-inspection', path: '/Operations/QualityInspection', snapshot: false },
  { name: 'inbound-approvals', path: '/Operations/InboundApprovals', snapshot: false },
  { name: 'picking', path: '/Operations/PickTasks', snapshot: false },
  { name: 'rf-picking', path: '/Operations/RfPicking', snapshot: false },
  { name: 'waves', path: '/Operations/Waves', snapshot: false },
  { name: 'wave-planning', path: '/Vouchers/WavePlanning', snapshot: false },
  { name: 'rf-movement', path: '/Operations/RfMovement' },
  { name: 'movement-tasks', path: '/Operations/MovementTasks' },
  { name: 'shipping', path: '/Operations/Shipping', snapshot: false },
  { name: 'shipping-dispatch', path: '/Operations/ShippingDispatch' },
  { name: 'inventory', path: '/Reports/Inventory', snapshot: false },
  { name: 'stock-movement', path: '/Reports/StockMovement', snapshot: false },
  { name: 'inventory-in-out-summary', path: '/Reports/InventoryInOutSummary', snapshot: false },
  { name: 'warehouse-overview', path: '/Reports/WarehouseOverview', snapshot: false },
  { name: 'inventory-transactions', path: '/Reports/InventoryTransactions' },
  { name: 'stock-valuation', path: '/Reports/StockValuation', snapshot: false },
  { name: 'stock-count', path: '/Reports/StockCount' },
  { name: 'exception-center', path: '/Operations/ExceptionCenter', snapshot: false },
  { name: 'slotting', path: '/Operations/Slotting', snapshot: false },
  { name: 'slotting-simulation', path: '/Operations/SlottingSimulation', snapshot: false },
  { name: 'yard-management', path: '/Operations/YardManagement' },
  { name: 'dock-board', path: '/Operations/DockBoard', snapshot: false },
  { name: 'optimization-dashboard', path: '/Operations/OptimizationDashboard' },
  { name: 'automation-dashboard', path: '/Operations/AutomationDashboard' },
  { name: 'integration-dashboard', path: '/Operations/IntegrationDashboard' },
  { name: 'carrier-connectors', path: '/Operations/CarrierConnectors' },
  { name: 'delivery-reconciliation', path: '/Operations/DeliveryReconciliation' },
  { name: 'label-templates', path: '/Labels/Templates', snapshot: false },
  { name: 'label-print-jobs', path: '/Labels/PrintJobs', snapshot: false },
  { name: 'three-pl-runs', path: '/Operations/ThreePlBillingRuns' },
  { name: 'three-pl-rates', path: '/Operations/ThreePlBillingRates' },
  { name: 'semantic-bi', path: '/Reports/SemanticBi', snapshot: false },
  { name: 'predictive-alerts', path: '/Reports/PredictiveAlerts', snapshot: false },
  { name: 'ai-assistant', path: '/Reports/AiAssistant' },
  { name: 'workflow-profiles', path: '/Operations/WorkflowProfiles' },
  { name: 'sre-dashboard', path: '/System/SreDashboard' }
];

const zoomByProject: Record<string, number> = {
  'desktop-100': 1,
  'desktop-110': 1.1,
  'desktop-125': 1.25,
  mobile: 1
};

const dynamicTableRoutes = new Set([
  'quality-inspection',
  'inbound-approvals',
  'movement-tasks',
  'waves',
  'wave-planning',
  'shipping',
  'shipping-dispatch',
  'stock-movement',
  'inventory-in-out-summary',
  'warehouse-overview',
  'inventory-transactions',
  'stock-valuation',
  'stock-count',
  'slotting',
  'slotting-simulation',
  'label-templates',
  'label-print-jobs'
]);

const textFromCodePoints = (...points: number[]) => String.fromCodePoint(...points);
const mojibakeTokens = [
  textFromCodePoints(0x00c3, 0x0192),
  textFromCodePoints(0x00c3, 0x201e),
  textFromCodePoints(0x00c3, 0x2020),
  textFromCodePoints(0x00c3, 0x00a1, 0x00c2, 0x00ba),
  textFromCodePoints(0x00c3, 0x00a1, 0x00c2, 0x00bb),
  textFromCodePoints(0x00c3, 0x201a),
  textFromCodePoints(0x00ef, 0x00bf, 0x00bd)
];
const invalidUiMarkers = ['@@media', '@@page'];

function isSameOriginAsset(pageUrl: string, responseUrl: string) {
  try {
    return new URL(responseUrl).origin === new URL(pageUrl).origin;
  } catch {
    return false;
  }
}

function isIgnorableBrowserResourceNoise(message: string) {
  return message.includes('Failed to load resource: net::ERR_QUIC_PROTOCOL_ERROR');
}

async function gotoVisualAudited(page: Parameters<typeof screenshotMasks>[0], route: { name: string; path: string }, projectName: string) {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const sameOriginServerErrors: string[] = [];
  const onConsole = (message: { type(): string; text(): string }) => {
    const text = message.text();
    if (message.type() === 'error' && !isIgnorableBrowserResourceNoise(text)) consoleErrors.push(text);
  };
  const onPageError = (error: Error) => pageErrors.push(error.message);
  const onResponse = (response: { status(): number; url(): string }) => {
    if (response.status() >= 500 && isSameOriginAsset(page.url(), response.url())) {
      sameOriginServerErrors.push(`${response.status()} ${response.url()}`);
    }
  };

  page.on('console', onConsole);
  page.on('pageerror', onPageError);
  page.on('response', onResponse);
  try {
    const response = await page.goto(route.path, { waitUntil: 'networkidle' });
    expect(response?.status() ?? 200, `${route.name} ${projectName} HTTP status`).toBeLessThan(400);
    const currentPath = new URL(page.url()).pathname.toLowerCase();
    expect(currentPath, `${route.name} ${projectName} must not redirect to login`).not.toBe('/account/login');
    await expect(page.locator('#sidebar'), `${route.name} ${projectName} authenticated sidebar`).toBeVisible();
    expect(consoleErrors, `${route.name} ${projectName} console errors`).toEqual([]);
    expect(pageErrors, `${route.name} ${projectName} page errors`).toEqual([]);
    expect(sameOriginServerErrors, `${route.name} ${projectName} same-origin 5xx`).toEqual([]);
  } finally {
    page.off('console', onConsole);
    page.off('pageerror', onPageError);
    page.off('response', onResponse);
  }
}

async function assertNoTextOrMarkerRegression(page: Parameters<typeof screenshotMasks>[0], routeName: string, projectName: string) {
  const result = await page.locator('body').evaluate((body, payload) => {
    const text = body.textContent || '';
    const html = document.documentElement.innerHTML;
    return {
      mojibakeHits: payload.mojibakeTokens.filter((token) => text.includes(token)),
      markerHits: payload.invalidUiMarkers.filter((token) => html.includes(token)),
      overflowingButtons: Array.from(document.querySelectorAll<HTMLElement>('button, a.btn'))
        .filter((element) => {
          const style = window.getComputedStyle(element);
          const rect = element.getBoundingClientRect();
          if (style.display === 'none' || style.visibility === 'hidden' || rect.width <= 1 || rect.height <= 1) return false;
          return element.scrollWidth > element.clientWidth + 2 || element.scrollHeight > element.clientHeight + 2;
        })
        .slice(0, 8)
        .map((element) => (element.textContent || element.getAttribute('aria-label') || element.className || element.tagName).trim().slice(0, 80))
    };
  }, { mojibakeTokens, invalidUiMarkers });

  expect(result.mojibakeHits, `${routeName} ${projectName} visible text mojibake`).toEqual([]);
  expect(result.markerHits, `${routeName} ${projectName} invalid CSS/Razor markers`).toEqual([]);
  expect(result.overflowingButtons, `${routeName} ${projectName} button/link text overflow`).toEqual([]);
}

function screenshotMasks(page, routeName: string) {
  if (dynamicTableRoutes.has(routeName)) {
    return [
      page.locator('.metric-grid'),
      page.locator('.stat-grid'),
      page.locator('tbody')
    ];
  }

  if (routeName === 'sre-dashboard') {
    return [
      page.locator('.metric-grid'),
      page.locator('.enterprise-section').nth(0),
      page.locator('.yardops-two-column')
    ];
  }

  if (routeName === 'semantic-bi') {
    return [
      page.locator('.metric-grid'),
      page.locator('.enterprise-section').nth(1).locator('tbody')
    ];
  }

  if (routeName === 'exception-center') {
    return [
      page.locator('.stats-grid'),
      page.locator('.filter-bar select[name="category"]'),
      page.locator('.exception-center-table-container')
    ];
  }

  if (routeName === 'home') {
    return [
      page.locator('.stats-grid'),
      page.locator('.task-card-grid'),
      page.locator('.app-springboard'),
      page.locator('.role-workspace-panel')
    ];
  }

  return [];
}

async function stabilizeRouteForScreenshot(page, routeName: string) {
  await page.addStyleTag({
    content: `
      @media (min-width: 701px) {
        .offline-queue-widget.is-empty { display: none !important; }
      }
    `
  });

  if (routeName === 'stock-count') {
    await page.locator('input[name="countDate"]').fill('2026-07-14');
  }

  if (routeName === 'inventory-transactions') {
    await page.locator('input[name="dateFrom"]').fill('2026-06-15');
    await page.locator('input[name="dateTo"]').fill('2026-07-15');
    await page.addStyleTag({
      content: `
        .table-responsive {
          height: 420px !important;
          min-height: 420px !important;
          max-height: 420px !important;
          overflow: hidden !important;
        }
      `
    });
  }

  if (routeName === 'three-pl-runs') {
    for (const [name, value] of [['periodFrom', '2026-06-14'], ['periodTo', '2026-07-14']] as const) {
      const inputs = page.locator(`input[name="${name}"]`);
      for (let index = 0; index < await inputs.count(); index += 1) {
        await inputs.nth(index).fill(value);
      }
    }
  }

  if (routeName === 'semantic-bi') {
    await page.addStyleTag({
      content: `
        .metric-grid { min-height: 128px !important; max-height: 128px !important; overflow: hidden !important; }
        .enterprise-section:nth-of-type(2) .semantic-bi-snapshot-wrap {
          max-height: 460px !important;
          overflow: hidden !important;
        }
        .enterprise-section:nth-of-type(2) tbody tr:nth-child(n+6) { display: none !important; }
        @media (max-width: 700px) {
          .enterprise-section:nth-of-type(2) tbody tr:nth-child(-n+7) { display: table-row !important; }
          .enterprise-section:nth-of-type(2) tbody tr:nth-child(n+8) { display: none !important; }
        }
      `
    });
  }

  if (routeName === 'sre-dashboard') {
    await page.addStyleTag({
      content: `
        .metric-grid { min-height: 128px !important; max-height: 128px !important; overflow: hidden !important; }
        .enterprise-section { min-height: 340px !important; max-height: 340px !important; overflow: hidden !important; }
        .yardops-two-column { min-height: 220px !important; max-height: 220px !important; overflow: hidden !important; }
      `
    });
  }

  if (routeName === 'home') {
    await page.addStyleTag({
      content: `
        .stats-grid { min-height: 128px !important; max-height: 128px !important; overflow: hidden !important; }
        .app-springboard { max-height: 360px !important; overflow: hidden !important; }
        .task-card-grid { max-height: 360px !important; overflow: hidden !important; }
        .role-workspace-panel { max-height: 360px !important; overflow: hidden !important; }
      `
    });
  }

  if (routeName === 'receiving') {
    await page.addStyleTag({
      content: `
        .table-container tbody tr:nth-child(n+9) { display: none !important; }
        .table-container { max-height: 1180px !important; overflow: hidden !important; }
      `
    });
  }

  if (routeName === 'quality-inspection') {
    await page.addStyleTag({
      content: `
        .stats-grid { min-height: 128px !important; max-height: 128px !important; overflow: hidden !important; }
        .table-container tbody tr:nth-child(n+8) { display: none !important; }
      `
    });
  }

  if (routeName === 'dock-board') {
    await page.addStyleTag({
      content: `
        .dock-clock { visibility: hidden !important; }
        .dock-appt:nth-child(n+2) { display: none !important; }
        .yardops-table tbody tr:nth-child(n+7) { display: none !important; }
      `
    });
  }

  if (routeName === 'exception-center') {
    await page.evaluate(() => {
      document.querySelectorAll<HTMLElement>('.exception-center-table td:nth-child(5) .text-muted.fs-body-sm').forEach((element) => {
        element.textContent = (element.textContent || '').replace(/Quá hạn [\d.,]+ giờ\./g, 'Quá hạn trong thời gian kiểm thử.');
      });
    });

    await page.addStyleTag({
      content: `
        .exception-center-table-container {
          height: 1180px !important;
          min-height: 1180px !important;
          max-height: 1180px !important;
          overflow: hidden !important;
        }
        .exception-center-table tbody tr:nth-child(n+4) { display: none !important; }
        .exception-center-table th:nth-child(7),
        .exception-center-table td:nth-child(7) { visibility: hidden !important; }
        @media (max-width: 700px) {
          .exception-center-table-container {
            height: 1540px !important;
            min-height: 1540px !important;
            max-height: 1540px !important;
          }
        }
      `
    });
  }

  if (dynamicTableRoutes.has(routeName)) {
    await page.addStyleTag({
      content: `
        .metric-grid, .stat-grid { min-height: 128px !important; max-height: 128px !important; overflow: hidden !important; }
        .table-container tbody tr:nth-child(n+9),
        .table-responsive tbody tr:nth-child(n+9),
        .enterprise-table-wrap tbody tr:nth-child(n+9) { display: none !important; }
        .table-container,
        .table-responsive,
        .enterprise-table-wrap {
          max-height: 980px !important;
          overflow: hidden !important;
        }
      `
    });
  }
}

function attachRuntimeAudit(page: Page, label: string, allowedConsoleErrors: RegExp[] = []) {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const sameOriginServerErrors: string[] = [];
  const onConsole = (message: { type(): string; text(): string }) => {
    const text = message.text();
    if (message.type() === 'error' && !isIgnorableBrowserResourceNoise(text)) consoleErrors.push(text);
  };
  const onPageError = (error: Error) => pageErrors.push(error.message);
  const onResponse = (response: { status(): number; url(): string }) => {
    if (response.status() >= 500 && isSameOriginAsset(page.url(), response.url())) {
      sameOriginServerErrors.push(`${response.status()} ${response.url()}`);
    }
  };
  page.on('console', onConsole);
  page.on('pageerror', onPageError);
  page.on('response', onResponse);

  return () => {
    page.off('console', onConsole);
    page.off('pageerror', onPageError);
    page.off('response', onResponse);
    const unexpectedConsoleErrors = consoleErrors.filter(error => !allowedConsoleErrors.some(pattern => pattern.test(error)));
    expect(unexpectedConsoleErrors, `${label} console errors`).toEqual([]);
    expect(pageErrors, `${label} page errors`).toEqual([]);
    expect(sameOriginServerErrors, `${label} same-origin 5xx`).toEqual([]);
  };
}

function markMissingFixture(testInfo: TestInfo, description: string) {
  testInfo.annotations.push({ type: 'blocked', description: `missing master-data fixture: ${description}` });
}

async function getVoucherDocumentFixture(page: Page, testInfo: TestInfo) {
  const itemOption = page.locator('select.item-select option:not([value=""]):not([disabled])').first();
  const partnerOption = page.locator('select[name="PartnerId"] option:not([value=""]):not([disabled])').first();
  const warehouseControl = page.locator('select[name="WarehouseId"], input[name="WarehouseId"]').first();

  if (!(await itemOption.count()) || !(await partnerOption.count()) || !(await warehouseControl.count())) {
    markMissingFixture(testInfo, 'voucher UI requires at least one active item, partner and warehouse');
    return null;
  }

  const item = await itemOption
    .evaluate((option) => {
      const opt = option as HTMLOptionElement;
      const itemText = opt.textContent?.trim() || '';
      const match = itemText.match(/^\[([^\]]+)\]\s*(.*)$/);
      return {
        itemId: opt.value,
        itemCode: match?.[1] || itemText,
        itemName: match?.[2] || itemText,
        baseUomId: opt.dataset.uom || '',
        trackExpiry: opt.dataset.trackExpiry === 'true',
        itemText
      };
    });
  const partnerId = await partnerOption.evaluate(option => (option as HTMLOptionElement).value);
  const warehouseId = await warehouseControl.evaluate(element => (element as HTMLSelectElement | HTMLInputElement).value);
  if (!item.itemId || !partnerId || !warehouseId) {
    markMissingFixture(testInfo, 'voucher master-data controls do not expose usable identifiers');
    return null;
  }
  return { item, partnerId, warehouseId };
}

async function mockAnalyzeReceipt(page: Page, fixture: { item: { itemId: string; itemCode: string; itemName: string; baseUomId: string; trackExpiry: boolean }; partnerId: string; warehouseId: string }, referenceNo: string) {
  const lotNumber = `${fixture.item.itemCode.replace(/[^A-Z0-9]+/gi, '-').slice(0, 18)}-260601`;
  const line = {
    ItemId: Number(fixture.item.itemId),
    ItemCode: fixture.item.itemCode,
    ItemName: fixture.item.itemName,
    Quantity: 25,
    UnitPrice: 18500,
    UnitName: 'Cai',
    LotNumber: lotNumber,
    ManufacturingDate: '2026-06-01',
    ExpiryDate: '2028-12-31',
    BaseUomId: Number(fixture.item.baseUomId),
    TransactionUomId: Number(fixture.item.baseUomId),
    IsMatched: true,
    RequiresReview: false,
    DocumentNumber: referenceNo,
    LineNumber: 1
  };
  const header = {
    ReferenceNo: referenceNo,
    VoucherDate: '2026-06-01T00:00:00',
    PartnerId: Number(fixture.partnerId),
    PartnerCode: 'OCR-SUP-001',
    PartnerName: 'Nha cung cap mau OCR',
    WarehouseId: Number(fixture.warehouseId),
    WarehouseCode: 'OCR-WH',
    WarehouseName: 'Kho nhan OCR',
    InventoryOwnershipMode: 'Internal',
    VehicleNumber: '51D-123.45',
    DriverName: 'Tran Minh Khoi',
    DriverPhone: '0909000111',
    Description: 'Chung tu nhap kho mau OCR'
  };
  const singlePayload = {
    data: JSON.stringify([line]),
    header,
    rawText: '{}',
    provider: 'Groq',
    logId: 123,
    warnings: [],
    confidence: 1,
    parseStatus: 'Success'
  };
  const batchPayload = {
    provider: 'Batch',
    parseStatus: 'Success',
    documents: [
      {
        DocumentKey: `${referenceNo}|2026-06-01|OCR-SUP-001`,
        ReferenceNo: referenceNo,
        VoucherDate: '2026-06-01',
        PartnerName: header.PartnerName,
        Header: header,
        Lines: [line],
        SourceFiles: ['mock-inbound.png'],
        DuplicateDocumentFiles: [],
        Warnings: [],
        Provider: 'Groq',
        ParseStatus: 'Success',
        Confidence: 1,
        SourceLogId: 123
      }
    ],
    warnings: [],
    duplicateFileCount: 0,
    duplicateDocumentCount: 0,
    requiresDocumentSelection: false,
    canAutoApply: true,
    readyLineCount: 1
  };

  await page.route('**/Vouchers/AnalyzeReceipt', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(singlePayload)
    });
  });
  await page.route('**/Vouchers/AnalyzeReceipts', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(batchPayload)
    });
  });
}

async function resolveHeaderConflictDialog(page: Page, action: 'ocr' | 'keep') {
  const dialog = page.locator('.swal2-popup').filter({ hasText: 'Header OCR khác dữ liệu hiện tại' }).first();
  try {
    await expect(dialog).toBeVisible({ timeout: 1200 });
  } catch {
    return;
  }

  await dialog.getByRole('button', { name: action === 'ocr' ? 'Dùng dữ liệu OCR' : 'Giữ dữ liệu hiện tại' }).click();
}

async function chooseVoucherItem(page: Page, row: Locator, itemCode: string, itemId: string) {
  const select2Selection = row.locator('select.item-select + .select2-container .select2-selection');
  if (await select2Selection.count()) {
    await select2Selection.click();
    await page.locator('.select2-search__field').fill(itemCode);
    const option = page
      .locator('.select2-dropdown.wms-item-select-dropdown .select2-results__option:not(.select2-results__message)')
      .filter({ hasText: itemCode })
      .first();
    await expect(option).toBeVisible();
    await option.click();
    return;
  }

  await row.locator('select.item-select').selectOption(itemId);
}

async function fillMinimalInboundVoucher(page: Page, testInfo: TestInfo) {
  const fixture = await getVoucherDocumentFixture(page, testInfo);
  if (!fixture) return null;
  await page.locator('select[name="PartnerId"]').selectOption(fixture.partnerId);

  const row = page.locator('#linesContainer .line-row').first();
  await chooseVoucherItem(page, row, fixture.item.itemCode, fixture.item.itemId);
  await expect(row.locator('select.item-select')).toHaveValue(fixture.item.itemId);
  await expect.poll(async () => row.locator('select.source-uom-select').evaluate(select => (select as HTMLSelectElement).disabled)).toBe(false);
  await row.locator('.qty-input').fill('1');
  const putawaySelect = row.locator('select.putaway-loc-select');
  if (await putawaySelect.count()) {
    let locValue = '';
    await expect.poll(async () => {
      locValue = await putawaySelect.evaluate(select => {
        const options = Array.from((select as HTMLSelectElement).options);
        const selected = options.find(option => option.selected && option.value && !option.disabled);
        const firstEnabled = options.find(option => option.value && !option.disabled);
        return (selected || firstEnabled)?.value || '';
      });
      return locValue;
    }, { message: 'inbound voucher should expose at least one enabled putaway location in the selected warehouse' }).not.toBe('');
    await putawaySelect.selectOption(locValue);
  }
  return fixture;
}

for (const route of routes) {
  test(`${route.name} renders without layout collision`, async ({ page }, testInfo) => {
    await gotoVisualAudited(page, route, testInfo.project.name);
    const zoom = zoomByProject[testInfo.project.name] ?? 1;
    await page.addStyleTag({ content: `html { zoom: ${zoom}; }` });
    await stabilizeRouteForScreenshot(page, route.name);
    await expect(page.locator('body')).toBeVisible();
    await assertNoTextOrMarkerRegression(page, route.name, testInfo.project.name);

    if (testInfo.project.name === 'mobile') {
      const overflow = await page.evaluate(() => Math.max(0, document.documentElement.scrollWidth - window.innerWidth));
      expect(overflow).toBeLessThanOrEqual(24);
      await expect(page.locator('.page-title, h1').first()).toBeVisible();
      const primaryAction = page.locator('.btn-primary:visible, .page-actions .btn:visible, .mobile-quick-link:visible').first();
      if (await primaryAction.count()) {
        await expect(primaryAction).toBeVisible();
      }
    }

    if (route.name === 'dock-board') {
      const dockDoors = page.locator('.dock-door');
      if (await dockDoors.count()) {
        await expect(page.locator('.dock-grid'), 'dock-board dock grid').toBeVisible();
        await expect(dockDoors.first(), 'dock-board first dock door').toBeVisible();
      } else {
        const emptyState = page.locator('.dock-board-empty-state');
        await expect(emptyState, 'dock-board empty state').toBeVisible();
        await expect(emptyState).toContainText('Chưa cấu hình cửa bến');
        await expect(emptyState.getByRole('link', { name: /Mở cấu hình kho/ })).toBeVisible();
      }
    }

    if (route.snapshot === false || testInfo.project.name === 'mobile') return;

    await expect(page).toHaveScreenshot(`${route.name}-${testInfo.project.name}.png`, {
      fullPage: true,
      mask: screenshotMasks(page, route.name)
    });
  });
}

test('warehouse overview stays cohesive at desktop laptop tablet and mobile widths', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'The responsive contract runs every requested width once.');

  const route = { name: 'warehouse-overview-responsive', path: '/Reports/WarehouseOverview' };
  const viewports = [
    { name: 'desktop-1440', width: 1440, height: 900, metricColumns: 4, panelColumns: 2 },
    { name: 'laptop-1366', width: 1366, height: 768, metricColumns: 4, panelColumns: 2 },
    { name: 'tablet-768', width: 768, height: 1024, metricColumns: 2, panelColumns: 1 },
    { name: 'mobile-390', width: 390, height: 844, metricColumns: 1, panelColumns: 1 }
  ];
  let baselineMetrics: string[] | null = null;

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await gotoVisualAudited(page, route, viewport.name);
    await stabilizeRouteForScreenshot(page, 'warehouse-overview');

    const report = page.locator('.warehouse-overview-report');
    await expect(report, `${viewport.name} report`).toBeVisible();
    await expect(report.locator('.warehouse-overview-filter'), `${viewport.name} filter`).toBeVisible();
    await expect(report.locator('.warehouse-overview-panel'), `${viewport.name} panels`).toHaveCount(4);
    await expect(report.locator('.warehouse-overview-table-scroll'), `${viewport.name} table regions`).toHaveCount(4);

    const layout = await report.evaluate((element) => {
      const columns = (selector: string) => {
        const target = element.querySelector<HTMLElement>(selector);
        if (!target) return 0;
        return getComputedStyle(target).gridTemplateColumns.split(' ').filter(Boolean).length;
      };
      const metricsRect = element.querySelector<HTMLElement>('.warehouse-overview-metrics')?.getBoundingClientRect();
      const primaryGridRect = element.querySelector<HTMLElement>('.warehouse-overview-grid-primary')?.getBoundingClientRect();
      return {
        globalOverflow: Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth),
        metricColumns: columns('.warehouse-overview-metrics'),
        primaryPanelColumns: columns('.warehouse-overview-grid-primary'),
        balancedPanelColumns: columns('.warehouse-overview-grid-balanced'),
        metricsBottom: metricsRect?.bottom ?? 0,
        primaryGridTop: primaryGridRect?.top ?? 0,
        clippedPanels: Array.from(element.querySelectorAll<HTMLElement>('.warehouse-overview-panel'))
          .filter((panel) => {
            const rect = panel.getBoundingClientRect();
            return rect.left < -1 || rect.right > window.innerWidth + 1 || rect.width <= 0;
          }).length
      };
    });

    expect(layout.globalOverflow, `${viewport.name} body overflow`).toBeLessThanOrEqual(1);
    expect(layout.metricColumns, `${viewport.name} metric columns`).toBe(viewport.metricColumns);
    expect(layout.primaryPanelColumns, `${viewport.name} primary panel columns`).toBe(viewport.panelColumns);
    expect(layout.balancedPanelColumns, `${viewport.name} balanced panel columns`).toBe(viewport.panelColumns);
    expect(layout.primaryGridTop - layout.metricsBottom, `${viewport.name} KPI-to-panel spacing`).toBeGreaterThanOrEqual(10);
    expect(layout.clippedPanels, `${viewport.name} clipped panels`).toBe(0);

    const tableRegions = report.locator('.warehouse-overview-table-scroll');
    const nestedScrollRegions = await tableRegions.evaluateAll((elements) => elements.map(element => ({
      scrollHeight: element.scrollHeight,
      clientHeight: element.clientHeight,
      overflowY: getComputedStyle(element).overflowY,
      overscrollY: getComputedStyle(element).overscrollBehaviorY
    })));
    for (const [index, nestedScroll] of nestedScrollRegions.entries()) {
      expect(nestedScroll.scrollHeight, `${viewport.name} table ${index + 1} nested vertical scroll range`)
        .toBeLessThanOrEqual(nestedScroll.clientHeight + 1);
      expect(nestedScroll.overflowY, `${viewport.name} table ${index + 1} vertical overflow`).not.toBe('auto');
      expect(nestedScroll.overscrollY, `${viewport.name} table ${index + 1} wheel chaining`).not.toBe('contain');
    }

    const firstTableRegion = tableRegions.first();
    await page.evaluate(() => window.scrollTo(0, 0));
    await firstTableRegion.hover();
    await page.mouse.wheel(0, 420);
    await expect.poll(() => page.evaluate(() => window.scrollY), {
      message: `${viewport.name} page must scroll while the pointer is over a report table`
    }).toBeGreaterThan(0);
    await page.evaluate(() => window.scrollTo(0, 0));

    const metricValues = await report.locator('.warehouse-overview-metric-card .metric-value').allTextContents();
    if (baselineMetrics === null) baselineMetrics = metricValues;
    else expect(metricValues, `${viewport.name} must preserve the same KPI output`).toEqual(baselineMetrics);

    await assertNoTextOrMarkerRegression(page, route.name, viewport.name);
    await testInfo.attach(`warehouse-overview-${viewport.name}`, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png'
    });
  }
});

test('serial-tracked inbound readiness is actionable and responsive', async ({ page }, testInfo) => {
  if (testInfo.project.name !== 'desktop-100') {
    testInfo.annotations.push({
      type: 'viewport-covered',
      description: 'The primary desktop project executes the explicit desktop, laptop, tablet and mobile matrix.'
    });
    return;
  }

  const voucherId = process.env.WMS_SERIAL_RECEIVING_VOUCHER_ID;
  if (!voucherId) {
    markMissingFixture(testInfo, 'serial readiness requires WMS_SERIAL_RECEIVING_VOUCHER_ID with missing serials');
    return;
  }

  const viewports = [
    { name: 'desktop-1440', width: 1440, height: 900 },
    { name: 'laptop-1366', width: 1366, height: 768 },
    { name: 'tablet-768', width: 768, height: 1024 },
    { name: 'mobile-390', width: 390, height: 844 }
  ];

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const route = {
      name: `serial-readiness-${viewport.name}`,
      path: `/Vouchers/Details/${encodeURIComponent(voucherId)}`
    };
    await gotoVisualAudited(page, route, viewport.name);

    const readiness = page.locator('.readiness-panel').filter({
      has: page.locator('a[href*="/Operations/SerialReceiving"]')
    }).first();
    await expect(readiness, `${viewport.name} serial readiness panel`).toBeVisible();
    await expect(readiness.locator('a[href*="/Operations/SerialReceiving"]'), `${viewport.name} serial action`).toBeVisible();
    await expect(page.locator('#approveBtn'), `${viewport.name} inventory post guard`).toBeDisabled();

    const overflow = await page.evaluate(() =>
      Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth));
    expect(overflow, `${viewport.name} body overflow`).toBeLessThanOrEqual(1);

    await testInfo.attach(`serial-readiness-${viewport.name}`, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png'
    });
  }
});

test('quality inspection item options always identify the selected goods line', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One desktop pass covers the QC item-option regression.');

  await page.goto('/Operations/QualityInspection', { waitUntil: 'networkidle' });
  const openButton = page.locator('[data-wms-call-self="openQcFromButton"]').first();
  await expect(openButton, 'QC regression requires a receiving voucher with an unchecked line').toHaveCount(1);

  await openButton.click();
  const options = await page.locator('#qcItem option:not([value=""])').allTextContents();
  expect(options.length, 'QC dialog should expose at least one goods line').toBeGreaterThan(0);
  for (const option of options) {
    const normalized = option.trim();
    expect(normalized, 'QC goods line must include an item identifier').not.toMatch(/^[-–—]\s*\(SL:/i);
    expect(normalized, 'QC goods line must not be blank before quantity').not.toMatch(/^\s*\(SL:/i);
  }
});

test('RBAC and navigation impact routes remain functionally sound', async ({ page }, testInfo) => {
  const impactRoutes = [
    { name: 'home-rbac-impact', path: '/' },
    { name: 'trusted-devices-rbac-impact', path: '/Account/TrustedDevices' },
    { name: 'optimization-rbac-impact', path: '/Operations/OptimizationDashboard' },
    { name: 'automation-rbac-impact', path: '/Operations/AutomationDashboard' },
    { name: 'integration-rbac-impact', path: '/Operations/IntegrationDashboard' },
    { name: 'stock-snapshot-rbac-impact', path: '/Reports/StockSnapshot' },
    { name: 'period-locks-rbac-impact', path: '/Reports/PeriodLocks' }
  ];

  for (const route of impactRoutes) {
    await gotoVisualAudited(page, route, testInfo.project.name);
    await assertNoTextOrMarkerRegression(page, route.name, testInfo.project.name);
    await expect(page.locator('main, .app-main, .main-content').first(), `${route.name} main content`).toBeVisible();
  }

  await page.goto('/', { waitUntil: 'networkidle' });
  await expect(page.locator('a.topbar-icon-btn[href*="/Reports/Alerts"]'), 'admin alert shortcut').toHaveCount(1);
  await expect(page.locator('#sidebar a', { hasText: 'Chốt tồn' }), 'admin stock snapshot navigation').toHaveCount(1);
  await expect(page.locator('#sidebar a', { hasText: 'Khóa kỳ' }), 'admin period lock navigation').toHaveCount(1);
});

test('demo data page exposes three safe internal warehouse domains', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers demo data controls without mutating DB.');

  await gotoVisualAudited(page, { name: 'demo-data', path: '/System/DemoData' }, testInfo.project.name);
  await expect(page.getByRole('heading', { name: 'Demo dữ liệu' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Demo kho thiết bị IT/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /Demo kho vật tư y tế/ })).toBeVisible();
  await expect(page.getByRole('button', { name: /Demo kho thương mại điện tử/ })).toBeVisible();

  const bodyText = await page.locator('body').innerText();
  expect(bodyText).not.toContain('thuê kho');
  expect(bodyText).not.toContain('undefined');
  expect(bodyText).not.toContain('null');
  expect(bodyText).not.toContain('NaN');
  expect(await page.locator('form.demo-data-form').count()).toBe(3);
  await assertNoTextOrMarkerRegression(page, 'demo-data', testInfo.project.name);
});

test('demo data submit uses enterprise confirm and posts selected domain without mutating DB', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers demo data submit wiring without mutating DB.');

  let postedBody = '';
  await page.route('**/System/ApplyDemoData', async route => {
    postedBody = route.request().postData() ?? '';
    await route.fulfill({
      status: 200,
      contentType: 'text/html; charset=utf-8',
      body: '<!doctype html><html lang="vi"><body><main>Mocked demo data apply</main></body></html>'
    });
  });

  await gotoVisualAudited(page, { name: 'demo-data', path: '/System/DemoData' }, testInfo.project.name);
  await page.evaluate(() => {
    window.confirm = () => {
      throw new Error('Native confirm should not be used by demo data submit.');
    };
    window.enterpriseConfirm = async () => true;
  });

  await page.getByRole('button', { name: /Demo kho thiết bị IT/ }).click();
  await expect.poll(() => postedBody, { message: 'demo data form should post after enterprise confirmation' }).toContain('domain=it');
  await expect.poll(() => postedBody).toContain('confirmApply=APPLY_DEMO_DATA');
});

test('demo data submit disables all domain buttons while request is pending', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers demo data pending-submit wiring without mutating DB.');

  let requestSeen = false;
  let releaseRoute: (() => void) | undefined;
  await page.route('**/System/ApplyDemoData', async route => {
    requestSeen = true;
    await new Promise<void>(resolve => {
      releaseRoute = resolve;
    });
    await route.fulfill({
      status: 200,
      contentType: 'text/html; charset=utf-8',
      body: '<!doctype html><html lang="vi"><body><main>Mocked demo data apply</main></body></html>'
    });
  });

  await gotoVisualAudited(page, { name: 'demo-data', path: '/System/DemoData' }, testInfo.project.name);
  await page.evaluate(() => {
    window.enterpriseConfirm = async () => true;
  });

  const demoButtons = page.locator('form.demo-data-form button[type="submit"]');
  await page.getByRole('button', { name: /Demo kho thiết bị IT/ }).click({ noWaitAfter: true });
  await expect.poll(async () => demoButtons.evaluateAll(buttons => buttons.every(button => (button as HTMLButtonElement).disabled))).toBe(true);
  await expect(page.locator('form.demo-data-form button[type="submit"] .fa-spinner')).toHaveCount(1);
  await expect.poll(() => requestSeen, { message: 'demo data request should be pending after buttons are disabled' }).toBeTruthy();

  releaseRoute?.();
  await page.waitForLoadState('domcontentloaded');
});

test('demo data cancel keeps selected domain button idle and does not post', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers demo data cancel wiring without mutating DB.');

  let postCount = 0;
  await page.route('**/System/ApplyDemoData', async route => {
    postCount += 1;
    await route.fulfill({
      status: 409,
      contentType: 'text/plain; charset=utf-8',
      body: 'Unexpected demo data apply request during cancel.'
    });
  });

  await gotoVisualAudited(page, { name: 'demo-data', path: '/System/DemoData' }, testInfo.project.name);
  await page.evaluate(() => {
    window.confirm = () => {
      throw new Error('Native confirm should not be used by demo data cancel.');
    };
    window.enterpriseConfirm = async () => false;
  });

  const button = page.getByRole('button', { name: /Demo kho thiết bị IT/ });
  await button.click();
  await page.waitForTimeout(900);

  await expect.poll(() => postCount, { message: 'cancelled demo data action must not post' }).toBe(0);
  await expect(button).toBeEnabled();
  await expect(button).toContainText('Demo kho thiết bị IT');
  await expect(button.locator('.fa-spinner')).toHaveCount(0);
});

test('users action column is visible without horizontal scroll on desktop', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One deterministic desktop pass covers the user action visibility regression.');

  for (const width of [1366, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/Users', { waitUntil: 'networkidle' });
    await expect(page.locator('[data-user-action="reset-password"]').first()).toBeVisible();
    await expect(page.locator('[data-user-action="lock-account"]').first()).toBeVisible();

    const result = await page.evaluate(() => {
      const wrap = document.querySelector<HTMLElement>('.identity-table-wrap');
      const reset = document.querySelector<HTMLElement>('[data-user-action="reset-password"]');
      const lock = document.querySelector<HTMLElement>('[data-user-action="lock-account"]');
      if (!wrap || !reset || !lock) return null;
      const wrapBox = wrap.getBoundingClientRect();
      const resetBox = reset.getBoundingClientRect();
      const lockBox = lock.getBoundingClientRect();
      return {
        scrollLeft: wrap.scrollLeft,
        wrapRight: Math.round(wrapBox.right),
        resetRight: Math.round(resetBox.right),
        lockRight: Math.round(lockBox.right),
        resetLeft: Math.round(resetBox.left),
        lockLeft: Math.round(lockBox.left)
      };
    });

    expect(result, `users action visibility result at ${width}px`).not.toBeNull();
    expect(result?.scrollLeft ?? 1, `users table should not be manually scrolled at ${width}px`).toBe(0);
    expect(result?.resetRight ?? 99999, `reset action right edge at ${width}px`).toBeLessThanOrEqual((result?.wrapRight ?? 0) + 2);
    expect(result?.lockRight ?? 99999, `lock action right edge at ${width}px`).toBeLessThanOrEqual((result?.wrapRight ?? 0) + 2);
    expect(result?.resetLeft ?? -1, `reset action visible left edge at ${width}px`).toBeGreaterThanOrEqual(0);
    expect(result?.lockLeft ?? -1, `lock action visible left edge at ${width}px`).toBeGreaterThanOrEqual(0);
  }
});

test('users create account modal scrolls to footer on short viewport', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One deterministic desktop pass covers the create-user modal scroll regression.');

  await page.setViewportSize({ width: 1366, height: 720 });
  await page.goto('/Users', { waitUntil: 'networkidle' });
  await page.locator('[data-wms-call="openUserModal"]').first().click();

  const overlay = page.locator('#modalUser');
  const body = page.locator('#modalUser .modal-body');
  await expect(overlay).toHaveClass(/active/);
  await expect(body).toBeVisible();

  const initial = await body.evaluate((element) => ({
    scrollHeight: element.scrollHeight,
    clientHeight: element.clientHeight,
    scrollTop: element.scrollTop
  }));
  expect(initial.scrollHeight, 'create-user modal body should have scrollable content on short viewport').toBeGreaterThan(initial.clientHeight + 20);

  await body.evaluate((element) => {
    element.scrollTop = element.scrollHeight;
    element.dispatchEvent(new Event('scroll', { bubbles: true }));
  });

  const result = await page.evaluate(() => {
    const modal = document.querySelector<HTMLElement>('#modalUser .identity-modal');
    const modalBody = document.querySelector<HTMLElement>('#modalUser .modal-body');
    const footer = document.querySelector<HTMLElement>('#modalUser .modal-footer');
    const submit = document.querySelector<HTMLElement>('#modalUser button[type="submit"]');
    if (!modal || !modalBody || !footer || !submit) return null;

    const modalBox = modal.getBoundingClientRect();
    const bodyBox = modalBody.getBoundingClientRect();
    const footerBox = footer.getBoundingClientRect();
    const submitBox = submit.getBoundingClientRect();
    return {
      bodyScrollable: modalBody.scrollHeight > modalBody.clientHeight,
      bodyAtBottom: modalBody.scrollTop + modalBody.clientHeight >= modalBody.scrollHeight - 2,
      modalTop: Math.round(modalBox.top),
      modalBottom: Math.round(modalBox.bottom),
      bodyBottom: Math.round(bodyBox.bottom),
      footerTop: Math.round(footerBox.top),
      footerBottom: Math.round(footerBox.bottom),
      submitBottom: Math.round(submitBox.bottom),
      viewportHeight: window.innerHeight
    };
  });

  expect(result, 'create-user modal geometry').not.toBeNull();
  expect(result?.bodyScrollable).toBe(true);
  expect(result?.bodyAtBottom).toBe(true);
  expect(result?.modalTop ?? -1).toBeGreaterThanOrEqual(0);
  expect(result?.modalBottom ?? 99999).toBeLessThanOrEqual((result?.viewportHeight ?? 0) + 1);
  expect(result?.bodyBottom ?? -1).toBeLessThanOrEqual((result?.footerTop ?? 0) + 1);
  expect(result?.footerBottom ?? 99999).toBeLessThanOrEqual((result?.viewportHeight ?? 0) + 1);
  expect(result?.submitBottom ?? 99999).toBeLessThanOrEqual((result?.viewportHeight ?? 0) + 1);
});

test('collapsed sidebar keeps enterprise rail groups and flyouts', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'Mobile uses drawer navigation instead of desktop mini rail.');

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.evaluate(() => localStorage.removeItem('wms_sidebar_collapsed'));
  await page.reload({ waitUntil: 'networkidle' });
  const zoom = zoomByProject[testInfo.project.name] ?? 1;
  await page.addStyleTag({ content: `html { zoom: ${zoom}; }` });
  const body = page.locator('body');
  if (!(await body.evaluate((element) => element.classList.contains('sidebar-collapsed')))) {
    await page.locator('#sidebarToggle').click();
  }
  await expect(body).toHaveClass(/sidebar-collapsed/);
  await page.addStyleTag({ content: `.offline-queue-widget.is-empty { display: none !important; }` });
  await stabilizeRouteForScreenshot(page, 'home');

  const railGeometry = await page.locator('.sidebar .sidebar-brand, .sidebar .nav-section[data-nav-label]')
    .evaluateAll(sections => sections.map(section => {
      const target = section.querySelector<HTMLElement>('.nav-section-title, .nav-link') ?? section as HTMLElement;
      const icon = section.matches('.sidebar-brand')
        ? section.querySelector<HTMLElement>('.brand-icon')
        : target.querySelector<HTMLElement>('.nav-section-icon, .nav-icon');
      const rect = target.getBoundingClientRect();
      const iconRect = icon?.getBoundingClientRect();
      return {
        label: section.getAttribute('data-nav-label') || 'brand',
        centerX: Math.round(rect.left + rect.width / 2),
        iconCenterX: iconRect ? Math.round(iconRect.left + iconRect.width / 2) : null,
        width: Math.round(rect.width),
        height: Math.round(rect.height)
      };
    }));
  expect(railGeometry.length, 'collapsed sidebar should expose all primary rail groups').toBeGreaterThanOrEqual(8);
  const centerXs = railGeometry.map(item => item.centerX);
  expect(Math.max(...centerXs) - Math.min(...centerXs), 'collapsed sidebar icons should align on one vertical rail').toBeLessThanOrEqual(2);
  const iconCenterXs = railGeometry.map(item => item.iconCenterX).filter((value): value is number => value !== null);
  expect(iconCenterXs.length, 'collapsed sidebar should expose measurable icons').toBeGreaterThanOrEqual(8);
  expect(Math.max(...iconCenterXs) - Math.min(...iconCenterXs), 'collapsed sidebar actual icon centers should align on one vertical rail').toBeLessThanOrEqual(2);
  for (const item of railGeometry) {
    expect(item.width, 'collapsed sidebar icon target width').toBeGreaterThanOrEqual(40);
    expect(item.height, 'collapsed sidebar icon target height').toBeGreaterThanOrEqual(40);
  }

  for (const label of ['Trang chính', 'Nhập kho', 'Xuất kho', 'Tồn kho', 'Vận chuyển', 'Báo cáo', 'Danh mục', 'Hệ thống', 'Hướng dẫn sử dụng']) {
    await expect(page.locator(`.sidebar .nav-section[data-nav-label="${label}"]`).first()).toBeVisible();
  }

  for (const label of ['Nhập kho', 'Xuất kho', 'Tồn kho', 'Vận chuyển', 'Hệ thống']) {
    const group = page.locator(`.sidebar .nav-section[data-nav-label="${label}"]`).first();
    await group.locator('.nav-section-title').focus();
    await expect(group).toHaveClass(/flyout-open/);
    const flyout = group.locator('.nav-section-body');
    await expect(flyout).toBeVisible();
    const flyoutBox = await flyout.boundingBox();
    const viewport = page.viewportSize();
    expect(flyoutBox?.y ?? 0, `collapsed flyout for ${label} should not overlap topbar`).toBeGreaterThanOrEqual(56);
    expect((flyoutBox?.y ?? 0) + (flyoutBox?.height ?? 0), `collapsed flyout for ${label} should fit viewport`).toBeLessThanOrEqual((viewport?.height ?? 900) - 4);
    await page.keyboard.press('Escape');
    await expect(group).not.toHaveClass(/flyout-open/);
  }

  await expect(page.locator('.sidebar')).toBeVisible();
});

test('outbound voucher exposes FEFO source lot and location selection surface', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the outbound FEFO selection surface.');

  await page.goto('/Vouchers/Create?type=XuatKho', { waitUntil: 'networkidle' });
  const row = page.locator('#linesContainer .line-row').first();
  await expect(row.locator('.source-location-btn')).toBeVisible();
  await expect(row.locator('.source-lot-input')).toHaveCount(1);
  await expect(row.locator('.source-expiry-input')).toHaveCount(1);
  await expect(row.locator('.fefo-override-reason-input')).toHaveCount(1);
  await expect(row.locator('.source-fefo-note')).toHaveCount(1);

  const itemSelect = row.locator('select.item-select');
  const firstItemOption = itemSelect.locator('option:not([value=""]):not([disabled])').first();
  const firstItemValue = await firstItemOption.count() ? await firstItemOption.getAttribute('value') : null;
  if (!firstItemValue) {
    markMissingFixture(testInfo, 'outbound FEFO smoke requires at least one active item');
    return;
  }
  expect(firstItemValue, 'outbound form should expose at least one item for source-location smoke test').toBeTruthy();
  await itemSelect.selectOption(firstItemValue);
  await row.locator('.qty-input').fill('1');
  await row.locator('.source-location-btn').click();

  const modal = page.locator('#locationSuggestionModal.is-open .loc-suggest-modal').first();
  await expect(modal).toBeVisible();
  await expect(modal.locator('.loc-suggest-loading')).toHaveCount(0);
  await expect(modal).toContainText(/Tồn khả dụng|Vật tư này chưa có trong ô\/vị trí nào|Lô\/HSD\/FEFO/);
  const modalBox = await modal.boundingBox();
  const viewport = page.viewportSize();
  expect(modalBox?.height ?? 0, 'FEFO source-location modal should stay within viewport').toBeLessThanOrEqual((viewport?.height ?? 900) * 0.92);
  await expect(page.locator('#locationSuggestionModal .loc-suggest-close')).toBeVisible();
});

test('mobile scanner modal fits viewport', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'mobile', 'Scanner fit is a mobile-only visual gate.');

  await page.goto('/Operations/RfReceiving', { waitUntil: 'networkidle' });
  await page.evaluate(() => {
    const modal = document.getElementById('scannerModal');
    if (modal) {
      modal.classList.add('active');
      modal.setAttribute('aria-hidden', 'false');
    }
  });

  const modal = page.locator('#scannerModal .scanner-modal').first();
  await expect(modal).toBeVisible();
  const box = await modal.boundingBox();
  const viewport = page.viewportSize();
  expect(box?.width ?? 0).toBeLessThanOrEqual(viewport?.width ?? 390);
  expect(box?.height ?? 0).toBeLessThanOrEqual(viewport?.height ?? 844);
});

test('voucher create keeps source unit available after item selection', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass is enough for the UOM regression gate.');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const firstRow = page.locator('#linesContainer .line-row').first();
  const ownerSelect = page.locator('select[name="OwnerPartnerId"]').first();
  await expect(page.locator('#ownerPartnerFieldDiv')).toBeHidden();
  expect(await page.locator('.main-content').innerText()).not.toMatch(/Internal \/ unowned|unowned|Chủ hàng kho dịch vụ|Nội bộ \/ chưa gán chủ hàng/);
  const ownershipToggle = page.locator('[data-ownership-toggle]');
  if (await ownershipToggle.count()) {
    await expect(page.getByText('Loại sở hữu hàng')).toBeVisible();
    await expect(ownershipToggle.getByText('Nội bộ', { exact: true })).toBeVisible();
    await expect(page.locator('input[name="InventoryOwnershipMode"][value="Internal"]')).toBeChecked();
  } else {
    await expect(page.locator('input[type="hidden"][name="InventoryOwnershipMode"][value="Internal"]')).toHaveCount(1);
  }
  if (await ownerSelect.count()) {
    await expect(ownerSelect).toBeDisabled();
    await expect(ownershipToggle.getByText('Khách hàng thuê kho', { exact: true })).toBeVisible();
    await page.locator('input[name="InventoryOwnershipMode"][value="ThreePl"]').check({ force: true });
    await expect(page.locator('#ownerPartnerFieldDiv')).toBeVisible();
    await expect(ownerSelect).toBeEnabled();
    await expect(page.locator('#ownerPartnerFieldDiv')).toContainText('Chủ hàng');
    const ownerOptions = (await ownerSelect.locator('option').allTextContents()).join('\n');
    expect(ownerOptions).toContain('Chọn chủ hàng');
    expect(ownerOptions).not.toContain('Chủ hàng 3PL ACME');
    expect(ownerOptions).not.toContain('Internal / unowned');
    await page.locator('input[name="InventoryOwnershipMode"][value="Internal"]').check({ force: true });
    await expect(ownerSelect).toBeDisabled();
  } else {
    await expect(ownershipToggle.getByText('Khách hàng thuê kho', { exact: true })).toHaveCount(0);
    await expect(page.locator('input[name="InventoryOwnershipMode"][value="ThreePl"]')).toHaveCount(0);
  }

  const itemValues = await firstRow.locator('select.item-select option:not([value=""]):not([disabled])')
    .evaluateAll(options => options.map(option => {
      const opt = option as HTMLOptionElement;
      return {
        value: opt.value,
        baseUom: opt.dataset.uom || '',
        text: (opt.textContent || '').trim()
      };
    }));
  if (itemValues.length <= 3) {
    markMissingFixture(testInfo, 'item dropdown regression requires at least four active items');
    return;
  }
  expect(itemValues.length, 'voucher create should expose more than the first three active items').toBeGreaterThan(3);

  await firstRow.locator('.select2-container').first().click();
  const itemDropdown = page.locator('.select2-dropdown.wms-item-select-dropdown').first();
  await expect(itemDropdown).toBeVisible();
  await expect.poll(async () => itemDropdown.locator('.select2-results__option:not(.select2-results__message)').count())
    .toBeGreaterThan(3);
  const dropdownBox = await itemDropdown.boundingBox();
  expect(dropdownBox?.height ?? 0, 'item dropdown should be tall enough to avoid looking capped at three items').toBeGreaterThan(180);
  await page.keyboard.press('Escape');

  const chooseItemWithSelect2 = async (row: Locator, item: { value: string; baseUom: string; text: string }) => {
    const code = item.text.match(/\[([^\]]+)\]/)?.[1] || item.text;
    await row.locator('select.item-select + .select2-container .select2-selection').click();
    await page.locator('.select2-search__field').fill(code);
    const option = page
      .locator('.select2-dropdown.wms-item-select-dropdown .select2-results__option:not(.select2-results__message)')
      .filter({ hasText: code })
      .first();
    await expect(option).toBeVisible();
    await option.click();
  };

  const sampleIndexes = Array.from(new Set([0, 1, 2, itemValues.length - 1])).filter(index => index >= 0);
  for (let index = 0; index < sampleIndexes.length; index++) {
    if (index > 0) {
      await page.evaluate(() => (window as any).addRow?.());
    }

    const row = page.locator('#linesContainer .line-row').nth(index);
    const item = itemValues[sampleIndexes[index]];
    await chooseItemWithSelect2(row, item);

    const sourceUom = row.locator('select.source-uom-select');
    await expect.poll(async () => sourceUom.evaluate(select => (select as HTMLSelectElement).disabled), {
      message: `source UOM should be enabled after selecting ${item.text}`
    }).toBe(false);
    await expect.poll(async () => sourceUom.locator('option:not([value=""])').count()).toBeGreaterThan(0);
    if (item.baseUom) {
      await expect.poll(async () => sourceUom.locator(`option[value="${item.baseUom}"]`).count()).toBe(1);
      await expect.poll(async () => sourceUom.inputValue()).toBe(item.baseUom);
    } else {
      await expect.poll(async () => sourceUom.inputValue()).not.toBe('');
    }

    await row.locator('.qty-input').fill('1');
  }

  await page.getByRole('button', { name: /Gợi ý vị trí cất hàng/ }).click();
  const putawayDialog = page.locator('.swal2-popup').first();
  await expect(putawayDialog).toBeVisible();
  await expect(putawayDialog).not.toContainText(/Fixed Bin|fixed bin/);
  const putawayDialogText = await putawayDialog.innerText();
  expect(putawayDialogText).not.toMatch(/phân bổ\s+0\/\d+/i);
});

test('voucher create auto-selects only a server-approved putaway location', async ({ page }, testInfo) => {
  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const firstRow = page.locator('#linesContainer .line-row').first();
  const itemSelect = firstRow.locator('select.item-select');
  const putawaySelect = firstRow.locator('select.putaway-loc-select');
  const itemId = await itemSelect.locator('option:not([value=""]):not([disabled])').first().getAttribute('value');
  const locationIds = await putawaySelect.locator('option:not([value=""]):not([disabled])')
    .evaluateAll(options => options.map(option => (option as HTMLOptionElement).value));

  if (!itemId || locationIds.length < 2) {
    markMissingFixture(testInfo, 'putaway regression requires one item and two active locations in the selected warehouse');
    return;
  }

  const unsafeDefaultLocationId = locationIds[0];
  const safeSuggestedLocationId = locationIds[1];
  await itemSelect.locator(`option[value="${itemId}"]`).evaluate((option, defaultLocationId) => {
    (option as HTMLOptionElement).dataset.defaultLoc = defaultLocationId;
  }, unsafeDefaultLocationId);

  let suggestionRequestCount = 0;
  await page.route('**/Warehouses/GetSuggestedLocations**', async route => {
    suggestionRequestCount++;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{
        locationId: Number(safeSuggestedLocationId),
        locationCode: 'AUDIT_TEST_SAFE_PUTAWAY',
        hasOtherItem: false,
        hasSameItem: false,
        available: 1000
      }])
    });
  });

  await itemSelect.selectOption(itemId);

  await expect.poll(() => putawaySelect.inputValue(), {
    message: 'automatic putaway must use the location approved by the suggestion API'
  }).toBe(safeSuggestedLocationId);
  expect(await putawaySelect.inputValue()).not.toBe(unsafeDefaultLocationId);
  expect(suggestionRequestCount).toBe(1);
});

test('voucher create validation does not leave submit button loading', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass is enough for the submit validation regression gate.');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const partnerSelect = page.locator('select[name="PartnerId"]').first();
  const submitButton = page.locator('#submitBtn').first();

  await partnerSelect.selectOption('');
  await submitButton.click();

  const warningDialog = page.locator('.swal2-popup').first();
  await expect(warningDialog).toBeVisible();
  await expect(warningDialog).toContainText(/Vui lòng chọn nhà cung cấp|Thiếu thông tin/);
  await page.waitForTimeout(900);
  await expect(submitButton).toBeEnabled();
  await expect(submitButton).toContainText('Lưu và gửi duyệt');
  await expect(submitButton.locator('.fa-spinner')).toHaveCount(0);
});

test('voucher details dock modal decodes Vietnamese transport fields', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the dock modal seed encoding regression.');
  const finishAudit = attachRuntimeAudit(page, 'voucher details dock modal encoding');

  await page.goto('/Vouchers', { waitUntil: 'networkidle' });
  const detailHrefs = await page.locator('a[href*="/Vouchers/Details/"]').evaluateAll(links =>
    Array.from(new Set(links.map(link => (link as HTMLAnchorElement).href).filter(Boolean)))
  );
  if (detailHrefs.length === 0) {
    markMissingFixture(testInfo, 'dock modal decoding requires at least one voucher detail row');
    finishAudit();
    return;
  }
  expect(detailHrefs.length, 'voucher list should expose at least one detail link for dock modal regression').toBeGreaterThan(0);

  let inspectedTransportSeed = false;
  for (const href of detailHrefs.slice(0, 30)) {
    await page.goto(href, { waitUntil: 'networkidle' });
    const assignDockButton = page.locator('#assignDockBtn');
    if (!(await assignDockButton.isVisible().catch(() => false))) continue;

    await assignDockButton.click();
    const modal = page.locator('#dockModal');
    await expect(modal).toBeVisible();
    await expect(modal).toContainText('Đơn vị vận chuyển');
    await expect(modal).toContainText('Tài xế');

    const values = await modal.locator('input').evaluateAll(inputs => inputs.map(input => ({
      id: input.id,
      value: (input as HTMLInputElement).value
    })));
    for (const field of values) {
      expect(field.value, `${field.id} should not render raw HTML entity`).not.toMatch(/&(?:amp;)?#x[0-9a-f]+;/i);
      expect(field.value, `${field.id} should not render escaped named entity`).not.toMatch(/&(aacute|agrave|acirc|atilde|eacute|iacute|oacute|uacute|quot|amp);/i);
    }

    const carrierValue = values.find(field => field.id === 'dock-carrierName')?.value ?? '';
    const driverValue = values.find(field => field.id === 'dock-driverName')?.value ?? '';
    if (carrierValue.trim() || driverValue.trim()) {
      inspectedTransportSeed = true;
      break;
    }

    await page.locator('#dockCancelBtn').click();
  }

  if (!inspectedTransportSeed) {
    markMissingFixture(testInfo, 'dock modal decoding requires an editable inbound voucher with transport data');
    finishAudit();
    return;
  }
  expect(inspectedTransportSeed, 'at least one editable inbound voucher should have transport/driver seed data to verify decoding').toBe(true);
  finishAudit();
});

test('voucher OCR applies header and matched lines from document result', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the OCR header-line regression.');
  const finishAudit = attachRuntimeAudit(page, 'voucher OCR header-line');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const fixture = await getVoucherDocumentFixture(page, testInfo);
  if (!fixture) {
    finishAudit();
    return;
  }
  await mockAnalyzeReceipt(page, fixture, 'AI-IN-20260601-001');

  await page.locator('#documentFileInput').setInputFiles({
    name: 'mock-inbound.png',
    mimeType: 'image/png',
    buffer: Buffer.from('mock image')
  });

  const preview = page.locator('.swal2-popup').first();
  await expect(preview).toBeVisible();
  await expect(preview).toContainText('Số chứng từ: AI-IN-20260601-001');
  await expect(preview).toContainText('Nhà cung cấp');
  await expect(preview).toContainText(fixture.item.itemCode);
  await page.getByRole('button', { name: /Áp dụng/ }).click();
  await resolveHeaderConflictDialog(page, 'ocr');

  await expect(page.locator('input[name="ReferenceNo"]')).toHaveValue('AI-IN-20260601-001');
  await expect(page.locator('input[name="VoucherDate"]')).toHaveValue('2026-06-01');
  await expect(page.locator('select[name="PartnerId"]')).toHaveValue(fixture.partnerId);
  await expect(page.locator('input[name="VehicleNumber"]')).toHaveValue('51D-123.45');
  await expect(page.locator('input[name="DriverName"]')).toHaveValue('Tran Minh Khoi');
  await expect(page.locator('input[name="Description"]')).toHaveValue('Chung tu nhap kho mau OCR');

  const row = page.locator('#linesContainer .line-row').first();
  await expect(row.locator('select.item-select')).toHaveValue(fixture.item.itemId);
  await expect(row.locator('input[name$=".TransactionQty"]')).toHaveValue(/25/);
  await expect(row.locator('input[name$=".LotNumber"]')).toHaveValue(new RegExp(`${fixture.item.itemCode.replace(/[^A-Z0-9]+/gi, '-').slice(0, 18)}-260601`));
  await expect(row.locator('.mfg-date-input')).toHaveValue('2026-06-01');
  if (fixture.item.trackExpiry) {
    await expect(row.locator('.exp-date-input')).toHaveValue('2028-12-31');
    await expect(row.locator('.exp-date-input')).toBeEnabled();
    await expect(row.locator('.expiry-policy-note')).toContainText('Bắt buộc HSD');
  } else {
    await expect(row.locator('.exp-date-input')).toHaveValue('');
    await expect(row.locator('.exp-date-input')).toBeDisabled();
    await expect(row.locator('.expiry-policy-note')).toContainText('Không áp dụng');
  }
  await expect(row.locator('select.source-uom-select')).not.toBeDisabled();

  finishAudit();
});

test('voucher OCR does not overwrite manually entered header fields', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the OCR no-overwrite regression.');
  const finishAudit = attachRuntimeAudit(page, 'voucher OCR no-overwrite');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const fixture = await getVoucherDocumentFixture(page, testInfo);
  if (!fixture) {
    finishAudit();
    return;
  }
  await mockAnalyzeReceipt(page, fixture, 'AI-IN-20260601-001');

  await page.locator('input[name="ReferenceNo"]').fill('MANUAL-REF-001');
  await page.locator('input[name="Description"]').fill('Ghi chú nhập tay phải được giữ nguyên');
  await page.locator('select[name="PartnerId"]').selectOption(fixture.partnerId);

  await page.locator('#documentFileInput').setInputFiles({
    name: 'mock-inbound.png',
    mimeType: 'image/png',
    buffer: Buffer.from('mock image')
  });
  await expect(page.locator('.swal2-popup').first()).toBeVisible();
  await page.getByRole('button', { name: /Áp dụng/ }).click();

  await resolveHeaderConflictDialog(page, 'keep');

  await expect(page.locator('input[name="ReferenceNo"]')).toHaveValue('MANUAL-REF-001');
  await expect(page.locator('input[name="Description"]')).toHaveValue('Ghi chú nhập tay phải được giữ nguyên');
  await expect(page.locator('select[name="PartnerId"]')).toHaveValue(fixture.partnerId);
  await expect(page.locator('input[name="VehicleNumber"]')).toHaveValue('51D-123.45');
  await expect(page.locator('input[name="DriverName"]')).toHaveValue('Tran Minh Khoi');
  await expect(page.locator('#AiOcrLogId')).toHaveValue('123');
  await expect(page.locator('#linesContainer .line-row').first().locator('input[name$=".OcrSourceLineNumber"]')).toHaveValue('1');
  await expect(page.locator('#submitBtn')).toBeEnabled();
  await expect(page.locator('#submitBtn').locator('.fa-spinner')).toHaveCount(0);

  finishAudit();
});


test('voucher OCR multi-document preview requires one document and does not double quantities', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the OCR multi-document regression.');
  const finishAudit = attachRuntimeAudit(page, 'voucher OCR multi-document duplicate guard');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const fixture = await getVoucherDocumentFixture(page, testInfo);
  if (!fixture) {
    finishAudit();
    return;
  }
  const makeHeader = (referenceNo: string, supplier: string, description: string) => ({
    ReferenceNo: referenceNo,
    VoucherDate: '2026-06-09T00:00:00',
    PartnerId: Number(fixture.partnerId),
    PartnerCode: supplier,
    PartnerName: supplier,
    WarehouseId: Number(fixture.warehouseId),
    WarehouseCode: 'OCR-WH',
    WarehouseName: 'Kho nhan OCR',
    InventoryOwnershipMode: 'Internal',
    VehicleNumber: referenceNo.endsWith('071') ? '51D-771.62' : '51C-668.19',
    DriverName: referenceNo.endsWith('071') ? 'Mai Phuong Anh' : 'Truong Hai Long',
    DriverPhone: '0932218609',
    Description: description
  });
  const makeLine = (referenceNo: string, quantity: number, lot: string, lineNumber: number) => ({
    ItemId: Number(fixture.item.itemId),
    ItemCode: fixture.item.itemCode,
    ItemName: fixture.item.itemName,
    Quantity: quantity,
    UnitPrice: 18500,
    UnitName: 'Cai',
    LotNumber: lot,
    ManufacturingDate: '2026-06-09',
    ExpiryDate: '',
    BaseUomId: Number(fixture.item.baseUomId),
    TransactionUomId: Number(fixture.item.baseUomId),
    IsMatched: true,
    RequiresReview: false,
    DocumentNumber: referenceNo,
    LineNumber: lineNumber
  });
  const header071 = makeHeader('HD-ECOM-2026-071', 'DigiHub Viet Nam', 'Nhap hang bo sung truoc chien dich flash sale.');
  const header072 = makeHeader('HD-ECOM-2026-072', 'GearZone Distribution', 'Nhap phu kien gaming va gia do laptop.');

  await page.route('**/Vouchers/AnalyzeReceipts', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        provider: 'Batch',
        parseStatus: 'Success',
        documents: [
          {
            DocumentKey: 'HD-ECOM-2026-071|2026-06-09|DIGIHUB',
            ReferenceNo: 'HD-ECOM-2026-071',
            VoucherDate: '2026-06-09',
            PartnerName: 'DigiHub Viet Nam',
            Header: header071,
            Lines: [makeLine('HD-ECOM-2026-071', 80, 'AB9-260609', 1), makeLine('HD-ECOM-2026-071', 60, 'GAN-260609', 2)],
            SourceFiles: ['ecommerce-inbound-bill-01.jpg'],
            DuplicateDocumentFiles: ['ecommerce-inbound-bill-01.png'],
            Warnings: ['ecommerce-inbound-bill-01.png trung chung tu HD-ECOM-2026-071 va da bo qua.'],
            Provider: 'Groq',
            ParseStatus: 'Success',
            Confidence: 0.95,
            SourceLogId: 171
          },
          {
            DocumentKey: 'HD-ECOM-2026-072|2026-06-09|GEARZONE',
            ReferenceNo: 'HD-ECOM-2026-072',
            VoucherDate: '2026-06-09',
            PartnerName: 'GearZone Distribution',
            Header: header072,
            Lines: [makeLine('HD-ECOM-2026-072', 48, 'G102-260609', 1), makeLine('HD-ECOM-2026-072', 24, 'K2-260609', 2)],
            SourceFiles: ['ecommerce-inbound-bill-02.jpg'],
            DuplicateDocumentFiles: ['ecommerce-inbound-bill-02.png'],
            Warnings: ['ecommerce-inbound-bill-02.png trung chung tu HD-ECOM-2026-072 va da bo qua.'],
            Provider: 'Groq',
            ParseStatus: 'Success',
            Confidence: 0.94,
            SourceLogId: 172
          },
          {
            DocumentKey: 'hash:unusable-document',
            ReferenceNo: '',
            VoucherDate: '',
            PartnerName: '',
            Header: {},
            Lines: [{
              ItemId: null,
              ItemCode: '',
              ItemName: '',
              Quantity: 0,
              UnitName: '',
              IsMatched: false,
              RequiresReview: true,
              LineNumber: 1
            }],
            SourceFiles: ['unusable-document.jpg'],
            DuplicateDocumentFiles: [],
            Warnings: ['Không nhận diện được số chứng từ hoặc vật tư.'],
            Provider: 'Groq',
            ParseStatus: 'Partial',
            Confidence: 0.2,
            SourceLogId: 173
          }
        ],
        warnings: ['Da bo qua 2 file trung noi dung/chung tu.'],
        duplicateFileCount: 2,
        duplicateDocumentCount: 2,
        requiresDocumentSelection: true,
        canAutoApply: false,
        readyLineCount: 4
      })
    });
  });

  await page.locator('#documentFileInput').setInputFiles([
    { name: 'ecommerce-inbound-bill-01.jpg', mimeType: 'image/jpeg', buffer: Buffer.from('doc-071-jpg') },
    { name: 'ecommerce-inbound-bill-01.png', mimeType: 'image/png', buffer: Buffer.from('doc-071-png-duplicate') },
    { name: 'ecommerce-inbound-bill-02.jpg', mimeType: 'image/jpeg', buffer: Buffer.from('doc-072-jpg') },
    { name: 'ecommerce-inbound-bill-02.png', mimeType: 'image/png', buffer: Buffer.from('doc-072-png-duplicate') }
  ]);

  const preview = page.locator('.swal2-popup').first();
  await expect(preview).toBeVisible();
  await expect(preview).toContainText('HD-ECOM-2026-071');
  await expect(preview).toContainText('HD-ECOM-2026-072');
  await expect(preview.locator('input[name="wmsOcrDocumentChoice"]')).toHaveCount(3);
  await expect(preview.locator('input[name="wmsOcrDocumentChoice"][value="2"]')).toBeDisabled();
  await expect(preview).toContainText('Không thể áp dụng: chứng từ chưa có số và chưa có dòng vật tư đủ điều kiện.');
  await preview.locator('input[name="wmsOcrDocumentChoice"][value="0"]').check({ force: true });
  await page.locator('.swal2-confirm').click();
  await resolveHeaderConflictDialog(page, 'ocr');

  await expect(page.locator('input[name="ReferenceNo"]')).toHaveValue('HD-ECOM-2026-071');
  const rows = page.locator('#linesContainer .line-row');
  await expect(rows).toHaveCount(2);
  await expect(rows.nth(0).locator('input[name$=".TransactionQty"]')).toHaveValue('80');
  await expect(rows.nth(1).locator('input[name$=".TransactionQty"]')).toHaveValue('60');
  await expect(rows.nth(0).locator('input[name$=".OcrDocumentNumber"]')).toHaveValue('HD-ECOM-2026-071');
  await expect(rows.nth(1).locator('input[name$=".OcrDocumentNumber"]')).toHaveValue('HD-ECOM-2026-071');
  await expect(rows.nth(0).locator('input[name$=".OcrSourceLineNumber"]')).toHaveValue('1');
  await expect(rows.nth(1).locator('input[name$=".OcrSourceLineNumber"]')).toHaveValue('2');
  await expect(page.locator('#AiOcrLogId')).toHaveValue('171');
  const quantityValues = await page.locator('#linesContainer input[name$=".TransactionQty"]').evaluateAll(inputs =>
    inputs.map(input => (input as HTMLInputElement).value));
  expect(quantityValues).toEqual(['80', '60']);

  finishAudit();
});

test('voucher manual row does not inherit OCR trace and survives OCR replacement', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the OCR row replacement regression.');
  const finishAudit = attachRuntimeAudit(page, 'voucher OCR row replacement keeps manual rows');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const firstRow = page.locator('#linesContainer .line-row').first();
  await firstRow.locator('input[name$=".OcrDocumentNumber"]').evaluate((input: HTMLInputElement) => { input.value = 'OCR-OLD-001'; });
  await firstRow.locator('input[name$=".OcrSourceLineNumber"]').evaluate((input: HTMLInputElement) => { input.value = '1'; });
  await page.locator('#AiOcrLogId').evaluate((input: HTMLInputElement) => { input.value = '701'; });

  await page.locator('[data-wms-call="addRow"]').click();
  let rows = page.locator('#linesContainer .line-row');
  await expect(rows).toHaveCount(2);
  const manualRow = rows.nth(1);
  await expect(manualRow.locator('input[name$=".OcrDocumentNumber"]')).toHaveValue('');
  await expect(manualRow.locator('input[name$=".OcrSourceLineNumber"]')).toHaveValue('');
  await manualRow.locator('input[name$=".TransactionQty"]').fill('7');

  await page.evaluate(() => {
    (window as any).__wmsReplaceOcrRows = (window as any).replaceAppliedOcrRowsIfNeeded();
  });
  const decision = page.locator('.swal2-popup').filter({ hasText: 'Thay chứng từ AI đang áp dụng?' }).first();
  await expect(decision).toBeVisible();
  await expect(decision).toContainText('chỉ thay các dòng AI cũ');
  await decision.getByRole('button', { name: 'Thay dòng AI cũ' }).click();
  await page.evaluate(async () => { await (window as any).__wmsReplaceOcrRows; });

  rows = page.locator('#linesContainer .line-row');
  await expect(rows).toHaveCount(1);
  await expect(rows.first().locator('input[name$=".TransactionQty"]')).toHaveValue('7');
  await expect(rows.first().locator('input[name$=".OcrDocumentNumber"]')).toHaveValue('');
  await expect(rows.first().locator('input[name$=".OcrSourceLineNumber"]')).toHaveValue('');
  await expect(page.locator('#AiOcrLogId')).toHaveValue('');

  finishAudit();
});

test('voucher Excel import previews rows and blocks applying the same file twice', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the Excel preview and duplicate-file regression.');
  const finishAudit = attachRuntimeAudit(page, 'voucher Excel preview duplicate guard');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const fixture = await getVoucherDocumentFixture(page, testInfo);
  if (!fixture) {
    finishAudit();
    return;
  }

  let importRequestCount = 0;
  await page.route('**/Vouchers/ImportLinesExcel', async route => {
    importRequestCount += 1;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        data: JSON.stringify([{
          ItemId: Number(fixture.item.itemId),
          ItemCode: fixture.item.itemCode,
          ItemName: fixture.item.itemName,
          Quantity: 3,
          UnitPrice: 0,
          LotNumber: '',
          BaseUomId: Number(fixture.item.baseUomId),
          TransactionUomId: Number(fixture.item.baseUomId),
          ConversionRate: 1,
          IsMatched: true
        }]),
        mode: 'Preview',
        policy: 'AllOrNothing',
        templateVersion: 'WMS-VOUCHER-LINES-1.0',
        rowCount: 1,
        fileHashSha256: 'a'.repeat(64),
        warnings: []
      })
    });
  });

  const file = {
    name: 'AUDIT_TEST_voucher-lines.xlsx',
    mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    buffer: Buffer.from('mock workbook handled by route')
  };
  await page.locator('#excelFileInput').setInputFiles({
    ...file,
    name: 'AUDIT_TEST_voucher-lines-copy.xlsx'
  });
  const preview = page.locator('.swal2-popup').filter({ hasText: 'Kiểm tra dữ liệu Excel' }).first();
  await expect(preview).toBeVisible();
  await expect(preview).toContainText(fixture.item.itemCode);
  await preview.getByRole('button', { name: 'Áp dụng 1 dòng' }).click();

  const applied = page.locator('.swal2-popup').filter({ hasText: 'Đã áp dụng dữ liệu Excel' }).first();
  await expect(applied).toBeVisible();
  await applied.getByRole('button', { name: /OK|Đồng ý/i }).click();
  let rows = page.locator('#linesContainer .line-row');
  await expect(rows).toHaveCount(1);
  await expect(rows.first().locator('input[name$=".TransactionQty"]')).toHaveValue('3');
  await expect(page.locator('#excelFileInput')).toHaveValue('');
  await expect(page.locator('#importExcelBtn .fa-spinner')).toHaveCount(0);
  await page.locator('#excelFileInput').setInputFiles({
    ...file,
    name: 'AUDIT_TEST_voucher-lines-renamed.xlsx'
  });
  await expect.poll(() => importRequestCount).toBe(2);
  const duplicate = page.locator('.swal2-popup').filter({ hasText: 'File đã được áp dụng' }).first();
  await expect(duplicate).toBeVisible();
  await expect(duplicate).toContainText('không chèn lặp');
  await duplicate.getByRole('button', { name: /OK|Đồng ý/i }).click();

  rows = page.locator('#linesContainer .line-row');
  await expect(rows).toHaveCount(1);
  await expect(rows.first().locator('input[name$=".TransactionQty"]')).toHaveValue('3');
  expect(importRequestCount).toBe(2);

  finishAudit();
});

test('voucher create can submit after validation failure and blocks double submit request', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the submit retry/double-click regression.');
  const finishAudit = attachRuntimeAudit(page, 'voucher submit retry double-click');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  const submitButton = page.locator('#submitBtn').first();

  await page.locator('select[name="PartnerId"]').selectOption('');
  await submitButton.click();
  await expect(page.locator('.swal2-popup').first()).toContainText(/Thiếu thông tin|Vui lòng chọn/);
  await page.locator('.swal2-confirm').click();
  await expect(page.locator('.swal2-container')).toHaveCount(0);
  await expect(submitButton).toBeEnabled();
  await expect(submitButton.locator('.fa-spinner')).toHaveCount(0);

  if (!await fillMinimalInboundVoucher(page, testInfo)) {
    finishAudit();
    return;
  }

  let postCount = 0;
  await page.route('**/Vouchers/Create**', async route => {
    if (route.request().method() !== 'POST') {
      await route.continue();
      return;
    }

    postCount += 1;
    await new Promise(resolve => setTimeout(resolve, 600));
    await route.fulfill({
      status: 200,
      contentType: 'text/html; charset=utf-8',
      body: '<!doctype html><html lang="vi"><body><h1>Phiếu đã được kiểm thử lưu</h1></body></html>'
    });
  });

  await submitButton.scrollIntoViewIfNeeded();
  const submitBox = await submitButton.boundingBox();
  expect(submitBox, 'submit button should be measurable before double-click regression').not.toBeNull();
  const submitX = (submitBox?.x ?? 0) + (submitBox?.width ?? 0) / 2;
  const submitY = (submitBox?.y ?? 0) + (submitBox?.height ?? 0) / 2;
  const postStarted = page.waitForRequest(request =>
    request.method() === 'POST' && request.url().includes('/Vouchers/Create'));
  await page.mouse.click(submitX, submitY);
  await postStarted;
  await page.mouse.click(submitX, submitY);
  await page.waitForLoadState('domcontentloaded');
  expect(postCount, 'double click should not create duplicate submit requests').toBe(1);
  await expect(page.getByRole('heading', { name: 'Phiếu đã được kiểm thử lưu' })).toBeVisible();

  finishAudit();
});

test('voucher OCR shows friendly error and restores upload button on provider failure', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass covers the OCR failure regression.');
  const finishAudit = attachRuntimeAudit(page, 'voucher OCR provider failure', [
    /^Failed to load resource: the server responded with a status of 400 \(Bad Request\)$/
  ]);

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  await page.route('**/Vouchers/AnalyzeReceipts', async route => {
    await route.fulfill({
      status: 400,
      contentType: 'application/json',
      body: JSON.stringify({
        error: 'Dịch vụ đọc chứng từ đang quá tải tạm thời. Vui lòng thử lại sau.',
        guidance: 'Bạn vẫn có thể nhập bằng Excel, quét mã vạch hoặc chọn vật tư thủ công trên phiếu.',
        code: 'DOCUMENT_READ_ERROR'
      })
    });
  });

  const uploadButton = page.locator('#uploadDocumentBtn');
  await page.locator('#documentFileInput').setInputFiles({
    name: 'mock-provider-503.png',
    mimeType: 'image/png',
    buffer: Buffer.from('mock image')
  });

  const errorDialog = page.locator('.swal2-popup').first();
  await expect(errorDialog).toBeVisible();
  await expect(errorDialog).toContainText('Lỗi đọc chứng từ');
  await expect(errorDialog).toContainText('đang quá tải tạm thời');
  await expect(errorDialog).toContainText('nhập bằng Excel');
  await expect(uploadButton).toBeEnabled();
  await expect(uploadButton.locator('.fa-spinner')).toHaveCount(0);

  finishAudit();
});

test('workflow profiles use Vietnamese operational labels', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass is enough for the workflow wording gate.');

  await page.goto('/Operations/WorkflowProfiles', { waitUntil: 'networkidle' });
  const moduleOptions = (await page.locator('select[name="moduleKey"] option').allTextContents()).join('\n');
  const visibleText = await page.locator('.main-content').innerText();

  await expect(page.getByRole('heading', { name: 'Quy tắc vận hành kho' })).toBeVisible();
  expect(visibleText).toContain('Phạm vi áp dụng');
  expect(visibleText).toContain('Toàn kho');
  const ownerScopeRadio = page.locator('input[name="workflowScopeMode"][value="ThreePl"]');
  if (await ownerScopeRadio.count()) {
    expect(visibleText).toContain('Theo khách hàng thuê kho');
    await expect(page.locator('#workflowOwnerScopeField')).toBeHidden();
  } else {
    expect(visibleText).not.toContain('Theo chủ hàng 3PL');
    await expect(page.locator('#workflowOwnerScopeField')).toBeHidden();
  }
  expect(moduleOptions).toContain('Nhập kho');
  expect(moduleOptions).toContain('Di chuyển nội bộ');
  expect(visibleText).not.toMatch(/Inbound receive and QC|Directed movement|Outbound pick pack ship|Carrier handover|Cycle count and adjustment/);
  expect(visibleText).not.toMatch(/\binbound\b|\boutbound\b|\bmovement\b|\bshipping\b|\bstockcount\b/);
  expect(visibleText).not.toMatch(/Cấu hình quy trình|Internal \/ unowned|unowned/);
  expect(visibleText).not.toContain('Theo chủ hàng 3PL');
});

test('receiving action buttons do not overlap', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'Desktop table action layout is the regression target.');

  await gotoVisualAudited(page, { name: 'receiving', path: '/Operations/Receiving' }, testInfo.project.name);
  const actionLists = page.locator('.receiving-action-list');
  if (await actionLists.count() === 0) {
    markMissingFixture(testInfo, 'receiving action layout requires at least one receiving row');
    return;
  }
  await expect(actionLists.first()).toBeVisible();

  const result = await actionLists.evaluateAll(lists => {
    for (const list of lists) {
      const buttons = Array.from(list.querySelectorAll<HTMLElement>('.btn'));
      if (buttons.length < 2) continue;

      const boxes = buttons.map(button => {
        const rect = button.getBoundingClientRect();
        return {
          left: rect.left,
          right: rect.right,
          top: rect.top,
          bottom: rect.bottom
        };
      });

      const overlaps = boxes.some((a, index) => boxes.slice(index + 1).some(b => {
        const horizontal = Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left));
        const vertical = Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top));
        return horizontal > 1 && vertical > 1;
      }));

      return { foundTwoButtons: true, overlaps };
    }

    return { foundTwoButtons: false, overlaps: false };
  });

  if (!result.foundTwoButtons) {
    markMissingFixture(testInfo, 'receiving action overlap requires a row with at least two actions');
    return;
  }
  expect(result.foundTwoButtons, 'receiving demo data should include a serial-tracked row with two actions').toBe(true);
  expect(result.overlaps, 'receiving row action buttons must not overlap').toBe(false);
});

test('offline queue stays hidden when empty to avoid navigation flash', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One authenticated desktop pass is enough for the queue empty-state gate.');

  await page.goto('/Vouchers/Create?type=NhapKho', { waitUntil: 'networkidle' });
  await page.evaluate(async () => {
    localStorage.setItem('wms_offline_queue_hidden', 'false');
    await (window as any).wmsOfflineQueue?.render?.();
  });

  const widget = page.locator('#offlineQueueWidget');
  const toggle = page.locator('#offlineQueueToggle');
  await expect(widget).toHaveAttribute('data-pending-count', '0');
  await expect(widget).toHaveClass(/is-empty/);
  await expect(widget).toHaveClass(/is-ready/);
  await expect(toggle).toBeHidden();
});

test('enterprise toast is not covered by fixed topbar', async ({ page }, testInfo) => {
  test.skip(!['desktop-100', 'mobile'].includes(testInfo.project.name), 'Toast collision is checked on primary desktop and mobile only.');

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.evaluate(() => {
    (window as any).enterpriseNotify?.({
      title: 'Kiểm tra thông báo nghiệp vụ',
      text: 'Toast phải nằm dưới thanh điều hướng và không bị che.',
      icon: 'error',
      timer: 6000
    });
  });

  const popup = page.locator('.swal2-popup').first();
  await expect(popup).toBeVisible();
  const popupBox = await popup.boundingBox();
  const headerBox = await page.locator('.app-topbar').boundingBox();
  expect(popupBox?.y ?? 0).toBeGreaterThanOrEqual((headerBox?.height ?? 56) + 4);
});

test('quick search degrades safely when barcode lookup is unavailable', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One desktop pass covers the quick-search network fallback.');

  const pageErrors: string[] = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  await page.route('**/Items/GetItemByBarcode**', route => route.fulfill({
    status: 503,
    contentType: 'application/json',
    body: JSON.stringify({ error: 'temporary outage' })
  }));

  await page.goto('/', { waitUntil: 'networkidle' });
  await page.locator('[data-wms-call="openQuickSearch"]').click();
  await page.locator('#qsInput').fill('DEMO-IT');

  await expect(page.locator('#qsResults')).toContainText('Không thể tra nhanh theo mã vạch');
  await expect(page.locator('#qsResults')).toContainText('Tìm phiếu');
  await expect(page.locator('#qsResults')).toContainText('Tìm vật tư');
  expect(pageErrors, 'quick search fallback must not cause page errors').toEqual([]);
});

test('offline queue exposes a safe degraded state when local storage is blocked', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One desktop pass covers the offline-storage fallback.');

  const pageErrors: string[] = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  await page.addInitScript(() => {
    Storage.prototype.setItem = function () {
      throw new DOMException('Storage blocked for regression test', 'SecurityError');
    };
    Storage.prototype.removeItem = function () {
      throw new DOMException('Storage blocked for regression test', 'SecurityError');
    };
  });

  await page.goto('/Operations/RfReceiving', { waitUntil: 'networkidle' });
  await expect(page.locator('html')).toHaveAttribute('data-wms-offline-storage', 'unavailable');
  expect(pageErrors, 'blocked local storage must not cause page errors').toEqual([]);
});

test('receiving business rejection restores the button and leaves no retry item', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop-100', 'One desktop pass covers the receiving queue rejection contract.');

  await page.addInitScript(() => {
    indexedDB.deleteDatabase('wms-offline-scan-queue');
    localStorage.removeItem('wms_offline_scan_queue_v1');
  });
  await page.route('**/Vouchers/ConfirmReceiving', route => route.fulfill({
    status: 422,
    contentType: 'application/json',
    body: JSON.stringify({
      success: false,
      code: 'INBOUND_APPOINTMENT_REQUIRED',
      message: 'Phiếu nhập cần đủ thông tin lịch xe đến trước khi chuyển sang bước nhận hàng.'
    })
  }));

  await page.goto('/Operations/RfReceiving', { waitUntil: 'networkidle' });
  const form = page.locator('form[action*="/Vouchers/ConfirmReceiving"][data-offline-queue="true"]').first();
  if (await form.count() === 0) {
    markMissingFixture(testInfo, 'receiving rejection requires an approved inbound with a valid appointment');
    return;
  }

  const submit = form.locator('button[type="submit"]');
  await submit.click();
  await expect(page.locator('.swal2-popup')).toContainText('lịch xe đến');
  await expect(submit).toBeEnabled();
  await expect(submit).not.toHaveAttribute('aria-busy', 'true');
  await expect(submit).not.toHaveClass(/enterprise-submit-loading|is-wms-loading/);
  await expect.poll(async () => page.evaluate(async () => {
    const queue = (window as any).wmsOfflineQueue;
    const rows = queue ? await queue.exportQueueSnapshot() : [];
    return rows.filter((row: any) => row.status !== 'sent').length;
  })).toBe(0);
});
