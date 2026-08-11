# Báo cáo audit full codebase WMS Pro - P0/P1/P2/P3

Ngày audit: 06/07/2026  
Chế độ: audit-only, chưa sửa code nghiệp vụ  
Điều phối chính: Codex  
Sub-agent đã dùng:
- Inventory/Data/FEFO audit
- Authorization/Security/Telemetry audit
- Report/Snapshot/Export audit
- UI/Sidebar/UX audit

## 1. Tổng quan hệ thống

WMS Pro là hệ thống quản lý kho nội bộ ASP.NET Core MVC, Entity Framework, SQL Server. Codebase hiện chia theo các module chính:

- Nhập kho: `VouchersController.Inbound.cs`, `InboundExecutionService`, `Voucher`, `VoucherDetail`, `ItemLocation`, `InventoryTransaction`.
- Xuất kho: `VouchersController.Outbound.cs`, `OutboundExecutionService`, wave/pick/reservation/pack/ship.
- Tồn kho: `ItemLocations` là tồn hiện tại; `InventoryTransactions` là sổ giao dịch; `StockReservations` là giữ chỗ.
- Điều chuyển: `MovementTaskService`, RF movement, source/destination location.
- Kiểm kê: `ReportsController.StockCount.cs`, `StockCountSheet`, `StockCountLine`.
- Chốt tồn/khóa kỳ: `ReportsController.Inventory.cs`, `StockSnapshot`, `StockSnapshotRun`, `WarehousePeriodLock`.
- Báo cáo: `ReportsController.Inventory.cs`, `ReportsController.Analytics.cs`, Razor views trong `Views/Reports`.
- Phân quyền: `WmsPermissions`, `PermissionAuthorization`, `RbacSeedService`, role claim/permission claim khi login.
- Telemetry/audit: `AuditLogs`, `RequestTelemetryLogs`, audit tự động trong `AppDbContext`.
- UI/sidebar/dashboard: `_Layout.cshtml`, `_SidebarNav.cshtml`, `Views/Home/Index.cshtml`, `wwwroot/css/site.css`.

Điểm mạnh hiện tại:
- Có RBAC theo permission claim, Admin override policy.
- Có test backend và visual regression khá dày.
- `ItemLocation` đã có guard tồn/giữ chỗ không âm và index batch theo owner/item/location/lot/expiry/hold.
- Báo cáo nhập/xuất mới đã bắt đầu dùng `InventoryTransactions`, không chỉ suy từ tồn hiện tại.
- Sidebar đã gọn hơn và các lỗi active menu đã được xử lý phần lớn.

Rủi ro lớn nhất:
- Một số nghiệp vụ tài chính/tồn kho còn có lỗ hổng dữ liệu P0: xuất thiếu với phiếu không cho partial, và chốt tồn ngày quá khứ có thể ghi tồn hiện tại thành lịch sử.
- Một số action backend chưa dùng đúng permission policy, dễ tạo chênh giữa menu và quyền thật.

## 2. Danh sách lỗi P0 - bắt buộc sửa ngay

### P0-INV-001 - Phiếu xuất không cho partial vẫn có thể xuất thiếu và hoàn tất

- File: `Services/OutboundExecutionService.cs`
- Dòng liên quan: 112-126, 1159-1200, 1268-1269.
- Mô tả: post outbound đang so `pickedAndReady` với `totalReserved`, trong khi release picking có thể chỉ reserve được phần tồn khả dụng. Nếu đơn yêu cầu 10 nhưng chỉ reserve/pick được 6, hệ thống có thể coi 6/6 reserved là đủ, dù phiếu không cho partial.
- Ảnh hưởng nghiệp vụ: phiếu xuất có thể hoàn tất thiếu hàng, sai cam kết đơn, sai tồn và sai đối soát.
- Cách tái hiện: tạo phiếu xuất số lượng 10, tồn khả dụng chỉ 6, `PartialShipmentAllowed=false`, release/pick/post.
- Hướng fix: khi release phải fail nếu `allocated < ordered` và không cho partial; khi post phải so consumed/picked theo từng `VoucherDetail.BaseQty`, chỉ cho thiếu nếu có partial/backorder/cancel remaining hợp lệ.
- Có nên fix ngay: Có.

