# AI Smart Cycle Counting - Feature Dictionary

Version contract: `AI-FEATURE-SCHEMA-0.1`  
Trạng thái: contract AI-0, rule baseline AI-2 dùng tập con feature hiện tại; chưa phải dataset/model production.

## 1. Quy tắc chung

- Grain: warehouse-owner-item-location-lot-expiry tại `PredictionCutoff`.
- Mọi event feature chỉ dùng bản ghi có `event_time <= PredictionCutoff`.
- Số lượng dùng base UOM; tiền dùng cost snapshot và currency xác định tại cutoff.
- `NULL owner` là scope nội bộ riêng, không phải tất cả owner.
- Không dùng `CountedQty`, `Variance`, `ApprovedAt`, adjustment sinh từ chính outcome đang dự đoán hoặc dữ liệu sau cutoff làm feature.
- Giá trị thiếu phải có cờ DQ riêng; không tự đổi `NULL` thành 0 nếu hai ý nghĩa khác nhau.

## 2. Dictionary

`AVAILABLE` nghĩa là có nguồn runtime; `DERIVABLE` cần query builder có cutoff; `BLOCKED` thiếu provenance/schema/dữ liệu.

| ID | Feature | Định nghĩa tại grain | Nguồn/event time | Leakage guard | Readiness |
|---|---|---|---|---|---|
| `F001` | `on_hand_base_qty` | Tồn tại cutoff | `InventoryTransactions` ledger dựng lại theo `TransactionDate/CreatedAt` | Không đọc balance hiện tại cho cutoff quá khứ | `DERIVABLE` |
| `F002` | `reserved_base_qty` | Giữ chỗ còn hiệu lực tại cutoff | Reservation/history và event hiệu lực | Không lấy trạng thái hiện tại nếu thiếu history | `BLOCKED` cho historical |
| `F003` | `available_base_qty` | `on_hand - reserved - hold` | Derived | Cùng cutoff và UOM | `DERIVABLE` |
| `F004` | `inbound_qty_7d/30d/90d` | Tổng nhập base UOM theo cửa sổ | `InventoryTransactions` | `event_time <= cutoff` | `DERIVABLE` |
| `F005` | `outbound_qty_7d/30d/90d` | Tổng xuất base UOM theo cửa sổ | `InventoryTransactions` | Không dùng số phiếu thay quantity | `DERIVABLE` |
| `F006` | `movement_count_7d/30d/90d` | Số giao dịch vật lý | `InventoryTransactions` | Loại idempotent duplicate/reversal theo contract ledger | `DERIVABLE` |
| `F007` | `adjustment_abs_qty_30d/90d` | Tổng tuyệt đối điều chỉnh | `InventoryTransactions` loại Adjust | Không lấy adjustment từ outcome sau cutoff | `DERIVABLE` |
| `F008` | `transaction_actor_count_30d` | Số actor khác nhau | Inventory/audit actor | Không lộ PII trong UI feature | `DERIVABLE` |
| `F009` | `off_hour_activity_count_30d` | Event ngoài ca đã cấu hình | Transaction/audit + shift config | Thiếu shift config thì DQ missing | `BLOCKED` |
| `F010` | `days_since_last_approved_count` | Ngày từ `LastCountedAt` đã duyệt | `CycleCountSchedules.LastCountedAt` hoặc approved sheet | Không dùng ngày tạo sheet | `AVAILABLE` sau AI-BL-01 |
| `F011` | `prior_count_count_180d` | Số outcome approved trước cutoff | Stock count sheet/line | Chỉ status ổn định, approved trước cutoff | `DERIVABLE` |
| `F012` | `prior_variance_rate_180d` | Tỷ lệ outcome có quantity variance | Stock count outcome | Không dùng outcome hiện tại | `DERIVABLE`, hiện thiếu mẫu |
| `F013` | `prior_abs_variance_qty_180d` | Tổng absolute variance | Stock count line | Base UOM, approved only | `DERIVABLE`, hiện thiếu mẫu |
| `F014` | `recount_count_180d` | Số lần yêu cầu đếm lại | Audit/history | Notes text không đủ provenance ổn định | `BLOCKED` |
| `F015` | `abc_class` | Class A/B/C và version policy | Cycle count schedule/program | Lưu version/cutoff, không hard-code trong model | `AVAILABLE`, provenance partial |
| `F016` | `xyz_class` | Demand variability class | Outbound bucket theo config | Min sample, mean=0 -> DQ state | `BLOCKED` |
| `F017` | `days_since_last_receipt` | Ngày từ nhập vật lý cuối | Ledger inbound | Tách khỏi last outbound | `DERIVABLE` |
| `F018` | `days_since_last_outbound` | Ngày từ xuất vật lý cuối | Ledger outbound | Không reset bởi receipt mới | `DERIVABLE` |
| `F019` | `location_movement_count_30d` | Lưu lượng tại vị trí | Ledger source/destination | Hai hướng cùng cutoff | `DERIVABLE` |
| `F020` | `location_distinct_sku_count` | SKU đang có tồn tại cutoff | Ledger snapshot | Không dùng ItemLocations hiện tại cho quá khứ | `DERIVABLE` |
| `F021` | `slot_occupied_flag` | Vị trí có tồn > tolerance | Ledger snapshot/location | Dùng quantity tolerance | `DERIVABLE` |
| `F022` | `capacity_utilization` | Usage/capacity cùng dimension | Location + capacity profile | Thiếu dimension -> NULL + DQ, không chia quantity/capacity giả | `BLOCKED` |
| `F023` | `lot_count_at_location` | Số lot còn tồn | Ledger lot | Lot null là bucket riêng | `DERIVABLE` |
| `F024` | `days_to_expiry` | HSD - cutoff date | Lot/expiry ledger | Chỉ item quản lý HSD; missing flag riêng | `DERIVABLE` |
| `F025` | `lot_tracking_flag` | Item yêu cầu quản lý lot | Item master | Master data tại cutoff nếu có history | `AVAILABLE` current |
| `F026` | `expiry_tracking_flag` | Item yêu cầu HSD | Item master | Như trên | `AVAILABLE` current |
| `F027` | `serial_tracking_flag` | Item quản lý serial | Item master | Serial là drill-down, không quantity sum | `AVAILABLE` current |
| `F028` | `serial_coverage_ratio` | Serial active / on-hand integer units | Serial registry + ledger | Chỉ item serial; denominator > 0 | `DERIVABLE` |
| `F029` | `hold_qty_ratio` | Hold/quarantine / on-hand | Quality/hold records | Historical state cần history | `BLOCKED` cho historical |
| `F030` | `unit_cost_snapshot` | Cost tại cutoff | Cost/valuation snapshot | Không dùng giá hiện tại cho label quá khứ | `BLOCKED` nếu thiếu snapshot |
| `F031` | `data_quality_flags` | Bitset/codes về source thiếu | DQ extractor | Không impute âm thầm | `DERIVABLE` |
| `F032` | `source_watermark` | Max source event/version đã đọc | Ledger, balance và count source tại lần chấm | Bắt stale score và reproducibility | `AVAILABLE_CURRENT` từ AI-2; historical reconstruction vẫn `BLOCKED` |

