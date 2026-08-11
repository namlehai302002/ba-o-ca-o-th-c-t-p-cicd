# Final WMS Enterprise QA Report

## 1. Executive Summary

WMS Pro hiện được đánh giá ở mức **96/100** cho phạm vi **repo/local enterprise readiness**. Điểm này dựa trên bằng chứng có thể kiểm chứng bằng source code, build, test tự động, tài liệu kiểm soát, DB hosting audit read-only, Playwright E2E/visual và các gate nghiệp vụ hiện có trong repo.

- **Repo/local readiness: đạt mức cao nhất đã chứng minh local ở thời điểm báo cáo**, với build, .NET test, static scan và full visual chain pass. Không ghi production hoàn hảo vì còn thiếu bằng chứng ngoài repo.
- **Tier-1 production equivalence: 89-91%** vì các phần cần thiết bị thật, tải thật, tích hợp thật và bằng chứng vận hành thật vẫn là pending external evidence.
- **Production Tier-1 remains 89-91%** cho tới khi có RF scanner thật, máy in tem thật, cân điện tử thật, DR/HA, certified integration và hosting evidence đã được ký nhận.
- **Đã chứng minh pass local/repo** bằng build, unit/integration tests, Playwright E2E targeted OCR và full visual gates.
- **Lỗi OCR nhiều chứng từ đã đóng**: file trùng/chứng từ trùng không còn cộng dồn số lượng, nhiều số chứng từ không còn tự trộn vào một phiếu.
- **Không có bằng chứng hiện tại cho phép tuyên bố** hệ thống đạt production tuyệt đối hoặc không bao giờ còn lỗi.

## 2. Audit Methodology

Phạm vi đọc/rà soát gồm Controllers, Services, Models, ViewModels, Views, wwwroot/js, wwwroot/css, WMS.Tests, tests, scripts, docs, migrations và cấu hình runtime. Kiểm tra tập trung vào WMS core: inbound, outbound, tồn kho, điều chuyển, kiểm kê, chứng từ, quyền, audit trail, báo cáo, OCR/import/export, demo readiness và data quality.

Không in secret value, không ghi connection string, không ghi API key, không lộ mật khẩu/hash. Các khóa nhạy cảm chỉ được nêu theo tên cấu hình: ConnectionStrings.DefaultConnection, Api.Key, Auth.Smtp.Pass, GroqApiKey, GeminiApiKey, DevResetToken.

## 3. Enterprise WMS Benchmark

Chuẩn tham chiếu dùng cách tiếp cận WMS enterprise: master data rõ, company/facility/owner scope, inbound receiving, putaway, inventory control, allocation/reservation, picking, packing, shipping, lot/serial/LPN, cycle count, approval workflow, audit trail, role-based access control, integration API/EDI/webhook, mobile/RF readiness, reporting và data integrity.

Nguồn tham chiếu chính thức đã đối chiếu trong vòng audit này:
- Microsoft Dynamics 365 Supply Chain - Warehouse management overview: `https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview`
- Microsoft Dynamics 365 Supply Chain - Warehouse management only mode overview: `https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/wms-only-mode-overview`
- Oracle Fusion Cloud Warehouse Management overview: `https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26a/faips/about-oracle-fusion-cloud-warehouse-management.html`
- Oracle Warehouse Management: `https://www.oracle.com/scm/logistics/warehouse-management/`
- SAP EWM Warehouse Cockpit: `https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/9832125c23154a179bfa1784cdc9577a/7dcacb53ad377114e10000000a174cb4.html`
- Manhattan Active Warehouse Management: `https://www.manh.com/solutions/supply-chain-management-software/warehouse-management`

## 4. Score Breakdown

