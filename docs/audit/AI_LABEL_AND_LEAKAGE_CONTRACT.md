# AI Smart Cycle Counting - Label And Leakage Contract

Contract version: `AI-LABEL-0.1`  
Áp dụng cho dataset builder, benchmark, rule/ML evaluation và outcome loop.

## 1. Prediction unit

Khóa logic:

`WarehouseId + OwnerPartnerId + ItemId + LocationId + LotNumber + ExpiryDate + PredictionCutoff`

- Null trong owner/lot/expiry là giá trị bucket rõ nghĩa, không phải wildcard.
- Lịch/work có thể aggregate ở item-owner-location, nhưng prediction/outcome contribution phải giữ lot/HSD.
- Quantity được đổi về base UOM trước khi so sánh.
- Serial-tracked item yêu cầu integer unit và serial coverage; serial ID không nằm trong grain ranking mặc định.

## 2. Cutoff, horizon và eligibility

- `PredictionCutoff`: thời điểm snapshot feature bất biến.
- Feature chỉ đọc event có thời gian nghiệp vụ `<= PredictionCutoff`; nếu dùng ingestion time phải khai báo và kiểm tra late arrival.
- Outcome window bắt đầu sau cutoff và kết thúc tại lần kiểm kê approved đầu tiên của cùng grain trong horizon đã cấu hình.
- Eligible outcome: sheet `Approved`, có `ApprovedAt`, line có `CountedQty`, `SystemQty`, `Variance`, scope hợp lệ và ledger/balance reconcile.
- Loại khỏi label: draft, counting, counted chưa duyệt, phiếu hủy, outcome mở khóa sau đó, test không cô lập, dòng thiếu base-UOM mapping hoặc DQ blocked.
- Một grain chỉ lấy outcome đầu tiên sau cutoff để tránh cùng prediction bị nhân đôi. Recount trong cùng workflow là một outcome cuối.

## 3. Hai label tách biệt

### 3.1. Quantity variance

`AbsoluteVarianceQty = abs(CountedBaseQty - SystemBaseQty)`

`HasQuantityVariance = AbsoluteVarianceQty > QuantityTolerance`

Tolerance AI-0:

- Decimal base UOM: `0.0001`, khớp độ chính xác quantity hiện có.
- Serial-tracked hoặc UOM nguyên chiếc: tolerance bằng 0 sau khi kiểm tra giá trị là số nguyên.
- Không làm tròn theo định dạng UI trước khi tạo label.

### 3.2. Material variance

`HasMaterialVariance` chỉ xác định khi có threshold snapshot versioned tại cutoff, có thể gồm:

- absolute quantity threshold theo base UOM;
- percentage threshold với mẫu số và zero rule rõ ràng;
- value threshold dùng unit-cost snapshot và currency.

Hiện `StockCountSheet` không lưu `ProgramId` hoặc threshold snapshot. Vì vậy dữ liệu legacy/manual không truy ra threshold đáng tin cậy phải mang label `UNKNOWN`, không tự dùng mặc định 5%.

## 4. Label provenance tối thiểu

Mỗi sample tương lai phải truy ra:

- prediction key/cutoff và source watermark;
- stock count sheet/line ID;
- `ApprovedAt`, approver và trạng thái ổn định;
- system/count quantity base UOM, tolerance version;
- material-threshold version và cost snapshot nếu áp dụng;
- reason code đã xác nhận;
- generated adjustment voucher/ledger references nếu có variance;
- DQ status và exclusion reason.

## 5. Leakage controls

| Nguy cơ | Control bắt buộc |
|---|---|
| Ngày kiểm kê bị cập nhật khi tạo sheet | Chỉ cập nhật `LastCountedAt` tại approval; regression AI-BL-01 |
| Feature chứa outcome hiện tại | Join feature bằng `event_time <= cutoff`; adjustment của count xảy ra sau cutoff bị loại |
| Random split cùng grain/thời gian | Chia temporal, giữ test cuối cùng chưa từng tune; group cùng scope để tránh copy gần nhau |
| Balance hiện tại dùng cho quá khứ | Rebuild từ ledger/snapshot có watermark; nếu không làm được thì `DQ_BLOCKED` |
| Cost/master data hiện tại | Dùng snapshot/version có hiệu lực; thiếu thì label/value feature là unknown |
| Recount nhiều lần | Chỉ dùng outcome cuối đã approved của workflow, không tạo nhiều label |
| Override được coi là AI sai | Override chỉ là decision; label chỉ đến từ count outcome approved |
| Scope owner bị trộn | Null owner và owner cụ thể là các partition khác nhau |

## 6. Temporal split và benchmark

- Train: cửa sổ cũ nhất; validation: cửa sổ kế tiếp; test: cửa sổ mới nhất chưa dùng tune.
- Gap/embargo tối thiểu bằng outcome horizon nếu feature window và label window có thể chồng nhau.
- Báo rõ số ngày, số sheet, số grain, prevalence, warehouse/owner coverage và positive count của từng split.
- Không chạy model candidate nếu validation/test không có đủ positive/negative samples để tính Precision@K, Recall@K và lift có ý nghĩa.
- Baseline bắt buộc: Random, ABC đến hạn và rule-based versioned.

## 7. Baseline DQ và quyết định AI-0

Evidence `artifacts/ai-smart-cycle-count/ai-cycle-count-dq.txt` cho thấy:

- 1 approved sheet, 1 approved day, 1 line;
- 0 quantity variance, 0 material variance có thể xác định;
- 0 active schedule và 0 structured line reason.

Do đó:

- Workflow label contract: `PASS` về khả năng định nghĩa và truy vết trên code hiện tại.
- Dataset train/evaluation: `BLOCKED` vì sample và temporal depth không đủ.
- Không backfill/mutate hosting. Không sinh positive label giả.
- AI-2 có thể triển khai rule baseline/shadow mode sau AI-1; AI-4/5 chỉ mở khi DQ acceptance đủ.

## 8. Acceptance cho dữ liệu ML

Owner nghiệp vụ phải chốt ngưỡng chính thức, nhưng tối thiểu kỹ thuật cần:

- ít nhất hai kỳ thời gian độc lập và test holdout có cả positive/negative;
- đủ Top-K eligible ở K được công bố;
- reason/scope/UOM/cutoff completeness đạt ngưỡng DQ;
- không có ledger mismatch nghiêm trọng;
- dataset build tái lập bằng command, seed/config và artifact hash.

