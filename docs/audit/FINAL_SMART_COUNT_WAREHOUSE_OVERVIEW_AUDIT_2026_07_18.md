# Rà soát cuối: Kiểm kê thông minh và Tổng quan kho

Ngày xác minh: 18/07/2026  
Phạm vi: runtime kiểm kê thông minh, đề xuất kiểm kê, Tổng quan kho, đối soát thống kê, chất lượng dữ liệu, cuộn trang và cache giao diện.  
Kết luận cho phạm vi đã kiểm: `PARTIALLY VERIFIED`. Không dùng báo cáo này để suy diễn đã đạt 0 lỗi trên thiết bị, pilot hoặc dữ liệu production chưa được kiểm thử.

## 1. Finding đã xác nhận và xử lý

| ID | Trạng thái | Runtime path | Root cause | Xử lý và rollback |
|---|---|---|---|---|
| AI-UI-01 | CONFIRMED / FIXED | `GET /Reports/InventoryRisk` | Mã DQ kỹ thuật có thể bị hiển thị trực tiếp cho nhân viên vận hành và nhãn trạng thái chưa đủ rõ. | Mọi mã `PARTIAL_*`/`BLOCKED_*` được ánh xạ sang nhãn nghiệp vụ tiếng Việt trong `InventoryRiskUiLabels`; Playwright chặn tái xuất hiện mã kỹ thuật. Rollback riêng `ViewModels/InventoryRiskViewModels.cs` và view liên quan. |
| AI-UI-02 | CONFIRMED / FIXED | `GET /Reports/InventoryRisk`, `GET /Reports/InventoryRiskRecommendations`, `GET /Reports/WarehouseOverview` | Vùng bảng có cuộn dọc riêng làm bánh xe không tiếp tục cuộn trang khi con trỏ nằm trên bảng/panel. | Bảng chỉ giữ cuộn ngang; cuộn dọc được chuyển cho trang. Playwright kiểm tra `scrollHeight`, `overflow-y`, `overscroll-behavior-y` và thao tác wheel thật. Rollback các rule có prefix `inventory-risk`, `recommendation` và `warehouse-overview` trong `wwwroot/css/site.css`. |
| AI-GRAIN-01 | CONFIRMED / FIXED | `GET /Reports/InventoryRisk`, workflow tạo phiếu kiểm kê | Dòng kiểm kê hiện tại không lưu từng số sê-ri, nên xếp hạng rồi tạo phiếu cho vật tư quản lý số sê-ri có thể tạo phạm vi không đủ grain. | Phạm vi serial được đánh dấu `Chưa thể chấm điểm`, không được tạo recommendation/phiếu tự động. Rollback rule `BLOCKED_SERIAL_COUNT_NOT_SUPPORTED` nếu sau này mô hình dòng kiểm kê serial được triển khai đầy đủ. |
| AI-GRAIN-02 | CONFIRMED / FIXED | chấm điểm và `POST /Reports/ApproveStockCount` | Một scope có nhiều `HoldStatus` nhưng `StockCountLine` không lưu `HoldStatus`; adjustment trước đây có nguy cơ chọn bucket đầu tiên không xác định. | Chặn chấm điểm/tạo phiếu cho scope nhiều trạng thái tồn; approval có variance cũng từ chối trước khi điều chỉnh. Rollback cục bộ trong `InventoryRiskScoringService` và `ReportsController.StockCount`. |
| STAT-01 | CONFIRMED / FIXED | `GET /Reports/WarehouseOverview` | Dòng nhập/xuất từng dựa vào dấu quantity nên có thể tính nhầm `Move`, `Putaway`, `Adjust` thành gross flow; danh sách mã phát sinh nhiều cũng có thể giữ SKU chỉ có luồng nội bộ dưới dạng `0/0`. | Chỉ phân loại nghiệp vụ nhập: `Receive`, `TransferIn`, `KitProduce`; nghiệp vụ xuất: `Ship`, `TransferOut`, `KitConsume`, `VasConsume`. Regression thêm giao dịch nội bộ và một SKU chỉ có `Move` để chứng minh không làm tăng nhập/xuất hoặc xuất hiện ở top item. Rollback helper/predicate phân loại transaction type trong controller. |
| DQ-LOCATION-01 | CONFIRMED / FIXED | Tổng quan kho và Tier-1 DQ | Cảnh báo trộn SKU không xét `Location.AllowMixedSku`, tạo false positive cho vị trí được cấu hình cho phép trộn. | Chỉ cảnh báo nhiều SKU khi vị trí không cho phép trộn; vẫn luôn cảnh báo trộn nhiều chủ hàng. Rollback predicate trong controller và `Tier1DataQualityAuditService`. |
| UI-CACHE-01 | CONFIRMED / FIXED | tải CSS/JS qua service worker | Shell asset dùng cache-first có thể giữ giao diện cũ sau khi publish bản sửa. | Đổi shell asset sang network-first, cache response thành công và chỉ fallback cache khi offline; tăng cache version. Rollback riêng `wwwroot/service-worker.js`. |

