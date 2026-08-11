/*
  WMS Pro - Seed demo kho vật tư y tế
  Đường nạp chính trong ứng dụng: Hệ thống -> Demo dữ liệu -> Demo kho vật tư y tế.
  File này là artifact SQL tham chiếu cho đội vận hành khi cần đối chiếu bộ dữ liệu.

  Chính sách an toàn:
  - Không xóa tài khoản, mật khẩu/hash, vai trò, phân quyền, cấu hình xác thực, migration hoặc schema.
  - Cleanup dữ liệu vận hành phải chạy trong transaction và giữ lại kho/vùng/đối tác đang được scope đăng nhập tham chiếu.
*/

BEGIN TRANSACTION;

DECLARE @DemoDomain nvarchar(50) = N'Kho vật tư y tế';

SELECT @DemoDomain AS DemoDomain,
       N'DEMO-MED-KHO' AS WarehouseCode,
       N'Kho vật tư y tế' AS WarehouseName,
       N'Bác sĩ Nguyễn Thảo Vy' AS WarehouseManager;

SELECT *
FROM (VALUES
    (N'DEMO-MED-MASK-4L', N'Khẩu trang y tế 4 lớp', N'Hộp', 180, N'Quản lý lô và hạn dùng'),
    (N'DEMO-MED-GLOVE-NIT-M', N'Găng tay nitrile size M', N'Hộp', 95, N'Quản lý lô và hạn dùng'),
    (N'DEMO-MED-TEST-COVID', N'Bộ test nhanh kháng nguyên', N'Bộ', 240, N'FEFO và cảnh báo hạn dùng'),
    (N'DEMO-MED-SANITIZER-500', N'Nước sát khuẩn tay 500ml', N'Chai', 72, N'Quản lý lô'),
    (N'DEMO-MED-BANDAGE-ROLL', N'Bông băng cuộn vô trùng', N'Gói', 140, N'Vật tư tiêu hao'),
    (N'DEMO-MED-SYRINGE-5ML', N'Kim tiêm 5ml vô trùng', N'Hộp', 60, N'Quản lý lô'),
    (N'DEMO-MED-PARA-500', N'Paracetamol 500mg', N'Vỉ', 320, N'Quản lý hạn dùng')
) AS Items(ItemCode, ItemName, UomName, Quantity, Scenario);

SELECT *
FROM (VALUES
    (N'DEMO-MED-PN-202606-001', N'Nhập kho', N'PO-MED-202606-019', N'Công ty Dược An Phát'),
    (N'DEMO-MED-PX-202606-001', N'Xuất kho', N'REQ-ER-202606-011', N'Khoa Cấp cứu')
) AS Vouchers(VoucherCode, VoucherType, ReferenceNo, PartnerName);

ROLLBACK TRANSACTION;
