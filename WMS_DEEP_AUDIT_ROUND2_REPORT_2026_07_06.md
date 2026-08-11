# WMS Pro - Báo cáo audit sâu vòng 2

Ngày audit: 06/07/2026  
Phạm vi: audit trước, chưa sửa code nghiệp vụ theo `Prompt Review Codebase.txt`.

## 0. Kết luận nhanh

Hệ thống đã có nền tảng khá rộng cho WMS nội bộ: nhập/xuất, RF scan, tồn vị trí, lô/HSD, kiểm kê, vận chuyển, báo cáo, audit, RBAC, mobile visual regression. So với các WMS lớn như Oracle WMS, Microsoft Dynamics 365 Warehouse Management, SAP EWM và NetSuite WMS, mức hoàn thiện hiện tại ước khoảng **72-78% cho mục tiêu WMS nội bộ**, nhưng chỉ khoảng **60-68% nếu so với chuẩn enterprise WMS đầy đủ**.

Điểm mạnh: giao diện desktop/mobile hiện đã qua visual suite lớn, database thật đang sạch ở các check dữ liệu chính, menu đã gọn hơn nhiều, admin có quyền rộng, dashboard có định hướng đúng.  
Điểm chưa được phép gọi là 100%: còn P0/P1 trong destructive demo seed, luồng xuất non-partial, hủy nhập sau cross-dock, một số phân quyền backend, constraint DB và active menu trùng route.

Theo chuẩn các hệ thống lớn:
- Oracle Cloud WMS dùng roles/groups/permissions để kiểm soát chức năng; Administrator có full permissions và role thấp không tự có toàn quyền. Nguồn: https://docs.oracle.com/en/cloud/saas/readiness/logistics/26b/wms26b/26B-wms-wn-f45822.htm
- Microsoft Dynamics 365 cho phép cấu hình menu mobile theo worker và warehouse; worker chỉ thấy menu phù hợp công việc. Nguồn: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/configure-mobile-devices-warehouse
- SAP EWM có Warehouse Management Monitor là màn trung tâm cho quản lý xem tình hình, cảnh báo và phản ứng nhanh. Nguồn: https://help.sap.com/saphelp_ewm700_ehp02/helpdata/en/51/cdcb53ad377114e10000000a174cb4/content.htm
- NetSuite WMS tách role Warehouse Manager, Inbound Manager, Outbound Manager, Mobile Operator. Nguồn: https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_156520478173.html
- Oracle WMS đã hạn chế default global menu cho non-admin để tránh user thấy chức năng chưa được gán. Nguồn: https://docs.oracle.com/en/cloud/saas/readiness/logistics/24b/wms24b/24B-wms-wn-f33087.htm

## 1. Đã rà soát những gì

Đã scan toàn bộ project từ gốc hiện tại:
- Tổng file hiện có trong cây repo/artifact: 900.
- Source chính: 380 file `.cs`, 132 file `.cshtml`, 41 file `.js/.ts`, 26 file `.css`.
- Đã đọc prompt audit tại `C:\Users\1\Downloads\Prompt Review Codebase.txt`.
- Đã đọc và rà soát các nhóm chính: `Controllers`, `Services`, `Models`, `Data`, `Migrations`, `Views`, `wwwroot`, `Program.cs`, `Authorization`, `tests/visual`, `WMS.Tests`.
- Đã kiểm tra `appsettings*` theo hướng chỉ đọc; không sửa secret, không đổi connection string.
- Đã kiểm tra DB thật theo read-only query; không reset, không seed, không chạy migration lên DB.

