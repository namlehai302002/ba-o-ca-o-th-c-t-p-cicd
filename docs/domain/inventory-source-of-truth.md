# Hợp Đồng Nguồn Tồn Kho Chuẩn

## 1. Phạm Vi

Tài liệu này chốt nguồn dữ liệu chuẩn cho số dư tồn, giữ chỗ, ledger, đơn vị tính, thời gian nghiệp vụ và khóa kỳ trên runtime hiện tại. Mọi thay đổi sau này phải giữ các invariant bên dưới hoặc cập nhật contract, migration, reconciliation và regression test trong cùng thay đổi.

## 2. Thứ Tự Nguồn Dữ Liệu

1. `ItemLocation` là nguồn sự thật của **số dư hiện tại**. `Quantity`, `ReservedQty` và `HoldStatus` mô tả snapshot tồn tại một vị trí.
2. `InventoryTransaction` là ledger bất biến dùng để giải thích thay đổi trước/sau, idempotency, audit và reconciliation. Ledger không thay thế trực tiếp truy vấn số dư hiện tại.
3. `StockReservation`, reservation của kitting và VAS là nguồn chi tiết của lượng giữ chỗ. `ItemLocation.ReservedQty` là snapshot tổng hợp và được tính lại bởi `InventoryReservationService.RecalculateReservedQtyAsync`.
4. `Item.CurrentStock` và `Item.TotalStockValue` là cache tổng hợp. Chúng phải được tính lại từ `SUM(ItemLocation.Quantity)` qua `InventoryBalanceService.SyncCurrentStockAsync`; không được dùng làm nguồn quyết định xuất, giữ chỗ, kiểm kê hoặc báo cáo có scope.

Không có hai nguồn số dư nào được phép cập nhật độc lập. Một workflow có thể cập nhật tạm cache trong transaction để hiển thị/audit, nhưng trước commit kết quả cuối phải khớp tổng `ItemLocation`.

## 3. Khóa Số Dư Và Phạm Vi

Khóa logic của một dòng tồn gồm:

`WarehouseId` (suy ra từ `Location -> Zone`) + `OwnerPartnerId` + `ItemId` + `LocationId` + `LotNumber` + `ExpiryDate` + `HoldStatus`.

- `OwnerPartnerId = null` biểu thị hàng nội bộ, không có nghĩa là được bỏ qua owner scope khi người dùng đã có danh sách owner được cấp.
- Bốn unique index có filter bảo vệ các trường hợp lot/HSD cùng null, chỉ có lot, chỉ có HSD hoặc có cả hai.
- Số sê-ri được quản lý ở `SerialNumber` theo warehouse, owner, item, location, lot, HSD, hold và trạng thái. Với vật tư `TrackSerial`, số serial active/consumed phải reconcile với nghiệp vụ quantity tương ứng; serial không tạo một dòng `ItemLocation` riêng.
- LPN là lớp chứa/di chuyển; LPN detail phải reconcile về cùng khóa item-location-owner-lot-expiry.
- `ItemLocation.Quantity`, reservation và ledger luôn dùng **BaseUom** của vật tư. `VoucherDetail.TransactionQty` giữ số lượng theo đơn vị giao dịch; `BaseQty = TransactionQty x ConversionRate` là lượng ghi tồn.

## 4. Số Lượng Khả Dụng

Công thức số học chuẩn:

`AvailableQty = Quantity - ReservedQty`

Invariant bắt buộc:

- `Quantity >= 0`.
- `ReservedQty >= 0`.
- `ReservedQty <= Quantity`.
- Tổng reservation còn mở bằng `ReservedQty` của đúng khóa tồn.
- Chỉ dòng có `HoldStatus = Available`, còn hạn và thỏa rule FEFO/quality mới **được phép phân bổ**. Vì vậy arithmetic available lớn hơn 0 không tự động có nghĩa là pickable nếu hàng đang QC hold, quarantine, damaged hoặc expired.

## 5. Transaction, Ledger Và Cache

