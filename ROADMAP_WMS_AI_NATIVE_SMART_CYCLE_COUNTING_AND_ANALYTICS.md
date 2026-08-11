# ROADMAP WMS AI-NATIVE SMART CYCLE COUNTING VÀ THỐNG KÊ KHO

> Phiên bản: 1.0  
> Ngày lập: 14/07/2026  
> Trạng thái: `PLANNING - CHƯA TRIỂN KHAI`  
> Phạm vi: AI chấm điểm rủi ro sai lệch tồn kho, đề xuất kiểm kê và hệ thống thống kê nghiệp vụ kho  
> Liên kết roadmap chính: `ROADMAP_WMS_ENTERPRISE_100_PERCENT_FULL.md`, trọng tâm G3.0 và G13.3

---

## 1. Mục tiêu

Xây dựng một năng lực **AI Smart Cycle Counting** có thể:

1. Quan sát lịch sử nhập, xuất, chuyển, điều chỉnh, giữ chỗ và kiểm kê.
2. Ước lượng rủi ro sai lệch cho từng tổ hợp `Kho - Chủ hàng - Mã hàng - Vị trí - Lô/HSD` tại một thời điểm xác định.
3. Giải thích các yếu tố làm tăng rủi ro bằng dữ liệu có thể truy vết.
4. Đề xuất danh sách và thứ tự kiểm kê trong giới hạn nhân lực được giao.
5. Cho quản lý duyệt, sửa hoặc từ chối đề xuất trước khi tạo phiếu kiểm kê.
6. Dùng kết quả kiểm kê đã duyệt làm phản hồi để đánh giá và cải thiện mô hình.
7. Quay về chiến lược ABC/quy tắc khi mô hình thiếu dữ liệu, lỗi, quá cũ hoặc không đạt ngưỡng tin cậy.
8. Tuyệt đối không để AI tự ghi sổ, tự điều chỉnh tồn, tự duyệt chênh lệch hoặc bỏ qua phân quyền.

Song song, xây dựng bộ thống kê kho có công thức, nguồn dữ liệu, mẫu số, điều kiện loại trừ và drill-down rõ ràng. Mục tiêu của thống kê là:

> Thấy vấn đề -> hiểu nguyên nhân -> mở dữ liệu nguồn -> thực hiện hành động có kiểm soát.

Roadmap này không coi biểu đồ nhiều màu, một cột điểm rủi ro hoặc chatbot là bằng chứng của một hệ thống AI-native.

---

## 2. Phạm vi và giới hạn

### 2.1. Trong phạm vi

- Độ chính xác và sai lệch tồn kho.
- Tồn kho tại một thời điểm và sổ giao dịch tồn.
- Tuổi tồn, hàng chậm, hàng chết, cận hạn và FEFO.
- Thời gian xử lý nhập, xuất và chất lượng hoàn thành đơn.
- ABC-XYZ, Pareto nguyên nhân, ngoại lệ thống kê và công suất vị trí.
- Năng suất theo workload đã chuẩn hóa, hiệu suất nhà cung cấp và nguy cơ thiếu hàng.
- Trích xuất đặc trưng, huấn luyện, đánh giá, phiên bản hóa, suy luận, giải thích và giám sát mô hình.
- Quy trình duyệt đề xuất, tạo phiếu kiểm kê, phản hồi và audit trail.
- Phân quyền theo kho/chủ hàng và kiểm thử UI đa thiết bị.

### 2.2. Ngoài phạm vi của phiên bản đầu

- AI tự động điều chỉnh tồn kho.
- AI tự động post/hủy/reversal/chốt kỳ.
- Deep learning hoặc mô hình ngôn ngữ lớn làm bộ chấm điểm cốt lõi.
- Gọi API AI bên ngoài cho mỗi giao dịch kho.
- Xếp hạng nhân viên chỉ bằng số task/giờ.
- Dự báo mua hàng đầy đủ như ERP/MRP.
- Điều khiển robot, drone, cân điện tử hoặc thiết bị kho thật.
- Thay đổi schema hosting trước khi migration được kiểm chứng trên clone dùng một lần.

### 2.3. Nguyên tắc an toàn

- Giữ nguyên secret, API key và connection string trong `appsettings*`.
- Không đưa secret vào log, model artifact, dataset, report hoặc ảnh kiểm thử.
- Database hosting chỉ dùng đọc/đối soát có giới hạn trong giai đoạn audit và phát triển.
- Huấn luyện bằng dữ liệu clone, bản xuất đã loại dữ liệu nhạy cảm hoặc dữ liệu demo được gắn nhãn rõ.
- Không sửa trực tiếp số tồn để làm đẹp KPI hoặc tạo nhãn huấn luyện.
- Không tick `[x]` khi chưa có test pass và evidence của chính build hiện tại.
- Không tuyên bố “0 bug”, “100%” hoặc “enterprise-ready” chỉ dựa trên việc build thành công.

---

## 3. Kết quả audit baseline hiện tại

### 3.1. Chú giải trạng thái

- `CONFIRMED`: đã truy vết trực tiếp trong source hiện tại.
- `SUSPECTED`: có dấu hiệu nhưng cần test/runtime/SQL để kết luận.
- `UNKNOWN`: chưa có đủ dữ liệu để đánh giá.
- `BLOCKED`: phụ thuộc dữ liệu, thiết bị, hạ tầng hoặc phê duyệt bên ngoài repository.

### 3.2. Nền tảng có thể tái sử dụng

| Trạng thái | Thành phần hiện có | Evidence source | Kết luận |
|---|---|---|---|
| `CONFIRMED` | Ledger tồn kho | `Models/InventoryTransaction.cs`, `InventoryTransaction` | Có kho, chủ hàng, mã hàng, vị trí, lô, HSD, delta, before/after, actor, source reference và thời điểm; đây là nguồn chính cho feature lịch sử và reconciliation. |
| `CONFIRMED` | Kết quả kiểm kê | `Models/StockCountSheet.cs`, `Models/StockCountLine.cs` | Có số hệ thống, số thực đếm, sai lệch, người đếm, thời điểm, vị trí, lô/HSD; có thể tạo ground truth sau khi quy trình duyệt được xác minh. |
| `CONFIRMED` | Chương trình kiểm kê chu kỳ | `Models/AdvancedWmsModels.cs`, `CycleCountProgram`, `CycleCountSchedule` | Đã có tần suất ABC, ngày đến hạn và sai lệch tích lũy. |
| `CONFIRMED` | Bộ lập kế hoạch kiểm kê | `Services/CoreWmsServices.cs`, `CycleCountPlanningService.GenerateDueSheetAsync` | Đã tạo phiếu theo ABC và sai lệch trong transaction `Serializable`; có thể mở rộng sau khi sửa đúng ngữ nghĩa ngày kiểm kê. |
| `CONFIRMED` | Cảnh báo dự báo | `Services/Enterprise1113Services.cs`, `EnterpriseAnalyticsService.BuildPredictiveAlertsAsync` | Đã có pipeline tạo cảnh báo và UI, nhưng điểm hiện là luật cố định, chưa phải mô hình học máy. |
| `CONFIRMED` | Bản ghi cảnh báo | `Models/AnalyticsUxProductionEnterpriseModels.cs`, `EnterprisePredictiveAlert` | Có risk score, scope và citation; dùng được cho cảnh báo tổng quát nhưng chưa đủ để lưu model version, feature snapshot, feedback và evaluation. |
| `CONFIRMED` | Trợ lý dữ liệu nội bộ | `EnterpriseAnalyticsService.AskAssistantAsync`, `Views/Reports/AiAssistant.cshtml` | Là giao diện hỏi đáp có kiểm soát và citation; không nên dùng thay cho risk-scoring engine. |
| `CONFIRMED` | Quyền báo cáo và kiểm kê | `WmsPermissions.ReportView`, `WmsPermissions.StockCountApprove`, controller authorization | Có nền RBAC; cần tách quyền xem đề xuất, duyệt đề xuất và quản trị mô hình. |

### 3.2.1. Bản đồ tái sử dụng báo cáo và chức năng "AI" hiện có

