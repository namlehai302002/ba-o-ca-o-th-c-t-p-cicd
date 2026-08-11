# WMS World-Class Gap Assessment - 2026-06-29

## Ket luan ngan

Trong pham vi repo/local da kiem chung, WMS Pro dang o muc khoang **95-96/100**. So voi cac WMS Tier-1 dang chay production that nhu Oracle WMS Cloud, Microsoft Dynamics 365 Supply Chain Management, SAP EWM va cac suite tuong duong, muc tuong duong production thuc te van nen ghi nhan **88-91%** cho den khi co bang chung thiet bi, tai, DR/HA, pentest, hosting va tich hop certified.

Khong nen tuyen bo "100% world-class production" neu chua co evidence ngoai repo. Muc 100% chi hop ly khi ca code, UI, nghiep vu, bao mat, thiet bi that va van hanh that deu duoc ky nhan.

## Nguon benchmark chinh thuc da doi chieu

- Oracle WMS Cloud 26B - Companies and Facilities: https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owsec/companies-and-facilities.html
- Oracle WMS Cloud 26B - Access Control Lists/Functional Security: https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owsec/access-control-lists-functional-security.html
- Microsoft Dynamics 365 Supply Chain - Warehouse management overview: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview
- SAP Extended Warehouse Management official documentation: https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT

## Bang diem hien tai

| Nhom | Diem repo/local | Diem production-equivalent | Ghi chu |
|---|---:|---:|---|
| Inbound/outbound/inventory core | 96 | 91 | Co regression sau cho voucher, receiving, picking, shipping, ledger, LPN, lot/serial. |
| Owner/warehouse scope | 96 | 92 | Da harden slotting va API owner scope. Van can pentest va staging tenant evidence. |
| UI/UX | 96 | 90 | Visual auth/main/no-device/mobile-deep da xanh; van can UAT tren device that. |
| Integration/API/EDI/webhook | 93 | 86 | Co API/EDI/webhook scaffold va scope guard; can certification voi ERP/TMS/OMS/MHE/carrier that. |
| Automation/optimization | 92 | 86 | Co slotting, replenishment, capacity, dashboard; can load/concurrency va thiet bi/MHE evidence. |
| Security/config | 93 | 86 | Co role/scope/API key/static guards; can secret store, rotation, WAF, pentest. |
| Performance/DR/HA | 82 | 72 | Chua co k6/soak/backup-restore/HA evidence trong moi truong that. |
| Tong the | 95-96 | 88-91 | Manh cho internal WMS local; chua du bang chung de goi la Tier-1 production 100%. |

## Gap da dong trong dot tiep tuc nay

### API owner scope khong duoc lo hoac dung item noi bo

Rui ro: Khi API key bi cau hinh `Api:ScopedOwnerPartnerId`, endpoint `/api/v1/items` va KPI truoc day van co the dua item master `OwnerPartnerId = null` vao ket qua. API tao voucher cung co the nhan item noi bo cho phieu cua owner do dieu kien cu chi chan item thuoc owner khac. Voi WMS 3PL/enterprise, partner-scoped API phai mac dinh chi thay va chi ghi dung owner duoc cap, tranh lo/dung nham SKU noi bo.