## 3. Feature không được phép

- Kết quả đếm, variance, reason, adjustment hoặc approval của chính outcome tương lai.
- Trạng thái balance/reservation hiện tại khi tái dựng một cutoff cũ mà không có lịch sử.
- Email/tên người dùng thô làm feature xếp hạng; chỉ dùng aggregate hợp lệ và kiểm soát quyền.
- Dữ liệu OCR/LLM không xác minh, alert text hoặc lời giải thích do LLM sinh.
- Capacity ratio khi numerator và denominator khác dimension.

## 4. DQ policy

- `DQ_BLOCKED`: thiếu warehouse/item/location, warehouse-location mismatch, tracked lot/HSD thiếu, ledger không reconcile, cutoff không tái dựng được, phạm vi có nhiều trạng thái tồn hoặc vật tư quản lý số sê-ri chưa có mô hình kiểm kê từng số sê-ri. Hai trường hợp cuối không được tự động tạo phiếu vì cấu trúc dòng kiểm kê hiện tại chưa lưu trạng thái tồn hoặc danh sách số sê-ri cần đếm.
- `DQ_PARTIAL`: lịch sử giao dịch/kiểm kê còn ngắn nên điểm ưu tiên chưa có đủ tín hiệu quá khứ. `NULL owner` là scope hàng nội bộ hợp lệ và không tự tạo cảnh báo.
- `DQ_OK`: source watermark, grain, UOM và cutoff đầy đủ.
- Model/rule phải báo coverage; không xếp hạng scope `DQ_BLOCKED` như rủi ro bằng 0.
- Giao diện chỉ hiển thị nhãn nghiệp vụ tiếng Việt; mã kỹ thuật `BLOCKED_*`/`PARTIAL_*` chỉ dùng trong log, snapshot và kiểm thử. Mã `PARTIAL_*` cũ vẫn được ánh xạ sang nhãn tiếng Việt để đọc snapshot đã lưu trước khi nâng hợp đồng.

## 5. Tập feature rule baseline AI-2

Rule `RULE-BASELINE-1.0` sử dụng các feature đã truy vết: `F001`, `F002`, `F003`, `F006`, `F007`, `F008`, `F010`, `F011`, `F012`, `F013`, `F015`, `F017`, `F018`, `F019`, `F020`, `F023`, `F024`, `F025`, `F026`, `F027`, `F029`, `F031` và `F032` tại cutoff hiện tại. Feature bị `BLOCKED` cho historical dataset không được suy diễn là đã sẵn sàng cho AI-4/AI-5.

Mỗi snapshot lưu feature JSON chuẩn hóa, feature hash, source watermark, DQ status và scope key. Cùng input, cấu hình và cutoff cho cùng output hash; thay cấu hình dưới cùng version bị từ chối và bắt buộc tăng version.
