# WMS Runtime Map

## Evidence Scope

- Roadmap SHA-256: `7DEF7929A06598E7C002AADD017666A522A66E508F3E26083FCFC173C0EC928A`
- Prompt SHA-256: `902D483FF99A4A0811960FAD61073488A582BA7267A7C71B6FB2DCA5687BB238`
- Route inventory: `artifacts/full-audit/CONTROLLER_ACTION_INVENTORY.csv`
- Navigation inventory: `artifacts/full-audit/UI_NAVIGATION_INVENTORY.csv`
- DI inventory: `artifacts/full-audit/SERVICE_REGISTRATION_INVENTORY.csv`
- Data inventory: `artifacts/full-audit/DBSET_INVENTORY.csv`
- Inventory write candidates: `artifacts/full-audit/INVENTORY_WRITE_CANDIDATES.csv`
- State mutation candidates: `artifacts/full-audit/STATE_MUTATION_CANDIDATES.csv`

The map describes the current implementation. It does not mark a path correct merely because it exists.

## Process Startup

1. `Program.cs:21` creates the application builder and loads standard ASP.NET Core configuration providers.
2. `Program.cs:49` reads `ConnectionStrings:DefaultConnection` and configures SQL Server.
3. `Program.cs:75` registers permission policies for every `WmsPermissions` code.
4. `Program.cs:110-181` registers core and enterprise services.
5. `Program.cs:286-294` conditionally registers background workers.
6. `Program.cs:297-301` creates the DataProtection directory and persists keys to disk.
7. The host conditionally executes `IRbacSeedService.EnsureSeededAsync()` through the tested startup-initialization switch; default behavior remains enabled.
8. `Services/RbacSeedService.cs:24-160` can insert/update permission, role and role-permission rows and calls `SaveChangesAsync` three times.
9. Middleware is applied in the order forwarded headers → correlation telemetry → exception handler → HSTS/HTTPS → security headers → compression/static files → routing → rate limiting → authentication → authorization.
10. Conventional MVC routes, `/health/live` and `/health` are mapped at `Program.cs:464-471`.

**Safety result:** read-only smoke can disable RBAC initialization, background workers and request telemetry through tested configuration switches. Default startup remains write-capable.

## Configuration Path

- `appsettings.json` contains `ConnectionStrings.DefaultConnection`; its value is never copied to audit artifacts.
- `Properties/launchSettings.json` places another connection under `profiles.http.connectionStrings.DefaultConnection` rather than under `environmentVariables.ConnectionStrings__DefaultConnection`.
- Current runtime precedence for that custom launch-profile property is not yet evidenced. Until a controlled startup test proves otherwise, the intended hosting target and the effective runtime target are treated separately.
- Background workers can be disabled by `BackgroundWorkers__Enabled` in the launch environment.

## Authentication And Authorization

1. Cookie authentication is configured at `Program.cs:61-72`; unsafe MVC methods receive global antiforgery validation.
2. All MVC actions receive a global authenticated-user filter at `Program.cs:29-38` unless an allow-anonymous marker applies.
3. Login claims are built at `Controllers/AccountController.cs:379-411`: identity, role, optional warehouse, owner scopes and permission codes.
4. Non-admin login without a warehouse is rejected at `Controllers/AccountController.cs:585-591`.
5. `Authorization/PermissionAuthorization.cs:22-38` gives Admin full policy override and otherwise requires an exact permission claim.
6. `Services/TenantScopeService.cs:29-88` applies owner restriction only when owner-scope IDs exist; no owner IDs means unrestricted owner scope by current design.
7. API integration uses `ApiKeyAllowAnonymousAttribute` plus per-action API-key validation; the missing-key unit matrix covers all 18 reflected HTTP actions. Live HTTP scope/authenticated integration testing remains pending.
8. Inbound submit/receive mutations use `WmsRoles.InboundRoles` plus `voucher.create`; inbound approve/reject uses Admin/Manager plus `voucher.approve.inbound`.
9. Pick/pack mutations use `WmsRoles.OutboundRoles` plus `voucher.create`. Dock milestone mutation uses `WmsRoles.InboundRoles`, `voucher.create`, warehouse scope and owner scope.
10. API voucher creation validates the integer voucher type before enum conversion and persistence.
11. Repository reflection tests reject server-owned voucher status fields in first-party mutation contracts; authenticated direct-route testing remains blocked.
12. OCR/import/catch-weight/defect helpers require `voucher.create`; dock assignment requires inbound approval and backorder creation requires outbound approval.
13. Putaway suggestions use inbound roles, outbound source-location lookup uses outbound roles, and shared voucher lookups require `voucher.create`. Warehouse/owner scope is checked before putaway data is returned.
14. Serial receive, inventory movement, cross-dock and cycle-count program actions require their current create/reassign/cancel/release/count permissions. Shipment-load mutations require `voucher.confirm.shipping`.
15. Seed and reflection tests prove the report-only role has no voucher mutation path, while `SystemController` and `UsersController` are Admin-only.
16. `ApiIntegrationController` bypasses cookie auth only through the API-key boundary; all 18 reflected HTTP actions now return 401 before runtime work when `X-API-Key` is absent.
17. Anonymous account routes fail closed in tested production configurations: first-Admin bootstrap needs its token, public registration is opt-in, and development password reset is hidden outside Development.

