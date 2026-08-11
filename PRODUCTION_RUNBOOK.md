# Production Runbook

## Backup And Restore

- Tạo backup DB trước mỗi release.
- Lưu bản package đã build, migration script và manifest.
- Kiểm thử restore trên môi trường tách biệt trước khi tuyên bố pass production.

## Disaster Recovery

- Xác định RPO/RTO.
- Chuẩn bị quy trình chuyển dịch vụ, restore DB và kiểm tra đăng nhập.
- DR/HA thật cần bằng chứng môi trường server thật.

## Monitoring

- Theo dõi `/health`, latency, error rate, queue depth, outbox, OCR provider và integration callback.
- Log không được chứa secret value.

## Incident Response

- Ghi nhận thời gian, người xử lý, tác động nghiệp vụ.
- Tạm khóa thao tác nguy hiểm khi có rủi ro sai tồn kho.
- Dùng rollback notes và backup đã kiểm thử.

## Release Checklist

- Build pass.
- Test pass.
- Visual regression pass.
- Migration script reviewed.
- Config hash captured.
- Security scope scan reviewed.

## Operational Notes

- Không in connection strings trong log/report.
- Không tự seed/reset dữ liệu production nếu không có change ticket.
