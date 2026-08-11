# State Machine Chuẩn Của Chứng Từ Kho

Ngày đối chiếu runtime: 11/07/2026  
Phạm vi: build hiện tại của repository WMS, không mô tả một thiết kế giả định.

## 1. Nguồn Chuẩn Và Phạm Vi

Tài liệu này là nguồn chuẩn cho trạng thái chứng từ và các state machine phụ trợ. Các nguồn code được truy vết trực tiếp:

- `Models/Voucher.cs`: các trường trạng thái và quy tắc hiển thị.
- `Models/Enums.cs`, `Models/MovementTask.cs`: enum trạng thái.
- `Controllers/VouchersController.Inbound.cs`: route nhập kho.
- `Controllers/VouchersController.Outbound.cs`: route xuất kho, đóng gói, giao hàng và hủy.
- `Services/InboundExecutionService.cs`: transaction hoàn tất nhập.
- `Services/OutboundExecutionService.cs`: reservation, picking và transaction ghi sổ xuất/chuyển.
- `Services/VoucherCancellationService.cs`: reversal và đóng chứng từ.
- `Controllers/ReportsController.StockCount.cs`: kiểm kê và điều chỉnh.
- `Controllers/OperationsController.cs`, `Services/MovementTaskService.cs`: QC và di chuyển nội bộ.
- `Models/WmsRoles.cs`, `Models/AuthorizationModels.cs`, `Services/RbacSeedService.cs`: role, permission và SoD.

Trạng thái được quyết định server-side. UI chỉ quyết định nút nào được hiển thị; controller/service vẫn phải kiểm tra role, permission, kho, chủ hàng, trạng thái, kỳ khóa và invariant dữ liệu.

## 2. Mô Hình Trạng Thái Của Voucher

`Voucher` không có một enum duy nhất. Trạng thái hiệu lực là tổ hợp của:

- `InboundStatusEnum` cho `NhapKho`, `KhachTra`, `NhapThanhPham`.
- `FulfillmentStatusEnum` cho `XuatKho`, `TraNCC`, `ChuyenKho`, `XuatSanXuat`.
- `IsPosted` cho biết tác động tồn kho đã được ghi sổ hoàn chỉnh.
- `IsCancelled` là cờ đóng chứng từ sau hủy/reversal.
- `ReviewResult`, `PackedAt` và `ShippedAt` bổ sung kết quả kiểm, đóng gói và bàn giao.

Thứ tự ưu tiên khi diễn giải trạng thái đóng là **IsCancelled > IsPosted**. Với phiếu chưa hủy, trạng thái nhập/xuất và các timestamp nghiệp vụ quyết định bước đang thực hiện. Không được suy luận trạng thái chỉ bằng thứ tự số của enum; ví dụ `Completed`, `PartiallyIssued`, `Packed`, `Shipped` là các kết quả nghiệp vụ khác nhau.

## 3. Vai Trò Runtime

| Role | Nhãn tiếng Việt | Phạm vi chính |
|---|---|---|
| `Admin` | Quản trị viên | Toàn quyền theo policy đã seed, không bỏ qua scope/SoD nếu code yêu cầu. |
| `Manager` | Quản lý kho | Duyệt, phát hành, ghi sổ, hủy và xử lý ngoại lệ theo quyền được cấp. |
| `Staff` | Nhân viên kho tổng hợp | Vai trò vận hành cũ cho người kiêm nhiệm. |
| `InboundStaff` | Nhân viên nhập kho | Tạo, tiếp nhận, quét nhận và kiểm hàng nhập. |
| `OutboundStaff` | Nhân viên xuất kho | Lấy hàng, quét lấy hàng và đóng gói. |
| `InventoryStaff` | Nhân viên tồn kho/kiểm kê | Tồn kho, mã kiện, sê-ri, kiểm kê và di chuyển nội bộ. |
| `TransportStaff` | Nhân viên vận chuyển | Bàn giao, chuyến xe, chứng từ và đối soát vận chuyển. |
| `ReportViewer` | Nhân viên báo cáo | Chỉ đọc dashboard/báo cáo theo permission và scope. |
| `Viewer` | Chỉ xem | Chỉ đọc dữ liệu cơ bản theo scope. |

Role là điều kiện cần trên các route có `Authorize(Roles=...)`; permission policy là điều kiện bổ sung trên route nhạy cảm. Kho và chủ hàng tiếp tục được kiểm tra trong controller/query/service.

