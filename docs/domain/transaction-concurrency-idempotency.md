# Hợp đồng transaction, concurrency và idempotency

## Phạm vi

Tài liệu này mô tả các invariant đang được thực thi cho nhập kho, xuất kho, chuyển kho, kiểm kê/điều chỉnh, reservation, hủy phiếu và ledger tồn kho.

## Transaction boundary

- `AppDbContext.SaveChangesAsync` tự mở transaction khi caller chưa có transaction, ghi dữ liệu nghiệp vụ, `InventoryTransaction` và `AuditLog` trong cùng boundary.
- Các workflow nhiều bước dùng `IUnitOfWork` và isolation `Serializable`: `InboundExecutionService`, `OutboundExecutionService`, `VoucherCancellationService`, movement, cycle count, snapshot/reconciliation và các workflow liên quan.
- Lỗi sau lần lưu dữ liệu đầu tiên phải rollback toàn bộ transaction; không được để lại tồn, reservation, header/line hoặc ledger một phần.
- OCR, email và provider HTTP không được gọi trong method đang giữ database transaction. Contract test kiểm tra ở phạm vi method.

## Concurrency

- `Item` và `ItemLocation` dùng SQL Server `rowversion`.
- `Voucher.UpdatedAt` là concurrency token trên cột sẵn có. `AppDbContext` tăng token khi phiếu bị sửa, không cần migration hoặc đổi schema.
- `DbUpdateConcurrencyException` được chuẩn hóa thành HTTP 409, mã `DATA_CONCURRENCY_CONFLICT` và thông báo tiếng Việt yêu cầu tải lại dữ liệu.
- Transaction tồn kho vẫn phải tuân theo các check constraint: quantity/reserved không âm và reserved không vượt quantity.
- Conflict không được tự retry như lỗi nghiệp vụ. Retry deadlock/transient cho toàn unit of work vẫn là mục mở; không bật retry riêng lẻ quanh commit của manual transaction.

## Idempotency

- Mỗi ledger row có `IdempotencyKey` unique và `TransactionGroupKey` xác định business event.
- API/integration có idempotency key; worker/outbox claim event trước khi xử lý và ghi trạng thái hoàn tất.
- Các command nhập, phát hành lấy hàng, ghi sổ xuất và hủy phiếu phải an toàn khi client gửi lại sau khi mất response.
- Hủy phiếu đã hủy trả business error và không tạo thêm reversal.
- Import/OCR dùng content hash và business identity để phát hiện nội dung trùng trước khi cộng số lượng.

## Ledger và provenance

- Ledger là append-only trong runtime; sửa sai dùng adjustment hoặc reversal.
- Ledger lưu liên kết có thể áp dụng tới voucher, voucher line, reservation, pick/movement task và mã tham chiếu nghiệp vụ.
- Runtime HTTP tự bổ sung `correlationId` và `requestPath` vào `MetadataJson` mà không thay đổi schema.
- Reversal hủy phiếu lưu `originalInventoryTransactionIds`, lý do hủy và mã lý do trong metadata.
- Demo seed ghi opening balance, posted voucher và active reservation theo đúng thứ tự thời gian; snapshot cuối phải reconcile với `ItemLocation`.

## Evidence và rollback

- SQL concurrency chỉ chạy trên SQL Server cục bộ và database có tiền tố `AUDIT_TEST_`.
- Hosting chỉ dùng SELECT qua read-only guard; không migration hoặc sửa dữ liệu.
- Rollback code: gỡ mapping concurrency token, metadata enrichment và patch demo ledger cùng các test tương ứng. Không rollback bằng cách sửa/xóa ledger đã ghi.
- Evidence hiện hành: `artifacts/full-audit/GATE1_TRANSACTION_CONCURRENCY_EVIDENCE_2026_07_13.md`.