### P0-REP-001 - Chốt tồn ngày quá khứ dùng tồn hiện tại làm lịch sử

- File: `Controllers/ReportsController.Inventory.cs`
- Dòng liên quan: 1186-1195, 1497-1521.
- Mô tả: khi ngày chọn chưa có snapshot, màn hình preview lấy current `ItemLocations`. Khi bấm chốt, `GenerateStockSnapshot` cũng lấy current `ItemLocations` nhưng ghi `SnapshotDate` theo ngày người dùng chọn.
- Ảnh hưởng nghiệp vụ: có thể tạo dữ liệu chốt tồn/kế toán sai ngày. Ví dụ hôm nay tồn 10, chọn chốt cho tháng trước, hệ thống ghi tháng trước = 10.
- Cách tái hiện: chọn một ngày cũ chưa có chốt, kho đang có tồn hiện tại, bấm chốt.
- Hướng fix: chỉ cho chốt ngày hiện tại; hoặc dựng balance as-of từ `InventoryTransactions` đến cuối ngày. Nếu không dựng được as-of thì ngày quá khứ phải hiển thị “chưa có dữ liệu”, không preview bằng current stock.
- Có nên fix ngay: Có.

## 3. Danh sách lỗi P1 - nên sửa trước demo/bảo vệ

### P1-INV-002 - Inbound service dùng `Math.Abs(ConversionRate)` nên có thể nuốt conversion âm

- File: `Services/InboundExecutionService.cs`, `Controllers/VouchersController.Inbound.cs`
- Mô tả: luồng service tính good/defect quantity bằng `Math.Abs(detail.ConversionRate)`. Guard conversion âm có trong legacy controller nhưng không chắc phủ service path.
- Ảnh hưởng: dữ liệu import/migration bẩn có conversion âm vẫn post được, làm sai số lượng defect/good.
- Fix: block `ConversionRate <= 0` trong service trước khi post.

### P1-SEC-001 - `ConfirmShipping` thiếu policy `voucher.confirm.shipping`

- File: `Controllers/VouchersController.Outbound.cs:824`
- Mô tả: action chỉ có role `WmsRoles.TransportRoles`; `TransportRoles` có thể gồm role rộng hơn permission.
- Ảnh hưởng: user có role phù hợp nhưng không có permission chuyên biệt vẫn có thể xác nhận giao hàng nếu gọi POST trực tiếp.
- Fix: thêm `[Authorize(Policy = WmsPermissions.VoucherConfirmShipping)]`.

### P1-SEC-002 - Demo seed có thể xóa dữ liệu vận hành, chỉ cần Admin

- File: `Controllers/SystemController.cs:103`, `Services/DemoDataSeedService.cs`
- Mô tả: `ApplyDemoData` là thao tác phá dữ liệu thật nhưng chưa gắn `DangerOps` như các action nguy hiểm khác.
- Ảnh hưởng: admin bị compromise hoặc thao tác nhầm có thể wipe dữ liệu vận hành.
- Fix: yêu cầu `DangerOps`, `System:AllowDangerOps`, giới hạn Development/demo hoặc thêm xác nhận nhiều bước/MFA.

### P1-SEC-003 - Kiểm kê approve/unlock dùng policy `ReportView`

- File: `Controllers/ReportsController.StockCount.cs:283`, `Controllers/ReportsController.StockCount.cs:550`
- Mô tả: action duyệt/mở khóa kiểm kê đang check `ReportView`, không dùng `StockCountApprove`/`StockCountUnlock`.
- Ảnh hưởng: permission matrix không tách được quyền xem báo cáo với quyền tác động tồn.
- Fix: đổi policy đúng nghiệp vụ và thêm test 403.

