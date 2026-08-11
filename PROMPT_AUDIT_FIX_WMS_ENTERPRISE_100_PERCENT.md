# PROMPT RÀ SOÁT, BENCHMARK, SỬA LỖI VÀ KIỂM CHỨNG WMS TOÀN DIỆN

Sao chép toàn bộ nội dung từ phần “BẮT ĐẦU PROMPT” đến “KẾT THÚC PROMPT” và gửi cho agent đang mở tại thư mục gốc của dự án WMS.

---

# BẮT ĐẦU PROMPT

Bạn là Principal WMS Architect, Senior Warehouse Business Analyst, Staff .NET Engineer, Database Reliability Engineer, Application Security Engineer, QA Automation Lead, Playwright Visual QA Engineer và Production Readiness Reviewer.

Bạn đang làm việc trực tiếp trong repository của một **hệ thống quản lý kho nội bộ WMS**. Nhiệm vụ của bạn không chỉ là review hoặc đề xuất. Bạn phải:

1. Rà soát toàn bộ repository có hệ thống và có bằng chứng.
2. Hiểu đúng nghiệp vụ hiện tại trước khi sửa.
3. Benchmark trung thực với các WMS enterprise hàng đầu.
4. Chấm phần trăm theo rubric định lượng.
5. Phát hiện bug, lỗi logic, lỗ hổng, thiếu tính năng, dữ liệu sai và lỗi UI.
6. Sửa tất cả lỗi nằm trong phạm vi được phép sửa.
7. Viết hoặc bổ sung regression tests.
8. Chạy đầy đủ build, automated tests, database tests, E2E và Playwright visual.
9. Lặp lại audit → sửa → test → kiểm tra artifact cho đến khi đạt gate hoặc gặp blocker thật sự.
10. Chỉ tuyên bố 100% khi toàn bộ điều kiện bằng chứng trong prompt này đã đạt.

Tài liệu nguồn bắt buộc:

- Đọc toàn bộ file `ROADMAP_WMS_ENTERPRISE_100_PERCENT_FULL.md`.
- Đọc mọi `AGENTS.md`, README, tài liệu kiến trúc, runbook, state machine, permission matrix và hướng dẫn test có hiệu lực.
- Nếu tên roadmap khác, tìm file roadmap enterprise/full-scope mới nhất và ghi rõ file đã dùng.

## 1. HỢP ĐỒNG TRUNG THỰC

Mục tiêu là **100% evidence-based readiness và 0 known open defects**, không phải một lời bảo đảm tuyệt đối rằng phần mềm sẽ không bao giờ có bug.

Các quy tắc không được vi phạm:

- Không được báo “100%”, “0 bug”, “production-ready”, “all pass” hoặc “world-class” nếu thiếu bằng chứng.
- Không được tự suy ra pass từ tên file, tên test, screenshot cũ, artifact cũ hoặc tài liệu tự khai.
- Không được làm tròn 99.x% thành 100%.
- UNKNOWN, NOT RUN, NOT TESTED, BLOCKED và artifact không xác minh nhận 0 điểm cho đến khi có bằng chứng.
- Một test pass nhưng assertion yếu hoặc không kiểm tra đúng hành vi không được tính là bằng chứng đầy đủ.
- Screenshot được tạo nhưng chưa mở và kiểm tra trực quan không được tính là visual pass.
- Không cập nhật baseline screenshot chỉ để làm test xanh; phải xác minh thay đổi là đúng.
- Không dùng mock để thay cho real integration test ở các invariant tồn kho quan trọng.
- Không dùng số lượng test, file hoặc dòng code để suy ra chất lượng.
- Không che lỗi integrity/security bằng cách sửa UI hoặc suppress warning.
- Nếu không thể chạy một test, ghi rõ lý do, lệnh đã thử, bằng chứng và điều kiện cần để chạy; không đánh dấu pass.
- Phân biệt rõ “không có lỗi được phát hiện” với “đã chứng minh không thể có lỗi”.

Kết luận hợp lệ chỉ được là:

- `NO-GO`
- `PARTIALLY VERIFIED`
- `DEVICE-FREE READY`
- `ENTERPRISE INTERNAL WMS READY`

## 2. QUYỀN HẠN VÀ AN TOÀN

Bạn được phép:

- Đọc toàn bộ file trong repository.
- Sửa source code, migration, script, test và tài liệu trong repository để hoàn thành nhiệm vụ.
- Tạo dữ liệu test, database local/test, screenshot, trace, video và report.
- Cài dependency đã khai báo trong project nếu môi trường cho phép và cần để build/test.

Bạn không được tự ý:

- Deploy production.
- Kết nối hoặc ghi vào production database.
- Gửi email/tin nhắn thật, gọi thanh toán thật hoặc tạo giao dịch ngoài test.
- Commit, push, merge hoặc mở pull request nếu người dùng chưa yêu cầu.
- Xóa dữ liệu, reset hard, checkout đè thay đổi hoặc dùng lệnh phá hủy.
- In secret, token, connection string, password hoặc dữ liệu nhạy cảm.
- Sửa file generated/vendor để “fix” thay vì sửa nguồn sinh ra nó.

Trước khi sửa:

- Kiểm tra trạng thái Git và thay đổi đang có.
- Giữ nguyên mọi thay đổi không liên quan của người dùng.
- Không giả định working tree sạch.
- Nếu file đang có thay đổi chồng lấn, đọc diff và xử lý cẩn thận.

## 3. CÁCH LÀM VIỆC

- Bắt đầu bằng inventory và baseline, không sửa ngẫu nhiên.
- Duy trì plan có trạng thái và chỉ một bước chính in-progress.
- Cập nhật tiến độ ngắn gọn trong lúc làm.
- Không hỏi điều có thể tự trả lời bằng cách đọc repository hoặc kiểm tra an toàn.
- Chỉ hỏi khi thiếu quyết định nghiệp vụ làm đổi đáng kể phạm vi hoặc cần quyền ngoài repository.
- Nếu cần hỏi, gom blocker sau khi hoàn tất phần không bị chặn.
- Không dừng sau báo cáo baseline: tiếp tục sửa theo severity và chạy regression.
- Nếu hỗ trợ sub-agent, có thể chia business/data, security/architecture, UI/Playwright. Một lead sở hữu scorecard; tránh hai agent sửa cùng file và phải tự kiểm tra lại kết quả trước khi hợp nhất.

## 4. PHA 0 — BASELINE VÀ INVENTORY TOÀN REPOSITORY

### 4.1. Xác định stack thật

Tự phát hiện và ghi:

- Solution/project files, .NET SDK/runtime, ASP.NET Core version.
- ORM/database engine và migration strategy.
- Frontend stack, CSS framework, JavaScript/TypeScript.
- Test framework, Playwright config, browser projects và authentication setup.
- Package manager và lockfile.
- OCR, email, storage, background jobs, identity và API ngoài.
- Hosting assumptions và environment config.

Không giả định stack chỉ từ ảnh hoặc tên thư mục.

### 4.2. Lập file inventory

Dùng công cụ nhanh như `rg --files` và tạo:

- `artifacts/full-audit/FILE_AUDIT_MANIFEST.csv`
- `artifacts/full-audit/REPOSITORY_INVENTORY.md`

Mỗi path có:

- Path và category.
- First-party, generated, vendor, artifact hay binary.
- Có cần line-by-line review không.
- Reviewer/status.
- Finding liên quan.
- Test/evidence bao phủ.
- Lý do exclude.

Phân loại:

1. **First-party source**: review hoặc có test/evidence truy vết.
2. **Config/migration/schema/script**: bắt buộc review.
3. **Test code**: review assertion, isolation và false positive.
4. **Views/CSS/JS/static assets**: review và visual-test.
5. **Docs/runbook**: kiểm tra khớp runtime.
6. **Generated** như `bin`, `obj`: inventory, không line-review; kiểm tra commit nhầm.
7. **Vendor** như `node_modules`: không đọc từng file; kiểm tra manifest, lockfile, license, vulnerability.
8. **Artifacts/logs/test-results**: kiểm tra freshness, secret/PII và retention; không coi là source.
9. **Images/fonts/binary**: kiểm tra metadata, license, size, usage, broken reference và visual result.

“Rà từng file” nghĩa là 100% path được phân loại, 100% first-party file có coverage record, mọi exclusion có lý do; không lãng phí thời gian đọc hàng trăm nghìn file generated/vendor.

### 4.3. Baseline trước chỉnh sửa

Chạy và lưu:

- Restore/install dependency.
- Build đúng cấu hình.
- Unit/integration tests hiện có.
- Lint/typecheck/analyzer.
- Migration/model drift.
- App startup smoke.
- Playwright hiện có.
- Data-quality scripts.

Tạo:

- `artifacts/full-audit/BASELINE_BUILD_TEST.md`
- `artifacts/full-audit/BASELINE_DEFECTS.md`
- `artifacts/full-audit/BASELINE_SCORECARD.md`

Không sửa test để che baseline fail trước khi ghi nguyên nhân.

## 5. PHA 1 — HIỂU KIẾN TRÚC VÀ NGHIỆP VỤ

### 5.1. Bản đồ runtime

Tạo `docs/audit/WMS_RUNTIME_MAP.md`:

- Request → middleware → authn/authz → controller.
- Controller → service → data/DbContext.
- Transaction boundary.
- Background jobs.
- OCR/file/import/export.
- Inventory write path.
- Audit/log/metrics.
- External integrations.

### 5.2. Chốt invariant

Xác định:

- Source of truth tồn kho.
- Quantity, ReservedQty, AvailableQty.
- Warehouse/location/owner/item/lot/serial/status/UOM scope.
- Ledger và source voucher/source line.
- Voucher state machine.
- FEFO/FIFO.
- UOM conversion/rounding.
- NSX/HSD.
- Lock period/snapshot.
- Reversal/cancellation.
- Idempotency/uniqueness.

Nếu tài liệu và code mâu thuẫn, ghi finding; không chọn bên thuận tiện.

## 6. PHA 2 — BENCHMARK VỚI WMS LỚN

Nghiên cứu nguồn chính thức hiện hành của:

- SAP Extended Warehouse Management.
- Oracle Warehouse Management.
- Manhattan Active Warehouse Management.
- Blue Yonder Warehouse Management.
- Có thể bổ sung Microsoft Dynamics 365 SCM, Infor WMS hoặc Körber.

Yêu cầu:

- Chỉ dùng primary/official source cho claim kỹ thuật.
- Ghi link trực tiếp, ngày truy cập và capability.
- Phân biệt capability với marketing.
- Không gọi tương đương chỉ vì có menu cùng tên.

Tạo `docs/audit/WMS_ENTERPRISE_CAPABILITY_MATRIX.md` gồm:

- Domain/requirement.
- Applicability: Required Now, Required Later, Optional, N/A.
- State: Absent, Prototype, Partial, Complete, Enterprise-verified.
- Evidence.
- Gap/root cause.
- Severity/risk.
- Benchmark reference.
- Score 0–4.
- Effort/dependency/owner/release.

Bao phủ tối thiểu:

### Core inbound

- PO/ASN/inbound order.
- Appointment/dock.
- Receiving/discrepancy/tolerance.
- QC/quarantine.
- Directed putaway.
- Cross-dock/flow-through.
- Supplier return.
- LPN/SSCC/pallet/container.

### Inventory

- Multi-warehouse/zone/bin.
- Multi-owner.
- Lot/serial/expiry/FEFO/FIFO.
- UOM/dual-UOM/catch-weight.
- Available/hold/quarantine/damaged/expired/in-transit.
- Replenishment/transfer/cycle count.
- Traceability/recall.
- Adjustment/reversal.

### Outbound

- Allocation/priority.
- Wave/waveless/batch/zone/cluster picking.
- Short pick/backorder/substitution/partial.
- Packing/cartonization/VAS/kitting.
- Staging/load/ship/manifest.
- Carrier/TMS.

### Execution

- Slotting/task interleaving.
- Labor/shift/skill/workload.
- Yard/dock.
- WES/WCS/robotics/ASRS.
- Resource orchestration.

### Enterprise/platform

- Multi-company/3PL/billing.
- ERP/OMS/TMS/e-commerce/EDI/API/event/webhook.
- Workflow/config/feature flag.
- SSO/OIDC/SCIM.
- Multi-language/timezone/currency/UOM.
- Analytics/KPI/exception/AI.
- HA/DR/scalability/observability/compliance.
- Mobile/offline/scanner/printer/RFID/scale/camera.

Không ép triển khai capability không phù hợp. Internal Readiness có thể loại N/A hợp lệ; Enterprise Parity vẫn phải phản ánh thiếu capability. Mọi N/A cần lý do và owner phê duyệt.

## 7. PHA 3 — CHẤM ĐIỂM

Báo cáo:

1. Internal WMS Readiness.
2. Tier-1 Enterprise Capability Parity.
3. Evidence Coverage.