Các file/nhóm file trọng yếu đã đối chiếu:
- Navigation/layout: `Views/Shared/_SidebarNav.cshtml`, `Views/Shared/_Layout.cshtml`, `wwwroot/css/site.css`, `wwwroot/js/site.js`.
- Dashboard: `Controllers/HomeController.cs`, `Views/Home/Index.cshtml`.
- Nhập kho/xuất kho/tồn: `Services/InboundExecutionService.cs`, `Services/OutboundExecutionService.cs`, `Services/VoucherCancellationService.cs`, `Controllers/VouchersController.*.cs`, `Controllers/OperationsController.*.cs`.
- Kiểm kê/chốt tồn/báo cáo: `Controllers/ReportsController.StockCount.cs`, `Controllers/ReportsController.Inventory.cs`, `Views/Reports/*.cshtml`.
- DB integrity: `Data/AppDbContext.cs`, `Migrations/AppDbContextModelSnapshot.cs`, migration `20260704090000_AddStockSnapshotRuns.cs`, migration `20260705070000_RepairReportFefoDatabaseGuards.cs`.
- Security/RBAC: `Models/WmsRoles.cs`, `Models/AuthorizationModels.cs`, `Services/RbacSeedService.cs`, `Authorization/PermissionAuthorization.cs`, `Controllers/AccountController.cs`, `Controllers/SystemController.cs`, `Controllers/ApiIntegrationController.cs`.
- Visual tests: `tests/visual/wms-visual-regression.spec.ts`, `tests/visual/wms-mobile-deep.spec.ts`, `tests/visual/wms-no-device-evidence.spec.ts`.

## 2. Agent đã dùng

Đã dùng sub-agent theo lát cắt:
- Database/model/migrations: hoàn tất, phát hiện rủi ro destructive seed, cascade delete, unique null gaps, reservation constraints.
- Inventory flow: hoàn tất, phát hiện P0 xuất thiếu non-partial, P0 hủy nhập sau cross-dock, P1 kiểm kê dùng variance cũ.
- Authorization/security/API: hoàn tất, phát hiện P0 demo data, P1 confirm shipping thiếu permission policy, P1 API key quá rộng, P1 login rate-limit chưa gắn đúng.

Một số sub-agent khác bị lỗi hạ tầng token refresh bị revoke. Các phần đó đã được audit bù bằng local scan, DB query, build/test và Playwright.

## 3. Kết quả build/test/DB/visual

Build:
- `dotnet build WMS.csproj --no-restore /p:UseSharedCompilation=false /p:UseAppHost=false`: pass, 0 error.
- Còn 1 warning `MSB3061` do `WMS.exe` đang chạy dev server và bị lock khi build cố xóa file output. Không phải lỗi compile.

Unit/integration tests:
- `dotnet test WMS.Tests\WMS.Tests.csproj --no-build /p:UseSharedCompilation=false`: **693/693 passed**.
- Có chỉnh 1 dòng trong báo cáo cũ `WMS_FINAL_AUDIT_REPORT_2026_07_06.md` để không chứa literal host dev cục bộ làm quality gate fail. Không sửa code app.

DB read-only preflight trên DB hiện tại:
- Database: `HeThongNaNaNa`.
- EF migrations trong DB: 85.
- Migration mới nhất: `20260705070000_RepairReportFefoDatabaseGuards`.
- `ItemLocations` âm/over-reserved: 0.
- Duplicate `ItemLocations` theo batch key: 0.
- `StockReservations` âm/over-closed: 0.
- Duplicate active reservation theo key hiện tại: 0.
- `VoucherDetails` invalid qty/conversion: 0.
- HSD < NSX trong `VoucherDetails`: 0.
- Duplicate `StockCountLines`: 0.
- Duplicate `StockSnapshots` trong run: 0.
- Legacy/null snapshot run: 0.
- Duplicate `WarehousePeriodLocks`: 0.

Visual:
- `npm run visual:auth`: 1 passed.
- `npm run visual:test`: **194 passed / 66 skipped**.
- `npm run visual:mobile-deep`: lần đầu dùng host alias khác origin cookie nên bị false-fail do auth state không áp dụng. Rerun đúng origin cookie: **420 passed**.
- `npm run visual:no-device`: lần đầu cũng false-fail vì origin auth mismatch. Rerun đúng origin cookie: **10 passed**.

