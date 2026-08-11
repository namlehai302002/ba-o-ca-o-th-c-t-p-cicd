# Báo cáo refactor navigation/sidebar/dashboard WMS - 05/07/2026

## 1. Đã rà soát những file nào

- Layout/navigation: `Views/Shared/_Layout.cshtml`, `Views/Shared/_SidebarNav.cshtml`.
- Dashboard: `Views/Home/Index.cshtml`, `Controllers/HomeController.cs`, `ViewModels/ViewModels.cs`.
- Báo cáo tổng quan kho và cảnh báo dữ liệu: `Views/Reports/WarehouseOverview.cshtml`, `Controllers/ReportsController.WarehouseOverview.cs`, `ViewModels/WarehouseOverviewViewModels.cs`.
- Nhóm route nghiệp vụ đang được gom menu: `Controllers/VouchersController*.cs`, `Controllers/OperationsController*.cs`, `Controllers/ReportsController*.cs`, `Controllers/WarehousesController.cs`, `Controllers/ItemsController.cs`, `Controllers/UsersController.cs`, `Controllers/SystemController.cs`, `Controllers/LabelsController.cs`.
- Phân quyền/role/permission: `Program.cs`, `Models/AuthorizationModels.cs`, `Data/AppDbContext.cs`, `AccountController.cs`, các `[Authorize]` trong controller liên quan.
- Visual/regression test: `tests/visual/wms-visual-regression.spec.ts`, snapshot trong `tests/visual/wms-visual-regression.spec.ts-snapshots`.
- Unit/integration test liên quan: `WMS.Tests/EnterpriseUiRedesignTests.cs`, `WMS.Tests/EnterpriseUiUxPolishTests.cs`, `WMS.Tests/BusinessLogicHardeningTests.cs`, `WMS.Tests/LoginHelpRequestTests.cs`.
- Database audit: chạy `scripts/Invoke-WmsDataQualityAudit.ps1` trên connection hiện tại từ `Properties/launchSettings.json`; không chỉnh `appsettings.json`, `appsettings.Development.json`, `launchSettings.json`.
- Tham chiếu cách các hệ WMS lớn tổ chức nghiệp vụ: Microsoft Dynamics 365 warehouse menu/mobile flows, Microsoft WMS-only, SAP EWM warehouse monitor, Oracle WMS, Manhattan WMS.

## 2. Menu cũ đang có vấn đề gì

- Sidebar bị quá tải: nhiều chức năng nâng cao nằm ngay trong nhóm vận hành hằng ngày, làm nhân viên kho phải nhìn quá nhiều mục.
- Một số mục sai ngữ cảnh nghiệp vụ: bãi xe/cửa bến/phí bãi/3PL nằm lẫn trong luồng nhập kho hoặc tồn kho, trong khi thực tế nên thuộc vận chuyển, danh mục hợp đồng hoặc báo cáo chi phí.
- Báo cáo và tồn kho có mục thống kê trùng cảm giác chức năng; dashboard lại lặp gần như toàn bộ sidebar nên nhìn nặng.
- Collapsed sidebar có cảm giác lệch vì rail icon và flyout phụ thuộc menu cũ quá dài, nhiều nhóm không cùng logic.
- Trang tổng quan kho có text nửa Anh nửa Việt: `ItemLocations`, `Quantity`, `ReservedQty`, `InventoryTransactions`, `NEGATIVE_STOCK`, `OVER_RESERVED` bị lộ ra UI.

## 3. Menu mới đã được tổ chức lại ra sao

