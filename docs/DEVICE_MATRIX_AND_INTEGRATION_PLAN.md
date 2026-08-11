# Device Matrix And Integration Plan

Ngay cap nhat: 12/06/2026

Tai lieu nay ghi ro phan nao co the xac minh local va phan nao can thiet bi/API/moi truong that. Khong duoc claim pass production neu chua co evidence that.

## Device Matrix

| Device | Use case | Local evidence hien co | Evidence that can co | Pass criteria | Status |
|---|---|---|---|---|---|
| Barcode scanner USB/HID | Quet item/location/lot trong receiving, picking, movement | Browser input/manual scan route, no-device visual | Video quet bang scanner that, ma scan, phieu, audit log | Scan dung field, sai ma bi chan, khong mat focus/layout | Chua xac minh local bang hardware |
| RF handheld Android/iOS | RF receiving, RF picking, RF movement | Mobile-deep visual routes | Device model, browser/app version, video thao tac, network log | Hoan tat flow end-to-end, responsive, offline/timeout ro | Chua xac minh hardware |
| Camera/mobile camera | Quet QR/barcode bang camera | No-device test cho fallback | Permission prompt, video camera scan, error permission denied | Cho phep scan, deny hien message than thien | Chua xac minh hardware |
| Label printer ZPL | In tem hang, tem customer, tem LPN | Label route/template source | ZPL/PDF output, anh tem in that, printer model | Barcode doc duoc, text khong tran, dung khach/owner | Chua xac minh printer |
| Document printer | In phieu nhap/xuat/manifest | Print CSS/source/visual | PDF/giay in that, page break, chu ky | Khong cat bang, header/footer dung | Chua xac minh printer |
| Scale/can dien tu | Catch weight receiving/pick-pack | Catch weight service/tests | API/COM/Bluetooth evidence, calibration record | Can nhan dung, sai tolerance bi chan | Chua xac minh hardware |

## Integration Matrix

| Integration | Data direction | Scope/security | Test case | Evidence can co | Status |
|---|---|---|---|---|---|
| ERP/accounting | Item, UOM, PO/SO, inventory posting | API key, warehouse scope, owner scope, idempotency | Import item/PO, export receipt/issue ledger | Contract, sample payload, retry/dead-letter log | Chua certified integration |
| OMS/e-commerce | Sales order, cancellation, allocation status | API key + owner/warehouse scope | Receive 3 orders, reserve, cancel 1, fulfill 2 | Order ids, reservation ledger, webhook ack | Chua integration that |
| TMS/carrier | Shipment, label, manifest, tracking | Carrier connector permission, idempotency | Create label, manifest close, tracking callback | Label PDF/ZPL, callback log, status timeline | Chua carrier certification |
| MHE/WCS | Pick/putaway command, mission event | Connector profile, signed callback | Send mission, complete event, retry failed command | Command id, event id, audit trail | Chua MHE hardware |
| Webhook | Inventory/voucher event | Secret/signature, retry/dead-letter | Simulate endpoint 200/500/timeout | Delivery log, retry schedule | Local/mock only |
| EDI | 940/945/943/944/846 payloads | Partner/owner scope, payload audit | Import order, export shipment/receipt/inventory | EDI sample, validation report | Chua partner certification |

## Evidence Naming

Use pattern:

```text
artifacts/production-evidence/YYYYMMDD/<CODE>-<short-name>/
```

Examples:

- `HW-RF-001-rf-receiving-video.mp4`
- `HW-PRINT-002-label-scan-result.png`
- `INT-CAR-001-carrier-label-and-callback.json`
- `INT-ERP-001-receipt-posting-payload-redacted.json`

## Minimum Pass Criteria Before Production Claim

- It nhat 1 inbound, 1 outbound, 1 transfer, 1 stock count pass tren device that neu flow dung device.
- Label/document printer in that va barcode scan lai duoc.
- OCR/API provider co timeout/503 test va SLA fallback.
- Load test staging co p95/p99, timeout, deadlock, error rate.
- Backup restore drill co log restore va smoke test sau restore.
- UAT role checklist duoc sign-off.

