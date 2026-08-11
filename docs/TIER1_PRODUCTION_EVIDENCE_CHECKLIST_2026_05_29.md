# Tier-1 Production Evidence Checklist - 2026-06-01

Scope: bằng chứng cần có trước khi WMS Pro được gọi là sẵn sàng production theo chuẩn Tier-1 WMS. Tài liệu này không chứa mật khẩu, API key, cookie, connection string, customer data hoặc secret.

## 0. Repo/Local Evidence Snapshot

| Evidence | Command / proof | Status | Date | Owner | Artifact |
|---|---|---|---|---|---|
| Build | `dotnet build WMS.sln --no-restore -v:minimal` | Pass, 0 warnings / 0 errors | 2026-06-01 | Codex local gate | `artifacts/tier1-evidence/dotnet-build.log` |
| .NET tests | `dotnet test WMS.Tests\WMS.Tests.csproj --no-build` | Pass, 697/697 | 2026-07-07 | Codex local gate | `FINAL_WMS_ENTERPRISE_QA_REPORT.md` |
| Vulnerability scan | `dotnet list WMS.sln package --vulnerable --include-transitive` and `npm audit --json` | Pass, no vulnerable packages | 2026-07-01 | Codex local gate | `FINAL_WMS_ENTERPRISE_QA_REPORT.md` |
| Visual main | `WMS_BASE_URL=<local-dev-url> npm run visual:test` | Pass: 194 passed / 66 skipped; no-device 10/10; mobile-deep 420/420 | 2026-07-07 | Codex local gate | `FINAL_WMS_ENTERPRISE_QA_REPORT.md` |
| Runtime seed safety | `SeedData` action and startup seed registration absent | Pass | 2026-06-01 | Codex local gate | Unit tests |
| Data quality audit service | `scripts\Invoke-WmsDataQualityAudit.ps1` read-only against DB hosting connection in `launchSettings` | Pass, 0 issue rows across 17 issue groups | 2026-07-05 | Codex local gate | `artifacts/data-quality/wms-data-quality-audit-20260705-130335.txt` |
| Tier-1 evidence gate | `.\scripts\Invoke-Tier1EvidenceGate.ps1` | Blocked only by external/data-quality/DR evidence, local gates pass | 2026-06-01 | Release owner | `artifacts/tier1-evidence/tier1-evidence-manifest.json` |

Visual sidecar status artifacts remain part of the local evidence set: `artifacts/visual-public/test-results/.last-run.json`, `artifacts/visual-no-device/test-results/.last-run.json` and `artifacts/visual-mobile-deep/test-results/.last-run.json`.

Repo/local readiness hiện đạt mức cao nhất đã chứng minh bằng gate local trong `FINAL_WMS_ENTERPRISE_QA_REPORT.md`; không ghi production hoàn hảo khi còn thiếu load k6 trên máy hiện tại và các bằng chứng external. Production Tier-1 phải giữ dưới mức hoàn hảo cho đến khi mọi evidence ID bên dưới có ngày chạy thật, owner và artifact đã redact.

## 1. Real Device Evidence

| Evidence ID | Required proof | Status | Date | Owner | Artifact rule |
|---|---|---|---|---|---|
| HW-RF-001 | RF handheld receiving: login, scan ASN/voucher, item, location, lot, expiry, quantity and complete receive. | Blocked | Pending device run | Warehouse ops | `artifacts/production-evidence/**/HW-RF-001*` |
| HW-RF-002 | RF handheld picking: scan task, source location, item/package, quantity and wrong-code rejection. | Blocked | Pending device run | Warehouse ops | `artifacts/production-evidence/**/HW-RF-002*` |
| HW-RF-003 | RF handheld movement: scan source, LPN/item, destination and warehouse-scope validation. | Blocked | Pending device run | Warehouse ops | `artifacts/production-evidence/**/HW-RF-003*` |
| HW-SCAN-001 | USB scanner keyboard-wedge scans populate only the focused field and reject malformed barcodes. | Blocked | Pending device run | Warehouse ops | `artifacts/production-evidence/**/HW-SCAN-001*` |
| HW-SCAN-002 | Bluetooth scanner covers USB criteria plus reconnect after device sleep. | Blocked | Pending device run | Warehouse ops | `artifacts/production-evidence/**/HW-SCAN-002*` |
| HW-SCAN-003 | Mobile camera scan opens, scans, closes, restores focus and does not cover primary actions. | Blocked | Pending device run | Warehouse ops | `artifacts/production-evidence/**/HW-SCAN-003*` |
| HW-PRINT-001 | Label printer prints item/customer/shipping labels with readable barcode and correct page size. | Blocked | Pending printer run | Warehouse ops | `artifacts/production-evidence/**/HW-PRINT-001*` |
| HW-PRINT-002 | Document printer prints voucher, handover and manifest with correct page breaks/signatures. | Blocked | Pending printer run | Warehouse ops | `artifacts/production-evidence/**/HW-PRINT-002*` |

## 2. Load And Staging Evidence

