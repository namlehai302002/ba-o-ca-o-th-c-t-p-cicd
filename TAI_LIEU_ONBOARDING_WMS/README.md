# Tài Liệu Onboarding Hệ Thống WMS Pro

> Phiên bản đối chiếu: 18/07/2026  
> Phạm vi: hệ thống quản lý kho nội bộ  
> Nguồn hướng dẫn vận hành trên ứng dụng: menu `Hướng dẫn sử dụng`

Thư mục này dùng để bàn giao cho người mới tham gia dự án hoặc người vận hành mới cần hiểu nhanh WMS Pro. Tài liệu mô tả cách dùng; quyền và nút thực tế vẫn do vai trò, kho, khu vực và chủ hàng được cấp trên hệ thống quyết định.

## Ba Điểm Vào Chuẩn

1. `Hướng dẫn sử dụng` trên ứng dụng: chỉ hiện nội dung và lối vào phù hợp vai trò đang đăng nhập.
2. `../HUONG_DAN_TOAN_BO_NGHIEP_VU_WMS_FULL.md`: sổ tay vận hành tổng hợp và các nguyên tắc kiểm soát.
3. `../HUONG_DAN_THUC_HANH_WMS_CHI_TIET.md`: 25 bài thực hành theo luồng end-to-end, có tiêu chí đạt và checklist cuối ca.

## Nên Đọc Theo Thứ Tự

1. `01_TONG_QUAN_HE_THONG.md` - hiểu hệ thống này làm gì, phục vụ ai và ranh giới trách nhiệm.
2. `02_KIEN_TRUC_VA_DU_LIEU.md` - hiểu kiến trúc ứng dụng, dữ liệu, mã kiện, tồn kho, số sê-ri và sổ giao dịch.
3. `03_NGHIEP_VU_NHAP_XUAT_TON.md` - hiểu luồng nhập kho, xuất kho, tồn kho và các chặn nghiệp vụ quan trọng.
4. `04_VAN_HANH_DIEN_THOAI_VAN_CHUYEN.md` - hiểu quét bằng điện thoại, chuyến xe, vận đơn và bộ kết nối vận tải.
5. `05_PHAN_QUYEN_BAO_MAT.md` - hiểu vai trò, quyền, kho, chủ hàng và nguyên tắc cấp tài khoản.
6. `06_HUONG_DAN_DEV_TEST.md` - hướng dẫn chạy dự án, build, test và kiểm tra trước khi bàn giao.
7. `07_THUAT_NGU.md` - bảng thuật ngữ Việt hóa để đọc tài liệu và giao diện nhất quán.
8. `08_BAN_DO_MAN_HINH_VA_LUONG_DU_LIEU.md` - bản đồ màn hình, luồng dữ liệu nhập/xuất/tồn và vận chuyển.
9. `09_CHECKLIST_BAN_GIAO_VAN_HANH.md` - checklist bàn giao cho vận hành kho và lập trình viên mới.

## Mục Tiêu Của Bộ Tài Liệu

- Giúp người mới hiểu bức tranh tổng thể trong một buổi đọc.
- Giúp quản lý kho hiểu các luồng vận hành chính mà không cần đọc mã nguồn.
- Giúp lập trình viên mới biết nên bắt đầu từ controller, service, model và test nào.
- Giúp cả nhóm dùng cùng một ngôn ngữ nghiệp vụ, tránh hiểu nhầm giữa tồn kho, mã kiện, số sê-ri, vận đơn và chuyến xe.
- Cho phép bàn giao nguyên bộ tài liệu mà không phụ thuộc vào ghi chú miệng.

## Ranh Giới An Toàn Khi Thực Hành

- Chỉ dùng tài khoản và phạm vi kho/chủ hàng được giao.
- Không sửa trực tiếp database để làm cho giao diện “đúng số”.
- Không thử nghiệm bằng chứng từ hoặc tồn kho thật nếu chưa có kế hoạch đảo giao dịch/dọn dữ liệu được duyệt.
- Trên database hosting dùng để demo, không nạp lại dữ liệu mẫu khi còn giao dịch dở dang; không chạy migration, khóa kỳ, điều chỉnh tồn hoặc cleanup trực tiếp.
- Số lượng thực nhận và số sê-ri là hai kiểm soát riêng. Xác nhận đủ số lượng không thay thế việc đăng ký đủ số sê-ri đối với mặt hàng quản lý theo từng chiếc.
- AI chỉ xếp hạng rủi ro và đề xuất kiểm kê. Người có quyền vẫn phải xem bằng chứng và phê duyệt; AI không tự ghi sổ hoặc điều chỉnh tồn.

## Khi Tài Liệu Và Màn Hình Khác Nhau

1. Kiểm tra đúng tài khoản, vai trò, kho và chủ hàng ở góc phải.
2. Mở lại `Hướng dẫn sử dụng` để xem lối vào dành cho vai trò hiện tại.
3. Nếu route có nhưng menu không hiện, không tự mở URL để vượt quyền; báo quản trị viên kiểm tra permission và data scope.
4. Nếu hệ thống báo kỳ khóa, tách nhiệm vụ, thiếu số sê-ri, xung đột vị trí hoặc dữ liệu vừa thay đổi, dừng thao tác và xử lý nguyên nhân; không lặp bấm liên tục.
5. Trước mỗi lần bàn giao hoặc phát hành, chạy checklist kiểm thử trong `06_HUONG_DAN_DEV_TEST.md` và lưu evidence của build hiện tại.

## Trạng Thái Hệ Thống Và Mức Xác Minh

Tài liệu không tự chứng minh hệ thống đã sẵn sàng production. Trạng thái phải lấy từ báo cáo audit mới nhất của đúng build đang bàn giao. Những hạng mục phụ thuộc thiết bị thật, đối tác, pilot kho, triển khai production, dữ liệu AI lịch sử hoặc xử lý dữ liệu legacy phải tiếp tục ghi `BLOCKED`/`PARTIAL` cho đến khi có evidence tương ứng.