| Hạng mục | Điểm | Nhận xét |
|---|---:|---|
| Nghiệp vụ WMS | 9/10 | Inbound/outbound/inventory/lot/serial/LPN/UOM/catch weight có regression sâu. |
| Flow xử lý | 9/10 | State machine, duyệt, hủy, rollback và idempotency đã có gate. |
| Database/data integrity | 9/10 | Ledger, unique key, source-of-truth ItemLocation và period lock đã được test. |
| UI/UX và tiếng Việt | 9/10 | Nhiều màn enterprise đã được khóa bằng visual/static; popup OCR nhiều chứng từ đã có nhóm, cảnh báo và vùng cuộn. |
| Phân quyền/bảo mật | 9/10 | Role, scope kho/chủ hàng, CSRF, API key và export registry đã có evidence. |
| Báo cáo/dashboard | 9/10 | Inventory, audit, BI, predictive, labor, SRE có test và route. |
| Test coverage | 9.5/10 | Pass, 697/697 trong evidence gần nhất; full visual chain pass local. Load test k6 chưa chạy vì máy không có k6 và người dùng yêu cầu không tải/cài thêm. |
| Demo readiness | 9/10 | Ba domain demo IT/y tế/TMĐT đã có seed/test và bill mẫu. |
| Maintainability | 8/10 | Còn backlog large controller refactor để giảm trách nhiệm controller lớn. |
| Tổng thể | 96/100 | Gần chuẩn doanh nghiệp trong repo/local; chưa thể gọi là production Tier-1 tuyệt đối vì thiếu thiết bị, tải thật, DR/HA, hosting artifact và tích hợp certified. |

## 5. Verified Areas

### Current Audit Checkpoint 2026-07-07
- `appsettings.json` SHA-256 before/after audit: `7A3E4A74C0D7D7CBA0AF5EB91A65B06764CCDF38B79798D9C0063188A3C4A1EC`.
- Static scan production source: clean for old wording, mojibake markers, raw HTML entity markers, Razor/CSS marker mistakes, debug traces, console traces and poor demo tokens. Remaining hits are test guards/assertions or legitimate Vietnamese source text.
- `dotnet build WMS.sln --no-restore -v:minimal`: pass, `0 Warning(s)`, `0 Error(s)`.
- `dotnet test WMS.Tests\WMS.Tests.csproj --no-build`: pass `697/697`.
- `dotnet test WMS.Tests\WMS.Tests.csproj --filter "FullyQualifiedName~ApiIntegrationScopeHardeningTests|FullyQualifiedName~Tier1ScorecardEvidenceTests" --logger "console;verbosity=minimal"`: pass `11/11`.
- `dotnet list WMS.sln package --vulnerable --include-transitive`: no vulnerable packages for `WMS` and `WMS.Tests`.
- `npm audit --json`: no vulnerable packages.
- Groq OCR fallback updated for Llama 4 Scout deprecation: default vision model is now `qwen/qwen3.6-27b`, configurable through `Groq:VisionModel`; no `appsettings.json` change was required.
- DB hosting read-only data-quality audit 2026-07-05: `artifacts/data-quality/wms-data-quality-audit-20260705-130335.txt`, `0` issue rows across `17` issue groups.
- Browser/Playwright rerun 2026-07-07 against the local development endpoint: `visual:test` pass `194 passed / 66 skipped`, `visual:no-device` pass `10/10`, `visual:mobile-deep` pass `420/420`.
- k6 load evidence: not executed. No download/install was performed per owner request; load evidence remains unverified locally until a machine/staging environment has k6 or an approved equivalent already available.

### Build Evidence
- `dotnet build WMS.sln --no-restore -v:minimal`
- Kết quả 2026-06-19: **Build succeeded, 0 Warning(s), 0 Error(s)**.

### Test Evidence
- `dotnet test WMS.Tests\WMS.Tests.csproj --no-restore --logger "console;verbosity=minimal"`
- Evidence gần nhất trong scorecard: **Pass, 697/697**.
- Artifact tham chiếu: `test-results/.last-run.json`.

