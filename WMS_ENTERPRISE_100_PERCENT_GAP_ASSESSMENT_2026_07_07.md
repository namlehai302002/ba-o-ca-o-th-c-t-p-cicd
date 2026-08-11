# WMS Enterprise 100 Percent Gap Assessment - 2026-07-07

## Ket luan ngan gon

Trang thai hien tai cua he thong WMS noi bo: khoang 88-92% so voi muc can co cua mot he thong quan ly kho noi bo chuyen nghiep, va khoang 65-75% neu so truc tiep voi cac WMS enterprise toan cau nhu Oracle WMS Cloud, SAP EWM, Microsoft Dynamics 365 Supply Chain Management hoac Manhattan Active WM.

Ly do khong nen cham 100% ngay luc nay: cac WMS enterprise khong chi co UI va nghiep vu nhap/xuat/ton. Ho con co quy trinh san xuat chinh thuc, UAT theo tung vai tro, test thiet bi that, load test, DR/HA, monitoring, audit/compliance, mobile device governance, automation/MHE integration, labor management, yard/dock optimization va data governance o quy mo lon.

Trong pham vi du an noi bo, code/local/browser/DB audit hien da xanh theo cac cong kiem gan nhat:

- Build: pass, 0 warning / 0 error.
- EF drift: pass, khong co pending model changes.
- Unit test: pass 697/697.
- DB hosting data-quality artifact: 17 nhom kiem, khong co dong issue.
- Visual auth smoke: pass 1/1.
- Visual smoke trong diem: pass 19/19, 1 skipped dung thiet ke mobile cho collapsed flyout.
- Mobile deep smoke trong diem: pass 16/16.
- No-device RF/print smoke: pass 10/10.

## Benchmark theo he thong lon

1. Oracle WMS Cloud

- Oracle dung role, group va permission de kiem soat access. Administrator co quyen rong tren he thong, cac role khac duoc gioi han theo permission/man hinh.
- He thong cua minh da co admin override, role menu, permission claim va RBAC seed. Muc nay dat tot cho noi bo.
- Nguon: https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owsec/access-control-lists-functional-security.html
- Nguon: https://docs.oracle.com/en/cloud/saas/readiness/logistics/26b/wms26b/26B-wms-wn-f45822.htm

2. SAP EWM

- SAP EWM co Warehouse Management Monitor lam man trung tam de quan ly documents, warehouse orders/tasks, stock/bin, resource va alerts.
- He thong cua minh da co Trang chinh, Tong quan kho, KPI, canh bao, ton kho, nhap/xuat, van chuyen va audit. Muc nay dat kha tot cho noi bo.
- Khoang cach enterprise: can drill-down exception/action tu dashboard manh hon, workflow xu ly ngoai le chuan hon, va monitoring production theo thoi gian thuc.
- Nguon: https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT/3d97bec9bf1649099384bb8167df3cf2/51cdcb53ad377114e10000000a174cb4.html

3. Microsoft Dynamics 365 Supply Chain Management

- Dynamics 365 co workspace theo doi suc khoe/license cua thiet bi kho va mobile app cho handheld.
- He thong cua minh da co RF/mobile smoke, no-device fallback va menu van hanh mobile. Muc nay tot cho demo/noi bo.
- Khoang cach enterprise: can device enrollment, device health, session audit, offline sync conflict policy va test may quet/may in/camera that.
- Nguon: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/mobile-device-workspace
- Nguon: https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/install-configure-warehouse-management-app

4. Manhattan Active WM

- Manhattan tap trung vao cloud-native, automation, sortation, AS/RS, robotics, WES va integration voi thiet bi tu dong.
- He thong cua minh da co cac hook/tich hop/automation dashboard o muc nen tang, nhung chua the coi la ngang enterprise automation.
- Khoang cach enterprise: can integration contract that, MHE simulator, event outbox/retry monitoring, SLO, load test va runbook van hanh.
- Nguon: https://www.manh.com/solutions/supply-chain-management-software/warehouse-management

## Diem da dat gan enterprise cho WMS noi bo