| Thành phần hiện có | Điều hướng và runtime path đã xác nhận | Phạm vi thật sự hiện tại | Quyết định trong roadmap này |
|---|---|---|---|
| `Tổng quan kho` | Sidebar `Báo cáo` → `/Reports/WarehouseOverview` → `ReportsController.WarehouseOverview` → `BuildWarehouseOverviewModelAsync` → `Views/Reports/WarehouseOverview.cshtml` | Cockpit quản lý: tồn hiện tại, giữ chỗ, khả dụng, giá trị tồn theo quyền, dòng nhập/xuất theo kỳ, phiếu mở, mã hàng phát sinh nhiều và ngoại lệ dữ liệu. | **Giữ nguyên làm trang tổng quan chính.** Không tạo thêm một dashboard tổng quan kho thứ hai. Các KPI mới chỉ được bổ sung hoặc drill-down từ route này sau khi có metric contract và reconciliation. |
| `Thống kê nhập/xuất` | Sidebar `Báo cáo` → `/Reports/InventoryInOutSummary` → `BuildInventoryInOutSummaryModelAsync` → `InventoryTransactions` → `Views/Reports/InventoryInOutSummary.cshtml` | Báo cáo chi tiết theo kỳ từ ledger, có lọc kho/chủ hàng/vật tư/vị trí/lô/loại giao dịch, tổng nhập, tổng xuất, chênh lệch và xuất Excel. | **Giữ nguyên làm sổ giao dịch và drill-down nhập/xuất.** Không tạo báo cáo nhập/xuất trùng chức năng. |
| `Báo cáo quản trị dữ liệu` | Sidebar `Báo cáo` → `/Reports/SemanticBi`; từ trang này có nút sang `/Reports/PredictiveAlerts` | Lớp metric quản trị hiện có và điểm vào cảnh báo vận hành. | Tái sử dụng làm report hub/metric dictionary khi phù hợp; không biến thành một dashboard trùng `WarehouseOverview`. |
| `Cảnh báo dự báo` | `/Reports/PredictiveAlerts`; truy cập từ `SemanticBi` và action của bàn làm việc quản lý, không có mục con trực tiếp trong sidebar Báo cáo | Luật phát hiện thiếu hàng, cận hạn, trễ SLA và quá tải; điểm hiện dùng giá trị/ngưỡng cố định. | Định danh rõ là **cảnh báo theo quy tắc** cho tới khi có model, calibration và model version. Không dùng nó thay cho AI dự đoán sai lệch tồn kho. |
| `Trợ lý dữ liệu nội bộ` | `/Reports/AiAssistant` và POST `/Reports/AskAiAssistant`; hiện không có link trực tiếp trong sidebar. View có gợi ý `Tồn kho còn bao nhiêu?` | Trợ lý chỉ đọc, phân loại một số ý định cố định, tổng hợp Semantic BI/cảnh báo và lưu citation; chặn yêu cầu thay đổi dữ liệu. | Giữ làm lớp hỏi đáp có kiểm soát. Có thể trích dẫn kết quả kiểm kê thông minh sau này, nhưng không sở hữu feature pipeline, risk score hay quyết định tạo phiếu. |
| `AI Smart Cycle Counting` đề xuất | Chưa có route/workflow chuyên biệt trong source hiện tại | Dự đoán xác suất/rủi ro sai lệch tại grain đã chốt, giải thích nguyên nhân và đưa đề xuất qua human approval. | Đây là năng lực mới, không trùng `Tổng quan kho`, `Thống kê nhập/xuất`, `Cảnh báo dự báo` hoặc `Trợ lý dữ liệu nội bộ`. |

Quyết định chống trùng chức năng:

1. `WarehouseOverview` là cửa vào tổng quan; `InventoryInOutSummary` là drill-down ledger theo kỳ.
2. `STAT-01..15` là **hợp đồng chỉ số và phần mở rộng có kiểm soát**, không đồng nghĩa với 15 trang hoặc một bộ menu báo cáo mới.
3. Mỗi STAT phải được gắn vào route hiện có phù hợp trước; chỉ tạo route mới khi impact map chứng minh không có màn hình đích tương thích.
4. Không hiển thị điểm luật cố định dưới nghĩa xác suất AI đã hiệu chỉnh.
5. Test hiện có đã bao phủ nghiệp vụ chính của hai route báo cáo và một phần scope/citation của cảnh báo, trợ lý; vẫn phải bổ sung regression cho lifecycle cảnh báo, owner scope và cross-device trước khi tái sử dụng trong AI-native.

### 3.3. Vấn đề phải xử lý trước hoặc cùng lúc triển khai

| ID | Mức | Trạng thái | Finding | Ảnh hưởng |
|---|---|---|---|---|
| `AI-BL-01` | P0 dữ liệu AI | `CONFIRMED` | `CycleCountPlanningService.GenerateDueSheetAsync` gán `CycleCountSchedule.LastCountedAt` ngay khi **tạo** phiếu tại `Services/CoreWmsServices.cs:724`, trước khi kiểm đếm và duyệt hoàn tất. | Feature “số ngày từ lần kiểm kê cuối” và lịch kế tiếp có thể sai; gây leakage/ngữ nghĩa nhãn sai. |
| `AI-BL-02` | P1 | `CONFIRMED` | `CycleCountSchedule` định danh đến `Item/Owner/Location`, trong khi `StockCountLine` và ledger có `Lot/ExpiryDate`. | Yêu cầu AI là SKU-vị trí-lô; phải quyết định rõ mức dự đoán và cách tổng hợp, không gắn nhãn lô giả. |
| `AI-BL-03` | P1 | `CONFIRMED` | `BuildPredictiveAlertsAsync` dùng các điểm 95/90/70/92 và ngưỡng hard-code. | Đây là rule-based baseline, không phải xác suất đã hiệu chỉnh hoặc mô hình tự học. |
| `STAT-BL-01` | P0 KPI | `CONFIRMED` | `ReportsController.Analytics` tính `DaysOfSupply = tổng số lượng tồn / số phiếu xuất trung bình mỗi ngày`. | Sai thứ nguyên; phải dùng số lượng xuất base-UOM theo mã hàng/phạm vi tương thích. |
| `STAT-BL-02` | P1 KPI | `CONFIRMED` | `SlowMovingReport` lấy lần giao dịch cuối từ mọi `VoucherDetail` đã post. | Phiếu nhập có thể làm mới “lần luân chuyển”; phải tách lần nhận gần nhất và lần xuất/tiêu thụ gần nhất từ ledger. |
| `STAT-BL-03` | P1 KPI | `CONFIRMED` | `AbcAnalysis` xếp hạng theo giá trị tồn hiện tại và ngưỡng 80/95 hard-code. | Đây là ABC giá trị tồn, không phải ABC giá trị sử dụng; phải đổi nhãn hoặc bổ sung loại ABC và ngưỡng cấu hình. |
| `STAT-BL-04` | P0 KPI | `CONFIRMED` | `SpaceUtilization` cộng số lượng khác UOM và thay capacity thiếu bằng `100`. | Tỷ lệ công suất có thể vô nghĩa; không được hiển thị capacity giả. |
| `STAT-BL-05` | P1 KPI | `CONFIRMED` | `DockToStock` dùng timestamp fallback và chỉ hiển thị trung bình. | Có thể tạo duration suy diễn; cần cờ completeness, median, P90/P95 và số mẫu. |
| `STAT-BL-06` | P1 KPI | `CONFIRMED` | KPI `FillRate` hiện là tỷ lệ reservation đã consume/tổng reserve. | Phải giữ tên “tỷ lệ đáp ứng giữ chỗ”; không được trình bày như order-line fill rate. |
| `AI-BL-04` | P1 quản trị | `CONFIRMED` | `EnterprisePredictiveAlert` không có model version, prediction cutoff, feature hash, confidence calibration, decision/override và outcome. | Không đủ audit trail cho AI-native; cần thiết kế additive, không sửa phá contract cũ. |
| `AI-BL-05` | P1 dependency | `CONFIRMED` | `WMS.csproj` hiện chưa tham chiếu ML.NET. | Chỉ thêm package sau khi so sánh phương án, pin version, kiểm license/vulnerability và chứng minh cần thiết. |
| `AI-BL-06` | P1 lifecycle | `CONFIRMED` | `UpsertPredictiveAlertsAsync` chỉ kiểm tra alert mở đã tồn tại rồi bỏ qua; chưa refresh score/message/citation và chưa đóng alert không còn điều kiện. | Danh sách cảnh báo mở có thể giữ dữ liệu cũ; phải có lifecycle idempotent, freshness và regression test trước khi dùng làm nguồn AI/trợ lý. |
| `AI-BL-07` | P0 scope | `SUSPECTED` | `BuildPredictiveAlertsAsync` chỉ nhận `warehouseId`; luồng expiry/capacity truyền `OwnerPartnerId = null` và action chưa truyền owner scope. | Có nguy cơ trả cảnh báo chéo chủ hàng trong mô hình nhiều chủ hàng; phải xác minh bằng direct URL/service/SQL test rồi mới chọn cách sửa. |

### 3.4. Điều chưa được phép kết luận từ audit source

