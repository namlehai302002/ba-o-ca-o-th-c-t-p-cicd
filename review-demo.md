# Review Demo WMS Pro - Kịch Bản 30 Phút

Mục tiêu của buổi demo: cho hội đồng thấy WMS Pro không chỉ là phần mềm nhập - xuất - tồn cơ bản, mà là hệ thống quản lý kho nội bộ có quy trình, kiểm soát dữ liệu, giao diện vận hành, báo cáo, RF/mobile, tối ưu vị trí, cảnh báo và tích hợp.

Nguyên tắc khi nói: nói chắc những gì hệ thống đã có và đã demo được; không nói quá thành "AI tối ưu toàn bộ kho" hoặc "production 100% tuyệt đối". Cách nói an toàn và hay hơn là: "Hệ thống đi theo hướng data-driven và rule-based analytics, có thể mở rộng sang machine learning khi có đủ dữ liệu vận hành thực tế."

## 1. Chuẩn Bị Trước Khi Demo

Mở sẵn theo đúng thứ tự tab:

| Thứ tự | Mở sẵn | Mục đích |
|---:|---|---|
| 1 | Word báo cáo đồ án | Chỉ show mục tiêu, phạm vi, tính mới, kiến trúc và kết quả kiểm thử. |
| 2 | Canva thuyết trình | Dẫn câu chuyện trong 7-8 slide, không đọc quá nhiều chữ. |
| 3 | WMS Pro đã đăng nhập | Demo nghiệp vụ thật. |
| 4 | `FINAL_WMS_ENTERPRISE_QA_REPORT.md` hoặc bản PDF/Word tương ứng | Show evidence nhanh: build, test, visual, audit. |
| 5 | `DEMO_SCENARIOS.md` hoặc dữ liệu demo đã seed | Phòng khi cần nhắc 3 domain demo: IT, y tế, thương mại điện tử. |

Checklist 10 phút trước demo:

- Đăng nhập sẵn bằng tài khoản có quyền Admin/Manager.
- Kiểm tra menu chính, dashboard, phiếu nhập, phiếu xuất, RF Receiving, RF Picking, Slotting, Predictive Alerts, Integration Dashboard mở được.
- Chuẩn bị một phiếu nhập, một phiếu xuất, một wave/pick task hoặc dữ liệu demo có sẵn.
- Không mở `appsettings.json`, không show secret, connection string, API key, mật khẩu.
- Nếu dữ liệu demo ít, vào `/System/DemoData` để chọn domain phù hợp trước buổi demo, không seed/reset ngay trong lúc đang thuyết trình nếu không cần.

## 2. Timeline 30 Phút

| Thời gian | Phần | Nội dung chính | Kết quả cần đạt |
|---:|---|---|---|
| 00:00-01:30 | Mở đầu | Giới thiệu WMS Pro, bài toán kho nội bộ, lý do chọn đề tài. | Hội đồng hiểu vấn đề thực tế. |
| 01:30-04:00 | Show Word báo cáo | Mục tiêu, phạm vi, tính mới, kiến trúc, kiểm thử. | Chứng minh có nền tảng học thuật/kỹ thuật. |
| 04:00-08:00 | Canva | Dẫn 7-8 slide theo câu chuyện: pain point -> giải pháp -> demo flow -> kết quả. | Người nghe nắm roadmap demo. |
| 08:00-10:00 | Tổng quan app | Dashboard, phân quyền, menu module. | Cho thấy hệ thống hoàn chỉnh. |
| 10:00-14:00 | Inbound | Phiếu nhập, QC, lot/serial/expiry, putaway/RF Receiving. | Chứng minh kiểm soát đầu vào kho. |
| 14:00-17:00 | Inventory | Tồn kho theo vị trí, bản đồ kho, kiểm kê, risk/alert. | Chứng minh tồn kho có truy vết. |
| 17:00-21:00 | Outbound | Phiếu xuất, reservation, wave picking, RF Picking, packing/shipping. | Chứng minh luồng xuất kho end-to-end. |
| 21:00-24:00 | Smart modules | Slotting, Predictive Alerts, Semantic BI/AI Assistant. | Nêu tính mới, data-driven, không chỉ CRUD. |
| 24:00-26:30 | Integration & security | API, webhook/outbox, idempotency, role/scope, audit. | Chứng minh tư duy enterprise. |
| 26:30-28:00 | Evidence | Build/test/visual gates, 673/673 tests, visual chain pass. | Tăng độ tin cậy. |
| 28:00-30:00 | Kết luận/Q&A | Nhắc lại giá trị, giới hạn thật, hướng phát triển. | Chốt gọn và chuyên nghiệp. |