- `Trang chính`: vào dashboard điều hành.
- `Nhập kho`: chỉ giữ luồng hàng vào kho: tạo phiếu nhập, duyệt phiếu nhập, tiếp nhận, quét nhận, QC, lịch sử nhập.
- `Xuất kho`: chỉ giữ luồng hàng ra khỏi kho: tạo phiếu xuất, đợt gom đơn, nhiệm vụ lấy hàng, quét lấy hàng, nhiệm vụ tiếp theo, đóng gói & giao.
- `Tồn kho`: số lượng/vị trí/mã kiện/sê-ri/kiểm kê/điều chỉnh/di chuyển/hàng sắp thiếu/hàng chậm/lịch sử nhập xuất.
- `Vận chuyển`: điều phối vận chuyển, bảng chuyến xe, đối soát giao hàng, nhãn & chứng từ, cửa bến, bãi xe, bộ kết nối vận tải, chuyển thẳng.
- `Báo cáo`: tổng quan kho, chỉ số vận hành, thống kê nhập/xuất, tồn kho, vận hành vận chuyển, chi phí, đối soát phí bãi, tính phí 3PL, quản trị dữ liệu, bất thường, sắp hết hạn, phân nhóm quan trọng.
- `Danh mục`: đối tác, vật tư, đơn vị tính, khu vực kho, vị trí/kệ/khu chứa, cấu hình phân loại đơn, hợp đồng 3PL, bảng giá phí bãi, bảng giá kho nhiều chủ hàng.
- `Hệ thống`: người dùng, yêu cầu truy cập, phân quyền khu vực, quy tắc vận hành, phát hành trực tiếp, giám sát, nhật ký, phân tích nhật ký, cảnh báo, chốt tồn, khóa kỳ, dữ liệu mẫu, tự động hóa, tích hợp, thiết bị tin cậy.
- `Hướng dẫn sử dụng`: giữ riêng, không trộn vào nghiệp vụ.

## 4. Những chức năng nào được di chuyển sang nhóm khác

- `DockBoard` và `YardManagement`: chuyển khỏi nhập kho, gom vào `Vận chuyển`.
- `ShippingDispatch`, `ShipmentLoads`, `DeliveryReconciliation`, `CarrierConnectors`, `CrossDockOpportunities`: nằm trong `Vận chuyển`, không còn lẫn ở xuất kho/tồn kho.
- `YardBillingRates`, `ThreePlBillingRates`, `ThreePlContracts`: gom vào `Danh mục` vì là dữ liệu nền/hợp đồng/bảng giá.
- `YardBillingCharges`, `ThreePlBillingRuns`, `FinancialCostDashboard`: gom vào `Báo cáo` với quyền xem báo cáo tài chính.
- `SortationConfigs`: gom vào `Danh mục`; `OrderStreamingConfigs`, `AutomationDashboard`, `IntegrationDashboard`: gom vào `Hệ thống`.
- `StockSnapshot`, `PeriodLocks`, `DemoData`: gom vào `Hệ thống`; đổi nhãn `DemoData` thành `Dữ liệu mẫu` trên menu để không lộ text dev quá mạnh.
- `Tra cứu phiếu` không còn là nhóm sidebar chính để giảm nhiễu; route cũ vẫn còn, có thể truy cập qua search/topbar/link nội bộ.

## 5. Dashboard đã sửa gì

- Dashboard chỉ còn bàn làm việc quản trị gọn, bàn làm việc nhanh, công việc cần xử lý, chỉ số vận hành và cảnh báo khẩn.
- Bàn làm việc nhanh đại diện nhóm lớn: nhập kho, xuất kho, tồn kho, vận chuyển, báo cáo, cấu hình; không lặp toàn bộ sidebar.
- Công việc cần xử lý chỉ hiện task có số lượng thật: phiếu nhập chờ duyệt, nhiệm vụ lấy hàng, nhiệm vụ di chuyển, chuyến giao trễ. Nếu không có việc thì hiện trạng thái rỗng rõ ràng.
- Thêm `OpenMovementTasks` để dashboard biết nhiệm vụ di chuyển thật sự đang mở.
- Action nhanh tạo phiếu/nhận việc chỉ hiện với người có quyền vận hành.

## 6. Permission/role menu đã xử lý như thế nào

- Không tạo migration/role mới để tránh phá dữ liệu hosting hiện tại.
- Chuẩn hóa theo role/claim đang có trong hệ thống:
  - `Admin`: thấy toàn bộ, gồm hệ thống, dữ liệu mẫu, bảo mật, báo cáo tài chính.
  - `Manager`: thấy vận hành, báo cáo, danh mục, cấu hình nghiệp vụ; không thấy một số chức năng chỉ admin.
  - `Staff`: thấy nhóm vận hành chính: nhập kho, xuất kho, tồn kho, vận chuyển cơ bản.
  - `Viewer`: thấy tồn kho/báo cáo đọc dữ liệu theo quyền xem.