| Domain | Weight |
|---|---:|
| Architecture/code quality | 6% |
| Security/permission/isolation | 10% |
| Inventory/data integrity | 14% |
| Core warehouse workflows | 14% |
| Advanced WMS | 12% |
| Integration/extensibility | 8% |
| Devices/mobile/automation | 6% |
| UI/UX/accessibility/localization | 7% |
| QA/automated/visual tests | 8% |
| Performance/scalability/resilience | 6% |
| Deployment/observability/DR/compliance | 6% |
| Analytics/labor/optimization | 3% |
| **Total** | **100%** |

Điểm:

- 0: absent/broken/not tested.
- 1: prototype.
- 2: partial.
- 3: complete nhưng thiếu hardening/test/evidence.
- 4: enterprise-grade verified.

Trần điểm:

- Critical stock/data/auth/security issue: tối đa 49%.
- Chưa pass concurrency/idempotency/migration/restore: tối đa 69%.
- Chưa real E2E theo role cho nhập/xuất/kiểm kê: tối đa 79%.
- Chưa visual-test 100% route/state: tối đa 89%.
- Evidence Coverage dưới 100%: không được báo 100%.
- Còn defect: không được báo “0 known defects”.

Tạo:

- `docs/audit/WMS_SCORECARD_BEFORE.md`
- `docs/audit/WMS_SCORECARD_AFTER.md`

Mỗi điểm phải trỏ đến evidence.

## 8. PHA 4 — REVIEW TỪNG LỚP CODE

### 8.1. Solution/project/dependency

- Target framework, nullable, analyzers, warnings.
- Project references.
- Package duplicate/outdated/vulnerable.
- Lockfile deterministic.
- Dev dependency trong production.
- Generated file commit nhầm.
- License package/font/image/template.
- Dead/duplicate project/source/test.

### 8.2. Startup/middleware/config

- DI lifetime/circular dependency.
- Middleware order.
- Exception/status handlers.
- HTTPS/HSTS/forwarded headers/proxy trust.
- Static files/cache.
- Authn/authz/session/cookie.
- CSRF.
- CORS/CSP/security headers.
- Localization/timezone.
- Health/readiness.
- Background jobs.
- Environment config.
- Không hard-code secret/production endpoint.

### 8.3. Controllers/endpoints

- Policy và object/data scope.
- Overposting/mass assignment.
- Server validation.
- HTTP verb/status/error contract.
- Anti-forgery.
- Thin controller.
- Async/cancellation/timeout.
- Không swallow exception/stack trace.
- Pagination/filter/sort/limit.
- Upload/download scope.
- Open redirect/path traversal/IDOR.

### 8.4. Services/business layer

- Transaction.
- Idempotency.
- Concurrency.
- State transition.
- Inventory invariant.
- Validation.
- Rounding/decimal/date/time.
- Retry transient only.
- External call ngoài DB transaction.
- Duplicate side effect.
- Error contract.
- Safe logging.
- Dead/duplicate code.

### 8.5. Data/DbContext/repository

- FK/unique/check/not-null/index.
- Owner/warehouse query filter.
- Tracking/no-tracking.
- N+1/Cartesian/client evaluation.
- DB paging/aggregate.
- Raw SQL parameterization.
- Isolation/deadlock.
- RowVersion.
- Decimal precision/date/timezone.
- Soft delete/referential integrity.
- Pool/timeout.
- Seed/reference data.

### 8.6. Models/DTO/ViewModels

- Nullability/validation.
- Entity exposure/overposting.
- Mapping.
- Annotation vs DB.
- Enum/status.
- Decimal/UOM/date.
- Không nhận audit/role/owner/status nhạy cảm.
- Serialization cycle/data leak.

### 8.7. Migration/schema

- Fresh DB và upgrade DB thật.
- Model drift.
- Backfill/resume/idempotency.
- Lock/downtime.
- Destructive change.
- Rollback/forward-fix.
- Index/constraint conflict.
- Seed duplicate.
- Rolling app/schema compatibility.

### 8.8. Views/Razor/HTML/JS/CSS

- Semantic HTML/encoding.
- Anti-forgery.
- XSS/unsafe HTML.
- Duplicate ID/name/label.
- Correct model binding.
- Client/server validation.
- Double submit/spinner.
- Duplicate handlers/memory leak.
- Fetch error/timeout/cancel.
- Role menu/route.
- CSS overflow/z-index/sticky.
- Responsive/zoom.
- Broken assets.
- Vietnamese typo/mojibake.
- Không lộ label kỹ thuật/raw status.

### 8.9. Tests

- Assertion kiểm tra kết quả thật.
- Không catch rồi bỏ qua.
- Không phụ thuộc order/time/shared state.
- Isolation/cleanup.
- Mock đúng boundary.
- Critical invariant có real DB test.
- E2E auth/role thật.
- Snapshot không update mù.
- Retry không che flaky bug.

### 8.10. Scripts/docs/logs/artifacts

- Script deterministic, exit code đúng.
- Không hard-code path cá nhân nếu không có fallback.
- Docs khớp runtime.
- Runbook có verify/rollback.
- Không secret/PII/production dump.
- Artifact cũ phải có build/date và được rerun.

## 9. PHA 5 — AUDIT NGHIỆP VỤ VÀ SỬA LỖI

### 9.0. Core WMS Completeness Contract

Trước khi chấm điểm hoặc tuyên bố core đầy đủ, phải lập `docs/audit/CORE_WMS_FUNCTION_MATRIX.md` và kiểm tra 100% các nhóm sau:

1. Cấu trúc kho: warehouse, zone, location/bin và constraint/capacity áp dụng.
2. Master data: item/SKU, category, UOM/conversion, partner, owner, lot, serial, NSX/HSD và inventory status.
3. User/role/permission và warehouse/owner data scope.
4. Inbound: request/order, receive, discrepancy, partial, QC/quarantine, putaway, post, cancel và reversal.
5. Inventory: on-hand/reserved/available/in-transit, ledger, reservation, internal move, transfer, replenishment, hold, damaged và expired.
6. Outbound: request/order, allocation/reservation, FEFO/FIFO, pick, pack/stage/handover/ship, partial/short, cancel và reversal.
7. Count/adjust: scope, snapshot, count, recount, approval, adjustment, period lock và reconcile.
8. Return/quality/exception: supplier/customer/internal return, disposition, quarantine, scrap, recall và xử lý discrepancy.
9. Import/OCR/export/attachment với security, idempotency và fallback.
10. Reports: on-hand, inbound-outbound-stock, movement, aging, expiry, discrepancy, traceability, audit và KPI.
11. Cross-cutting: state machine, validation, transaction, concurrency, idempotency, permission, audit, logging, monitoring và recovery.
12. Operations: data seed, migration, backup/restore, runbook, UAT, support và reconciliation.

Mỗi dòng trong matrix phải có:

- Requirement và acceptance criteria.
- Schema/constraint/migration.
- Service/API/server validation.
- UI và role/state.
- Permission/data scope.
- State machine/transaction/concurrency/idempotency.
- Ledger/audit/log.
- Seed/test data.
- Unit/integration/API/E2E.
- Playwright functional/visual.
- Data-quality/reconciliation.
- Documentation/runbook.
- Evidence path, finding, score và trạng thái.

Chỉ được đánh dấu `COMPLETE` khi tất cả lớp bắt buộc pass. Nếu chỉ có menu hoặc CRUD, thiếu business state, thiếu validation, thiếu test hay chưa kiểm tra visual thì phải đánh dấu `PARTIAL`. Mọi mục hoàn thành phải được đổi checklist từ `- [ ]` thành `- [x]` ngay trong roadmap bằng evidence tương ứng.

### 9.1. Management Command Center — Trang tổng quát của Quản lý kho

Trang tổng quát phải trả lời “hôm nay cần làm gì?” bằng dữ liệu có thể hành động, không chỉ hiển thị vài tổng số nhập/xuất/tồn.

#### Bắt buộc rà soát trước khi sửa

Trước mọi edit liên quan dashboard:

1. Hoàn thành 100% repository inventory và first-party coverage record.
2. Dùng route/controller/view/navigation để tìm runtime path thật của dashboard.
3. Trace route → controller/API → service/query → DbContext/table/column/status/date.
4. Tìm toàn bộ model, DTO, view model, layout, partial/component, CSS, JavaScript, localization, permission, cache, index, job, test và artifact liên quan.
5. Xác định code/view/query cũ hoặc trùng nhưng không tự xóa.
6. Đọc Git diff và giữ thay đổi của người dùng.
7. Tạo `docs/audit/DASHBOARD_FILE_IMPACT_MAP.md`.
8. Tạo `docs/audit/DASHBOARD_METRIC_DICTIONARY.md`.
9. Tạo `docs/audit/DASHBOARD_ROLE_ACTION_MATRIX.md`.
10. Chưa được sửa dashboard cho đến khi ba tài liệu trên đủ path, dependency, metric formula, permission và regression scope.

Không sửa theo tên file đoán mò. Không tạo controller/service/view mới nếu chưa chứng minh không thể mở rộng implementation đang chạy. Không hard-code số liệu, card, status hoặc dữ liệu demo để làm UI đẹp.

#### Benchmark và nguyên tắc

Đối chiếu nguồn chính thức mới nhất của SAP Warehouse Monitor/Cockpit, Oracle Activity Monitor/Command Center, Manhattan real-time dashboards và Blue Yonder Analyst Workbench. Áp dụng các nguyên tắc:

- Current situation/real-time visibility.
- Not Started/In Progress/Completed/Blocked/Overdue.
- KPI so với target/trend.
- Exception-first.
- Unified warehouse/inventory/workload view.
- Drill-down/actionable analytics.
- Role/data-scope isolation.
- Data freshness và reconciliation.

#### Phạm vi chức năng bắt buộc

Kiểm tra và triển khai theo G7.1 của roadmap:

- Ngữ cảnh kho, owner, ngày, ca, timezone, last refresh và stale data.
- Work queue “Việc cần làm hôm nay” có priority, SLA, assignee, progress, blocker và action.
- KPI tổng quan theo đúng định nghĩa created/due/scheduled/completed today.
- Nhập: expected/arrived/receiving/received/posted/putaway, discrepancy, overdue và cycle time.
- Xuất: due/reserved/picking/picked/packing/staged/shipped, short, late, fill rate và cycle time.
- Tồn: on-hand/available/reserved/blocked, stockout/low/overstock, expiry, aging, capacity và integrity.
- Chuyển kho, replenishment, kiểm kê, adjustment, return, quarantine và recall.
- Approval, data-quality, OCR/import/export, job, integration, backup và health exceptions.
- Workload/labor/capacity nếu module/dữ liệu tồn tại.
- Trend theo giờ/ca/ngày/7–30 ngày và so với kế hoạch.
- Drill-down/quick action/back navigation giữ context.

#### Luật dữ liệu

- Mỗi metric có business definition, formula, source, status, date field, scope, distinct key, UOM/currency/timezone, cancellation/reversal/partial rule và drill-down.
- Không double-count header/line/ledger do join.
- Không cộng quantity thuộc UOM không tương thích.
- Không trộn created today, due today, scheduled today và completed today.
- Không hiển thị financial metric cho role không có quyền.
- Dashboard count phải khớp detail count tại cùng as-of-time/filter.
- Cache key phải gồm permission/data scope; không cache lẫn user/owner/warehouse.

#### Playwright/visual bắt buộc

- Inventory 100% route/card/chart/table/action của dashboard.
- Từng role: Admin, quản lý kho, role vận hành và owner nếu có.
- Desktop 1366×768, 1440×900, 1920×1080; zoom 100/110/125/150; tablet/mobile nếu support.
- States: normal, empty, loading, partial error, full error, stale, long text, many alerts, expired session.
- Test card/chart → drill-down → filtered list → action → return.
- Bắt console/page/network error, overflow, overlap, clipping, broken asset và unauthorized data.
- Manual review mọi screenshot/diff.
- Ở 1366×768, khối “Việc cần làm hôm nay” phải thấy sớm và không bị đẩy xuống bởi card trang trí.

#### Artifact bắt buộc

- `docs/audit/DASHBOARD_FILE_IMPACT_MAP.md`
- `docs/audit/DASHBOARD_METRIC_DICTIONARY.md`
- `docs/audit/DASHBOARD_ROLE_ACTION_MATRIX.md`
- `artifacts/dashboard-command-center/DASHBOARD_DATA_RECONCILIATION.md`
- `artifacts/dashboard-command-center/DASHBOARD_QUERY_PERFORMANCE.md`
- `artifacts/dashboard-command-center/DASHBOARD_PLAYWRIGHT_MATRIX.csv`
- `artifacts/dashboard-command-center/VISUAL_QA_REPORT.md`

Không được tick G7.1 nếu thiếu một artifact bắt buộc, một metric chưa reconcile, một role chưa test hoặc còn visual/console/network error.

Mỗi nghiệp vụ test:

- Happy path.
- Validation/boundary.
- Permission/scope.
- Concurrency.
- Idempotency/double-click/retry.
- Transaction rollback/fault injection.
- Cancel/reversal.
- Audit/log.
- UI/E2E.
- Data reconciliation.

### Master data

- Warehouse/zone/location.
- Item/category/UOM/conversion.
- Supplier/customer/owner.
- Lot/serial/status.
- User/role/permission.
- Unique/deactivate/delete/referential rules.

### Inbound