## 3. Lời Mở Đầu 90 Giây

Nói ngắn, tự tin:

> Đề tài của em là WMS Pro, hệ thống quản lý kho nội bộ. Bài toán em tập trung không chỉ là ghi nhận nhập - xuất - tồn, mà là làm sao để kho vận hành có kiểm soát: hàng phải biết đang ở kho nào, vị trí nào, lô nào, hạn dùng nào, ai thao tác, khi nào thao tác, và nếu có sai lệch thì hệ thống phải hỗ trợ phát hiện sớm.
>
> Điểm em muốn nhấn mạnh là hệ thống được thiết kế theo hướng WMS thông minh: có RF/mobile, QC, putaway, wave picking, cảnh báo vận hành, gợi ý vị trí lưu trữ, báo cáo và lớp tích hợp API/webhook. Phạm vi demo hôm nay em sẽ đi theo một luồng kho thực tế trong 30 phút: từ báo cáo, slide tổng quan, sau đó chạy trực tiếp trên hệ thống.

## 4. Phần Show Word Báo Cáo - 2.5 Phút

Chỉ mở đúng các mục này, không cuộn lan man:

| Mục trong Word | Thời lượng | Nói gì |
|---|---:|---|
| Lý do chọn đề tài | 30s | Kho thực tế hay gặp sai vị trí, sai tồn, mất thời gian tìm hàng, nhập tay dễ lỗi. |
| Mục tiêu hệ thống | 30s | Quản lý kho nội bộ end-to-end: master data, nhập, xuất, tồn, kiểm kê, báo cáo, phân quyền. |
| Tính mới | 60s | Inventory Risk Map, Smart Slotting/Putaway, Dynamic Wave Picking Lite, Explainable Predictive Alerts, Integration Health. |
| Kiến trúc | 30s | ASP.NET Core MVC, EF Core, SQL Server, service layer, RBAC, audit, API. |
| Kiểm thử/evidence | 30s | Build sạch, test tự động, visual regression, không show secret. |

Câu nói nên dùng:

> Trong báo cáo em không chỉ mô tả chức năng, mà còn gắn từng nhóm chức năng với vấn đề vận hành kho: sai lệch tồn kho, mất thời gian picking, khó truy vết lot/serial, thiếu kiểm soát ngoại lệ và thiếu giám sát tích hợp.

Không nói:

- "Hệ thống đã thay thế hoàn toàn WMS doanh nghiệp lớn."
- "AI dự báo chính xác 100%."
- "Không bao giờ còn bug."

Nói thay thế:

> Trong phạm vi đồ án và repo/local, hệ thống đã có kiểm thử tự động và evidence rõ ràng. Khi triển khai production thật vẫn cần UAT với thiết bị thật, load test và quy trình vận hành thật.

## 5. Canva Thuyết Trình - 4 Phút

Canva nên có 8 slide, mỗi slide nói 25-35 giây.

### Slide 1 - Tên Đề Tài

Tiêu đề gợi ý:

> WMS Pro - Hệ thống quản lý kho nội bộ thông minh

Subtitle:

> Kiểm soát nhập - xuất - tồn, RF/mobile, tối ưu vị trí, cảnh báo vận hành và tích hợp dữ liệu.

