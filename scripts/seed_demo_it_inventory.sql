/*
  WMS Pro - Seed demo kho thiết bị IT
  Đường nạp chính trong ứng dụng: Hệ thống -> Demo dữ liệu -> Demo kho thiết bị IT.
  File này là artifact SQL tham chiếu cho đội vận hành khi cần đối chiếu bộ dữ liệu.

  Chính sách an toàn:
  - Không xóa tài khoản, mật khẩu/hash, vai trò, phân quyền, cấu hình xác thực, migration hoặc schema.
  - Cleanup dữ liệu vận hành phải chạy trong transaction và giữ lại kho/vùng/đối tác đang được scope đăng nhập tham chiếu.
*/

BEGIN TRANSACTION;

DECLARE @DemoDomain nvarchar(50) = N'Kho thiết bị IT';

SELECT @DemoDomain AS DemoDomain,
       N'DEMO-IT-KHO' AS WarehouseCode,
       N'Kho thiết bị IT' AS WarehouseName,
       N'Trần Minh Khôi' AS WarehouseManager;

SELECT *
FROM (VALUES
    (N'DEMO-IT-LAP-DELL-5420', N'Laptop Dell Latitude 5420 i5/16GB/512GB', N'Chiếc', 10, N'Serial thiết bị'),
    (N'DEMO-IT-LAP-HP-440G9', N'Laptop HP ProBook 440 G9 i5/8GB/256GB', N'Chiếc', 6, N'Serial thiết bị'),
    (N'DEMO-IT-PROJ-EPSON-X49', N'Máy chiếu Epson EB-X49', N'Chiếc', 4, N'Serial thiết bị'),
    (N'DEMO-IT-RT-TPLINK-AX55', N'Router TP-Link Archer AX55', N'Chiếc', 12, N'Thiết bị mạng'),
    (N'DEMO-IT-SW-CISCO-24P', N'Switch Cisco CBS250 24-Port', N'Chiếc', 5, N'Thiết bị mạng'),
    (N'DEMO-IT-MOUSE-M185', N'Chuột Logitech M185 Wireless', N'Cái', 49, N'Phụ kiện IT'),
    (N'DEMO-IT-KBD-DELL-KB216', N'Bàn phím Dell KB216', N'Cái', 30, N'Phụ kiện IT'),
    (N'DEMO-IT-MON-SAMSUNG-24', N'Màn hình Samsung 24 inch IPS', N'Chiếc', 16, N'Phụ kiện IT')
) AS Items(ItemCode, ItemName, UomName, Quantity, Scenario);

SELECT *
FROM (VALUES
    (N'DEMO-IT-PN-202606-001', N'Nhập kho', N'PO-IT-202606-042', N'Công ty TNHH Dell Technologies Việt Nam'),
    (N'DEMO-IT-PN-202606-002', N'Nhập kho', N'PO-IT-202606-043', N'Epson Việt Nam - Thiết bị trình chiếu'),
    (N'DEMO-IT-PN-202606-003', N'Nhập kho', N'PO-IT-202606-044', N'Nhà phân phối thiết bị mạng An Phát'),
    (N'DEMO-IT-PX-202606-001', N'Xuất kho', N'REQ-LAB-202606-018', N'Phòng Lab Công nghệ')
) AS Vouchers(VoucherCode, VoucherType, ReferenceNo, PartnerName);

SELECT *
FROM (VALUES
    (N'DEMO-IT-LAP-DELL-5420', 10, N'Truy xuất serial theo từng laptop; 1 serial đang QC'),
    (N'DEMO-IT-LAP-HP-440G9', 6, N'Truy xuất serial theo từng laptop'),
    (N'DEMO-IT-PROJ-EPSON-X49', 4, N'Truy xuất serial theo từng máy chiếu'),
    (N'DEMO-IT-RT-TPLINK-AX55', 12, N'Truy xuất serial theo thiết bị mạng'),
    (N'DEMO-IT-SW-CISCO-24P', 5, N'Truy xuất serial theo thiết bị mạng'),
    (N'DEMO-IT-MON-SAMSUNG-24', 16, N'Truy xuất serial theo màn hình')
) AS SerialPolicy(ItemCode, SerialCount, Notes);

ROLLBACK TRANSACTION;