## 4. Luồng Nhập Kho

Áp dụng cho `NhapKho`, `KhachTra`, `NhapThanhPham`.

### 4.1 Danh Sách Trạng Thái

`InboundStatusEnum`: `Draft`, `PendingApproval`, `Approved`, `Receiving`, `Completed`, `Rejected`.

| Trạng thái | Tên hiển thị | Có thể sửa dữ liệu nghiệp vụ | Tác động tồn |
|---|---|---|---|
| `Draft` | Nháp | Tạo/lưu nội dung trước gửi duyệt. | Không. |
| `PendingApproval` | Chờ duyệt | Không được bỏ qua duyệt; quản lý duyệt hoặc từ chối. | Không. |
| `Approved` | Đã duyệt | Chỉ bổ sung thông tin tiếp nhận hợp lệ trước khi nhận. | Không. |
| `Receiving` | Đang nhận hàng | Ghi số thực nhận, kiểm hàng, QC, lô/HSD/sê-ri và vị trí cất hàng. | Chưa tăng tồn cho đến hoàn tất. |
| `Completed` | Hoàn tất | Không sửa line bằng luồng tạo; chỉ đọc, in, export hoặc hủy có reversal. | Đã tăng tồn và có ledger nhận. |
| `Rejected` | Từ chối | Có thể hiệu chỉnh rồi gửi duyệt lại. | Không. |

### 4.2 Chuyển Trạng Thái

| Từ → đến | Route/symbol | Guard chính | Role/policy hiện tại | Side effect |
|---|---|---|---|---|
| `Draft` hoặc `Rejected` → `PendingApproval` | `POST /Vouchers/SubmitForApproval` | Phiếu nhập, đúng scope, kế hoạch nhận hợp lệ, sinh ASN nếu thiếu. | `InboundRoles` + `voucher.create`. | Ghi `SubmittedBy/At`, chưa đổi tồn. |
| `PendingApproval` → `Approved` | `POST /Vouchers/ApproveInbound` | Đúng scope, người duyệt khác người tạo. | `Admin,Manager` + `voucher.approve.inbound`. | Ghi `ApprovedBy/At`, chưa đổi tồn. |
| `PendingApproval` → `Rejected` | `POST /Vouchers/RejectInbound` | Đúng scope, bắt buộc lý do. | `Admin,Manager` + `voucher.approve.inbound`. | Ghi `RejectionReason`, chưa đổi tồn. |
| `Approved` → `Receiving` | `POST /Vouchers/ConfirmReceiving` | Có ASN và thời gian dự kiến đến; đúng scope; hỗ trợ idempotent queued retry. | `InboundRoles` + `voucher.create`. | Ghi `ReceivedBy/At`, chưa đổi tồn. |
| `Approved` → `Receiving` | `POST /Operations/UpdateDockMilestone` với mốc `unload-start` | Phiếu nhập chưa hủy, đúng warehouse/owner scope; chỉ role nhập kho. | `InboundRoles` + `voucher.create`. | Ghi mốc dock và `ReceivedBy/At`, chưa đổi tồn. |
| `Receiving` → `Receiving` | `POST /Vouchers/ConfirmActualReceivingQty` | Dòng thuộc phiếu, số thực nhận hợp lệ, đúng scope. | `InboundRoles` + `voucher.create`. | Ghi kết quả kiểm và variance, chưa post. |
| `Receiving` → `Completed` | `POST /Vouchers/CompleteInbound` → `Approve` → `CompleteInboundAsync` | Có người nhận/người kiểm, mọi dòng đã kiểm, vị trí/lô/HSD/sê-ri/UOM/catch weight hợp lệ, kỳ chưa khóa, SoD. | `Admin,Manager` + `voucher.approve.inbound`. | Transaction serializable: tăng `ItemLocation`, tạo ledger `Receive`/`Adjust`, đồng bộ tồn, ghi `IsPosted=true`, `CompletedBy/At`. |

`ReviewResultEnum`: `Undefined`, `Pending`, `Pass`, `PassWithAdjustment`, `Fail`. `PassWithAdjustment` bắt buộc ghi chú và điểm trách nhiệm hợp lệ trước khi hoàn tất.

## 5. Luồng Xuất Kho

Áp dụng cho `XuatKho`, `TraNCC`, `ChuyenKho`, `XuatSanXuat`.