Lời nói:

> Em xây dựng WMS Pro theo hướng một hệ thống kho nội bộ có thể kiểm soát dữ liệu từ đầu vào đến đầu ra, thay vì chỉ lưu phiếu kho đơn giản.

### Slide 2 - Vấn Đề Thực Tế

Ghi 4 pain point:

- Có tồn trên hệ thống nhưng ngoài kho không tìm thấy hàng.
- Nhân viên mất nhiều thời gian đi tìm và lấy hàng.
- Nhập kho thủ công dễ sai số lượng, sai mã, thiếu QC.
- Quản lý chỉ thấy vấn đề sau khi sự cố đã xảy ra.

Lời nói:

> Đây là các vấn đề rất thực tế trong kho: dữ liệu tồn không khớp hiện trường, picking tốn thời gian, và ngoại lệ bị xử lý rời rạc qua chat hoặc Excel.

### Slide 3 - Giải Pháp Tổng Thể

Vẽ flow:

```text
Master Data -> Inbound/QC -> Putaway -> Inventory -> Wave Picking -> Packing/Shipping -> Reports/API
```

Lời nói:

> Hệ thống đi theo luồng vận hành kho thật: tạo dữ liệu nền, nhập hàng có kiểm soát, đưa hàng vào vị trí, quản lý tồn, xuất hàng theo wave, đóng gói, giao hàng và báo cáo.

### Slide 4 - Tính Mới Cốt Lõi

Ghi 5 điểm:

1. Inventory Risk Map.
2. Smart Slotting/Putaway.
3. Dynamic Wave Picking Lite.
4. Explainable Predictive Alerts.
5. Integration Health Dashboard.

Lời nói:

> Điểm mới của đề tài là hệ thống không dừng ở nhập - xuất - tồn, mà bổ sung lớp hỗ trợ quyết định: đánh giá rủi ro tồn kho, gợi ý vị trí, điều phối picking, cảnh báo có giải thích và giám sát tích hợp.

### Slide 5 - Demo Flow

Ghi timeline demo app:

```text
Dashboard -> Receiving/QC -> Inventory/Map -> Slotting -> Wave/RF Picking -> Alerts/Integration
```

Lời nói:

> Phần demo em sẽ không đi từng menu rời rạc, mà đi theo một câu chuyện vận hành: hàng vào kho, được kiểm tra, được cất đúng vị trí, sau đó được lấy theo wave và theo dõi bằng báo cáo/cảnh báo.

### Slide 6 - Kiến Trúc Và Bảo Mật

Ghi:

- ASP.NET Core MVC, EF Core, SQL Server.
- Service layer cho nghiệp vụ.
- RBAC, warehouse scope, owner scope.
- Audit trail, CSRF, API key.
- Test và visual regression.

Lời nói:

> Em tách phần nghiệp vụ quan trọng vào service và có kiểm soát quyền theo vai trò, kho và chủ hàng để hạn chế thao tác sai phạm vi.

### Slide 7 - Evidence

Ghi đúng số liệu mới:

- Build: `0 warning / 0 error`.
- .NET tests: `673/673`.
- Visual: `6/6`, `1/1`, `185 passed / 63 skipped`, `10/10`, `416/416`.
- NuGet/npm audit: không có vulnerability được phát hiện.

Lời nói:

> Đây là bằng chứng kiểm thử local/repo. Em không dùng test để che bug; các lỗi phát hiện được đều được khóa bằng regression test.

### Slide 8 - Kết Luận Và Hướng Phát Triển

Ghi:

- Hoàn thiện luồng WMS nội bộ.
- Có tính mới theo hướng smart warehouse.
- Có thể mở rộng RFID, thiết bị thật, load test, ERP/TMS/carrier thật.

Lời nói:

> Hướng phát triển tiếp theo là đưa hệ thống lên môi trường staging/production thật, kiểm thử với RF scanner, máy in tem, cân điện tử, tải thật và các tích hợp doanh nghiệp thật.