| Evidence ID | Required proof | Status | Date | Owner | Artifact rule |
|---|---|---|---|---|---|
| LOAD-001 | Authenticated k6 summary against staging account/state. | Blocked | Pending staging run | Release owner | `artifacts/production-evidence/**/LOAD-001*` |
| LOAD-002 | p95 latency, error rate and failed checks meet agreed release thresholds. | Blocked | Pending threshold sign-off | Release owner | `artifacts/production-evidence/**/LOAD-002*` |
| LOAD-003 | Write-heavy load uses disposable staging seed data, never production data. | Blocked | Pending staging seed proof | Release owner | `artifacts/production-evidence/**/LOAD-003*` |
| LOAD-004 | Soak test captures queue depth, DB latency and outbox/dead-letter behavior over time. | Blocked | Pending soak run | SRE owner | `artifacts/production-evidence/**/LOAD-004*` |

## 3. Backup, Restore, HA And DR Evidence

| Evidence ID | Required proof | Status | Date | Owner | Artifact rule |
|---|---|---|---|---|---|
| DR-001 | Restore latest backup into staging and run health/login/inventory/voucher/report/export smoke. | Blocked | Pending restore drill | SRE owner | `artifacts/production-evidence/**/DR-001*` |
| DR-002 | Row-count validation for inventory, vouchers, users, audit logs, migrations and outbox tables. | Blocked | Pending restore drill | SRE owner | `artifacts/production-evidence/**/DR-002*` |
| DR-003 | Record actual RPO/RTO with approver sign-off. | Blocked | Pending DR drill | SRE owner | `artifacts/production-evidence/**/DR-003*` |
| DR-004 | App/database failover tested with user impact and recovery steps. | Blocked | Pending HA drill | SRE owner | `artifacts/production-evidence/**/DR-004*` |
| DR-005 | Release rollback tested without data loss or wrong stock movement. | Blocked | Pending rollback drill | Release owner | `artifacts/production-evidence/**/DR-005*` |

## 4. Certified Integration Evidence

| Evidence ID | System family | Required proof | Status | Artifact rule |
|---|---|---|---|---|
| INT-ERP-001 | ERP | Item, UOM, supplier/customer, PO, SO, inventory transaction and reconciliation payloads with auth/scope/idempotency/retry/dead-letter/rollback. | Blocked | `artifacts/production-evidence/**/INT-ERP-001*` |
| INT-TMS-001 | TMS | Load, route, shipment, tracking and delivery reconciliation payloads with stock-safe failure handling. | Blocked | `artifacts/production-evidence/**/INT-TMS-001*` |
| INT-OMS-001 | OMS | Order import, allocation, partial fulfillment, cancellation and backorder with duplicate/late-cancel audit. | Blocked | `artifacts/production-evidence/**/INT-OMS-001*` |
| INT-MHE-001 | MHE/WCS | Induction, pick confirmation, pack confirmation, exception and heartbeat with retry/dead-letter/operator recovery. | Blocked | `artifacts/production-evidence/**/INT-MHE-001*` |
| INT-CAR-001 | Carrier | Rate, label, manifest, tracking and delivery exception with traceability to voucher/package/load. | Blocked | `artifacts/production-evidence/**/INT-CAR-001*` |

## 5. Monitoring And Runbook Evidence

| Evidence ID | Required proof | Status | Date | Owner | Artifact rule |
|---|---|---|---|---|---|
| OBS-001 | Correlation ID traces user request, background job, integration delivery and audit record end-to-end. | Blocked | Pending staging/prod telemetry | SRE owner | `artifacts/production-evidence/**/OBS-001*` |
| OBS-002 | Dashboard has request count, 4xx/5xx, p95/p99 latency and slow endpoints. | Blocked | Pending telemetry proof | SRE owner | `artifacts/production-evidence/**/OBS-002*` |
| OBS-003 | Scan queue, outbox, webhook and carrier connector queue depth are visible and alerted. | Blocked | Pending telemetry proof | SRE owner | `artifacts/production-evidence/**/OBS-003*` |
| OBS-004 | Dead-letter creation, replay and audit are tested. | Blocked | Pending runbook drill | SRE owner | `artifacts/production-evidence/**/OBS-004*` |
| OBS-005 | SEV1/SEV2 alerts reach on-call channel and incident owner. | Blocked | Pending alert drill | SRE owner | `artifacts/production-evidence/**/OBS-005*` |
| OBS-006 | Operator follows runbook for health, backup, integration outage and rollback. | Blocked | Pending runbook drill | SRE owner | `artifacts/production-evidence/**/OBS-006*` |

## 6. Sign-Off Rule

- `Pass`: artifact exists, is redacted, dated, reproducible and signed off by the responsible owner.
- `Blocked`: dependency outside local repo is missing, such as real device, staging account, backup permission, production telemetry or integration partner contract.
- `Failed`: evidence ran and found a defect or unsafe condition.
- Production Tier-1 can be marked **100%** only when every local gate and every external evidence ID is `Pass`.
- Current honest score remains **96/100 repo/local readiness by current evidence** and **89-91% Tier-1 production equivalence** until k6/local load evidence and all blocked external IDs pass.
