# Báo cáo rà soát navigation, dashboard, RBAC và dữ liệu WMS

Ngày rà soát: 06/07/2026  
Phạm vi: hệ thống quản lý kho nội bộ, ưu tiên nghiệp vụ kho, menu gọn, role rõ, Admin toàn quyền, không làm vỡ route/component hiện có.

## 1. Đã rà soát những file nào

- Cấu hình và startup: `Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`.
- Role/RBAC: `Models/WmsRoles.cs`, `Services/RbacSeedService.cs`, `Models/AuthorizationModels.cs`, `WMS.Tests/UnitTest1.cs`.
- Layout/navigation: `Views/Shared/_Layout.cshtml`, `Views/Shared/_SidebarNav.cshtml`, CSS/layout liên quan qua visual tests.
- Dashboard: `Controllers/HomeController.cs`, `Views/Home/Index.cshtml`, `Services/Enterprise1113Services.cs`.
- Người dùng/role UI: `Controllers/AccountController.cs`, `Views/Users/Index.cshtml`, `Views/Account/TrustedDevices.cshtml`.
- Nhập kho/xuất kho/tồn kho/vận chuyển: `Controllers/VouchersController*.cs`, `Controllers/OperationsController*.cs`.
- Báo cáo: `Controllers/ReportsController*.cs`, `Views/Reports/WarehouseOverview.cshtml`, các report được Playwright quét.
- Hướng dẫn: `Views/Help/Index.cshtml`.
- Roadmap: `ROADMAP_WMS_REPORT_HSD_FEFO_LOG_CONTROL.md`.
- Test/visual: `WMS.Tests/UnitTest1.cs`, `WMS.Tests/EnterpriseUiUxPolishTests.cs`, `tests/visual/*.ts`.
- DB audit artifact: `artifacts/data-quality/wms-rbac-data-quality-audit-2026-07-06.sql`, `artifacts/data-quality/wms-rbac-data-quality-audit-2026-07-06.txt`.

Không xóa hoặc làm sạch secret trong `appsettings.json`. File cấu hình chỉ được đọc để xác định DB hosting và chạy audit.

## 2. Menu cũ đang có vấn đề gì

- Nhập kho, Xuất kho, Tồn kho, Báo cáo, Hệ thống bị trộn nhiều chức năng không cùng nghiệp vụ.
- Nhiều mục nâng cao như tính phí kho nhiều chủ hàng, bảng giá, chốt tồn, khóa kỳ, phân tích nhật ký, demo dữ liệu nằm rải rác làm nhân viên kho thấy nặng.
- Dashboard lặp lại quá nhiều menu con, giống một sidebar thứ hai.
- Sidebar khi thu gọn có icon không thẳng hàng và flyout dài, khó đọc.
- Một số route báo cáo đã có menu nhưng role báo cáo chưa vào được route tương ứng.
- Một số text UI còn nửa Anh nửa Việt như `Net`, `Dòng ledger`, `Cockpit`, `ledger`, `reservation`, `scope`, `work order`.

## 3. Menu mới đã được tổ chức lại ra sao

- `Trang chính`: màn điều hành theo ca.
- `Nhập kho`: tạo phiếu nhập, duyệt phiếu nhập, tiếp nhận, quét nhận hàng, kiểm tra chất lượng, lịch sử nhập.
- `Xuất kho`: tạo phiếu xuất, đợt gom đơn, nhiệm vụ lấy hàng, quét lấy hàng, nhiệm vụ tiếp theo, đóng gói & giao.
- `Tồn kho`: xem tồn, sơ đồ kho, mã kiện, số sê-ri, kiểm kê, điều chỉnh tồn, nhiệm vụ/quét di chuyển, hàng sắp thiếu, hàng chậm, lịch sử nhập xuất.
- `Vận chuyển`: điều phối vận chuyển, bảng chuyến xe, đối soát giao hàng, nhãn & chứng từ, dock/yard, bộ kết nối vận tải, chuyển thẳng.
- `Báo cáo`: tổng quan kho, chỉ số vận hành, thống kê nhập/xuất, báo cáo tồn, vận hành vận chuyển, chi phí, quản trị dữ liệu, bất thường, sắp hết hạn, phân nhóm quan trọng.
- `Danh mục`: đối tác, danh mục vật tư, đơn vị tính, kho/vị trí, cấu hình phân loại đơn, hợp đồng và bảng giá kho nhiều chủ hàng.
- `Hệ thống`: người dùng, yêu cầu truy cập, phân quyền khu vực, quy tắc vận hành, giám sát, nhật ký, cảnh báo, chốt tồn, khóa kỳ, dữ liệu mẫu, tự động hóa, tích hợp, thiết bị tin cậy.
- `Hướng dẫn sử dụng`: giữ riêng, không trộn vào nghiệp vụ.

