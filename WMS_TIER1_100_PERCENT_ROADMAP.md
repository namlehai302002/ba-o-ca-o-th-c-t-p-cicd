# WMS Pro Tier-1 100 Percent Roadmap

Ngay cap nhat: 2026-07-01

Muc tieu cua tai lieu nay la dinh nghia ro "100%" cho WMS Pro theo chuan co the kiem chung. Trong bao cao nay, 100% khong co nghia la "khong bao gio con bug"; 100% co nghia la moi gate code, UI, nghiep vu, bao mat, thiet bi, hieu nang, DR/HA, tich hop va van hanh deu co evidence pass, da redact va co owner ky nhan.

Khong sua `appsettings.json`, khong in secret, khong ghi connection string, API key, cookie, password hoac customer data vao artifact.

## 1. Current Honest Score

| Nhom danh gia | Diem hien tai | Diem de goi la 100% | Nhan xet |
|---|---:|---:|---|
| Repo/local readiness | 95-96/100 | 100/100 local gates | Build, .NET test, visual, static guard va security scan da manh; can tiep tuc mo rong route visual va regression khi phat hien gap. |
| Production-equivalent | 88-91/100 | 100/100 signed evidence | Chua the dat 100% that neu thieu RF scanner, label printer, can dien tu, load/soak, DR/HA, pentest, hosting va integration certified. |
| UI/UX local | 96/100 | 100/100 visual + UAT | Visual desktop/mobile/no-device da xanh; can them route it duoc cover va UAT tren device that. |
| Nghiep vu WMS | 96/100 | 100/100 regression + UAT | Inbound/outbound/inventory/lot/serial/LPN/UOM/catch weight da co test sau; can tiep tuc bo sung scenario production-scale. |
| Bao mat/scope | 93/100 | 100/100 pentest + secret governance | Role, warehouse scope, owner scope, API key, CSRF da co gate; can pentest IDOR/export/API va secret rotation evidence. |
| Hieu nang/DR/HA | 72-82/100 | 100/100 staging evidence | Day la gap lon nhat: can k6/soak, backup restore, rollback va failover drill that. |

## 2. Benchmark Doi Chieu

| Benchmark | Nang luc Tier-1 can doi chieu | Trang thai WMS Pro |
|---|---|---|
| Oracle WMS Cloud 26B | Company/facility scope, ACL/functional security, inventory visibility, inbound/outbound, VAS, integration. | Da co warehouse/owner scope, RBAC, inbound/outbound, VAS/kitting, API/webhook; can certified external evidence. |
| Microsoft Dynamics 365 SCM Warehouse | Warehouse mobile app, wave templates, work templates, work pools, location directives. | Da co RF receiving/picking/movement, wave/pick task, slotting/putaway; can UAT tren thiet bi that va route/work-template maturity. |
| SAP EWM | Real-time control inbound/outbound/internal process, storage bin optimization, RF/mobile, integration. | Da co stock ledger, location/bin, RF/mobile, movement, integration scaffold; can load, MHE/WCS, DR/HA va certified integration. |
| Manhattan Active WM | Unified distribution control, labor, slotting, automation, global inventory visibility. | Da co slotting, labor/exception/automation dashboard, 3PL/billing, integration health; can telemetry, automation device evidence va production network proof. |

Nguon tham chieu:
- Oracle WMS Cloud Online Help 26B: `https://docs.oracle.com/pls/topic/lookup?ctx=owm-latest&id=OWMOL`
- Oracle ACL/Functional Security: `https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owsec/access-control-lists-functional-security.html`
- Microsoft D365 Warehouse management overview: `https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/warehouse-management-overview`
- Microsoft D365 Warehouse Management mobile app: `https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/install-configure-warehouse-management-app`
- SAP Extended Warehouse Management: `https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT`
- Manhattan Active Warehouse Management: `https://www.manh.com/solutions/supply-chain-management-software/warehouse-management`

## 3. Scorecard Theo Nhom Chuc Nang

