# Dashboard File Impact Map

## Runtime Routes

`GET /Home/Index` → `HomeController.Index` → `BuildScopedDashboardAsync` or `BuildEnterpriseDashboardAsync` → `DashboardViewModel` → `Views/Home/Index.cshtml` → shared layout/sidebar/CSS.

`GET /Reports/WarehouseOverview` → `ReportsController.WarehouseOverview` → `BuildWarehouseOverviewModelAsync` → `WarehouseOverviewPageViewModel` → `Views/Reports/WarehouseOverview.cshtml` → shared layout/sidebar/CSS.

## Direct Runtime Files

| File/symbol | Responsibility | Data/consumer | Change risk |
|---|---|---|---|
| `Controllers/HomeController.cs` | Dashboard scope and aggregate queries | Items, locations, vouchers, waves, pick/movement tasks, reservations, alerts | High: metric and data-scope correctness |
| `Services/DashboardCommandCenterService.cs:BuildAsync` | Builds the role-scoped process summaries and prioritized work queue | Vouchers, pick/movement tasks, count sheets, quality inspections and exception cases | High: date, status, owner and warehouse semantics |
| `ViewModels/DashboardCommandCenterViewModels.cs` | Command Center request, scope, KPI, process and work-item contracts | Home controller, partial view and tests | Medium: additive contract; no public API serialization |
| `ViewModels/ViewModels.cs:8` | `DashboardViewModel` contract | Home controller/view/tests | Medium: additive properties are compatible |
| `Views/Home/Index.cshtml` | Role workspace, work queue and KPI presentation | Dashboard model and role claims | Medium: responsive/wording/drill-down |
| `Views/Home/_CommandCenter.cshtml` | Scope/filter bar, six work counters, prioritized queue, drill-down and process progress | `DashboardCommandCenterViewModel` | Medium: accessibility, route and responsive behavior |
| `Program.cs` service registration | Scoped `IDashboardCommandCenterService` registration | Home request scope | Low: additive DI registration |
| `Controllers/ReportsController.WarehouseOverview.cs` | Management overview date/warehouse scope, KPI and exception queries | Inventory, reservation, voucher and data-quality summary | High: management metric and scope correctness |
| `ViewModels/WarehouseOverviewViewModels.cs` | `WarehouseOverviewPageViewModel` and row contracts | Reports controller/view/Playwright | Medium: additive properties are compatible |
| `Views/Reports/WarehouseOverview.cshtml` | Management overview filters, KPI grid, warehouse/daily-flow/detail panels | Warehouse overview model | Medium: responsive, wording and drill-down |
| `Services/InventoryBalanceService.cs` | On-hand snapshot aggregation and value inputs | Dashboard and reports | High: shared inventory meaning |
| `Services/Enterprise1113Services.cs` | Role workspace/analytics-related services | Home role workspace and enterprise screens | Medium |
| `Views/Shared/_Layout.cshtml` | Topbar, pending-inbound badge and shell | Every authenticated page | High: global query/layout consumer |
| `Views/Shared/_SidebarNav.cshtml` | Role menu and active state | Every authenticated page | High: route/navigation regression |
| `Controllers/OperationsController.ExceptionCenter.cs` | Read-only exception projection, explicit synchronization and guarded state transitions | Operational anomaly queries and `OperationExceptionCases` | High: scope, concurrency and audit semantics |
| `Models/OperationExceptionCase.cs`, `Models/Enums.cs` | Persisted exception identity and Open/Acknowledged/Resolved/Ignored states | Exception Center, API health summary and Command Center | High: schema-compatible state contract |
| `Views/Operations/ExceptionCenter.cshtml` | Filterable exception workflow and responsive action controls | Operations controller | Medium: role, accessibility and mobile reflow |
| `wwwroot/css/site.css` | Dashboard/grid/card/shell responsive rules | All pages | High: shared selectors and breakpoints |

## Indirect Data Contracts

- `Models/Voucher.cs`, `Models/Enums.cs`: status/date/partial semantics.
- `Models/Item.cs`, `Models/ItemLocation.cs`, reservation/pick/wave/movement models.
- `Services/TenantScopeService.cs`: owner scope.
- `Authorization/PermissionAuthorization.cs`, `Models/WmsRoles.cs`: role/financial visibility.
- Report drill-down actions in Reports/Operations controllers.

## Permission And Navigation Contract

| Surface | Route guard | Navigation visibility | Direct-route evidence |
|---|---|---|---|
| Staff home dashboard | Authenticated controller/runtime role workspace plus warehouse/owner query scope | `Trang chính` is available to authenticated operational roles | Authenticated route and role suites. |
| Management warehouse overview | `WmsRoles.ReportManagerRoles` and policy `report.view` | Sidebar `Tổng quan kho` appears only when `canSeeOperationalReports` is true | Role allow/deny suite plus `RBAC and navigation impact routes remain functionally sound`. |

Hiding the menu is not authorization. `/Reports/WarehouseOverview` remains protected by controller role/policy and its warehouse/owner query scope.

## Existing Test Consumers

- Dashboard/role/static tests under `WMS.Tests`.
- `tests/visual/wms-visual-regression.spec.ts` home/shell tests.
- `tests/visual/wms-mobile-deep.spec.ts` mobile route coverage.
- `WMS.Tests/DashboardCommandCenterTests.cs`: warehouse/owner scope, day boundary, deterministic filters, role actions and hand-calculated process reconciliation.
- `WMS.Tests/Gate7CommandCenterContractTests.cs`: exception concurrency token and stable bounded identity key.
- `tests/visual/wms-gate7-command-center.spec.ts`: read-only performance, drill-down, filters, native progress, console/network checks and four independent viewports.
- `tests/visual/wms-role-access.spec.ts`: role process matrix; browser execution currently requires fresh isolated role auth states.

## Query, Cache And Background-Job Impact

- The Command Center executes bounded EF aggregates and bounded work projections in the request. It does not add a cache, timer, auto-refresh or background worker.
- Queries are `AsNoTracking`, filtered by warehouse/owner before projection, and capped to 5–50 displayed work items after database-side candidate limits.
- Exception generation is not performed by `GET /`; the Command Center only reads persisted cases. `GET /Operations/ExceptionCenter` is also read-only. Synchronization is an explicit protected `POST`.
- No migration or index was added for Gate 7. Existing entity/status/date indexes remain the dependency; staging query-plan/index review is still required before production-scale claims.

## Duplicate And Legacy Surface Decision

- `Views/Home/Index.cshtml` still contains the existing role workspace and operational KPI area. They are retained because they have active consumers and provide quick actions/financial metrics not duplicated by the new queue.
- `/Reports/WarehouseOverview` remains a separate period-based management report. It is not replaced by the same-day Command Center.
- No source file was deleted or renamed. No unreferenced dashboard implementation was proven safe to remove.

## Gate 7 Change Boundary

Only the traced files above were changed. Public routes, API contracts, database schema and existing report routes remain compatible. The responsive defect at exactly 768 CSS pixels was corrected by converting only the work table to repeated cards at `max-width: 768px`; broader tablet grids remain two-column.

## Mandatory Regression Scope

1. Admin, Manager, operational roles, ReportViewer and Viewer.
2. Warehouse scope and owner scope.
3. Empty/zero/partial/error/stale/long text/many alerts.
4. Metric-to-detail reconciliation at one as-of timestamp.
5. Desktop 1366/1440/1920, laptop minimum, tablet and mobile independently.
6. Console/network/accessibility/overflow and manual screenshot review.

No dashboard source file is approved for deletion or replacement. Changes must extend the traced runtime path unless a consumer analysis proves otherwise.