### P1-SEC-004 - Audit log tự động có nguy cơ ghi raw payload/dữ liệu nhạy cảm

- File: `Data/AppDbContext.cs:27`, `Data/AppDbContext.cs:79`, `Data/AppDbContext.cs:472`
- Mô tả: audit serialize nhiều bảng nhạy cảm; ignored properties hiện chủ yếu có `PasswordHash`, chưa đủ để redact OCR/EDI/webhook/MFA/login help raw data.
- Ảnh hưởng: AuditLogs có thể chứa payload nhạy cảm hoặc dữ liệu chứng từ.
- Fix: thêm denylist/redaction theo field (`*Payload*`, `RawJsonResponse`, `ParsedData`, `CodeHash`, contact notes, token/hash fields) hoặc bỏ audit raw telemetry/auth/OCR.

### P1-REP-002 - Stock valuation snapshot mode có thể cộng trùng nhiều phiên chốt cùng ngày

- File: `Controllers/ReportsController.Inventory.cs:979-995`
- Mô tả: query lọc theo warehouse/date nhưng không lọc `StockSnapshotRunId`, trong khi hệ thống cho nhiều run cùng ngày.
- Ảnh hưởng: định giá tồn snapshot có thể bị nhân đôi/nhân nhiều.
- Fix: thêm selector run hoặc default latest run, đồng bộ UI/export.

### P1-REP-003 - Rò owner scope trong `StockMovement` và `InventoryTransactions`

- File: `Controllers/ReportsController.Inventory.cs:38`, `Controllers/ReportsController.Inventory.cs:524`
- Mô tả: action ép warehouse scope nhưng chưa áp owner scope như `InventoryInOutSummary`.
- Ảnh hưởng: user owner-scoped có thể thấy giao dịch của chủ hàng khác.
- Fix: apply owner scope cho query UI, export và dropdown.

### P1-REP-004 - `StockMovement` UI và Excel không khớp với điều chỉnh/chuyển kho

- File: `Views/Reports/StockMovement.cshtml`, `Controllers/ReportsController.Inventory.cs`
- Mô tả: UI coi mọi non-inbound là xuất âm, export lại map khác cho điều chỉnh/chuyển kho.
- Ảnh hưởng: số liệu trên màn và Excel không thống nhất.
- Fix: dùng chung mapper, ưu tiên chuyển báo cáo lịch sử sang `InventoryTransactions`.

### P1-UI-001 - Viewer thấy link dẫn tới 403

- File: `Views/Shared/_SidebarNav.cshtml:122-161`, `Views/Home/Index.cshtml:120-122`, `Views/Shared/_Layout.cshtml:251-271`
- Mô tả: Viewer có thể thấy `Kiểm kê`, `Hàng chậm`, dashboard link `OpsKpi`, bell cảnh báo hoặc thiết bị tin cậy dù controller giới hạn quyền.
- Ảnh hưởng: trải nghiệm lỗi 403, demo bị nhìn thiếu chuyên nghiệp.
- Fix: gate menu/topbar/dashboard theo đúng role/policy hoặc đổi link tới màn được phép.

## 4. Danh sách lỗi P2 - cải thiện sau P0/P1

### P2-API-001 - API integration dùng single API key rộng quyền

- File: `Controllers/ApiIntegrationController.cs`
- Mô tả: scope warehouse/owner là optional; nếu không cấu hình scope, một key có thể đọc/ghi rộng.
- Fix: per-client key, required scope ở production, chuyển validation thành filter bắt buộc.

### P2-REP-005 - Bắt buộc ngày từ/đến chưa đồng nhất

- File: `Controllers/ReportsController.Inventory.cs:35`, `Controllers/ReportsController.Inventory.cs:521-522`
- Mô tả: một số report fallback 30 ngày khi thiếu ngày, trong khi report khác đã bắt buộc ngày.
- Fix: dùng chung validator date range cho UI/export.

