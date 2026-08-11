# ROADMAP WMS ENTERPRISE FULL-SCOPE — 100% EVIDENCE-BASED READINESS

> Phiên bản: 3.0 — Full enterprise scope  
> Mục tiêu: đưa WMS nội bộ đạt **100% tiêu chí áp dụng đã được kiểm chứng**, 0 lỗi đã biết còn mở và có mức sẵn sàng tương đương chuẩn vận hành của WMS enterprise trong phạm vi đã phê duyệt.  
> Phạm vi: toàn bộ source code, database, API, UI, bảo mật, nghiệp vụ, dữ liệu, test, hiệu năng, tích hợp, thiết bị, hạ tầng, tài liệu, pilot và vận hành sau go-live.  
> Kết quả hợp lệ: **NO-GO**, **DEVICE-FREE READY** hoặc **ENTERPRISE INTERNAL WMS READY**. Không được tuyên bố 100% nếu thiếu bằng chứng.

---

## 1. Mục tiêu và nguyên tắc

Roadmap này phải chứng minh được các điều sau bằng test, log hoặc artifact:

1. Không một thao tác hợp lệ hoặc đồng thời nào làm sai tồn kho.
2. Không thể post/hủy/xử lý trùng một chứng từ do double-click, retry hoặc job chạy lại.
3. Người dùng chỉ xem và thao tác đúng phạm vi quyền, kho và owner được cấp.
4. Dữ liệu sai được ngăn chặn ở nơi phù hợp, không chỉ được phát hiện sau khi đã ghi vào database.
5. Các luồng nhập, xuất, chuyển kho, kiểm kê, điều chỉnh và trả hàng hoạt động end-to-end.
6. Có thể deploy, migrate, rollback và khôi phục database bằng quy trình đã diễn tập.
7. Lỗi có thể truy vết, cảnh báo và xử lý mà không lộ secret hoặc dữ liệu nhạy cảm.
8. Hệ thống đạt ngưỡng hiệu năng đã thống nhất trên quy mô dữ liệu và số người dùng dự kiến.

### Nguyên tắc bắt buộc

- Backend phải kiểm tra quyền; ẩn menu không được xem là bảo vệ quyền.
- Mọi thay đổi tồn kho phải đi qua một luồng nghiệp vụ chuẩn và có ledger.
- Một command nghiệp vụ phải atomic: thành công toàn bộ hoặc rollback toàn bộ.
- Không dùng số tồn hiển thị trên client làm căn cứ quyết định cập nhật tồn.
- Không được sửa/xóa trực tiếp ledger đã post; sửa sai bằng nghiệp vụ đảo/reversal có audit.
- Test phải chạy được lặp lại và có dữ liệu test xác định.
- Mọi tiêu chí nghiệm thu phải có bằng chứng, không nghiệm thu bằng cảm nhận.
- Hạng mục `BLOCKER` không được hoãn để đạt go-live gate.

---

## 2. Phạm vi

### Trong phạm vi

- Authentication, role, permission, route/API guard và data scope.
- Transaction, concurrency, idempotency và trạng thái chứng từ.
- Tồn kho theo kho, vị trí, owner, vật tư, lô, serial và đơn vị tính nếu hệ thống có hỗ trợ.
- Nhập kho, xuất kho, chuyển kho, reservation, kiểm kê, điều chỉnh, trả hàng và hủy phiếu.
- Import Excel, export Excel/PDF, upload chứng từ và OCR nếu đang có trong hệ thống.
- Unit test, integration test, API test, E2E test và visual regression.
- Database constraint, migration, index, data-quality audit và backup/restore.
- Logging, monitoring, audit trail, exception workflow và runbook vận hành.
- Performance smoke/load bằng công cụ sẵn có; không bắt buộc cài k6.

### Phạm vi được triển khai theo giai đoạn

- Gate 0–7 kiểm chứng toàn bộ phần mềm mà chưa phụ thuộc thiết bị thật.
- Gate 8–11 bổ sung capability enterprise, multi-warehouse/owner và integration.
- Gate 12 kiểm thử scanner, camera, printer, mobile, network và automation.
- Gate 13–14 kiểm thử analytics, labor, AI, scalability, compliance và resilience.
- Gate 15 hoàn tất dữ liệu thật, pilot, cutover và hypercare.

Không có hạng mục nào được tự động xem là “không cần”. Tính năng không áp dụng phải có quyết định N/A, lý do nghiệp vụ và người phê duyệt. N/A hợp lệ được loại khỏi điểm Internal Readiness nhưng vẫn thể hiện là khoảng cách trong Enterprise Parity.

---

## 3. Quy ước quản lý roadmap

### Mức độ

| Mức độ | Ý nghĩa | Quy tắc nghiệm thu |
|---|---|---|
| `BLOCKER` | Có thể làm sai tồn, mất dữ liệu, lộ quyền hoặc không khôi phục được hệ thống | Bắt buộc pass; không được defer |
| `MUST` | Cần có để vận hành ổn định và truy vết được | Bắt buộc pass hoặc xác nhận `N/A` có bằng chứng |
| `SHOULD` | Cải thiện khả năng vận hành, hiệu năng hoặc UX | Có thể defer nếu có owner, deadline và risk acceptance |

### Trạng thái

- `TODO`: chưa làm.
- `IN_PROGRESS`: đang làm.
- `PASS`: đã đạt và có bằng chứng.
- `FAIL`: đã kiểm tra nhưng chưa đạt.
- `BLOCKED`: bị chặn bởi dependency.
- `N/A`: không áp dụng, phải ghi rõ lý do và bằng chứng.
- `DEFERRED`: chỉ dùng cho `SHOULD`, phải có owner và ngày xử lý.

### Thông tin bắt buộc cho từng hạng mục

| ID | Severity | Owner | Status | Dependency | Evidence | Deadline | Ghi chú/rủi ro |
|---|---|---|---|---|---|---|---|
| Ví dụ: G1.1 | BLOCKER | Backend | TODO | Không | Link artifact/test | YYYY-MM-DD |  |

---

## 4. Hồ sơ tải và dữ liệu chuẩn trước khi test

Phải điền các biến sau trước khi chốt ngưỡng hiệu năng:

| Biến | Nội dung cần xác định |
|---|---|
| `U_EXPECTED` | Số người dùng đồng thời dự kiến ở giờ cao điểm |
| `U_TEST` | Mức test đồng thời, tối thiểu `max(2 × U_EXPECTED, 20)` nếu môi trường cho phép |
| `ITEM_COUNT` | Số vật tư active dự kiến |
| `LOCATION_COUNT` | Số kho/vị trí dự kiến |
| `LOT_SERIAL_COUNT` | Số lô/serial active dự kiến |
| `TX_PER_DAY` | Số dòng giao dịch tồn kho mỗi ngày |
| `TX_HISTORY` | Số dòng lịch sử dùng trong test báo cáo |
| `IMPORT_MAX_ROWS` | Số dòng import tối đa được hỗ trợ |
| `EXPORT_MAX_ROWS` | Số dòng export đồng bộ tối đa; lớn hơn phải xử lý nền nếu cần |
| `RPO` | Mức mất dữ liệu tối đa được chấp nhận |
| `RTO` | Thời gian khôi phục tối đa được chấp nhận |

Nếu chưa có số liệu thật, Product Owner và kỹ thuật phải thống nhất một profile giả lập, ghi rõ giả định và dùng nhất quán trong tất cả artifact.

---


## 4.1. Mô hình chấm điểm 100%

Phải báo cáo đồng thời ba chỉ số, không được gộp thành một con số mơ hồ:

1. **Internal WMS Readiness Score**: mức hoàn thiện đối với phạm vi WMS nội bộ đã phê duyệt.
2. **Tier-1 Enterprise Capability Parity**: mức bao phủ tính năng so với nhóm WMS lớn; tính năng không triển khai vẫn làm giảm điểm parity dù có thể hợp lý với kho nội bộ.
3. **Evidence Coverage**: tỷ lệ yêu cầu/file/luồng đã được kiểm tra bằng bằng chứng mới trên đúng build.

### Trọng số chuẩn

| Nhóm đánh giá | Trọng số |
|---|---:|
| Kiến trúc và chất lượng code | 6% |
| Authentication, security, permission và data isolation | 10% |
| Inventory/data integrity | 14% |
| Nghiệp vụ kho cốt lõi | 14% |
| Nghiệp vụ kho nâng cao | 12% |
| Integration và extensibility | 8% |
| Thiết bị, mobile và automation | 6% |
| UI/UX, accessibility và localization | 7% |
| QA, automated test và visual quality | 8% |
| Performance, scalability và resilience | 6% |
| Deployment, observability, DR, audit và compliance | 6% |
| Analytics, labor và optimization | 3% |
| **Tổng** | **100%** |

### Thang điểm từng yêu cầu

- 0/4: không có, hỏng hoặc chưa kiểm tra.
- 1/4: có prototype nhưng không dùng ổn định.
- 2/4: có một phần, thiếu case quan trọng hoặc lỗi đáng kể.
- 3/4: hoạt động đủ nhưng thiếu hardening, test hoặc bằng chứng production.
- 4/4: enterprise-grade, pass đầy đủ functional/non-functional/security/visual tests và có artifact.

### Công thức và luật chống chấm điểm ảo

- Internal Readiness = tổng điểm có trọng số của các mục áp dụng / tổng trọng số áp dụng.
- Enterprise Parity không loại bỏ tính năng enterprise chỉ vì hệ thống hiện chưa cần; phải thể hiện đúng khoảng cách với sản phẩm lớn.
- UNKNOWN và NOT TESTED nhận 0 điểm cho đến khi có bằng chứng.
- Chỉ cho N/A trong Internal Readiness khi có lý do nghiệp vụ, owner phê duyệt và bằng chứng.
- Không dùng số lượng file, số lượng test hoặc số lượng màn hình để tự suy ra chất lượng.
- Artifact cũ chỉ là manh mối; phải xác minh lại trên build hiện tại trước khi dùng làm bằng chứng.

### Trần điểm bắt buộc

- Có lỗi Critical về tồn kho, mất dữ liệu, authorization hoặc secret: Internal Readiness tối đa 49%.
- Chưa pass concurrency, idempotency, migration hoặc restore rehearsal: tối đa 69%.
- Chưa có E2E thật theo vai trò cho luồng nhập/xuất/kiểm kê: tối đa 79%.
- Chưa visual-test toàn bộ màn hình và trạng thái được hỗ trợ: tối đa 89%.
- Evidence Coverage dưới 100%: không được báo 100%.
- Còn bất kỳ defect đã biết nào chưa đóng: trạng thái không được ghi “0 known defects”.

### Ý nghĩa “100%”

100% trong roadmap này có nghĩa là:

- 100% yêu cầu áp dụng đạt 4/4.
- 100% file nguồn first-party được phân loại và review hoặc test có truy vết.
- 100% critical workflow pass.
- 100% gate bắt buộc có artifact mới.
- 0 defect đã biết còn mở ở mọi severity tại thời điểm ký.
- 0 test fail, flaky chưa xử lý, console error, network error, visual diff chưa duyệt hoặc data-quality issue chưa giải quyết.
- Thiết bị và pilot thật đã pass nếu tuyên bố Enterprise Internal WMS Ready.

“100%” không phải bằng chứng rằng phần mềm không bao giờ có bug trong tương lai; đó là trạng thái kiểm chứng tại build, dữ liệu, môi trường và thời điểm được ghi trong sign-off.


# GATE 0 — BASELINE, PHẠM VI VÀ KIẾN TRÚC NGHIỆP VỤ

## G0.1. Chốt baseline có thể tái lập — `MUST`

- [x] Ghi commit SHA/build version dùng để đánh giá.
- [x] Ghi phiên bản runtime, database và các dependency chính.
- [x] Ghi cấu hình staging liên quan nhưng không ghi secret.
- [x] Tạo database test/staging từ migration và seed chuẩn.
- [ ] Tạo thêm database từ bản sao dữ liệu hiện có đã ẩn dữ liệu nhạy cảm.
- [x] Ghi số lượng row của các bảng lớn và profile tải tại Mục 4.
- [x] Liệt kê integration bên ngoài: OCR, email, object storage, background jobs và API khác.
- [x] Chốt danh sách browser/viewport thực sự được hỗ trợ.

### Tiêu chí nghiệm thu

- Một người khác có thể dựng lại môi trường test từ tài liệu.
- Mọi artifact sau đó đều tham chiếu cùng build, schema và profile dữ liệu.

### Bằng chứng

- `artifacts/baseline/environment.md`
- `artifacts/baseline/data-profile.md`
- `artifacts/baseline/build-info.json`
- `artifacts/baseline/staging-configuration.md`

## G0.2. Chốt nguồn dữ liệu chuẩn — `BLOCKER`

- [x] Xác định nguồn sự thật cho tồn kho: ledger, ItemLocation hoặc mô hình tương đương.
- [x] Nếu lưu cả `Item.CurrentStock` và tổng vị trí, mô tả cách cập nhật atomic và cách reconcile.
- [x] Xác định công thức `AvailableQty = Quantity - ReservedQty` và các ngoại lệ hợp lệ.
- [x] Xác định phạm vi tồn: warehouse, location, owner, item, lot, serial, UOM.
- [x] Xác định quy tắc làm tròn và độ chính xác decimal cho số lượng, tiền và conversion rate.
- [x] Xác định chính sách thời gian: UTC trong DB hay giờ Việt Nam, cách xử lý ngày hết hạn và thời điểm khóa kỳ.

### Tiêu chí nghiệm thu

- Không còn hai nguồn dữ liệu cùng được xem là nguồn chuẩn nhưng có thể cập nhật độc lập.
- Công thức và invariant tồn kho được dùng nhất quán trong code, audit và test.

### Bằng chứng

- `docs/domain/inventory-source-of-truth.md`
- `docs/audit/WMS_RUNTIME_MAP.md`
- `WMS.Tests/Gate0BaselineContractTests.cs`
- Inventory balance, reservation, ledger, UOM, period-lock và Vietnam-time regression tests.

## G0.3. Chốt state machine của chứng từ — `BLOCKER`

- [x] Liệt kê trạng thái của từng loại chứng từ.
- [x] Lập bảng chuyển trạng thái hợp lệ và vai trò được thực hiện.
- [x] Quy định trạng thái nào được sửa line, được duyệt, được post, được hủy và được reversal.
- [x] Quy định hành vi của phiếu partial, rejected, failed và cancelled.
- [x] Không cho client tự gửi trạng thái tùy ý để bỏ qua business service.

### Bằng chứng

- `docs/domain/voucher-state-machine.md`
- Automated tests cho toàn bộ transition hợp lệ và không hợp lệ.

---

# GATE 1 — TRANSACTION, CONCURRENCY VÀ IDEMPOTENCY

> Toàn bộ Gate 1 là `BLOCKER` và không được phép defer.

## G1.1. Atomic transaction cho nghiệp vụ tồn kho

- [x] Post nhập kho cập nhật header, line, tồn và ledger trong cùng transaction.
- [x] Post xuất kho cập nhật reservation, tồn, line và ledger trong cùng transaction.
- [x] Chuyển kho trừ nguồn và cộng đích trong cùng transaction hoặc có workflow bù trừ an toàn được chứng minh.
- [x] Kiểm kê/điều chỉnh cập nhật kết quả và ledger trong cùng transaction.
- [x] Hủy/reversal cập nhật chứng từ, reservation, tồn và ledger trong cùng transaction.
- [ ] Mô phỏng lỗi tại từng bước để xác nhận rollback không để lại partial data.
- [x] External call như OCR/email không được giữ database transaction quá lâu.

### Tiêu chí nghiệm thu

- Mỗi test fault injection cho kết quả database trước và sau giống nhau nếu command thất bại.
- Không tồn tại phiếu đã post nhưng thiếu ledger hoặc chỉ cập nhật một phần vị trí tồn.

## G1.2. Chống xử lý trùng và retry an toàn