## 6. Demo App - Luồng Click 18 Phút

### 6.1 Dashboard Và Phân Quyền - 2 Phút

Mở:

- `/`
- Dashboard chính.
- Menu người dùng/phân quyền nếu cần.

Nói:

> Dashboard cho người quản lý thấy tình hình vận hành nhanh: phiếu hôm nay, tồn thấp, cảnh báo, trạng thái kho. Hệ thống có phân quyền Admin, Manager, Staff, Viewer và còn kiểm soát theo warehouse scope/owner scope.

Điểm ăn tiền:

> Với kho nội bộ, sai quyền rất nguy hiểm vì có thể sửa tồn hoặc xem dữ liệu ngoài phạm vi. Vì vậy hệ thống không chỉ có role, mà còn có scope theo kho và chủ hàng.

### 6.2 Inbound - Guided Receiving & QC - 4 Phút

Mở:

- `/Vouchers/Create` hoặc danh sách phiếu nhập.
- `/Operations/RfReceiving`
- Chi tiết phiếu nhập.
- Nếu có serial: `/Operations/SerialReceiving?voucherId=...`

Demo:

1. Mở một phiếu nhập.
2. Chỉ ra trạng thái phiếu: draft/approved/receiving/completed.
3. Chỉ ra dòng hàng, số lượng, lot/expiry/serial nếu có.
4. Mở RF Receiving để thấy màn hình vận hành mobile.
5. Nhắc QC/Hold/Defect nếu hàng lỗi.

Nói:

> Luồng nhập kho được thiết kế có kiểm soát. Hàng không chỉ được cộng tồn ngay, mà đi qua phiếu nhập, duyệt/tiếp nhận, kiểm tra chất lượng, ghi nhận lot/serial/hạn dùng và sau đó mới putaway vào vị trí.

Điểm nói thêm từ ý tưởng:

> Module này giải quyết lỗi đầu vào: nhận thiếu phải có lý do, hàng lỗi được đưa vào trạng thái hold/quarantine, hàng có hạn dùng phải nhập expiry date, hàng serial phải quét đủ số serial. Nhờ vậy dữ liệu tồn ngay từ đầu đã đáng tin hơn.

### 6.3 Inventory - Tồn Kho, Vị Trí, Kiểm Kê, Bản Đồ Kho - 3 Phút

Mở:

- `/Reports/Inventory`
- `/Warehouses/InventoryMap`
- `/Reports/StockCount`
- Có thể mở `/Reports/Alerts`

Demo:

1. Show tồn kho theo item/vị trí/kho.
2. Show bản đồ kho hoặc vị trí có mức đầy/cảnh báo.
3. Show kiểm kê hoặc cảnh báo tồn thấp/hết hạn nếu có dữ liệu.

Nói:

> Vấn đề lớn của kho là hệ thống báo còn hàng nhưng ngoài kho không tìm thấy. Vì vậy dữ liệu tồn không chỉ lưu theo item, mà gắn với kho, khu vực, vị trí, lot, hạn dùng, serial và trạng thái hold.

Điểm nói thêm từ ý tưởng:

> Hướng Inventory Risk Map giúp quản lý không kiểm kê dàn trải, mà ưu tiên vị trí rủi ro cao: lâu chưa kiểm kê, nhiều lần điều chỉnh tồn, từng thiếu hàng khi picking hoặc có lỗi QC. Đây là cách kiểm kê có trọng tâm.

### 6.4 Smart Slotting / Putaway - 3 Phút

Mở:

- `/Operations/Slotting`
- `/Operations/SlottingSimulation`
- Nếu có: `/Operations/OptimizationDashboard`

Demo:

1. Show danh sách gợi ý slotting.
2. Chỉ ra ABC/XYZ, điểm slotting, vị trí hiện tại, vị trí đề xuất.
3. Mở mô phỏng slotting.
4. Nếu phù hợp, nói "hệ thống có thể tạo movement task", không nhất thiết bấm nếu sợ thay đổi dữ liệu demo.

