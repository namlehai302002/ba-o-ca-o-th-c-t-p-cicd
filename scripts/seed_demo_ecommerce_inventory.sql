/*
  WMS Pro - Seed demo kho thương mại điện tử
  Đường nạp chính trong ứng dụng: Hệ thống -> Demo dữ liệu -> Demo kho thương mại điện tử.
  File này là artifact SQL tham chiếu cho đội vận hành khi cần đối chiếu bộ dữ liệu.

  Chính sách an toàn:
  - Không xóa tài khoản, mật khẩu/hash, vai trò, phân quyền, cấu hình xác thực, migration hoặc schema.
  - Cleanup dữ liệu vận hành phải chạy trong transaction và giữ lại kho/vùng/đối tác đang được scope đăng nhập tham chiếu.
*/

BEGIN TRANSACTION;

DECLARE @DemoDomain nvarchar(50) = N'Kho thương mại điện tử';

SELECT @DemoDomain AS DemoDomain,
       N'DEMO-ECOM-KHO' AS WarehouseCode,
       N'Kho thương mại điện tử' AS WarehouseName,
       N'Lê Gia Hân' AS WarehouseManager;

SELECT *
FROM (VALUES
    (N'DEMO-ECOM-HEAD-BT-A9', N'Tai nghe Bluetooth AirBeat A9', N'Cái', 120, N'Reservation và picking'),
    (N'DEMO-ECOM-CHG-65W-GAN', N'Sạc nhanh GaN 65W', N'Cái', 85, N'Reservation và picking'),
    (N'DEMO-ECOM-CABLE-C2C-1M', N'Cáp Type-C to Type-C 1m', N'Cái', 240, N'Bán lẻ tốc độ cao'),
    (N'DEMO-ECOM-CASE-IP15', N'Ốp lưng iPhone 15 trong suốt', N'Cái', 180, N'Wave picking'),
    (N'DEMO-ECOM-MOUSE-G102', N'Chuột gaming Logitech G102', N'Cái', 64, N'Serial theo đợt nhập'),
    (N'DEMO-ECOM-KBD-MECH-K2', N'Bàn phím cơ Keychron K2', N'Cái', 38, N'Hàng giá trị cao'),
    (N'DEMO-ECOM-STAND-LAP-ALU', N'Giá đỡ laptop nhôm gấp gọn', N'Cái', 95, N'Đóng gói nhanh')
) AS Items(ItemCode, ItemName, UomName, Quantity, Scenario);

SELECT *
FROM (VALUES
    (N'DEMO-ECOM-PN-202606-001', N'Nhập kho', N'PO-ECOM-202606-088', N'Nhà phân phối DigiHub Việt Nam'),
    (N'DEMO-ECOM-PN-202606-002', N'Nhập kho', N'PO-ECOM-202606-089', N'GearZone Distribution'),
    (N'DEMO-ECOM-PN-202606-003', N'Nhập kho chờ nhận', N'PO-ECOM-202606-090', N'GearZone Distribution'),
    (N'DEMO-ECOM-PX-202606-001', N'Xuất kho', N'SO-ECOM-HCM-240601', N'Khách lẻ kênh Online - Quận 1'),
    (N'DEMO-ECOM-PX-202606-002', N'Xuất kho', N'SO-ECOM-DN-240602', N'Khách sỉ phụ kiện Đà Nẵng'),
    (N'DEMO-ECOM-PX-202606-003', N'Xuất kho', N'SO-ECOM-HCM-240603', N'Khách lẻ kênh Online - Quận 1')
) AS Vouchers(VoucherCode, VoucherType, ReferenceNo, PartnerName);

SELECT *
FROM (VALUES
    (N'DEMO-ECOM-MOUSE-G102', 64, N'Truy xuất serial theo chuột gaming còn tồn'),
    (N'DEMO-ECOM-KBD-MECH-K2', 38, N'Truy xuất serial theo bàn phím cơ còn tồn')
) AS SerialPolicy(ItemCode, SerialCount, Notes);

ROLLBACK TRANSACTION;