Probe active menu riêng:
- `/Reports/Inventory`: còn **2 active menu**: `Xem tồn kho`, `Báo cáo tồn kho`.
- `/Reports/StockMovement?nav=inbound`: 1 active `Lịch sử nhập kho`.
- `/Reports/StockMovement?nav=inventory`: 1 active `Lịch sử nhập xuất`.
- `/Items?stockStatus=low`: 1 active `Hàng sắp thiếu`.
- `/Warehouses/InventoryMap?map=master`: 1 active `Vị trí/kệ/khu chứa`.
- `/Warehouses/InventoryMap?map=inventory`: 1 active `Sơ đồ kho`.

## 4. Menu cũ và menu hiện tại còn vấn đề gì

Các lỗi đã sửa từ vòng trước có vẻ ổn:
- `StockMovement` đã tách active bằng `nav=inbound` và `nav=inventory`.
- `Items?stockStatus=low` không còn làm active `Danh mục vật tư`.
- `InventoryMap?map=master` và `map=inventory` đã tách active.
- Sidebar icon/rail đã ổn qua visual mobile-deep.

Vấn đề còn lại:
- `Reports/Inventory` đang được dùng cho cả nhóm `Tồn kho > Xem tồn kho` và `Báo cáo > Báo cáo tồn kho`.
- Trong `Views/Shared/_SidebarNav.cshtml`, cả hai link cùng điều kiện `act == "Inventory"`, gây 2 active menu cùng lúc.
- Đây là lỗi UX/navigation architecture, không làm vỡ route nhưng làm nhân viên hiểu sai vị trí hiện tại.

Hướng fix sau khi được phép:
- Cách nhẹ: thêm query `nav=inventory` và `nav=report` cho cùng action, active theo query.
- Cách sạch hơn: giữ `Reports/Inventory` cho báo cáo, tạo route/action riêng dạng `Inventory/Current` hoặc `Reports/InventoryReport` tùy kiến trúc mong muốn.
- Không xóa chức năng; chỉ tách ngữ cảnh điều hướng.

## 5. Menu mới đã được tổ chức đến đâu

Menu hiện tại đã khá sát cấu trúc WMS nội bộ:
- Trang chính.
- Nhập kho: tạo, duyệt, tiếp nhận, quét nhận, QC, lịch sử nhập.
- Xuất kho: tạo, wave, pick task, RF picking, next task, packing/shipping.
- Tồn kho: xem tồn, sơ đồ kho, tra cứu mã kiện/sê-ri, kiểm kê, điều chỉnh, di chuyển, hàng sắp thiếu, hàng chậm, lịch sử nhập xuất.
- Vận chuyển: dispatch, chuyến xe, đối soát, nhãn/chứng từ, dock board, yard, carrier, cross-dock.
- Báo cáo: tổng quan, KPI, nhập/xuất, tồn, vận chuyển, chi phí, quản trị dữ liệu, bất thường, HSD, ABC.
- Danh mục: đối tác, vật tư, đơn vị, kho/khu, vị trí, phân loại đơn, hợp đồng, bảng giá.
- Hệ thống: người dùng, yêu cầu truy cập, zone assignment, workflow, order streaming, SRE, audit, alert, chốt tồn, khóa kỳ, dữ liệu mẫu, automation, integration, trusted devices.
- Hướng dẫn sử dụng.

Đánh giá: nhóm menu đã hợp lý khoảng 85-90% cho WMS nội bộ. Cần xử lý duplicate `Reports/Inventory` và giảm thêm một số mục advanced nếu vai trò non-admin còn thấy quá nhiều.

## 6. Dashboard đã sửa/đang đạt đến đâu

Dashboard hiện đã không còn lặp nguyên sidebar như ban đầu. Nó đang theo mô hình các hệ thống lớn: management landing page phải cho quản lý thấy tình hình kho, cảnh báo và hành động nhanh ngay khi mở.

Đúng hướng:
- Card chính theo phân hệ: Nhập kho, Xuất kho, Tồn kho, Vận chuyển, Báo cáo, Cấu hình.
- Có `Bàn làm việc quản trị` cho admin.
- Có `Công việc cần xử lý` thay vì chỉ trang trí.
- Có KPI vận hành và cảnh báo.

