# Gate 3 Enterprise Workflow Benchmark

Ngày truy cập nguồn: 2026-07-14  
Phạm vi: chuẩn nghiệp vụ tham chiếu cho WMS nội bộ; không sao chép độ phức tạp của ERP/WMS Tier-1 nếu không cần cho vận hành nội bộ.

## Nguồn chính thức

1. Oracle WMS Cloud, [Inbound Shipments and ASN statuses](https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owmol/description-of-asn-statuses.html).
2. Oracle WMS Cloud, [Receiving Operations Setup](https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owmim/shipment-verification.html).
3. Oracle WMS Cloud, [Allocation](https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owmwr/allocation.html).
4. Oracle WMS Cloud, [Wave allocation methods](https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owmol/optional-step-additional-configuration-parameters.html).
5. Oracle WMS Cloud, [Serial Number Tracking](https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owmol/serial-number-tracking.html) và [serial caveats](https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owmol/important-caveats.html).
6. Oracle WMS Cloud, [Deferred Cycle Count Inventory Updates](https://docs.oracle.com/en/cloud/saas/warehouse-management/25d/owmol/reinitiate-in-progress-deferred-cycle-counts.html).
7. Microsoft Dynamics 365 SCM, [Release to warehouse](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/release-to-warehouse-process).
8. Microsoft Dynamics 365 SCM, [Reservations in Warehouse management](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/reservations-in-warehouse-management).
9. Microsoft Dynamics 365 SCM, [Location directives](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/create-location-directive).
10. Microsoft Dynamics 365 SCM, [Packing containers for shipment](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/packing-containers) và [outbound load handling](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/outbound-load-handling).
11. Microsoft Dynamics 365 SCM, [Cycle counting](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/cycle-counting) và [count reason codes](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/reason-codes-for-counting-journals).
12. SAP EWM, [Quality Inspection](https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT/3d97bec9bf1649099384bb8167df3cf2/95cccb53ad377114e10000000a174cb4.html) và [Direct and Combined Storage Control](https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT/3d97bec9bf1649099384bb8167df3cf2/9cc8cb53ad377114e10000000a174cb4.html).
13. GS1, [Global Traceability Standard](https://www.gs1.org/standards/gs1-global-traceability-standard/current-standard).

## Invariant chuẩn áp dụng cho dự án

| Miền | Mẫu workflow ở hệ thống lớn | Contract áp dụng cho WMS nội bộ này |
|---|---|---|
| Inbound reference | ASN/receipt advice xác định item, quantity và reference line; trạng thái chuyển từ in-transit sang receiving rồi complete/verified | Phiếu nhập phải có source/reference truy vết được; sau khi bắt đầu nhận, thay đổi line phải bị giới hạn theo state machine |
| Receipt tolerance | Over/under receipt dùng warning/error threshold có cấu hình | Không tự nhận thừa/thiếu âm thầm; actual, defect và discrepancy phải có validation, lý do và audit |
| Lot/serial/expiry | Thu thập theo item policy; serial phải đủ số lượng và duy nhất | Không tự tạo serial giả; số serial active phải khớp base quantity trước khi available/post |
| Quality | Inspection decision dẫn tới follow-up action; disposition có thể chuyển stock sang QA/quarantine | Stock cần kiểm tra không được available trước quyết định; release/reject/rework phải giữ đúng item-owner-location-lot-expiry identity |
| Putaway | Location directive chọn vị trí put; strategy và capacity/compatibility là server-side policy | Gợi ý UI chỉ là đề xuất; server phải tái kiểm tra warehouse, owner, compatibility, capacity và state khi post |
| Allocation | Allocation liên kết order line với stock cụ thể; wave tạo allocation/task | Reservation phải gắn detail/item/owner/location/lot/expiry, không vượt available và consume/release đúng một lần |
| FEFO/FIFO | FEFO theo priority/expiry date; FIFO theo thời điểm nhận/tạo; tie-breaker rõ | Không dùng stock expired/blocked/quarantine; fallback khi thiếu expiry phải có policy rõ và deterministic |
| Outbound execution | Release -> wave/allocation -> work/pick -> pack -> load/ship confirm | Pick complete không đồng nghĩa post/ship; mỗi mốc có guard, actor, timestamp và audit riêng |
| Partial/short | Partial release, short pick và split load giữ phần còn lại mở hoặc backorder | Partial quantity phải giữ đúng remaining reservation/detail; cancel phần còn lại chỉ release một lần |
| Cycle count | Create work -> count -> pending review -> approve/reject/recount; adjustment có thể deferred | Snapshot/scope phải ổn định; reject không đổi tồn; approve tạo adjustment/ledger có reason và SoD |
| Count concurrency | Chính sách có thể cho giao dịch tiếp tục hoặc khóa item/location trong lúc count | Dự án phải chọn và kiểm thử một policy rõ; không pha trộn snapshot với on-hand hiện tại mà không reconcile |
| Traceability | Lot/serial/logistic unit liên kết các sự kiện receive, move, pick, pack, ship | Một serial chỉ ở một nơi/trạng thái tại một thời điểm; ledger phải truy ngược/tiến tới voucher và stock identity |
| Reversal | Nghiệp vụ hoàn tác giữ lịch sử và reference giao dịch gốc | Không xóa ledger đã post; reversal tạo delta đối ứng, idempotent và có reason/correlation |
| Scope | Facility/company là tiêu chí lọc bắt buộc trong allocation/report | Mọi query/mutation phải áp warehouse và owner scope ở server, không dựa vào menu/UI |

## Nguyên tắc fit-gap

- Ưu tiên invariant và traceability, không thêm wave/LPN/automation chỉ để giống sản phẩm enterprise.
- Giữ mô hình hiện có nếu đáp ứng contract; chỉ tạo abstraction mới khi test chứng minh logic bị phân tán và không thể bảo vệ invariant bằng sửa cục bộ.
- Một chức năng chỉ `COMPLETE` khi schema, runtime, permission/scope, state, transaction, ledger/audit, automated test, Playwright và data reconciliation đều có evidence.
- Capability cần thiết bị, partner sandbox hoặc pilot thật được ghi `BLOCKED`, không suy diễn pass từ mock.

## Acceptance contract dùng cho Gate 3

1. Không có đường ghi tồn nào bỏ qua transaction, ledger, correlation và scope.
2. Không có state transition trái thứ tự hoặc ghi trạng thái hoàn tất khi mutation tồn thất bại.
3. Không có reservation âm, vượt available, consume/release lặp hoặc stock identity sai.
4. Không có lot hết hạn, hold, quarantine hoặc sai owner/kho được allocation.
5. Partial, cancel và reversal giữ đúng remaining quantity và không double-post.
6. Serial quantity/state/location phải reconcile với tồn serial-tracked.
7. Count/recount/approval không sửa trực tiếp on-hand ngoài adjustment/ledger được kiểm soát.
8. Business rejection trả thông điệp tiếng Việt có hành động khắc phục; technical detail chỉ vào log có correlation ID.
9. Mỗi finding phải có red test hoặc reproduction đáng tin cậy trước khi sửa, rồi targeted/full regression sau sửa.
10. Không tick roadmap chỉ dựa trên code presence hoặc unit test đơn lẻ.