Da sua:
- `Controllers/ApiIntegrationController.cs`: query item va active KPI item khi co owner scope gio chi lay `OwnerPartnerId == scopedOwner`; API tao voucher cung reject line item neu item owner khong dung owner cua phieu/API.
- `WMS.Tests/ApiIntegrationScopeHardeningTests.cs`: fixture them internal item `OwnerPartnerId = null`; regression xac nhan API owner 10 chi thay `ITEM-SCOPE`, KPI active item = 1, total stock = 5, reject voucher dung item noi bo va van accept voucher dung item owner 10.
- `Controllers/ApiIntegrationController.cs`: API webhook replay direct-id gio phai doc duoc `warehouseId` va `ownerPartnerId` tu payload va khop API scope; payload ngoai scope hoac thieu scope bi 403.
- `WMS.Tests/ApiIntegrationScopeHardeningTests.cs`: regression xac nhan replay webhook ngoai scope/thieu scope bi chan, payload dung owner/kho duoc replay.
- `Controllers/ApiIntegrationController.cs`: webhook replay scope gio chi yeu cau nhung field scope da cau hinh; API chi scope theo kho khong bi bat buoc co `ownerPartnerId`, nhung payload sai kho van bi 403.
- `WMS.Tests/ApiIntegrationScopeHardeningTests.cs`: regression xac nhan warehouse-only scoped API replay duoc payload dung kho du khong co owner, va chan payload sai kho.
- `Controllers/ApiIntegrationController.cs`: MHE callback va carrier callback gio normalize/trim `CorrelationId` roi pre-check theo warehouse/owner scope truoc khi goi service mutate; callback ngoai scope hoac bypass bang whitespace bi 403 va khong tao event.
- `WMS.Tests/ApiIntegrationScopeHardeningTests.cs`: regression xac nhan MHE/carrier callback ngoai scope voi `CorrelationId` co khoang trang khong doi status va khong ghi event.
- `Controllers/ApiIntegrationController.cs`: `POST /api/v1/vouchers` gio dung `X-Idempotency-Key` qua `IntegrationIdempotencyKeys`, tranh tao trung voucher khi ERP/TMS retry cung request sau timeout.
- `WMS.Tests/ApiIntegrationScopeHardeningTests.cs`: regression xac nhan retry API tao voucher voi cung idempotency key tra lai dung voucher cu va DB chi co mot voucher.
- `Controllers/ApiIntegrationController.cs`: idempotency key cua API tao voucher gio duoc reserve bang unique index truoc khi mutate, request song song cung key chi co mot request duoc tao voucher; request con lai nhan cached response hoac 409 dang xu ly.
- `WMS.Tests/ApiIntegrationScopeHardeningTests.cs`: regression SQLite shared in-memory xac nhan concurrent retry cung `X-Idempotency-Key` chi tao 1 voucher/1 line/1 idempotency key.

Evidence:
- `dotnet test WMS.Tests/WMS.Tests.csproj --filter "FullyQualifiedName~ApiIntegrationScopeHardeningTests" --logger "console;verbosity=minimal"`: pass `8/8`.
- `dotnet build WMS.sln --no-restore -v:minimal`: pass `0 warning / 0 error`.
- `dotnet test WMS.Tests\WMS.Tests.csproj --no-restore --logger "console;verbosity=minimal"`: pass `675/675`.
- `appsettings.json` SHA-256 khong doi: `7A3E4A74C0D7D7CBA0AF5EB91A65B06764CCDF38B79798D9C0063188A3C4A1EC`.

## Viec can them de tien toi 100% dung nghia

1. Chay load/soak/performance tren staging gan production, gom p50/p95/p99, throughput, lock contention va DB wait stats.
2. Lam UAT co bien ban ky voi RF scanner, mobile handheld, camera scan, label printer va can dien tu that.
3. Chung minh backup/restore, rollback, DR/HA va RPO/RTO bang log drill that.
4. Dua secret/API key/connection string ra secret store, rotate key va luu evidence da redact.
5. Pentest role/scope/export/API, dac biet tenant/owner isolation va IDOR direct-id endpoints.
6. Chung nhan integration voi ERP/TMS/OMS/MHE/carrier that, gom retry, dead-letter, replay va idempotency.
7. Bo sung performance baseline cho report lon, inventory ledger, voucher nhieu dong, wave/pick task dong thoi.
8. Tiep tuc tach controller lon thanh service/query object de giam rui ro maintainability dai han.

## Ranh gioi 100%

Repo/local co the dat rat cao khi build/test/visual/schema/package deu xanh. Production 100% chi co the ket luan sau khi evidence thiet bi, tai, bao mat, hosting va van hanh that cung xanh. Bat ky bao cao nao ghi 100% khi chua co cac evidence nay deu la qua tay va khong phu hop chuan enterprise.