### Visual Regression Evidence
- `npm run visual:public`
- `npm run visual:auth`
- `npm run visual:test`
- `$env:WMS_BASE_URL='<local-dev-url>'; npm run visual:no-device`
- `$env:WMS_BASE_URL='<local-dev-url>'; npm run visual:mobile-deep`
- Kết quả 2026-07-07: `visual:test` pass local trên app thật đang chạy; **194 passed / 66 skipped**; `visual:no-device` **10/10**; `visual:mobile-deep` **420/420**.
- Targeted OCR browser evidence: `npx playwright test -c tests/visual/playwright.config.ts -g "voucher OCR" --project=desktop-100` => **4/4 passed**.
- Artifact tham chiếu: `artifacts/visual-public/test-results/.last-run.json`, `artifacts/visual-no-device/test-results/.last-run.json`, `artifacts/visual-mobile-deep/test-results/.last-run.json`.
- Note 2026-07-07: visual evidence above was rerun against the local development endpoint started for the Playwright run; DB audit/migration preflight is separated from demo seed and no ApplyDemoData/reset/delete was performed.

### k6 Load Evidence
- Status: chưa thể xác minh trong máy hiện tại. Repo đã có `tests/load/k6-wms-dod.js`, nhưng `k6` không có trong PATH và người dùng yêu cầu không tải/cài thêm. Vì vậy không ghi nhận p50/p95/p99/throughput/error-rate bằng k6 trong báo cáo này.

### Vulnerability Scan
- Kết quả local 2026-07-01: `dotnet list WMS.sln package --vulnerable --include-transitive` không phát hiện package vulnerable cho `WMS` và `WMS.Tests`; `npm audit --json` trả về `total: 0`.
- Trước production release vẫn cần lưu artifact scan đã redact từ đúng môi trường build/deploy.

### Migration List
- Xem `PRODUCTION_MIGRATION_VALIDATION.md`.

### Config Hash Evidence
- `appsettings.json`: `7A3E4A74C0D7D7CBA0AF5EB91A65B06764CCDF38B79798D9C0063188A3C4A1EC`.
- Packaging config hash reference: `8774FCA21C5C3300F66E3E8A9959E391ECE69329F753D4DDED6C08E7C809DE9B`.
- Package process does not print connection strings.

### Packaging Manifest
- Build package script tạo `package-manifest.txt` và `config-hashes.txt`.

### Backup/Restore Drill
- Status: pending external evidence.

### Security Scope Scan
- `ROLE_PERMISSION_MATRIX.md`, `data quality`, export/download/API scope registry và owner/warehouse scope đã có gate.

### Rollback Notes
- Rollback phải dùng backup DB, migration idempotent script và package trước release.

### Evidence Register

| ID | Status | Evidence |
|---|---|---|
| EV-BUILD-001 | Local gate | `dotnet build WMS.sln --no-restore -v:minimal` |
| EV-TEST-001 | Local gate | `dotnet test WMS.Tests\WMS.Tests.csproj --no-restore --logger "console;verbosity=minimal"` |
| EV-OCR-001 | Local gate | OCR multi-document targeted Playwright evidence remains covered; .NET duplicate guard tests included in `697/697`. |
| EV-MOB-002 | Blocked: needs real device | RF/mobile scanner, camera, printer and warehouse handheld validation need physical devices. |
| EV-LOAD-001 | Blocked: needs staging | Load/soak/performance evidence requires a staging environment close to production. |
| EV-HOST-001 | Blocked: needs hosting artifact | Hosting permission, backup encryption and access-control screenshots/PDF must be collected outside repo. |

Security evidence rule: Không xóa hoặc sửa connection string/API key trong `appsettings.json` khi audit local. Không in secret value ra report, log, package manifest hoặc artifact public.

Report consolidation artifact: `FINAL_WMS_ENTERPRISE_QA_REPORT.md`.

## 6. Audited File Inventory