### 5.1 Danh Sách Trạng Thái

`FulfillmentStatusEnum`: `Draft`, `WaitingForPick`, `Picking`, `Picked`, `Completed`, `PartiallyIssued`, `Packed`, `Shipped`.

| Trạng thái | Ý nghĩa | `IsPosted` điển hình | Tác động tồn |
|---|---|---|---|
| `Draft` | Phiếu chưa phát hành lấy hàng. | `false` | Không. |
| `WaitingForPick` | Đã giữ chỗ và tạo nhiệm vụ. | `false` | Tăng reserved, không giảm physical. |
| `Picking` | Có nhiệm vụ đang lấy/phân loại. | `false` | Ghi picked quantity, chưa giảm physical. |
| `Picked` | Các nhiệm vụ bắt buộc đã hoàn tất. | `false` | Reservation vẫn còn cho đến post. |
| `Completed` | Đã ghi sổ hết phần phải xuất. | `true` | Giảm physical, consume/release reservation, tạo ledger. |
| `PartiallyIssued` | Đã ghi một phần, còn lượng active. | `false` | Giảm phần đã post; phần còn lại vẫn được quản lý. |
| `Packed` | Phiếu đã post và đã tạo kiện xuất. | `true` | Không đổi tồn. |
| `Shipped` | Đã bàn giao vận chuyển. | `true` | Không đổi tồn lần nữa. |

### 5.2 Chuyển Trạng Thái

| Từ → đến | Route/symbol | Guard chính | Role/policy hiện tại | Side effect |
|---|---|---|---|---|
| `Draft` → `WaitingForPick` | `POST /Vouchers/ConfirmForPicking` hoặc release trực tiếp/wave | Phiếu outbound, chưa hủy/post, kỳ chưa khóa, đúng scope, tồn FEFO hợp lệ. | `Admin,Manager` + `voucher.release.picking`. | Transaction serializable: tạo wave, `StockReservation` và `PickTask`; recalculation reserved. |
| `WaitingForPick` → `Picking`/`Picked` | `POST /Vouchers/ConfirmPickTask` | Task/scan/location/lot/sê-ri/assignee hợp lệ; không vượt lượng; xử lý short/bulk/sort. | `OutboundRoles` + `voucher.create`. | Cập nhật task/allocation; chưa giảm physical. |
| `Picking` hoặc `Picked` → `Completed` | `POST /Vouchers/PostReservedOutbound` | Có reservation, lượng pick đủ nếu không cho partial, sort đã xong, QC không hold, kỳ chưa khóa, SoD. | `Admin,Manager` + `voucher.post.outbound`. | Transaction serializable: giảm nguồn, consume/release reservation, ledger `Ship`/`TransferOut`, `IsPosted=true` khi không còn active. |
| `Picking`/`Picked` → `PartiallyIssued` | `POST /Vouchers/PostReservedOutbound` | `PartialShipmentAllowed=true`, có lượng đã pick/post và còn lượng active. | Như post outbound. | Post phần có thể xuất; release/backorder theo lựa chọn; chưa coi toàn phiếu đã post. |
| `Completed` → `Packed` | `POST /Vouchers/ConfirmPacking` | `IsPosted=true`, chưa packed, có kiện/LPN hợp lệ, cùng owner/SKU, catch weight nếu bắt buộc. | `OutboundRoles` + `voucher.create`. | Tạo `OutboundPackage`, ghi `PackedBy/At`; không đổi tồn. |
| `Packed` → `Shipped` | `POST /Vouchers/ConfirmShipping` | `IsPosted=true`, có package, manifest/tracking theo loại phiếu, không thuộc chuyến đang mở, SoD. | `TransportRoles` + `voucher.confirm.shipping`. | Ghi bàn giao, `ShippedBy/At`; không đổi tồn. |

### 5.3 State Machine Phụ Trợ Xuất Kho

`ReservationStatusEnum`: `Active`, `Consumed`, `Released`.

- `Active`: còn lượng mở `ReservedQty - ConsumedQty - ReleasedQty`.
- `Consumed`: lượng đã ghi sổ; không được consume lần hai.
- `Released`: lượng được trả lại available do short, partial, hủy hoặc reversal.

`PickTaskStatusEnum`: `Pending`, `Assigned`, `InProgress`, `Completed`, `Short`, `Cancelled`, `WaitingForBulk`.

