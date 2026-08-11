import { expect, test } from '@playwright/test';

type RoleExpectation = {
  roleLabel: string;
  visibleGroups: string[];
  hiddenGroups: string[];
  allowedRoutes: string[];
  deniedRoutes: string[];
  commandCenterProcesses: string[];
};

const commonGroups = ['Trang chính', 'Nhập kho', 'Xuất kho', 'Tồn kho', 'Vận chuyển', 'Báo cáo', 'Danh mục', 'Hệ thống', 'Hướng dẫn sử dụng'];

const expectations: Record<string, RoleExpectation> = {
  manager: {
    roleLabel: 'Quản lý kho',
    visibleGroups: commonGroups,
    hiddenGroups: [],
    allowedRoutes: ['/Partners', '/Operations/ShippingDispatch', '/Reports/WarehouseOverview', '/Reports/ScheduledReports'],
    deniedRoutes: ['/Users'],
    commandCenterProcesses: ['Nhập kho', 'Xuất kho', 'Di chuyển & bổ sung', 'Kiểm kê & chất lượng', 'Chuyển kho', 'Hàng trả']
  },
  inbound: {
    roleLabel: 'Nhân viên nhập kho',
    visibleGroups: ['Trang chính', 'Nhập kho', 'Hướng dẫn sử dụng'],
    hiddenGroups: ['Xuất kho', 'Tồn kho', 'Vận chuyển', 'Báo cáo', 'Danh mục', 'Hệ thống'],
    allowedRoutes: ['/Vouchers/Create?type=1', '/Operations/Receiving', '/Operations/QualityInspection'],
    deniedRoutes: ['/Vouchers/Create?type=2', '/Users'],
    commandCenterProcesses: ['Nhập kho', 'Hàng trả']
  },
  outbound: {
    roleLabel: 'Nhân viên xuất kho',
    visibleGroups: ['Trang chính', 'Xuất kho', 'Hướng dẫn sử dụng'],
    hiddenGroups: ['Nhập kho', 'Tồn kho', 'Vận chuyển', 'Báo cáo', 'Danh mục', 'Hệ thống'],
    allowedRoutes: ['/Vouchers/Create?type=2', '/Operations/PickTasks', '/Operations/Shipping'],
    deniedRoutes: ['/Reports/ScheduledReports', '/Vouchers/Create?type=1', '/Users'],
    commandCenterProcesses: ['Xuất kho', 'Hàng trả']
  },
  inventory: {
    roleLabel: 'Nhân viên tồn kho/kiểm kê',
    visibleGroups: ['Trang chính', 'Tồn kho', 'Hướng dẫn sử dụng'],
    hiddenGroups: ['Nhập kho', 'Xuất kho', 'Vận chuyển', 'Báo cáo', 'Danh mục', 'Hệ thống'],
    allowedRoutes: ['/Reports/Inventory?nav=inventory', '/Reports/StockCount', '/Operations/MovementTasks'],
    deniedRoutes: ['/Vouchers/Create?type=1', '/Users'],
    commandCenterProcesses: ['Di chuyển & bổ sung', 'Kiểm kê & chất lượng', 'Chuyển kho']
  },
  transport: {
    roleLabel: 'Nhân viên vận chuyển',
    visibleGroups: ['Trang chính', 'Vận chuyển', 'Hướng dẫn sử dụng'],
    hiddenGroups: ['Nhập kho', 'Xuất kho', 'Tồn kho', 'Báo cáo', 'Danh mục', 'Hệ thống'],
    allowedRoutes: ['/Operations/Shipping?nav=transport', '/Operations/ShippingDispatch', '/Operations/ShipmentLoads'],
    deniedRoutes: ['/Reports/StockCount', '/Users'],
    commandCenterProcesses: ['Xuất kho']
  },
  report: {
    roleLabel: 'Nhân viên báo cáo',
    visibleGroups: ['Trang chính', 'Báo cáo', 'Hướng dẫn sử dụng'],
    hiddenGroups: ['Nhập kho', 'Xuất kho', 'Tồn kho', 'Vận chuyển', 'Danh mục', 'Hệ thống'],
    allowedRoutes: ['/Reports/WarehouseOverview', '/Reports/Inventory?nav=report', '/Reports/OpsKpi'],
    deniedRoutes: ['/Vouchers/Create?type=1', '/Users'],
    commandCenterProcesses: ['Nhập kho', 'Xuất kho', 'Di chuyển & bổ sung', 'Kiểm kê & chất lượng', 'Chuyển kho', 'Hàng trả']
  },
  viewer: {
    roleLabel: 'Chỉ xem',
    visibleGroups: ['Trang chính', 'Tồn kho', 'Báo cáo', 'Hướng dẫn sử dụng'],
    hiddenGroups: ['Nhập kho', 'Xuất kho', 'Vận chuyển', 'Danh mục', 'Hệ thống'],
    allowedRoutes: ['/Reports/Inventory?nav=report'],
    deniedRoutes: ['/Reports/ScheduledReports', '/Reports/WarehouseOverview', '/Vouchers/Create?type=1', '/Users'],
    commandCenterProcesses: []
  }
};

