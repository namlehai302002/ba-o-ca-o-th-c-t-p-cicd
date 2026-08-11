# AI-4 Dataset và Benchmark Contract

Ngày kiểm chứng: 2026-07-16  
Phạm vi: dataset offline cho rủi ro sai lệch kiểm kê, temporal split và benchmark baseline.  
Trạng thái runtime hosting: `BLOCKED_CONFIGURATION` tại bước mở kết nối read-only của môi trường kiểm chứng hiện tại; chưa chạy schema query, chưa đọc dữ liệu và không áp dụng migration lên hosting trong AI-4.

## 1. Mục tiêu và giới hạn

AI-4 tạo một pipeline **chỉ đọc** để liên kết feature snapshot đã bất biến với kết quả kiểm kê được duyệt sau prediction cutoff. Pipeline không tạo phiếu, không điều chỉnh tồn, không ghi model và không thay đổi database.

AI-4 chỉ cung cấp hạ tầng dataset, temporal split và baseline benchmark. Việc huấn luyện hoặc công bố model ML thuộc AI-5 và phải giữ `BLOCKED_DATA` cho đến khi dataset thật đạt data gate.

## 2. Grain và cutoff

Một observation có grain:

`Warehouse + Owner + Item + Location + Lot + Expiry + PredictionCutoff + StockCountLine`

- Khóa nghiệp vụ và ID thô chỉ tồn tại trong bộ nhớ để join; artifact chỉ xuất số thứ tự vô danh như `record-000001`, không xuất ID hoặc hash định danh nghiệp vụ.
- Feature chỉ lấy từ `InventoryRiskFeatureSnapshots`; không dựng lịch sử bằng `ItemLocations` hiện tại.
- `PredictionCutoff <= BuildAsOf` và `Snapshot.CreatedAt <= BuildAsOf`.
- Snapshot được phép có độ trễ ghi tối đa 5 phút sau prediction cutoff để dung nạp transaction runtime; quá ngưỡng bị loại với `SNAPSHOT_BACKFILLED_AFTER_CUTOFF` nhằm ngăn backfill mang dữ liệu tương lai.
- Feature schema phải trùng version, JSON phải có đúng tập thuộc tính, không trùng/thiếu/thừa thuộc tính và SHA-256 phải khớp.
- Snapshot quản lý lô/HSD thiếu grain tương ứng bị loại.
- Snapshot hoặc outcome của vật tư quản lý số sê-ri bị loại với `SERIAL_TRACKED_OUTCOME_COVERAGE_UNAVAILABLE` vì outcome kiểm kê hiện chưa có liên kết số sê-ri đủ để tạo ground truth.

## 3. Nhãn và outcome hợp lệ

Nhãn hiện có:

- `HasQuantityVariance = abs(CountedQty - SystemQty) > QuantityTolerance`.
- `HasMaterialVariance = UNKNOWN_THRESHOLD_SNAPSHOT_MISSING` cho đến khi có snapshot ngưỡng số lượng/phần trăm/giá trị có hiệu lực tại cutoff.

Outcome chỉ hợp lệ khi:

- Phiếu và dòng kiểm kê ở trạng thái cuối đã duyệt.
- Có `CountedAt`, `ApprovedAt`, `CompletedAt`, `CountedQty`, `Variance` và base UOM; thời điểm đếm và duyệt phải sau prediction cutoff, đúng thứ tự và nằm trong outcome horizon.
- Không có lần mở khóa tại hoặc sau lần duyệt đang dùng.
- Kho của vị trí khớp kho phiếu; owner, item, location, lot và HSD khớp đúng grain snapshot.
- Base UOM của outcome phải trùng base UOM của snapshot.
- `Variance` lưu trữ khớp với phép tính lại trong tolerance.
- Sai lệch khác 0 phải có phiếu điều chỉnh được ghi sổ, chưa hủy, chứa đúng dòng điều chỉnh theo grain và số lượng có dấu; đồng thời có ledger event khớp đúng phiếu, item, vị trí, owner, lô, HSD và delta.
- Outcome xảy ra sau cutoff và trong outcome horizon đã trưởng thành tại `BuildAsOf`.
- Mỗi dòng outcome chỉ được dùng cho một snapshot. Khi nhiều snapshot cạnh tranh cùng outcome, ưu tiên prediction đã liên kết trực tiếp với recommendation; nếu không có thì dùng cutoff gần outcome nhất. Các bản sao còn lại bị loại với `OUTCOME_REUSED_BY_MULTIPLE_SNAPSHOTS`.
- Dùng outcome hợp lệ đầu tiên trong horizon; draft, đang đếm, chưa duyệt, timeline sai và ledger chưa reconcile đều bị loại.

`AUDIT_TEST_` bị loại mặc định; chỉ được đưa vào khi command bật rõ `--include-isolated-test-data`. Dữ liệu `DEMO-`/`DEMO_` cũng bị loại mặc định; chỉ được đưa vào khi bật `--include-demo-data`, vẫn giữ cờ `is_demo_data`, công bố số dòng riêng và tạo readiness blocker để không thể nhầm với bằng chứng production.

## 4. Temporal split

- Không random split.
- Train, validation và test có boundary thời gian tách biệt.
- Embargo giữa train-validation và validation-test phải ít nhất bằng outcome horizon.
- Label của train không được vượt boundary validation và label của validation không được vượt boundary test; các trường hợp này tạo readiness blocker riêng.
- Entity xuất hiện ở partition muộn hơn sẽ bị purge khỏi partition sớm hơn để không overlap entity giữa các tập.
- Mỗi partition phải có cả positive và negative; test phải đủ ít nhất 100 dòng để báo `Precision@100`.
- Khi lịch sử không đủ để tạo boundary an toàn, kết quả là `TEMPORAL_SPLIT_INSUFFICIENT_HISTORY`, không hạ ngắn embargo.

