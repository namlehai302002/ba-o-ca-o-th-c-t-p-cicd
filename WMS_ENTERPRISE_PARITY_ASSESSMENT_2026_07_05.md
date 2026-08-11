# WMS Enterprise Parity Assessment - 2026-07-05

Phạm vi: đánh giá WMS Pro so với nhóm WMS enterprise lớn như Microsoft Dynamics 365 SCM Warehouse, SAP EWM, Oracle Fusion Cloud Warehouse Management và Manhattan Active WM.

Nguyên tắc chấm điểm: không xem "100%" là "không bao giờ còn bug". 100% chỉ có nghĩa là mọi module, dữ liệu, UI, bảo mật, hiệu năng, thiết bị, tích hợp và vận hành đều có bằng chứng nghiệm thu lặp lại được.

## 1. Kết Luận Ngắn

| Góc nhìn | Điểm hiện tại | Ý nghĩa |
|---|---:|---|
| Roadmap WMS report/HSD/FEFO/log control | 100/100 | Các mục trong `ROADMAP_WMS_REPORT_HSD_FEFO_LOG_CONTROL.md` đã tick xong và có evidence build/test/visual/DB. |
| Repo/local engineering readiness | 96/100 | Code, test, UI visual gate, DB audit read-only và tài liệu kiểm soát đang sạch theo các gate hiện có. |
| So với WMS Tier-1 production toàn cầu | 89-91/100 | Functional breadth đã rất rộng, nhưng chưa thể ngang tuyệt đối với Tier-1 vì thiếu UAT thiết bị thật, load/soak thật, DR/HA, pentest, monitoring thật và certified integrations. |
| Khả năng dùng cho WMS nội bộ nâng cao | 94-96/100 | Đủ mạnh để demo, vận hành thử có kiểm soát và làm nền triển khai nội bộ nếu có quy trình backup/UAT/monitoring. |

## 2. Benchmark Chính Thức Đã Đối Chiếu

- Microsoft Dynamics 365 Warehouse Management nhấn mạnh inbound/outbound workflow, mobile devices, batch/serial, nhiều picking strategy, label/ZPL, Power BI, QMS, traceability, wave processing, packing/containerization và cross-docking.
- Dynamics WMS-only mode nhấn mạnh tích hợp ERP/OMS ngoài, automation integration, carrier integration và Warehouse Management mobile app.
- Oracle Fusion Cloud Warehouse Management nhấn mạnh visibility từ DC đến store shelf, KPI warehouse, receiving/picking/packing/shipping, wave management, inventory tracking, labor optimization, AI exception và MHE integration.
- SAP EWM Warehouse Cockpit/KPI/Yard/Slotting nhấn mạnh cockpit key figures, exception, yard management, slotting/rearrangement và monitor vận hành.
- Manhattan Active WM nhấn mạnh unified distribution control, inbound-to-outbound visibility, labor, slotting, automation, robotics/MHE và tối ưu fulfillment theo thời gian thực.

Nguồn:

- `https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview`
- `https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/wms-only-mode-overview`
- `https://docs.oracle.com/en/cloud/saas/supply-chain-and-manufacturing/26a/faips/about-oracle-fusion-cloud-warehouse-management.html`
- `https://www.oracle.com/scm/logistics/warehouse-management/`
- `https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/9832125c23154a179bfa1784cdc9577a/7dcacb53ad377114e10000000a174cb4.html`
- `https://www.manh.com/solutions/supply-chain-management-software/warehouse-management`

## 3. WMS Pro Đang Có Gì

| Nhóm capability | Trạng thái WMS Pro | Mức so với enterprise |
|---|---|---:|
| Inbound/receiving/putaway | Có phiếu, duyệt, nhận, QC, putaway, RF receiving | 90-95% |
| Outbound/reservation/picking/packing/shipping | Có reservation, FEFO, pick tasks, RF picking, shipping dispatch, chống xuất quá tồn | 90-95% |
| Inventory accuracy | Có tồn theo kho/vị trí/lô/serial/owner, stock count, stock snapshot, movement ledger | 92-96% |
| Reporting/cockpit | Có tổng quan kho, nhập/xuất kỳ, định giá tồn, BI, predictive, SRE, audit | 88-94% |
| Mobile/RF/offline | Có mobile shell, RF screens, scan parser, offline queue | 80-88% vì chưa có UAT thiết bị thật |
| Slotting/replenishment/optimization | Có slotting, simulation, replenishment, optimization dashboard | 80-88% |
| Yard/dock | Có yard management, dock board, evidence upload, yard billing | 80-88% |
| Labor/productivity | Có labor productivity, approval, productivity rules | 78-86% |
| MHE/automation/carrier | Có dashboard/connector/callback/replay | 75-85% vì chưa certified với thiết bị/hãng thật |
| 3PL billing | Có contract, rate, run, invoice, dispute, export | 85-92% |
| Security/audit/scope | Có role, warehouse scope, owner scope, CSRF, audit, data quality | 90-95% |
| Observability/SRE | Có SRE dashboard, telemetry controls, health/data-quality audit | 78-88% vì thiếu production monitoring/alerting thật |