## 4. Những chức năng nào được di chuyển sang nhóm khác

- `Bảng giá phí bãi` -> `Danh mục`.
- `Bảng giá kho nhiều chủ hàng` -> `Danh mục`.
- `Hợp đồng kho nhiều chủ hàng` -> `Danh mục`.
- `Tính phí kho nhiều chủ hàng` -> `Báo cáo`/chi phí, chỉ hiện khi có quyền tài chính.
- `Chốt tồn`, `Khóa kỳ` -> `Hệ thống`.
- `Demo dữ liệu` -> `Hệ thống`, chỉ Admin.
- `Phân tích nhật ký`, `Nhật ký hệ thống`, `Cảnh báo` -> `Hệ thống`.
- `Cấu hình phân loại đơn`, cấu hình kho/vị trí -> `Danh mục`.
- Các nghiệp vụ chuyến xe, điều phối, đối soát giao hàng, nhãn/chứng từ -> `Vận chuyển`.

Không xóa route cũ. Các link vẫn trỏ về controller/action hiện có.

## 5. Dashboard đã sửa gì

- Dashboard chỉ còn các lối vào chính theo nhóm: Nhập kho, Xuất kho, Tồn kho, Vận chuyển, Báo cáo, Cấu hình.
- `Bàn làm việc quản trị` chỉ hiện cho Admin/Manager.
- `Công việc cần xử lý` chỉ hiện việc có hành động thật: phiếu nhập chờ duyệt, nhiệm vụ lấy hàng, di chuyển tồn, phiếu giao trễ.
- Card KPI giữ mức vừa đủ: tổng vật tư, giá trị tồn nếu có quyền tài chính, mã hàng sắp thiếu, phiếu hôm nay, đợt lấy hàng mở, tỷ lệ đáp ứng giữ chỗ.
- Mục text nửa Anh nửa Việt đã sửa: `Net` -> `Chênh lệch`, `Dòng ledger` -> `Dòng sổ kho`, `Cockpit` -> `Bảng điều hành`.
- Hướng dẫn sử dụng đã Việt hóa các thuật ngữ lộ cho người dùng như `ledger`, `reservation`, `scope`, `work order`, `component`, `idempotency`, `retry`.

## 6. Permission/role menu đã xử lý như thế nào

- `Admin`: toàn quyền. DB audit xác nhận Admin có đủ 25/25 permission, không thiếu permission nào.
- `Manager`: quản lý kho, duyệt nghiệp vụ, cấu hình vận hành, xem báo cáo quản trị.
- `Staff`: vai trò vận hành cũ, vẫn giữ để không vỡ tài khoản hiện hữu; dùng cho nhân viên kiêm nhiệm.
- `InboundStaff`: nhập kho, tiếp nhận, quét nhận, kiểm tra chất lượng.
- `OutboundStaff`: xuất kho, lấy hàng, quét lấy hàng, đóng gói/bàn giao.
- `InventoryStaff`: tồn kho, kiểm kê, điều chỉnh, di chuyển tồn, tra mã kiện/số sê-ri.
- `TransportStaff`: điều phối giao hàng, chuyến xe, chứng từ, đối soát.
- `ReportViewer`: xem dashboard/báo cáo vận hành, không làm đổi tồn kho; đã mở route `WarehouseOverview`, `OpsKpi`, `TopItems`, `Analytics`, `SpaceUtilization`, `DockToStock`, `SemanticBi`, `PredictiveAlerts`, `AiAssistant`, `ExceptionCenter` dạng đọc.
- `Viewer`: chỉ xem dữ liệu cơ bản và tồn kho/tra cứu được phân quyền.