Còn cần kiểm sau khi fix phase:
- Viewer/role thấp không được thấy card dẫn đến trang 403.
- Cần tách rõ “manager overview” và “operator workbench” nếu dùng nhiều role thực tế.
- Màn đầu tiên cho quản lý nên ưu tiên: tồn hiện tại, đơn/phiếu quá hạn, inbound chờ nhận, outbound chờ pick/ship, cảnh báo HSD/low stock, dữ liệu bất thường.

## 7. Permission/role hiện tại

Đúng hướng:
- Admin full quyền theo `WmsRoles.IsAdmin`.
- Đã có role chuyên biệt: Admin, Manager, InboundStaff, OutboundStaff, InventoryStaff, TransportStaff, ReportViewer/ReportingSpecialist, Viewer.
- Menu có điều kiện theo role: inbound/outbound/inventory/transport/report/admin-manager.
- Có permission model trong `AuthorizationModels.cs` và seed trong `RbacSeedService.cs`.

Vấn đề còn lại:
- Một số action backend vẫn dùng role tổng quát thay vì policy permission cụ thể.
- Seed RBAC hiện grant thêm, chưa reconcile/remove quyền dư.
- Một số link dashboard/topbar có thể hiện cho role không có quyền vào backend.

Role đề xuất cho WMS nội bộ:
- `Admin`: full system, DangerOps riêng cho thao tác phá hủy.
- `WarehouseManager`: quản lý vận hành, duyệt, xem báo cáo, không mặc định DangerOps.
- `InboundStaff`: nhập kho, receiving, RF receiving, QC cơ bản nếu được cấp.
- `OutboundStaff`: pick, pack, ship prep, RF picking.
- `InventoryStaff`: kiểm kê, di chuyển vị trí, tra cứu tồn, điều chỉnh nếu có permission riêng.
- `TransportStaff`: dispatch, shipment loads, delivery reconciliation, label/handover.
- `ReportViewer` hoặc `ReportingSpecialist`: báo cáo/KPI/export theo quyền.
- `Auditor`: xem audit/log/report, không sửa nghiệp vụ.
- `Support/ITOps`: quản trị thiết bị/tích hợp/telemetry, không tự động có quyền kho.
- `DangerOps`: permission cực hạn cho seed/reset/gộp dữ liệu/migration-runbook, không gắn theo role thường.

## 8. P0 - lỗi bắt buộc sửa trước khi gọi là an toàn

### P0-01 - Demo data seed có thể xóa dữ liệu thật

File:
- `Controllers/SystemController.cs:105`
- `Services/DemoDataSeedService.cs:323`
- `Services/DemoDataSeedService.cs:377`

Mô tả:
- `ApplyDemoData` chỉ yêu cầu confirm text, không có policy `DangerOps` và không gọi `IsDangerOpsAllowed()`.
- Service chạy nhiều `ExecuteDeleteAsync` trên stock snapshots, item locations, vouchers, reservations, ledger/audit liên quan.

Ảnh hưởng:
- Admin bấm nhầm trên DB hosting có thể wipe operational data.
- Đây là rủi ro lớn nhất vì user đang dùng DB hosting thật.

Hướng fix:
- Thêm `[Authorize(Policy = WmsPermissions.DangerOps)]`.
- Hard-block khi không phải Development và `System:AllowDangerOps` không bật.
- Thêm runbook token/backup checkpoint.
- Đổi label UI thành “Dữ liệu mẫu - nguy hiểm” và chỉ hiện trong môi trường phù hợp.

### P0-02 - Phiếu xuất không cho partial vẫn có thể xuất thiếu và hoàn tất

File:
- `Services/OutboundExecutionService.cs:112`
- `Services/OutboundExecutionService.cs:126`
- `Services/OutboundExecutionService.cs:207`
- `Services/OutboundExecutionService.cs:1197`