- Chưa kết luận dữ liệu lịch sử đủ để huấn luyện mô hình có ý nghĩa.
- Chưa kết luận sai lệch kiểm kê trong DB hosting là ground truth sạch.
- Chưa kết luận cần migration nào trước khi lập schema impact map và chạy clone migration rehearsal.
- Chưa kết luận Logistic Regression, FastTree hay FastForest là model chiến thắng trước benchmark.
- Chưa kết luận AI tốt hơn ABC/ngẫu nhiên trước temporal holdout và thử nghiệm thực tế.
- Chưa kết luận các KPI hiện có đúng chỉ vì UI đã hiển thị được số liệu.

---

## 4. Đối chiếu thông lệ chính thức

Ngày truy cập: 14/07/2026.

1. [Oracle WMS Intelligent Cycle Counting](https://docs.oracle.com/en/cloud/saas/warehouse-management/26b/owaig/intelligent-cycle-counting.html) mô tả việc dùng hành vi tồn kho thời gian thực, lịch sử độ chính xác, picks/returns, adjustments và mức độ hoạt động của vị trí để ưu tiên danh sách kiểm kê.
2. [Microsoft Dynamics 365 Cycle Counting](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/cycle-counting) tách ba bước: tạo công việc, thực hiện đếm và xử lý chênh lệch; hỗ trợ threshold, kế hoạch định kỳ, blind count và pending review.
3. [Oracle WMS Cycle Count Inventory Updates](https://docs.oracle.com/en/cloud/saas/warehouse-management/25d/owmol/reinitiate-in-progress-deferred-cycle-counts.html) cho phép duyệt/từ chối adjustment trước khi thay đổi tồn ở chế độ deferred.
4. [Microsoft ML.NET Machine Learning Tasks](https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks) xác định bài toán nhị phân phù hợp với dự đoán “có/không có sai lệch” và cung cấp các trainer như logistic regression, FastTree và FastForest.
5. [ML.NET Permutation Feature Importance](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/explain-machine-learning-model-permutation-feature-importance-ml-net) cung cấp cách đánh giá mức đóng góp tương đối của feature; đây là giải thích toàn cục, không được trình bày giả như nguyên nhân tuyệt đối của từng dự đoán.
6. [NIST AI RMF Core](https://airc.nist.gov/airmf-resources/airmf/5-sec-core/) yêu cầu quản trị, lập bản đồ rủi ro, đo lường và quản lý xuyên suốt vòng đời; model cần test trước triển khai và giám sát khi vận hành.
7. [NIST AI RMF Measure Playbook](https://airc.nist.gov/airmf-resources/playbook/measure/) nhấn mạnh drift, lịch sử đo lường, audit log và so sánh hiệu năng production với pre-deployment.
8. [Microsoft Inventory Aging Report](https://learn.microsoft.com/en-us/dynamics365/supply-chain/cost-management/inventory-aging-report) tính tuổi tồn từ giao dịch nhận/phát và bucket theo “as of”, thay vì chỉ lấy số tồn hiện tại.
9. [NIST Outlier Criteria](https://www.itl.nist.gov/div898/handbook/prc/section1/prc16.htm) mô tả IQR và các fence; outlier phải được điều tra, không tự động xóa hoặc coi là gian lận.

### Kết luận áp dụng cho WMS này

- AI chỉ **đề xuất và ưu tiên**; con người duyệt và workflow kiểm kê hiện có thực thi.
- Chênh lệch phải vào hàng chờ duyệt; không điều chỉnh trực tiếp từ prediction.
- Cần blind count cho nhân viên thực hiện khi policy yêu cầu.
- Risk score không phải “xác suất” nếu chưa calibration và kiểm chứng.
- Mọi explanation phải trỏ về feature snapshot và giao dịch nguồn.
- Mô hình phải có fallback, version, drift monitoring, override và decommission plan.

---

## 5. Hợp đồng AI-native

Một phiên bản chỉ được gọi là AI-native khi đạt đầy đủ các lớp sau:

| Lớp | Hành vi bắt buộc | Không đạt nếu |
|---|---|---|
| Quan sát | Đọc dữ liệu ledger, kiểm kê và audit đến prediction cutoff. | Dùng số liệu nhập tay hoặc future data. |
| Dự đoán | Xếp hạng rủi ro ở scope đã xác định, có model/rule version. | Chỉ dùng điểm hard-code nhưng gọi là AI probability. |
| Giải thích | Hiển thị reason code, giá trị feature và nguồn drill-down. | Sinh lời giải thích chung chung hoặc không khớp snapshot. |
| Lập kế hoạch | Tối ưu danh sách trong budget số dòng/thời gian/kho/zone. | Chỉ hiển thị điểm nhưng không nối vào workflow. |
| Human-in-the-loop | Quản lý duyệt, sửa, từ chối và ghi lý do. | AI tự tạo adjustment hoặc tự post. |
| Phản hồi | Gắn kết quả kiểm kê cuối cùng với prediction ban đầu. | Ghi đè prediction hoặc mất lịch sử. |
| Học thích nghi | Tái huấn luyện có điều kiện và benchmark champion/challenger. | Tự động thay model production không qua gate. |
| Giám sát | Theo dõi precision, lift, calibration, drift, freshness và lỗi inference. | Chỉ theo dõi uptime endpoint. |
| Fallback | Quay về ABC/rule-based khi model không hợp lệ. | Chặn vận hành khi AI lỗi. |
| Audit | Lưu cutoff, scope, version, feature hash, output, người duyệt và outcome. | Không tái dựng được lý do của một đề xuất cũ. |

---

## 6. Hợp đồng dữ liệu và nhãn

### 6.1. Đơn vị dự đoán

Mặc định đề xuất:

`WarehouseId + OwnerPartnerId + ItemId + LocationId + LotNumber + ExpiryDate + PredictionCutoff`

Quy tắc:

- Với hàng không quản lý lô, `LotNumber/ExpiryDate` là rỗng có chủ đích.
- Với kiểm kê toàn vị trí, risk có thể tổng hợp từ các dòng item-lot nhưng phải lưu cả child contribution.
- Không trộn chủ hàng khi chấm điểm hoặc hiển thị.
- Không trộn số lượng khác base UOM nếu chưa quy đổi bằng bảng conversion có hiệu lực tại cutoff.
- Serial/LPN là feature và drill-down; việc dự đoán đến từng serial chỉ xem xét khi dữ liệu đủ lớn.

### 6.2. Prediction cutoff và chống data leakage

- Mọi feature phải có `event_time <= prediction_cutoff`.
- Nhãn chỉ lấy từ kiểm kê hoàn tất/được duyệt sau cutoff trong prediction horizon.
- Không dùng `CountedQty`, `Variance`, adjustment hoặc reason code sinh ra sau lần kiểm kê làm feature cho chính prediction đó.
- Chia train/validation/test theo thời gian, không random split làm lẫn tương lai vào quá khứ.
- Khi cùng SKU-vị trí xuất hiện nhiều lần, dùng grouped temporal split để giảm leakage.
- Snapshot feature phải immutable hoặc có hash để tái dựng.

### 6.3. Định nghĩa nhãn tối thiểu

Hai nhãn cần lưu riêng:

1. `HasQuantityVariance`: `abs(CountedQty - SystemQty) > tolerance theo UOM`.
2. `HasMaterialVariance`: sai lệch vượt một trong các ngưỡng số lượng, phần trăm hoặc giá trị được cấu hình.

Không gộp hai khái niệm. Một lệch rất nhỏ có thể là sai lệch kỹ thuật nhưng không phải sai lệch trọng yếu.

### 6.4. Feature dictionary phiên bản đầu

| Nhóm | Feature ứng viên | Nguồn chính | Cửa sổ |
|---|---|---|---|
| Giao dịch | số giao dịch, tổng delta tuyệt đối, số nhập/xuất/chuyển | `InventoryTransactions` | 1/7/30/90 ngày |
| Điều chỉnh | số lần, tổng lượng, tổng giá trị, reason code | ledger + audit/adjustment | 30/90/180 ngày |
| Kiểm kê | ngày từ lần kiểm kê **đã hoàn tất**, số lần lệch, độ lệch tuyệt đối/tương đối | `StockCountSheet/Line` | lịch sử có cutoff |
| Tác nghiệp | số actor khác nhau, số thao tác ngoài giờ, số retry/cancel | ledger + `AuditLogs` | 7/30 ngày |
| Vị trí | pick-face/reserve, traffic, zone, số SKU, mức sử dụng hợp lệ | `Locations`, ledger | snapshot/cửa sổ |
| Mã hàng | ABC/XYZ, giá trị, velocity, yêu cầu lot/HSD/serial | `Items` + analytics | snapshot/cửa sổ |
| Tracking | số lô/serial/LPN, hold/QC/quarantine, ngày tới HSD | `ItemLocations` + tracking | snapshot |
| Chất lượng dữ liệu | thiếu source, mismatch ledger/balance, timestamp không đủ | DQ queries | snapshot |

Feature DQ không được làm AI “đoán bù” cho dữ liệu hỏng. Nếu integrity fail, hệ thống phải gắn `DATA_QUALITY_BLOCKED` và đưa vào ngoại lệ vận hành.

---

## 7. Chiến lược mô hình

### 7.1. Các phương án phải benchmark

| Phương án | Vai trò | Ưu điểm | Giới hạn |
|---|---|---|---|
| Random | Control A | Baseline khách quan tối thiểu | Không dùng nghiệp vụ. |
| ABC + lịch đến hạn | Control B | Phù hợp WMS truyền thống, dễ giải thích | Không phản ứng tốt với rủi ro động. |
| Rule-based risk | Fallback/champion ban đầu | Không cần package ML, deterministic | Trọng số chủ quan, không tự học. |
| Logistic Regression | ML candidate 1 | Xác suất dễ calibration, hệ số dễ trình bày | Khó bắt quan hệ phi tuyến. |
| Decision Tree/FastTree | ML candidate 2 | Dễ diễn giải các ngưỡng | Có thể overfit. |
| Random Forest/FastForest | Challenger | Có thể cải thiện ranking | Giải thích cục bộ và calibration khó hơn. |

### 7.2. Lựa chọn công nghệ

- Ưu tiên inference cục bộ trong ứng dụng hoặc worker, không phụ thuộc OCR/Groq/Gemini.
- ML.NET là ứng viên tự nhiên vì ứng dụng .NET và có trainer/metric/PFI chính thức.
- Chưa thêm dependency trong giai đoạn roadmap.
- Trước khi thêm ML.NET phải kiểm version tương thích .NET 8, license, vulnerability, kích thước artifact, thời gian inference và quy trình restore locked.
- Nếu dữ liệu chưa đủ, giữ rule-based/ABC và gắn nhãn rõ `BASELINE`, không giả lập model AI.

### 7.3. Metric đánh giá mô hình

Metric chính là ranking value, không dùng accuracy tổng thể làm chỉ số duy nhất vì lớp sai lệch thường hiếm.

- `Precision@K = số đối tượng có sai lệch trong Top K / K`.
- `Recall@K = số sai lệch phát hiện trong Top K / tổng sai lệch trong tập đánh giá`.
- `Lift@K = Precision@K của AI / prevalence của tập đánh giá`.
- Giá trị sai lệch tuyệt đối phát hiện trong Top K.
- Số lượt kiểm kê cần để phát hiện một sai lệch.
- PR-AUC; ROC-AUC chỉ là metric bổ sung.
- Brier score/calibration curve nếu hiển thị risk dưới dạng xác suất.
- Coverage: tỷ lệ đối tượng có thể chấm điểm hợp lệ.
- Stability theo kho, chủ hàng, nhóm ABC/XYZ và khoảng thời gian.
- Latency và failure rate của batch scoring.

### 7.4. Điều kiện model được promote

- Temporal test chưa từng dùng để tune.
- Không thấp hơn ABC ở metric an toàn đã chốt.
- `Precision@K` hoặc `Lift@K` đạt ngưỡng do owner nghiệp vụ phê duyệt.
- Calibration đủ để gọi output là “xác suất”; nếu không chỉ gọi “điểm rủi ro”.
- Không có regression phân quyền/scope.
- Explanation và feature provenance đầy đủ.
- Shadow-mode pass trước khi tạo đề xuất thật.
- Có rollback một thao tác về model/rule champion trước.

---

## 8. Workflow nghiệp vụ đề xuất

### 8.1. State machine

`Generated -> PendingReview -> Approved | Modified | Rejected -> CountSheetCreated -> InProgress -> PendingVarianceReview -> Reconciled -> Closed`

Trạng thái bổ sung:

- `Expired`: đề xuất quá freshness window.
- `Invalidated`: phát sinh giao dịch làm thay đổi snapshot trước khi duyệt/tạo phiếu.
- `Fallback`: được tạo bởi ABC/rule vì model không khả dụng.
- `BlockedByDataQuality`: không được dùng để tạo phiếu cho đến khi DQ được xử lý.

### 8.2. Quy tắc chuyển trạng thái

- Batch scoring chỉ tạo `Generated/PendingReview`.
- Người có quyền duyệt mới được `Approve/Modify/Reject`.
- Tạo phiếu kiểm kê phải idempotent theo recommendation ID.
- Không tạo hai phiếu active trùng cùng scope nếu policy không cho phép.
- Khi stock movement phát sinh sau snapshot, hệ thống phải tính lại hoặc cảnh báo stale; không âm thầm dùng score cũ.
- Nhân viên kiểm kê không thấy `SystemQty` khi chương trình blind count.
- Sai lệch vượt tolerance phải vào pending review.
- Người kiểm đếm và người duyệt adjustment phải tuân thủ SoD hiện có.
- `LastCountedAt` chỉ cập nhật sau mốc kiểm kê được định nghĩa là hoàn tất/được duyệt, không cập nhật lúc tạo phiếu.
- AI outcome chỉ được ghi sau khi count outcome ổn định; không lấy phiếu hủy/draft làm nhãn dương hoặc âm.

### 8.3. Human override

Mọi quyết định sửa/từ chối đề xuất phải lưu:

- Người thực hiện và thời điểm.
- Quyết định trước/sau.
- Reason code có cấu trúc.
- Ghi chú tùy chọn.
- Scope và model version.
- Không dùng override làm nhãn “AI sai” nếu chưa có kết quả kiểm kê thực tế.

---

## 9. Thiết kế dữ liệu dự kiến

Đây là thiết kế ứng viên, chưa phải quyết định migration.

| Entity ứng viên | Mục đích tối thiểu |
|---|---|
| `InventoryRiskModelVersion` | model/rule type, version, training cutoff, feature schema, metrics, artifact hash, trạng thái champion/challenger/retired |
| `InventoryRiskFeatureSnapshot` | prediction cutoff, scope key, feature JSON/cột chọn lọc, source watermark, feature hash, DQ status |
| `InventoryRiskPrediction` | model version, risk score/probability, severity, top reason codes, freshness, generated time |
| `CycleCountRecommendation` | prediction, priority, estimated effort, state, assignee/work pool, approved/modified/rejected metadata |
| `CycleCountRecommendationDecision` | immutable decision/override history |
| `InventoryRiskOutcome` | link prediction -> stock count line/sheet -> final variance/value/reason |
| `InventoryRiskEvaluationSnapshot` | Precision@K, lift, calibration, coverage, slice metrics và drift theo kỳ |

Quy tắc schema:

- Ưu tiên bảng additive, FK/index rõ ràng và không thay public contract cũ.
- Không dùng `EnterprisePredictiveAlert` làm kho duy nhất cho feature/prediction/model lifecycle.
- Có thể phát sinh `EnterprisePredictiveAlert` từ recommendation để tái sử dụng màn hình cảnh báo, nhưng alert chỉ là projection.
- Migration phải có precheck, script tương thích, test clean/upgrade DB, rollback hoặc forward-fix.
- Hosting migration là checkpoint riêng, không tự động áp dụng.

---

## 10. Hệ thống thống kê đề xuất

### 10.1. Hợp đồng chung cho mọi KPI

Mỗi KPI phải có:

- Tên nghiệp vụ và tên kỹ thuật.
- Câu hỏi nghiệp vụ được trả lời.
- Công thức tử số/mẫu số.
- Grain và nguồn dữ liệu chuẩn.
- Event time/date field.
- Kho, chủ hàng, zone, item, lot/serial và role scope.
- UOM/currency/cost snapshot.
- Điều kiện include/exclude.
- Cách xử lý partial, cancel, reversal, return và adjustment.
- Quy tắc dữ liệu thiếu.
- Số mẫu, median, P90/P95 khi đo duration.
- Freshness và thời điểm snapshot.
- Drill-down đến ledger/chứng từ/audit nguồn.
- Test reconciliation độc lập.

Không lấy trung bình của các tỷ lệ trung bình. Phải cộng tử số và mẫu số ở grain chuẩn rồi mới chia.

### 10.2. STAT-01 - Độ chính xác và sai lệch tồn kho

Hiển thị:

- Tỷ lệ dòng kiểm kê khớp hoàn toàn.
- Độ chính xác theo số lượng.
- Tổng thừa, thiếu và giá trị tuyệt đối.
- Sai lệch theo kho, owner, zone, vị trí, SKU, lot, người/ca và reason code.
- Sai lệch lặp lại tại cùng một scope.

Công thức đề xuất:

- `LineAccuracy = ExactCountLines / EligibleCompletedCountLines * 100`.
- `QuantityAccuracy = max(0, 1 - Sum(abs(CountedQty-SystemQty)) / Sum(max(abs(CountedQty), abs(SystemQty)))) * 100`.
- Khi mẫu số bằng 0, hiển thị `N/A`, không hiển thị 100% giả.
- `VarianceValue = Sum(abs(VarianceQty) * UnitCostAtCountSnapshot)`.

### 10.3. STAT-02 - Tồn kho tại một thời điểm và sổ giao dịch

- Chọn thời điểm, kho, owner, item, vị trí, lot/HSD, serial/LPN và hold status.
- Tồn đầu + nhập - xuất + chuyển vào - chuyển ra +/- điều chỉnh = tồn cuối.
- Tính từ `InventoryTransactions` hoặc snapshot bất biến đã reconcile.
- Mỗi bucket drill-down tới transaction, voucher, task và actor.
- Có cảnh báo nếu ledger và balance hiện tại không khớp.

### 10.4. STAT-03 - Tuổi tồn, hàng chậm và hàng chết

- Bucket 0-30, 31-60, 61-90, 91-180 và >180 ngày, cấu hình được.
- Tách `ngày nhận gần nhất`, `ngày xuất gần nhất`, `số ngày không xuất` và tuổi lượng tồn còn lại.
- Số lượng, giá trị, tỷ trọng và vị trí chiếm dụng.
- Không dùng một phiếu nhập mới để xóa lịch sử “không xuất” của hàng chậm.

### 10.5. STAT-04 - Cận hạn, hết hạn và FEFO compliance

- Bucket đã hết hạn, 0-7, 8-30, 31-60 và >60 ngày.
- Tồn có HSD thiếu dù item yêu cầu quản lý HSD.
- Giá trị cận hạn, hold/QC quá lâu và tồn hết hạn còn khả dụng.
- FEFO chỉ kết luận vi phạm sau khi tái dựng eligible lots tại allocation/pick cutoff, loại lô hold/quarantine/reserved không hợp lệ.

### 10.6. STAT-05 - Thời gian xử lý nhập kho

Milestone:

`Arrival -> ReceiveStart -> ReceiveComplete -> QcComplete -> PutawayCreated -> PutawayComplete/Posted`

Mỗi đoạn và tổng dock-to-stock có:

- Median, P90, P95, min/max và sample count.
- Tỷ lệ đạt SLA.
- Dữ liệu thiếu milestone được đưa vào “không đủ dữ liệu”, không fallback âm thầm.
- Drill-down từng voucher/task.

### 10.7. STAT-06 - Thời gian xử lý xuất kho

Milestone:

`Release -> Allocation -> Wave -> PickStart -> PickComplete -> PackComplete -> ShipConfirm`

- Tách thời gian thao tác và thời gian chờ.
- Median/P90/P95/sample count/SLA theo kho, zone, ca và loại đơn.
- Partial/cancel/retry phải có rule riêng.

### 10.8. STAT-07 - Chất lượng hoàn thành đơn xuất

- Order complete rate.
- `LineFillRate = fully fulfilled eligible lines / eligible order lines`.
- On-time ship rate, short/partial/cancel/rework rate.
- Perfect order chỉ tính khi có đủ on-time, complete, damage-free và document-accurate.
- Giữ KPI reservation consumption hiện có dưới tên “Tỷ lệ đáp ứng giữ chỗ”, không trộn với line fill rate.

### 10.9. STAT-08 - ABC-XYZ

- Phân biệt `ABC giá trị tồn` và `ABC giá trị sử dụng = lượng xuất base-UOM * cost snapshot`.
- Ngưỡng A/B/C cấu hình theo warehouse/owner/program; không hard-code 80/95 trong controller.
- XYZ dựa trên biến động nhu cầu theo bucket thời gian đã chốt, có min sample và xử lý mean=0.
- Kết quả AX..CZ dùng làm feature và fallback policy, không tự động thay đổi tồn.

### 10.10. STAT-09 - Pareto nguyên nhân sai lệch

- Reason taxonomy có cấu trúc: nhận, pick, putaway, move, UOM, lot/serial, duplicate, cancel/reversal, damage/loss, manual adjustment, integration và unknown.
- Pareto theo số lần, số lượng tuyệt đối và giá trị.
- Drill-down đến count/adjustment/ledger/audit.
- Không gán reason bằng suy đoán AI nếu người có trách nhiệm chưa xác nhận.

### 10.11. STAT-10 - Phát hiện bất thường thống kê

- IQR/MAD theo cohort tương thích, không trộn warehouse/task khác bản chất.
- Đánh dấu giao dịch, duration, adjustment, actor hoặc vị trí bất thường để điều tra.
- Outlier là cảnh báo, không phải kết luận gian lận và không bị tự động xóa khỏi dữ liệu.
- Ngưỡng, sample size và cửa sổ được hiển thị trong metric dictionary.

### 10.12. STAT-11 - AI Smart Cycle Counting

- Danh sách rủi ro theo scope, model version, điểm/xác suất, confidence và freshness.
- Top reason codes cùng giá trị feature.
- So sánh Random/ABC/Rule/ML.
- Nút duyệt/sửa/từ chối và tạo phiếu idempotent.
- Outcome loop và model monitoring.

### 10.13. STAT-12 - Công suất và sử dụng vị trí

- Slot occupancy: vị trí có tồn / vị trí khả dụng.
- Capacity chỉ tính khi đơn vị capacity tương thích: slot, pallet, volume hoặc weight.
- Không cộng “cái”, “kg”, “thùng” rồi chia một capacity chung.
- Nếu thiếu capacity dimension, chỉ hiển thị occupancy và gắn `CAPACITY_DATA_MISSING`.
- Heatmap theo zone/aisle và drill-down vị trí.

### 10.14. STAT-13 - Năng suất và cân bằng workload

- Task/line/base-UOM mỗi giờ, active/wait time, error/rework và backlog.
- Chuẩn hóa theo loại task, quãng đường, zone, trọng lượng/thể tích, lot/serial và thời gian chờ hệ thống.
- Không dùng làm xếp hạng/kỷ luật nhân viên khi dữ liệu không đủ context.
- Role quản lý mới được xem dữ liệu cá nhân chi tiết.

### 10.15. STAT-14 - Hiệu suất nhà cung cấp

- On-time appointment, in-full, QC pass, damage, document/lot/HSD error.
- Dock-to-stock và adjustment theo supplier.
- Cần supplier/partner mapping và expected-vs-actual timestamp có chất lượng.

### 10.16. STAT-15 - Nguy cơ thiếu hàng và bổ sung

- Velocity 7/30/90 ngày từ outbound base quantity.
- Days of supply theo từng item/base UOM, không dùng số phiếu.
- Lead-time demand, reorder point, safety stock và projected stockout chỉ khi dữ liệu demand/lead time đủ sạch.
- Đây là decision support; không tự tạo đơn mua trong WMS nội bộ phiên bản đầu.

---

## 11. Thiết kế UI/UX

### 11.1. Kiến trúc thông tin

- Không thêm 15 mục menu mới.
- Không mặc định tạo route `Phân tích tồn kho` mới. Tái sử dụng `Tổng quan kho` làm cockpit và `Thống kê nhập/xuất` làm drill-down ledger.
- Các nhóm Chính xác, Tuổi tồn/HSD, Vận hành, Nguyên nhân và Công suất được đặt vào report hub/tab hiện có phù hợp sau impact map; chỉ tách trang khi dữ liệu, quyền và workflow thực sự khác nhau.
- Trong `Tồn kho > Kiểm kê`, thêm `Kiểm kê thông minh` cho người có quyền.
- `Quản trị mô hình AI` nằm trong `Hệ thống` và chỉ hiện với role quản trị phù hợp.
- `Cảnh báo dự báo` hiện tại phải được gắn nhãn rõ là điểm theo quy tắc cho đến khi model thật pass calibration; không tạo thêm một menu cảnh báo AI trùng nó.
- `Trợ lý dữ liệu nội bộ` là lối vào phụ có kiểm soát, không phải nhóm menu nghiệp vụ chính và không thay thế màn hình báo cáo/drill-down.
- Dashboard chính chỉ hiển thị 1-2 work item cần hành động, không lặp toàn bộ report hub.

### 11.2. Màn hình Kiểm kê thông minh

Phải có:

- Bộ lọc thời gian, kho, owner, zone, ABC/XYZ, severity, model/fallback và trạng thái.
- KPI: số đề xuất chờ duyệt, coverage, model freshness, expected risk/value và DQ blocked.
- Bảng xếp hạng ổn định, hỗ trợ sort/filter/paging phía server.
- Cột: SKU, vị trí, lot/HSD, tồn, điểm rủi ro, mức, lý do, lần kiểm kê cuối, effort, trạng thái.
- Panel chi tiết giải thích và link ledger/count history.
- Chọn nhiều dòng, duyệt/sửa/từ chối có confirmation và reason.
- Hiển thị rõ `Điểm theo AI`, `Điểm theo quy tắc` hoặc `ABC fallback`.
- Không hiển thị confidence giả khi model không calibration.

### 11.3. Màn hình thống kê

Mỗi báo cáo có bốn lớp:

1. KPI hiện tại, kỳ trước, chênh lệch và sample count.
2. Trend theo ngày/tuần/tháng, rolling window khi phù hợp.
3. Breakdown theo kho/zone/item/lot/reason/workflow.
4. Drill-down tới dữ liệu nguồn.

### 11.4. Cross-device contract

- Desktop 1920x1080 và laptop 1366x768: bảng đầy đủ, header/filter không che nội dung.
- Tablet 1024x768 và 768x1024: filter reflow, bảng dùng column priority hoặc horizontal scroll có chủ đích.
- Mobile 390x844 và 360x800: thao tác thực hiện kiểm kê; màn hình quản trị có thể rút gọn nhưng không rò dữ liệu/quyền.
- Không suy diễn mobile pass từ ảnh desktop.
- Không overlap, clipping, text tràn, sticky header che modal, menu hover quá viewport hoặc nút loading xoay vô hạn.
- Keyboard focus, label, contrast, reduced motion và touch target phải được kiểm thử.

---

## 12. Permission và data scope

Tên permission cuối cùng phải theo convention hiện có; các tên sau là ứng viên:

| Permission ứng viên | Quyền |
|---|---|
| `inventory.risk.view` | Xem risk/recommendation trong warehouse-owner scope. |
| `inventory.risk.review` | Duyệt, sửa hoặc từ chối recommendation. |
| `inventory.risk.model.manage` | Train/register/promote/retire model; không bao gồm duyệt adjustment. |
| `report.inventory.analytics.view` | Xem thống kê phi tài chính. |
| `report.inventory.analytics.financial` | Xem cost/value theo scope. |

Role matrix mục tiêu:

| Role | Xem risk | Duyệt đề xuất | Thực hiện đếm | Duyệt chênh lệch | Quản trị model | Xem tài chính |
|---|---:|---:|---:|---:|---:|---:|
| Admin | Có | Có | Có | Có | Có | Theo quyền full-admin hiện có |
| Manager/Inventory Manager | Có | Có | Có | Có | Không mặc định | Theo permission |
| Inventory Staff | Theo scope | Không | Có | Không | Không | Không |
| Report Viewer | Chỉ xem tổng hợp | Không | Không | Không | Không | Theo permission |
| Inbound/Outbound/Transport Staff | Không mặc định | Không | Không | Không | Không | Không |

Yêu cầu:

- Kiểm cả menu, direct URL, API, export, background job và cache key.
- Scope kho/chủ hàng phải áp dụng ở query trước pagination/aggregation.
- Không để model artifact hoặc feature snapshot bypass scope qua download endpoint.
- Financial metrics dùng permission riêng.
- Admin có full quyền theo contract hiện tại nhưng vẫn ghi audit cho thao tác quản trị model.

---

## 13. API, job và observability dự kiến

### 13.1. API ứng viên

- `GET /api/inventory-risk/recommendations`
- `GET /api/inventory-risk/recommendations/{id}`
- `POST /api/inventory-risk/recommendations/{id}/decision`
- `POST /api/inventory-risk/recommendations/{id}/create-count-sheet`
- `GET /api/inventory-risk/models/current`
- `GET /api/inventory-risk/evaluation`

API write phải có anti-forgery/auth phù hợp, idempotency key, row version và structured business errors.

### 13.2. Background jobs

- Feature snapshot job.
- Batch scoring job.
- Recommendation expiry/invalidation job.
- Outcome linking job.
- Evaluation/drift job.
- Retraining candidate job chỉ tạo challenger; không tự promote.

### 13.3. Telemetry

- Model version, batch ID, correlation ID và scope count.
- Duration, rows scored, coverage, DQ blocked, fallback reason và error count.
- Không log raw feature chứa định danh nhạy cảm.
- Alert khi model quá freshness, coverage giảm, drift vượt ngưỡng hoặc inference failure.

---

## 14. Test strategy

### 14.1. Unit tests

- Mọi công thức KPI, zero denominator, rounding, UOM và timezone.
- Feature window/cutoff, leakage guard và missing-data behavior.
- Rule baseline và severity mapping deterministic.
- State transition, stale/invalidation và fallback.
- Permission decision và reason code validation.

### 14.2. SQL Server integration tests

- Feature extraction từ ledger với dữ liệu nhập/xuất/chuyển/adjust/reversal.
- As-of reconciliation và owner/warehouse scope.
- Count outcome link với lot/HSD.
- Idempotent batch/recommendation/count-sheet creation.
- Concurrent movement trong lúc recommendation được duyệt.
- Migration clean DB, upgrade DB và rollback/forward-fix trên clone.
- Không dùng EF InMemory làm bằng chứng duy nhất cho query/transaction.

### 14.3. Model tests

- Temporal split và không overlap entity/time.
- Baseline Random/ABC/Rule/ML trên cùng tập test.
- Precision@K, lift, PR-AUC, calibration, coverage và slice metrics.
- Reproducibility theo seed/version/hash.
- Empty/small/imbalanced dataset behavior.
- Model artifact tamper/hash mismatch.
- Drift simulation và rollback champion.

### 14.4. API/security tests

- Role matrix, direct URL, IDOR, warehouse-owner scope và export scope.
- CSRF, overposting, invalid transition, replay và stale row version.
- Formula injection khi export CSV/Excel.
- Rate limit cho train/score endpoints quản trị.

### 14.5. E2E/Playwright

- Quản lý xem -> lọc -> mở giải thích -> duyệt -> tạo phiếu.
- Nhân viên kiểm kê chỉ thấy nhiệm vụ trong scope và blind count đúng policy.
- Chênh lệch -> pending review -> người có quyền duyệt.
- Reject/modify/fallback/stale/DQ-blocked states.
- Desktop, laptop, tablet và mobile riêng biệt.
- Console/page/network error, asset, overflow, overlap, focus và accessibility.
- Snapshot chỉ cập nhật sau functional pass và manual visual review.

### 14.6. Performance

- Batch extraction/scoring trên clone có volume đại diện.
- Server pagination và query plan/index review.
- Không chạy load/stress mạnh trên hosting.
- Hosting chỉ read smoke giới hạn sau khi được phép.

---

## 15. Roadmap triển khai theo Gate

Mọi checkbox mặc định để trống. Chỉ tick khi task hoàn tất, test mới pass và evidence tồn tại.

### AI-0 - Chốt contract và sửa nền dữ liệu

- [x] Tạo `docs/audit/AI_SMART_CYCLE_COUNT_RUNTIME_MAP.md`.
- [x] Tạo `docs/audit/AI_FEATURE_DICTIONARY.md`.
- [x] Tạo `docs/audit/AI_LABEL_AND_LEAKAGE_CONTRACT.md`.
- [x] Tạo/cập nhật metric dictionary cho STAT-01..15.
- [x] Xác định milestone thật của `LastCountedAt`; sửa việc cập nhật lúc tạo phiếu bằng regression test.
- [x] Chạy DQ profile về số phiếu kiểm kê, tỷ lệ có kết quả, variance, reason, warehouse-owner-lot completeness.
- [x] Chốt grain dự đoán và tolerance theo UOM.

Evidence AI-0: `artifacts/ai-smart-cycle-count/AI0/AI0_EVIDENCE.md`. Build Release `0 warning/0 error`, targeted regression `5/5` pass, SQL guard và hosting DQ SELECT-only pass. Kết luận dữ liệu: `PASS_FOR_WORKFLOW_SMOKE`, nhưng `BLOCKED_FOR_ML_TRAINING_AND_TEMPORAL_BENCHMARK` do chỉ có 1 dòng outcome trong 1 ngày và không có positive variance.

PASS khi contract được review, query DQ có evidence và lỗi ngữ nghĩa ngày kiểm kê có test pass.  
FAIL khi label/cutoff không tái dựng được.  
BLOCKED khi dữ liệu lịch sử không có kết quả kiểm kê đủ tin cậy.

### AI-1 - Chuẩn hóa thống kê nguồn sự thật

- [x] Sửa `DaysOfSupply` dùng outbound base quantity theo item/UOM.
- [x] Tách last receipt/last outbound trong báo cáo hàng chậm.
- [x] Đổi nhãn ABC hiện tại hoặc bổ sung configurable usage-value ABC.
- [x] Loại capacity mặc định giả và định nghĩa capacity dimension.
- [x] Sửa dock-to-stock để không fallback timestamp âm thầm.
- [x] Bổ sung median/P90/P95/sample count và drill-down.
- [x] Reconcile STAT-01..07 với ledger/snapshot độc lập. Evidence: `docs/audit/FINAL_SMART_COUNT_WAREHOUSE_OVERVIEW_AUDIT_2026_07_18.md` và hai artifact SQL chỉ đọc `ai-cycle-count-readiness-final-detailed-20260718.txt`, `ai-statistics-reconciliation-final-detailed-20260718.txt`.

Evidence AI-1: `artifacts/ai-smart-cycle-count/AI1/AI1_EVIDENCE.md` và `docs/audit/FINAL_SMART_COUNT_WAREHOUSE_OVERVIEW_AUDIT_2026_07_18.md`. Build Release `0 warning/0 error`; full regression trên runtime cuối `1128/1128`; Playwright AI-1 `4/4`; đối soát STAT-01..07 bằng fixture và truy vấn hosting chỉ đọc đã hoàn thành. Trạng thái dữ liệu production vẫn là `BLOCKED_DATA` đối với STAT-05/06/07 vì hosting thiếu mẫu milestone hợp lệ và mẫu số; trạng thái này không phủ nhận việc triển khai, kiểm thử và đối soát công thức đã hoàn thành.

PASS khi metric contract, query và UI khớp cùng snapshot trên test DB.  
Rollback: giữ route cũ, feature flag projection mới nếu cần; không mất dữ liệu.

### AI-2 - Rule baseline và màn hình rủi ro

- [x] Xây rule baseline có version và cấu hình, không hard-code trong controller.
- [x] Tạo immutable feature snapshot và prediction record trên clone/migration rehearsal.
- [x] Hiển thị điểm, reason code, source drill-down, freshness và DQ status.
- [x] Áp dụng warehouse-owner scope và permission.
- [x] Không tạo phiếu/adjustment trong shadow mode.

Evidence AI-2: `artifacts/ai-smart-cycle-count/AI2/AI2_EVIDENCE.md`. Migration additive đã rehearsal trên SQL Server clone dùng một lần, unit/scope/permission `6/6`, affected regression `16/16`, SQL integration `1/1`, Playwright `6/6` viewport và kiểm tra ảnh thủ công pass. Migration chưa áp dụng hosting; màn hình chạy read-only và tự khóa lưu lịch sử thử nghiệm khi schema chưa sẵn sàng.

PASS khi cùng input/version cho cùng output, scope test pass và UI cross-device không lỗi nghiêm trọng.

### AI-3 - Workflow recommendation và human approval

- [x] Triển khai recommendation state machine.
- [x] Duyệt/sửa/từ chối với reason và audit.
- [x] Tạo phiếu kiểm kê idempotent qua service hiện có.
- [x] Chặn active duplicate theo policy.
- [x] Invalidate/re-score khi snapshot stale.
- [x] Giữ blind count và pending variance review.
- [x] AI không tự điều chỉnh tồn.

Evidence AI-3: `artifacts/ai-smart-cycle-count/AI3/AI3_EVIDENCE.md`. Recommendation E2E service, state/concurrency/relational `14/14`, stock-count hardening `4/4`, blind-view `1/1`, full regression `1044/1044`, Playwright AI-2 và AI-3 mỗi bộ `6/6` viewport, ảnh thủ công và secret scan đều pass. SQL Server migration rehearsal là `BLOCKED_ENV` và migration chưa áp dụng hosting; không suy diễn SQLite thành pass cho migration SQL Server.

PASS khi E2E từ recommendation đến count outcome pass với SoD và concurrency.

### AI-4 - Dataset và benchmark khoa học

- [x] Tạo dataset builder có cutoff và feature schema version.
- [x] Loại leakage và dữ liệu hủy/draft không hợp lệ.
- [x] Chia temporal train/validation/test.
- [ ] Benchmark Random, ABC, Rule và model ứng viên.
- [ ] Báo Precision@10/50/100, lift, PR-AUC, value detected, effort và coverage.
- [x] Công bố sample size và limitation; dữ liệu demo phải ghi rõ là demo.

Evidence AI-4: `artifacts/ai-smart-cycle-count/AI4/AI4_EVIDENCE.md`. Pipeline dataset, leakage guard, temporal split và artifact tái lập đã pass targeted `26/26`, full regression `1064/1064`, build `0` warning/error và secret scan. Runtime hosting vẫn `BLOCKED_CONFIGURATION`; chưa có test set/model ứng viên nên benchmark thực và các metric chưa được tick, AI-4 chưa PASS toàn Gate.

PASS khi experiment tái lập được bằng command + seed + artifact hash.

### AI-5 - ML candidate và explainability

- [ ] Audit/pin dependency ML sau khi được chọn.
- [ ] Huấn luyện Logistic Regression candidate.
- [ ] Benchmark tree/forest challenger nếu sample phù hợp.
- [ ] Calibration trước khi gọi score là xác suất.
- [ ] PFI toàn cục và reason code cục bộ dựa trên feature thật.
- [ ] Không dùng LLM sinh lý do không kiểm chứng.

PASS khi model thắng/không thua baseline theo acceptance threshold và explanation truy vết được.  
Nếu không thắng, giữ rule/ABC và ghi kết quả trung thực.

### AI-6 - Feedback và model lifecycle

- [ ] Link prediction với outcome kiểm kê cuối cùng.
- [ ] Lưu override và outcome độc lập.
- [ ] Champion/challenger, promote/retire và artifact hash.
- [ ] Model registry UI dành cho admin phù hợp.
- [ ] Rollback về champion cũ hoặc rule fallback.
- [x] Không tự promote model.

Evidence AI-5/AI-6: `artifacts/ai-smart-cycle-count/AI5-AI9/AI5_AI6_LIFECYCLE_EVIDENCE.md`. AI-5 giữ `BLOCKED_DATA` vì chưa có temporal test cohort/model artifact hợp lệ và repository chưa có dependency ML đã được duyệt. AI-6 đã pass cơ chế fail-closed cho nhiều champion, chặn model retired, tạo version mới ở trạng thái challenger và chỉ cho champion sinh recommendation; targeted `47/47`, full regression `1075/1075`, build `0` warning/error. Outcome cuối, promotion/retire UI và rollback rehearsal vẫn chưa hoàn tất nên AI-6 chưa PASS toàn Gate.

PASS khi rollback rehearsal và audit trail tái dựng được một prediction cũ.

### AI-7 - Monitoring, drift và adaptive planning

- [ ] Theo dõi precision/lift/calibration/coverage theo thời gian và slice.
- [ ] Data drift, concept drift proxy và freshness alert.
- [ ] Budget planner theo số dòng/thời gian/work pool.
- [x] Không thay đổi task đang thực hiện nếu chưa có policy.
- [ ] Retraining trigger chỉ tạo candidate và cần duyệt.

Evidence AI-7 (một invariant đã hoàn tất): `artifacts/ai-smart-cycle-count/AI5-AI9/AI7_GOVERNANCE_EVIDENCE.md`. Regression `InProgressCountTask_ShouldRejectRecommendationModification` pass `1/1`; task đang `InProgress` giữ nguyên trạng thái, effort, assignee, work pool và decision history khi có yêu cầu sửa không hợp lệ. Full regression cuối pass `1085/1085`. Monitoring/drift/budget/retraining vẫn chưa đủ điều kiện nên AI-7 chưa PASS toàn Gate.

PASS khi drift/failure simulation kích hoạt cảnh báo và fallback đúng.

### AI-8 - Hoàn thiện STAT-08..15

- [ ] ABC-XYZ cấu hình và test.
- [ ] Pareto reason taxonomy và drill-down.
- [ ] IQR/MAD anomaly labeling.
- [ ] Space utilization theo dimension hợp lệ.
- [ ] Workload productivity có normalization.
- [ ] Supplier inbound scorecard.
- [x] Stockout/replenishment risk chỉ khi demand/lead time đủ sạch.

Evidence AI-8 (một contract hoàn tất, các STAT còn lại `PARTIAL/BLOCKED`): `artifacts/ai-smart-cycle-count/AI5-AI9/AI8_STAT_READINESS_EVIDENCE.md`. Targeted analytics pass `9/9`; Release build `0` warning/error; full regression `1085/1085`; Playwright pass `4/4` project với 24 lượt route trên desktop/laptop/tablet/mobile. Risk chỉ bật khi demand-active sample và lead time hợp lệ; scorecard nhà cung cấp chưa được tick vì chưa có damage reason taxonomy, không điền damage rate giả.

PASS theo metric contract riêng của từng STAT; mục thiếu dữ liệu giữ `BLOCKED`, không điền số giả.

### AI-9 - Release rehearsal và pilot

- [ ] Full build/unit/SQL integration/API/E2E/Playwright pass.
- [ ] Artifact scan không chứa secret/PII ngoài policy.
- [ ] Shadow mode tối thiểu một chu kỳ đánh giá đã chốt.
- [ ] UAT Manager, Inventory Staff, Report Viewer và Admin.
- [ ] Pilot một warehouse/owner test, có rollback và sign-off.
- [ ] So sánh Random/ABC/AI trên cùng budget.

PASS khi owner nghiệp vụ ký nhận và không có Critical/High mở.  
BLOCKED nếu chưa có đủ count outcomes/pilot thực tế.

Rà soát cuối ngày 18/07/2026: Release build sạch `0 warning/0 error`, full backend `1114/1114`, Playwright AI-1 `4/4`, AI-2 `6/6`, AI-3 `6/6`, cross-device `16/16`, mobile-deep `424/424` và protected-secret scan `0 match`. AI-9 vẫn để trống đúng quy tắc vì chưa có temporal cohort đủ sạch, shadow cycle/pilot ký nhận và browser UAT role cô lập; chi tiết tại `artifacts/full-audit/FINAL_SYSTEM_REAUDIT_2026_07_18.md`.

---

## 16. Thứ tự triển khai khuyến nghị

1. Hoàn tất các invariant liên quan của Gate 3, đặc biệt ledger, kiểm kê, adjustment, concurrency và reconciliation.
2. Thực hiện `AI-0` và `AI-1` để sửa source-of-truth và KPI trước khi huấn luyện.
3. Làm `AI-2` rule baseline + UI shadow mode để có demo trung thực, ít rủi ro.
4. Làm `AI-3` workflow duyệt và tạo phiếu kiểm kê.
5. Thu thập outcome sạch, sau đó mới làm `AI-4` và `AI-5`.
6. Hoàn thiện feedback/model lifecycle/monitoring ở `AI-6` và `AI-7` để đạt AI-native.
7. Mở rộng STAT-08..15 theo độ sạch dữ liệu.
8. Pilot/release rehearsal cuối cùng.

Gate 4 về import/OCR có thể tiếp tục song song sau khi Gate 3 ổn định; không cần chờ toàn bộ AI. Tuy nhiên không nên quảng bá AI-native hoặc train model trước khi `AI-0/AI-1` hoàn tất.

---

## 17. File impact map dự kiến

Chỉ là danh sách ứng viên; phải runtime trace lại trước khi sửa.

### Có khả năng mở rộng

- `Services/CoreWmsServices.cs`
- `Services/Enterprise1113Services.cs`
- `Controllers/ReportsController.StockCount.cs`
- `Controllers/ReportsController.Analytics.cs`
- `Controllers/ReportsController.Enterprise1113.cs`
- `Models/AdvancedWmsModels.cs`
- `Models/StockCountSheet.cs`
- `Models/StockCountLine.cs`
- `Models/InventoryTransaction.cs`
- `Models/AnalyticsUxProductionEnterpriseModels.cs`
- `Data/ApplicationDbContext.cs` hoặc DbContext tương ứng sau runtime trace
- `Services/RbacSeedService.cs`
- `Views/Reports/*`
- `Views/Shared/_SidebarNav.cshtml`
- `Views/Shared/_Layout.cshtml`
- `WMS.Tests/*`
- Playwright specs/config hiện có

### File mới có thể cần

- Service feature extraction/scoring/planning nhỏ, tách theo trách nhiệm.
- ViewModel typed cho statistics và recommendations; tránh tiếp tục mở rộng `ViewBag` cho metric phức tạp.
- Model/entity additive sau schema design.
- Unit/integration/E2E/Playwright specs.
- `docs/audit/AI_*` và evidence artifact.

Không tạo một “God AI Service” chứa feature extraction, training, scoring, workflow, permission và persistence trong cùng lớp.

---

## 18. Rủi ro và biện pháp kiểm soát

| Rủi ro | Mức | Kiểm soát |
|---|---|---|
| Dữ liệu kiểm kê quá ít hoặc thiên lệch | Cao | Rule/ABC baseline, shadow mode, công bố coverage; không giả model. |
| Leakage từ kết quả kiểm kê tương lai | Cao | Prediction cutoff, immutable snapshot, temporal/group split test. |
| `LastCountedAt` sai ngữ nghĩa | Cao | Sửa trước feature extraction, regression + migration data repair có kiểm soát nếu cần. |
| AI tạo quá nhiều việc | Cao | Budget planner, max tasks, approval và duplicate policy. |
| Model drift | Trung bình/Cao | Monitoring, freshness, champion/fallback và retrain có duyệt. |
| Điểm bị hiểu là xác suất | Trung bình | Calibration gate; nhãn UI “điểm” khi chưa calibration. |
| Explanation gây hiểu nhầm | Trung bình | Reason từ snapshot, PFI chỉ ghi là global, không dùng văn bản bịa. |
| KPI sai UOM/currency | Cao | Base UOM conversion, cost snapshot, metric contract và reconciliation. |
| Permission leak qua aggregate/export | Cao | Scope trước aggregation, direct API/IDOR/export test. |
| Migration ảnh hưởng hosting | Cao | Clone rehearsal, precheck, backup, rollback/forward-fix, checkpoint riêng. |
| UI quá tải | Trung bình | Report hub theo tab, progressive disclosure, server pagination. |
| Phụ thuộc API AI ngoài | Thấp nếu theo thiết kế | Inference local; không dùng external API cho scoring cốt lõi. |

---

## 19. Evidence và quy tắc tick checklist

Mỗi mục chỉ được tick khi evidence ghi đủ:

- Build/version/hash hiện tại.
- File và symbol đã thay đổi.
- Command/test/query.
- Exit code.
- Kết quả và số test.
- DQ/reconciliation nếu ảnh hưởng dữ liệu.
- Playwright artifact nếu ảnh hưởng UI/route/permission.
- Migration rehearsal artifact nếu ảnh hưởng schema.
- Rollback đã mô tả hoặc diễn tập theo mức rủi ro.

Thư mục evidence đề xuất:

`artifacts/ai-smart-cycle-count/<build-id>/<gate-id>/`

Không tick mục đang `FAIL`, `BLOCKED`, `NOT TESTED`, dùng dữ liệu demo giả như production hoặc chỉ mới được code review.

---

## 20. Definition of Done

### 20.1. AI-assisted MVP

- Rule baseline có version, explanation và scope.
- Shadow-mode, human approval và tạo phiếu idempotent.
- Không tự adjustment.
- STAT-01, STAT-02, STAT-09 và STAT-11 reconcile với ledger.
- Permission, SQL integration và Playwright pass.

### 20.2. AI-native

- Có model được benchmark với Random/ABC/Rule trên temporal holdout.
- Prediction, feature snapshot, model version, decision và outcome truy vết đầy đủ.
- Human approval, feedback loop, model monitoring, drift và fallback vận hành.
- Champion/challenger và rollback rehearsal.
- KPI thống kê không dùng công thức sai thứ nguyên hoặc timestamp suy diễn.
- UAT và pilot có sign-off.

### 20.3. Kết luận được phép

- `PLANNED`: mới có roadmap.
- `AI-ASSISTED VERIFIED`: rule/model hỗ trợ nhưng chưa đủ vòng đời AI-native.
- `AI-NATIVE SHADOW VERIFIED`: đầy đủ pipeline nhưng chưa pilot production.
- `AI-NATIVE PILOT VERIFIED`: pilot đạt acceptance criteria trong scope được nêu rõ.
- `BLOCKED`: thiếu dữ liệu, pilot, thiết bị, quyền hoặc hạ tầng bên ngoài.

Không dùng kết luận tuyệt đối “100%” hoặc “0 bug” nếu evidence không chứng minh toàn bộ phạm vi.

---

## 21. Giá trị trình bày cho đồ án

Tên đề tài gợi ý:

> **Xây dựng hệ thống WMS AI-native hỗ trợ phát hiện rủi ro sai lệch tồn kho và tối ưu kiểm kê chu kỳ bằng học máy có khả năng giải thích.**

Demo thuyết phục nên đi theo kịch bản:

1. Cho xem ledger và kết quả kiểm kê lịch sử làm nguồn dữ liệu.
2. So sánh Random, ABC, Rule và AI trên cùng ngân sách Top K.
3. Mở một đề xuất rủi ro cao, xem lý do và giao dịch nguồn.
4. Quản lý duyệt, hệ thống tạo phiếu kiểm kê.
5. Nhân viên thực hiện blind count.
6. Chênh lệch vào hàng chờ duyệt, không tự sửa tồn.
7. Kết quả quay lại evaluation dashboard và model lifecycle.
8. Trình bày limitation, fallback và rollback.

Điểm nghiên cứu cốt lõi không phải “có AI”, mà là chứng minh được:

- AI/rule có phát hiện nhiều sai lệch hơn Random/ABC với cùng công sức hay không.
- Lợi ích có ổn định theo thời gian, kho và nhóm hàng hay không.
- Hệ thống có giải thích, kiểm soát, fallback và audit được hay không.