- [x] Mỗi command quan trọng có idempotency strategy hoặc unique business key.
- [x] Double-click nút post chỉ tạo một lần thay đổi tồn.
- [x] Retry do timeout không tạo thêm ledger hoặc reservation.
- [x] Background job chạy lại không xử lý cùng business event hai lần.
- [x] Hủy phiếu nhiều lần không hoàn tồn/reservation nhiều lần.
- [x] Import lại cùng file hoặc cùng chứng từ phải phát hiện duplicate trước khi ghi.
- [x] Có response rõ ràng khi request đã được xử lý trước đó.

### Tiêu chí nghiệm thu

- Gửi lặp cùng command tối thiểu 5 lần vẫn chỉ tạo một kết quả nghiệp vụ.
- Không có duplicate ledger, duplicate reservation hoặc duplicate voucher ngoài trường hợp được thiết kế rõ.

## G1.3. Concurrency và race condition

- [x] Hai người cùng xuất một item/location/lot không thể làm âm tồn khả dụng.
- [x] Hai người cùng reserve không thể vượt tồn khả dụng.
- [x] Post xuất và kiểm kê cùng lúc phải có kết quả xác định hoặc conflict rõ ràng.
- [x] Post chuyển kho và xuất kho cùng nguồn phải được kiểm soát.
- [x] Hai người cùng sửa một phiếu không âm thầm ghi đè dữ liệu mới hơn.
- [x] Áp dụng row version/concurrency token hoặc atomic conditional update phù hợp.
- [ ] Có retry giới hạn cho deadlock/transient error; không retry lỗi nghiệp vụ.
- [x] Log correlation ID và mã conflict để truy vết.

### Tiêu chí nghiệm thu

- Test đồng thời ở mức `U_TEST` không tạo âm tồn, vượt reservation hoặc duplicate transaction.
- Conflict trả thông báo tiếng Việt dễ hiểu và không làm spinner/nút submit bị kẹt.

## G1.4. Ledger và reversal

- [x] Ledger đã post là append-only đối với người dùng thông thường.
- [x] Mỗi ledger row liên kết được với source voucher/source line và request/correlation ID.
- [x] Unique constraint hoặc kiểm soát tương đương ngăn tạo hai ledger cho cùng source event.
- [x] Sửa sai bằng reversal/adjustment, không update âm thầm lịch sử đã post.
- [x] Reversal tham chiếu giao dịch gốc và có audit reason.

### Bằng chứng Gate 1

- Unit/integration tests.
- Concurrency test report.
- Fault-injection/rollback report.
- SQL audit chứng minh không có duplicate hoặc orphan ledger.

---

# GATE 2 — SECURITY, PERMISSION VÀ DATABASE INTEGRITY

## G2.1. Role, permission và route/API guard — `BLOCKER`

- [x] Tạo permission matrix cho Admin, Quản lý kho, Nhập kho, Xuất kho, Kiểm kê, Vận chuyển và Báo cáo.
- [x] Bổ sung owner/đối tác nếu hệ thống có scoped owner.
- [x] Mỗi quyền ghi rõ menu thấy/không thấy, action được phép và API/route bị chặn.
- [x] Backend kiểm tra quyền trên tất cả action đọc và ghi quan trọng.
- [x] Admin full quyền theo policy đã định nghĩa.
- [x] Nhân viên báo cáo không sửa/post/hủy chứng từ.
- [x] Nhân viên vận chuyển không truy cập quản trị hệ thống.
- [x] Kiểm tra truy cập trực tiếp URL/API khi menu đã bị ẩn.
- [x] Kiểm tra mass assignment: client không tự gán owner, warehouse, role, status hoặc audit fields.

### Tiêu chí nghiệm thu

- Không có route quan trọng chỉ được bảo vệ bằng UI.
- Test policy và integration authorization pass.

## G2.2. Object-level và data-scope authorization — `BLOCKER`

- [x] User kho A không xem/sửa được chứng từ hoặc tồn kho B nếu không có quyền.
- [x] Owner A không đọc hoặc tác động dữ liệu owner B.
- [x] Đổi ID trong URL/body không gây IDOR.
- [x] Export, dashboard, autocomplete và lookup cũng áp dụng cùng data scope.
- [ ] Background job và scheduled report chạy dưới scope được xác định rõ.
- [ ] Admin override nếu có phải được audit.

## G2.3. Authentication và session security — `BLOCKER`

- [x] Password được hash bằng thuật toán phù hợp; không lưu hoặc log plaintext.
- [x] Cookie/session dùng cấu hình `HttpOnly`, `Secure` và `SameSite` phù hợp.
- [x] Session timeout, logout và thu hồi phiên hoạt động đúng.
- [x] Login có rate limit/lockout hợp lý và không tiết lộ tài khoản có tồn tại hay không.
- [x] Reset mật khẩu/token có hạn dùng và chỉ dùng một lần nếu hệ thống hỗ trợ.
- [x] MFA được test nếu đang có; nếu chưa dùng phải ghi rõ quyết định phạm vi.
- [x] Tài khoản bị khóa/ngưng hoạt động mất quyền theo thời gian chấp nhận được.

## G2.4. Web, API và file security — `BLOCKER`

- [x] Bảo vệ CSRF cho request thay đổi dữ liệu dùng cookie authentication.
- [x] Encode output và kiểm thử XSS ở tên vật tư, ghi chú, nhà cung cấp và filename.
- [x] Query dùng parameterization/ORM an toàn; không ghép raw SQL từ input.
- [x] Validate DTO server-side; không tin validation của UI.
- [x] Upload kiểm tra extension, MIME/content signature, kích thước và filename an toàn.
- [x] File upload không thực thi được và không path traversal.
- [x] Export trung hòa công thức nguy hiểm bắt đầu bằng `=`, `+`, `-`, `@` khi cần.
- [x] Không lộ stack trace, connection string, API key hoặc internal path cho client.
- [x] Quét dependency vulnerability và secret trong pipeline.

## G2.5. Database constraints — `BLOCKER`

- [x] Foreign key cho các quan hệ bắt buộc.
- [ ] Unique constraint/index cho mã nghiệp vụ và khóa chống duplicate.
- [x] `NOT NULL` cho dữ liệu thực sự bắt buộc.
- [x] Check constraint hoặc application invariant cho quantity, reserved quantity và conversion rate.
- [x] Precision/scale decimal thống nhất, không dùng floating point cho số lượng/tiền cần chính xác.
- [x] Không cho `ReservedQty < 0` hoặc `ReservedQty > Quantity` ngoài trạng thái đặc biệt được tài liệu hóa.
- [x] Chính sách xóa/soft delete không tạo orphan và không cho vô hiệu hóa danh mục đang được sử dụng trái quy tắc.
- [ ] Index/constraint phải tương thích với owner, warehouse, lot và serial scope đã chốt.

## G2.6. Data-quality audit nâng cao — `MUST`

- [x] Quantity hoặc available quantity âm ngoài ngoại lệ được phê duyệt.
- [x] Reserved quantity âm hoặc vượt quantity.
- [x] Tổng reservation active không khớp reserved quantity.
- [x] Consumed/released vượt reserved.
- [x] Tồn tổng và tồn theo vị trí không khớp nếu cả hai được lưu.
- [x] Phiếu đã post nhưng thiếu ledger.
- [x] Ledger orphan, duplicate hoặc liên kết với phiếu chưa post.
- [x] Tổng ledger không reconcile với tồn hiện tại theo phạm vi chuẩn.
- [ ] Transaction có quantity, UOM, direction hoặc amount bất hợp lệ.
- [x] Lô hết hạn nhưng vẫn available/pickable; lô hết hạn ở khu cách ly không mặc định xem là lỗi.
- [x] NSX lớn hơn HSD hoặc date boundary sai.
- [x] Serial duplicate theo phạm vi unique đã chốt.
- [x] Vật tư active thiếu BaseUom.
- [x] UnitConversion thiếu, duplicate hoặc rate không hợp lệ.
- [x] Phiếu header không có line hoặc line không có header.
- [x] Voucher total/line total sai do rounding.
- [x] Trạng thái phiếu không khớp với reservation/ledger.

### Tiêu chí nghiệm thu Gate 2

- Không có security finding mức Critical/High chưa xử lý.
- Data-quality audit không có issue `BLOCKER`.
- Issue được chấp nhận chỉ có thể thuộc mức không chặn, có owner, lý do và deadline.

### Bằng chứng

- `docs/security/permission-matrix.md`
- `artifacts/security/`
- `scripts/data-quality/`
- `artifacts/data-quality/`
- `artifacts/full-audit/GATE2_SECURITY_DATABASE_EVIDENCE_2026_07_13.md`

### Trạng thái xác minh ngày 13/07/2026

- `PARTIALLY VERIFIED`: 879/879 regression, 14/14 role Playwright và Release build 0 warning/error đã pass.
- Chưa đạt tiêu chí PASS vì DB hiện tại thiếu 7 unique index (`WMS-G2-005`) và còn 2 ledger lịch sử có trạng thái trung gian không hợp lệ (`WMS-G2-006`).
- Không áp migration hoặc sửa dữ liệu trên DB hosting; hai mục này chỉ được xử lý ở checkpoint có phê duyệt riêng.

---

# GATE 3 — BUSINESS RULE VÀ END-TO-END WORKFLOW ( làm cho chuẩn doanh nghiệp lớn trên thế giới )

## G3.0. Core WMS Completeness Contract — `BLOCKER`

Không được kết luận hệ thống đủ core chỉ vì đã có menu hoặc CRUD. Danh mục dưới đây là baseline bắt buộc của WMS nội bộ; mục nào không áp dụng phải có quyết định `N/A` được phê duyệt, không được âm thầm bỏ qua.

### A. Cấu trúc tổ chức, người dùng và danh mục

- [ ] Kho, khu vực, dãy/kệ/tầng/ô hoặc mô hình vị trí tương đương.
- [ ] Vật tư/SKU, nhóm vật tư, thuộc tính, trạng thái hoạt động.
- [ ] Đơn vị tính cơ sở, đơn vị phụ, quy đổi và quy tắc làm tròn.
- [ ] Nhà cung cấp, bộ phận/đơn vị nhận, khách hàng hoặc đối tác liên quan.
- [ ] Owner/chủ hàng nếu kho quản lý nhiều chủ.
- [ ] Lot/batch, serial, NSX, HSD và trạng thái chất lượng nếu áp dụng.
- [ ] Người dùng, vai trò, quyền thao tác và phạm vi kho/owner.
- [ ] Mã nghiệp vụ unique, deactivate/soft-delete và referential rules.

### B. Nhập kho

- [ ] Yêu cầu/phiếu nhập và nguồn tham chiếu.
- [ ] Draft → submit → approve/reject → post → complete/cancel/reversal.
- [ ] Nhận đủ, thiếu, thừa và nhận từng phần.
- [ ] Kiểm tra vật tư, UOM, quantity, location, owner, lot, serial, NSX và HSD.
- [ ] Kiểm tra chất lượng, hold/quarantine/release/reject nếu áp dụng.
- [ ] Putaway vào đúng vị trí và không vượt constraint/capacity đã định nghĩa.
- [x] Form tạo phiếu nhập chỉ tự chọn vị trí cất hàng đã được API xác nhận; không cần bấm nút gợi ý và đã có xUnit/Playwright regression tại `AUTO_PUTAWAY_NO_CLICK_FIX_EVIDENCE_2026_07_13.md`.
- [ ] Import/OCR/manual entry không tạo dòng hoặc chứng từ trùng.
- [ ] Post nhập tạo tồn và ledger đúng một lần.

### C. Tồn kho và lưu trữ

- [ ] On-hand, reserved, available và in-transit có công thức/source of truth rõ.
- [ ] Tồn theo kho, vị trí, owner, item, lot, serial, status và UOM.
- [ ] Inventory ledger truy vết về voucher/source line.
- [ ] Reservation/allocation, consume, release và expiry của reservation.
- [ ] Di chuyển nội bộ giữa vị trí.
- [ ] Chuyển kho và trạng thái in-transit/receive discrepancy.
- [ ] Replenishment khu pick từ khu reserve nếu nghiệp vụ cần.
- [ ] Block/unblock, quarantine, damaged, expired và disposal.
- [ ] Không cho negative available hoặc cross-owner/cross-warehouse leakage.
- [ ] Stock inquiry, movement history và traceability chính xác.

### D. Xuất kho

- [ ] Yêu cầu/phiếu xuất và nguồn tham chiếu.
- [ ] Draft → submit → approve/reject → reserve → pick → pack/bàn giao → post/ship → cancel/reversal.
- [ ] FEFO/FIFO và loại trừ expired/blocked/quarantine/wrong-owner stock.
- [ ] Xuất đủ, thiếu, từng phần, short-pick, backorder hoặc xử lý thiếu hàng.
- [ ] Reservation không vượt available và được release đúng một lần khi hủy.
- [ ] Pick đúng location/lot/serial/UOM/quantity.
- [ ] Packing, staging, bàn giao/vận chuyển nếu thuộc quy trình.
- [ ] Post xuất trừ tồn và tạo ledger đúng một lần.

### E. Kiểm kê và điều chỉnh

- [ ] Kiểm kê toàn phần, theo vị trí, item, lot/serial hoặc cycle count.
- [ ] Scope/snapshot/freeze policy rõ và không tạo trùng kỳ.
- [ ] Count, recount, submit, approve/reject và chênh lệch.
- [ ] Adjustment tăng/giảm có reason, permission, approval và ledger.
- [ ] Khóa kỳ/chốt tồn/mở khóa nếu có.
- [ ] Giao dịch đồng thời trong lúc kiểm kê được xử lý theo policy.
- [ ] Reconcile tồn vật lý, bảng tồn và ledger.

### F. Trả hàng, chất lượng và ngoại lệ

- [ ] Trả nhà cung cấp, nhận hàng trả hoặc hoàn nội bộ theo phạm vi.
- [ ] Disposition: restock, quarantine, repair/rework, return hoặc scrap.
- [ ] Không tự restock trước quality decision.
- [ ] Hàng hết hạn/hư hỏng bị chặn khỏi allocation.
- [ ] Recall/trace forward-backward cho lot/serial nếu áp dụng.
- [ ] Negative stock, duplicate ledger, stuck reservation và failed job có workflow xử lý.
- [ ] Hủy/reversal không xóa lịch sử và không hoàn tồn hai lần.

### G. Chứng từ, dữ liệu, báo cáo và vận hành

- [ ] Import Excel có preview, validation từng dòng, duplicate protection và rollback policy.
- [ ] OCR có source document/hash/source line, human confirmation và manual fallback.
- [ ] Export Excel/PDF đúng filter, quyền, timezone, font và chống formula injection.
- [ ] Attachment/chứng từ có upload/download security và retention.
- [ ] Báo cáo tồn, nhập-xuất-tồn, movement, aging, expiry, discrepancy và audit.
- [ ] Dashboard/exception hiển thị số liệu reconcile với source.
- [ ] Notification/approval queue nếu quy trình yêu cầu.
- [ ] Audit trail trả lời được ai làm gì, khi nào, trước/sau và lý do.
- [ ] Backup/restore, health, log, request ID, monitoring và runbook cho nghiệp vụ core.

### H. Điều kiện “đủ một chức năng core”

Mỗi chức năng core chỉ được tick hoàn thành khi có đầy đủ:

- [ ] Business requirement và acceptance criteria.
- [ ] Database/schema/constraint/migration.
- [ ] Backend service/API và server-side validation.
- [ ] UI theo role, trạng thái loading/empty/error/validation.
- [ ] Permission, warehouse/owner scope và chống truy cập trực tiếp.
- [ ] State machine, transaction, concurrency và idempotency khi có ghi dữ liệu.
- [ ] Ledger/audit/log/correlation phù hợp.
- [ ] Dữ liệu seed cho happy, boundary, error và concurrent cases.
- [ ] Unit, integration, API và real E2E tests phù hợp.
- [ ] Playwright functional/visual trên route, role, viewport và state áp dụng.
- [ ] Data-quality/reconciliation sau thao tác.
- [ ] Tài liệu hướng dẫn, exception handling và runbook nếu ảnh hưởng vận hành.

Thiếu bất kỳ lớp bắt buộc nào ở trên thì chức năng chỉ là `PARTIAL`, không được tính hoàn thành hoặc dùng để báo core 100%.

## G3.1. Quy tắc tồn kho cốt lõi — `BLOCKER`