Mô tả:
- Gate post so `pickedAndReady` với `totalReserved`, không so với ordered/detail qty.
- Release có thể allocate thiếu nhưng vẫn tạo reservation khi `PartialShipmentAllowed == false`.

Ảnh hưởng:
- Đơn yêu cầu 10, tồn 6, có thể pick/post 6 và đóng completed nếu reservation hết.
- Sai nghiệp vụ fulfillment.

Hướng fix:
- Release non-partial phải fail nếu không allocate đủ ordered qty.
- Post non-partial phải so với tổng required qty của voucher detail.
- Thêm test: tồn thiếu + `PartialShipmentAllowed=false` phải không release/post được.

### P0-03 - Hủy phiếu nhập sau cross-dock có thể đảo tồn sai

File:
- `Services/InboundExecutionService.cs:158`
- `Services/VoucherCancellationService.cs:338`
- `Services/VoucherCancellationService.cs:355`
- `Services/VoucherCancellationService.cs:372`

Mô tả:
- Inbound complete chỉ cộng putaway qty sau khi trừ completed cross-dock qty.
- Cancel inbound lại trừ toàn bộ `detail.BaseQty`.

Ảnh hưởng:
- Có thể làm âm tồn hoặc trừ lấn vào tồn cùng item/lot ở vị trí inbound.

Hướng fix:
- `UndoInboundPosted` phải reverse đúng putaway qty, không trừ phần đã cross-dock.
- Nếu cross-dock có stage riêng thì reverse stage/cross-dock theo flow riêng.

### P0-04 - Chốt tồn ngày quá khứ đang lấy tồn hiện tại nếu tạo snapshot mới

File:
- `Controllers/ReportsController.Inventory.cs:1188`
- `Controllers/ReportsController.Inventory.cs:1497`
- `Controllers/ReportsController.Inventory.cs:1521`

Mô tả:
- Khi chưa có snapshot, màn preview/generate dùng `ItemLocations` hiện tại để tạo snapshot cho `snapshotDate` được chọn.

Ảnh hưởng:
- Nếu chọn ngày quá khứ, snapshot không phải lịch sử tại ngày đó mà là tồn hiện tại gắn vào ngày quá khứ.

Hướng fix:
- Chỉ cho chốt ngày hiện tại hoặc ngày khóa kỳ hợp lệ.
- Nếu muốn snapshot quá khứ, phải rebuild từ `InventoryTransactions` theo cutoff date, không dùng tồn hiện tại.

## 9. P1 - nên sửa trước demo/bảo vệ

### P1-01 - `ItemLocations` có rủi ro cascade delete theo convention

File:
- `Data/AppDbContext.cs:982`
- `Migrations/AppDbContextModelSnapshot.cs` quanh entity `ItemLocation`.

Ảnh hưởng:
- Xóa physical item/location bằng script/admin có thể kéo mất tồn vị trí.

Fix:
- Explicit `DeleteBehavior.NoAction` cho `ItemLocation -> Item`, `Location`, `OwnerPartner`.

### P1-02 - Unique key `StockCountLines` hở với null lot/expiry

File:
- `Data/AppDbContext.cs:1605`

Ảnh hưởng:
- Duplicate line kiểm kê với no-lot/lot-only/expiry-only có thể lọt qua DB.

Fix:
- Tạo 4 filtered unique indexes giống logic batch key của `ItemLocations`.

### P1-03 - Migration `AddStockSnapshotRuns` có rủi ro backfill/unique fail

File:
- `Migrations/20260704090000_AddStockSnapshotRuns.cs:47`
- `Migrations/20260704090000_AddStockSnapshotRuns.cs:82`

Ảnh hưởng:
- DB có nhiều snapshot cùng ngày/kho/item/owner có thể fail hoặc merge sai run lịch sử.

Fix:
- Preflight duplicate, backup, cleanup, migration theo run key an toàn.

### P1-04 - Confirm shipping dùng role, chưa dùng permission policy