- Menu tài chính dùng `report.view.financial` hoặc Admin để tránh lộ chi phí cho người không có quyền.
- Route/component cũ vẫn giữ `[Authorize]` hiện có; sidebar chỉ ẩn/hiện link theo role để giảm rối, không thay thế bảo mật backend.
- Nếu muốn tách hẳn `Nhân viên vận chuyển`, `Nhân viên kiểm kê`, `Nhân viên báo cáo` thành role độc lập như enterprise WMS, cần xác nhận thêm để thêm role/permission seed và mapping DB.

## 7. Có ảnh hưởng route/component nào không

- Không xóa controller/action/view hiện có.
- `_Layout.cshtml` chỉ thay block sidebar dài bằng partial `_SidebarNav.cshtml`, nên route mapping cũ vẫn giữ.
- Link mới vẫn trỏ vào action cũ: `Vouchers/Create`, `Operations/*`, `Reports/*`, `Warehouses/*`, `Items/*`, `Labels/Index`, `System/*`, `Users/*`.
- Các route nâng cao không biến mất: phí bãi, tính phí 3PL, hợp đồng 3PL, chốt tồn, khóa kỳ, dữ liệu mẫu, tích hợp, tự động hóa vẫn có đường vào đúng nhóm.
- Visual baseline được cập nhật cho 5 ảnh desktop bị lệch do sidebar mới; đây là thay đổi chủ đích, không phải lỗi layout.

## 8. Kết quả build/lint/test

- `dotnet build WMS.csproj --no-restore`: pass, 0 warning, 0 error.
- `dotnet build WMS.Tests/WMS.Tests.csproj --no-restore`: pass, 0 warning, 0 error.
- `dotnet test WMS.Tests/WMS.Tests.csproj --no-build`: pass, 692 passed, 0 failed, 0 skipped.
- Full visual regression: pass, 194 passed, 66 skipped, 0 failed.
- Auth visual đã refresh bằng `npm run visual:auth`.
- DB audit hosting: `artifacts/data-quality/wms-data-quality-audit-20260705-191456.txt`, 17 result sets, 0 issue rows.
- App đã được chạy bằng URL nội bộ của dev server để kiểm tra giao diện thật.

## 9. Những điểm còn cần tôi xác nhận thêm

- Có muốn thêm role DB độc lập cho `TransportStaff`, `InventoryCounter`, `ReportViewer` không, hay giữ mô hình `Admin/Manager/Staff/Viewer` hiện tại cho gọn?
- `Tra cứu phiếu` nên tiếp tục chỉ nằm ở search/topbar, hay muốn đưa lại thành một link nhỏ trong `Tồn kho` hoặc `Hướng dẫn sử dụng`?
- `Dữ liệu mẫu` có nên ẩn hoàn toàn trên hosting production bằng config/permission riêng không?
- Các module 3PL/phí bãi có dùng thường xuyên không; nếu ít dùng, có thể gom sâu hơn dưới một trang `Chi phí & 3PL` để sidebar còn gọn hơn.

## Nguồn tham chiếu nghiệp vụ

- Microsoft Dynamics 365 - cấu hình menu/mobile device warehouse: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/configure-mobile-devices-warehouse
- Microsoft Dynamics 365 - warehouse management overview: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview
- Microsoft Dynamics 365 - WMS-only mode: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/wms-only-mode-overview
- SAP Extended Warehouse Management - warehouse monitor: https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT/3d97bec9bf1649099384bb8167df3cf2/51cdcb53ad377114e10000000a174cb4.html
- Oracle Warehouse Management: https://www.oracle.com/scm/logistics/warehouse-management/
- Manhattan Warehouse Management: https://www.manh.com/solutions/supply-chain-management-software/warehouse-management