- Draft → line → validate → approve → post → putaway.
- Partial/over/under.
- Lot/serial/NSX/HSD/UOM.
- QC/quarantine.
- Cancel/reversal.
- ASN/PO/OCR/import duplicate.

### Outbound

- Draft → reserve → FEFO/FIFO → pick → pack → approve → ship/post.
- Không vượt available.
- Partial/short/backorder.
- Release/cancel đúng một lần.
- Expired/blocked/wrong-owner.
- Reversal/return.

### Transfer

- Same warehouse/location.
- Inter-warehouse/in-transit.
- Atomicity hoặc compensation.
- Cancel/discrepancy.

### Count/adjust

- Scope/snapshot.
- Duplicate period.
- Concurrent transaction policy.
- Recount/approval.
- Adjustment ledger.
- Period lock.

### Return/quarantine/expiry/recall

- Disposition.
- Không restock trước QC.
- Block/unblock.
- Expired không pickable.
- Forward/backward traceability.

### Import/OCR/export

- Format/size/MIME/signature.
- Missing/extra columns.
- Invalid/duplicate rows.
- Preview/dry-run.
- Atomic/partial policy.
- Retry idempotent.
- OCR multi-document/hash/source-line/timeout/429/fallback.
- Manual fallback.
- Export filter/permission/format/large data.
- Formula injection.

## 10. PHA 6 — DATABASE VÀ DỮ LIỆU

Chạy data-quality audit:

- Negative quantity/available.
- Reserved < 0 hoặc > quantity.
- Reservation active mismatch.
- Consumed/released vượt reserved.
- Item total vs location total.
- Posted voucher thiếu ledger.
- Orphan/duplicate ledger.
- Ledger-stock mismatch.
- Invalid quantity/UOM/direction.
- Expired lot pickable.
- NSX > HSD.
- Duplicate serial.
- Active item thiếu BaseUom.
- Conversion thiếu/trùng/sai.
- Header thiếu line/line orphan.
- State-reservation-ledger mismatch.
- Rounding/total mismatch.
- Cross-owner/warehouse leakage.
- Orphan file/audit/source document.

Dữ liệu test cần:

- Mọi role.
- Nhiều kho/vị trí/owner nếu có.
- Item thường/lot/serial/expired/quarantine.
- UOM/rounding boundaries.
- Full/partial/cancel/reversal voucher.
- Long Unicode Vietnamese/special chars.
- Empty/error/large table UI states.
- Concurrency data.

Không tạo data chỉ để happy path pass.

## 11. PHA 7 — SECURITY

Kiểm tra:

- Password hash.
- Cookie/session/timeout/logout/revoke.
- Rate limit/lockout.
- CSRF/XSS/SQL injection.
- IDOR/mass assignment.
- Role/permission/data scope.
- Path traversal/upload/download.
- Formula injection/open redirect.
- CORS/CSP/headers/TLS.
- Secret/log/PII.
- Dependency vulnerability.
- Authentication bypass.
- Disabled user/token.
- Admin override audit.
- Owner/warehouse isolation.
- Background job scope.

Tạo:

- `docs/audit/SECURITY_THREAT_MODEL.md`
- `artifacts/security/SECURITY_TEST_REPORT.md`

Không in secret; chỉ ghi key/path và remediation.

## 12. PHA 8 — AUTOMATED TEST

### Build/test

Tự phát hiện lệnh đúng rồi chạy:

- Clean/restore/build.
- Unit/integration/API.
- Migration/data-quality.
- E2E.
- Analyzer/lint/typecheck.

Ghi command, exit code, duration, result.

### Concurrency/idempotency/fault

- Hai issue cùng stock.
- Hai reservation cùng stock.
- Post và count cùng lúc.
- Transfer và issue cùng source.
- Hai người sửa cùng voucher.
- Lặp cùng command ít nhất 5 lần.
- Job chạy lặp.
- Cancel/reversal lặp.
- Import/OCR retry.
- Fault sau từng bước transaction.
- Deadlock/transient retry.

Sau test chạy reconcile. Pass khi không negative available, không over-reserve, không duplicate ledger, không partial data và conflict có lỗi rõ.

### Real E2E

Với mọi role áp dụng:

- Login thật.
- Menu/route/API/scope.
- Inbound.
- Outbound.
- Transfer.
- Count/adjust.
- Cancel/reversal.
- Return/quarantine.
- Import/OCR fallback.
- Export đúng quyền.
- Audit/request ID.

Critical E2E dùng DB/test service thật. Mock external provider phải có contract/fallback test.

## 13. PHA 9 — PLAYWRIGHT FUNCTIONAL VÀ VISUAL 100%

### Route/page inventory

Sinh danh sách từ route/controller/view/navigation, không chỉ sidebar.

Tạo `artifacts/visual-full/PLAYWRIGHT_PAGE_MATRIX.csv`:

- Route/page/role/scope/state.
- Viewport/zoom/browser.
- Functional result.
- Console/network result.
- Screenshot/diff.
- Manual review.
- Finding ID.

Mọi UI route phải xuất hiện hoặc có exclude reason.

### Roles

- Public/unauthenticated.
- Admin.
- Quản lý kho.
- Nhập kho.
- Xuất kho.
- Tồn kho/kiểm kê.
- Vận chuyển.
- Báo cáo.
- Owner/đối tác nếu có.

Test menu và URL trực tiếp.

### Viewports

Desktop:

- 1366×768.
- 1440×900.
- 1920×1080.

Tablet:

- 768×1024.
- 1024×768.

Mobile nếu support:

- 360×800.
- 390×844.
- 430×932.

Zoom:

- 100%, 110%, 125%.
- 150% cho màn quan trọng.

Browser:

- Chromium bắt buộc.
- Firefox/WebKit/Edge nếu support.

### States

- Normal/empty/loading.
- Validation/business/server error.
- 401/403/404.
- Long Vietnamese text.
- Large table/many columns.
- Max input.
- Long modal/confirmation/dropdown edge.
- Toast/sidebar/header.
- Pagination/filter/sort.
- Disabled/submitting/double-click.
- Slow/failing network.
- Expired session.

### Automated assertions

Bắt:

- `console.error`.
- `pageerror`.
- `requestfailed`.
- Unexpected 4xx/5xx.
- Broken image/font/css/js.
- Unhandled promise.
- Horizontal overflow.
- Out-of-viewport element.
- Critical overlap.
- Text clipping.
- Zero-size/hidden clickable.
- Duplicate id.
- Missing label.
- Focus/trap issue.
- Stuck spinner.
- Control bị topbar/sidebar/modal che.
- Unauthorized data trong DOM/network.

Whitelist chỉ bằng rule cụ thể có lý do.

### Visual snapshots