function expectationForProject(projectName: string) {
  const expectation = expectations[projectName];
  if (!expectation) throw new Error(`No role expectation configured for project ${projectName}.`);
  return expectation;
}

test('role sees only the expected navigation groups', async ({ page }, testInfo) => {
  const role = expectationForProject(testInfo.project.name);
  const response = await page.goto('/', { waitUntil: 'networkidle' });
  expect(response?.status() ?? 200).toBeLessThan(400);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
  await expect(page.locator('#sidebar')).toBeVisible();
  await expect(page.locator('.topbar-user')).toContainText(role.roleLabel);
  await expect(page.locator('.topbar-user-menu a[href*="/Account/TrustedDevices"]')).toHaveCount(1);
  await expect(page.locator('a.topbar-icon-btn[href*="/Reports/Alerts"]')).toHaveCount(0);

  const labels = (await page.locator('#sidebar [data-nav-label]').evaluateAll(elements =>
    elements.map(element => element.getAttribute('data-nav-label') || '').filter(Boolean)
  ));

  for (const label of role.visibleGroups) expect(labels, `${role.roleLabel} should see ${label}`).toContain(label);
  for (const label of role.hiddenGroups) expect(labels, `${role.roleLabel} must not see ${label}`).not.toContain(label);
  expect(new Set(labels).size, `${role.roleLabel} navigation labels must be unique`).toBe(labels.length);

  if (testInfo.project.name === 'manager') {
    await expect(page.locator('#sidebar a', { hasText: 'Chốt tồn' })).toHaveCount(1);
    await expect(page.locator('#sidebar a', { hasText: 'Khóa kỳ' })).toHaveCount(1);
  }
});

test('role direct-route authorization matches the navigation contract', async ({ page }, testInfo) => {
  const role = expectationForProject(testInfo.project.name);

  for (const route of role.allowedRoutes) {
    const response = await page.request.get(route, { maxRedirects: 0 });
    expect(response.status(), `${role.roleLabel} should be allowed on ${route}`).toBe(200);
  }

  for (const route of role.deniedRoutes) {
    const response = await page.request.get(route, { maxRedirects: 0 });
    expect([302, 403], `${role.roleLabel} should be denied on ${route}`).toContain(response.status());
    if (response.status() === 302) {
      expect(response.headers().location || '', `${role.roleLabel} denial redirect for ${route}`).toMatch(/\/Account\/Login/i);
    }
  }


  const trustedDevices = await page.request.get('/Account/TrustedDevices', { maxRedirects: 0 });
  expect(trustedDevices.status(), `${role.roleLabel} should manage its own trusted devices`).toBe(200);

  for (const adminRoute of ['/Operations/IntegrationDashboard', '/Reports/Alerts']) {
    const response = await page.request.get(adminRoute, { maxRedirects: 0 });
    expect([302, 403], `${role.roleLabel} must be denied on ${adminRoute}`).toContain(response.status());
  }
});

test('role dashboard exposes only the assigned operational process summaries', async ({ page }, testInfo) => {
  const role = expectationForProject(testInfo.project.name);
  const response = await page.goto('/', { waitUntil: 'networkidle' });
  expect(response?.status() ?? 200).toBeLessThan(400);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);

  const commandCenter = page.locator('[data-dashboard-command-center]');
  await expect(commandCenter).toBeVisible();
  await expect(commandCenter.locator('.command-center-kpi')).toHaveCount(6);

  const actualProcesses = await commandCenter.locator('.command-center-process-title').allTextContents();
  expect(actualProcesses, `${role.roleLabel} process summaries`).toEqual(role.commandCenterProcesses);

  if (testInfo.project.name === 'report') {
    const actionLabels = await commandCenter.locator('.command-center-action-cell a').allTextContents();
    expect(actionLabels.every(label => label.includes('Xem chi tiết')), 'ReportViewer actions must be read-only').toBeTruthy();
  }
});