## MVC And UI Surface

- Static discovery found 396 controller actions across 15 controllers.
- Largest surfaces: Operations 193 actions, Reports 45, Vouchers 35, Labels 20 and API Integration 18.
- Static navigation discovery found 516 tag-helper/literal links.
- Shared shell: `Views/Shared/_Layout.cshtml` → `Views/Shared/_SidebarNav.cshtml` → `wwwroot/css/site.css` and inline navigation JavaScript.
- Controller and class authorization are recorded in the route inventory; runtime endpoint metadata remains `UNKNOWN` until safe application startup.

## Inventory Source And Write Path

1. Snapshot/source used by reads: `ItemLocation.Quantity` and `ReservedQty` (`Services/InventoryBalanceService.cs:28-58`).
2. Cached item total: `Item.CurrentStock`, synchronized from location rows by `InventoryBalanceService.SyncCurrentStockAsync`.
3. Available quantity: `Quantity - ReservedQty`.
4. `Data/AppDbContext.cs:103-244` wraps `SaveChangesAsync` in a transaction when no outer transaction exists.
5. `Data/AppDbContext.cs:251-323` captures every relevant `ItemLocation` change and creates append-only inventory ledger candidates.
6. `Data/AppDbContext.cs:324-442` resolves warehouse, computes before/after deltas and derives ledger idempotency keys.
7. `InventoryTransactionSemanticRules.Validate` is called before ledger rows are added.
8. Fifty direct quantity/reservation mutation candidates exist in controllers/services and must each be traced to transaction, ledger context and scope before being accepted.
9. `InventoryReservationService.RecalculateReservedQtyAsync` recomputes reservation snapshots from voucher, kitting and VAS reservations, then persists immediately.
10. `WarehousePeriodLockPolicy` resolves the effective transaction date and is rechecked inside inbound/outbound/order-streaming transactions before mutation; `ReportsController.QuickAdjustFromSnapshot` performs the same in-transaction recheck, then reads current stock, selects stock layers, computes the difference and writes the adjustment inside that one serializable transaction.
11. `ReportsController.SetPeriodLock/ClearPeriodLock` uses a serializable transaction, reuses the unique warehouse/date history row, preserves superseded rows and writes explicit set/update/reopen/supersede/clear audit actions.

## Core Workflow Paths