## 2. Tệp runtime và test đã rà soát/tác động

- `Services/InventoryRiskScoringService.cs`
- `ViewModels/InventoryRiskViewModels.cs`
- `Controllers/ReportsController.InventoryRiskRecommendations.cs`
- `Controllers/ReportsController.StockCount.cs`
- `Controllers/ReportsController.WarehouseOverview.cs`
- `Services/Tier1DataQualityAuditService.cs`
- `Views/Reports/InventoryRisk.cshtml`
- `Views/Reports/InventoryRiskRecommendations.cshtml`
- `Views/Reports/WarehouseOverview.cshtml`
- `wwwroot/css/site.css`
- `wwwroot/service-worker.js`
- `WMS.Tests/InventoryRiskScoringTests.cs`
- `WMS.Tests/BusinessLogicHardeningTests.cs`
- `WMS.Tests/Tier1ProductionEvidenceGateTests.cs`
- `WMS.Tests/WorldClassZeroMarkerEvidenceTests.cs`
- `tests/visual/wms-ai-inventory-risk.spec.ts`
- `tests/visual/wms-visual-regression.spec.ts`
- `docs/audit/AI_FEATURE_DICTIONARY.md`

## 3. Build và regression evidence

| Kiểm tra | Exit code | Kết quả | Evidence |
|---|---:|---|---|
| Targeted xUnit cho AI/overview/DQ/stock-count/service-worker | 0 | 36/36 pass | `artifacts/final-audit/test-results/targeted-ai-overview-rerun-20260718.trx` |
| Regression SKU chỉ điều chuyển nội bộ | 0 | 1/1 pass | `artifacts/final-audit/test-results/warehouse-overview-internal-flow-20260718.trx` |
| `dotnet build WMS.sln -c Release --no-restore --nologo` | 0 | 0 warning, 0 error | `artifacts/final-audit/final-release-build-20260718.txt` |
| Full .NET regression trên runtime cuối | 0 | 1.128/1.128 pass | `artifacts/final-audit/test-results/final-full-regression-runtime-20260718.trx` |
| Playwright visual chính | 0 | 211 pass, 81 skip theo project/fixture, 0 fail | `artifacts/visual-authenticated/playwright-report/index.html` |
| AI-2 kiểm kê thông minh, 6 viewport | 0 | 6/6 pass | `artifacts/ai-smart-cycle-count/AI2/playwright-report/index.html` |
| AI-3 đề xuất kiểm kê, 6 viewport | 0 | 6/6 pass | `artifacts/ai-smart-cycle-count/AI3/playwright-report/index.html` |
| AI-1 analytics, 4 viewport | 0 | 4/4 pass | `artifacts/ai-smart-cycle-count/AI1/playwright-report/index.html` |
| Gate 7 Command Center | 0 | 9/9 pass, gồm warm p95 | `artifacts/dashboard-command-center/playwright-report/index.html` |
| NuGet vulnerability audit | 0 | 0 package có advisory trong nguồn hiện tại | `artifacts/final-audit/nuget-vulnerability-audit-20260718.txt` |
| npm audit | 0 | 0 vulnerability | `artifacts/final-audit/npm-audit-20260718.json` |
| Quét chuỗi mojibake first-party | 0 | 0 file nghi vấn; loại trừ vendor/generated/artifact | `artifacts/final-audit/mojibake-scan-20260718.txt` |

`81 skip` của visual chính là các test có điều kiện chỉ chạy ở project/viewport hoặc cần fixture chuyên biệt; đây không phải test fail. Các workflow cần fixture thật vẫn phải được đánh giá riêng trước production.

## 4. Đối soát DB hosting chỉ đọc

Hai script được guard chỉ cho phép truy vấn đọc; không migration, không ghi dữ liệu và không thay đổi chứng từ/tồn kho:

- `scripts/WmsAiCycleCountReadinessAudit.sql`
- `scripts/WmsAiStatisticsReconciliation.sql`

Evidence chi tiết:

- `artifacts/final-audit/ai-cycle-count-readiness-final-detailed-20260718.txt`
- `artifacts/final-audit/ai-statistics-reconciliation-final-detailed-20260718.txt`

Kết quả chính:

- 10 bucket tồn được đối soát; 0 lệch quantity và 0 lệch reservation giữa tồn hiện tại với ledger.
- 1 dòng kiểm kê đã duyệt có đủ counted quantity/variance và khớp chính xác; đây là mẫu quá nhỏ để kết luận chất lượng mô hình.
- Không có tracked lot/HSD thiếu hoặc warehouse-location mismatch trong tập evidence áp dụng. Hai script này không dùng để kết luận về serial duplicate toàn hệ thống.
- 7 SKU đang có tồn đều chưa có lịch sử outbound đủ dùng; dữ liệu tuổi tồn chưa đủ chiều sâu.
- 7 phiếu nhập đã post chưa có đủ milestone để tính dock-to-stock; 7 luồng xuất chưa có đủ milestone để tính outbound lead time/line fill.
- Chưa có lịch kiểm kê chủ động; lịch sử được duyệt chỉ có một ngày.

Vì vậy các KPI nguồn sự thật và reconciliation hiện tại là PASS, còn khả năng huấn luyện/đánh giá AI theo thời gian là `BLOCKED` bởi độ sâu dữ liệu, không phải lỗi số dư tồn hiện tại.

## 5. Đối chiếu thông lệ nghiệp vụ

Thiết kế sau sửa giữ ba nguyên tắc được mô tả trong tài liệu chính thức của các hệ thống WMS lớn:

- Đề xuất kiểm kê dựa trên dữ liệu chỉ hỗ trợ ưu tiên công việc; nhân sự có quyền vẫn quyết định và xác nhận kết quả.
- Phiếu kiểm kê phải có scope/grain rõ, hỗ trợ kiểm soát blind count, reason và approval trước adjustment.
- Chênh lệch kiểm kê không được tự ý đổi tồn; adjustment phải đi qua workflow, permission, ledger và audit.

Nguồn tham khảo:

- Oracle Intelligent Cycle Counting: <https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owaig/intelligent-cycle-counting.html>
- Microsoft Dynamics 365 cycle counting: <https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/cycle-counting>
- Microsoft Dynamics 365 counting reason codes: <https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/reason-codes-for-counting-journals>
- SAP cycle counting: <https://help.sap.com/docs/SAP_SUPPLY_CHAIN_MANAGEMENT/57574d15fa1d414792d74047b66c3e41/4cb4e7e04c1f1921e10000000a15822b.html>

## 6. PASS/FAIL/BLOCKED

- PASS: mã DQ không rò ra UI; nhãn vận hành là tiếng Việt.
- PASS: cuộn trang khi con trỏ nằm trên bảng AI/Tổng quan kho; không còn nested vertical scroll tại các vùng đã kiểm.
- PASS: KPI nhập/xuất loại giao dịch nội bộ khỏi gross flow và giữ cùng kết quả ở desktop/laptop/tablet/mobile.
- PASS: vị trí cho phép trộn SKU không còn bị báo lỗi giả; trộn owner vẫn bị chặn/cảnh báo.
- PASS: serial và multi-hold scope không tạo adjustment mơ hồ.
- BLOCKED: độ chính xác/lift của model AI production vì chỉ có một outcome kiểm kê đã duyệt và chưa có temporal depth.
- BLOCKED: KPI dock-to-stock/outbound lead time production vì dữ liệu hiện có thiếu milestone hợp lệ.
- BLOCKED: xác nhận trên thiết bị quét/in thật, ca kho thật, pilot và production deployment.

Không có mục `FAIL` trong phạm vi test vừa chạy. Điều này không phải lời cam kết tuyệt đối rằng mọi đường chạy ngoài fixture, thiết bị hoặc pilot đều không thể có lỗi.

## 7. Bảo vệ cấu hình

`appsettings.json` không được sửa. SHA-256 trước và sau vòng audit cùng là `7A3E4A74C0D7D7CBA0AF5EB91A65B06764CCDF38B79798D9C0063188A3C4A1EC`. Báo cáo không chứa giá trị secret, API key hoặc connection string. Evidence: `artifacts/final-audit/secret-guard-and-appsettings-hash-20260718.txt`.
