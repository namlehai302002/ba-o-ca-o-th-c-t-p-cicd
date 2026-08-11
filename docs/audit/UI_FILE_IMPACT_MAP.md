# UI File Impact Map

## Shared Shell

| File | Consumers | Responsibilities | Risk |
|---|---|---|---|
| `Views/Shared/_Layout.cshtml` | All MVC pages | topbar, sidebar host, breadcrumb, notices, scanner/offline shell, navigation JavaScript | Critical shared surface |
| `Views/Shared/_SidebarNav.cshtml` | Authenticated shell | role groups, route/query-context active state, icons | High |
| `Views/Shared/_ScannerModal.cshtml` | Operational mobile routes | scan modal | High for mobile/RF |
| `Views/Reports/WarehouseOverview.cshtml` | `/Reports/WarehouseOverview` | management summary header, filters, KPI grid and detail panels | High for management demo |
| `wwwroot/css/site.css` | All pages | global tokens, shell, components and route-specific styles | Critical shared surface |
| `wwwroot/js/site.js` | All pages | common interaction/search/network behavior | High |
| `wwwroot/js/mobile-scanner.js` | Camera-enabled paths | scanner/camera | High, device-dependent |
| `wwwroot/js/offline-scan-queue.js` | Operational shell | offline/retry queue | High, idempotency-dependent |
| `wwwroot/js/pwa.js` | PWA-capable pages | service-worker/install behavior | Medium |

## Page Surfaces

- 133 Razor views are recorded in the repository inventory.
- 396 controller actions and 515 static navigation links are recorded in runtime inventories.
- Reports, Operations and Vouchers are the largest route groups and require generated route/role/state coverage rather than menu-only sampling.
- Print views and binary assets require reference/license/render checks even when excluded from line review.

## CSS/Breakpoint Consumers

- Desktop sidebar width tokens and collapsed rail are defined in `site.css` around lines 7400-7810.
- Collapsed flyout uses fixed positioning, calculated top/max-height and internal vertical scrolling.
- At max-width 1024 the sidebar becomes an off-canvas drawer and main content loses desktop offset.
- Additional route/component media queries exist throughout the large shared stylesheet; no global override is allowed before selector-consumer regression.

## Playwright Sources

| Config/spec | Purpose | Current discovery |
|---|---|---:|
| `playwright.public.config.ts` | public/auth routes across desktop, laptop, tablet and mobile | 12 |
| `playwright.config.ts` | authenticated desktop/mobile visual suite | 69 in the verified `desktop-100` project |
| `playwright.mobile-deep.config.ts` | deeper mobile/tablet emulation | 424 |
| `playwright.no-device.config.ts` | RF/print simulator evidence | 10 |
| `playwright.real-e2e.config.ts` | real workflow smoke | 8 |
| `playwright.auth.config.ts` | storage-state setup | 1 |

Discovery count is not pass evidence. On the isolated local SQL Server fixture, public Playwright passes 12/12, authenticated desktop passes 68/68 with one intentional mobile-only skip, mobile/tablet deep coverage passes 424/424, no-device coverage passes 10/10, and the seven-role access matrix passes. Evidence is recorded in `artifacts/ui-cross-device/UI_ROUTE_ROLE_STATE_VIEWPORT_MATRIX.csv` and the suite reports. The generated authentication states remain test artifacts and must not be copied or exposed.

## Latest Traced Changes

| Change | Runtime consumers | Regression surface |
|---|---|---|
| Warehouse overview action/header reflow | `Views/Reports/WarehouseOverview.cshtml`, `.warehouse-overview-*` selectors in `site.css` | 1440x900, 1366x768, 768x1024 and 390x844 targeted screenshots plus mobile-deep route audit |
| Quality-inspection item identity fallback | `Views/Operations/QualityInspection.cshtml`, `/Operations/QualityInspection` | option identity assertion and authenticated desktop route audit |
| RF receiving business rejection recovery | `Views/Operations/RfReceiving.cshtml`, offline queue handler, `/Operations/RfReceiving` | button recovery, no stale retry entry, keyboard-wedge and mobile-deep tests |
| AI analytics risk gate and supplier scorecard | `ReportsController.Analytics`, `Views/Reports/Analytics.cshtml`, `Views/Reports/SupplierInboundScorecard.cshtml`, breadcrumb-only `_Layout.cshtml` mapping | source-of-truth tests plus 24 route traversals across 1440x900, 1366x768, 768x1024 and 390x844; manual scorecard screenshot review |

## Required Impact Checks Before UI Patch

1. Resolve every affected route, view, shared selector, JavaScript handler and API call.
2. Confirm desktop screenshot state separately from tablet/mobile reproduction.
3. Test active navigation state by controller, action and query context.
4. Verify flyout first/last item reachability, Escape/outside-click/focus return and zoom.
5. Run targeted screenshot/interaction tests, then all consumers of shared selectors.
6. Manually open every resulting diff before baseline approval.
