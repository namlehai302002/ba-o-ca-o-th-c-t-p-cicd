# Voucher State Machine

> **Tài liệu cũ:** Nguồn chuẩn hiện tại là `docs/domain/voucher-state-machine.md`, được truy vết từ runtime và kiểm tra bằng automated contract test. File này được giữ lại để bảo toàn liên kết và lịch sử tham khảo; không dùng để nghiệm thu Gate G0.3.

Ngay cap nhat: 12/06/2026

Pham vi tai lieu nay la state machine van hanh cho phieu kho WMS Pro. Tai lieu dung de doi chieu code, UAT va test regression; khong thay doi schema, route hay config.

## Nguyen tac chung

- Moi phieu kho co header va lines. Header quyet dinh kho, doi tac, chu hang neu co, ngay chung tu, so chung tu goc va trang thai. Lines quyet dinh vat tu, so luong, don vi tinh, lo, NSX, HSD, vi tri va serial neu co.
- Ton kho chi duoc ghi nhan tai buoc posting/complete hop le, khong ghi ton khi phieu con nhap/chua duyet.
- Moi action thay doi trang thai phai co actor, thoi diem, audit log va validation server-side.
- UI co the an nut theo role/trang thai, nhung backend van phai chan that neu user goi truc tiep endpoint.
- Neu phieu da huy, da khoa hoac da ghi so cuoi luong, khong cho sua/duyet/nhan/ghi so lai tru khi co rule dao nguoc/chung tu bu tru rieng.

## Roles

| Role | Ten nghiep vu | Pham vi hanh dong |
|---|---|---|
| Admin | Quan tri he thong | Toan bo cau hinh, phan quyen, audit, seed demo co kiem soat, xem va xu ly tat ca kho theo scope |
| Manager | Quan ly kho | Duyet, tu choi, huy hop le, phan cong, release picking, xu ly ngoai le, bao cao |
| Staff | Nhan vien kho/thu kho | Tao phieu theo scope, nhan hang, kiem hang, putaway, picking, packing, scan RF/mobile |
| Viewer | Chi xem | Xem dashboard, danh muc, ton kho, phieu va bao cao duoc cap quyen |

## Inbound State Machine

Ap dung cho: `NhapKho`, `KhachTra`, `NhapThanhPham`.

| State ky thuat | Ten hien thi | Action vao state | Role hop le | Validation chinh | Stock effect | Audit effect | Action tiep theo hop le |
|---|---|---|---|---|---|---|---|
| `Draft` | Nhap | Tao/Luu tam | Admin, Manager, Staff | Co kho, doi tac neu bat buoc, it nhat 1 line hop le neu submit | Khong doi ton | CreatedBy/CreatedAt | Gui duyet, sua, huy draft |
| `PendingApproval` | Cho duyet | Gui duyet | Admin, Manager, Staff | Lines co item active, UOM hop le, qty > 0, location hop le neu bat buoc | Khong doi ton | SubmittedBy/SubmittedAt | Duyet, tu choi, huy theo rule |
| `Approved` | Da duyet | Duyet | Admin, Manager | SoD: nguoi tao khong tu duyet neu policy bat; kho/owner scope dung | Khong doi ton | ApprovedBy/ApprovedAt | Nhan hang, lap lich dock, huy theo rule |
| `Receiving` | Dang nhan hang | Xac nhan nhan hang / bat dau kiem | Admin, Manager, Staff | Phieu da duyet, chua huy, chua posted; co dock/nguoi nhan neu policy yeu cau | Khong doi ton hoac chi tao task/QC, tuy flow | ReceivedBy/ReceivedAt, ReviewResult Pending | Hoan tat nhap, kiem hang, ghi variance |
| `Completed` | Hoan tat | Hoan tat nhap/ghi so | Admin, Manager | Neu can kiem thi phai co nguoi kiem/ket qua; qty/UOM/location/lot/expiry/serial hop le; HSD >= NSX | Tang ItemLocation/CurrentStock, tao ledger Receive | CompletedBy/CompletedAt, InventoryTransaction | Xem, in, export, huy bang reversal neu rule cho phep |
| `Rejected` | Tu choi | Tu choi duyet/kiem | Admin, Manager | Bat buoc co ly do tu choi | Khong doi ton | RejectionReason, ReviewedBy/ReviewedAt | Xem, sao chep tao phieu moi |
| `Cancelled` | Da huy | Huy | Admin, Manager | Trang thai cho phep huy; bat buoc ly do | Neu da posted thi reversal/release reservation theo service | CancelledBy/CancelledAt/CancelReason | Xem audit, khong thao tac tiep |

## Outbound State Machine

Ap dung cho: `XuatKho`, `TraNCC`, `ChuyenKho`, `XuatSanXuat`.