Nói:

> Smart Slotting giải quyết việc kho phụ thuộc vào kinh nghiệm cá nhân. Hệ thống gợi ý vị trí dựa trên tốc độ luân chuyển, sức chứa, khu picking, hạn dùng và xung đột vị trí.

Điểm nói thêm từ ý tưởng:

> Hàng nhóm A, xuất nhiều, nên ở gần khu picking. Hàng nhóm C, luân chuyển chậm, có thể ở xa hơn. Hàng gần hết hạn cần ưu tiên vị trí dễ lấy để hỗ trợ FEFO.

Câu tránh bị hỏi khó:

> Trong phạm vi đồ án, thuật toán đang ở mức rule-based/data-driven, chưa tuyên bố là machine learning. Khi có dữ liệu vận hành đủ lớn, phần này có thể mở rộng sang ML cho bài toán Storage Location Assignment.

### 6.5 Outbound - Dynamic Wave Picking Lite & RF Picking - 4 Phút

Mở:

- `/Vouchers/WavePlanning`
- `/Operations/Waves`
- `/Operations/PickTasks`
- `/Operations/RfPicking`
- Chi tiết phiếu xuất/packing/shipping nếu có.

Demo:

1. Show nhiều phiếu xuất hoặc wave đã tạo.
2. Mở Wave Planning/Waves.
3. Show pick task.
4. Mở RF Picking để thấy giao diện nhân viên kho.
5. Nhắc packing/shipping/handover.

Nói:

> Picking là khâu tốn thời gian nhất trong kho. Thay vì xử lý từng đơn rời rạc, hệ thống gom đơn thành wave, sinh pick task và hỗ trợ nhân viên lấy hàng bằng RF/mobile.

Điểm nói thêm từ ý tưởng:

> Dynamic Wave Picking Lite giúp gom đơn theo độ gấp, khu vực, nhóm hàng và deadline. Khi wave chưa bắt đầu, hệ thống có thể re-plan để giảm quãng đường và giảm nhầm lẫn khi xử lý nhiều đơn cùng lúc.

Câu ăn điểm:

> Em không chỉ demo phiếu xuất, mà demo cách điều phối công việc lấy hàng. Đây mới là phần làm WMS khác với phần mềm quản lý tồn kho thông thường.

### 6.6 Explainable Predictive Alerts, Semantic BI, AI Assistant - 3 Phút

Mở:

- `/Reports/PredictiveAlerts`
- `/Reports/SemanticBi`
- `/Reports/AiAssistant`
- `/Reports/OpsKpi`

Demo:

1. Show cảnh báo tồn thấp/hết hạn/quá tải/trễ SLA nếu có.
2. Chỉ ra phần giải thích nguyên nhân/gợi ý hành động.
3. Mở Semantic BI hoặc AI Assistant để nói về truy vấn nghiệp vụ.

Nói:

> Điểm quan trọng là cảnh báo không chỉ báo "nguy hiểm", mà cần giải thích vì sao. Ví dụ hàng sắp hết hạn thì hệ thống nêu số ngày còn lại, tồn hiện tại, tốc độ xuất và gợi ý ưu tiên FEFO.

Điểm nói thêm từ ý tưởng:

> AI Assistant trong đề tài không thay người quản lý ra quyết định. Nó hỗ trợ truy vấn dữ liệu và giải thích cảnh báo bằng ngôn ngữ tự nhiên, giúp người không chuyên vẫn khai thác được dữ liệu WMS.

### 6.7 Exception Center & Integration Health - 2.5 Phút

Mở:

- `/Operations/ExceptionCenter`
- `/Operations/IntegrationDashboard` hoặc dashboard tích hợp tương ứng nếu menu đang hiển thị.
- Nếu cần API evidence: nhắc `/api/v1`, `X-API-Key`, webhook/outbox.

Demo:

1. Show ngoại lệ vận hành: receiving, QC, picking, shipping, API.
2. Show trạng thái/ưu tiên/SLA nếu có.
3. Show integration/outbox/retry/dead-letter nếu có dữ liệu.

Nói:

> Trong kho thật, ngoại lệ không nên nằm rải rác trong chat hoặc Excel. Exception Center gom lỗi về một nơi, có phân loại nguyên nhân, người phụ trách, trạng thái và SLA xử lý.

Điểm nói thêm từ ý tưởng:

> Với integration, điểm mới không chỉ là có API. Hệ thống còn có giám sát, retry, dead-letter, replay và idempotency để giảm rủi ro mất dữ liệu khi ERP/TMS/carrier gọi lại nhiều lần hoặc timeout.

## 7. Evidence Chốt Cuối - 90 Giây

Mở `FINAL_WMS_ENTERPRISE_QA_REPORT.md` hoặc bản Word/PDF evidence và chỉ đúng phần Current Audit Checkpoint.

Nói:

> Phần kiểm thử local/repo hiện có: build pass 0 warning/0 error, .NET tests pass 673/673, targeted API và scorecard pass 11/11, visual regression pass trên public/auth/main/no-device/mobile-deep, NuGet và npm audit không phát hiện vulnerable package. Đây là bằng chứng kỹ thuật để đảm bảo demo không chỉ chạy được bằng tay mà còn có regression test khóa lại các lỗi nghiệp vụ quan trọng.

Nhắc ranh giới thật:

> Em vẫn phân biệt rõ local/repo readiness và production thật. Production thật cần thêm RF scanner, máy in tem, cân điện tử, load test, DR/HA, pentest và tích hợp thật. Việc nói rõ giới hạn này giúp báo cáo thực tế hơn.

## 8. 10 Câu Nói Thêm Để Nghe Hay Hơn

1. "WMS Pro không chỉ lưu số lượng tồn, mà lưu tồn theo ngữ cảnh vận hành: kho, vị trí, lot, hạn dùng, serial, trạng thái hold và chủ sở hữu."
2. "Điểm khác biệt của WMS so với quản lý kho bằng Excel là mọi thay đổi tồn đều có quy trình, trạng thái, audit và người chịu trách nhiệm."
3. "Inventory Risk Map giúp chuyển kiểm kê từ bị động sang chủ động: kiểm kê nơi có rủi ro cao trước."
4. "Smart Slotting giảm phụ thuộc vào kinh nghiệm cá nhân, vì vị trí lưu trữ được đề xuất dựa trên dữ liệu luân chuyển và sức chứa."
5. "Wave Picking giúp điều phối công việc lấy hàng, không chỉ tạo phiếu xuất."
6. "RF/mobile làm cho nhân viên kho thao tác theo task, giảm nhập tay và giảm sai sót."
7. "Cảnh báo có giải thích giúp quản lý biết vì sao hệ thống cảnh báo và nên làm gì tiếp theo."
8. "Exception Center biến các sự cố rời rạc thành dữ liệu quản trị có thể phân tích nguyên nhân gốc."
9. "Integration Health giúp hệ thống không bị mù khi kết nối với ERP/TMS/carrier: có retry, dead-letter và replay."
10. "Thiết kế hiện tại là rule-based/data-driven, an toàn cho đồ án và có thể mở rộng sang machine learning khi có dữ liệu thật."

## 9. Câu Hỏi Hội Đồng Có Thể Hỏi Và Cách Trả Lời

