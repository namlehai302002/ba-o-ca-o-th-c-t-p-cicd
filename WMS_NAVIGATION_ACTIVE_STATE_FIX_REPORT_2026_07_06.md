# Bao cao fix navigation active-state va sidebar flyout - 2026-07-06

## 1. Da ra soat nhung file nao

- `Views/Shared/_SidebarNav.cshtml`
- `Views/Shared/_Layout.cshtml`
- `Views/Reports/StockMovement.cshtml`
- `Views/Warehouses/InventoryMap.cshtml`
- `wwwroot/css/site.css`
- `WMS.Tests/EnterpriseUiRedesignTests.cs`
- `WMS.Tests/EnterpriseUiUxPolishTests.cs`
- `tests/visual/wms-visual-regression.spec.ts` va `tests/visual/wms-mobile-deep.spec.ts` qua Playwright
- Database SQL Server theo connection hien tai cua app

## 2. Menu cu dang co van de gi

- Cung mot route `Reports/StockMovement` duoc dung cho ca `Lich su nhap kho` va `Lich su nhap xuat`, nen sidebar active nham hai ngu canh.
- Route `Items/Index?stockStatus=low` duoc xem nhu `Items/Index`, nen `Hang sap thieu` lam sang nham `Danh muc vat tu`.
- Route `Warehouses/InventoryMap` duoc dung cho ca `So do kho` va `Vi tri/ke/khu chua`, nen hai nhom menu bi active cheo.
- Flyout cua sidebar thu gon bi rule `max-height: none !important` cu override, lam nhom `He thong` dai va kho cuon toi muc cuoi.
- Khi sidebar thu gon, noi dung trang co the bi cam giac bi topbar de neu scroll/doi trang thai nhanh.

## 3. Menu moi da duoc to chuc lai ra sao

- Giu nguyen route va component cu de khong lam vo man hinh hien co.
- Them ngu canh query:
  - `nav=inbound` cho `Lich su nhap kho`.
  - `nav=inventory` cho `Lich su nhap xuat`.
  - `map=inventory` cho `So do kho`.
  - `map=master` cho `Vi tri/ke/khu chua`.
  - `stockStatus=low` tiep tuc danh dau rieng `Hang sap thieu`.
- Sidebar active dua tren ngu canh route/query thay vi chi dua tren controller/action.

## 4. Nhung chuc nang nao duoc di chuyen sang nhom khac

- Khong di chuyen route/component trong dot fix nay.
- Chi tach ngu canh dieu huong de cac muc dang dung chung man hinh khong bi sang nham.

## 5. Dashboard da sua gi

- Dot fix nay khong doi dashboard.
- Da xu ly bug sidebar/topbar anh huong trai nghiem khi mo dashboard o che do sidebar thu gon.

## 6. Permission/role menu da xu ly nhu the nao

- Khong doi role/permission nghiep vu.
- Giu logic hien co: Admin full quyen; manager/admin thay cau hinh, bao cao va he thong; nhan vien chi thay nhom theo vai tro inbound/outbound/inventory/transport/report.
- Bo sung test dam bao rule menu/role khong bi lui.

## 7. Co anh huong route/component nao khong

- Khong doi action/controller.
- Cac query moi duoc controller bo qua neu khong can, nen link cu van hoat dong.
- Form loc `StockMovement` giu lai `nav`.
- Link chon kho trong `InventoryMap` giu lai `map`.

## 8. Ket qua build/lint/test

- `dotnet build WMS.csproj --no-restore /p:UseSharedCompilation=false`: pass, 0 warning, 0 error.
- `dotnet test WMS.Tests/WMS.Tests.csproj --no-restore /p:UseSharedCompilation=false`: pass 693/693.
- `npm run visual:auth`: pass 1/1.
- `npm run visual:test`: pass 194/194, skipped 66 theo cau hinh.
- `npm run visual:mobile-deep`: pass 420/420.
- `npm run visual:no-device`: pass 10/10.
- Kiem tra DOM rieng:
  - `Reports/StockMovement?nav=inbound`: chi active `Lich su nhap kho`.
  - `Reports/StockMovement?nav=inventory`: chi active `Lich su nhap xuat`.
  - `Items?stockStatus=low`: chi active `Hang sap thieu`.
  - `Warehouses/InventoryMap?map=master`: chi active `Vi tri/ke/khu chua`.
  - `Warehouses/InventoryMap?map=inventory`: chi active `So do kho`.
  - Flyout `He thong` o sidebar thu gon nam trong viewport, scroll duoc toi `Thiet bi tin cay`.

## 9. Nhung diem con can xac nhan them

- Database read-only check ket noi duoc DB `HeThongNaNaNa`, co 139 bang, 85 migration, latest migration `20260705070000_RepairReportFefoDatabaseGuards`.
- Cac bang cot loi co mat; rieng `InventoryTransactions` va `StockSnapshotRuns` nam schema `wms_user`, khong phai `dbo`.
- Kiem tra du lieu cot loi deu 0 loi: ton/giu cho am, giu cho vuot ton, ton thieu vat tu, ton thieu o kho, dong phieu thieu phieu cha, ledger thieu vat tu/kho.
- Neu hosting yeu cau tat ca bang cung schema `dbo`, can xac nhan lai chinh sach schema; hien tai app van doc duoc vi connection user/schema dang dung phu hop.