- [x] Không xuất hoặc reserve quá tồn khả dụng. Evidence: `GATE3_CORE_WORKFLOW_EVIDENCE_2026_07_14.md` (shared-stock, partial/non-partial và SQL Server atomic-post regressions).
- [x] FEFO chỉ chọn lô hợp lệ, không bị block/quarantine và chưa hết hạn. Evidence: `GATE3_CORE_WORKFLOW_EVIDENCE_2026_07_14.md` (near-expiry, quality-hold, owner/warehouse tests).
- [x] Có rule minimum remaining shelf life nếu nghiệp vụ yêu cầu. Policy hiện tại là 30 ngày và được kiểm tra ở release, wave, order streaming và post; evidence cùng file Gate 3.
- [ ] FIFO được dùng khi không quản lý HSD nhưng có ngày nhập/lô.
- [ ] Quy tắc fallback khi thiếu lot/date được định nghĩa rõ, không tự đoán âm thầm.
- [x] Serial chỉ được nhập/xuất một lần theo vòng đời hợp lệ. Evidence: serial lifecycle/reservation/post tests trong full regression 942/942 và hosting DQ `SERIAL_ACTIVE_DUPLICATE = 0`.
- [ ] UOM conversion áp dụng thống nhất khi nhập, xuất, tồn và báo cáo.
- [x] Quy tắc rounding được test ở giá trị biên. Evidence: `CreateInbound_ShouldRoundTransactionConversionAndBaseQtyAtDefinedBoundaries`, API/backorder/RMA targeted 7/7 và full regression 942/942 trong `GATE3_CORE_WORKFLOW_EVIDENCE_2026_07_14.md`.
- [ ] Partial receipt, partial issue và partial cancel cập nhật đúng số còn lại.
- [x] Không post phiếu rỗng hoặc line có quantity không hợp lệ. Evidence: `Inbound_ShouldRejectEmptyZeroAndUomMismatchWithoutStockMutation`, targeted 7/7, full regression 942/942 và hosting mismatch 0/0 trong evidence Gate 3.
- [x] Chốt kỳ không tạo trùng và khóa sửa/post dữ liệu thuộc kỳ đã khóa. Evidence: management red 0/2 -> green 2/2, relational unique-index 1/1, in-transaction post/release/quick-adjust checks và final period-lock suite 9/9 trong `GATE3_CORE_WORKFLOW_EVIDENCE_2026_07_14.md`.
- [x] Mở khóa kỳ nếu có phải giới hạn quyền và được audit. Evidence: Admin/Manager + `ReportView` + antiforgery metadata; `PERIOD_LOCK_SET/UPDATE/REOPEN/SUPERSEDE/CLEAR` audit assertions; final period-lock suite 9/9.

## G3.2. Nhập kho end-to-end — `BLOCKER`

- [ ] Tạo draft → thêm line → validate → duyệt → post → putaway/ghi vị trí.
- [ ] Kiểm tra lot, serial, NSX, HSD và UOM.
- [ ] Post tăng đúng tồn, tạo đúng ledger và không tạo reservation thừa.
- [ ] Partial receipt giữ đúng số lượng còn lại.
- [ ] Reject/hủy trước post không thay đổi tồn.
- [ ] Reversal sau post tạo giao dịch đảo đúng và có audit.

## G3.3. Xuất kho end-to-end — `BLOCKER`

- [ ] Tạo draft → reserve → chọn lô FEFO/FIFO → pick → duyệt → post/ship.
- [x] Tồn khả dụng giảm đúng khi reserve và tồn vật lý giảm đúng khi post theo mô hình đã chọn. Evidence: `gate3-core-business-deep-20260714.trx` và `gate3-sqlserver-regression-green-20260714.trx`.
- [x] Release/cancel reservation trả lại available quantity đúng một lần. Evidence: cancellation/serial-reservation lifecycle và repeated-command idempotency trong Gate 3 regression.
- [x] Partial pick/ship giữ đúng số còn lại. Evidence: partial/non-partial 16/16, partial-focused 9/9 gồm exact cancel và SQL Server dùng một lần 1/1 trong `GATE3_CORE_WORKFLOW_EVIDENCE_2026_07_14.md`.
- [x] Không chọn hàng expired, blocked hoặc sai owner/kho. Evidence: FEFO/hold/cross-owner/cross-warehouse matrix trong `GATE3_CORE_WORKFLOW_EVIDENCE_2026_07_14.md`.
- [x] Reversal/return tạo đúng ledger và trạng thái. Evidence: return/reversal state 6/6 và SQL Server post-reversal 1/1 trong `GATE3_CORE_WORKFLOW_EVIDENCE_2026_07_14.md`.

## G3.4. Chuyển kho/vị trí — `BLOCKER`

- [x] Chuyển cùng kho khác vị trí. Evidence: movement/transfer regression và no-device RF pass.
- [x] Chuyển khác kho nếu hệ thống hỗ trợ. Evidence: transfer warehouse-scope/lot-expiry/serial tests 5/5.
- [x] Không cộng đích khi chưa trừ nguồn thành công. Evidence: destination-topology rollback và atomic transfer tests 5/5.
- [ ] Hàng in-transit được mô hình hóa rõ nếu quy trình không atomic tức thời.
- [ ] Hủy/receive thiếu/thừa có workflow và audit rõ.

## G3.5. Kiểm kê và điều chỉnh — `BLOCKER`

- [ ] Tạo đợt kiểm kê đúng phạm vi kho/vị trí/item.
- [ ] Snapshot/chốt số hệ thống không bị tạo trùng kỳ.
- [x] Chênh lệch tạo adjustment/ledger đúng dấu và đúng quantity. Evidence: `StockCountWorkflow_ShouldStartSubmitRecountAndApproveAdjustment` và stock-count regression 278/278.
- [ ] Post giao dịch xảy ra trong lúc kiểm kê được xử lý theo policy rõ.
- [ ] Recount, approve và reject đúng vai trò.
- [ ] Không sửa trực tiếp số tồn để làm khớp kiểm kê.

## G3.6. Trả hàng, quarantine và hàng hết hạn — `MUST`

- [ ] Trả nhà cung cấp hoặc khách hàng theo nghiệp vụ hệ thống hỗ trợ.
- [x] Hàng trả về không tự động available nếu cần kiểm tra chất lượng. Evidence: MVC/API customer-return QC regressions 3/3 và focused suite 15/15.
- [ ] Block/unblock/quarantine có quyền và audit.
- [x] Hàng hết hạn vẫn có thể được lưu nhưng không được cấp phát trái policy. Evidence: FEFO/minimum-shelf-life release, wave, streaming và post regressions.
- [ ] Tiêu hủy/reversal nếu có phải tạo ledger rõ ràng.

## G3.7. Bộ test tự động bắt buộc — `BLOCKER`

- [x] Unit tests cho invariant, calculation, state transition và permission policy. Evidence hiện tại: full xUnit 942/942 và các focused Gate 3 TRX trong `artifacts/full-audit/test-results`.
- [x] Integration tests dùng database thật cùng engine với production/staging. Evidence: disposable SQL Server `gate3-sqlserver-regression-green-20260714.trx` 3/3, `gate3-sqlserver-backorder-quantity-20260714.trx` 3/3 và `gate3-demo-seed-sqlserver-20260714.trx` 1/1; không dùng database hosting để ghi test.
- [x] API tests cho authorization, validation, idempotency và error contract. Evidence: `gate3-api-auth-validation-idempotency-20260714.trx` 14/14 trong `artifacts/full-audit/test-results`.
- [ ] E2E tests cho happy path và critical failure path.
- [ ] Test boundary: quantity nhỏ nhất/lớn nhất, ngày hết hạn hôm nay, cuối tháng/kỳ và rounding.
- [ ] Test chạy độc lập, không phụ thuộc thứ tự và tự dọn dữ liệu.

### Tiêu chí nghiệm thu Gate 3

- Tất cả critical E2E scenario pass.
- Không còn lỗi làm sai quantity, available quantity, reservation hoặc ledger.
- Business exception có mã lỗi ổn định và thông điệp tiếng Việt dễ hiểu.

---

# GATE 4 — NHẬP LIỆU, IMPORT, OCR VÀ EXPORT

## G4.1. Nhập liệu thủ công và fallback — `MUST`

- [x] Tất cả nghiệp vụ cốt lõi vẫn thực hiện được khi OCR hoặc dịch vụ ngoài ngừng hoạt động.
- [x] Lookup vật tư/kho/vị trí áp dụng permission và data scope.
- [x] Form không cho submit trùng khi đang xử lý.
- [x] Validation client và server cho kết quả nhất quán.
- [x] Dữ liệu người dùng đã nhập không bị mất vô lý khi lỗi validation hoặc lỗi tạm thời.

## G4.2. Import Excel hardening — `MUST`

- [x] Có template và version template rõ ràng.
- [x] Test sai format, file hỏng và sheet sai.
- [x] Test thiếu/thừa cột bắt buộc.
- [x] Test dòng trống và khoảng trắng.
- [x] Test mã vật tư/kho/vị trí/lot không tồn tại hoặc ngoài scope.
- [x] Test UOM sai hoặc thiếu conversion.
- [x] Test quantity âm, bằng 0, không phải số, overflow và sai decimal separator.
- [x] Test duplicate trong file và duplicate với dữ liệu đã có.
- [x] Có preview/dry-run trước khi ghi đối với import làm thay đổi tồn/chứng từ.
- [x] Chính sách all-or-nothing hoặc partial success được hiển thị rõ; không được ngầm partial.
- [x] Lỗi trả theo row, column, mã lỗi và hướng sửa; không trả stack trace.
- [x] Import cùng file/request lần hai không tạo dữ liệu trùng.
- [x] Test tới `IMPORT_MAX_ROWS` và có giới hạn kích thước file.

## G4.3. Chuẩn hóa upload và OCR — `MUST` nếu OCR đang bật

- [x] Kiểm tra file security theo G2.4 trước khi gửi OCR.
- [x] Lưu source document ID, file hash, provider và thời điểm xử lý.
- [x] Không chỉ dùng số chứng từ để chống trùng; kết hợp supplier/owner và fingerprint phù hợp.
- [x] Phân loại rõ: hợp lệ, cần kiểm tra, không áp dụng và provider failure.
- [x] Không cho chọn chứng từ không có số và không có dòng áp dụng.
- [x] Nhiều chứng từ khác số trong một upload phải được tách hoặc bắt buộc chọn/xác nhận rõ.
- [x] Dòng duplicate không được cộng dồn âm thầm.
- [x] OCR không tự ghi đè form trước khi người dùng xác nhận.
- [x] Mỗi dòng OCR lưu source document/source line để truy vết.
- [x] Có hành động bỏ dòng OCR nhưng giữ dòng nhập tay.
- [x] Thay/thêm/xem trước chứng từ có hành vi rõ và audit.
- [x] Timeout, retry có backoff, rate limit và fallback được xử lý có giới hạn.
- [x] Provider lỗi nhưng fallback thành công chỉ hiện cảnh báo mềm.
- [x] Không log raw document hoặc dữ liệu nhạy cảm quá mức cần thiết.

## G4.4. Export Excel/PDF — `MUST`

- [x] Tên file có nghiệp vụ, filter và timestamp phù hợp.
- [x] Font tiếng Việt và timezone đúng.
- [x] Số lượng, tiền, ngày và phần trăm đúng format/precision.
- [x] Dữ liệu khớp filter UI và data scope của user.
- [x] Role không có quyền tài chính không nhận cột tài chính qua export/API.
- [x] Neutralize spreadsheet formula injection.
- [x] Export rỗng có thông báo hợp lý.
- [x] File mở được bằng Excel/PDF reader mục tiêu.
- [ ] Dữ liệu dài không làm vỡ cột hoặc mất nội dung quan trọng.
- [ ] Export lớn không giữ toàn bộ dữ liệu trong memory nếu có thể stream/chạy nền.
- [x] Test tới `EXPORT_MAX_ROWS` và ghi thời gian/file size.

### Tiêu chí nghiệm thu Gate 4

- Tắt OCR provider vẫn hoàn thành được nghiệp vụ cốt lõi bằng nhập tay/Excel.
- Import lỗi không làm thay đổi dữ liệu ngoài chính sách đã công bố.
- Không có duplicate do import/OCR retry.
- Export không lộ dữ liệu ngoài quyền và không chứa công thức nguy hiểm từ input.

### Evidence Gate 4 — 2026-07-15

- Trạng thái: `PARTIALLY VERIFIED`.
- Đã xác minh: G4.1 `5/5`, G4.2 `13/13`, G4.3 `14/14`, G4.4 `9/11`.
- Test: targeted .NET `74/74`, Playwright chức năng `7/7`, full regression `980/980`, Release build `0 warning/0 error`.
- Benchmark export: 5.000 dòng, 2.379 ms, 115.835 byte, mở lại thành công.
- Evidence: `artifacts/full-audit/GATE4_IMPORT_OCR_EXPORT_EVIDENCE_2026_07_15.md`.
- Còn mở: kiểm thử dữ liệu dài trên toàn bộ mẫu export và streaming/background export; hai checkbox tương ứng giữ nguyên `[ ]`.

---

# GATE 5 — RELEASE, MIGRATION, BACKUP VÀ VẬN HÀNH

## G5.1. CI quality gate — `MUST`

- [ ] Restore dependency, build và test tự động.
- [ ] Unit/integration/API/E2E critical suite pass.
- [ ] Migration/model drift check pass.
- [ ] Dependency vulnerability scan pass theo policy.
- [ ] Secret scan không phát hiện credential thật.
- [ ] Artifact ghi commit SHA, build number và test result.
- [ ] Không deploy nếu gate `BLOCKER` fail.

## G5.2. Migration safety — `BLOCKER`

- [ ] Migration chạy thành công trên database sạch.
- [ ] Migration chạy thành công trên bản sao database hiện tại có dữ liệu.
- [ ] Đo thời gian lock/downtime của migration lớn.
- [ ] Backfill dữ liệu có thể resume hoặc chạy lại an toàn.
- [ ] Migration không xóa/thu hẹp dữ liệu không tương thích nếu chưa có kế hoạch chuyển đổi.
- [ ] Có pre-check dung lượng, constraint conflict và dữ liệu không hợp lệ.
- [ ] Có rollback strategy hoặc forward-fix strategy được diễn tập.
- [ ] App version và schema version tương thích trong cửa sổ deploy.

## G5.3. Deployment và rollback — `BLOCKER`

- [ ] Viết release checklist trước, trong và sau deploy.
- [ ] Backup/checkpoint trước thay đổi rủi ro cao.
- [ ] Tách cấu hình theo environment; không hard-code secret.
- [ ] Chạy readiness check trước khi nhận traffic.
- [ ] Chạy smoke test login, quyền, xem tồn, tạo draft và một workflow an toàn sau deploy.
- [ ] Xác định rollback trigger, người quyết định và thời gian tối đa chờ.
- [ ] Rollback không dùng thao tác phá hủy dữ liệu ngoài kế hoạch đã duyệt.
- [ ] Ghi release note và known issues.

## G5.4. Backup, restore và disaster recovery — `BLOCKER`

- [ ] Có lịch backup, retention, mã hóa và quyền truy cập phù hợp.
- [ ] Theo dõi backup success/failure; không chỉ tin job đã được cấu hình.
- [ ] Restore bản backup gần nhất sang staging hoặc môi trường cô lập.
- [ ] Chạy migration cần thiết và data-quality audit sau restore.
- [ ] Đo RPO/RTO thực tế và so với mục tiêu.
- [ ] Diễn tập restore tối thiểu một lần trước sign-off.
- [ ] Ghi rõ quy trình khi backup hỏng hoặc restore thất bại.

## G5.5. Logging, metrics và alerting — `MUST`

- [ ] Structured log có timestamp, level, environment, app version và correlation/request ID.
- [ ] Log user/action/route/entity khi lỗi nhưng không log secret/password/token.
- [ ] Redact dữ liệu nhạy cảm và nội dung file không cần thiết.
- [ ] Log thời gian xử lý endpoint, job và provider quan trọng.
- [ ] Có metrics cho error rate, latency, job failure, OCR failure và database health.
- [ ] Có alert có người nhận cho database down, backup fail, job fail liên tục và error spike.
- [ ] Định nghĩa log retention và quyền xem log.
- [ ] Một request lỗi có thể lần từ UI/request ID đến server log và DB action liên quan.