- Navigation da gom dung nghiep vu: Trang chinh, Nhap kho, Xuat kho, Ton kho, Van chuyen, Bao cao, Danh muc, He thong, Huong dan.
- Dashboard da di theo huong control tower noi bo: mo len thay viec can lam, KPI, canh bao va loi vao nhanh.
- Role/permission co admin full quyen, manager/warehouse/transport/report/inventory auditor theo nhom nghiep vu.
- Bao cao ton, thong ke nhap/xuat, tong quan kho, audit, canh bao va snapshot/chot ton da co nen tang.
- DB da co data-quality audit va EF drift gate.
- Visual regression/no-device/mobile smoke da bao ve cac loi UI lon da tung gap: flyout bi che, active menu trung, mobile drawer che noi dung, scanner/print khong co thiet bi.

## Phan con thieu de tien gan 100% production enterprise

1. UAT theo vai tro that

- Tao checklist UAT rieng cho Admin, Quan ly kho, Nhan vien nhap, Nhan vien xuat, Nhan vien ton/kiem ke, Nhan vien van chuyen, Nhan vien bao cao.
- Moi vai tro can dang nhap bang account rieng, test menu thay/khong thay, quyen xem/sua/duyet/xuat file.

2. Test thiet bi that

- May quet barcode keyboard wedge.
- Camera mobile scan QR/barcode.
- May in tem/chung tu.
- Dien thoai Android kho va tablet neu co.
- Kich ban offline/mang yeu, scan lap, scan sai ma, scan mat ket noi.

3. Load/performance

- Test tai cho dashboard, bao cao ton, nhap/xuat, stock movement, inventory map.
- Can nguong muc tieu: p95 response, request/s, DB query thoi gian dai, CPU/RAM, connection pool.
- Can script k6/Playwright load hoac tool tuong duong tren moi truong gan production.

4. Backup/restore va DR

- Backup DB tu hosting theo lich.
- Test restore vao DB staging.
- Co runbook neu migration loi, mat DB, mat file upload, mat secret/app setting.

5. Monitoring production

- Health check endpoint.
- Error log tap trung.
- Slow query log.
- Canh bao khi login fail bat thuong, job fail, export fail, inventory audit co issue, migration chua apply.

6. Data governance

- Bo quy tac khoa ky/chot ton/snapshot co nguoi phe duyet.
- Audit ai sua gi, luc nao, truoc/sau la gi.
- Chinh sach xoa mem, archive, retention cho voucher, audit, scan log, file upload.

7. Nghiep vu nang cao neu muon ngang enterprise

- Labor management: nang suat nhan vien, task time, SLA.
- Slotting optimization nang cao.
- Wave planning nang cao.
- Cross-dock/cross-flow day du.
- Yard/dock appointment nang cao.
- Return/RMA neu doanh nghiep co doi tra.
- ASN/EDI/API voi ERP/eCommerce/carrier.
- MHE/robotics/sortation integration neu co thiet bi that.

## De xuat roadmap de len gan 100%

### P4 - Production readiness

- Viet UAT matrix theo role.
- Them smoke test login/menu theo tung role.
- Them health check + monitoring page doc log loi gan nhat.
- Them runbook backup/restore/migration rollback.
- Them k6/load smoke cho dashboard, inventory, stock movement.

### P5 - Device and warehouse floor readiness

- Test voi may quet/may in/camera that.
- Device registry/health dashboard cho handheld.
- Offline queue conflict policy.
- Print template QA cho tem va chung tu.

### P6 - Enterprise expansion

- Labor/KPI nang cao.
- EDI/API integration voi ERP/carrier/eCommerce.
- MHE/automation simulator va outbox/retry dashboard.
- Advanced exception workflow tu dashboard.

## Danh gia cuoi

- Muc dat voi WMS noi bo: 88-92%.
- Muc dat neu so voi WMS enterprise toan cau: 65-75%.
- Neu hoan thanh P4/P5 va UAT production that: co the dat 95%+ cho muc tieu WMS noi bo.
- De goi la 100% ngang enterprise lon: can them P6, ha tang production, thiet bi that, monitoring, DR, load test va quy trinh van hanh chinh thuc.

Khong co he thong nao co the cam ket 0 bug tuyet doi chi bang code review. Cam ket dung ky thuat nen la: moi thay doi phai qua build, unit test, EF drift, data-quality audit, visual regression, UAT role, test thiet bi that, load test va monitoring production.
