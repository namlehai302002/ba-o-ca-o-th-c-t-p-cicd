# Dashboard Metric Dictionary

This dictionary documents the verified same-day Command Center contract. Business-owner approval remains `PENDING`; formulas are therefore technically reconciled, not commercially signed off.

## Shared Contract

- `AsOfAt` is one `VietnamTime.Now` snapshot passed to every widget in the request. `BusinessDate = AsOfAt.Date`; daily windows use `[00:00, next 00:00)` in UTC+07:00.
- Warehouse scope comes from the authenticated `WarehouseId` claim when present; the query parameter is only effective when that claim does not lock the user. Owner scope comes from all `OwnerPartnerId` claims.
- Cancelled vouchers are excluded from all six process summaries. No quantities from incompatible UOMs are added by the Command Center; process metrics count workflow records and work items.
- There is no Command Center cache or auto-refresh. Each GET has a new `AsOfAt`; manual refresh preserves the current URL/query string.
- Each widget fails independently. A failed widget sets `IsPartial`, emits a Vietnamese warning and writes a structured warning log without exposing the exception to the page.

## Work Queue And Counters

| Display metric | Formula and distinct key | Scope/date/null/partial handling | Drill-down/status |
|---|---|---|---|
| Tổng việc mở | Count of role-applicable non-terminal items before UI filters. Keys are `voucher:{id}`, `pick:{id}`, `movement:{id}`, `count:{id}`, `quality:{id}`, `exception:{id}`. Related voucher/task rows remain separate work units by design. | Warehouse/owner query scope first; exception cases are excluded when owner scope is active because the table has no owner lineage. | Work queue; `VERIFIED` by scoped fixture tests. |
| Chưa bắt đầu / Đang làm / Đang chờ / Bị chặn / Trễ hạn | Count queue items by normalized `StateKey`. The UI combines in-progress and waiting only in the summary card while retaining separate raw counters. | Computed from the same unfiltered work set, so applying filters does not change the headline totals. | Filter query `workState`; `VERIFIED`. |
| Khẩn cấp / Cao / Trung bình / Thấp | Count by severity after deterministic SLA/priority mapping. Blocked or at least 8 hours overdue is critical; overdue, priority at least 90, or due within 2 hours is high; due within 8 hours is medium; otherwise low. | Uses the single `AsOfAt`. | Filter query `severity`; `VERIFIED`. |
| Hoàn thành hôm nay | Sum of `CompletedToday` across the role-visible process summaries. Unit is workflow records, not physical quantity. | Uses each process completion field in `[start,end)`. | Process drill-down; hand-calculated fixture `VERIFIED`. |
| Hàng đợi hiển thị | Filter by state/severity/assignee, then order severity rank → deadline → waiting time → reference code; display limit is clamped to 5–50. | `FilteredWorkItems` is before limit; `HiddenByLimitCount` discloses omitted rows. | URL query state; `VERIFIED`. |

## Process Summaries

| Process | Due today | Completed today | Open / overdue | Drill-down | Technical status |
|---|---|---|---|---|---|
| Nhập kho | Non-cancelled inbound `ExpectedArrivalAt` in `[start,end)`. | `CompletedAt` in `[start,end)`. | Open: not posted and inbound state not Completed/Rejected. Overdue: same plus expected arrival before `AsOfAt`. | `/Operations/Receiving` | `VERIFIED` |
| Xuất kho | Non-cancelled outbound `RequestedDeliveryDate` in `[start,end)`. | `ShippedAt` in window, or `CompletedAt` when no shipped timestamp exists. | Open: not posted. Overdue: not posted and requested delivery before business-day start. | `/Operations/PickTasks` | `VERIFIED` |
| Di chuyển & bổ sung | `MovementTask.DueAt` in `[start,end)`. | `CompletedAt` in window. | Open excludes Completed/Cancelled. Overdue is open with `DueAt < AsOfAt`. | `/Operations/MovementTasks` | `VERIFIED` |
| Kiểm kê & chất lượng | Count sheets by `CountDate` plus inspections by `CreatedAt` in `[start,end)`. | Count `ApprovedAt` plus inspection `InspectedAt` in window. | Open count excludes Approved; open QC includes Pending/Inspecting/Quarantine/OnHold. Overdue count is before today; QC SLA is four hours. | `/Reports/StockCount` | `VERIFIED`; combined unit is “việc”. |
| Chuyển kho | Non-cancelled transfer `RequestedDeliveryDate` in `[start,end)`. | `CompletedAt` in window. | Open: not posted. Overdue: due before business-day start. | `/Vouchers?type=6` | `VERIFIED`; current atomic-transfer model has no in-transit metric. |
| Hàng trả | Customer return uses `ExpectedArrivalAt`; supplier return uses `RequestedDeliveryDate`, both in `[start,end)`. | `CompletedAt` in window. | Open: not posted. Overdue uses arrival before `AsOfAt` for customer return and delivery before day start for supplier return. | `/Vouchers` | `VERIFIED`; disposition-stage analytics remain incomplete. |

`CompletionRate = min(100, CompletedToday / DueToday × 100)`, rounded to one decimal; when `DueToday = 0`, the displayed rate is `0.0%` rather than an undefined value.

## Permission And Financial Data

- Admin, Manager and legacy Staff see all six processes and actionable work.
- Inbound, Outbound, Inventory and Transport roles receive only their applicable processes/actions; ReportViewer receives read-only actions; Viewer receives no actionable Command Center process rows.
- Financial inventory value is part of the existing dashboard below the Command Center and remains guarded by Admin or `report.view.financial`. The Command Center itself does not select or render price, cost or currency.

## Reconciliation Evidence And Open Gaps

- Hand-calculated fixture: `DashboardCommandCenterTests.BuildAsync_ShouldReconcileAllProcessSummariesWithHandCalculatedFixture`.
- Day boundary and no-double-count fixture: `BuildAsync_ShouldUseExclusiveEndOfBusinessDayAndAvoidDoubleCounting`.
- Scope and filter fixtures: `BuildAsync_ShouldApplyWarehouseAndOwnerScopeAndSortBySeverity` and `BuildAsync_ShouldApplyReproducibleQueueFiltersWithoutChangingRawCounters`.
- Evidence: `artifacts/dashboard-command-center/DASHBOARD_DATA_RECONCILIATION.md` and current TRX reports.
- `PENDING/BLOCKED`: business-owner formula sign-off, multi-event partial receipt, in-transit transfer/discrepancy, owner lineage on persisted exceptions, labor/capacity/trend KPIs and staging-scale query-plan validation.