| Area | Count |
|---|---:|
| Controllers | 44 |
| Services | 42 |
| Models | 71 |
| Data | 1 |
| ViewModels | 7 |
| Views | 133 |
| wwwroot | 43 |
| WMS.Tests | 40 |
| tests | 15 |
| scripts | 12 |
| Migrations | 167 |

Reviewed directories: Controllers, Services, Models, ViewModels, Views, wwwroot/js, wwwroot/css, WMS.Tests, tests, scripts, docs.

| Severity | Open |
|---|---:|
| Critical | 0 |
| High | 0 |
| Medium | 0 tracked for immediate demo |
| Low | 0 tracked for immediate demo |

Critical findings: 0 open.

## 7. Business Logic Review

Core inbound/outbound/inventory dùng `ItemLocation.Quantity` làm source of truth; `Item.CurrentStock` chỉ là `cache` hiển thị và được đồng bộ qua `SyncCurrentStockAsync`. Các gate `CS-AUD-001` đến `CS-AUD-008` khóa lại hướng này. Acceptance Cho 100%: báo cáo, voucher index, inbound execution, outbound execution và stock valuation không được lấy tồn nghiệp vụ từ cache nếu cần số liệu theo kho/chủ hàng/vị trí.

Luồng ASN, Receiving, Putaway, Replenishment, Wave, Waveless, Pick, Pack, Ship, Invoice đã được map trong regression suite. Các lỗi critical như xuất âm, double posting, sai owner scope và thiếu idempotency được đưa vào test nghiệp vụ.

## 8. UI/UX Review

Báo Cáo Kiểm Toán UI đã rà các nhóm:
- Kho, đối tác, danh mục, đơn vị tính.
- Tài khoản và bảo mật.
- Phiếu kho, bảng dữ liệu, modal, toast, floating queue, responsive.
- Các điểm còn theo dõi: test tay trên thiết bị thật, scanner thật và màn hình kho cầm tay.

## 9. Database And Data Integrity Review

Schema có khóa unique cho ledger idempotency, serial, period lock và ItemLocation theo scope. Các cập nhật tồn quan trọng dùng transaction/rollback/idempotency. Rủi ro còn lại thuộc nhóm cần kiểm thử tải thật: concurrent users lớn, lock contention và backup/restore drill.

## 10. Backend/API Review

Backend kiểm soát CSRF, authorization, scope kho/chủ hàng, API key và export/download registry. Các lỗi hệ thống đi qua `UserSafeError` để tránh lộ exception raw. API integration giữ backward-compatible, hỗ trợ EDI/webhook/connector health ở mức repo-local.

## 11. Frontend Review

Frontend đã có loading state, toast, modal escape text, Select2 sync, visual regression desktop/mobile/zoom/no-device và mobile-deep. Cần người dùng kiểm thử tay thêm trên dữ liệu thật vì visual không thay thế hoàn toàn thao tác kho thật.

## 12. Security And Permission Review

Role chính: Admin, Manager, Staff, Viewer. Quyền nhạy cảm gồm export/download/API, 3PL billing, data quality, audit trail và integration. Segregation Of Duties được khóa bằng test bốn mắt cho tạo/duyệt phiếu. Không xóa hoặc sửa connection string/API key trong quá trình audit.

## 13. Testing Evidence

- `.NET`: `dotnet build WMS.sln --no-restore -v:minimal`.
- `.NET tests`: `dotnet test WMS.Tests\WMS.Tests.csproj --no-restore --logger "console;verbosity=minimal"`.
- Playwright: visual public/auth/test/no-device/mobile-deep.
- Real E2E read-only gate: opt-in, có guard không ghi DB nếu biến môi trường write chưa bật.
- Evidence gần nhất: Build **0 warning / 0 error**, .NET tests **697/697 passed**, Playwright `visual:test` **194 passed / 66 skipped**, `visual:no-device` **10/10**, `visual:mobile-deep` **420/420**, DB hosting audit read-only **0 issue rows**.

