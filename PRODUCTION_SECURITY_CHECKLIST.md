# Production Security Checklist

## Secrets And Configuration

- [x] Tên cấu hình nhạy cảm được phân loại.
- [ ] Secret rotation cần bằng chứng từ môi trường thật.

## Authentication

- [x] Cookie auth, lockout/MFA/password reset có test.
- [ ] External identity provider cần artifact thật nếu triển khai.

## Authorization And Scope

- [x] Role, warehouse scope, owner scope và export/download scope có matrix.
- [ ] UAT phân quyền với người dùng thật cần ký nhận.

## CSRF

- [x] Global anti-forgery convention bật cho MVC form.

## Audit Trail

- [x] Duyệt, hủy, post tồn, export, API và exception có audit/log.
- [ ] Retention policy production cần xác nhận pháp lý/vận hành.

## Data Protection

- [x] Không đưa secret vào report.
- [ ] Key ring hosting thật cần backup và quyền truy cập riêng.

## Security Headers

- [x] Header cơ bản được cấu hình trong Program.
- [ ] Pentest production cần chạy độc lập.
