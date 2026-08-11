# AI-native WMS - Metric Dictionary STAT-01..15

Version: `STAT-CONTRACT-0.1`  
Mục đích: chốt source of truth trước khi sửa query/UI. `PARTIAL` không đồng nghĩa KPI đã đạt acceptance.

## Quy ước chung

- Snapshot phải ghi `AsOf`, timezone, warehouse/owner scope, base UOM và currency nếu có.
- Cộng numerator/denominator ở grain chuẩn rồi mới chia; mẫu số 0 hiển thị `Chưa đủ dữ liệu`.
- Partial/cancel/reversal/return/adjustment phải phân loại riêng, không bỏ âm thầm.
- Duration báo sample count, median, P90, P95; thiếu milestone vào bucket `Không đủ dữ liệu`.
- Mỗi số phải drill-down tới ledger, voucher, task, count hoặc audit nguồn.

## Dictionary

| ID | Câu hỏi/công thức chuẩn | Grain, source và event time | Scope/thiếu dữ liệu | Hiện trạng |
|---|---|---|---|---|
| `STAT-01` | `LineAccuracy=ExactEligibleLines/EligibleApprovedLines`; `QuantityAccuracy=max(0,1-SumAbsVariance/SumMaxAbsQty)`; thừa/thiếu/value riêng | Approved stock-count line; `ApprovedAt`; unit cost at count | Kho-owner-zone-location-item-lot; mẫu số 0=`Chưa đủ dữ liệu`; thiếu cost không chặn quantity | `PARTIAL_VERIFIED`: query độc lập khớp 1/1 dòng chính xác trên hosting; mẫu quá nhỏ, chưa có variance/reason/value |
| `STAT-02` | Opening + inbound - outbound + transfer +/- adjustment = closing | `InventoryTransactions` theo transaction time, grain inventory bucket | Kho-owner-item-location-lot/HSD/serial/LPN/hold; mismatch là DQ alert | `PARTIAL_VERIFIED`: hosting reconcile 10 bucket, 0 lệch quantity/reservation; chưa có point-in-time automation |
| `STAT-03` | Tuổi tồn, ngày nhận cuối, ngày xuất cuối, ngày không xuất và value theo bucket | Ledger inbound/outbound riêng; cutoff report | Không dùng receipt mới reset “không xuất”; thiếu cost báo quantity-only | `VERIFIED_SOURCE_AND_UI`: receipt/outbound đã tách; regression chứng minh receipt mới không reset tuổi chậm |
| `STAT-04` | Tồn theo expiry bucket và FEFO compliance tại allocation/pick cutoff | Lot/expiry ledger, reservation/hold eligibility | Thiếu HSD ở tracked item là DQ; FEFO phải dựng eligible lot | `PARTIAL`: expiry có, FEFO reconstruction chưa hoàn chỉnh |
| `STAT-05` | Arrival -> receive start/complete -> QC -> putaway create/complete; duration percentile/SLA | Voucher, appointment, QC, putaway timestamps | Thiếu mốc vào missing bucket, không fallback timestamp | `PARTIAL_VERIFIED`: query/UI không còn fallback, có median/P90/P95/sample và drill-down; hosting có 0/7 phiếu đủ mốc |
| `STAT-06` | Release -> allocation -> wave -> pick start/complete -> pack -> ship; active/wait time | Outbound voucher/task/package/shipment | Partial/cancel/retry phân loại riêng | `PARTIAL`: milestone phân tán, percentile/reconcile chưa đủ |
| `STAT-07` | `LineFillRate=fully fulfilled eligible lines/eligible lines`; order complete/on-time/short/perfect order | Outbound line/order/shipment | Perfect order chỉ khi đủ 4 dimension; reservation KPI giữ tên riêng | `PARTIAL`: có “Tỷ lệ đáp ứng giữ chỗ”, chưa phải line fill rate |
| `STAT-08` | ABC inventory value và usage value tách riêng; XYZ theo demand variability/versioned thresholds | Ledger outbound base qty + cost snapshot theo bucket | Min sample, mean=0 rule; warehouse-owner-policy version | `CONFIRMED_DEFECT`: ABC hiện tại hard-code/khác nghĩa; XYZ thiếu |
| `STAT-09` | Pareto reason theo count, abs qty và value | Approved count/adjustment/ledger/audit + structured reason | Không suy đoán reason bằng AI | `BLOCKED`: chưa có reason taxonomy theo dòng |
| `STAT-10` | Outlier IQR/MAD theo cohort tương thích | Transaction/task/audit theo event time | Hiện threshold/window/sample; chỉ cảnh báo, không kết luận gian lận | `PARTIAL`: có rule/anomaly alert, chưa có cohort statistic versioned |
| `STAT-11` | Ranking risk, coverage, freshness, Precision@K/lift/value detected và outcome | Feature snapshot/prediction/recommendation/outcome | Model/rule version, warehouse-owner scope, DQ status | `BLOCKED_FOR_ML`: hiện chỉ có unversioned rule alerts |
| `STAT-12` | Slot occupancy = occupied usable slots/usable slots; capacity ratio chỉ cùng dimension | Location/zone + inventory snapshot + capacity profile | Thiếu dimension -> `CAPACITY_DATA_MISSING` | `VERIFIED_SOURCE_AND_UI`: occupancy tách capacity; chỉ tính kg khi capacity và trọng lượng đầy đủ, không còn capacity giả |
| `STAT-13` | Task/line/base-UOM per active hour, wait/rework/backlog | Labor/task events | Chuẩn hóa task/zone/distance/weight; role manager cho PII | `PARTIAL`: có labor view, cần context/reconcile |
| `STAT-14` | Supplier on-time, in-full, QC pass, damage, document/lot/HSD error, dock-to-stock | Appointment/inbound/QC/partner | Cần expected-vs-actual và supplier mapping sạch | `PARTIAL`: nguồn có nhưng contract/reconcile chưa đủ |
| `STAT-15` | Velocity 7/30/90 bằng outbound base qty; DOS=available base qty/avg daily outbound; projected stockout chỉ khi lead-time đủ | Ledger outbound + current/as-of inventory | Avg demand 0 -> `Chưa đủ dữ liệu`; không dùng số phiếu | `VERIFIED_SOURCE_AND_UI`: DOS tính theo SKU, base UOM, outbound ledger và owner scope; regression pass |

## Source-of-truth priority

1. Inventory quantity/as-of: physical `InventoryTransactions` hoặc immutable reconciled snapshot.
2. Current balance: `ItemLocations` chỉ cho snapshot hiện tại và phải reconcile ledger.
3. Workflow duration: timestamp nghiệp vụ thật trên voucher/task/QC/putaway/shipment.
4. Count outcome: approved `StockCountSheets` + `StockCountLines`.
5. Alert/model: prediction source record; `EnterprisePredictiveAlert` chỉ là projection.

## Acceptance AI-1

Một STAT chỉ chuyển sang `VERIFIED` khi có query độc lập, cùng snapshot/scope, test mẫu số 0/partial/reversal, drill-down và UI hiển thị đúng. Tài liệu này chỉ hoàn tất contract; không tự đánh dấu AI-1 hoàn thành.

Evidence AI-1: `artifacts/ai-smart-cycle-count/AI1/AI1_EVIDENCE.md`. STAT-01..07 mới đạt `PARTIAL_RECONCILIATION`: STAT-05/06 thiếu mẫu đủ milestone và STAT-07 không có mẫu số trên hosting, nên không suy diễn kết quả PASS.
