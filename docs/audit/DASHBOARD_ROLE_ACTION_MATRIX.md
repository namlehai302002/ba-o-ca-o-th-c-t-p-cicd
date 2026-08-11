# Dashboard Role Action Matrix

This is the implemented Gate 7 Command Center contract traced from `WmsRoles`, `HomeController` and `DashboardCommandCenterService`. “Act” means the queue exposes an operational action label; the destination endpoint still enforces its own role/policy.

| Command Center capability | Admin | Manager | Staff | Inbound | Outbound | Inventory | Transport | ReportViewer | Viewer |
|---|---|---|---|---|---|---|---|---|---|
| Open authenticated dashboard | Allow | Allow | Allow | Allow | Allow | Allow | Allow | Allow | Allow |
| Inbound summary/work | Act | Act | Act | Act | Hidden | Hidden | Hidden | Read-only | Hidden |
| Outbound summary/work | Act | Act | Act | Hidden | Act | Hidden | Act | Read-only | Hidden |
| Pick-task rows | Act | Act | Act | Hidden | Act | Hidden | Read-only | Read-only | Hidden |
| Movement/replenishment | Act | Act | Act | Hidden | Hidden | Act | Hidden | Read-only | Hidden |
| Count/quality | Act | Act | Act | Hidden | Hidden | Act | Hidden | Read-only | Hidden |
| Transfer | Act | Act | Act | Hidden | Hidden | Act | Hidden | Read-only | Hidden |
| Return | Act | Act | Act | Act | Act | Hidden | Hidden | Read-only | Hidden |
| Persisted exception rows without owner scope | Act | Act | Act | Hidden | Hidden | Act | Hidden | Read-only | Hidden |
| Owner-scoped persisted exception rows | Hidden fail-closed when owner claim exists | Hidden fail-closed | Hidden fail-closed | Hidden | Hidden | Hidden fail-closed | Hidden | Hidden fail-closed | Hidden |
| Financial value in existing dashboard | Allow | Permission claim | Permission claim | Permission claim | Permission claim | Permission claim | Permission claim | Permission claim | Deny without claim |

## Scope Rules

- Non-admin users require a warehouse assignment at login.
- Owner-scoped claims must constrain dashboard aggregates, detail routes, exports and cache keys consistently.
- A hidden card/menu is not authorization; backend direct-route tests are mandatory.
- Admin override is allowed but must remain auditable.
- A claim-locked warehouse overrides the `warehouseId` query parameter. Owner filtering is applied in EF queries before projection.
- `OperationExceptionCase` currently has no `OwnerPartnerId`; these rows are deliberately omitted for owner-scoped sessions to prevent cross-owner disclosure.

## Evidence Status

- `PASS`: nine-role service matrix in `DashboardCommandCenterTests.BuildAsync_ShouldExposeOnlyProcessesAssignedToRole`.
- `PASS`: ReportViewer action is read-only in `BuildAsync_ReportViewerShouldReceiveReadOnlyActions`.
- `PASS`: warehouse/owner isolation and fail-closed exception behavior in `BuildAsync_ShouldApplyWarehouseAndOwnerScopeAndSortBySeverity`.
- `PASS`: current authenticated Admin browser path on four viewports in `artifacts/dashboard-command-center/playwright-report.json`.
- `BLOCKED`: browser UAT for the remaining roles because the existing role storage states expired and no approved `AUDIT_TEST_` role-fixture password/run ID is present. The test run is not replaced with hosting writes.
- `BLOCKED`: owner/partner browser role is not defined in `WmsRoles`; applicability and business ownership require confirmation before adding a role.