Các route tạo/sửa/duyệt/ghi sổ vẫn không mở cho `Viewer`/`ReportViewer`. Báo cáo tài chính vẫn cần claim `report.view.financial`; ReportViewer không được cấp mặc định.

Vai trò `Nhân viên kiểm kê` được map vào `InventoryStaff`. Nếu muốn tách riêng `CycleCountStaff` thì cần xác nhận thêm vì hiện nghiệp vụ kiểm kê đang nằm trong nhóm tồn kho.

## 7. Có ảnh hưởng route/component nào không

- Không xóa controller/action cũ.
- Không đổi route URL public hiện có.
- Chỉ đổi nhóm menu/label và mở rộng quyền đọc cho role báo cáo ở các báo cáo phù hợp.
- `VouchersController.Create` có chặn theo loại phiếu: nhập chỉ role nhập, xuất chỉ role xuất, điều chỉnh/chuyển kho chỉ role tồn kho/kiểm kê; Admin/Manager/Staff legacy vẫn tương thích.
- `Waves` giữ Admin/Manager vì là lập đợt gom đơn cấp điều phối.
- `PickTasks/RfPicking` cho role xuất kho.
- `Receiving/RfReceiving/QualityInspection` cho role nhập kho.
- `Movement/RfMovement/StockCount` cho role tồn kho/kiểm kê.
- `ShipmentLoads/ShippingDispatch/DeliveryReconciliation` cho role vận chuyển.
- `ExceptionCenter` đọc được bởi vận hành + ReportViewer; thao tác xử lý vẫn giới hạn theo role vận hành/quản lý.

## 8. Kết quả build/lint/test

- `dotnet build WMS.csproj --no-restore /p:UseSharedCompilation=false`: PASS, 0 warning, 0 error.
- `dotnet build WMS.Tests/WMS.Tests.csproj --no-restore /p:UseSharedCompilation=false`: PASS, 0 warning, 0 error.
- `dotnet test WMS.Tests/WMS.Tests.csproj --no-build`: PASS, 692/692.
- `npm run visual:auth`: PASS, 1/1.
- `npm run visual:test`: PASS, 194 passed, 66 skipped, 0 failed.
- `npm run visual:public`: PASS, 6/6.
- `npm run visual:no-device`: PASS, 10/10.
- `npm run visual:mobile-deep`: PASS, 420/420.
- `npm run e2e:real`: 8 skipped theo thiết kế vì không bật `WMS_REAL_E2E`; không bật write trên DB hosting thật vì spec ghi rõ chỉ bật `WMS_REAL_E2E_WRITE=true` với DB disposable/staging.
- Không có npm lint script trong `package.json`; không chạy lint riêng.

DB hosting audit:

- Expected roles missing: không có.
- Admin missing permissions: không có.
- Role/permission duplicate: không có.
- Active users unknown role: 0.
- ItemLocations âm/giữ chỗ vượt tồn: 0.
- Duplicate ItemLocation grains: 0.
- HSD trước NSX: 0.
- Posted vouchers without ledger/sổ kho: 0.
- StockSnapshots thiếu run id: 0.
- PickTasks mở nhưng thiếu nguồn kho: 0.
- Active users hiện tại: Admin 7, Staff 1.

Roadmap `ROADMAP_WMS_REPORT_HSD_FEFO_LOG_CONTROL.md`: số checkbox chưa tick là 0.

## 9. Những điểm còn cần tôi xác nhận thêm