| Câu hỏi | Trả lời gợi ý |
|---|---|
| Hệ thống có gì mới so với CRUD nhập xuất tồn? | Điểm mới là lớp vận hành thông minh: risk map, slotting, wave picking, predictive alerts, integration health và exception center. |
| Có dùng AI thật không? | Có hướng AI/analytics ở mức hỗ trợ truy vấn và giải thích. Phần tối ưu chính đang rule-based để kiểm soát được logic và demo ổn định. |
| Vì sao không dùng ML luôn? | ML cần dữ liệu vận hành thật đủ lớn để huấn luyện và đánh giá. Trong đồ án, rule-based/data-driven là phù hợp, minh bạch và kiểm chứng được. |
| Làm sao tránh sai tồn? | Dùng phiếu có trạng thái, stock ledger, reservation, kiểm kê, audit, lot/serial/location và kiểm soát quyền. |
| Khi hệ thống khác gọi API trùng request thì sao? | API tạo voucher có idempotency key, retry cùng key không tạo trùng voucher; concurrent retry cũng được khóa bằng regression test. |
| Nếu nhân viên nhập sai thì sao? | Có validation, trạng thái duyệt, QC/hold, audit trail và quyền theo vai trò để giảm thao tác sai. |
| Production đã dùng được chưa? | Repo/local đã có build/test/visual evidence. Production thật cần thêm UAT thiết bị, load test, DR/HA, pentest và hosting evidence. |

## 10. Kịch Bản Dự Phòng Nếu Demo Bị Thiếu Dữ Liệu

Nếu không có phiếu nhập:

- Mở `/System/DemoData` trước buổi demo để seed domain IT/y tế/TMĐT.
- Trong lúc demo, chuyển sang show màn `RfReceiving` và giải thích flow bằng dữ liệu hiện có.

Nếu không có wave:

- Mở `/Operations/Waves` để show board.
- Mở `/Vouchers/WavePlanning` để giải thích cách gom phiếu.
- Không cần bấm tạo mới nếu dữ liệu chưa đủ.

Nếu không có cảnh báo:

- Mở `/Reports/PredictiveAlerts` và nói về rule cảnh báo.
- Chuyển sang `/Reports/Alerts` hoặc `/Reports/ExpiryReport` nếu có dữ liệu hết hạn/tồn thấp.

Nếu app chậm hoặc lỗi mạng:

- Quay lại Canva slide Demo Flow.
- Show ảnh/chứng cứ trong report/evidence.
- Nói: "Em sẽ mô tả nhanh luồng trên slide và quay lại app khi kết nối ổn định."

Nếu bị hỏi về 100% production:

> Em không khẳng định production tuyệt đối trong môi trường chưa có thiết bị thật. Em khẳng định trong phạm vi repo/local hệ thống đã có kiểm thử tự động, visual regression và evidence rõ ràng; còn production cần UAT, load test và tích hợp thật.

## 11. Chốt Kết Luận 60 Giây

> Tóm lại, WMS Pro giải quyết bài toán kho nội bộ theo luồng end-to-end: từ nhập kho, QC, putaway, quản lý tồn, kiểm kê, xuất kho, wave picking, packing, shipping đến báo cáo và tích hợp. Điểm mới của hệ thống là không chỉ ghi nhận dữ liệu, mà còn hỗ trợ ra quyết định bằng risk map, slotting, predictive alerts, exception center và integration health.
>
> Trong phạm vi đồ án, hệ thống đã có giao diện vận hành, phân quyền, kiểm thử và evidence kỹ thuật. Hướng phát triển tiếp theo là kiểm thử với thiết bị kho thật, tải thật và tích hợp với ERP/TMS/carrier thật để tiến gần hơn mô hình smart warehouse trong doanh nghiệp.

## 12. Bản Siêu Ngắn Nếu Chỉ Còn 15 Phút

| Thời gian | Làm gì |
|---:|---|
| 00:00-02:00 | Canva slide 1-4: vấn đề, giải pháp, tính mới. |
| 02:00-04:00 | Show Word: mục tiêu, tính mới, kiến trúc. |
| 04:00-07:00 | Demo inbound/RF Receiving/QC. |
| 07:00-10:00 | Demo inventory/slotting. |
| 10:00-13:00 | Demo wave/RF Picking/predictive alerts. |
| 13:00-14:00 | Show evidence test/visual. |
| 14:00-15:00 | Kết luận và hướng phát triển. |