- Full-page và component screenshot.
- Tên gồm build/role/route/viewport/state.
- Ổn định animation/time/random data mà không đổi behavior.
- Threshold nhỏ có tài liệu.
- Mở và xem bằng mắt mọi diff.
- Kiểm tra spacing/alignment/hierarchy/typography/icon/color/contrast/wrapping/overflow/z-index.
- Không duyệt baseline mới nếu chưa giải thích diff.
- Before/after cho visual bug.

### Accessibility/keyboard

- Tab/focus/Enter/Space/Escape.
- Modal focus trap/return.
- Label/aria.
- Contrast.
- Không chỉ dùng màu.
- Reflow/zoom.
- Semantic table/form/status.

Visual chỉ pass khi:

- Matrix coverage 100%.
- 0 unexpected console/page/network error.
- 0 unresolved diff.
- 0 overflow/overlap/clipping.
- 0 typo/mojibake/raw technical label.
- Mọi screenshot đã manual-reviewed.

### Cross-device UI Zero-Defect Protocol

Phải áp dụng cho toàn bộ UI first-party, không chỉ dashboard hoặc trang trong ảnh.

#### Quy tắc dùng ảnh tham chiếu của người dùng

Bộ ảnh hiện có là các trạng thái **desktop**: icon rail thu gọn, sidebar desktop mở rộng, các flyout desktop dài và trang dashboard. Ảnh crop hẹp không chứng minh viewport mobile. Không được suy diễn hoặc ghi finding mobile từ các ảnh này.

- Dùng ảnh làm baseline tham chiếu cho các trạng thái desktop: rail thu gọn, sidebar mở rộng, flyout Nhập kho/Tồn kho/Vận chuyển/Báo cáo/Danh mục/Hệ thống, quick workspace, công việc cần xử lý và KPI card.
- Reproduce từng trạng thái trên build hiện tại; ghi viewport, DPR, browser, zoom, role, route, menu state và build/version.
- Kiểm tra flyout desktop tại các viewport/zoom hỗ trợ: item không bị clip, vùng scroll dùng được, click ngoài/Escape đóng đúng và focus trả về trigger.
- Kiểm tra icon rail có tooltip, accessible name, keyboard support, hit target và active/hover/focus state.
- Kiểm tra sidebar mở rộng không sai route/active state, tràn chữ, nhảy layout hoặc che nội dung ngoài thiết kế.
- Kiểm tra card/grid desktop và laptop không overlap, clipping, ép chữ hoặc sai hierarchy.
- Kiểm tra KPI bằng Metric Dictionary: currency, as-of-time, scope, formula, numerator/denominator, period và drill-down/reconciliation.
- Browser fullscreen instruction là UI trình duyệt, không phải app defect.
- Chỉ tạo finding sau khi tái hiện; chỉ đóng finding sau root cause, fix, regression và before/after Playwright evidence.

Tablet và mobile vẫn phải được audit đầy đủ bằng viewport/thiết bị thật hoặc emulation đã xác nhận theo support matrix. Đây là yêu cầu kiểm thử độc lập, không phải kết luận rằng bộ ảnh desktop đã chứng minh có lỗi mobile.

#### Audit trước khi chỉnh UI

Trước mọi thay đổi shared layout/navigation/CSS/JS:

1. Hoàn thành repository/file inventory.
2. Tạo `docs/audit/UI_FILE_IMPACT_MAP.md`.
3. Tạo `docs/audit/UI_BREAKPOINT_AND_REFLOW_CONTRACT.md`.
4. Tạo `artifacts/ui-cross-device/UI_ROUTE_ROLE_STATE_VIEWPORT_MATRIX.csv`.
5. Map route → layout → partial/component → CSS/JS/asset → data/action.
6. Xác định global selector/token/breakpoint và toàn bộ consumer.
7. Kiểm tra Git diff; giữ nguyên thay đổi người dùng.
8. Không dùng global override/`!important`/fixed width để vá một ảnh nếu chưa regression consumer.
9. Không sửa file theo tên đoán mò; chỉ sửa file được chứng minh nằm trên runtime path.

#### Support matrix

Desktop/laptop: 1280×720, 1366×768, 1440×900, 1536×864, 1920×1080 và 2560×1440 nếu support.

Tablet: 768×1024, 820×1180, 1024×768 và 1180×820.

Mobile: 320×568, 360×800, 375×812, 390×844, 412×915, 430×932; portrait/landscape cho flow quan trọng.

Zoom/scaling: 100%, 110%, 125%, 150%, 200%; reflow 320 CSS px/400% cho scope accessibility; Windows scaling/DPR khi môi trường cho phép.

Browser: Chromium và Edge bắt buộc nếu là target doanh nghiệp; Firefox/WebKit nếu support matrix công bố.

#### Bắt buộc kiểm tra từng component type

- Shell/layout/container/grid.
- Expanded/collapsed sidebar.
- Mobile drawer.
- Flyout/submenu/menu dài.
- Topbar/search/notification/help/profile.
- Breadcrumb/page header/quick actions.
- Dashboard/card/KPI/chart.
- Table/list/filter/sort/paging/bulk action.
- Form/input/select/autocomplete/date/file/scan.
- Modal/drawer/popover/tooltip/toast/confirm.
- Tabs/accordion/stepper.
- Empty/loading/error/stale/offline/session-expired.
- Print/export preview nếu có UI.

#### Quy tắc responsive

- Không body horizontal overflow.
- Không content/action bị cắt hoặc unreachable.
- Wide table chỉ scroll trong container hoặc có mobile representation rõ.
- Mobile không giữ đồng thời icon rail và desktop flyout nếu làm giảm usability.
- Mobile drawer có backdrop, close/back, body lock, internal scroll, Escape và focus return.
- Desktop flyout collision-aware, max-height/max-width và item đầu/cuối tiếp cận được.
- Main action phải wrap/stack/overflow menu; không biến mất.
- Grid card reflow theo width; không ép text hoặc fixed height cắt nội dung.
- Sticky/fixed element không che focus, validation, toast hoặc content.
- Virtual keyboard, orientation và safe-area không che field/action.

#### Accessibility

- Target WCAG 2.2 AA.
- Reflow không cuộn hai chiều ngoài vùng dữ liệu ngoại lệ.
- Target tối thiểu 24×24 CSS px/spacing hợp lệ; warehouse touch control ưu tiên 44×44.
- Keyboard-only workflow core.
- Visible focus, correct tab order, skip/main landmarks.
- ARIA/semantic đúng cho nav/menu/dialog/table/form/alert/status/progress.
- Contrast, non-color status, reduced motion và screen-reader smoke.

#### Automated assertions bổ sung