- Có muốn chuyển tài khoản thật từ `Staff` legacy sang các role hẹp hơn (`InboundStaff`, `OutboundStaff`, `InventoryStaff`, `TransportStaff`, `ReportViewer`) không? DB hiện chưa có active user nào ở các role mới ngoài Admin/Staff.
- Có cần tách riêng `CycleCountStaff/Nhân viên kiểm kê` khỏi `InventoryStaff` không?
- Có muốn bật `WMS_REAL_E2E_WRITE=true` trên một DB staging/disposable để test vòng đời tạo phiếu thật end-to-end không? Không nên bật trên DB hosting đang dùng làm dữ liệu làm việc nếu chưa có kế hoạch dọn dữ liệu.
- Có cần thêm SSO/MFA bắt buộc, phân quyền theo ca/kho/zone chi tiết hơn, và quy trình duyệt thay đổi role không?
- Có cần kiểm thử tải, backup/restore drill, HA/DR, thiết bị RF/camera/máy in thật, cân điện tử, tích hợp ERP/TMS/carrier thật không?

## Đánh giá so với WMS lớn trên thế giới

Tham chiếu chính thức:

- Microsoft Dynamics 365: warehouse worker/mobile device user và menu theo tác vụ kho: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/mobile-device-work-users
- Microsoft Dynamics 365: cấu hình mobile device menu items: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/configure-mobile-devices-warehouse
- Oracle WMS Cloud: role/group/permission và role permissions UI: https://docs.oracle.com/en/cloud/saas/readiness/logistics/26b/wms26b/26B-wms-wn-f45822.htm
- Oracle WMS Cloud: ACL/functional security, Admin có quyền hệ thống: https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owsec/access-control-lists-functional-security.html
- SAP EWM: vai trò warehouse manager/specialist/worker/goods receipt/goods issue/yard: https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT/3d97bec9bf1649099384bb8167df3cf2/489823f12f6d73e9e10000000a42189b.html
- SAP EWM Warehouse Management Monitor: https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT/3d97bec9bf1649099384bb8167df3cf2/51cdcb53ad377114e10000000a174cb4.html
- Manhattan Labor Management: dashboard năng suất và workforce planning: https://www.manh.com/solutions/supply-chain-management-software/labor-management-system

Đánh giá thực tế sau vòng này:

- So với mục tiêu WMS nội bộ cho nhân viên kho: khoảng 92/100 theo bằng chứng local + DB audit + visual test hiện có.
- So với Tier-1 global WMS như SAP EWM/Oracle WMS/Manhattan/Dynamics: khoảng 82-86/100 về phạm vi phần mềm hiện thấy trong repo. Khoảng cách còn lại chủ yếu là kiểm chứng sản xuất, thiết bị thật, tích hợp thật, UAT nhiều ca/kho/chủ hàng, load/soak/security/DR, labor management nâng cao, yard/automation/carrier certification.

Để tiến sát 100% theo chuẩn doanh nghiệp lớn, cần thêm:

- UAT theo role thật: Admin, Manager, InboundStaff, OutboundStaff, InventoryStaff, TransportStaff, ReportViewer, Viewer.
- Test thiết bị thật: RF scanner, camera, máy in tem, máy in chứng từ, cân điện tử, mạng yếu/offline.
- Test toàn chuỗi nghiệp vụ trên staging disposable: nhập -> QC -> cất hàng -> giữ chỗ -> lấy hàng -> đóng gói -> giao -> đối soát -> chốt tồn -> khóa kỳ.
- Kiểm thử tải và dữ liệu lớn: nhiều kho, nhiều chủ hàng, nhiều vị trí, nhiều serial/LPN, nhiều giao dịch/ngày.
- Kiểm thử phân quyền dữ liệu theo kho/zone/chủ hàng, không chỉ menu.
- Backup/restore drill, giám sát lỗi, cảnh báo SLA, audit immutable, quy trình cấp/thu hồi quyền.
- Tích hợp ERP/TMS/carrier/MHE thật có idempotency, retry, hàng đợi lỗi và đối soát.

Kết luận: phần navigation/dashboard/RBAC/report UI và dữ liệu trọng yếu đã được làm sạch theo hướng WMS nội bộ chuyên nghiệp. Không thể cam kết "0 bug tuyệt đối" nếu chưa có UAT thiết bị thật và staging write-test, nhưng vòng xác minh hiện tại không còn lỗi build, unit test, DB audit hoặc visual Playwright.