### P2-REP-006 - `ExportStockSnapshot` không khớp UI preview

- File: `Views/Reports/StockSnapshot.cshtml`, `Controllers/ReportsController.Inventory.cs`
- Mô tả: UI có thể preview current stock khi chưa có snapshot, export lại chỉ query `StockSnapshots`.
- Fix: disable export ở preview hoặc export đúng preview với nhãn rõ.

### P2-REP-007 - Cột “Ngày nhập nguồn” trong báo cáo nhập/xuất có thể bị suy diễn

- File: `Controllers/ReportsController.Inventory.cs`
- Mô tả: outbound đang dùng ngày chứng từ/giao dịch, không phải source receipt date thật.
- Fix: đổi tên cột hoặc bổ sung source receipt date vào ledger/batch.

### P2-UI-002 - `/Reports/Inventory` có thể active hai menu cùng lúc

- File: `Views/Shared/_SidebarNav.cshtml:131`, `Views/Shared/_SidebarNav.cshtml:232`
- Mô tả: cùng route được dùng cho “Xem tồn kho” và “Báo cáo tồn kho”.
- Fix: dùng query context như các route đã tách, hoặc chọn canonical menu item.

## 5. Danh sách lỗi P3 - nâng cấp dài hạn

### P3-DB-001 - Unique index kiểm kê chưa phủ đủ tổ hợp null lot/expiry

- File: `Data/AppDbContext.cs:1605-1608`
- Mô tả: unique index `StockCountLine` chỉ filter khi cả `LotNumber` và `ExpiryDate` đều non-null.
- Ảnh hưởng: nếu bypass controller, có thể tạo duplicate line no-batch/lot-only/expiry-only.
- Fix: thêm filtered indexes cho các tổ hợp null/non-null, cần migration sau khi xác nhận.

### P3-OBS-001 - Telemetry mặc định có thể quá ồn

- File: `Services/Enterprise1113Services.cs`
- Mô tả: sampling 100% có thể ghi nhiều request thường.
- Fix: production sampling thấp hơn, chỉ ghi error/slow/sensitive path, retention rõ ràng.

### P3-REP-008 - “Chốt tồn” và “khóa kỳ” wording dễ gây hiểu nhầm

- File: `Views/Reports/StockSnapshot.cshtml`, `Views/Reports/PeriodLocks.cshtml`
- Mô tả: chốt tồn không tự tạo period lock, nhưng wording có thể khiến user nghĩ đã khóa kỳ.
- Fix: đổi wording hoặc thêm flow “chốt và khóa kỳ”.

### P3-UI-003 - Một số module nâng cao có view/test nhưng thiếu lối vào rõ trong sidebar

- Ví dụ: labor productivity, replenishment, slotting, kitting, VAS, MHE, tenant owner scope.
- Ảnh hưởng: discoverability thấp, không phải lỗi runtime.
- Fix: thêm nhóm nâng cao hoặc shortcut theo role workspace.

## 6. Rủi ro dữ liệu/database

- Có cần migration không:
  - P0/P1 phần lớn sửa bằng code/service/controller.
  - P3-DB-001 cần migration nếu thêm unique indexes cho `StockCountLine`.
  - Nếu chọn dựng chốt tồn quá khứ bằng ledger as-of, có thể không cần migration nếu `InventoryTransactions` đủ dữ liệu; nếu cần receipt layer/source date thì có thể cần migration.
- Có rủi ro mất dữ liệu không:
  - Có, với `ApplyDemoData` nếu chạy nhầm trên DB thật.
  - Có, với chốt tồn ngày quá khứ nếu đã tạo snapshot sai lịch sử.
- Có cần backup không:
  - Có, trước mọi fix liên quan snapshot/chốt tồn, demo seed, constraint/index production.
- Bảng nên thêm constraint/index:
  - `StockCountLines` cho các tổ hợp lot/expiry null.
  - Cân nhắc constraint/guard service cho `VoucherDetail.ConversionRate > 0`.