- Body `scrollWidth <= clientWidth`.
- Bounding rect không vượt viewport ngoài whitelist.
- Critical controls không overlap.
- Không clipped/zero-size/hidden clickable.
- Menu/modal item đầu và cuối reachable.
- Drawer/flyout open/close/focus/body lock đúng.
- Touch target/accessible name/label/duplicate ID.
- Không unexpected console/page/network error hoặc broken asset.
- Full-page/component screenshots và manual review mọi diff.

#### UI 100% gate

Không được báo UI 100% khi:

- Route/role/state/viewport matrix chưa 100%.
- Mobile pass được suy ra từ desktop pass.
- Còn screenshot/diff chưa mở xem.
- Còn overflow, clipping, overlap, unreachable control hoặc menu/modal/table/form defect.
- Còn typo, lỗi dấu, raw label, contrast/accessibility, console/network hoặc broken asset.
- Chưa có sign-off riêng cho desktop/laptop/tablet/mobile.

Artifact bắt buộc:

- `docs/audit/UI_FILE_IMPACT_MAP.md`
- `docs/audit/UI_BREAKPOINT_AND_REFLOW_CONTRACT.md`
- `artifacts/ui-cross-device/UI_ROUTE_ROLE_STATE_VIEWPORT_MATRIX.csv`
- `artifacts/ui-cross-device/UI_DEFECT_REGISTER.md`
- `artifacts/ui-cross-device/UI_ACCESSIBILITY_REPORT.md`
- `artifacts/ui-cross-device/UI_VISUAL_REVIEW_MANIFEST.csv`
- `artifacts/ui-cross-device/screenshots/`
- `artifacts/ui-cross-device/diffs/`
- `artifacts/ui-cross-device/traces/`

## 14. PHA 10 — PERFORMANCE/RELIABILITY

Điền:

- Expected users.
- Test concurrency tối thiểu max(2×expected, 20) nếu môi trường cho phép.
- Item/location/lot/serial counts.
- Transaction history.
- Import/export max rows.

Đo p50/p95/p99, throughput, error, CPU/memory/DB nếu có, cold/warm, load/stress/spike/soak/recovery.

Luồng:

- Login/menu.
- Stock.
- History.
- Dashboard.
- Post inbound/outbound.
- Import/export/report/job.

Ngưỡng mặc định:

- Login/menu p95 ≤ 2s.
- Stock p95 ≤ 2s.
- History p95 ≤ 3s.
- Post không OCR/upload p95 ≤ 3s.
- Dashboard p95 ≤ 3s.
- Export 10k ≤ 30s hoặc background.
- Error < 1%.
- Integrity error = 0.

Dùng dotnet/PowerShell cho API load; Playwright cho browser journey. Review execution plan, index, N+1, paging, aggregate, tracking, memory, timeout. Sau performance chạy data-quality.

## 15. PHA 11 — DEPLOYMENT/OPS/DR

Kiểm tra:

- CI build/test/analyzer/security/migration.
- Env config/secret.
- Fresh/upgrade DB.
- Deploy/smoke/rollback.
- Health/liveness/readiness.
- Structured log/correlation.
- Metrics/alerts/retention.
- Job/provider monitoring.
- Audit.
- Backup schedule/success/encryption/retention.
- Restore rehearsal/RPO/RTO.
- DR/failover.
- Runbook/manual fallback/reconcile.

Backup chỉ pass khi restore và verify data thành công.

## 16. PHA 12 — DEVICE/INTEGRATION/PILOT

Nếu chưa có thiết bị:

- Test simulator/protocol mock.
- Mark real-device UNVERIFIED.
- Không báo Enterprise Internal WMS Ready.
- Tạo exact device matrix/test plan.

Khi có:

- Scanner/GS1/QR.
- Camera.
- Printer/label/PDF.
- Scale/RFID.
- Mobile/tablet/kiosk.
- Wi-Fi drop/roam/reconnect.
- Offline/retry/idempotency.
- WCS/WES/robotics.
- Print → scan → post → reconcile round-trip.

Integration phải có contract, auth/scope, timeout/retry, idempotency, outbox/inbox, DLQ/replay, schema version, reconciliation, sandbox và fallback.

Pilot phải có migration/control total, real users/roles/shifts, real workflows/exceptions, physical reconciliation, cutover/rollback, training/SOP/support và hypercare KPI.

## 17. FINDING REGISTER

Tạo `docs/audit/WMS_FINDINGS_REGISTER.md`.

Mỗi finding:

- ID/severity/domain.
- File/vị trí.
- Invariant.
- Repro/evidence.
- Expected/actual.
- Impact/root cause.
- Fix/regression test/artifact.
- Status/owner.

Severity:

- Critical: mất/sai tồn, corruption, auth bypass, cross-owner leak, secret, restore fail.
- High: core flow, race/duplicate, privilege, migration, UI chặn nghiệp vụ.
- Medium: edge business/report/validation/UI/accessibility đáng kể.
- Low: wording/spacing/maintainability; vẫn đóng nếu mục tiêu 0 defect.

Gộp biểu hiện cùng root cause; không ghi trùng.

## 18. FIX VÀ REGRESSION LOOP

Ưu tiên:

1. Data loss/security/stock integrity.
2. Transaction/concurrency/idempotency.
3. Core workflows.
4. Migration/backup/restore.
5. Permission/scope.
6. Import/OCR/integration.
7. Performance/reliability.
8. UI/visual/accessibility/wording.
9. Maintainability/docs.

Mỗi fix:

- Xác nhận root cause.
- Thêm failing regression test khi khả thi.
- Sửa nhỏ nhất nhưng đúng kiến trúc.
- Không hard-code test data.
- Targeted test → affected suite → full regression.
- Data-quality nếu ảnh hưởng DB/tồn.
- Playwright nếu ảnh hưởng UI/route/permission.
- Cập nhật finding/manifest.

Không reset hard khi regression. Tiếp tục cho tới khi mọi lỗi đóng hoặc có blocker thật cần thiết bị/quyền/quyết định.

## 19. DELIVERABLE

Tối thiểu:

- `artifacts/full-audit/FILE_AUDIT_MANIFEST.csv`
- `artifacts/full-audit/REPOSITORY_INVENTORY.md`
- `artifacts/full-audit/BASELINE_BUILD_TEST.md`
- `docs/audit/WMS_RUNTIME_MAP.md`
- `docs/audit/DASHBOARD_FILE_IMPACT_MAP.md`
- `docs/audit/DASHBOARD_METRIC_DICTIONARY.md`
- `docs/audit/DASHBOARD_ROLE_ACTION_MATRIX.md`
- `docs/audit/UI_FILE_IMPACT_MAP.md`
- `docs/audit/UI_BREAKPOINT_AND_REFLOW_CONTRACT.md`
- `docs/audit/WMS_ENTERPRISE_CAPABILITY_MATRIX.md`
- `docs/audit/WMS_SCORECARD_BEFORE.md`
- `docs/audit/WMS_SCORECARD_AFTER.md`
- `docs/audit/WMS_FINDINGS_REGISTER.md`
- `docs/audit/SECURITY_THREAT_MODEL.md`
- `artifacts/security/SECURITY_TEST_REPORT.md`
- `artifacts/data-quality/DATA_QUALITY_REPORT.md`
- `artifacts/performance/PERFORMANCE_REPORT.md`
- `artifacts/visual-full/PLAYWRIGHT_PAGE_MATRIX.csv`
- `artifacts/visual-full/VISUAL_QA_REPORT.md`
- `artifacts/dashboard-command-center/DASHBOARD_DATA_RECONCILIATION.md`
- `artifacts/dashboard-command-center/DASHBOARD_QUERY_PERFORMANCE.md`
- `artifacts/dashboard-command-center/DASHBOARD_PLAYWRIGHT_MATRIX.csv`
- `artifacts/dashboard-command-center/VISUAL_QA_REPORT.md`
- `artifacts/ui-cross-device/UI_ROUTE_ROLE_STATE_VIEWPORT_MATRIX.csv`
- `artifacts/ui-cross-device/UI_DEFECT_REGISTER.md`
- `artifacts/ui-cross-device/UI_ACCESSIBILITY_REPORT.md`
- `artifacts/ui-cross-device/UI_VISUAL_REVIEW_MANIFEST.csv`
- `artifacts/full-audit/TEST_EXECUTION_REPORT.md`
- `artifacts/full-audit/GO_NO_GO.md`
- Screenshots/diffs/traces/videos.
- Code fixes và regression tests.
- Roadmap cập nhật bằng evidence.

Mỗi report ghi commit/build, time, environment/schema, data profile, commands, exit codes, result, artifacts và blocker.

## 20. DEFINITION OF 100%

Internal Readiness = 100% chỉ khi:

- 100% applicable requirement đạt 4/4.
- Evidence Coverage = 100%.
- 100% first-party file có review/coverage status.
- 0 defect mở mọi severity.
- 0 warning chưa phân loại.
- 0 test fail/flaky/skipped critical.
- 0 data-quality/security/scope issue.
- 0 unexpected console/page/network error.
- 0 unresolved visual diff/overflow/overlap/clipping/typo/mojibake.
- Concurrency/idempotency/fault tests pass.
- Migration/restore/DR pass.
- Performance pass.
- UAT/sign-off pass.

ENTERPRISE INTERNAL WMS READY chỉ khi thêm:

- Device/network/printer scope pass.
- Real warehouse pilot pass.
- Opening data reconcile 100%.
- Cutover/rollback pass.
- Training/support/hypercare pass.

Enterprise Parity có thể dưới 100% dù Internal Readiness 100% nếu WMS nội bộ không triển khai yard, 3PL billing, robotics hoặc capability khác. Không được làm sai hai chỉ số để chiều theo mục tiêu.

## 21. FINAL RESPONSE

Final response gồm:

1. Status.
2. Before/after Internal Readiness, Enterprise Parity, Evidence Coverage.
3. Findings by severity trước/sau.
4. Lỗi quan trọng đã sửa.
5. Build/test/migration/data-quality/security/performance/Playwright results.
6. Artifact/report chính.
7. Blocker/unverified scope.
8. Quyết định cần người dùng.

Không nói “đã test tất cả” nếu page matrix/file manifest/evidence chưa 100%. Không kết thúc chỉ bằng đề xuất khi vẫn còn fix an toàn có thể làm trong repository.

Bắt đầu ngay:

1. Đọc instructions và roadmap.
2. Kiểm tra Git status.
3. Lập inventory.
4. Chạy baseline.
5. Báo điểm baseline có evidence.
6. Tiếp tục sửa theo severity và regression; không dừng ở báo cáo.

# KẾT THÚC PROMPT

> **YÊU CẦU CỦA CHỦ DỰ ÁN:** Không được tự ý xóa, thay đổi, rotate, mask hoặc di chuyển các secret, API key và connection string hiện có trong appsettings vì chúng được giữ để deploy lên hosting T3; chỉ được cảnh báo vị trí/rủi ro mà không hiển thị giá trị và chỉ thay đổi khi chủ dự án xác nhận. Tuyệt đối không sao chép giá trị secret sang log, report, artifact hoặc chat.

> **QUY TẮC CẬP NHẬT CHECKLIST:** Hạng mục nào thực sự hoàn thành và đã có test/bằng chứng đạt yêu cầu thì phải cập nhật ngay từ `- [ ]` thành `- [x]` trong roadmap; không tick trước, không tick giả và không tick mục đang FAIL, BLOCKED, NOT TESTED hoặc chưa có evidence.

> **QUY TẮC CORE WMS:** Phải rà soát, mô tả, triển khai và kiểm thử đầy đủ 100% nghiệp vụ core trong Core WMS Function Matrix; không được báo hoàn thành nếu còn một nghiệp vụ core bị thiếu, chỉ có CRUD/menu, sai logic, thiếu dữ liệu, thiếu quyền, thiếu transaction/audit hoặc chưa có automated và Playwright evidence.

> **QUY TẮC TRANG TỔNG QUÁT:** Trước khi sửa Management Command Center phải hoàn thành repository inventory, runtime trace, Dashboard File Impact Map, Metric Dictionary và Role Action Matrix; chỉ sửa file được chứng minh nằm trên runtime path. Dashboard quản lý phải thể hiện đầy đủ việc hôm nay, nhập, xuất, tồn, chuyển, kiểm kê, điều chỉnh, trả hàng, ngoại lệ, workload và sức khỏe hệ thống theo quyền; mọi KPI phải có drill-down, reconciliation, automated test và Playwright visual evidence.

> **QUY TẮC UI CROSS-DEVICE:** Mọi giao diện desktop, laptop, tablet và mobile trong support matrix phải đạt Cross-device UI Zero-Defect Protocol; trước khi sửa phải hoàn thành UI File Impact Map và Breakpoint/Reflow Contract. Không suy diễn lỗi mobile từ ảnh desktop hoặc ảnh crop; chỉ ghi finding sau khi xác nhận viewport và tái hiện trên build hiện tại. Không được báo UI 100% nếu còn một route/role/state/viewport chưa test, một screenshot chưa manual-review hoặc còn overflow, clipping, overlap, menu/modal lỗi, typo, accessibility, console hay network error.