| Nhom | Da dat trong repo/local | Con thieu de 100% dung nghia | Evidence can co | Owner | Acceptance gate |
|---|---|---|---|---|---|
| Inbound/QC/Putaway | Voucher, approval, receiving, QC hold/quarantine, serial/expiry, RF receiving. | UAT receiving tren RF handheld va camera scan that. | `HW-RF-001`, `HW-SCAN-003`, bien ban UAT. | Warehouse ops | Nhan hang dung item/location/lot/expiry, sai scan bi chan. |
| Outbound/Wave/Picking | Reservation, wave, pick task, RF picking, packing, shipping. | Wave stress voi nhieu user va device that. | `HW-RF-002`, load wave/pick, UAT picker. | Warehouse ops | Khong xuat am, pick sai ma bi chan, wave hoan tat dung ton. |
| Inventory/Stock Count | ItemLocation la source of truth, ledger, stock count, adjustment approval. | Kiem ke thiet bi that va reconciliation sau restore. | `DR-001`, `DR-002`, stock count UAT. | SRE + Inventory owner | Row-count va ton kho khop truoc/sau restore. |
| Lot/Serial/LPN/UOM/Catch weight | Co regression cho lot/serial/LPN/UOM/catch weight. | UAT serial scanner, label barcode va catch-weight device. | `HW-SCAN-001`, `HW-PRINT-001`, scale evidence. | Warehouse ops | Serial duplicate bi chan, barcode doc duoc, can ghi dung. |
| RF/Mobile UI | Visual no-device, mobile-deep, RF receiving/picking/movement. | Kiem thu tren handheld, tablet, scanner USB/Bluetooth. | `HW-RF-001..003`, `HW-SCAN-001..003`. | Warehouse ops | Khong overflow, focus scan dung field, offline/degraded state an toan. |
| Slotting/Optimization | Slotting, slotting simulation, optimization dashboard. | Baseline hieu nang voi nhieu SKU/location va sign-off rule. | Slotting perf artifact, ops sign-off. | Warehouse manager | Goi y dung scope owner/kho, khong tao movement sai. |
| Labor/Yard/3PL/Billing | Co labor, yard, 3PL billing/contracts/rates/portal. | UAT gia dich vu, invoice, yard dock thuc te. | Billing UAT, yard evidence. | Finance + Warehouse ops | Phi tinh dung rule, khong lap hoa don sai owner. |
| Integration/API/EDI/Webhook | API key, owner/warehouse scope, idempotency, webhook replay, EDI scaffold. | Certified ERP/TMS/OMS/MHE/carrier. | `INT-ERP-001`, `INT-TMS-001`, `INT-OMS-001`, `INT-MHE-001`, `INT-CAR-001`. | Integration owner | Retry/dead-letter/replay khong tao trung stock/voucher. |
| Security/Permission | RBAC, CSRF, API scope, export registry, password/session guards. | Pentest IDOR, tenant isolation, secret rotation, WAF/hosting evidence. | Pentest report, secret rotation log. | Security owner | 0 critical/high open, all findings triaged. |
| Observability/SRE | Health, OpenTelemetry packages, SRE dashboard, evidence gate. | Metrics/alerts tren staging/prod, on-call drill. | `OBS-001..006`. | SRE owner | p95/p99, 4xx/5xx, queue depth, dead-letter alert visible. |
| DR/HA/Rollback | Checklist/runbook co san. | Restore/failover/rollback drill that. | `DR-001..005`. | SRE + Release owner | RPO/RTO do duoc, rollback khong sai ton. |
| Performance/Load | Co script k6 scaffold. | Chay k6/soak tren staging gan production. | `LOAD-001..004`. | Release owner | p95/error-rate/DB wait/queue depth dat nguong release. |
| UI/UX/Tieng Viet | Visual desktop/mobile/zoom, mojibake/static guard. | Them route visual it cover va UAT tren device that. | Visual chain, UAT screenshots. | QA owner | 0 overlap, 0 mojibake visible, 0 5xx, 0 console/page error. |

## 4. Roadmap De Len 100%

### Phase 1 - Repo/Local Hardening

- Mo rong visual route coverage cho cac man Tier-1: slotting, slotting simulation, waves, wave planning, inbound approvals, label templates, print jobs, integration dashboard, exception center, predictive alerts.
- Duy tri static scan cho mojibake, raw HTML entity, debug/console trace, route 5xx, button overflow, modal overflow, print layout.
- Bo sung regression ngay khi phat hien gap nghiep vu: concurrent stock posting, cancellation rollback, FEFO/expiry, serial/LPN duplicate, owner/warehouse scope, webhook/API idempotency.
- Cap nhat scorecard/report moi lan them test de evidence khong lech so luong.

### Phase 2 - Staging Evidence

- Chay build/test/visual tren staging gan production, khong dung production data.
- Chay k6 authenticated load, write-heavy disposable data, soak test queue/outbox/dead-letter.
- Luu artifact da redact: command, timestamp, threshold, result, owner.
- Chay restore drill, row-count validation, health/login/inventory/voucher/report/export smoke.

### Phase 3 - Real Device And Integration

- UAT RF handheld receiving/picking/movement, USB/Bluetooth scanner, mobile camera scan, label printer, document printer, scale/catch-weight.
- Certified integration voi ERP/TMS/OMS/MHE/carrier: auth, scope, retry, dead-letter, replay, idempotency, reconciliation.
- Pentest API/export/download/scope/IDOR, secret governance, WAF/hosting controls.
- Ky nhan moi evidence ID truoc khi nang production-equivalent len 100%.

## 5. Definition Of 100%

| Cap do | Duoc goi la 100% khi |
|---|---|
| Repo/local 100% | Build, full .NET, vulnerability scan, npm audit, static scan, visual chain va targeted regression deu pass; khong co critical/high/medium open trong repo-local. |
| UI 100% local | Tat ca route visual trong manifest pass tren desktop/mobile/zoom/no-device/mobile-deep; khong 5xx, khong console/page error, khong mojibake visible, khong button/table/modal overflow. |
| Nghiep vu 100% local | Moi luong inbound/outbound/inventory/approval/cancel/rollback/idempotency/scope deu co regression pass va khong co known open bug. |
| Production 100% | Tat ca local gate va tat ca external evidence ID `HW-*`, `LOAD-*`, `DR-*`, `INT-*`, `OBS-*` deu `Pass`, co ngay chay that, owner, artifact da redact va sign-off. |

## 6. Current Blockers For Production 100%

- Thieu RF scanner/mobile handheld, camera, printer, scale/catch-weight evidence that.
- Thieu k6/load/soak/stress tren staging gan production.
- Thieu backup/restore/failover/rollback drill co RPO/RTO that.
- Thieu pentest va secret rotation evidence.
- Thieu certified ERP/TMS/OMS/MHE/carrier integration.
- Thieu production telemetry/on-call alert drill.

Ket luan: WMS Pro hien o muc rat manh cho demo, bao ve va internal WMS repo/local. De ghi "100% nhu he thong lon" mot cach trung thuc, can hoan tat ca local hardening lan external evidence backlog tren; neu chua co cac artifact do thi diem production phai giu duoi 100%.

