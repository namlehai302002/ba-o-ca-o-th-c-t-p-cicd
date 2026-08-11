# Bản Đồ Màn Hình Và Luồng Dữ Liệu

## 1. Cách đọc hệ thống theo màn hình

WMS Pro được tổ chức theo nhóm nghiệp vụ thay vì theo cấu trúc mã nguồn. Người mới nên bắt đầu từ trang chính, sau đó đi theo từng nhóm trên menu trái.

| Nhóm | Mục đích | Màn hình tiêu biểu |
| --- | --- | --- |
| Nhập kho | Tạo phiếu, nhận hàng, kiểm phẩm, số sê-ri, mã kiện và cất hàng. | Tiếp nhận hàng, quét nhận hàng bằng điện thoại, kiểm tra chất lượng. |
| Xuất kho | Giữ chỗ, lấy hàng, lấy thiếu, ghi sổ xuất, đóng gói và giao hàng. | Đợt gom đơn, nhiệm vụ lấy hàng, quét lấy hàng bằng điện thoại, đóng gói và giao. |
| Tồn kho | Xem tồn, mã kiện, số sê-ri, kiểm kê, di chuyển và lịch sử nhập xuất. | Xem tồn kho, sơ đồ kho, tra cứu mã kiện, tra cứu số sê-ri, kiểm kê, kiểm kê thông minh, điều chỉnh tồn kho, nhiệm vụ di chuyển. |
| Vận chuyển | Điều phối vận đơn, chuyến xe, chứng từ bàn giao và đối soát giao hàng. | Điều phối vận chuyển, bảng chuyến xe, nhãn và chứng từ, đối soát giao hàng. |
| Báo cáo | Theo dõi hiệu suất, nhập/xuất, tồn, vận chuyển, chi phí và chất lượng dữ liệu. | Tổng quan kho, chỉ số vận hành, thống kê nhập/xuất, báo cáo tồn kho, báo cáo vận hành vận chuyển, báo cáo chi phí, bất thường dữ liệu. |
| Danh mục | Quản lý dữ liệu nền. | Đối tác, danh mục vật tư, đơn vị tính, khu vực kho, vị trí/kệ/khu chứa. |
| Hệ thống | Quản lý người dùng, phân quyền, nhật ký và khóa kỳ. | Người dùng, phân quyền khu vực, cảnh báo, nhật ký, thiết bị tin cậy. |

## 2. Luồng dữ liệu nhập kho

1. Phiếu nhập được tạo với kho, đối tác và dòng vật tư.
2. Nhân viên nhận hàng xác nhận số lượng thực tế.
3. Hệ thống kiểm tra lô, hạn dùng, số sê-ri, cân trọng lượng thực tế và vị trí cất.
4. Nếu hợp lệ, hệ thống tạo hoặc cập nhật mã kiện, chi tiết mã kiện, tồn vị trí và sổ giao dịch tồn kho.
5. Nếu có lỗi, trung tâm ngoại lệ ghi nhận để người vận hành xử lý.

## 3. Luồng dữ liệu xuất kho

1. Phiếu xuất được tạo và phát hành.
2. Hệ thống giữ chỗ tồn đúng kho, đúng chủ hàng, đúng lô và đúng hạn dùng.
3. Nhân viên lấy hàng bằng điện thoại, quét vật tư, vị trí, mã kiện và số sê-ri khi cần.
4. Phiếu được ghi sổ xuất, sau đó đóng gói thành kiện xuất.
5. Vận đơn hoặc chuyến xe được gắn vào kiện xuất.
6. Khi giao hàng, hệ thống kiểm tra kiện, cân thực tế, mã vận đơn, mã chuyến và nhật ký bàn giao.

## 4. Luồng dữ liệu vận chuyển

1. Kiện xuất là đơn vị chính để tạo vận đơn.
2. Bộ kết nối vận tải nhận yêu cầu tạo vận đơn qua hàng đợi tích hợp.
3. Chuyến xe gom nhiều phiếu hoặc nhiều kiện để bàn giao.
4. Nhãn vận chuyển, bản kê chuyến xe và biên bản bàn giao được in từ dữ liệu kiện, vận đơn và chuyến xe.
5. Báo cáo đối soát phát hiện thiếu vận đơn, thiếu scan lên chuyến, lệch chuyến hoặc hãng báo giao thất bại.

## 5. Luồng dữ liệu kiểm toán

- Tồn vị trí là ảnh chụp vận hành để truy vấn nhanh.
- Mã kiện là mô hình container dùng cho di chuyển nguyên kiện.
- Sổ giao dịch tồn kho là lớp kiểm toán số lượng và giữ lịch sử sau thời điểm cutover.
- Nhật ký hệ thống và trung tâm ngoại lệ dùng để theo dõi thao tác, cảnh báo và xử lý vận hành.

## 6. Luồng kiểm kê thông minh

1. Hệ thống tổng hợp tín hiệu giao dịch, tồn, giữ chỗ, lịch sử sai lệch và độ mới dữ liệu theo phạm vi người dùng.
2. Mô hình xếp hạng rủi ro và nêu các yếu tố ảnh hưởng; kết quả có thời điểm tính và phiên bản.
3. Nhân viên tồn kho hoặc quản lý mở chi tiết bằng chứng, chọn đề xuất phù hợp và tạo công việc kiểm kê.
4. Nhân viên thực hiện đếm mù, nhập số đếm và gửi duyệt chênh lệch.
5. Chỉ workflow điều chỉnh đã được phê duyệt mới làm đổi tồn. AI không trực tiếp ghi sổ.