## 7. Kế hoạch fix an toàn

Thứ tự đề xuất:

1. Data integrity: khóa `GenerateStockSnapshot` không cho ngày quá khứ nếu không có ledger as-of; xử lý snapshot valuation theo latest/specified run.
2. Inventory flow: chặn release/post thiếu hàng khi phiếu không cho partial.
3. Lot/HSD/FEFO: thêm guard `ConversionRate > 0` trong service và test FEFO non-partial.
4. Report/history: owner scope cho `StockMovement`/`InventoryTransactions`, đồng bộ UI/export.
5. Authorization: thêm policy `VoucherConfirmShipping`, đổi policy kiểm kê approve/unlock.
6. Telemetry/audit: redact sensitive fields trong audit.
7. UI/sidebar: fix Viewer links/topbar links và double-active `/Reports/Inventory`.
8. Test/regression: thêm test theo từng nhóm trước khi đổi tiếp.

Không nên sửa đồng thời tất cả module tồn kho trong một patch lớn. Nên chia 4 patch:

- Patch A: P0 snapshot/chốt tồn.
- Patch B: P0 outbound non-partial.
- Patch C: P1 authorization/RBAC/UI links.
- Patch D: P1 report scope/export/audit redaction.

## 8. File tuyệt đối không nên sửa nếu chưa xác nhận

- `appsettings.json`
- `appsettings.Development.json`
- `Properties/launchSettings.json` nếu đang chứa connection thật
- migration production đã áp lên DB thật
- service seed/reset dữ liệu thật nếu chưa có backup và xác nhận
- mọi thao tác trực tiếp trên DB hosting có ghi dữ liệu

Trong audit này chưa sửa các file trên.

## 9. Đề xuất test sau khi fix

- Non-partial outbound thiếu tồn: release/post phải fail.
- Wave release nhiều dòng: thiếu một dòng không được hoàn tất nếu không cho partial.
- Chốt tồn ngày quá khứ: phải bị reject hoặc dùng ledger as-of đúng.
- Stock valuation snapshot nhiều run cùng ngày: chỉ lấy latest/specified run.
- Staff không có `voucher.confirm.shipping` POST `ConfirmShipping` phải 403.
- User có `report.view` nhưng không có `stockcount.approve/unlock` phải bị chặn.
- Demo data production/Admin thường không có `DangerOps` phải bị chặn.
- Audit redaction không chứa raw OCR/EDI/webhook/MFA/login-help payload.
- Owner-scoped user không thấy report/giao dịch của owner khác.
- `StockMovement` UI/export khớp cho nhập/xuất/điều chỉnh/chuyển kho.
- Mỗi route sidebar chỉ có một `.nav-link.active`.
- Viewer dashboard/topbar/sidebar không có link dẫn tới 403.

## 10. Kết luận

Đánh giá hiện tại:

- So với mục tiêu WMS nội bộ dùng để demo/quản lý kho cơ bản: khoảng 82-86%.
- So với chuẩn production nghiêm ngặt của doanh nghiệp lớn: khoảng 65-72% vì còn P0 dữ liệu/chốt tồn và P1 phân quyền/audit.
- Có thể demo các luồng phổ thông, nhưng chưa nên tuyên bố “100% production-ready” trước khi sửa ít nhất 2 lỗi P0 và các P1 phân quyền/report chính.

5 việc nên sửa trước:

1. Chốt tồn ngày quá khứ không được dùng tồn hiện tại.
2. Phiếu không cho partial không được xuất thiếu.
3. Sửa policy `ConfirmShipping`, `StockCountApprove`, `StockCountUnlock`.
4. Khóa demo seed bằng `DangerOps` và điều kiện môi trường.
5. Apply owner scope và đồng bộ UI/export cho report lịch sử/tồn kho.

Theo prompt audit, báo cáo này dừng ở phân tích và kế hoạch. Chỉ nên bắt đầu sửa code sau khi người dùng xác nhận “bắt đầu fix”.
