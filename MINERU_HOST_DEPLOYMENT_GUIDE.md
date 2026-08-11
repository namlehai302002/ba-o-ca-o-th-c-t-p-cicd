# MinerU Host Deployment Guide

## InterData Shared Hosting Boundary

Gói **Hosting Windows Sinh Viên** dạng **shared ASP.NET** không nên chạy MinerU chung trong cùng hosting vì MinerU cần Python, runtime OCR nặng, tiến trình nền dài và có thể cần Docker hoặc GPU/CPU riêng.

## Recommended Deployment

- Chạy WMS trên shared ASP.NET.
- Chạy MinerU trên VPS riêng nếu cần OCR PDF nặng.
- VPS nên có Python runtime, process supervisor và giới hạn tài nguyên.
- Docker chỉ nên dùng khi gói hạ tầng cho phép container.

## Safe Configuration

Nếu chưa có MinerU service riêng, dùng cấu hình:

```json
{
  "MinerU": {
    "Enabled": false
  }
}
```

Không cần xóa các giá trị hiện có trong `appsettings.json`; chỉ đổi bằng biến môi trường hoặc cấu hình hosting khi thật sự triển khai.

## Fallback

Ảnh PNG/JPG có thể dùng provider đọc ảnh nhẹ hơn. PDF cần kiểm tra provider phù hợp và timeout thân thiện.