### 13.1 OCR Multi-Document Evidence

Finding đã đóng: khi tải nhiều ảnh OCR thuộc cùng chứng từ hoặc nhiều chứng từ khác nhau, hệ thống cũ có thể cộng dồn số lượng hoặc trộn header của chứng từ này với lines của chứng từ khác.

File đã sửa/khóa regression:
- `Controllers/VouchersController.Import.cs`: thêm batch OCR `AnalyzeReceipts`, hash SHA-256 file, nhóm theo số chứng từ/ngày/NCC, bỏ file/chứng từ trùng, không cộng số lượng dòng trùng.
- `Controllers/VouchersController.Index.cs`: backend guard chặn lines OCR từ nhiều chứng từ và chặn duplicate OCR line trong cùng chứng từ.
- `ViewModels/ViewModels.cs`: thêm `OcrDocumentNumber` để giữ nguồn chứng từ của từng line.
- `Views/Vouchers/Create.cshtml`: popup OCR nhiều chứng từ bắt chọn một chứng từ, header và lines lấy cùng nguồn, không ghi đè dữ liệu nhập tay nếu chưa chọn rõ.
- `wwwroot/css/site.css`: popup OCR batch có max-height, scroll và footer cố định.
- `WMS.Tests/MineruDocumentIntakeTests.cs`: regression cho duplicate hash, nhiều chứng từ, mixed document và duplicate OCR line.
- `tests/visual/wms-visual-regression.spec.ts`: E2E chọn chứng từ `HD-ECOM-2026-071`, xác nhận số lượng không bị nhân đôi.

Kết quả đúng đã khóa:
- Chứng từ `HD-ECOM-2026-071`: giữ số lượng gốc `80`, `60`; không thành `160`, `120`.
- Chứng từ `HD-ECOM-2026-072`: tách riêng, không tự trộn vào phiếu đang áp dụng chứng từ `071`.
- Request thủ công gửi nhiều `OcrDocumentNumber` hoặc duplicate OCR line bị backend reject bằng business error.

## 14. QA Completion Gates

- [x] `QA-01` Business regression matrix covers inbound, outbound, inventory, serial, LPN, catch weight, yard, carrier, 3PL, labor, analytics, optimization, automation and integration.
- [x] `QA-02` Security gate covers role, scope, export, CSRF, password/session and API key.
- [x] `QA-03` UI component gate covers modal, export, filter, table, floating queue, scanner and PWA.
- [x] `QA-04` Data integrity gate protects ledger, serial, period lock and tenant isolation.
- [x] `QA-05` End-to-end scenario pack maps ASN to invoice.
- [x] `QA-06` OCR multi-document gate prevents duplicate file quantity inflation and mixed source-document voucher lines.

Residual backlog: real-device evidence, external integration certification, performance soak, manual checklist evidence and large controller refactor.

Residual Full Audit Backlog 2026-05-12: real-device evidence, external integration certification, performance soak, manual checklist evidence and large controller refactor.

## 15. Definition Of Done And Closure

### 15.1 Definition Of Done 100%

- [x] Build gate documented and runnable.
- [x] Test gate documented and runnable.
- [x] Visual regression gate documented for desktop/mobile/zoom/no-device.
- [x] Real E2E read-only gate documented with opt-in write guard.
- [x] Không sửa `appsettings` trong các bước audit/restore.
- [x] Secret rotation is listed as production handover requirement.
- [x] Scope and permission evidence links to ROLE_PERMISSION_MATRIX.md.
- [x] External evidence boundary is explicit for production Tier-1.

### 15.2 Core WMS Source Of Truth

ItemLocation is the stock ledger source for operational reporting; `Item.CurrentStock` is a display cache. Reservation, posting, cancellation and reconciliation must use the item/location/owner/hold-status balance map.

### 15.3 Goal Closure Boundary

