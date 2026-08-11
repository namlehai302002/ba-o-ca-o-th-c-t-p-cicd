# Hợp Đồng Bảo Mật Xác Thực Và Phiên

Ngày xác minh: 13/07/2026  
Runtime: cookie authentication trong `Program.cs`, `AccountController` và `ActiveUserCookieAuthenticationEvents`.

## Mật Khẩu Và Đăng Nhập

- Mật khẩu chỉ lưu bằng BCrypt; hash lỗi không có fallback plaintext.
- Login giới hạn 10 request/phút/IP và khóa 15 phút sau 5 lần sai.
- Thời điểm khóa được lưu/so sánh bằng UTC.
- Phản hồi cho tài khoản không tồn tại, sai mật khẩu và đang khóa dùng cùng thông báo; nguyên nhân chi tiết chỉ nằm trong audit nội bộ.
- Public registration mặc định tắt. First-admin bootstrap và reset dành cho development có guard môi trường/cấu hình riêng.

## Cookie Và Thu Hồi Phiên

- Cookie `HttpOnly`, `SameSite=Lax`; production dùng `Secure=Always`, development bám theo HTTPS của request.
- Hạn phiên 8 giờ, sliding expiration; logout ghi audit và xóa cookie đăng nhập cùng cookie thiết bị tin cậy.
- Mỗi request có cookie đều đọc lại trạng thái account. Tài khoản không còn tồn tại, bị vô hiệu hóa, đang khóa hoặc có watermark thu hồi mới hơn thời điểm phát hành cookie sẽ bị từ chối ngay.
- Đổi mật khẩu bởi Admin, xử lý yêu cầu truy cập, development reset và khóa người dùng đều cập nhật watermark thu hồi UTC.

## MFA Và Reset

- Admin/Manager dùng thử thách captcha 6 chữ số khi không có thiết bị tin cậy hợp lệ.
- Challenge hết hạn sau 5 phút, tối đa 5 lần thử, hash được so sánh constant-time và chỉ dùng một lần.
- Hệ thống không có public password-reset token qua email. Reset hiện tại là quy trình Admin có audit; vì vậy contract expiry/single-use của public reset token là `N/A`, không được suy diễn là tính năng đang có.
- Reset mật khẩu làm mất hiệu lực cookie cũ và thiết bị tin cậy thông qua watermark.

## Lỗi Và Dữ Liệu Nhạy Cảm

- Global exception handler chỉ trả lỗi nghiệp vụ an toàn hoặc thông báo chung kèm trace ID.
- Developer exception page mặc định tắt; chỉ bật khi chủ động đặt `Diagnostics:ExposeDeveloperExceptionPage=true` trong môi trường Development.
- Message có hình dạng connection string, API key, secret hoặc đường dẫn nội bộ bị `UserSafeError` che trước khi trả client.

## Evidence

- `artifacts/full-audit/test-results/gate2-auth-session-targeted-20260713.trx`: 21/21 pass.
- `artifacts/full-audit/test-results/gate2-web-file-security-targeted-20260713.trx`: 43/43 pass.
- `WMS.Tests/Gate2SecurityContractTests.cs` và `WMS.Tests/LoginHelpRequestTests.cs`.