| Workflow | Entry routes | Primary runtime implementation | Persistence/invariant path | Current verification |
|---|---|---|---|---|
| Inbound | `/Vouchers/Create?type=1`, approval/receiving/RF/dock routes | `VouchersController.Inbound`, `OperationsController.Receiving`, `OperationsController.DockBoard`, `InboundExecutionService` | MVC/API create normalizes quantity/conversion/base decimal boundaries; execution revalidates nonempty positive lines and UOM/base consistency plus period lock before serializable `ItemLocation` mutation and auto ledger/audit; customer return is QC-pending | `PARTIAL`: quantity/UOM 7/7, disposable SQL Server 3/3, full regression 942/942 and authenticated UOM visual pass; multi-event partial receipt and authenticated write E2E remain |
| Outbound | `/Vouchers/Create?type=2`, waves/pick/RF/packing/shipping | `VouchersController.Outbound`, `OutboundExecutionService`, `ShipmentLoadService`, `OrderStreamingService` | Exact outbound role/policy → period-lock check → preplanned FEFO reservation with 30-day shelf life → partial-policy-aware release → pick/post recheck → partial cancel or UOM-safe backorder → `ItemLocation` → ledger | `PARTIAL`: partial/non-partial 16/16, API 14/14, SQL Server partial cancel 1/1 and reversal 1/1, full 942/942; strict FIFO receipt layers, concurrent partial-post race and authenticated write E2E remain |
| Transfer/movement | Movement task and RF routes | `MovementTaskService`, transfer branches in outbound/cancellation | Atomic source/destination mutation with destination-warehouse topology recheck → ledger | `PARTIAL`: transfer 5/5, mobile deep and no-device pass; in-transit/partial receive/discrepancy are not modeled |
| Count/adjust | Reports stock-count routes, period-lock actions and adjustment voucher | `ReportsController.StockCount`, `ReportsController.Inventory`, snapshot service | Draft → blind count → counted → recount/approve → signed adjustment → in-transaction lock recheck → location/ledger; lock set/clear/reopen is serializable and audited | `PARTIAL`: workflow 3/3, affected 278/278, period-lock 9/9 and authenticated cross-device visual pass; concurrent-post/freeze and SQL race matrix remain |
| Cancel/reversal | `/Vouchers/Cancel` | `VoucherCancellationService` | Serializable transaction → reservation/location reversal → immutable original ledger plus audited Cancel counter-entry | `PARTIAL`: repeated-command and SQL Server reversal pass; authenticated role write E2E remains |
| Return/QC | Return voucher types, quality and recall routes | `ReturnRmaService`, MVC/API return creation and quality/recall services | Customer return starts QC-pending; serializable RMA disposition plus exact owner/location/lot/expiry QC ledger; supplier return posts as stock reduction | `PARTIAL`: return/QC 3/3, focused 15/15 and ledger/state 6/6 pass; disposal and authenticated write E2E remain |
| Snapshot reconciliation | background reconciliation and snapshot-outbox workers | `InventorySnapshotService` | Per-run/per-event serializable transaction → snapshot → ledger → cached balance | `PARTIAL` |
| OCR/import/private files | Voucher analyze/import/download, item image and yard evidence routes | `VouchersController.Import`, `VoucherDocumentIntakeService`, `VoucherImportQueryService`, item/yard actions | extension + MIME + signature validation, bounded OpenXML, provider fallback, owner/warehouse scope, persistence cleanup, document/line trace and confirmed apply | `PARTIAL`: targeted .NET 74/74 and authenticated functional Playwright 7/7 pass; live provider certification and malware scanner remain blocked |
| Reporting/export | `/Reports/*` and scoped operational exports, including `/Reports/Analytics` and `/Reports/SupplierInboundScorecard` | report partial controllers, inventory balance queries and `SpreadsheetExportSecurity` | read-only scoped queries, financial-permission column policy, formula neutralization and bounded synchronous workbook generation; analytics risk fails closed on dirty demand/lead-time samples and supplier KPI keeps missing denominators null | `PARTIAL`: Gate 4 export scope/data/formula/empty/readability and 5.000-row benchmark pass; AI analytics source-of-truth 9/9 and four-viewport report matrix pass; damage taxonomy, long-cell matrix and streaming/background export remain open |

## Dashboard Runtime

- Route: `/Home/Index` → `HomeController.Index` → scoped or enterprise query → `DashboardViewModel` → `Views/Home/Index.cshtml`.
- Warehouse scope is taken from the `WarehouseId` claim; Admin without a warehouse receives the enterprise view.
- Financial value is controlled by Admin or `report.view.financial`.
- The layout separately queries pending inbound approvals during Razor rendering.
- Dashboard formulas and drill-down mappings are documented in `DASHBOARD_METRIC_DICTIONARY.md`.

## Database Evidence

- Read-only target fingerprint: `E9ED607147F26B77E88A57937B852B263AED3A35A75D773E7150BB43A7FFF306`.
- Metadata: 139 user tables, 87 applied migrations, 369 enabled/trusted foreign keys and 7 check constraints.
- Gate 4 data-quality recheck: 17/18 core groups returned zero; three legacy locations still contain multiple positive stock keys (`LOCATION_MULTIPLE_STOCK_KEYS=3`).
- Gate 3 ledger summary: duplicate idempotency, orphan, balance equation and reversal metadata returned zero; two historical demo opening rows have negative intermediate availability while final balances reconcile.
- Gate 3 inbound quantity/UOM summary: open and historical inbound mismatch result sets both returned zero rows on hosting after the server-side invariant was added.
- RBAC reconciliation: five issue result sets returned zero rows; all nine roles were present and Admin had all 25 defined permissions.
- Detailed evidence: `artifacts/data-quality/wms-hosting-metadata.txt`, `wms-hosting-summary.txt` and `artifacts/full-audit/gate3/wms-*-20260714.txt`.
- RBAC evidence: `artifacts/data-quality/wms-rbac-readonly-audit-20260711.txt`.
- No database write or migration was performed by this audit.

## Remaining Runtime Verification

- Authenticated write E2E and production UAT for every specialized role.
- Strict FIFO receipt-layer design for non-expiry aggregate stock.
- In-transit/partial receive/discrepancy transfer state model if applicable.
- Broader disposable SQL Server fault injection for count, return and period-lock boundaries.
- Remaining per-write-candidate transaction/ledger mapping.
- Live external provider certification, malware scanning, background-worker and integration sandbox behavior.
- Export long-content layout matrix and streaming/background implementation for files beyond the bounded 5.000-row synchronous path.