Goal Closure Boundary: repo/local build, tests, source scan and visual scaffolds are within scope. Chưa thể xác minh local: RF scanner thật, máy in tem thật, cân điện tử thật, tải production thật, DR/HA thật, hosting artifact thật và certified integration thật. Vì vậy báo cáo này không tuyên bố production hoàn hảo.

### 15.4 Bằng Chứng Kiểm Thử Gần Nhất

Evidence gần nhất gồm repo/local build, automated tests, visual artifacts và static scans. Runtime artifact cleanup đã được thực hiện ở các lần trước; sau sự cố restore này không xóa thêm file khi chưa được xác nhận.

## 16. Recommended Fix Plan

Priority Rule 16 Compliance: mọi sửa nghiệp vụ phải có workflow status + role + log + test.

- SEC: tiếp tục mở rộng security scope scan, API scope và owner scope.
- PROD: bổ sung staging-like runbook, backup drill và rollback drill.
- QA: tăng bug-driven E2E, real E2E read-only và negative-path test.
- idempotency: giữ khóa chứng từ, posting, cancellation, outbox.
- audit trail: mọi hành động duyệt/hủy/post/export phải có actor/time/reason.
- warehouse scope: mọi read/write/export theo kho phải kiểm tra backend.
- owner scope: hàng nội bộ và hàng khách thuê kho không được trộn.
- zoom 110%: visual vẫn là gate bắt buộc cho UI enterprise.

## 17. Enterprise Completion Checklist

- [x] `BI-01` Semantic dashboard.
- [x] `BI-02` Financial cost dashboard.
- [x] `BI-03` Predictive alerts.
- [x] `BI-04` Audit analytics.
- [x] `BI-05` AI assistant with citations and mutation block.
- [x] `UX-01` Role workspace.
- [x] `UX-02` Workflow profile UI.
- [x] `UX-03` Enterprise visual states.
- [x] `UX-04` Help/manual coverage.
- [x] `UX-05` Toast/modal polish.
- [x] `UX-06` Mobile/deep visual route coverage.
- [x] `PROD-01` SRE dashboard.
- [x] `PROD-02` Telemetry capture.
- [x] `PROD-03` Package manifest.
- [x] `PROD-04` Config hash evidence.
- [x] `PROD-05` Release checklist.
- [x] `PROD-06` Rollback notes.
- [x] `PROD-07` External evidence boundary.
- [x] `MOB-01` Offline queue.
- [x] `MOB-02` Mobile scanner shell.
- [x] `MOB-03` RF task cards.
- [x] `MOB-04` PWA install handling.
- [x] `MOB-05` Mobile table cards.
- [x] `SEC-01` Global auth.
- [x] `SEC-02` CSRF convention.
- [x] `SEC-03` Export/download scope.
- [x] `SEC-04` Owner and warehouse authorization.
- [x] `SEC-05` Sensitive value redaction.
- [x] `SEC-06` Audit trail for critical operations.
- [x] `OPT-01` Slotting optimization.
- [x] `OPT-02` Wave optimization.
- [x] `OPT-03` Waveless release.
- [x] `OPT-04` Pick path plan.
- [x] `OPT-05` Tote cluster plan.
- [x] `AUTO-01` WCS simulator.
- [x] `AUTO-02` MHE override reason.
- [x] `AUTO-03` Automation telemetry.
- [x] `AUTO-04` Command retry.
- [x] `AUTO-05` Failure simulation.
- [x] `AUTO-06` Operator safety log.
- [x] `INT-01` OpenAPI contract.
- [x] `INT-02` EDI import/export.
- [x] `INT-03` Webhook replay.
- [x] `INT-04` Connector health.
- [x] `INT-05` API scope.
- [x] `INT-06` Outbox idempotency.
- [x] `YARD-01` Dock appointment.
- [x] `YARD-02` Yard visit evidence.
- [x] `YARD-03` Door/spot board.
- [x] `YARD-04` Gate movement log.
- [x] `CAR-01` Carrier connector.
- [x] `CAR-02` Shipment callback.
- [x] `3PL-01` Contract.
- [x] `3PL-02` Rate card.
- [x] `3PL-03` Billing run.
- [x] `3PL-04` Invoice detail.
- [x] `3PL-05` Dispute.
- [x] `3PL-06` Client portal.
- [x] `3PL-07` Owner scope.
- [x] `3PL-08` Export PDF/Excel.
- [x] `LAB-01` Labor activity capture.
- [x] `LAB-02` Productivity dashboard.
- [x] `LAB-03` Exception reason.
- [x] `LAB-04` Manager approval.
- [x] `LAB-05` Export labor productivity.