## G5.6. Health/readiness check — `MUST`

- [ ] Kiểm tra database connectivity.
- [ ] Kiểm tra schema/migration version.
- [ ] Kiểm tra background job heartbeat.
- [ ] Kiểm tra OCR/config presence nhưng không gọi tốn phí quá mức.
- [ ] Kiểm tra storage/disk/log path nếu phù hợp kiến trúc.
- [ ] Hiện app version/build time.
- [ ] Phân biệt liveness và readiness nếu hạ tầng hỗ trợ.
- [ ] Trang chi tiết chỉ cho Admin/ops; endpoint public không lộ internals hoặc secret.

## G5.7. Audit trail và governance — `MUST`

- [ ] Audit tạo/sửa/duyệt/post/hủy/reversal chứng từ.
- [ ] Audit sửa line, quantity, lot, serial, HSD và vị trí.
- [ ] Audit thay đổi item, warehouse, location, owner và UOM.
- [ ] Audit thay đổi user, role, permission và data scope.
- [ ] Audit khóa/mở kỳ, block/unblock stock và adjustment.
- [ ] Ghi before/after cho trường quan trọng, actor, timestamp, reason và correlation ID.
- [ ] Audit không bị user nghiệp vụ sửa/xóa.
- [ ] Admin lọc theo user, action, entity, kho, thời gian và correlation ID.

## G5.8. Runbook vận hành — `MUST`

- [ ] Backup và restore.
- [ ] Deploy, migration và rollback/forward-fix.
- [ ] OCR/provider outage và rate limit.
- [ ] Lệch tồn, duplicate ledger hoặc negative stock.
- [ ] Import/export lỗi.
- [ ] User mất quyền, khóa tài khoản hoặc quên MFA.
- [ ] Background job lỗi hoặc chạy trùng.
- [ ] Disk/log đầy và database mất kết nối.
- [ ] Security incident và nghi ngờ lộ secret.
- [ ] Mỗi runbook có điều kiện kích hoạt, bước xử lý, xác minh sau xử lý và escalation owner.

### Bằng chứng Gate 5

- `artifacts/release/`
- `artifacts/migrations/`
- `artifacts/backup-restore/`
- `docs/runbooks/`
- Ảnh/log diễn tập restore và smoke test sau deploy.

---

# GATE 6 — PERFORMANCE, RELIABILITY VÀ UI QUALITY

## G6.1. Performance contract — `MUST`

Ngưỡng mặc định dưới đây được dùng nếu Product Owner chưa phê duyệt ngưỡng khác:

| Luồng | Ngưỡng mặc định trên staging đã warm-up |
|---|---|
| Login và tải menu theo quyền | p95 ≤ 2 giây |
| Tra cứu/filter tồn kho có paging | p95 ≤ 2 giây |
| Mở lịch sử nhập/xuất có paging | p95 ≤ 3 giây |
| Post phiếu không gồm OCR/file upload | p95 ≤ 3 giây |
| Dashboard quản lý | p95 ≤ 3 giây |
| Export 10.000 dòng | ≤ 30 giây hoặc chuyển sang background job |
| Error rate hệ thống ở smoke/load test | < 1%, và 0 lỗi integrity |

Nếu thay đổi ngưỡng phải ghi rõ lý do, môi trường và người phê duyệt.

## G6.2. Performance smoke/load không cần k6 — `MUST`

- [x] Dùng dotnet/PowerShell script có concurrency control cho API load smoke.
- [x] Dùng Playwright cho browser journey timing; không xem vòng lặp UI đơn thuần là load test backend đầy đủ.
- [x] Đo p50, p95, p99, throughput và error rate.
- [ ] Test ở `U_TEST` với data profile đã chốt.
- [ ] Test login/menu, tồn kho, lịch sử, dashboard, post nhập/xuất an toàn và báo cáo.
- [ ] Đo import/export theo giới hạn công bố.
- [x] Tách cold start và warm result.
- [ ] Sau test chạy data-quality audit để chứng minh không sai tồn.

## G6.3. Query profiling và index review — `MUST`

- [ ] Profile query tồn kho, stock movement, lịch sử, dashboard, inventory map và báo cáo.
- [ ] Xem execution plan/query plan trên data profile gần thực tế.
- [ ] Index phục vụ filter warehouse, owner, item, location, lot, status và date.
- [x] Không tạo index trùng hoặc quá nhiều làm chậm write path mà không có bằng chứng.
- [ ] Read-only query dùng no-tracking khi phù hợp.
- [ ] Không có N+1 ở view/report lớn.
- [ ] Aggregate, filter và paging ở database thay vì load toàn bộ vào memory.
- [ ] Có timeout và cancellation phù hợp cho query dài.

## G6.4. Background job và provider resilience — `MUST`

- [ ] Job có idempotency và lock phù hợp để không chạy trùng.
- [x] Retry có giới hạn, exponential backoff và phân loại transient/permanent error.
- [x] Job fail nhiều lần chuyển trạng thái cần xử lý/dead-letter tương đương.
- [ ] Job restart không làm mất hoặc nhân đôi công việc.
- [ ] OCR provider có timeout, rate-limit handling, fallback và circuit-breaker nếu phù hợp.
- [x] External outage không chặn nhập liệu thủ công cốt lõi.

## G6.5. UI/UX Cross-Device Production Gate — Desktop, Laptop, Tablet và Mobile — `BLOCKER`

Mục tiêu là giao diện đúng chức năng, rõ ràng, responsive, accessible và không có lỗi đã biết trên toàn bộ phạm vi thiết bị/browser công bố. Chuẩn nền:

- [WCAG 2.2](https://www.w3.org/WAI/standards-guidelines/wcag/) Level AA.
- [Reflow](https://www.w3.org/WAI/WCAG21/Understanding/reflow.html): nội dung phải đọc và thao tác được mà không phải cuộn hai chiều, trừ vùng dữ liệu thật sự cần như bảng/sơ đồ.
- [Target Size Minimum](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html): tối thiểu 24×24 CSS px hoặc spacing hợp lệ; hệ thống kho ưu tiên 44×44 CSS px cho control chạm quan trọng.
- Mobile web không có một chuẩn accessibility riêng; phải áp dụng WCAG cùng responsive/touch/mobile-specific tests.

### G6.5.1. Quy tắc sử dụng ảnh tham chiếu — không suy diễn lỗi

Các ảnh chủ dự án cung cấp là **trạng thái giao diện desktop**: thanh menu icon thu gọn, sidebar desktop mở rộng, các flyout menu desktop và trang tổng quát. Ảnh bị crop hẹp hoặc chụp một vùng màn hình **không phải** bằng chứng rằng ứng dụng đang chạy ở viewport mobile.

- [x] Gắn nhãn đúng ảnh tham chiếu là desktop; không tự tạo finding mobile từ ảnh desktop/crop.
- [x] Dùng ảnh để lập danh sách trạng thái desktop cần regression: icon rail thu gọn, sidebar mở rộng, flyout Nhập kho, Tồn kho, Vận chuyển, Báo cáo, Danh mục, Hệ thống, dashboard, quick workspace và KPI card.
- [x] Không kết luận một biểu hiện là bug chỉ từ screenshot; phải tái hiện trên build hiện tại và xác nhận viewport, DPR, browser, zoom, role, route, menu state và build/version.
- [ ] Kiểm tra flyout desktop dài tại 1280×720, 1366×768, 1440×900, 1920×1080 và zoom 125–150%: không clip item, không tạo vùng không thể truy cập và có scroll hợp lệ khi cần.
- [ ] Kiểm tra flyout desktop mở/đóng đúng bằng click, click ngoài, Escape; focus đi vào hợp lý và trả về trigger sau khi đóng.
- [ ] Kiểm tra collapsed icon rail có tooltip, accessible name, keyboard support, active/hover/focus state và hit target rõ ràng.
- [ ] Kiểm tra sidebar mở rộng không làm mất route, sai active state, tràn chữ, nhảy layout hoặc che nội dung ngoài thiết kế.
- [ ] Kiểm tra grid/card desktop và laptop tại viewport/zoom hỗ trợ: không ép chữ, overlap, clipping hoặc lệch hàng.
- [ ] Kiểm tra title, description, CTA, badge và trạng thái trên quick card có hierarchy và scanability rõ ràng.
- [ ] Đối chiếu KPI “Tổng giá trị tồn kho” với currency, phạm vi kho/chủ hàng và as-of-time.
- [ ] Đối chiếu KPI “Tổng số vật tư” với định nghĩa SKU/item/quantity đã duyệt.
- [ ] Đối chiếu KPI “Phiếu phát sinh hôm nay” với định nghĩa created/due/posted/completed today.
- [ ] Đối chiếu KPI “Tỷ lệ đáp ứng giữ chỗ” với formula, period, numerator, denominator và sample size.
- [ ] Reconcile badge “7 mục cần theo dõi” với số item/card/drill-down chi tiết.
- [ ] Lớp “To exit full screen...” được nhận diện là UI trình duyệt, không ghi nhầm thành bug ứng dụng.
- [x] Chỉ ghi finding sau khi reproduce; chỉ đóng sau khi có root cause, fix, regression và before/after Playwright evidence.

Tablet và mobile vẫn là phạm vi kiểm thử bắt buộc ở các mục tiếp theo, nhưng phải được kiểm tra bằng viewport/thiết bị thật hoặc emulation được xác nhận; không suy diễn trạng thái hay lỗi mobile từ bộ ảnh desktop này.

### G6.5.2. UI file inventory và impact analysis trước khi sửa — `BLOCKER`

- [ ] Inventory 100% layout, Razor/view, partial/component, CSS, JS, asset, font và localization first-party.
- [ ] Map route → layout → component/partial → stylesheet/script → API/data.
- [ ] Xác định global CSS token, reset, breakpoint, container, sidebar/topbar và overlay system.
- [ ] Xác định selector/class dùng chung trước khi sửa CSS.
- [ ] Xác định component/page bị ảnh hưởng bởi từng thay đổi breakpoint hoặc global style.
- [ ] Tìm dead/duplicate CSS/JS/view nhưng không tự xóa khi chưa chứng minh không còn consumer.
- [ ] Đọc Git diff và không ghi đè thay đổi giao diện của người dùng.
- [x] Tạo `docs/audit/UI_FILE_IMPACT_MAP.md`.
- [x] Tạo `docs/audit/UI_BREAKPOINT_AND_REFLOW_CONTRACT.md`.
- [x] Tạo `artifacts/ui-cross-device/UI_ROUTE_ROLE_STATE_VIEWPORT_MATRIX.csv`.
- [x] Không dùng một global CSS override để “vá” ảnh nếu chưa regression toàn bộ consumer.

### G6.5.3. Support matrix bắt buộc

#### Desktop/laptop

- [x] 1280×720.
- [x] 1366×768.
- [x] 1440×900.
- [x] 1536×864.
- [x] 1920×1080.
- [ ] 2560×1440 nếu hệ thống công bố hỗ trợ màn hình lớn.

#### Tablet

- [x] 768×1024 portrait.
- [x] 820×1180 portrait.
- [x] 1024×768 landscape.
- [x] 1180×820 landscape.

#### Mobile

- [x] 320×568 minimum/reflow.
- [x] 360×800.
- [x] 375×812.
- [x] 390×844.
- [x] 412×915.
- [x] 430×932.
- [x] Portrait và landscape cho workflow mobile quan trọng.

#### Zoom, scaling và browser

- [ ] Zoom 80% kiểm tra màn hình mật độ cao nếu support.
- [ ] Zoom 100%, 110%, 125%, 150% trên toàn route chính.
- [ ] Zoom 200% cho toàn workflow quan trọng.
- [ ] Reflow 320 CSS px/400% cho accessibility scope.
- [ ] Windows display scaling 100%, 125%, 150% khi có môi trường.
- [x] Device pixel ratio 1 và 2 khi screenshot/render có khác biệt.
- [x] Chromium/Chrome.
- [ ] Microsoft Edge nếu là browser doanh nghiệp mục tiêu.
- [ ] Firefox và WebKit/Safari nếu support matrix công bố.
- [ ] Không dùng user-agent sniffing khi media/container query đáp ứng được.

### G6.5.4. Layout, grid và reflow

- [ ] Không có body-level horizontal scroll ở viewport được hỗ trợ.
- [ ] Nội dung không bị cắt bên trái/phải do fixed/min-width.
- [ ] Main content offset thay đổi đúng theo sidebar expanded/collapsed/hidden.
- [ ] Container max-width, gutter và whitespace phù hợp ở màn hình lớn.
- [ ] Grid dùng breakpoint/minmax hợp lý; card không bị ép chữ hoặc kéo quá rộng.
- [ ] Card chuyển 6→3→2→1 cột theo chiều rộng thực, không dựa vào thiết bị đoán mò.
- [ ] Header/title/action reflow; primary action không biến mất.
- [ ] Breadcrumb có truncate/wrap hợp lý, không đẩy action khỏi viewport.
- [ ] Section order trên mobile ưu tiên công việc/action trước phần trang trí.
- [ ] Fixed/sticky element không che content, anchor hoặc validation message.
- [ ] Safe-area inset được xử lý trên thiết bị có notch nếu chạy full-screen/PWA.
- [ ] Virtual keyboard không che field/action cuối form.
- [ ] Orientation change không mất state hoặc làm layout vỡ.
- [ ] Không gây cumulative layout shift lớn khi font/data/image tải.

### G6.5.5. Sidebar, drawer, flyout và navigation

- [ ] Desktop expanded sidebar có label rõ, active state và scroll độc lập.
- [ ] Desktop collapsed rail có tooltip, accessible name và keyboard access.
- [ ] Flyout định vị theo anchor, collision-aware, flip/shift khi gần mép viewport.
- [ ] Flyout có max-width theo viewport và max-height theo available space.
- [ ] Menu dài cuộn nội bộ nhưng item đầu/cuối luôn tiếp cận được.
- [ ] Flyout không bị topbar, viewport hoặc browser zoom cắt.
- [ ] Flyout đóng bằng Escape, outside click và chọn item; focus trở về trigger.
- [ ] Trigger có `aria-expanded`, `aria-controls`, `aria-haspopup` phù hợp.
- [ ] Arrow keys/Home/End/Enter/Space hoạt động theo pattern menu.
- [x] Mobile dùng drawer/sheet rõ ràng, không giữ đồng thời icon rail gây hai lớp nav.
- [ ] Mobile drawer có header/title, close/back, backdrop và body scroll lock.
- [ ] Mobile drawer không vượt chiều rộng viewport và xử lý safe area.
- [ ] Mobile drawer cho phép cuộn tới item cuối, không tạo nested-scroll trap.
- [ ] Nhóm menu theo nghiệp vụ; menu theo role không chứa chức năng không liên quan.
- [ ] Không làm menu quá dài khi có thể progressive disclosure/search/pinned actions.
- [ ] Active route/parent group vẫn rõ sau navigation/reload.
- [ ] Deep link/back/forward giữ navigation state hợp lý.

### G6.5.6. Topbar, global search, profile và quick actions

- [ ] Topbar không overflow ở tên user/warehouse dài.
- [ ] Search co giãn hợp lý; mobile có nút mở search thay vì mất hoàn toàn.
- [ ] Keyboard shortcut không che placeholder hoặc cản input.
- [ ] Notification/help/favorite/profile có accessible label và touch target.
- [ ] Profile menu không vượt viewport và không lộ action trái quyền.
- [x] Primary actions của trang stack/wrap/overflow menu hợp lý trên mobile.
- [ ] Action quan trọng vẫn tiếp cận được bằng keyboard và touch.
- [ ] Header sticky không che toast, modal, anchor hoặc first focus.
- [ ] Loading/session-expired/offline state của topbar rõ.

### G6.5.7. Dashboard, card, KPI và chart

- [ ] KPI có title, value, unit/currency, scope, period và as-of-time.
- [ ] Số lớn có separator/abbreviation/tooltip không gây hiểu nhầm.
- [ ] Card title và description có hierarchy, không dính thành một câu khó quét.
- [ ] Card đồng chiều cao chỉ khi không cắt nội dung; không ép height giả.
- [ ] Badge/count reconcile với detail.
- [ ] Mobile ưu tiên work queue/critical KPI; chart phụ xuống dưới.
- [ ] Chart responsive, label/legend/tooltip không cắt.
- [ ] Chart có table/text alternative và không chỉ dùng màu.
- [ ] Empty/zero không bị hiểu nhầm là lỗi tải.
- [ ] Skeleton có kích thước gần nội dung thật để tránh layout shift.
- [ ] Drill-down target đủ lớn và không nested clickable conflict.

### G6.5.8. Table, list, pagination và bulk action

- [ ] Table desktop có header, sort, filter, paging, sticky phù hợp.
- [x] Wide table scroll trong container, không làm body overflow.
- [ ] Mobile dùng priority columns, card/detail disclosure hoặc contained scroll có affordance.
- [ ] Không ẩn cột nghiệp vụ bắt buộc mà không có cách xem chi tiết.
- [ ] Header/cell align đúng kiểu text/number/date/status/action.
- [ ] Long code/name wrap/truncate có tooltip/accessibility.
- [ ] Row action menu không bị viewport/table container cắt.
- [ ] Bulk-selection bar không che pagination/content.
- [ ] Empty/loading/error/partial data states.
- [ ] Pagination và page-size usable bằng keyboard/touch.
- [ ] Responsive table vẫn giữ permission và row identity đúng.

### G6.5.9. Form và thao tác nhập liệu

- [ ] Label luôn gắn với field; placeholder không thay label.
- [ ] Required/optional, format, unit và help text rõ.
- [ ] Input/select/autocomplete/date picker/file upload responsive.
- [ ] Mobile keyboard type/inputmode đúng cho số, điện thoại, email, scan.
- [ ] Validation đặt gần field, summary/focus tới lỗi đầu và không bị sticky header che.
- [ ] Error text không làm grid/modal vỡ.
- [ ] Submit/cancel/action bar luôn tiếp cận được.
- [x] Double-submit bị chặn nhưng retry hợp lệ vẫn làm được.
- [ ] Spinner không kẹt; disabled state có lý do.
- [ ] Unsaved-change warning và back navigation đúng.
- [ ] Date/time/UOM/decimal/locale hiển thị nhất quán.
- [ ] Scanner input không kích hoạt hai lần do Enter/keydown.

### G6.5.10. Modal, drawer, popover, tooltip và toast

- [ ] Overlay nằm trong viewport, có max-size và internal scroll.
- [ ] Không có nested scroll khiến user không thoát được.
- [ ] Focus trap, initial focus, Escape, close và return focus đúng.
- [ ] Background bị inert/không tab được khi modal mở.
- [ ] Popover/tooltip không bị clip bởi overflow container.
- [ ] Tooltip không chứa action chỉ dùng hover.
- [ ] Confirm nguy hiểm ghi rõ đối tượng/hậu quả.
- [x] Toast không che topbar, action, validation hoặc nhau.
- [ ] Toast có đủ thời gian đọc và vùng thông báo accessible.
- [ ] Mobile modal chuyển thành sheet/full-screen khi nội dung dài nếu phù hợp.
- [ ] Z-index token nhất quán; không chạy đua giá trị tùy ý.

### G6.5.11. Nội dung, typography, màu sắc và localization

- [ ] Không typo, sai dấu, mojibake hoặc encoding lỗi.
- [ ] Thuật ngữ WMS tiếng Việt thống nhất giữa menu, form, report và error.
- [ ] Font fallback hỗ trợ đầy đủ tiếng Việt.
- [ ] Body text/readability/line-height hợp lý.
- [ ] Heading hierarchy đúng, không chọn heading chỉ để tăng cỡ chữ.
- [ ] Contrast text/control/focus đạt WCAG target.
- [ ] Trạng thái không chỉ biểu đạt bằng màu/icon.
- [ ] Icon nhất quán, đúng nghĩa và có accessible name khi không có text.
- [ ] Text dài, tên user/kho/vật tư/mã chứng từ max-length không phá layout.
- [ ] 200% text zoom không mất content/function.
- [ ] Currency, date, number, decimal, timezone và UOM đúng locale/scope.

### G6.5.12. Accessibility và interaction

- [ ] WCAG 2.2 AA automated scan trên mọi template/page type.
- [ ] Keyboard-only hoàn thành được workflow core.
- [ ] Tab order theo visual/logical order.
- [ ] Focus visible rõ; không bị overlay/sticky element che.
- [ ] Skip link/main landmark/nav/header/footer hợp lý.
- [ ] Form, table, status, alert, progress và dialog có semantics/ARIA đúng.
- [ ] Dynamic update được announce khi cần, không gây spam.
- [ ] Touch target tối thiểu chuẩn; primary warehouse control ưu tiên 44×44.
- [ ] Pointer gesture có lựa chọn thay thế.
- [ ] Reduced motion và high-contrast/forced-colors không phá chức năng nếu support.
- [ ] Screen-reader smoke cho login, navigation, form core, table và modal.

### G6.5.13. State, edge case và resilience

- [ ] Normal, empty, loading, skeleton và success.
- [ ] Validation/business/server/network/timeout/offline error.
- [ ] Partial widget failure và stale data.
- [ ] 401/403/404/500 có recovery action.
- [ ] Session expiry giữa form/action.
- [ ] Slow 3G/CPU throttling cho mobile workflow nếu support.
- [ ] Dữ liệu 0, 1, nhiều, cực dài, Unicode và ký tự đặc biệt.
- [ ] Permission thiếu, feature flag off và module N/A.
- [ ] Concurrent update/conflict không làm UI nói thành công giả.
- [ ] Refresh/back/forward/deep-link giữ hoặc khôi phục state đúng.

### G6.5.14. Playwright automated visual/functional matrix — `BLOCKER`

- [ ] Inventory 100% route/page/component first-party có UI.
- [ ] Ma trận route × role × state × viewport × browser đạt coverage đã công bố.
- [ ] Screenshot full-page và component cho canonical states.
- [x] Bắt `console.error`, `pageerror`, `requestfailed` và response lỗi không mong đợi.
- [x] Assert `documentElement.scrollWidth <= clientWidth` trừ whitelist component.
- [x] Detect element bounding box vượt viewport.
- [x] Detect overlap cho critical controls/header/sidebar/modal/action.
- [ ] Detect clipped/zero-size/hidden interactive element.
- [ ] Detect duplicate ID, missing accessible name/label và invalid focus.
- [ ] Detect menu/modal item đầu/cuối không tiếp cận được.
- [ ] Assert drawer/flyout close, body scroll lock và focus return.
- [ ] Assert row/card/action drill-down đúng route/context.
- [x] Mọi visual diff được mở bằng công cụ xem ảnh và manual-review.
- [x] Không update baseline nếu chưa giải thích diff và kiểm tra regression.
- [x] Traces/video/network logs lưu cho failure.

### G6.5.15. Điều kiện UI 100%

Chỉ được báo UI/UX 100% khi:

- [ ] UI route inventory = 100%.
- [ ] Role/state/viewport coverage theo support matrix = 100%.
- [ ] 0 unresolved horizontal overflow, clipping, overlap hoặc unreachable control.
- [x] 0 unresolved visual diff.
- [ ] 0 unexpected console/page/network error.
- [ ] 0 typo, lỗi dấu, raw technical label hoặc localization defect.
- [ ] 0 known accessibility violation trong target WCAG scope.
- [ ] 0 broken link/asset/icon/font.
- [ ] 0 menu/drawer/modal/table/form defect còn mở.
- [ ] 100% screenshot/diff canonical đã manual-reviewed.
- [ ] Desktop, laptop, tablet và mobile được sign-off riêng; không dùng desktop pass để suy ra mobile pass.

### Bằng chứng bắt buộc cho G6.5

- `docs/audit/UI_FILE_IMPACT_MAP.md`
- `docs/audit/UI_BREAKPOINT_AND_REFLOW_CONTRACT.md`
- `artifacts/ui-cross-device/UI_ROUTE_ROLE_STATE_VIEWPORT_MATRIX.csv`
- `artifacts/ui-cross-device/UI_DEFECT_REGISTER.md`
- `artifacts/ui-cross-device/UI_ACCESSIBILITY_REPORT.md`
- `artifacts/ui-cross-device/UI_VISUAL_REVIEW_MANIFEST.csv`
- `artifacts/ui-cross-device/screenshots/`
- `artifacts/ui-cross-device/diffs/`
- `artifacts/ui-cross-device/traces/`

## G6.6. UI wording, error handling và accessibility cơ bản — `MUST`

- [ ] Không lộ label kỹ thuật như `ItemLocation`, `ReservedQty`, `IsActive` hoặc raw error code.
- [ ] Status và business error được Việt hóa nhất quán.
- [ ] Empty state nói rõ chưa có dữ liệu hay filter không có kết quả.
- [ ] Error có hành động tiếp theo; request ID được hiển thị khi cần hỗ trợ.
- [ ] Không hiện stack trace hoặc secret.
- [ ] Focus, label, keyboard navigation và contrast cơ bản hoạt động ở form quan trọng.
- [ ] Nút nguy hiểm có xác nhận phù hợp; không dùng màu là tín hiệu duy nhất.

### Bằng chứng Gate 6

- `artifacts/performance/`
- `artifacts/query-plans/`
- `artifacts/visual-regression/`
- `artifacts/ui-cross-device/`
- `artifacts/full-audit/GATE6_PERFORMANCE_RELIABILITY_UI_EVIDENCE_2026_07_15.md`
- `artifacts/full-audit/FINAL_REAUDIT_GATES_0_1_2_3_4_6_2026_07_15.md`
- Trạng thái re-audit ngày 15/07/2026: `PARTIALLY VERIFIED`; build/test/Playwright hiện tại pass nhưng finding dữ liệu, hạ tầng và evidence bên ngoài còn mở.
- Báo cáo before/after nếu tối ưu query hoặc index.

---

# GATE 7 — EXCEPTION WORKFLOW, UAT VÀ GO/NO-GO

## G7.1. Management Command Center — Trang tổng quát “Hôm nay cần làm gì?” — `MUST`

Trang tổng quát của Quản lý kho phải là màn hình điều hành có thể hành động, không phải tập hợp biểu đồ trang trí. Khi mở trang, quản lý phải trả lời được ngay:

1. Hôm nay phải làm những việc gì?
2. Việc nào chưa bắt đầu, đang làm, đã xong, trễ hạn hoặc bị chặn?
3. Nhập, xuất, chuyển kho, kiểm kê và trả hàng đang đạt bao nhiêu phần trăm?
4. Kho đang thiếu hàng, âm tồn, giữ chỗ sai, hết hạn hoặc có chênh lệch nào?
5. Ai/nhóm nào đang phụ trách và còn bao nhiêu khối lượng?
6. Ngoại lệ nào cần xử lý trước để không ảnh hưởng vận hành?
7. Có thể bấm từ cảnh báo/KPI đến đúng danh sách và hành động xử lý hay không?

### Benchmark từ WMS enterprise

| Hệ thống | Cách tổ chức được dùng làm nguyên tắc |
|---|---|
| [SAP EWM Warehouse Monitor](https://help.sap.com/docs/SAP_EXTENDED_WAREHOUSE_MANAGEMENT/3d97bec9bf1649099384bb8167df3cf2/51cdcb53ad377114e10000000a174cb4.html) và [Warehouse Cockpit](https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/9832125c23154a179bfa1784cdc9577a/7dcacb53ad377114e10000000a174cb4.html) | Một công cụ trung tâm cho tình trạng hiện tại của kho, kèm KPI/đồ họa có thể cấu hình |
| [Oracle Activity Monitor](https://docs.oracle.com/cd/E12456_01/rwms/pdf/150/html/ui_user_guide/dashboards.htm) và [Inventory Command Center](https://docs.oracle.com/cd/E26401_01/doc.122/e48820/T291651T671994.htm) | KPI theo receiving, transport, cycle count, replenishment, picking, shipping; hiển thị Not Started/In Progress/Completed và cho xử lý ngoại lệ |
| [Manhattan Command Your DC](https://www.manh.com/our-insights/resources/demo-series/command-your-dc) | Dashboard/cảnh báo thời gian thực cho labor, inventory, throughput và quản lý tập trung nhiều kho |
| [Blue Yonder Analyst Workbench](https://blueyonder.com/solutions/finder) | Hợp nhất dữ liệu warehouse, labor và logistics để phân tích và điều phối |

Không sao chép nguyên mẫu của nhà cung cấp. Chỉ áp dụng các nguyên tắc: real-time visibility, trạng thái công việc, KPI có mục tiêu, exception-first, drill-down/action và data reconciliation.

### G7.1.1. Rà soát toàn bộ file và impact map trước khi sửa — `BLOCKER`

- [x] Inventory toàn bộ first-party file trước khi chỉnh dashboard.
- [x] Xác định route/controller/API hiện tại của trang tổng quát.
- [x] Xác định service/query/repository/DbContext/model/view model/DTO liên quan.
- [x] Xác định view/layout/partial/component/CSS/JavaScript/icon và localization liên quan.
- [x] Xác định permission/menu/navigation/route guard liên quan.
- [x] Xác định migration/index/cache/background job liên quan tới dữ liệu dashboard.
- [x] Xác định unit/integration/API/E2E/Playwright tests và screenshot artifact hiện có.
- [x] Xác định report/dashboard cũ, code trùng, query trùng và file không còn được dùng.
- [x] Trace từng KPI từ UI → API/service → query → bảng/cột/status/date.
- [x] Tạo `docs/audit/DASHBOARD_FILE_IMPACT_MAP.md` trước khi sửa.
- [x] Tạo `docs/audit/DASHBOARD_METRIC_DICTIONARY.md` trước khi sửa.
- [x] Không sửa file nào chỉ vì tên có chữ Dashboard; phải chứng minh nó nằm trên runtime path.
- [x] Không sửa khi chưa biết consumer, dependency, permission và regression scope.

### G7.1.2. Ngữ cảnh vận hành và định nghĩa “hôm nay” — `BLOCKER`

- [x] Hiển thị kho/owner/phạm vi hiện tại.
- [x] Hiển thị ngày, ca làm việc và timezone của kho.
- [ ] Phân biệt rõ: tạo hôm nay, đến hạn hôm nay, dự kiến hôm nay và hoàn thành hôm nay.
- [x] Không trộn các định nghĩa ngày khác nhau trong cùng KPI.
- [ ] Xử lý đúng ranh giới đầu ngày/cuối ngày, ca qua đêm và UTC/local time.
- [x] Hiển thị thời điểm dữ liệu được cập nhật gần nhất.
- [x] Có manual refresh; auto-refresh nếu có không làm mất filter, focus hoặc thao tác đang nhập.
- [x] Cảnh báo dữ liệu stale, query lỗi hoặc chỉ tải được một phần.
- [x] Filter mặc định là hôm nay và phạm vi user được quyền xem.
- [ ] Filter theo kho, owner, khu/vị trí, ca, trạng thái, priority và người phụ trách khi áp dụng.
- [x] Filter được phản ánh trong URL/query state hoặc cơ chế có thể tái lập.

### G7.1.3. Khối “Việc cần làm hôm nay” — `BLOCKER`

- [x] Một work queue hợp nhất, sắp theo Critical → High → SLA gần nhất → thời gian chờ lâu nhất.
- [x] Hiển thị loại việc, mã tham chiếu, mô tả ngắn và kho/khu liên quan.
- [ ] Hiển thị trạng thái: chưa bắt đầu, đang xử lý, chờ duyệt, bị chặn, trễ hạn, hoàn thành.
- [x] Hiển thị progress: số dòng/quantity/task đã xong trên tổng số.
- [x] Hiển thị deadline/SLA, thời gian còn lại hoặc thời gian đã trễ.
- [x] Hiển thị người/nhóm phụ trách và việc chưa được gán.
- [x] Hiển thị reason/blocker hoặc exception code đã Việt hóa.
- [x] Có action phù hợp quyền: xem, nhận việc, tiếp tục, duyệt, xử lý ngoại lệ.
- [ ] Mỗi dòng/card mở đúng trang chi tiết với filter/context được giữ.
- [x] Không hiển thị action mà user không có quyền thực hiện.
- [x] Có empty state “Hôm nay không còn việc cần xử lý” và thời điểm kiểm tra.

Work queue tối thiểu phải tổng hợp:

- [x] Phiếu nhập dự kiến/đến hạn/chậm tiếp nhận.
- [ ] Phiếu nhập đang nhận dở, thiếu/thừa hoặc chờ duyệt.
- [ ] Hàng đã nhận nhưng chưa putaway hoặc putaway quá SLA.
- [x] QC/quarantine chờ quyết định.
- [x] Phiếu xuất đến hạn hôm nay hoặc đã trễ.
- [ ] Phiếu xuất chưa reserve, thiếu tồn, short-pick hoặc chờ duyệt.
- [x] Pick/pack/staging/bàn giao chưa hoàn thành.
- [ ] Chuyển kho/vị trí đang chờ, in-transit hoặc quá hạn nhận.
- [x] Replenishment cần thực hiện.
- [x] Kiểm kê/cycle count đến hạn hoặc quá hạn.
- [ ] Chênh lệch kiểm kê/adjustment chờ duyệt.
- [ ] Hàng trả về chờ disposition.
- [ ] Lô sắp hết hạn/hết hạn cần xử lý.
- [x] Ngoại lệ tồn kho, dữ liệu, job hoặc integration cần xử lý.

### G7.1.4. KPI tổng quan trong ngày — `MUST`

Mỗi KPI card phải có giá trị, đơn vị, phạm vi, so sánh/mục tiêu khi có, thời điểm cập nhật và drill-down.

- [x] Tổng việc cần làm, chưa bắt đầu, đang làm, hoàn thành, bị chặn và trễ.
- [x] Tổng cảnh báo Critical/High/Medium/Low chưa đóng.
- [x] Phiếu nhập dự kiến hôm nay.
- [x] Phiếu nhập đã nhận/post hôm nay.
- [x] Phiếu nhập còn lại/chậm và tỷ lệ hoàn thành.
- [ ] Dòng/quantity/LPN nhận hôm nay theo đơn vị hợp lệ.
- [x] Phiếu xuất đến hạn hôm nay.
- [x] Phiếu xuất đã post/bàn giao/ship hôm nay.
- [x] Phiếu xuất còn lại/chậm và tỷ lệ hoàn thành.
- [ ] Dòng/quantity pick, pack và ship hôm nay.
- [ ] Chuyển kho dự kiến, hoàn thành, in-transit và quá hạn.
- [ ] Kiểm kê dự kiến, hoàn thành, quá hạn và chênh lệch chờ duyệt.
- [ ] Adjustment tăng/giảm hôm nay theo reason.
- [ ] Return dự kiến/đã xử lý/chờ disposition.
- [ ] SKU/vị trí có tồn; SKU hết hàng, sắp hết hàng hoặc vượt ngưỡng.
- [ ] Available, reserved và blocked quantity theo item/base UOM hoặc nhóm UOM tương thích.
- [x] Không cộng trực tiếp quantity của các UOM không tương thích.
- [x] Giá trị tồn chỉ hiện cho role có quyền tài chính và có currency/as-of-time rõ.
- [ ] Lô hết hạn, sắp hết hạn theo các bucket được cấu hình như 7/30/60/90 ngày.
- [ ] Negative stock, over-reservation và data-quality issue.

### G7.1.5. Thống kê nhập kho — `MUST`

- [ ] Dự kiến nhận, đã đến, đang nhận, đã nhận, đã post và đã putaway.
- [ ] Not Started/In Progress/Completed/Blocked/Overdue.
- [ ] Theo phiếu, dòng, quantity, LPN/pallet nếu áp dụng.
- [x] Tỷ lệ hoàn thành và remaining workload.
- [ ] Over/under receipt và discrepancy.
- [ ] Dock-to-receive và dock-to-stock/putaway cycle time.
- [ ] Nhà cung cấp/nguồn nhập có trễ hoặc discrepancy cao.
- [ ] QC pass/fail/quarantine.
- [ ] Top item/zone/location theo khối lượng nhập.
- [ ] Drill-down giữ đúng warehouse/owner/date/status filter.

### G7.1.6. Thống kê xuất kho — `MUST`

- [ ] Đến hạn, đã reserve, đang pick, đã pick, đang pack, đã bàn giao/ship.
- [ ] Not Started/In Progress/Completed/Blocked/Overdue.
- [ ] Theo phiếu, dòng, quantity, order/LPN nếu áp dụng.
- [ ] Tỷ lệ fill, completion và on-time.
- [ ] Remaining workload theo pick/pack/staging.
- [ ] Short-pick, stockout, backorder, thiếu reservation và late order.
- [ ] Order cycle time/pick-pack-ship cycle time.
- [ ] Top item/zone theo khối lượng xuất.
- [ ] Drill-down giữ đúng warehouse/owner/date/status filter.

### G7.1.7. Thống kê tồn kho, vị trí và chất lượng — `MUST`

- [ ] Tổng SKU active và SKU đang có tồn.
- [ ] Tổng vị trí, vị trí trống, đang dùng, đầy hoặc vượt capacity.
- [ ] On-hand/available/reserved/blocked theo phạm vi và UOM đúng.
- [ ] Low stock/out-of-stock/overstock theo rule đã cấu hình.
- [ ] Aging/dead stock/slow-moving nếu có dữ liệu.
- [ ] Hàng sắp hết hạn/hết hạn và quantity/value chịu ảnh hưởng.
- [ ] Quarantine/damaged/hold và thời gian bị giữ.
- [ ] Negative/invalid available và reservation bất thường.
- [ ] ItemLocation/ledger/reconciliation issue.
- [ ] Inventory accuracy và adjustment rate theo kỳ.
- [ ] Heat map/space utilization chỉ triển khai khi dữ liệu capacity đáng tin cậy.

### G7.1.8. Kiểm kê, điều chỉnh, trả hàng và chuyển kho — `MUST`

- [ ] Cycle count đến hạn, đang làm, hoàn thành và quá hạn.
- [ ] Count/recount/approval progress.
- [ ] Chênh lệch tăng/giảm theo quantity/value/reason.
- [ ] Adjustment chờ duyệt và adjustment bất thường.
- [ ] Transfer draft/in-transit/received/cancelled/overdue.
- [ ] Transfer discrepancy tại nguồn/đích.
- [ ] Return chờ nhận/QC/disposition/restock/return/scrap.
- [ ] Mọi số liệu drill-down về đúng chứng từ và ledger.

### G7.1.9. Ngoại lệ nghiệp vụ và sức khỏe hệ thống — `MUST`

- [ ] Phiếu chờ duyệt hoặc bị stuck ở trạng thái không hợp lệ.
- [ ] Negative stock, over-reservation, duplicate/orphan ledger.
- [ ] Phiếu đã post thiếu transaction hoặc transaction không có source hợp lệ.
- [ ] UOM/lot/serial/NSX/HSD/data master bất hợp lệ.
- [ ] Import/OCR/export fail và retry count.
- [ ] Background job/integration/dead-letter fail.
- [ ] Database/health/readiness bất thường.
- [ ] Backup/restore rehearsal và data-quality audit gần nhất.
- [ ] Login fail hoặc security alert bất thường chỉ hiện cho role phù hợp.
- [x] Exception có severity, owner, tuổi lỗi, deadline, trạng thái và action.
- [x] Không hiển thị secret, stack trace hoặc dữ liệu nhạy cảm.

### G7.1.10. Khối lượng công việc, nhân sự và năng lực — `CONDITIONAL`

- [ ] Open/assigned/unassigned/completed tasks.
- [ ] Remaining workload theo quy trình, zone và ca.
- [ ] Nhân viên/nhóm đang rảnh, quá tải hoặc có task trễ nếu hệ thống có task assignment.
- [ ] Throughput theo giờ/ca và so với kế hoạch.
- [ ] Productivity chỉ dùng công thức đã phê duyệt và dữ liệu đủ tin cậy.
- [ ] Không công khai dữ liệu hiệu suất cá nhân ngoài role được phép.
- [ ] Capacity/space/dock utilization nếu có dữ liệu thực.

### G7.1.11. Xu hướng và so sánh — `MUST`

- [ ] Hôm nay so với hôm qua, cùng ngày tuần trước hoặc kế hoạch.
- [ ] Trend 7/30 ngày cho inbound, outbound, adjustment và inventory accuracy.
- [ ] Throughput theo giờ để thấy backlog tăng/giảm trong ca.
- [ ] Dock-to-stock, order cycle, on-time và fill rate.
- [ ] Không so sánh hai khoảng thời gian có scope/filter khác nhau.
- [ ] Chart có trục, đơn vị, timezone, legend và empty/partial-data state rõ.
- [ ] Không dùng chart khi table/KPI truyền đạt rõ hơn.

### G7.1.12. Drill-down, quick action và UX — `MUST`

- [ ] Mọi card/chart/alert quan trọng bấm được vào danh sách đã filter.
- [ ] Count ở dashboard khớp count của danh sách chi tiết trong cùng snapshot.
- [ ] Back navigation giữ filter/context.
- [x] Quick actions chỉ gồm nghiệp vụ thường dùng và đúng role; không biến dashboard thành sidebar thứ hai.
- [x] Phân tầng: việc cần làm/ngoại lệ ở trên, KPI chính kế tiếp, chi tiết/trend ở dưới.
- [x] Critical/overdue nổi bật nhưng không chỉ dùng màu.
- [ ] Có loading, empty, partial error, full error và stale-data state.
- [ ] Không nhảy layout khi auto-refresh.
- [x] Không horizontal overflow, overlap, clipping hoặc card cao thấp vô lý.
- [ ] Desktop 1366×768 phải thấy phần “Việc cần làm hôm nay” không cần cuộn quá xa.
- [ ] Desktop 1920×1080 dùng không gian hợp lý, không kéo card quá rộng.
- [ ] Zoom 110%, 125%, 150%; tablet/mobile nếu support.
- [ ] Keyboard/focus/ARIA/contrast đạt target accessibility.
- [x] Thuật ngữ tiếng Việt thống nhất và không lộ field/status kỹ thuật.

### G7.1.13. Permission và data isolation — `BLOCKER`

- [x] Admin/quản lý kho/nhân viên/owner nhìn dashboard khác nhau theo quyền.
- [x] Warehouse/owner scope áp dụng ở query backend, không chỉ filter UI.
- [ ] KPI, drill-down, export và API dùng cùng permission scope.
- [x] Role không có quyền tài chính không nhận dữ liệu giá trị tồn trong HTML/API.
- [x] Đổi query parameter không xem được kho/owner khác.
- [ ] Cache key bao gồm role/user scope, warehouse, owner, timezone và filter cần thiết.
- [x] Không cache lẫn dữ liệu giữa user/tenant.

### G7.1.14. Metric dictionary và data reconciliation — `BLOCKER`

Với từng KPI phải ghi:

- [x] Tên hiển thị và business meaning.
- [x] Công thức tử số/mẫu số.
- [x] Source table/entity và status được tính/loại.
- [x] Date field dùng cho created/due/scheduled/completed.
- [x] Warehouse/owner/UOM/currency/timezone scope.
- [x] Distinct key để tránh double-count header/line/join.
- [x] Null/cancel/reversal/partial handling.
- [x] Refresh/caching/as-of-time.
- [x] Drill-down route và filter mapping.
- [x] Test data và expected result tính tay.
- [ ] Owner phê duyệt công thức.

### G7.1.15. Kỹ thuật, hiệu năng và độ tin cậy — `MUST`

- [x] Không tạo một mega-query khó kiểm soát nếu tách aggregate an toàn hơn.
- [x] Không N+1 hoặc load toàn bộ lịch sử vào memory.
- [ ] Aggregate/filter theo DB, có index cho warehouse/owner/status/date.
- [x] Query cancellation/timeout và partial failure strategy.
- [x] Cache chỉ dùng khi không làm sai dữ liệu real-time; TTL/invalidation được tài liệu hóa.
- [x] Tránh query storm do auto-refresh hoặc nhiều widget.
- [x] p95 tải dashboard đạt performance contract.
- [x] Dashboard lỗi không làm hỏng thao tác nghiệp vụ khác.
- [ ] Có structured log/correlation/metric cho widget/query fail.

### G7.1.16. Automated, Playwright và visual acceptance — `BLOCKER`

- [ ] Unit tests cho từng công thức KPI và ranh giới ngày/ca.
- [ ] Integration tests đối chiếu query với dữ liệu expected tính tay.
- [ ] API tests cho filter, permission, owner/warehouse scope và error state.
- [ ] E2E kiểm tra card → drill-down → action → quay lại dashboard.
- [ ] Test dữ liệu 0, 1, nhiều, partial, cancelled, reversed, overdue và cross-midnight shift.
- [x] Test không double-count khi join header/line/ledger.
- [ ] Test các UOM không tương thích không bị cộng chung.
- [ ] Playwright cho từng role và viewport/zoom được hỗ trợ.
- [x] Bắt console error, pageerror, failed request, 4xx/5xx không mong đợi.
- [ ] Visual states: normal, empty, loading, partial error, full error, stale, long text, many alerts.
- [x] Manual-review tất cả screenshot/diff; không update baseline mù.
- [ ] Dữ liệu dashboard reconcile 100% với detail query và data-quality audit.

### Bằng chứng bắt buộc cho G7.1

- `docs/audit/DASHBOARD_FILE_IMPACT_MAP.md`
- `docs/audit/DASHBOARD_METRIC_DICTIONARY.md`
- `docs/audit/DASHBOARD_ROLE_ACTION_MATRIX.md`
- `artifacts/dashboard-command-center/DASHBOARD_DATA_RECONCILIATION.md`
- `artifacts/dashboard-command-center/DASHBOARD_QUERY_PERFORMANCE.md`
- `artifacts/dashboard-command-center/DASHBOARD_PLAYWRIGHT_MATRIX.csv`
- `artifacts/dashboard-command-center/VISUAL_QA_REPORT.md`
- Screenshot before/after, diff, trace và test reports.

## G7.2. Exception workflow — `SHOULD`

- [x] Trạng thái: mới, đang xử lý, đã xử lý, bỏ qua có lý do.
- [x] Severity, owner và deadline.
- [ ] Lịch sử xử lý và comment.
- [x] Filter theo kho, owner, loại, severity và hạn xử lý.
- [x] Bỏ qua exception phải có quyền và audit.

## G7.3. UAT matrix theo vai trò — `MUST`

- [ ] Admin.
- [ ] Quản lý kho.
- [ ] Nhân viên nhập kho.
- [ ] Nhân viên xuất kho.
- [ ] Nhân viên kiểm kê/tồn kho.
- [ ] Nhân viên vận chuyển.
- [ ] Nhân viên báo cáo.
- [ ] Owner/đối tác nếu có.
- [ ] Mỗi role kiểm tra menu, route/API, data scope, action được phép và action bị chặn.

## G7.4. Operational UAT — `BLOCKER`

- [ ] Một ca nhập kho đầy đủ.
- [ ] Một ca xuất kho đầy đủ có reservation và FEFO/FIFO.
- [ ] Một ca partial receipt/partial issue.
- [ ] Một ca hủy hoặc reversal.
- [ ] Một ca chuyển kho/vị trí.
- [ ] Một đợt kiểm kê và adjustment.
- [ ] Một ca trả hàng/quarantine nếu có.
- [ ] Một import hợp lệ và một import có nhiều lỗi.
- [ ] Một lần OCR fail và chuyển sang fallback thủ công.
- [ ] Một export đúng quyền và filter.
- [ ] Một lỗi được truy vết từ request ID đến log và audit.
- [ ] Một lần restore rehearsal và kiểm tra dữ liệu sau restore.

## G7.5. Release rehearsal — `BLOCKER`

- [ ] Dùng đúng release checklist dự kiến cho production.
- [ ] Deploy/migrate trên staging từ phiên bản trước.
- [ ] Chạy automated smoke và critical E2E.
- [x] Chạy data-quality audit.
- [ ] Đo downtime và thời gian hoàn tất.
- [ ] Diễn tập rollback hoặc forward-fix theo chiến lược đã duyệt.
- [ ] Ghi lại lỗi, cập nhật runbook và chạy lại đến khi pass.

---

# MỐC A — GO/NO-GO DEVICE-FREE

Chỉ được kết luận **DEVICE-FREE READY** khi tất cả điều kiện sau đạt:

## Bắt buộc phải PASS

- [ ] Không còn task `BLOCKER` ở trạng thái TODO, FAIL, BLOCKED hoặc DEFERRED.
- [ ] Không còn security finding Critical/High chưa xử lý.
- [ ] Build và toàn bộ critical automated tests pass.
- [ ] Transaction rollback, idempotency và concurrency tests pass.
- [ ] Không âm tồn, vượt reservation, duplicate/orphan ledger hoặc sai reconcile.
- [ ] Permission, route guard và data-scope tests pass.
- [ ] Core E2E nhập, xuất, chuyển, kiểm kê, điều chỉnh, hủy/reversal pass.
- [ ] Import/OCR retry không tạo duplicate.
- [ ] Migration pass trên DB sạch và bản sao DB hiện tại.
- [ ] Backup đã restore thành công và đạt RPO/RTO đã duyệt.
- [ ] Health/readiness không lộ secret.
- [ ] Performance đạt contract và có 0 lỗi integrity sau load test.
- [ ] Release rehearsal pass.
- [ ] UAT có sign-off của Product Owner/người phụ trách kho và kỹ thuật.

## Điều kiện cho task được đánh dấu N/A

- Có lý do không áp dụng.
- Có bằng chứng feature/module không tồn tại hoặc đã tắt.
- Có người chịu trách nhiệm xác nhận.
- Không được dùng `N/A` để né một invariant tồn kho hoặc security control đang áp dụng.

## Điều kiện defer hạng mục SHOULD

- Có owner.
- Có deadline.
- Có mô tả tác động và workaround.
- Có người chấp nhận rủi ro.
- Không ảnh hưởng stock integrity, security, backup/restore hoặc core workflow.

---

# GATE 8 — ENTERPRISE BENCHMARK VÀ FIT-GAP

## G8.1. Nghiên cứu benchmark có nguồn — MUST

- [ ] Đối chiếu tài liệu chính thức hiện hành của SAP Extended Warehouse Management.
- [ ] Đối chiếu tài liệu chính thức hiện hành của Oracle Warehouse Management.
- [ ] Đối chiếu tài liệu chính thức hiện hành của Manhattan Active Warehouse Management.
- [ ] Đối chiếu tài liệu chính thức hiện hành của Blue Yonder Warehouse Management.
- [ ] Có thể bổ sung Microsoft Dynamics 365 SCM, Infor WMS hoặc Körber nếu phù hợp.
- [ ] Ghi URL, ngày truy cập, capability được xác nhận và giới hạn của nguồn.
- [ ] Không dùng nội dung marketing làm bằng chứng hệ thống của mình đã tương đương.
- [ ] Không triển khai tính năng không tạo giá trị chỉ để tăng điểm.

### Bộ nguồn chuẩn ban đầu

| Hệ thống | Nguồn chính thức | Nhóm capability dùng để đối chiếu |
|---|---|---|
| SAP EWM | [SAP Extended Warehouse Management](https://www.sap.com/products/scm/extended-warehouse-management.html) | Vận hành khối lượng lớn, inbound/outbound, liên kết vận tải, yard, sản xuất và storage control |
| Oracle WMS | [Oracle Warehouse Management](https://www.oracle.com/scm/logistics/warehouse-management/) | Inventory visibility, fulfillment, cross-dock, flow-through, VAS, kitting, pick/pack/load/ship |
| Manhattan Active WM | [Manhattan Warehouse Management](https://www.manh.com/solutions/supply-chain-management-software/warehouse-management) | Real-time execution, labor, robotics, transportation, workflow/API và WES |
| Blue Yonder WMS | [Blue Yonder Warehouse Management](https://blueyonder.com/solutions/warehouse-management) | Task/resource orchestration, labor, advanced slotting, load building, yard, returns, WES và automation |

Đây là baseline tham chiếu, không phải danh sách đóng. Khi audit phải kiểm tra lại tài liệu chính thức mới nhất và ghi ngày truy cập.

## G8.2. Capability matrix — MUST

Mỗi capability phải có mức áp dụng, trạng thái, bằng chứng code/API/UI/DB/test, gap, rủi ro, dependency, effort, owner, target release, điểm 0–4 và sản phẩm benchmark tham chiếu.

### Tiêu chí nghiệm thu

- Có Internal Readiness, Enterprise Parity và Evidence Coverage riêng.
- Tổng trọng số đúng 100%.
- Không cấp điểm chỉ dựa trên tên menu, screenshot hoặc tài liệu tự viết.

---

# GATE 9 — NGHIỆP VỤ WMS NÂNG CAO

## G9.1. Cấu trúc kho và handling unit — MUST nếu áp dụng

- [ ] Warehouse → zone → aisle → rack → level → bin/location có constraint rõ.
- [ ] Dock, staging, receiving, picking, packing, shipping và quarantine area được phân loại.
- [ ] Location capacity theo quantity, volume, weight hoặc item restriction.
- [ ] License Plate Number/Handling Unit/container/pallet có vòng đời và uniqueness.
- [ ] Split/merge/move handling unit có ledger và audit.
- [ ] Inventory status: available, reserved, hold, quarantine, damaged, expired, in-transit.
- [ ] Không cho chuyển trạng thái để né quality hoặc permission rule.

## G9.2. Inbound nâng cao — MUST/CONDITIONAL

- [ ] Purchase order/ASN/inbound order và receiving discrepancy.
- [ ] Dock appointment và receiving schedule nếu kho cần.
- [ ] Blind receiving nếu nghiệp vụ yêu cầu.
- [ ] Over/under receipt tolerance và approval.
- [ ] Quality inspection, quarantine và release/reject.
- [ ] Directed putaway theo zone, capacity, compatibility và velocity.
- [ ] Cross-docking và flow-through allocation nếu phù hợp.
- [ ] Supplier return và claim evidence.
- [ ] Inbound label/LPN/SSCC và traceability.
- [ ] Kitting/de-kitting hoặc production receipt nếu liên quan sản xuất.

## G9.3. Outbound nâng cao — MUST/CONDITIONAL

- [ ] Sales/transfer/issue order có priority, SLA và allocation rule.
- [ ] Wave, batch, zone, cluster và waveless picking theo phạm vi.
- [ ] Single, multi-order, case, pallet và piece picking.
- [ ] Task interleaving và route/path optimization nếu cần.
- [ ] Short pick, substitution, backorder, partial allocation và split shipment.
- [ ] Packing validation, cartonization và packing material.
- [ ] Value-added service: labeling, tagging, assembly hoặc repack.
- [ ] Load planning, staging, manifest và ship confirmation.
- [ ] Carrier/TMS handoff và proof of shipment nếu tích hợp.
- [ ] Hủy/reversal sau allocation, pick, pack và ship có state machine rõ.

## G9.4. Replenishment và slotting — CONDITIONAL

- [ ] Min/max, demand-based và emergency replenishment.
- [ ] Không tạo replenishment trùng hoặc vượt capacity.
- [ ] Forward pick và reserve storage reconcile đúng.
- [ ] Slotting dựa trên velocity, kích thước, tương thích và ergonomics.
- [ ] Recommendation có explanation và human approval.

## G9.5. Traceability, recall và hàng đặc thù — MUST nếu quản lý lô/serial

- [ ] Truy ngược từ khách hàng/chứng từ đến lot/serial và nguồn nhập.
- [ ] Truy xuôi từ lot/serial đến mọi vị trí, giao dịch và khách nhận.
- [ ] Recall/block toàn bộ lot trong thời gian mục tiêu.
- [ ] Catch weight và dual UOM nếu nghiệp vụ có cân nặng biến đổi.
- [ ] Hazmat, cold-chain, allergen hoặc incompatibility rule nếu có.
- [ ] Shelf-life, minimum remaining life và expiry boundary.
- [ ] Cycle count theo ABC, velocity, risk và location.
- [ ] Không mất chain of custody khi split/merge/repack.

## G9.6. Returns và reverse logistics — MUST/CONDITIONAL

- [ ] Customer return/RMA và supplier return.
- [ ] Disposition: restock, quarantine, repair, rework, return, scrap.
- [ ] Không tự restock trước quality decision.
- [ ] Refund/credit handoff nếu tích hợp ERP/accounting.
- [ ] Ảnh/chứng từ và audit cho hàng hư hỏng.
- [ ] Serial warranty/ownership validation nếu áp dụng.

### Tiêu chí nghiệm thu Gate 9

- Mọi capability Required Now có state machine, permission, transaction, audit và E2E.
- Capability N/A không bị ẩn khỏi Enterprise Parity.
- Không tính năng nâng cao nào cập nhật tồn ngoài inventory service/ledger chuẩn.

---

# GATE 10 — MULTI-WAREHOUSE, MULTI-OWNER, 3PL VÀ LOCALIZATION

## G10.1. Multi-warehouse/network — MUST nếu có nhiều kho

- [ ] Quyền và dữ liệu tách theo kho.
- [ ] Inter-warehouse transfer, in-transit, receive discrepancy và ownership.
- [ ] Network inventory visibility không lộ dữ liệu ngoài scope.
- [ ] Replenishment/chuyển kho đề xuất không vượt tồn hoặc capacity.
- [ ] Timezone và cut-off riêng từng kho nếu cần.

## G10.2. Multi-owner/3PL — CONDITIONAL

- [ ] Tồn, đơn, giá trị, chứng từ và báo cáo tách tuyệt đối theo owner.
- [ ] Owner-specific SKU alias, UOM, label, SLA và workflow.
- [ ] Contract, rate card, charge event và billing reconciliation.
- [ ] Storage, inbound, outbound, VAS, handling và minimum charge nếu áp dụng.
- [ ] Client portal/API không có cross-owner leakage.
- [ ] Mọi admin override có reason và audit.

## G10.3. Localization — MUST theo phạm vi

- [ ] Tiếng Việt thống nhất; ngôn ngữ khác nếu đã cam kết.
- [ ] Unicode, dấu tiếng Việt, tìm kiếm không dấu và collation đúng.
- [ ] Timezone, locale, date, number, decimal và currency chính xác.
- [ ] UOM/conversion không phụ thuộc text hiển thị.
- [ ] Không hard-code label nghiệp vụ trong logic.
- [ ] Tài liệu, email, export và print template dùng cùng thuật ngữ chuẩn.

---

# GATE 11 — INTEGRATION, API VÀ EXTENSIBILITY

## G11.1. Integration landscape — MUST

- [ ] Lập danh sách ERP, OMS, TMS, accounting, procurement, e-commerce, carrier, identity và BI.
- [ ] Mỗi integration có owner, direction, protocol, schema, SLA, timeout và source of truth.
- [ ] Xác định sync/async, failure mode, replay và reconciliation.
- [ ] Không gọi dịch vụ ngoài trong DB transaction dài.
- [ ] Sandbox/test tách production.

## G11.2. API enterprise contract — MUST

- [ ] Authentication/authorization và tenant/owner/warehouse scope.
- [ ] Versioning và backward compatibility.
- [ ] Idempotency cho command tạo/post/hủy.
- [ ] Pagination, filtering, sorting và limit.
- [ ] Validation/error envelope/correlation ID ổn định.
- [ ] Rate limit, timeout, cancellation và payload limit.
- [ ] OpenAPI khớp runtime.
- [ ] Không lộ internal entity hoặc mass assignment.
- [ ] Contract tests cho consumer quan trọng.

## G11.3. Event, EDI và messaging — CONDITIONAL

- [ ] Outbox/inbox hoặc cơ chế tương đương chống mất/nhân đôi event.
- [ ] At-least-once consumer idempotent.
- [ ] Retry/backoff/dead-letter và replay có kiểm soát.
- [ ] Event schema versioning và compatibility.
- [ ] EDI/ASN/order/shipment mapping có validation và acknowledgement.
- [ ] Ordering/partition key phù hợp.
- [ ] Reconciliation phát hiện missing/stuck message.
- [ ] Monitoring theo integration và partner.

## G11.4. Extension governance — MUST

- [ ] Có extension point rõ, không shortcut phá core.
- [ ] Feature flag có owner, default, expiry và audit.
- [ ] Configuration nghiệp vụ được validate/version/audit.
- [ ] Custom report/workflow không vượt permission/data scope.
- [ ] Plugin/script tùy biến được sandbox và security review nếu có.

---

# GATE 12 — DEVICE, MOBILE, PRINTING VÀ AUTOMATION

## G12.1. Device matrix — BLOCKER trước go-live thiết bị

Mỗi thiết bị phải ghi model, OS/firmware, browser/app, kết nối, owner, ca sử dụng, test case, kết quả và fallback:

- [ ] Handheld barcode/QR scanner.
- [ ] Camera điện thoại/tablet.
- [ ] Máy in tem và chứng từ.
- [ ] Máy cân nếu có.
- [ ] RFID reader nếu có.
- [ ] Kiosk/desktop/tablet.
- [ ] PLC/conveyor/robot/ASRS nếu có.

## G12.2. Barcode/GS1/LPN — MUST nếu quét mã

- [ ] Symbology thực tế và GS1 Application Identifier nếu sử dụng.
- [ ] SKU, lot, serial, expiry, quantity và LPN parse chính xác.
- [ ] Barcode trùng, hỏng, thiếu, ký tự đặc biệt và scan liên tục.
- [ ] Không nhận scan của owner/kho/item sai context.
- [ ] Chống xử lý hai lần do scanner phát sự kiện lặp.
- [ ] Feedback âm thanh/hình ảnh và recovery rõ.
- [ ] In → scan lại → đối chiếu dữ liệu round-trip.

## G12.3. Printing — MUST nếu in

- [ ] Template versioning, role, owner, printer và khổ giấy.
- [ ] Font tiếng Việt, quiet zone, DPI và barcode readability.
- [ ] Print queue, retry, duplicate print và reprint audit.
- [ ] Không đánh dấu nghiệp vụ hoàn thành chỉ vì request in đã gửi.
- [ ] Fallback PDF/manual print.
- [ ] Test tem thật trên vật liệu và khoảng cách quét thật.

## G12.4. Mobile/offline/network — CONDITIONAL

- [ ] Responsive trên thiết bị thật, portrait/landscape và keyboard mềm.
- [ ] Mất mạng giữa create/save/post/scan không gây duplicate.
- [ ] Offline queue nếu có phải mã hóa, idempotent và conflict-aware.
- [ ] Session/token trên thiết bị dùng chung được bảo vệ.
- [ ] Remote logout/device revocation nếu cần.
- [ ] Wi-Fi roaming, latency, packet loss và reconnect tại kho thật.
- [ ] Có quy trình thủ công khi mạng/thiết bị hỏng.

## G12.5. WCS/WES/robotics — CONDITIONAL

- [ ] Simulator test trước thiết bị thật.
- [ ] Command/acknowledgement/status/error contract.
- [ ] Correlation, timeout, retry và duplicate command protection.
- [ ] Emergency stop/manual takeover không làm sai inventory.
- [ ] Reconcile physical movement với system movement.
- [ ] Vendor outage và degraded mode.
- [ ] Security segmentation và credential rotation.

---

# GATE 13 — LABOR, ANALYTICS, OPTIMIZATION VÀ AI

## G13.1. KPI và operational intelligence — MUST

- [ ] Inventory accuracy.
- [ ] Order/line fill rate và order accuracy.
- [ ] Dock-to-stock time.
- [ ] Pick/pack/ship cycle time.
- [ ] On-time shipment/SLA.
- [ ] Throughput theo giờ/ca/kho.
- [ ] Aging, expiry, dead stock và space utilization.
- [ ] Adjustment/short-pick/damage rate.
- [ ] Dashboard drill-down khớp dữ liệu và permission.
- [ ] Công thức KPI được tài liệu hóa, version và reconcile tự động.

## G13.2. Labor/task management — CONDITIONAL

- [ ] Shift, skill, zone, availability và workload.
- [ ] Task assignment/reassignment và priority.
- [ ] Task interleaving không phá FIFO/FEFO/SLA.
- [ ] Productivity metric chính xác, giải thích được.
- [ ] Labor standard, break và privacy policy.
- [ ] Manual override có audit.

## G13.3. Forecasting/optimization/AI — CONDITIONAL

- [ ] Use case, baseline và KPI trước khi dùng AI.
- [ ] Data lineage, validation và drift monitoring.
- [ ] Recommendation có confidence/explanation và human approval.
- [ ] AI không tự post/hủy/điều chỉnh tồn khi thiếu control riêng.
- [ ] Model/provider version, fallback và cost/rate-limit monitoring.
- [ ] Chống prompt injection/data leakage khi xử lý chứng từ.
- [ ] Đánh giá sai số, hallucination và rollback.
- [ ] Rule-based fallback cho nghiệp vụ sống còn.

---

# GATE 14 — ENTERPRISE NON-FUNCTIONAL, COMPLIANCE VÀ RESILIENCE

## G14.1. Availability và scalability — MUST theo SLA

- [ ] SLO/SLI cho availability, latency, error rate và data freshness.
- [ ] Capacity plan theo peak, season và growth.
- [ ] Multi-instance an toàn: session, cache, job lock và idempotency.
- [ ] Load, stress, spike, soak và recovery test.
- [ ] Graceful degradation khi dependency phụ lỗi.
- [ ] Không có single point of failure trái SLA.
- [ ] DB failover/reconnect được diễn tập nếu hạ tầng hỗ trợ.
- [ ] Cache invalidation không làm quyết định sai tồn.
- [ ] Zero/low-downtime deployment theo mục tiêu.

## G14.2. Security enterprise — BLOCKER

- [ ] Threat model và data-flow diagram.
- [ ] SSO/OIDC/OAuth2 nếu tích hợp identity provider.
- [ ] SCIM/provisioning/deprovisioning nếu cần.
- [ ] Least privilege cho app, DB, storage, CI/CD và ops.
- [ ] Secret manager, rotation và break-glass.
- [ ] SAST, dependency, secret và DAST/API security test.
- [ ] Security headers, TLS, CORS/CSP và cookie policy.
- [ ] Audit/admin monitoring và suspicious-login alert.
- [ ] Pen-test hoặc independent review theo rủi ro.
- [ ] Không còn finding ở mọi severity nếu tuyên bố 0 known defects.

## G14.3. Privacy, retention và compliance — MUST

- [ ] Data classification và owner.
- [ ] Thu thập tối thiểu dữ liệu cần thiết.
- [ ] Retention/deletion/archive cho log, audit, file và backup.
- [ ] Mask/anonymize dữ liệu production dùng ở staging.
- [ ] Quy trình nghĩa vụ dữ liệu nếu pháp lý yêu cầu.
- [ ] Audit retention chống sửa.
- [ ] License package/font/template hợp lệ.
- [ ] Accessibility target và bằng chứng.

## G14.4. Business continuity — BLOCKER

- [ ] RPO/RTO được phê duyệt.
- [ ] Backup ở fault domain phù hợp và restore định kỳ.
- [ ] DR/failover exercise.
- [ ] Runbook mất điện, mạng, DB, storage và identity provider.
- [ ] Quy trình manual operation và nhập bù có kiểm soát.
- [ ] Reconcile giao dịch phát sinh khi outage.
- [ ] Escalation và quyền break-glass.

---

# GATE 15 — DATA MIGRATION, PILOT, CUTOVER VÀ HYPERCARE

## G15.1. Master/opening data readiness — BLOCKER

- [ ] Item, category, UOM, conversion, owner, partner, warehouse, location và user đầy đủ.
- [ ] Lot/serial/expiry/status/quantity opening balance được cleanse và reconcile.
- [ ] Không dùng dữ liệu demo làm production master data.
- [ ] Dry-run migration nhiều lần, có checksum/count/control total.
- [ ] Mapping lỗi có owner và resolution.
- [ ] Freeze window và delta migration.
- [ ] Sign-off tồn đầu kỳ bởi nghiệp vụ và kỹ thuật.

## G15.2. Real-device and warehouse pilot — BLOCKER

- [ ] Phạm vi pilot đủ đại diện và có thể rollback.
- [ ] Test mọi device/model/network/location/shift được hỗ trợ.
- [ ] Chạy inbound, outbound, transfer, count, return, exception và reprint thật.
- [ ] Đối soát vật lý với hệ thống trước, trong và sau pilot.
- [ ] Không còn workaround chưa tài liệu hóa.
- [ ] Nhân viên thật ký UAT về tốc độ, wording và thao tác.
- [ ] Mọi issue pilot được đóng và regression.

## G15.3. Cutover — BLOCKER

- [ ] Go-live plan theo thời gian, owner và dependency.
- [ ] Freeze, backup, migration, reconcile, deploy, smoke và mở quyền.
- [ ] Go/No-Go authority và rollback trigger.
- [ ] Xử lý giao dịch đang dở và hàng đang di chuyển.
- [ ] Printer/scanner/network fallback.
- [ ] Giữ hệ thống cũ hết retention/rollback window.
- [ ] Business sign-off sau control total.

## G15.4. Training, SOP, support và hypercare — MUST

- [x] SOP theo vai trò và exception.
- [ ] Training có thực hành, bài kiểm tra và attendance.
- [ ] Quick guide tại vị trí làm việc.
- [ ] L1/L2/L3, on-call, SLA và escalation.
- [ ] Không cấp shared admin account.
- [ ] Theo dõi error, latency, throughput, discrepancy và feedback hằng ngày.
- [ ] Reconcile tồn tăng cường trong hypercare.
- [ ] Root-cause và regression cho mọi incident.
- [ ] Kết thúc hypercare khi KPI ổn định trong cửa sổ phê duyệt.

Evidence SOP: `Views/Help/Index.cshtml`, `HUONG_DAN_THUC_HANH_WMS_CHI_TIET.md`, `HUONG_DAN_TOAN_BO_NGHIEP_VU_WMS_FULL.md`, `TAI_LIEU_ONBOARDING_WMS/README.md`, `artifacts/full-audit/FINAL_HELP_AND_SYSTEM_REAUDIT_2026_07_18.md`. Mục này chỉ xác nhận tài liệu theo vai trò/exception; không thay cho training, attendance, UAT hoặc hypercare thật.

---

# MỐC B — GO/NO-GO ENTERPRISE INTERNAL WMS

Chỉ được kết luận **ENTERPRISE INTERNAL WMS READY** khi:

- [ ] Mốc A còn hiệu lực trên cùng release candidate.
- [ ] Gate 8–15 đạt toàn bộ BLOCKER và MUST áp dụng.
- [ ] Internal WMS Readiness = 100%.
- [ ] Evidence Coverage = 100%.
- [ ] Enterprise Parity được báo cáo trung thực.
- [ ] 0 known defect còn mở ở mọi severity.
- [ ] 0 test fail hoặc flaky chưa xử lý.
- [ ] 0 console error, failed resource, visual diff hoặc accessibility violation chưa duyệt.
- [ ] Real-device/network/printer pilot pass trên phạm vi hỗ trợ.
- [ ] Opening balance/master data được đối soát 100% theo control total.
- [ ] Cutover và rollback rehearsal pass.
- [ ] Security/restore/DR/performance/pilot sign-off đầy đủ.
- [ ] Product Owner, quản lý kho, kỹ thuật, security/ops và người quyết định go-live cùng ký.

Nếu thiếu bất kỳ điều kiện nào, kết quả là NO-GO hoặc giữ ở DEVICE-FREE READY; không được làm tròn thành 100%.

---

# QUẢN LÝ ARTIFACT

```text
artifacts/
├── baseline/
├── build-test/
├── full-audit/
├── enterprise-benchmark/
├── scorecards/
├── security/
├── data-quality/
├── transaction-concurrency/
├── e2e/
├── import-export/
├── ocr/
├── integrations/
├── devices/
├── migrations/
├── backup-restore/
├── disaster-recovery/
├── performance/
├── query-plans/
├── visual-regression/
├── ui-cross-device/
├── dashboard-command-center/
├── accessibility/
├── uat/
├── pilot-cutover/
└── release-rehearsal/

docs/
├── domain/
├── architecture/
├── audit/
├── dashboard/
├── ui/
├── security/
├── integrations/
├── devices/
├── runbooks/
├── uat/
└── release/
```

Mỗi artifact phải ghi tối thiểu:

- Commit SHA/build version.
- Thời gian chạy.
- Environment và database/schema version.
- Data profile.
- Người hoặc automation đã chạy.
- Kết quả pass/fail.
- Link tới issue nếu fail.

Không commit secret, raw production backup hoặc dữ liệu cá nhân thật vào repository/artifact.

---

# THỨ TỰ TRIỂN KHAI BẮT BUỘC

1. Gate 0 — Baseline, nguồn dữ liệu chuẩn, state machine và mô hình chấm điểm.
2. Gate 1 — Transaction, concurrency, idempotency và ledger.
3. Gate 2 — Security, permission, database constraints và data-quality.
4. Gate 3 — Business rule và core end-to-end workflow.
5. Gate 4 — Nhập tay, Excel, OCR và export.
6. Gate 5 — CI, migration, deployment, restore, logging, audit và runbook.
7. Gate 6 — Performance, reliability, query và UI quality.
8. Gate 7 — Exception workflow, role UAT và device-free release rehearsal.
9. Mốc A — DEVICE-FREE READY.
10. Gate 8 — Enterprise benchmark và fit-gap.
11. Gate 9 — Advanced warehouse operations.
12. Gate 10 — Multi-warehouse, owner, 3PL và localization.
13. Gate 11 — Integration, API, EDI/event và extensibility.
14. Gate 12 — Device, mobile, printing và automation.
15. Gate 13 — Labor, KPI, optimization và AI.
16. Gate 14 — Enterprise non-functional, security, compliance và resilience.
17. Gate 15 — Data migration, real-device pilot, cutover và hypercare.
18. Mốc B — ENTERPRISE INTERNAL WMS READY hoặc NO-GO.

Không tối ưu UI hoặc thêm tính năng để che một lỗi integrity/security chưa được sửa. Khi gate trước fail, phải sửa nguyên nhân gốc và regression toàn bộ gate bị ảnh hưởng.

---

# VÒNG RÀ SOÁT CUỐI NGÀY 18/07/2026

- Release build: `0 warning / 0 error`.
- Full backend regression cuối: `1116/1116` pass.
- Cross-device có `/Help`: `16/16`; Help visual sau manual review: `4/4`; Command Center: `9/9`; mobile-deep: `424/424`; visual authenticated: `211 pass / 81 conditional skip / 0 fail`; no-device: `10/10`.
- AI Analytics/Risk/Recommendations: `4/4`, `6/6`, `6/6` pass.
- Protected-secret scan gồm generated evidence: `0 match`; SHA-256 `appsettings.json` không đổi.
- Hosting SELECT-only còn 3 vị trí legacy nhiều stock key và 2 opening-ledger row legacy sai intermediate invariant; không direct-update hoặc reload demo.
- Browser role UAT vẫn `BLOCKED` vì fixture cookie hết hạn và không có database local `AUDIT_TEST_*`; không tạo fixture trên hosting.
- Dữ liệu AI vẫn `BLOCKED` cho benchmark/pilot production vì chỉ có một count outcome và thiếu temporal/milestone cohort.

Evidence: `artifacts/full-audit/FINAL_SYSTEM_REAUDIT_2026_07_18.md`, `artifacts/full-audit/FINAL_HELP_AND_SYSTEM_REAUDIT_2026_07_18.md` và `artifacts/full-audit/FINAL_RELEASE_FREEZE_REAUDIT_2026_07_18.md`. Không tick thêm mục phụ thuộc clone, role UAT, dữ liệu lịch sử, thiết bị, pilot hoặc production sign-off.

---

# CẢI TIẾN LIÊN TỤC SAU GO-LIVE

Ngay cả sau khi đạt Enterprise Internal WMS Ready, hệ thống vẫn phải:

- Theo dõi SLO, KPI, incident, security advisory và dependency update.
- Chạy regression định kỳ cho critical workflow và visual baseline.
- Diễn tập restore/DR theo lịch.
- Rà quyền, tài khoản, audit retention và secret rotation định kỳ.
- Reconcile tồn và điều tra mọi chênh lệch.
- Đánh giá lại capability matrix khi nghiệp vụ, quy mô hoặc thiết bị thay đổi.
- Không dùng sign-off cũ để chứng minh cho build mới chưa kiểm tra.

---

# DEFINITION OF DONE CUỐI CÙNG

Roadmap chỉ được đánh dấu hoàn thành khi:

- Tất cả gate bắt buộc pass theo đúng dependency.
- Mọi blocker có automated test ngăn tái phát nếu có thể tự động hóa.
- Kết quả test và audit có thể tái lập trên build đã ký xác nhận.
- Không còn quyết định quan trọng ở trạng thái “tùy”, “nếu cần” hoặc “hợp lý” mà chưa có owner phê duyệt.
- Runbook đủ rõ để một người vận hành khác thực hiện mà không cần tác giả chỉ dẫn trực tiếp.
- Product Owner/người phụ trách kho, kỹ thuật và người chịu trách nhiệm vận hành cùng ký Go/No-Go.

**Kết quả hợp lệ:** NO-GO, DEVICE-FREE READY hoặc ENTERPRISE INTERNAL WMS READY.  
**Chỉ trạng thái ENTERPRISE INTERNAL WMS READY mới tương ứng 100% phạm vi áp dụng tại build và thời điểm ký; không được dùng từ “100%” cho phần chưa test hoặc chỉ có tài liệu.**

> **YÊU CẦU CỦA CHỦ DỰ ÁN:** Không được tự ý xóa, thay đổi, rotate, mask hoặc di chuyển các secret, API key và connection string hiện có trong appsettings vì chúng được giữ để deploy lên hosting T3; chỉ được cảnh báo vị trí/rủi ro mà không hiển thị giá trị và chỉ thay đổi khi chủ dự án xác nhận. Tuyệt đối không sao chép giá trị secret sang log, report, artifact hoặc chat.

> **QUY TẮC CẬP NHẬT CHECKLIST:** Hạng mục nào thực sự hoàn thành và đã có test/bằng chứng đạt yêu cầu thì phải cập nhật ngay từ `- [ ]` thành `- [x]` trong roadmap; không tick trước, không tick giả và không tick mục đang FAIL, BLOCKED, NOT TESTED hoặc chưa có evidence.

> **QUY TẮC CORE WMS:** Phải rà soát, mô tả, triển khai và kiểm thử đầy đủ 100% nghiệp vụ core tại G3.0; không được báo hoàn thành nếu còn một nghiệp vụ core bị thiếu, chỉ có CRUD/menu, sai logic, thiếu dữ liệu, thiếu quyền, thiếu transaction/audit hoặc chưa có automated và Playwright evidence.

> **QUY TẮC TRANG TỔNG QUÁT:** Trước khi sửa Management Command Center phải hoàn thành file inventory, runtime trace, Dashboard File Impact Map và Metric Dictionary; chỉ được sửa đúng file nằm trên runtime path. Trang quản lý phải thể hiện đầy đủ việc hôm nay, nhập, xuất, tồn, chuyển, kiểm kê, điều chỉnh, trả hàng, ngoại lệ, workload và sức khỏe hệ thống theo quyền; mọi KPI phải có drill-down, reconciliation, automated test và Playwright visual evidence.

> **QUY TẮC UI CROSS-DEVICE:** Mọi giao diện desktop, laptop, tablet và mobile trong support matrix phải đạt G6.5; trước khi sửa phải hoàn thành UI File Impact Map và Breakpoint/Reflow Contract. Không suy diễn lỗi mobile từ ảnh desktop hoặc ảnh crop; chỉ ghi finding sau khi xác nhận viewport và tái hiện trên build hiện tại. Không được báo UI 100% nếu còn một route/role/state/viewport chưa test, một screenshot chưa manual-review hoặc còn overflow, clipping, overlap, menu/modal lỗi, typo, accessibility, console hay network error.