## 5. Benchmark

Baseline bắt buộc:

1. Random deterministic bằng SHA-256 của seed và sample key.
2. ABC + số ngày đến hạn kiểm kê.
3. Rule baseline đã lưu cùng snapshot.
4. Model candidate chỉ được thêm từ AI-5 sau data gate.

Metric:

- Precision@10/50/100.
- Recall@10/50/100.
- Lift@10/50/100 so với prevalence.
- PR-AUC theo average precision khi candidate phủ toàn bộ tập có cả hai lớp.
- Tổng sai lệch tuyệt đối phát hiện chỉ được cộng khi tất cả positive trong Top K có cùng base UOM không rỗng; mixed/unknown UOM sẽ công bố `SUPPRESSED_MIXED_OR_UNKNOWN_UOM` thay vì cộng sai thứ nguyên.
- Số lượt kiểm kê trên một sai lệch phát hiện.
- Estimated effort khi toàn bộ Top K có effort.
- Coverage theo tỷ lệ observation có score.

Metric không được tạo khi mẫu, coverage hoặc lớp mục tiêu không đủ. Model candidate chỉ đủ điều kiện đánh giá/promote khi có tên, version, phủ score 100% test rows, có đủ Top K yêu cầu và PR-AUC khả dụng. Không có model candidate thì trạng thái tối đa là `BASELINE_ONLY`.

## 6. Artifact và tái lập

CLI yêu cầu phạm vi rõ ràng: dùng `--all-scopes` hoặc ít nhất một `--warehouse-id`; không được đồng thời dùng cả hai. `--owner-id` là bộ lọc tùy chọn. Connection ưu tiên biến môi trường `ConnectionStrings__Ai4ReadOnly`; chỉ khi truyền `--allow-application-connection` mới được fallback sang `ConnectionStrings__DefaultConnection` hoặc cấu hình ứng dụng. Tool không sao chép `appsettings*` vào output và không ghi connection ra console/artifact.

Ví dụ an toàn với connection read-only từ environment:

```text
dotnet run --project tools/WMS.Ai4.Dataset --configuration Release --no-restore -- --as-of yyyy-MM-ddTHH:mm:ss --all-scopes --outcome-horizon-days 90 --seed 20260716
```

`--as-of` là bắt buộc và không nhận timezone ngầm. `--read-timeout-seconds` giới hạn thời gian đọc. Tool kiểm tra schema trong snapshot transaction read-only, dùng no-tracking và rollback transaction sau khi dựng bundle.

Mỗi bundle nằm ở thư mục bất biến theo cutoff và dataset hash:

`artifacts/ai-smart-cycle-count/AI4/runs/{as-of}-{dataset-hash-prefix}`

- `experiment-manifest.json`
- `dataset-summary.csv`
- `split-summary.csv`
- `benchmark-results.csv`
- `predictions-sanitized.csv`
- `benchmark.log`
- `artifact-hashes.csv`

Artifact không chứa warehouse/owner/item/location/count-sheet/line ID thô, hash định danh nghiệp vụ, lot number, source watermark hoặc connection details. Chuỗi CSV có nguy cơ formula injection được prefix an toàn. Manifest lưu command được sinh lại từ typed query, seed, cutoff, schema version, sample/class count, limitation và hash source code/lockfile/tool binary. Bundle được ghi qua staging directory và promote nguyên tử; lần chạy bị hủy không phá bundle hợp lệ trước đó.

## 7. Data gate hiện tại

Run read-only ngày 2026-07-16 trả:

- Dataset: `BLOCKED_CONFIGURATION / SQL_CONNECTION_OPEN_INVALID_OPERATION` tại bước mở kết nối từ cấu hình ứng dụng được cho phép rõ bằng cờ CLI.
- Temporal split: `BLOCKED_DATA / TEMPORAL_SPLIT_INSUFFICIENT_HISTORY`.
- Benchmark: `BLOCKED_DATA`; không có model candidate và không có test rows.

Run không đi tới transaction/schema query nên kết quả này không chứng minh schema thiếu và cũng không phải bằng chứng về chất lượng dữ liệu hosting.

Do đó:

- Không thêm ML.NET hoặc dependency ML.
- Không huấn luyện Logistic Regression/Tree/Forest.
- Không calibration, PFI hoặc promote model.
- Rule/ABC vẫn là fallback/champion hiện hành.
- Không áp dụng migration hosting chỉ để làm checklist AI-4 xanh.

## 8. Nguồn phương pháp

- Microsoft ML.NET yêu cầu chuẩn bị dữ liệu, giữ test set tách biệt và đánh giá trên dữ liệu chưa dùng để train: <https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/prepare-data-ml-net>.
- Hướng dẫn train/evaluate của Microsoft phân biệt training và evaluation, đồng thời cảnh báo underfit/overfit: <https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/train-machine-learning-model-ml-net>.
- Permutation Feature Importance chỉ được xem xét ở AI-5 sau khi có model thật: <https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/explain-machine-learning-model-permutation-feature-importance-ml-net>.
- NIST AI RMF Playbook được dùng cho nguyên tắc đo lường, governance, limitation và human oversight: <https://airc.nist.gov/airmf-resources/playbook/>.

## 9. Rollback

AI-4 không có migration hoặc production write. Rollback là bỏ các service offline, test, tool project và artifact AI-4; runtime scoring/recommendation AI-2/AI-3 không phụ thuộc các thành phần này.
