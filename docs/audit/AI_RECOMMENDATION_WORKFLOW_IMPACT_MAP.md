# AI-3 Recommendation Workflow - Impact Map

Ngày chốt thiết kế: 16/07/2026  
Trạng thái trước triển khai: `CONFIRMED`, chưa apply schema lên hosting.

## 1. Runtime path và consumer

| Thành phần | Runtime hiện có | Thay đổi tối thiểu |
|---|---|---|
| Chấm điểm | `InventoryRiskScoringService` -> feature snapshot/prediction | Giữ nguyên; recommendation chỉ đọc prediction bất biến đã lưu |
| Lập kế hoạch kiểm kê | `ICycleCountPlanningService` / `CycleCountPlanningService` | Thêm một phương thức tạo phiếu từ đúng scope recommendation đã duyệt, dùng transaction và duplicate policy hiện có |
| Kiểm đếm | `ReportsController.StockCountStart/Submit/RequestRecount` | Đồng bộ trạng thái recommendation trong cùng unit of work; không đổi public route/model cũ |
| Duyệt sai lệch | `ReportsController.StockCountApproveDraft` | Đóng recommendation sau approval; SoD, period lock, ledger và adjustment hiện có giữ nguyên |
| Mở khóa | `ReportsController.StockCountUnlockApproved` | Đưa recommendation về chờ review kết quả; không dùng outcome chưa ổn định |
| UI | `Reports/InventoryRisk` và route con mới `InventoryRiskRecommendations` | Một lối vào phụ, không thêm menu sidebar mới |

## 2. Phương án đã so sánh

1. Tạo phiếu trực tiếp trong controller AI: loại vì lặp duplicate, blind count và transaction policy.
2. Nhét recommendation vào `CycleCountPlanningService`: loại vì service lập kế hoạch không nên sở hữu review/audit state machine.
3. Chọn: service recommendation nhỏ quản lý state/decision; mở rộng planning service đúng một primitive tạo sheet đã duyệt. Đây là blast radius thấp nhất và tái sử dụng workflow kiểm kê hiện có.

## 3. Invariant

- Prediction và snapshot là bất biến; mỗi prediction chỉ có tối đa một recommendation.
- Mọi approve/modify/reject lưu actor, thời điểm, before/after, reason code, note, scope và model version.
- Chỉ `Approved` hoặc `Modified` được tạo phiếu; thao tác lặp trả lại cùng sheet.
- Scope có bất kỳ feature đầu vào mới, movement/balance mới hoặc quá freshness chuyển `Invalidated/Expired`, không âm thầm tạo phiếu. Batch dùng watermark legacy bị chặn và yêu cầu chấm điểm lại.
- Grain chuẩn là `warehouse-owner-item-location-lot-expiry`; giá trị `null` là một bucket nghiệp vụ, không phải wildcard. Active duplicate cùng đúng grain bị chặn trong transaction; lô/HSD khác vẫn được phép lập phiếu riêng.
- Phiếu sinh ra luôn là blind count; trong giai đoạn `Draft/Counting`, UI không render `SystemQty` vào DOM, kể cả hidden input. Sau khi gửi kết quả, chỉ Manager/Admin có quyền review mới xem được số hệ thống.
- Người đã nhập số đếm không được tự duyệt kết quả. Vi phạm SoD được chặn và ghi audit `SOD_BLOCK`.
- Recommendation không ghi balance, reservation, ledger hoặc adjustment. Adjustment chỉ có thể phát sinh khi một người khác duyệt kết quả bằng workflow hiện có.
- Hosting thiếu schema thì route chỉ hiển thị trạng thái chưa sẵn sàng; không chạy migration tự động.

## 4. File tác động dự kiến

- `Models/InventoryRiskModels.cs`
- `ViewModels/InventoryRiskViewModels.cs`
- `Services/InventoryRiskRecommendationService.cs` (mới)
- `Services/CoreWmsServices.cs`
- `Controllers/ReportsController.cs`
- `Controllers/ReportsController.InventoryRisk.cs`
- `Controllers/ReportsController.StockCount.cs`
- `Data/AppDbContext.cs`
- `Program.cs`
- `Views/Reports/InventoryRisk.cshtml`
- `Views/Reports/InventoryRiskRecommendations.cshtml` (mới)
- `Views/Shared/_Layout.cshtml`
- Migration additive AI-3, unit/relational integration/Playwright tests và evidence tương ứng. Migration chỉ tạo hai bảng AI-3; không sửa bảng tồn/ledger.

## 5. Rollback

- Code rollback theo từng file trên; route hiện có không đổi.
- Migration `Down` chỉ xóa hai bảng recommendation/decision mới, sau khi bảo đảm không còn consumer và đã xuất audit nếu từng dùng.
- Không rollback hoặc xóa stock count sheet đã được người dùng tạo/đếm; recommendation link là provenance, không phải quyền sở hữu dữ liệu nghiệp vụ.
- Không apply migration hosting trong phase này.

## 6. Trạng thái xác minh môi trường

- Migration AI-3 đã sinh SQL idempotent để review và không chứa `DROP`, `ALTER`, `TRUNCATE`, `DELETE` hoặc `UPDATE`.
- Relational workflow đã chạy trên SQLite dùng một lần để kiểm tra unique prediction và materialization idempotent.
- SQL Server clone apply/reapply/rollback rehearsal: `BLOCKED_ENV` vì máy hiện tại không có LocalDB/container hay connection clone được cấp. Không dùng DB hosting để thay thế bước kiểm thử phá hủy này.