File:
- `Controllers/VouchersController.Outbound.cs:827`
- `Controllers/VouchersController.Outbound.cs:856`
- `Services/RbacSeedService.cs:156`

Ảnh hưởng:
- Role nằm trong `TransportRoles` có thể confirm shipping dù matrix permission không cấp rõ.

Fix:
- Thêm `[Authorize(Policy = WmsPermissions.VoucherConfirmShipping)]`.
- Test Staff không có permission phải 403.

### P1-05 - Login/MFA rate limit chưa gắn đúng action

File:
- `Program.cs` có policy `login`.
- `Controllers/AccountController.cs:520` Login POST chưa có `[EnableRateLimiting("login")]`.
- `Controllers/AccountController.cs:706` VerifyMfa POST chưa gắn rate limit.

Ảnh hưởng:
- Brute force login/MFA dễ hơn mức nên có.

Fix:
- Gắn rate limit cho Login GET/POST và VerifyMfa GET/POST.

### P1-06 - API key tích hợp là key đơn, quyền quá rộng

File:
- `Controllers/ApiIntegrationController.cs:48`
- Nhiều endpoint gọi `ValidateApiKey()`.

Ảnh hưởng:
- Một key dùng đọc inventory cũng có thể gọi write/replay/issue nếu biết endpoint.

Fix:
- Key hash per client + scopes: `read:stock`, `write:voucher`, `admin:replay`, `billing:issue`.

### P1-07 - Cross-dock thiếu owner/warehouse scope chặt

File:
- `Controllers/OperationsController.Advanced.cs`
- `Services/CrossDockService.cs`

Ảnh hưởng:
- Có thể tạo/complete cross-dock sai owner hoặc vượt warehouse scope trong một số path.

Fix:
- Owner scope cho inbound/outbound opportunity, execute và complete.
- Complete nhận scoped warehouse/owner.

### P1-08 - Duyệt kiểm kê dùng variance cũ trên tồn hiện tại

File:
- `Controllers/ReportsController.StockCount.cs:218`
- `Controllers/ReportsController.StockCount.cs:442`

Ảnh hưởng:
- Nếu tồn thay đổi giữa lúc lưu nháp và duyệt, số sau duyệt không bằng counted qty.

Fix:
- Re-read current qty khi approve; nếu khác `SystemQty`, bắt recount/rebase hoặc tính `CountedQty - currentQty`.

## 10. P2 - cải thiện quan trọng

- `StockReservations` active unique index nên thêm `OwnerPartnerId`.
- DB nên có check `ConsumedQty + ReleasedQty <= ReservedQty`.
- `WarehousePeriodLocks` unique vĩnh viễn theo warehouse/date; nếu cần lịch sử lock/unlock nên đổi sang filtered unique active.
- FEFO auto-pick helper nên nhận và filter `OwnerPartnerId`.
- Trusted device token quá dài và bind yếu; cần TTL ngắn hơn, opt-in, server-side revoke.
- MFA challenge đang truy cập bằng id; nên dùng opaque token + bind session/temp cookie/IP-UA.
- RBAC seed chỉ grant, chưa revoke quyền thừa.
- Các permission như `StockCountApprove`, `StockCountUnlock`, QC submit/resolve cần map vào policy backend đầy đủ.
- `Reports/Inventory` double active menu: P2 UX-route, nên tách query/action.
- `VoucherDetails` nên có DB check cho qty/conversion/price invariants nếu import/raw SQL có thể ghi.
- CSP còn thiếu; nên triển khai `Content-Security-Policy-Report-Only` trước.
- Public API docs nên yêu cầu API key hoặc chỉ public bản docs tối giản.

## 11. P3 - nâng cấp dài hạn

- Tách rõ “operator workbench” và “manager control tower”.
- Thêm Auditor/Support/ITOps role nếu dự án có vận hành thật.
- Thêm runbook backup/restore trước mọi migration có unique/check mới.
- Thêm report phân biệt “tồn hiện tại” và “tồn lịch sử dựng từ ledger”.
- Thêm contract test cho export Excel khớp UI.
- Thêm test multi-owner FEFO, cross-dock và stock count stale snapshot.
- Thêm retention/sampling cho request telemetry, tránh ghi static assets.

