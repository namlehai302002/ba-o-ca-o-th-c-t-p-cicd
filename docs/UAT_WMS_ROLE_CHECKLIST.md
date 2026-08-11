# UAT WMS Role Checklist

Ngay cap nhat: 12/06/2026

Muc dich: checklist nghiem thu theo vai tro de xac minh WMS Pro truoc demo, pilot hoac go-live. Checklist nay khong thay the test tu dong; no ghi bang chung thao tac that cua nguoi dung.

## Cach dung

- Moi dong phai co ket qua `Pass`, `Fail`, hoac `Blocked`.
- Evidence nen la screenshot, video, ma phieu, audit log id, export file da redact, hoac log server da redact.
- Neu dung DB shared hosting/production-like, khong seed/reset/migrate trong luc UAT tru khi co phe duyet.
- Khong ghi mat khau, connection string, API key, cookie, token vao evidence.

## Admin

| ID | Luong UAT | Expected result | Pass/Fail | Evidence |
|---|---|---|---|---|
| ADM-01 | Dang nhap, xem dashboard, sidebar, breadcrumb | Hien dung ten nguoi dung, role Quan tri, khong loi UI |  |  |
| ADM-02 | Tao/sua khoa danh muc: kho, khu vuc, vi tri, UOM, doi tac, vat tu | Validate bat buoc; khong tao duplicate code; audit log co actor |  |  |
| ADM-03 | Chay data quality audit read-only | Tra ve issue list ro severity; khong sua du lieu |  |  |
| ADM-04 | Xem users/roles/permissions | Thay role matrix, scope kho/owner; khong lo password hash |  |  |
| ADM-05 | Thu access URL manager/staff/viewer pages | Admin duoc phep neu policy cho phep; log audit nhay cam |  |  |
| ADM-06 | Export report/documents | File tai ve dung scope, khong lo secret/config |  |  |
| ADM-07 | Kiem tra demo data apply | Confirm bat buoc; cancel confirm khong spinner; concurrent apply bi khoa |  |  |

## Manager / Quan ly kho

| ID | Luong UAT | Expected result | Pass/Fail | Evidence |
|---|---|---|---|---|
| MGR-01 | Duyet phieu nhap cho dung scope kho | Trang thai PendingApproval -> Approved/Receiving theo flow; audit co ApprovedBy |  |  |
| MGR-02 | Tu choi phieu nhap co ly do | Trang thai Rejected; ton kho khong doi |  |  |
| MGR-03 | Huy phieu hop le co ly do | CancelledBy/CancelledAt/CancelReason day du; reservation/stock reverse dung |  |  |
| MGR-04 | Release picking cho phieu xuat | Tao reservation/pick task; khong vuot ton kha dung |  |  |
| MGR-05 | Thu xuat vuot ton | Backend chan bang loi nghiep vu, ton khong doi |  |  |
| MGR-06 | Duyet stock count adjustment | Chi sau khi Counted va co ly do chenhlech |  |  |
| MGR-07 | Xem bao cao ton/nhap-xuat-ton/audit | So lieu khop ledger; filter kho/owner dung |  |  |

## Warehouse Staff / Thu kho

| ID | Luong UAT | Expected result | Pass/Fail | Evidence |
|---|---|---|---|---|
| STF-01 | Tao phieu nhap co 1 line | Validate kho, NCC, item, UOM, qty, location |  |  |
| STF-02 | Tao phieu nhap nhieu line khac lot/HSD | Lines luu dung lot/NSX/HSD; khong merge sai |  |  |
| STF-03 | Xac nhan nhan hang/kiem hang | Tu gan ReceivedBy/nguoi kiem; ghi thoi gian va ket qua |  |  |
| STF-04 | Hoan tat nhap | Ton tang dung tai ItemLocation, CurrentStock, ledger |  |  |
| STF-05 | Tao/phat hanh pick task neu duoc cap quyen | Chi trong scope kho; quet dung item/location/serial |  |  |
| STF-06 | Thu sua phieu da completed | Backend chan; UI an/disable nut sai trang thai |  |  |
| STF-07 | RF/mobile receiving/picking tren browser | Layout khong vo; offline queue neu mat mang co message ro |  |  |

## Viewer / Chi xem

| ID | Luong UAT | Expected result | Pass/Fail | Evidence |
|---|---|---|---|---|
| VW-01 | Xem dashboard/danh muc/ton kho | Chi doc; khong thay nut mutating |  |  |
| VW-02 | Truy cap truc tiep URL tao/sua/xoa/duyet | Bi Forbid/Redirect an toan; backend chan that |  |  |
| VW-03 | Export/download neu khong co quyen | Bi chan hoac chi cho file duoc cap quyen |  |  |
| VW-04 | Xem report theo warehouse scope | Khong thay du lieu ngoai scope |  |  |

## Cross-role Negative Tests

| ID | Test | Expected result | Pass/Fail | Evidence |
|---|---|---|---|---|
| NEG-01 | User khong dang nhap vao URL bao mat | Redirect login |  |  |
| NEG-02 | Staff goi approve POST truc tiep | Forbid/403 hoac business error |  |  |
| NEG-03 | Viewer goi export/download truc tiep | Forbid/403 hoac safe envelope |  |  |
| NEG-04 | User scope kho A doc/mutate phieu kho B | Forbid/404 safe envelope |  |  |
| NEG-05 | User owner A doc/export owner B | Forbid/empty safe envelope |  |  |
| NEG-06 | Submit double-click | Chi tao/post mot lan; ton khong doi lan 2 |  |  |
| NEG-07 | OCR/API timeout/503 | Message than thien, button restore, khong ghi du lieu ban |  |  |

## Sign-off

| Role | Nguoi nghiem thu | Ngay | Ket luan | Chu ky |
|---|---|---|---|---|
| Admin |  |  |  |  |
| Manager |  |  |  |  |
| Warehouse Staff |  |  |  |  |
| Viewer |  |  |  |  |