- `Pending`/`Assigned` → `InProgress` khi bắt đầu quét.
- `InProgress` → `Completed` khi đủ lượng; → `Short` khi báo thiếu; → `WaitingForBulk` với luồng hai bước.
- Hủy phiếu chuyển task chưa hủy sang `Cancelled`; hoàn tất bulk mở các sort task liên quan.

## 6. Hủy Và Reversal

Route `POST /Vouchers/Cancel` yêu cầu `Admin,Manager`, policy `voucher.cancel`, đúng warehouse/owner scope, kỳ chưa khóa, lý do hợp lệ và SoD. `VoucherCancellationService.CancelVoucherAsync` chạy trong transaction serializable:

- Phiếu chưa post: release reservation còn active.
- Phiếu inbound đã post: đảo lượng đã nhận và ledger cancel tương ứng.
- Phiếu outbound/chuyển kho đã post hoặc đã consume: trả lượng về nguồn; chuyển kho còn trừ lại đích.
- Release serial đang mở; void LPN, serial assignment và catch-weight liên quan.
- Chuyển pick task sang `Cancelled`; đóng wave khi không còn phiếu hoạt động.
- Cuối cùng ghi `IsCancelled=true`, actor, thời điểm và lý do.

`Cancelled` là trạng thái đóng có ưu tiên hiển thị cao nhất. Mọi mutation khác phải bị từ chối; đọc/audit vẫn được phép theo scope. Reversal không xóa ledger cũ mà tạo vết nghiệp vụ đối ứng.

## 7. Chuyển Kho Và Di Chuyển Nội Bộ

### 7.1 Phiếu `ChuyenKho`

Runtime hiện tại dùng state machine outbound. Khi post, service giảm vị trí nguồn và tăng `DestLocationId` trong cùng transaction, đồng thời dùng ledger `TransferOut`. Hủy đảo cả nguồn và đích.

Trạng thái kiểm chứng: **PARTIAL** so với Core WMS contract. Luồng atomic hiện tại có bảo toàn tồn, nhưng chưa mô hình hóa riêng `InTransit`, xác nhận nhận tại kho đích và discrepancy khi nhận. Không được mô tả atomic transfer này như đã có luồng vận chuyển liên kho hai bước.

### 7.2 `MovementTaskStatusEnum`

Trạng thái: `Pending`, `Assigned`, `InProgress`, `Completed`, `Short`, `Cancelled`.

- Tạo nhiệm vụ ở `Pending`; gán người xử lý thành `Assigned`; bắt đầu thành `InProgress`.
- Hoàn tất chỉ khi scan nguồn/đích, owner/kho, lượng và LPN hợp lệ; kết quả là `Completed` hoặc `Short` theo lượng xác nhận.
- Nhiệm vụ mở có thể chuyển sang `Cancelled`; nhiệm vụ terminal không được mở lại bằng client.

## 8. Kiểm Kê Và QC

### 8.1 `StockCountStatusEnum`

Enum có `Draft`, `Counting`, `Counted`, `Approved`. Runtime UI hiện tạo/lưu ở `Draft`, quản lý duyệt trực tiếp sang `Approved` và có thể tạo phiếu điều chỉnh. Admin có policy `stockcount.unlock` mới được mở khóa `Approved` về `Draft`, với lý do và các guard tham chiếu.

`Counting` và `Counted` tồn tại trong model nhưng chưa có transition runtime độc lập được xác nhận. Vì vậy state machine kiểm kê được đánh dấu **PARTIAL**; không được ghi là bốn bước hoàn chỉnh trước khi có command/test tương ứng.

### 8.2 `QualityStatusEnum` Và `QcDispositionEnum`

`QualityStatusEnum`: `Good`, `Defect`, `Pending`, `Inspecting`, `Passed`, `Failed`, `Quarantine`, `OnHold`.

`QcDispositionEnum`: `Accept`, `Reject`, `Rework`, `ReturnToSupplier`, `Scrap`, `Hold`, `AcceptWithConditions`.

QC submit chỉ nhận dữ liệu server-side hợp lệ và permission `qc.submit.inspection`. Giải phóng cách ly yêu cầu `Admin,Manager`, permission `qc.resolve.hold`, ghi chú và disposition `Accept` hoặc `AcceptWithConditions`; các disposition khác giữ hàng để xử lý tiếp. Outbound post chặn dòng `OnHold`/`Defect`.

### 8.3 Quy Ước `Failed` Và Lỗi Thực Thi