## 12. Ảnh hưởng route/component

Chưa sửa code nghiệp vụ vòng này, nên không có route/component nào bị đổi.

Các route đã probe:
- `StockMovement` inbound/inventory active đúng nhờ query `nav`.
- `Items?stockStatus=low` active đúng.
- `InventoryMap?map=master/inventory` active đúng.
- `Reports/Inventory` vẫn double active và cần fix ở vòng sau.

## 13. Rủi ro database

DB hiện tại sạch ở các check read-only đã chạy, nhưng schema vẫn cần cứng hơn:
- Cần migration cho `ItemLocations` delete behavior.
- Cần migration cho unique `StockCountLines` với null-safe filtered indexes.
- Cần migration/check constraint cho reservation over-close.
- Cần cân nhắc migration active unique cho period locks.
- Trước các migration unique/check bắt buộc backup DB hosting và chạy preflight duplicate.

Không được tự chạy migration thật khi chưa xác nhận.

## 14. Kế hoạch fix an toàn

Thứ tự đề xuất:

1. Data safety: khóa `ApplyDemoData` bằng `DangerOps` + env guard.
2. Inventory correctness: sửa non-partial outbound và cancel inbound cross-dock.
3. Stock snapshot: chặn snapshot quá khứ bằng tồn hiện tại hoặc rebuild từ ledger.
4. DB constraints: `ItemLocations` delete behavior, `StockCountLines`, reservation check.
5. Authorization: `ConfirmShipping`, login/MFA rate limit, API key scopes, policy cho stockcount/QC.
6. Navigation: tách active `Reports/Inventory`.
7. Tests: thêm unit/integration cho P0/P1, rerun visual full.
8. Sau đó mới tinh chỉnh dashboard/role UX nâng cao.

## 15. File không nên sửa nếu chưa xác nhận

- `appsettings.json`
- `appsettings.Development.json`
- `appsettings.Production.json` nếu có
- file chứa connection string thật
- migration production đang áp trên DB hosting
- seed/demo data thật
- mọi thao tác reset/seed/migrate DB hosting

## 16. Điểm cần bạn xác nhận trước khi fix

1. Có bật cơ chế `DangerOps` trong DB hosting không, hay khóa hẳn demo seed trên hosting?
2. `Reports/Inventory` nên tách thành 2 route hay giữ 1 action với query `nav=inventory/report`?
3. Với phiếu xuất không đủ tồn và `PartialShipmentAllowed=false`, hệ thống phải fail ngay khi release hay cho giữ draft để chờ bổ sung tồn?
4. Khi stock count bị stale do tồn thay đổi trước lúc duyệt, muốn bắt kiểm lại hay tự rebase theo counted qty?
5. Chốt tồn quá khứ có cần nghiệp vụ rebuild từ ledger không, hay chỉ cho chốt ngày hiện tại?

## 17. Đánh giá phần trăm hiện tại

Ước lượng thực tế:
- Code/build/test nền: 88%.
- UI desktop/mobile sau visual đúng origin: 90%.
- Navigation/role UX: 82% do còn double active và role link cần rà thêm.
- Data hiện tại trong DB: 92% sạch theo check đã chạy.
- Schema data integrity: 75% do còn constraint/delete behavior gaps.
- Inventory business correctness: 70% do còn P0 outbound/cross-dock/snapshot.
- Security/RBAC: 68% do còn DangerOps/API key/rate-limit/policy gaps.
- Enterprise WMS parity: 60-68%.
- WMS nội bộ demo nếu fix P0/P1 trước: có thể lên 85-90%.

Kết luận: chưa thể nói 100% hoặc 0 bug. Vòng audit này tìm được các lỗi trọng yếu còn lại; bước tiếp theo nên fix theo nhóm nhỏ, bắt đầu từ P0-01 đến P0-04.