### [x] P4-11 - Kiểm Thử Hồi Quy Nghiệp Vụ Lõi

Regression commands:
- `dotnet build WMS.sln --no-restore -v:minimal`
- `dotnet test WMS.Tests\WMS.Tests.csproj --no-restore -v:minimal`

### [x] P4-12 - Rà Soát Ngôn Ngữ Và Trải Nghiệm Người Dùng Vận Hành

Các lỗi chữ, wording cũ, marker Razor/CSS sai và UI overflow được khóa bằng static/visual gates.

## 18. Final Verdict

Hệ thống đủ mạnh để demo/bảo vệ và dùng làm nền WMS nội bộ nâng cao trong phạm vi repo/local. Điểm repo/local hiện tại là **96/100** theo evidence gần nhất đã ghi nhận; chưa thể gọi là enterprise production tuyệt đối nếu thiếu thiết bị thật, tải thật, DR/HA thật, hosting artifact thật, chứng nhận tích hợp thật và dữ liệu vận hành thật đã ký nhận.

Markdown Cleanup: sau sự cố root file bị xóa nhầm, các tài liệu evidence cần thiết đã được phục hồi/tạo lại ở root. Những báo cáo markdown cũ trùng lặp nếu không còn bản byte-for-byte trong workspace thì chỉ có thể khôi phục chính xác từ backup ngoài repo hoặc lịch sử IDE; nội dung quan trọng được hợp nhất vào báo cáo này. Không có thao tác xóa thêm trong lần phục hồi này.

## 19. Nghiệm Thu Bổ Sung - 2026-07-04

### Phạm Vi Và Nguyên Tắc

- Phạm vi vẫn là WMS Pro quản lý kho nội bộ; không chuyển hướng sang marketplace hoặc hệ thống thuê kho.
- Không chỉnh `appsettings.json`, không đổi secret, không seed/reset/migrate database shared hosting.
- Không dọn log/artifact/markdown trong lượt nghiệm thu này vì người dùng chưa yêu cầu dọn tiếp.
- Điểm "100%" trong báo cáo chỉ được hiểu là pass theo gate có thể kiểm chứng local; không phải cam kết production tuyệt đối không còn lỗi.

### Kết Quả Kiểm Chứng Mới Nhất