`Voucher` không có trạng thái đích `Failed`. Khi một command duyệt, nhận, post, hủy hoặc reversal thất bại validation hay transaction, voucher phải giữ nguyên trạng thái trước lệnh và không được ghi tồn/ledger một phần. UI hiển thị business error để người vận hành sửa dữ liệu rồi gửi lại; không tự gán voucher sang một trạng thái mới.

- `QualityStatusEnum.Failed` chỉ là kết quả QC: hàng không được tự động available/post, disposition tiếp theo quyết định cách ly, làm lại, trả nhà cung cấp hoặc loại bỏ.
- Lỗi parser/OCR, outbox, webhook, carrier, EDI hoặc automation thuộc state machine của integration/command tương ứng. Chúng được retry, đưa vào exception/dead-letter hoặc xử lý thủ công; không thay đổi voucher/tồn nếu transaction nghiệp vụ chưa commit.
- Lỗi mạng ở RF chỉ được retry khi command idempotent. Business rejection 400/422 phải rời hàng đợi retry và giữ nguyên trạng thái server.
- Nếu lỗi xảy ra sau khi transaction bắt đầu, rollback là bắt buộc; không được dùng thông báo UI làm bằng chứng command đã thành công.

## 9. Quy Tắc Client Và Mass Assignment

- Không nhận trạng thái đích từ client để gán trực tiếp vào `InboundStatus`, `FulfillmentStatus`, `IsPosted`, `IsCancelled`, `ReviewResult`, reservation, task hoặc audit fields.
- Mỗi command nhận dữ liệu nghiệp vụ tối thiểu (`id`, quantity, reason, scan, package/manifest) rồi service tự suy ra trạng thái kế tiếp.
- Enum/filter trong GET chỉ phục vụ truy vấn, không phải mutation.
- Owner, warehouse và actor phải lấy/kiểm tra bằng claim, entity hiện tại và server-side scope; không tin hidden field.
- Action POST phải có anti-forgery; API phải dùng authentication/policy tương đương.
- Retry chỉ được xem là idempotent khi service đã xác nhận trạng thái hoặc idempotency key hiện tại, không dựa vào thông báo UI.

## 10. Chuyển Trạng Thái Bị Cấm

| Trường hợp | Kết quả backend bắt buộc |
|---|---|
| Nhập `Draft`/`PendingApproval` → `Completed` | Business error, không ghi tồn. |
| Nhập `Approved` → `Completed` khi chưa nhận/kiểm | Business error, không ghi tồn. |
| Outbound chưa reservation/pick → post | Business error, không giảm tồn. |
| Post vượt picked/available hoặc sai owner/kho/lot | Rollback toàn transaction. |
| Đóng gói trước post hoặc giao trước đóng gói | Business error. |
| Phiếu đã hủy → mutation bất kỳ | Business error/forbid, không có side effect. |
| Hoàn tất/post lặp lại | Idempotent result hoặc business error; không ghi ledger/tồn lần hai. |
| Người tạo tự duyệt/post/giao/hủy tại action có SoD | Từ chối. |
| Khác warehouse/owner scope → read/export/mutate | `Forbid`/safe `404`, không lộ dữ liệu. |
| Kỳ đã khóa → post/cancel/adjust | Từ chối trước mutation và kiểm tra lại trong transaction khi có TOCTOU risk. |

## 11. Evidence Và Giới Hạn Xác Minh

- Contract tài liệu được kiểm tra bởi `RepoLocalAuditClosureTests.VoucherStateMachine_ShouldBeCanonicalUtf8AndTraceCurrentRuntimeContracts`.
- DTO/action mutation không nhận các trường trạng thái do server sở hữu được kiểm tra bởi `AuthorizationMatrixTests.VoucherMutationContracts_ShouldNotExposeServerOwnedStateFields`.
- Các unit/integration test hiện có bao phủ nhiều happy/error/concurrency path, nhưng chưa phải bảng transition đầy đủ cho mọi enum và mọi role.
- Gate G0.3 chỉ được đánh dấu hoàn tất toàn bộ khi transition matrix tự động cho allowed/forbidden path pass trên build hiện tại và có evidence artifact.
- Chuyển kho hai bước, trạng thái `Counting`/`Counted`, UAT vai trò thật và thiết bị/pilot thật vẫn phải được đánh giá riêng; không suy diễn từ tài liệu này.