| State ky thuat | Ten hien thi | Action vao state | Role hop le | Validation chinh | Stock effect | Audit effect | Action tiep theo hop le |
|---|---|---|---|---|---|---|---|
| `Draft` | Nhap | Tao/Luu tam | Admin, Manager, Staff | Header/lines hop le neu submit | Khong doi ton | CreatedBy/CreatedAt | Gui duyet/release pick, sua, huy draft |
| `WaitingForPick` | Cho lay hang | Release picking/reserve | Admin, Manager | Ton kha dung >= so luong; UOM hop le; kho/owner scope dung; hang khong hold/huy/hong | Tang reserved, tao StockReservation/PickTask | ReleasedBy/Assigned evidence qua task | Picking, huy/release reservation |
| `Picking` | Dang lay hang | Bat dau pick/RF scan | Admin, Manager, Staff | PickTask assigned; scan dung item/location/serial/lot | Khong tru physical stock; co the cap nhat picked qty | PickTask scan log | Picked, short pick |
| `Picked` | Da lay hang | Hoan tat pick | Admin, Manager, Staff | Picked qty hop le, serial/lot khop reservation | Van giu reservation | PickedBy/PickTask CompletedAt | Ghi so xuat, packing |
| `Packed` | Da dong goi | Packing | Admin, Manager, Staff | Package/LPN/weight neu policy bat | Khong doi ton | PackedBy/PackedAt, package audit | Ship/post |
| `Completed` | Hoan tat xuat | Ghi so xuat | Admin, Manager | Reservation con hop le, khong xuat vuot ton, serial chua consumed | Giam ItemLocation/CurrentStock, consume reservation, tao ledger Issue | CompletedBy/CompletedAt, InventoryTransaction | Xem/in/export |
| `PartiallyIssued` | Xuat thieu | Ghi so mot phan | Admin, Manager | Policy cho phep partial; ly do short bat buoc | Tru phan consumed, release/cancel phan con lai theo rule | Short reason, audit | Backorder hoac dong phieu |
| `Shipped` | Da ban giao | Ban giao van chuyen | Admin, Manager, Staff neu policy | Carrier/load/package hop le | Khong doi ton neu da posted | ShippedBy/ShippedAt, manifest/tracking | Xem audit |
| `Cancelled` | Da huy | Huy | Admin, Manager | Trang thai cho phep huy; ly do bat buoc | Release reservation hoac reversal neu posted theo service | CancelledBy/CancelledAt/CancelReason | Xem audit |

## Transfer State Machine

| Buoc | Role | Validation | Stock effect |
|---|---|---|---|
| Tao phieu chuyen | Admin, Manager, Staff | Kho nguon va kho dich khac nhau, vi tri nguon/dich hop le, line hop le | Khong doi ton |
| Release/confirm pick | Admin, Manager | Ton kha dung tai nguon du | Giu cho ton nguon |
| Complete transfer | Admin, Manager | Pick/receive hop le, owner scope giu nguyen | Giam nguon, tang dich, tao ledger Transfer |
| Huy | Admin, Manager | Chua final hoac co reversal | Release reservation/reversal |

## Adjustment And Stock Count

| Flow | State | Rule |
|---|---|---|
| Kiem ke | Draft -> Counting -> Counted -> Approved | Chi tao dieu chinh ton sau khi duyet; chenh lech bat buoc co ly do |
| Dieu chinh tang | Draft/Pending -> Approved/Posted | Bat buoc reason code; location/UOM hop le; audit actor |
| Dieu chinh giam | Draft/Pending -> Approved/Posted | Khong lam ton am tru khi policy cho phep va co approval dac biet |

## Forbidden Transitions

| Transition bi cam | Ly do | Expected backend behavior |
|---|---|---|
| Draft -> Completed | Bo qua duyet/nhan/kiem | Return business error |
| PendingApproval -> Completed | Chua duyet | Return business error |
| Approved -> Posted outbound without reservation/pick | Sai luong xuat | Return business error |
| Cancelled -> Any mutating action | Phieu da dong | Return business error/Forbid |
| Completed -> Edit lines | Co nguy co lech ton | Return business error |
| Completed -> Complete again | Double posting | Idempotent no-op hoac business error, khong doi ton lan 2 |
| Different warehouse/owner scope -> Read/export/mutate | IDOR/scope leak | Forbid/404 safe envelope |

## Regression Evidence Expected

- Backend tests: wrong-state approve/complete/cancel, double-posting, negative qty, invalid UOM, over-issue, cancellation release reservation.
- Playwright opt-in E2E: create inbound with prefix, submit/approve/complete, verify stock increase, create outbound, verify over-issue blocked, complete outbound, verify stock decrease, cleanup/cancel created vouchers if possible.
- UAT: role matrix signed by Admin/Manager/Staff/Viewer for each state/action.