## 4. Evidence Mới Nhất 2026-07-05

| Gate | Kết quả |
|---|---|
| Build | `dotnet build WMS.csproj --no-restore`: pass, `0 warning / 0 error` |
| .NET tests | `dotnet test WMS.Tests\WMS.Tests.csproj --no-build`: pass, `691/691` |
| DB hosting audit | `artifacts/data-quality/wms-data-quality-audit-20260705-130335.txt`: `0` issue rows across `17` issue groups |
| Visual UI | `npm run visual:test`: `194 passed / 66 skipped` |
| Server log | `logs/wms-overview-run.err.log`: `0` byte |
| Config rule | Không sửa/xóa `appsettings.json` hoặc `appsettings.Development.json` |

## 5. Các Gap Còn Lại Để Gọi Là 100% Enterprise Production

| ID | Việc còn thiếu | Lý do bắt buộc nếu so với WMS lớn | Definition of Done |
|---|---|---|---|
| HW-RF-001 | UAT scanner RF/handheld thật cho receiving, picking, movement | Enterprise WMS sống bằng thiết bị kho thật, không chỉ viewport Playwright | Có video/log/screenshot đã redact, thiết bị quét đúng, sai mã bị chặn, mất mạng hồi phục được |
| HW-PRINT-001 | Test máy in tem/ZPL và chứng từ thật | Dynamics/Oracle/Manhattan đều coi label/scan là lõi vận hành | Tem đọc được bằng scanner thật, đúng page size, đúng barcode, đúng lô/HSD/serial |
| HW-SCALE-001 | Test cân điện tử/catch-weight thật nếu dùng hàng theo cân | Chênh cân là lỗi tài chính/tồn kho | Cân gửi dữ liệu đúng, lệch ngưỡng bị cảnh báo, audit có bằng chứng |
| LOAD-001 | Load/soak test staging | Không có p95/p99/error-rate thì chưa thể gọi production scale | k6/JMeter/Locust hoặc công cụ duyệt sẵn, có p95, p99, throughput, error rate, SQL CPU/IO |
| DR-001 | Backup/restore và rollback drill | WMS mất dữ liệu là sự cố nghiêm trọng | Restore DB thành công, RPO/RTO ghi rõ, key ring/attachment backup kiểm được |
| SEC-001 | Pentest/WAF/secret rotation/external IdP | Enterprise cần bằng chứng bảo mật ngoài unit test | Báo cáo pentest, rotation record, MFA/IdP sign-off, header/WAF evidence |
| INT-ERP-001 | Certified ERP/OMS/TMS/carrier/MHE integration | Hệ lớn có integration contract và retry/replay chuẩn | Sandbox/prod certification, idempotency, retry, DLQ, reconciliation report |
| OBS-001 | Monitoring/alerting/SLO thật | Tier-1 không chỉ có log, phải có cảnh báo vận hành | Dashboard, alert rule, incident runbook, on-call test, synthetic checks |
| UAT-OPS-001 | UAT có ký nhận người dùng kho thật | Test tự động không thay thế thao tác vận hành thật | Biên bản UAT theo vai trò Admin/Manager/Staff/Viewer và 3-5 luồng nghiệp vụ chính |
| ACC-001 | Accessibility/browser/device matrix thật | "Không lỗi giao diện" cần nhiều thiết bị và trình duyệt thật | WCAG smoke, Chrome/Edge/Safari, tablet/handheld, zoom 100/125/150 |

## 6. Kết Luận 100%

Roadmap hiện tại đã xong 100% theo phạm vi đã ghi. Để hệ thống được gọi là 100% như WMS enterprise toàn cầu, phần code hiện không phải nút thắt lớn nhất nữa; nút thắt còn lại là bằng chứng production: thiết bị thật, tải thật, DR/HA, pentest, monitoring, chứng nhận tích hợp và UAT ký nhận.

Không nên tuyên bố "0 bug tuyệt đối". Cách chuyên nghiệp là tuyên bố: mọi known issue trong phạm vi gate hiện tại đã về 0, và các gate ngoài môi trường local đang được quản lý bằng checklist evidence riêng.
