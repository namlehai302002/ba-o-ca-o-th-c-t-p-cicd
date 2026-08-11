# WMS Pro

> Hệ thống quản lý kho doanh nghiệp cho vận hành nội bộ, hàng khách hàng thuê kho, RF/mobile execution và kiểm soát tồn kho đầu-cuối.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-MVC-5C2D91?style=flat-square)](https://learn.microsoft.com/aspnet/core)
[![EF Core](https://img.shields.io/badge/EF%20Core-SQL%20Server-CC2927?style=flat-square)](https://learn.microsoft.com/ef/core)
[![Playwright](https://img.shields.io/badge/Playwright-Visual%20Regression-2EAD33?style=flat-square&logo=playwright)](https://playwright.dev/)
[![WMS](https://img.shields.io/badge/WMS-Enterprise%20Operations-0F3A6F?style=flat-square)](#wms-pro)

WMS Pro là nền tảng Warehouse Management System được xây dựng cho các kịch bản vận hành nghiêm túc: nhập kho, xuất kho, chuyển kho, điều chỉnh tồn, trả hàng, giữ chỗ tồn, lấy hàng, đóng gói, bàn giao vận chuyển, quản lý lô, hạn dùng, số sê-ri, mã kiện, đơn vị tính, cân trọng lượng thực tế và tồn kho nhiều chủ sở hữu.

README này được viết cho GitHub: dễ đọc, đủ thông tin kỹ thuật, không chứa secret và không thay thế tài liệu vận hành chi tiết trong hệ thống.

## Mục Lục

- [Tổng Quan](#tổng-quan)
- [Điểm Nổi Bật](#điểm-nổi-bật)
- [Module Nghiệp Vụ](#module-nghiệp-vụ)
- [Kiến Trúc Hệ Thống](#kiến-trúc-hệ-thống)
- [Stack Kỹ Thuật](#stack-kỹ-thuật)
- [Phân Quyền Vai Trò](#phân-quyền-vai-trò)
- [Quick Start Local](#quick-start-local)
- [Verification Gates](#verification-gates)
- [Production Readiness](#production-readiness)
- [Tài Liệu Liên Quan](#tài-liệu-liên-quan)
- [Troubleshooting](#troubleshooting)

## Tổng Quan

WMS Pro tập trung vào ba lớp năng lực chính:

- **Operational control**: phiếu kho, tác vụ kho, tồn kho, nhận hàng, lấy hàng, chuyển vị trí, kiểm kê, đóng gói và giao hàng.
- **Inventory integrity**: ledger, stock posting, reservation, owner scope, warehouse scope, UOM conversion, lot/expiry/serial/LPN và cancellation rule.
- **Enterprise readiness**: phân quyền theo vai trò, audit trail, health check, visual regression, evidence gate, production packaging và tài liệu vận hành.

Hệ thống được thiết kế theo hướng ưu tiên vận hành nội bộ: hàng nội bộ không bị ép gán chủ hàng, nhưng vẫn hỗ trợ hàng khách hàng thuê kho khi cần tách phạm vi sở hữu.

## Điểm Nổi Bật

- **Inbound**: tạo phiếu nhập, duyệt phiếu, tiếp nhận hàng, QC, directed putaway và ghi sổ nhập.
- **Outbound**: tạo phiếu xuất, reservation, FEFO, pick task, picking, packing, shipping và ghi sổ xuất.
- **Inventory**: tồn theo kho, khu vực, vị trí, vật tư, lô, hạn dùng, số sê-ri, mã kiện và chủ hàng.
- **Transfer & adjustment**: chuyển kho, chuyển vị trí, điều chỉnh tăng/giảm, lý do nghiệp vụ và audit transaction.
- **Return**: khách trả hàng, trả nhà cung cấp, khôi phục hoặc giảm tồn đúng loại chứng từ.
- **RF/mobile**: luồng thao tác nhanh cho nhận hàng, lấy hàng, di chuyển, kiểm tra và bàn giao.
- **UOM & catch weight**: đơn vị cơ sở, quy đổi hợp lệ, số lượng giao dịch và trọng lượng thực tế.
- **Yard & shipping**: lịch xe, dock board, carrier handover, shipment load và đối soát giao hàng.
- **Customer-owned stock**: chủ hàng, hợp đồng, bảng giá, billing scope và owner portal.
- **Security**: role, permission, warehouse scope, owner scope, CSRF, BCrypt và audit log.
- **Integration/SRE**: API/integration guard, webhook/EDI demo, health, telemetry, evidence gate và production package hygiene.

## Module Nghiệp Vụ

| Nhóm | Năng lực chính |
|---|---|
| Master Data | Vật tư/hàng hóa, danh mục, UOM, quy đổi, barcode, đối tác, kho, khu vực, vị trí |
| Inbound | Phiếu nhập, duyệt, tiếp nhận, QC, serial/lot/expiry, putaway, post stock |
| Outbound | Phiếu xuất, reservation, wave/pick task, picking, packing, shipping, post stock |
| Transfer | Chuyển kho/vị trí, kiểm tra scope kho, xuất nguồn và nhập đích |
| Adjustment | Điều chỉnh tồn, lý do nghiệp vụ, audit transaction, chặn âm tồn ngoài rule |
| Return | Khách trả hàng, trả nhà cung cấp, cập nhật tồn đúng loại phiếu |
| Inventory Control | Current stock, item location, hold/quarantine, min/max/reorder point, FEFO |
| Lot / Serial / LPN | Lô, hạn dùng, số sê-ri, mã kiện, cây LPN và truy xuất nguồn gốc |
| UOM / Catch Weight | Đơn vị cơ sở, conversion active, base quantity, transaction quantity, actual weight |
| Yard / Shipping | Dock, yard visit, carrier handover, shipment load, delivery reconciliation |
| Customer-Owned Stock | Chủ hàng, hợp đồng, bảng giá, tính phí, owner scope và billing |
| Labels / Documents | In phiếu, in nhãn, template nhãn, print job và chứng từ giao nhận |
| Reports / Analytics | KPI, tồn kho, biến động, ABC, slow-moving, expiry, labor productivity |
| Security / Admin | Người dùng, vai trò, phân quyền, trusted device, audit analytics |
| Integration / Automation | API, EDI/webhook demo, MHE/carrier connector, automation dashboard |

## Kiến Trúc Hệ Thống

```text
Browser / RF Mobile / Admin UI
          |
ASP.NET Core 8 MVC Controllers
          |
Application Services + Business Rules
          |
EF Core 8 + SQL Server
          |
Inventory Ledger / Audit / Background Jobs
```

Business logic được giữ trong service layer để controller tập trung điều phối request/response. Các nghiệp vụ tồn kho nhạy cảm như reservation, stock posting, UOM validation, cancellation và scope check được khóa bằng regression tests.

## Stack Kỹ Thuật

| Lớp | Công nghệ |
|---|---|
| Backend | ASP.NET Core 8 MVC, C# |
| Data | EF Core 8, SQL Server |
| Frontend | Razor Views, responsive CSS, JavaScript, Select2-style interactions, SweetAlert2 |
| Security | Cookie auth, RBAC, permission policy, anti-forgery, BCrypt |
| Observability | Health checks, OpenTelemetry tracing/metrics, readiness scripts |
| Testing | xUnit, static regression tests, Playwright visual regression |
| Operations | Production package script, evidence gate, runbook/checklist |

## Phân Quyền Vai Trò

| Vai trò | Phạm vi sử dụng |
|---|---|
| **Admin** | Cấu hình hệ thống, người dùng, phân quyền, SRE, bảo mật, audit và integration |
| **Manager** | Duyệt nghiệp vụ, điều phối kho, workflow, yard/shipping, báo cáo và xử lý ngoại lệ |
| **Staff** | Tạo phiếu, nhận hàng, lấy hàng, di chuyển, QC, RF/mobile, in nhãn và bàn giao |
| **Viewer** | Xem dashboard, danh mục, tồn kho, phiếu và báo cáo được cấp quyền |

Ngoài vai trò, hệ thống còn kiểm soát theo **warehouse scope** và **owner scope** để người dùng chỉ đọc/ghi dữ liệu trong phạm vi được cấp.

## Quick Start Local

### Prerequisites

- .NET SDK 8.x
- SQL Server hoặc SQL Server-compatible hosting
- Node.js LTS
- PowerShell
- Playwright browsers nếu chạy visual regression

### 1. Restore dependencies

```powershell
dotnet restore WMS.sln
npm install
npx playwright install chromium
```

### 2. Configure local secrets

Không ghi secret thật, connection string thật, API key thật hoặc mật khẩu thật vào README/GitHub. Dùng placeholder và cấu hình bằng `dotnet user-secrets` hoặc biến môi trường:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<SQL_SERVER_CONNECTION_STRING>"
dotnet user-secrets set "GeminiApiKey" "<OPTIONAL_AI_KEY>"
dotnet user-secrets set "DevResetToken" "<LOCAL_DEV_RESET_TOKEN>"
```

Nếu dùng hosting/shared SQL Server, hãy đặt connection string thật trong secret store hoặc môi trường triển khai phù hợp.

### 3. Run application

```powershell
dotnet run
```

Sau khi app chạy, mở URL được ASP.NET Core in ra console. Nếu hệ thống chưa có tài khoản, vào `/Account/Login` để đi qua luồng thiết lập quản trị viên đầu tiên.

## Verification Gates

### Backend

```powershell
dotnet build WMS.sln --no-restore -v:minimal
dotnet test WMS.Tests\WMS.Tests.csproj --no-restore -v:minimal
dotnet test WMS.Tests\WMS.Tests.csproj --no-restore --logger "console;verbosity=minimal"
```

### Visual regression

Chạy app local hoặc trỏ tới URL test:

```powershell
$env:WMS_BASE_URL="<LOCAL_WMS_BASE_URL>"
npm run visual:public
npm run visual:auth
npm run visual:test
npm run visual:no-device
npm run visual:mobile-deep
```

| Gate | Mục tiêu |
|---|---|
| `visual:public` | Login, access help, public auth screens |
| `visual:auth` | Tạo authenticated storage state |
| `visual:test` | Desktop, mobile, tablet, zoom, layout collision |
| `visual:no-device` | RF scanner, camera modal và print preview không cần thiết bị thật |
| `visual:mobile-deep` | Mobile/tablet deep audit, overflow, console/page error và response 5xx |

## Production Readiness

Repo/local readiness được kiểm bằng build, .NET tests, static scan và visual regression. Để xác nhận production ngoài đời ở mức Tier-1, cần thêm evidence ngoài repo:

- RF scanner, camera, printer và thiết bị thật.
- Load/soak test trên staging hoặc production-like environment.
- DR/HA backup restore evidence.
- Hosting protection evidence: file permission, config isolation, encrypted backup, access control.
- Certified integration evidence với ERP/TMS/OMS/MHE/carrier thực tế.

## Tài Liệu Liên Quan

| Tài liệu | Mục đích |
|---|---|
| [HUONG_DAN_TOAN_BO_NGHIEP_VU_WMS_FULL.md](HUONG_DAN_TOAN_BO_NGHIEP_VU_WMS_FULL.md) | Hướng dẫn tổng thể nghiệp vụ WMS |
| [HUONG_DAN_THUC_HANH_WMS_CHI_TIET.md](HUONG_DAN_THUC_HANH_WMS_CHI_TIET.md) | Hướng dẫn thao tác chi tiết |
| [PRODUCTION_RUNBOOK.md](PRODUCTION_RUNBOOK.md) | Runbook vận hành production |
| [PRODUCTION_SECURITY_CHECKLIST.md](PRODUCTION_SECURITY_CHECKLIST.md) | Checklist bảo mật production |
| [docs/API_INTEGRATION_ENTERPRISE_CONTRACTS.md](docs/API_INTEGRATION_ENTERPRISE_CONTRACTS.md) | Hợp đồng API/integration |
| [docs/EXPORT_DOWNLOAD_API_SCOPE_REGISTRY.md](docs/EXPORT_DOWNLOAD_API_SCOPE_REGISTRY.md) | Registry scope export/download/API read |
| [docs/MOBILE_RF_REAL_DEVICE_CHECKLIST_2026_05_21.md](docs/MOBILE_RF_REAL_DEVICE_CHECKLIST_2026_05_21.md) | Checklist thiết bị RF thật |
| [docs/TIER1_PRODUCTION_EVIDENCE_CHECKLIST_2026_05_29.md](docs/TIER1_PRODUCTION_EVIDENCE_CHECKLIST_2026_05_29.md) | Checklist evidence Tier-1 |

## Troubleshooting

### Visual test báo thiếu `WMS_BASE_URL`

```powershell
$env:WMS_BASE_URL="<LOCAL_WMS_BASE_URL>"
```

### Visual test báo thiếu auth state

```powershell
npm run visual:auth
```

### Build không tìm thấy package

```powershell
dotnet restore WMS.sln
```

### Playwright chưa có browser

```powershell
npx playwright install chromium
```

## Repository Notes

- README không chứa secret, connection string thật, API key thật hoặc mật khẩu thật.
- Không dùng badge CI giả nếu repository chưa có GitHub Actions tương ứng.
- Visual snapshots chỉ nên cập nhật khi thay đổi UI có chủ đích và đã kiểm tra bằng mắt.
- Production 100% ngoài đời phụ thuộc thêm vào thiết bị, load, DR/HA, hosting artifact và integration evidence.

---

**WMS Pro** - Built for warehouse teams that need reliable inventory, clear execution, and controlled growth.
