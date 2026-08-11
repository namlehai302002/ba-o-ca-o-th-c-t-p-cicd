# AI Smart Cycle Counting - Runtime Map

Ngày chốt baseline: 16/07/2026  
Phạm vi: AI-0 đến AI-2; truy vết runtime, hợp đồng dữ liệu, chuẩn hóa báo cáo nguồn sự thật và rule baseline ở chế độ thử nghiệm. Không áp dụng migration hoặc ghi dữ liệu AI lên hosting.

## 1. Nguồn chuẩn và kết luận baseline

- Roadmap nguồn: `ROADMAP_WMS_AI_NATIVE_SMART_CYCLE_COUNTING_AND_ANALYTICS.md`.
- Tính năng hiện có là kiểm kê chu kỳ theo ABC/lịch đến hạn và cảnh báo theo luật. Chưa có mô hình ML, feature snapshot, prediction version hoặc outcome loop.
- AI chỉ được xếp hạng và đề xuất. Mọi phiếu kiểm kê, duyệt sai lệch và điều chỉnh tồn vẫn phải đi qua người dùng, quyền, SoD và transaction hiện có.
- DQ hosting chỉ có 1 phiếu kiểm kê đã duyệt, 1 dòng khớp, 1 ngày dữ liệu và không có dòng sai lệch. Dữ liệu này đủ smoke workflow nhưng không đủ train/temporal benchmark.

## 2. Đối chiếu thực hành chính thức

Thiết kế contract tham chiếu các nguồn chính thức sau:

- [Oracle Intelligent Cycle Counting](https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owaig/intelligent-cycle-counting.html): ưu tiên đối tượng dễ sai dựa trên độ chính xác lịch sử, pick, return, adjustment và hoạt động vị trí.
- [Dynamics 365 cycle counting](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/cycle-counting): tạo công việc, kiểm đếm, đưa chênh lệch vào review; hỗ trợ blind count và ngân sách đếm.
- [Dynamics 365 reason codes](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/reason-codes-for-counting-journals): reason phải có cấu trúc để phân tích nguyên nhân.
- [ML.NET task guidance](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks) và [Permutation Feature Importance](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/explain-machine-learning-model-permutation-feature-importance-ml-net): chỉ chọn trainer sau benchmark và giữ explanation truy vết được.
- [NIST AI Resource Center](https://airc.nist.gov/): quản trị vòng đời, theo dõi, audit và fallback là bắt buộc trước khi gọi hệ thống AI-native.

## 3. Runtime path kiểm kê hiện tại

| Bước | Route/symbol | Quyền | Transaction và bảng chính | Trạng thái |
|---|---|---|---|---|
| Tạo chương trình | `POST /Operations/CreateCycleCountProgram` / `OperationsController.CreateCycleCountProgram` | `StockCountApprove` | `CycleCountPrograms` | Tạo cấu hình A/B/C, blind count, ngưỡng phần trăm |
| Chạy chương trình | `POST /Operations/RunCycleCountProgram` | `StockCountApprove` | Gọi `CreateOrRefreshSchedulesAsync` rồi `GenerateDueSheetAsync` | Tạo work đến hạn |
| Lập lịch | `CycleCountPlanningService.CreateOrRefreshSchedulesAsync` | Qua route gọi | `ItemLocations`, `Locations`, `CycleCountSchedules` | Grain lịch: program-owner-item-location |
| Tạo phiếu | `CycleCountPlanningService.GenerateDueSheetAsync` | Qua route gọi | Serializable; `CycleCountSchedules`, `StockCountSheets`, `StockCountLines`, `ItemLocations` | `Draft`; không cập nhật `LastCountedAt` |
| Bắt đầu đếm | `POST /Reports/StockCountStart` | Policy kiểm kê hiện có | `StockCountSheets` | `Draft -> Counting` |
| Gửi kết quả | `POST /Reports/StockCountSubmit` | Policy kiểm kê hiện có | Serializable; `StockCountSheets`, `StockCountLines` | `Draft/Counting -> Counted` |
| Yêu cầu đếm lại | `POST /Reports/StockCountRequestRecount` | Policy phê duyệt | Transaction; reset kết quả dòng | `Counted -> Counting` |
| Duyệt | `POST /Reports/ApproveStockCount` | `StockCountApprove` | Serializable; sheet/line, voucher điều chỉnh, inventory ledger/balance | `Counted -> Approved` |
| Hoàn tất lịch | `CycleCountPlanningService.CompleteApprovedSheetAsync` | Trong approve transaction | `CycleCountSchedules` | Cập nhật `LastCountedAt`, lịch kế tiếp, variance sau duyệt |
| Mở khóa | `POST /Reports/UnlockStockCount` | Quyền nâng cao hiện có | `StockCountSheets` | `Approved -> Counted`; cần được loại khỏi dataset ổn định tại cutoff |

## 4. Invariant đã xác minh

1. `LastCountedAt` là mốc phiếu được duyệt (`ApprovedAt`), không phải lúc tạo work.
2. Lịch kế tiếp được tính từ ngày duyệt và tần suất A/B/C của chương trình.
3. Tạo phiếu không được tạo scope active trùng kho-owner-item-location khi đã có sheet `Draft`, `Counting` hoặc `Counted`.
4. Cập nhật lịch và duyệt/điều chỉnh tồn nằm trong cùng transaction hiện có.
5. Gọi hoàn tất lịch lặp lại với cùng `ApprovedAt` là idempotent, không cộng variance hai lần.
6. Phiếu chưa duyệt, bị hủy hoặc đã mở khóa không được dùng làm outcome ổn định.

## 5. Runtime cảnh báo dự báo hiện tại

| Thành phần | Runtime | Đánh giá |
|---|---|---|
| Cảnh báo | `GET /Reports/PredictiveAlerts` -> `EnterpriseAnalyticsService.BuildPredictiveAlertsAsync` | Rule-based, không phải ML |
| Nguồn | `ItemLocations`, item/expiry/SLA/capacity và `EnterprisePredictiveAlerts` | Có thể dùng làm baseline sau khi chuẩn hóa scope/công thức |
| Lưu projection | `UpsertPredictiveAlertsAsync` | Alert là projection; chưa có version, cutoff, feature hash, DQ hoặc outcome |
| Trợ lý nội bộ | `EnterpriseAnalyticsService.AskAssistantAsync` | Chỉ đọc dữ liệu/alert; không được xem là model kiểm kê |

`EnterprisePredictiveAlert` không đủ làm source of truth cho AI-native. Mọi thiết kế bảng prediction/model/recommendation vẫn là ứng viên và chỉ được tạo sau migration rehearsal trên DB dùng một lần.

## 6. Scope và grain

- Grain dự đoán chuẩn: `WarehouseId + OwnerPartnerId + ItemId + LocationId + LotNumber + ExpiryDate + PredictionCutoff`.
- Grain work/lịch hiện có: `ProgramId + OwnerPartnerId + ItemId + LocationId`; khi tạo sheet phải bung ra lot/HSD để không mất provenance.
- `OwnerPartnerId = NULL` là phạm vi hàng nội bộ/chưa gắn owner, không phải wildcard và không được trộn với owner cụ thể.
- Số lượng chuẩn hóa về base UOM của item. Serial/LPN là cờ và drill-down; không cộng chuỗi serial vào quantity feature.
- Warehouse/owner scope phải được áp dụng đồng nhất ở query, export, API trực tiếp, job và cache.

## 7. Data-quality baseline

Query read-only: `scripts/WmsAiCycleCountReadinessAudit.sql`.  
Evidence: `artifacts/ai-smart-cycle-count/ai-cycle-count-dq.txt`.

| Kiểm tra | Kết quả |
|---|---|
| Phiếu kiểm kê | 1 approved; không thiếu `ApprovedAt` |
| Label dòng | 1/1 có `CountedQty` và variance; 1 exact; 0 variance |
| Scope | 1 dòng owner null; không có lỗi lot/HSD hoặc warehouse-location |
| Lịch active | 0; không có dữ liệu cần repair tự động |
| Reason | Không có variance; schema chưa có reason dòng có cấu trúc |
| Temporal depth | 1 ngày approved, span 0 ngày |
| Multi-owner | 0 phiếu multi-owner |

Kết luận: `BLOCKED_FOR_ML_TRAINING`, `PASS_FOR_WORKFLOW_SMOKE`. Không sửa lịch sử hosting và không tạo nhãn tổng hợp giả.

## 8. Finding register của AI-0

| ID | Trạng thái | Root cause | Xử lý/evidence |
|---|---|---|---|
| `AI-BL-01` | `CONFIRMED -> FIXED` | `GenerateDueSheetAsync` từng coi tạo phiếu là đã kiểm kê | Chuyển mốc sang approval; regression trong `CoreWmsCompletionTests` và `BusinessLogicHardeningTests` |
| `AI-DQ-01` | `CONFIRMED` | Lịch sử không đủ độ sâu và không có positive variance | Giữ ABC/rule baseline; chặn ML gate |
| `AI-DQ-02` | `CONFIRMED` | Chưa có reason code cấu trúc theo dòng | STAT-09 và AI-3 chưa thể COMPLETE |
| `AI-DATA-01` | `CONFIRMED` | `StockCountSheet` không giữ `ProgramId`/threshold snapshot | Material label là `UNKNOWN` nếu không truy ra cấu hình versioned |
| `AI-ARCH-01` | `CONFIRMED` | Cảnh báo hiện tại không lưu feature/model/outcome provenance | Chỉ dùng làm projection/fallback, không làm kho AI duy nhất |

## 9. Finding register của AI-1

| ID | Trạng thái | Xử lý đã kiểm chứng | Evidence |
|---|---|---|---|
| `STAT-BL-01` | `CONFIRMED -> FIXED` | Days of Supply tính riêng từng SKU bằng tồn khả dụng và outbound base UOM trong ledger; áp dụng kho-owner scope | `AiAnalyticsSourceOfTruthTests`, Playwright 3 viewport |
| `STAT-BL-02` | `CONFIRMED -> FIXED` | Hàng chậm tách lần nhận và lần xuất/tiêu thụ gần nhất; receipt mới không reset tuổi chậm | `AiAnalyticsSourceOfTruthTests` |
| `STAT-BL-03` | `CONFIRMED -> FIXED_BY_LABEL` | Route cũ giữ nguyên, báo cáo được định danh rõ là ABC theo giá trị tồn hiện tại; SKU thiếu valuation không bị gán giá giả | Unit regression và UI evidence |
| `STAT-BL-04` | `CONFIRMED -> FIXED` | Tách occupancy khỏi capacity; chỉ tính tải kg khi capacity và trọng lượng đều hợp lệ | Unit regression và UI evidence |
| `STAT-BL-05` | `CONFIRMED -> FIXED` | Bỏ fallback timestamp; thêm completeness, median/P90/P95, sample và drill-down | Unit regression và UI evidence |
| `STAT-BL-06` | `PARTIAL` | Hợp đồng line fill đã có query độc lập nhưng hosting chưa có dòng posted đủ điều kiện | `scripts/WmsAiStatisticsReconciliation.sql` |
| `AI-DQ-03` | `CONFIRMED / BLOCKED_DATA` | Hosting có 0/7 inbound đủ milestone, 0/7 outbound đủ milestone và 0 line fill eligible | Giữ KPI `Chưa đủ dữ liệu`, không dựng số giả |

## 10. Rollback

- Code: gỡ lời gọi `CompleteApprovedSheetAsync`, trả lại interface/service và test liên quan; không có schema rollback.
- Dữ liệu: thay đổi này không chạy backfill và không ghi hosting trong audit. Nếu rollback sau vận hành, phải giữ `LastCountedAt` đã duyệt vì đó là dữ liệu nghiệp vụ hợp lệ.
- Artifact audit có thể xóa độc lập; không ảnh hưởng runtime.

## 11. Runtime AI-2 đã kiểm chứng

| Bước | Route/symbol | Quyền | Dữ liệu và side effect | Trạng thái |
|---|---|---|---|---|
| Xem xếp hạng | `GET /Reports/InventoryRisk` -> `InventoryRiskScoringService.BuildPageAsync` | Role vận hành/báo cáo phù hợp + `ReportView` | Chỉ đọc balance, ledger, count outcome và lịch kiểm kê theo kho/chủ hàng | `PASS` |
| Chấm rule | `InventoryRiskScoringService.Score` | Qua route/service | Rule `RULE-BASELINE-1.0`, feature schema `AI-FEATURE-SCHEMA-0.1`, output hash xác định | `PASS` |
| Lưu thử nghiệm | `POST /Reports/InventoryRiskShadowRefresh` -> `PersistShadowBatchAsync` | Admin/Manager + `StockCountApprove` | Chỉ ghi ba bảng AI mới; không tạo phiếu, reservation, ledger hoặc thay đổi `ItemLocations` | `PASS_ON_CLONE`, hosting chưa migrate |
| Drill-down | `InventoryTransactions`, `StockCount` | Quyền route đích hiện có | Truy ngược nguồn, không gửi quyết định AI | `PASS` |

Grain triển khai là warehouse-owner-item-location-lot-expiry. Dữ liệu lỗi invariant được gắn `BLOCKED` và không có điểm; dữ liệu thiếu lịch sử được gắn `PARTIAL`. Màn hình ghi rõ điểm ưu tiên không phải xác suất và tự khóa nút lưu khi schema AI chưa tồn tại.

Migration `20260716065936_AddInventoryRiskShadowBaseline_AI2` là additive: ba bảng, tám index/FK, không `ALTER`, `DROP`, `UPDATE` hoặc `DELETE`. Rehearsal và persistence test chạy trên SQL Server clone `AUDIT_TEST_AI2_*` dùng một lần; số clone còn lại sau test bằng 0. Evidence: `artifacts/ai-smart-cycle-count/AI2/AI2_EVIDENCE.md`.