- `AppDbContext.SaveChangesAsync` mở database transaction khi caller chưa có transaction ngoài.
- Mọi thay đổi ledger-relevant của `ItemLocation` được snapshot trước save; ledger và audit được ghi rồi commit trong cùng transaction.
- Workflow nhiều bước phải dùng transaction ngoài phù hợp, thường là `Serializable`, và `SaveChangesAsync` tham gia transaction đó.
- `InventoryTransactionSemanticRules.Validate` phải xác nhận before/after/delta và available trước khi ledger được lưu.
- `InventoryTransaction.IdempotencyKey` là unique; retry không được sinh thêm ledger cho cùng thay đổi nghiệp vụ.
- `SyncCurrentStockAsync` phải chạy trên cùng `AppDbContext` trước commit của workflow thay đổi quantity. Nếu cache lệch, reconciliation lấy `ItemLocation` làm chuẩn để sửa cache, không sửa ngược dòng tồn từ cache.

Phương trình reconciliation:

- `Item.CurrentStock = SUM(ItemLocation.Quantity)` theo `ItemId` trên toàn hệ thống.
- Báo cáo theo warehouse/owner phải tổng hợp trực tiếp `ItemLocation` với scope tương ứng, không lọc bằng cache toàn cục.
- `AvailableAfter = QuantityAfter - ReservedAfter` và delta ledger phải bằng chênh lệch before/after.

## 6. Decimal, Quy Đổi Và Làm Tròn

| Dữ liệu | Precision chuẩn | Quy tắc |
|---|---|---|
| Quantity, BaseQty, ReservedQty, giá và amount nghiệp vụ kho | `decimal(18,4)` | Không dùng `float`/`double` làm nguồn lưu; không công bố độ chính xác cao hơn bốn số lẻ. |
| ConversionRate | `decimal(18,6)` | Phải lớn hơn 0; ưu tiên conversion theo item, sau đó mới dùng conversion global hợp lệ. |
| Tỷ lệ phần trăm | Thông thường `decimal(9,4)` | Chỉ làm tròn khi trình bày hoặc tại boundary đã định nghĩa. |

Khi nghiệp vụ bắt buộc làm tròn trước khi lưu, dùng `MidpointRounding.AwayFromZero`; quantity/amount làm tròn tối đa 4 số lẻ, conversion rate tối đa 6 số lẻ. SQL precision là boundary cuối, không được dựa vào làm tròn ngầm để che input vượt precision. Tiền VND có thể hiển thị 0 số lẻ nhưng vẫn lưu `decimal(18,4)` để giữ phép tính và audit nhất quán.

## 7. Chính Sách Thời Gian Và Khóa Kỳ

- Runtime nghiệp vụ hiện tại dùng giờ Việt Nam qua `VietnamTime` (`Asia/Ho_Chi_Minh` hoặc `SE Asia Standard Time`). Các `DateTime` nghiệp vụ được lưu như Vietnam wall-clock; đây không phải UTC.
- Trường ngày chứng từ, NSX, HSD và ngày khóa kỳ dùng semantics ngày Việt Nam và cột SQL `date` khi model đã định nghĩa.
- Hàng được xem hết hạn khi `ExpiryDate < VietnamTime.Today`; tại đúng ngày HSD, policy nghiệp vụ cụ thể có thể chặn sớm hơn (ví dụ outbound FEFO yêu cầu số ngày tối thiểu) nhưng phải hiển thị rõ.
- Một kỳ khóa active của kho chặn create/post/cancel/adjust khi ngày giao dịch `<= LockDate`. Runtime ưu tiên `CompletedAt`, sau đó operation time, rồi `VoucherDate` theo command hiện tại.
- Security token, correlation hoặc integration protocol có thể dùng UTC khi contract bên ngoài yêu cầu; không trộn UTC trực tiếp vào phép so sánh ngày kho.

## 8. Evidence Và Quy Tắc Thay Đổi

- Runtime map: `docs/audit/WMS_RUNTIME_MAP.md`.
- State machine: `docs/domain/voucher-state-machine.md`.
- DQ snapshot: `artifacts/data-quality/wms-data-quality-audit-hosting-20260712.txt` và `artifacts/data-quality/wms-data-quality-audit-local-final-20260713.txt`.
- Contract test: `Gate0BaselineContractTests` cùng các test inventory balance, reservation, ledger, UOM, period lock và Vietnam time hiện có.

Không được đổi nguồn sự thật, scope key, precision hoặc timezone bằng refactor cục bộ. Thay đổi contract cần impact map, migration/compatibility plan, reconciliation, rollback và full regression.