| Gate | Kết quả | Bằng chứng |
| --- | --- | --- |
| Hash cấu hình | Pass | `appsettings.json` SHA256: `7A3E4A74C0D7D7CBA0AF5EB91A65B06764CCDF38B79798D9C0063188A3C4A1EC`. |
| Model Groq OCR | Pass có lưu ý | Code dùng default `qwen/qwen3.6-27b`, cho phép override bằng `Groq:VisionModel`; không còn hard-code Llama 4 Scout trong production source. Theo Groq docs, Qwen3.6 27B là preview model, còn `openai/gpt-oss-120b` là production text model nhưng không thay thế trực tiếp cho OCR ảnh. |
| Static scan production source | Pass bước lọc | Không tìm thấy marker lỗi trong production source sau khi loại trừ vendor/generated/test guard: `ForFun`, `INVOICE11111`, `Internal / unowned`, `Chủ hàng kho dịch vụ`, `Fixed Bin`, `Hàng 3PL`, `Chủ hàng 3PL`, `@@media`, `@@page`, `Debug.WriteLine`, `console.log/warn/error`, HTML entity thô `&#x`. |
| Build | Pass | `dotnet build WMS.sln --no-restore -v:minimal` pass `0 Warning(s)`, `0 Error(s)`. |
| .NET test | Pass | `dotnet test WMS.Tests\WMS.Tests.csproj --no-build` pass `697/697`. |
| Visual/browser runtime | Pass | `visual:test` pass `194 passed / 66 skipped`, `visual:no-device` pass `10/10`, `visual:mobile-deep` pass `420/420` trên local development endpoint. |
| DB hosting audit | Pass | `artifacts/data-quality/wms-data-quality-audit-20260705-130335.txt` có `0` issue rows. |
| k6 load | Chưa thể xác minh local | Repo có script k6, nhưng người dùng yêu cầu không tải/cài thêm tool; nếu máy không có `k6` trong PATH thì giữ trạng thái chưa xác minh. |

### Finding Mới Đã Xử Lý / Đã Ghi Nhận

#### [Low] Báo cáo cuối và test guard còn mojibake trong text tiếng Việt

- Mức độ: Low vì không làm sai tồn kho, nhưng ảnh hưởng chất lượng tài liệu và độ tin cậy static guard.
- Khu vực: Report/test evidence.
- File liên quan: `FINAL_WMS_ENTERPRISE_QA_REPORT.md`, `WMS.Tests/EnterpriseUiUxPolishTests.cs`.
- Cách sửa: viết lại đoạn report bị hỏng dấu; đổi token mojibake trong test guard sang Unicode escape để vẫn bắt lỗi nhưng source không chứa text hỏng trực tiếp.

#### [Medium] Bằng chứng load k6 chưa chạy vì thiếu binary và không được tải thêm

- Mức độ: Medium cho performance evidence; không phải bug nghiệp vụ đã chứng minh trong source.
- Khu vực: Performance/load verification.
- File/vị trí liên quan: `tests/load/k6-wms-dod.js`, `scripts/Run-WmsVerification.ps1`.
- Mô tả: Repo đã có kịch bản load test, nhưng người dùng yêu cầu không tải/cài thêm tool. Nếu máy hiện tại không có `k6`, không được ghi nhận load gate là pass.
- Ảnh hưởng: Không thể ghi nhận p50/p95/p99/throughput/error-rate thật bằng k6 trên máy hiện tại. Các gate build, .NET, static và Playwright vẫn là bằng chứng repo/local chính.
- Quyết định: Không tải/cài thêm, không chỉnh `appsettings.json`. Khi có `k6` được cài sẵn hoặc môi trường staging, chạy lại kịch bản load và bổ sung số liệu.

### Kết Luận Nghiệm Thu Bổ Sung

- Đã chứng minh bằng đọc code/static scan: model Groq không còn hard-code Llama 4 Scout trong production source, `appsettings.json` không đổi, report/test guard đã được làm sạch lỗi chữ phát hiện trong lượt này.
- Đã chứng minh bằng gate local 2026-07-07: build pass `0 warning / 0 error`, `.NET tests` pass `697/697`; Playwright visual/browser `visual:test` pass `194 passed / 66 skipped`, `visual:no-device` pass `10/10`, `visual:mobile-deep` pass `420/420`; DB hosting audit read-only có `0` issue rows ở evidence gần nhất và migration preflight đã tách riêng với demo seed. Không seed/reset/xóa DB trong lượt nghiệm thu này.
- Chưa chứng minh local: k6 load/performance nếu thiếu binary, thiết bị thật, production load, DR/HA, hosting artifact và certified integration.
- Báo cáo này không tuyên bố production Tier-1 hoàn hảo hoặc hết bug tuyệt đối.
