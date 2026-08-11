# Production Migration Validation

## Dry Run

- Chạy migration script trên bản sao DB.
- Kiểm tra số lượng bảng chính trước/sau.
- Không chạy migration trực tiếp trên production nếu chưa có backup.

## Rollback Plan

- Lưu DB backup.
- Lưu package trước release.
- Có lệnh rollback hoặc restore đã thử trên môi trường tách biệt.

## Seed And Drift Validation

- Seed demo không chạy tự động ở production.
- Kiểm tra drift schema bằng script đọc-only.
- Không xóa user, role, permission, password hash hoặc scope khi làm demo data.

## Idempotent Script

```powershell
dotnet ef migrations script --idempotent
```

## Acceptance

- Build pass.
- Test pass.
- Migration dry run pass.
- Rollback drill có bằng chứng.
