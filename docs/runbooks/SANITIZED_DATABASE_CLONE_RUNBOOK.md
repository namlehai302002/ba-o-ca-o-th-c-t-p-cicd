# Runbook Bản Sao Database Đã Khử Dữ Liệu Nhạy Cảm

## Trạng Thái Hiện Tại

`BLOCKED`: chưa có destination database cô lập, quyền backup/export đã phê duyệt và data-classification sign-off. Không tạo bản sao từ database hosting trong lần đóng Gate 0 này và không tick checklist tương ứng.

## Điều Kiện Bắt Buộc Trước Khi Chạy

1. Nguồn dùng credential chỉ đọc hoặc quyền `COPY_ONLY` do DBA cấp riêng.
2. Destination là database dùng một lần/clone, không phải hosting đang demo hay production.
3. Có danh mục bảng/cột nhạy cảm và owner phê duyệt masking rule.
4. Backup/export tạm được mã hóa, có TTL và access control; không lưu trong repository/artifact.
5. Có correlation ID, audit log, người thực hiện, thời điểm và kế hoạch hủy dữ liệu.

## Nhóm Dữ Liệu Phải Mask Hoặc Thay Tổng Hợp

- Người dùng: email, username ngoài tài khoản test, phone, password hash, MFA/trusted-device/session data.
- Đối tác/tài xế/người liên hệ: tên, địa chỉ, phone, email, biển số nếu được xem là dữ liệu nhạy cảm.
- Integration: API key hash, webhook secret/signature, access token, endpoint nội bộ và payload nhạy cảm.
- Chứng từ/upload/audit: số chứng từ bên ngoài, attachment/OCR text, IP, user-agent và free-text có thể chứa PII.
- Không sao chép Data Protection keys, credential, config hoặc secret table vào clone.

Masking phải deterministic trong phạm vi cần giữ quan hệ, giữ đúng datatype/length/unique constraint nhưng không cho phép khôi phục giá trị gốc. Dữ liệu tồn, quantity, status và ledger chỉ được giữ nếu không chứa PII và vẫn reconcile sau masking.

## Quy Trình Khi Được Phê Duyệt

1. Chụp source fingerprint, schema/migration version và row-count control total bằng query chỉ đọc.
2. Tạo destination từ migration đúng build.
3. Nạp dữ liệu qua pipeline masking; không restore plaintext rồi mới mask nếu destination không có bảo vệ tương đương.
4. Chạy foreign-key/orphan, unique, DQ, inventory/ledger reconciliation và permission-scope test.
5. Quét exact-value sample có kiểm soát để xác nhận PII/secret không còn; chỉ lưu số đếm, không lưu giá trị.
6. Chạy smoke login/read/report bằng tài khoản `AUDIT_TEST_` cô lập.
7. Ghi hash, control total, command exit code, owner sign-off và thời hạn hủy clone.
8. Hủy file trung gian an toàn theo chính sách môi trường.

## Điều Kiện PASS

- Clone nằm ngoài hosting demo/production và không có secret/PII gốc.
- Schema tương thích build; control total và tất cả DQ/reconciliation pass.
- Có owner sign-off và evidence đã redact.
- Có cleanup/TTL đã kiểm chứng.

Chỉ sau khi đủ toàn bộ điều kiện trên mới được đổi mục Gate 0 về `[x]`.
