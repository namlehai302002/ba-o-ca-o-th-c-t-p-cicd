# Roadmap nâng cấp báo cáo tổng hợp, HSD theo lô/vị trí và kiểm soát log WMS Pro

Ngày lập: 02/07/2026  
Phạm vi: WMS Pro - hệ thống quản lý kho nội bộ  
Trạng thái cập nhật: đã rà soát lại repo, chạy migration trên đúng DB hosting, đối chiếu enterprise parity và verification ngày 05/07/2026. Roadmap đã hoàn tất trong phạm vi code/backend/API/database/local visual evidence; không sửa/xóa `appsettings.json`, không seed/reset/xóa dữ liệu DB thật.

## 0. Trạng thái triển khai đã đối chiếu - 05/07/2026

| Nhóm yêu cầu | Trạng thái | Bằng chứng trong repo/local | Ghi chú còn lại |
|---|---|---|---|
| Báo cáo nhập/xuất theo kỳ | Đã triển khai chính và đã siết server-side date range | `InventoryInOutSummaryPageViewModel`, route `/Reports/InventoryInOutSummary`, export Excel, view `Views/Reports/InventoryInOutSummary.cshtml`, tests `InventoryInOutSummary_ShouldShowLotExpiryLocationsAndFilterDirection`, `InventoryInOutSummary_ShouldRequireExplicitDateRangeOnServer` | UI và backend đều bắt buộc Từ ngày/Đến ngày; export cũng bị chặn nếu thiếu hoặc chọn khoảng ngày không hợp lệ. |
| Báo cáo tổng quan kho/cockpit | Đã triển khai mới và đưa vào menu Báo cáo | `WarehouseOverviewPageViewModel`, route `/Reports/WarehouseOverview`, view `Views/Reports/WarehouseOverview.cshtml`, menu `Tổng quan kho`, tests `WarehouseOverview_ShouldCombineInventoryFlowBacklogAndDataExceptions`, `WarehouseOverview_ShouldBeEnterpriseCockpitAndMenuEntry`, visual route `warehouse-overview` | Tổng quan lấy tồn hiện tại từ `ItemLocations`, dòng nhập/xuất từ `InventoryTransactions`, backlog từ `Vouchers`, ngoại lệ dữ liệu từ ledger/reservation invariant; không trộn số phiếu với số lượng tồn. |
| Lịch sử nhập xuất có lô/NSX/HSD/vị trí | Đã triển khai chính | `Views/Reports/StockMovement.cshtml`, `ReportsController.Inventory.StockMovement`, export stock movement | "Ngày nhập nguồn" trong báo cáo tổng hợp lấy theo transaction/source data hiện có; dữ liệu cũ thiếu nguồn thì không suy đoán. |
| Lịch sử chốt tồn | Đã triển khai theo ngày/kho và nhiều phiên trong cùng ngày; DB hosting đã cập nhật schema | `StockSnapshotRuns`, `StockSnapshot.StockSnapshotRunId`, `BuildStockSnapshotHistoryAsync`, `Views/Reports/StockSnapshot.cshtml`, migration `20260704090000_AddStockSnapshotRuns`, test `GenerateStockSnapshot_ShouldCreateNewRunAndKeepExistingClosingEvidence`; DB hosting latest migration `20260705070000_RepairReportFefoDatabaseGuards` | Mỗi lần chốt tạo một phiên riêng, không ghi đè mốc cũ; audit DB thật: `StockSnapshots` thiếu `StockSnapshotRunId` = 0, FK/index run đều OK. |
| HSD theo loại vật tư | Đã triển khai UI, backend chính và API write path | Item có `TrackLot`, `TrackExpiry`, `TrackSerial`; form nhập kho disable HSD nếu không quản lý hạn; tests HSD/lot trong `BusinessLogicHardeningTests`, `VoucherCreateRegressionTests`, `ApiIntegrationScopeHardeningTests`; API `POST /api/v1/vouchers` đã nhận `ManufacturingDate`/`ExpiryDate` và enforce HSD/lot policy | Luồng tạo phiếu chính và API đã chặn HSD trước NSX, thiếu HSD với item track expiry, thiếu lô với item track lot, cùng lô nhiều HSD, và bỏ qua HSD client gửi cho vật tư không quản lý hạn. |
| Xuất kho theo FEFO/vị trí | Đã triển khai source selection, guard override và visual route evidence | FEFO sort trong voucher helpers/outbound services; API `GetItemLocations` trả `availableQty`, `lotNumber`, `expiryDate`, `isFefoRecommended`, `isEnough`; form xuất có nút chọn lô/vị trí, hidden source lot/HSD/reason; tests FEFO; Playwright smoke `outbound voucher exposes FEFO source lot and location selection surface` pass | DB hosting hiện không có item `TrackExpiry=true`/2 lô HSD sẵn nên không seed dữ liệu thật chỉ để diễn; FEFO được khóa bằng unit/integration/API và visual route. |
| RequestTelemetry/AuditLogs | Đã triển khai kiểm soát chính và bổ sung index DB | `ProductionSreService.RecordRequestAsync` bỏ static assets/health và prune retention; `ShouldRecordForTelemetry` luôn giữ lỗi/chậm trước sampling; test `CorrelationTelemetry_ShouldAlwaysKeepErrorsAndSlowRequestsBeforeSampling`; DB có index `IX_RequestTelemetryLogs_*`, `IX_AuditLogs_Table_Date` | Không xóa audit nghiệp vụ; DB guard giúp query log không phình chậm bất thường. |
| Database integrity | Đã rà DB hosting thật và thêm guard còn thiếu | Migration `20260705070000_RepairReportFefoDatabaseGuards`; index/constraint cho `InventoryTransactions`, `RequestTelemetryLogs`, `AuditLogs`, `ItemLocations`, `VoucherDetails`, `Items`, `Vouchers`, `StockSnapshots`, `StockSnapshotRuns` đều OK | Data quality check: duplicate idempotency = 0, tồn âm/reserved overflow = 0, HSD trước NSX = 0, defect âm = 0, snapshot thiếu run id = 0. |
| Menu/sidebar/icon rail | Đã sửa lệch icon thật và khóa regression alignment smoke | CSS collapsed rail absolute-center icon link đơn; Playwright `collapsed sidebar keeps enterprise rail groups and flyouts` pass trên desktop 100/110/125 và đo `iconCenterX`; `visual:test` pass route collapsed | Logo, link đơn và nhóm menu đều cùng trục icon; flyout vẫn giữ layout đọc được khi focus/hover. |
| Verification local/visual | Đã có evidence mới nhất | Build `0 warning / 0 error`; .NET tests `691/691`; SQL data-quality audit DB hosting artifact `artifacts/data-quality/wms-data-quality-audit-20260705-130335.txt` = `0/17` nhóm có lỗi; `visual:test` `194 passed / 66 skipped`; targeted `warehouse-overview` + collapsed sidebar `7 passed / 1 skipped` | Load/k6, thiết bị thật, DR/HA và hosting artifact vẫn là checklist ngoài môi trường local/browser, không phải lỗi code roadmap. |
| Đánh giá so với WMS enterprise lớn | Đã lập báo cáo parity riêng | `WMS_ENTERPRISE_PARITY_ASSESSMENT_2026_07_05.md`: roadmap này `100/100`, repo/local readiness `96/100`, Tier-1 production equivalence `89-91/100` | Phần còn thiếu để gọi là 100% enterprise production là evidence ngoài code: thiết bị thật, load/soak, DR/HA, pentest, monitoring, UAT ký nhận và certified integrations. |

## 1. Mục tiêu

Roadmap này mô tả hướng nâng cấp WMS Pro để xử lý tốt hơn các nhu cầu nghiệp vụ kho nội bộ:

- Có màn thống kê tổng quát tất cả phiếu nhập/xuất , vận chuyển theo khoảng ngày.
- Lịch sử nhập xuất phải thể hiện rõ lô, NSX, HSD, ngày nhập nguồn và vị trí thao tác.
- Chốt tồn kho phải xem lại được các lần chốt trước.
- HSD phải áp dụng theo từng loại vật tư, không bắt mọi mặt hàng đều có hạn sử dụng.
- Khi xuất kho phải biết lấy hàng từ lô nào, HSD nào, vị trí nào.
- Với vật tư có HSD, hệ thống phải ưu tiên FEFO: lô hết hạn trước thì xuất trước.
- `RequestTelemetry` và `AuditLogs` phải được kiểm soát để tránh phình database.

Mục tiêu không phải biến WMS Pro thành hệ thống thuê kho hoặc marketplace. Trọng tâm vẫn là hệ thống quản lý kho nội bộ cho doanh nghiệp, trường học, phòng khám, kho thương mại điện tử hoặc các tổ chức có tài sản/vật tư/hàng hóa cần kiểm soát nghiêm túc.

## 2. Benchmark tham chiếu

Các hệ thống WMS/ERP lớn thường tách rõ ba lớp:

- Chứng từ nguồn: phiếu nhập, phiếu xuất, điều chuyển, kiểm kê.
- Tồn kho thực tế: theo kho, vị trí, lô, serial, HSD, trạng thái chất lượng.
- Điều phối vận hành: gợi ý vị trí cất hàng, vị trí lấy hàng, chiến lược FIFO/FEFO, audit log và báo cáo.

Tham chiếu chính:

- Microsoft Dynamics 365 Supply Chain dùng location directives để xác định vị trí putaway/picking cho từng loại công việc kho. Tài liệu nhấn mạnh location directive là rule giúp tìm vị trí lấy/cất hàng cho inventory movement: <https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/create-location-directive>
- Odoo Inventory có FEFO removal strategy cho hàng có hạn sử dụng, tức ưu tiên xuất lô hết hạn trước: <https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/inventory/shipping_receiving/removal_strategies/fefo.html>
- Oracle WMS/Inventory hiển thị tồn theo SKU, vị trí, trạng thái, số lượng và thông tin expiry/lot trong quản lý tồn: <https://docs.oracle.com/cloud/owm20b/owmcs_gs-cloud/OWMSU/inventory-management.html>
- Oracle Inventory Lot Control dùng expiration date để kiểm soát vòng đời và khả dụng của lot: <https://docs.oracle.com/cd/E26401_01/doc.122/e48820/T291651T427993.htm>

Áp dụng vào WMS Pro: hệ thống nên đi theo hướng "lot/location/expiry aware", tức mọi báo cáo, phiếu xuất và lịch sử giao dịch đều phải nhìn được hàng thuộc lô nào, hạn nào, đang ở vị trí nào.

## 3. Vấn đề hiện tại cần giải quyết

### 3.1. Thiếu form thống kê tổng quát nhập/xuất

Hiện hệ thống có lịch sử nhập xuất, nhưng chưa có một màn tổng hợp mạnh để xem tất cả sản phẩm đã nhập/xuất trong một khoảng thời gian theo yêu cầu:

- Chưa bắt buộc chọn từ ngày - đến ngày.
- Chưa tổng hợp rõ SL nhập, SL xuất, chênh lệch.
- Chưa nhóm được theo vật tư, lô, vị trí, kho, đối tác.
- Chưa thể hiện đầy đủ lô nào, HSD nào đã được xuất.

Ảnh hưởng:

- Khó đối chiếu nhập xuất tồn theo kỳ.
- Khó kiểm tra lô có hạn cũ nhất đã được xuất hay chưa.
- Khó truy trách nhiệm khi tồn kho lệch.

### 3.2. Lịch sử nhập xuất thiếu HSD/ngày nhập/vị trí nguồn

Lịch sử nhập xuất hiện cần bổ sung:

- Số lô.
- Ngày sản xuất.
- Hạn sử dụng.
- Ngày nhập nguồn.
- Vị trí nguồn.
- Vị trí đích.

Ảnh hưởng:

- Khi xuất hàng, quản lý không biết chính xác lô nào đã xuất.
- Nhân viên không thấy vị trí lấy hàng gắn với lô/HSD.
- Không kiểm chứng được FEFO.

### 3.3. Chốt tồn chưa có lịch sử các lần chốt trước

Màn chốt tồn hiện cần có khu vực lịch sử:

- Chốt ngày nào.
- Kho nào.
- Ai chốt.
- Chốt lúc nào.
- Bao nhiêu mã vật tư.
- Tổng giá trị tồn.
- Có bao nhiêu dòng chênh lệch.
- Xem lại chi tiết mốc chốt.

Rủi ro cần lưu ý:

- Nếu database hiện chỉ lưu snapshot theo ngày/kho và khi chốt lại cùng ngày thì ghi đè, hệ thống chưa thể xem nhiều phiên chốt trong cùng một ngày.
- Muốn chuẩn enterprise hơn cần thêm khái niệm `SnapshotBatch` hoặc `StockSnapshotRun`.

### 3.4. HSD chưa theo chính sách từng vật tư

Không phải vật tư nào cũng có hạn sử dụng:

- Vật tư y tế: thường bắt buộc lô + HSD, khuyến nghị NSX.
- Laptop, máy chiếu, router, switch, màn hình: thường quản lý serial/tài sản, không bắt buộc HSD.
- Thương mại điện tử: tùy loại sản phẩm. Tai nghe, sạc, chuột thường không cần HSD; mỹ phẩm, thực phẩm, pin hoặc hóa chất thì cần HSD.

Ảnh hưởng nếu làm sai:

- Bắt laptop nhập HSD là sai nghiệp vụ.
- Không bắt vật tư y tế nhập HSD là rủi ro an toàn và truy xuất.
- Cùng một mã hàng có nhiều lô khác HSD mà không quản lý rõ sẽ dễ xuất nhầm lô.

### 3.5. Xuất kho chưa hiển thị đủ lô/HSD/vị trí lấy hàng cho nhân viên

Khi xuất kho, nhân viên cần biết:

- Lấy mã hàng nào.
- Lấy ở kho nào.
- Lấy ở vị trí nào.
- Lô nào.
- HSD nào.
- Tồn khả dụng tại vị trí là bao nhiêu.

Với hàng có hạn dùng, hệ thống phải gợi ý lô HSD cũ nhất trước. Nếu user chọn lô khác, cần có lý do override và phân quyền rõ.

### 3.6. Telemetry và audit log có nguy cơ phình database

Cần tách rõ:

- Audit nghiệp vụ: phải giữ lâu, vì liên quan trách nhiệm.
- Telemetry kỹ thuật: có thể sampling, retention, chỉ lưu request lỗi/chậm/quan trọng.

Nếu ghi mọi request xem trang, refresh, CSS/JS/image, health check vào DB thì database sẽ phình nhanh mà giá trị nghiệp vụ thấp.

## 4. Roadmap triển khai đề xuất

### Giai đoạn 1 - Bổ sung báo cáo tổng hợp nhập/xuất theo kỳ

Mục tiêu: tạo màn báo cáo tổng quan để quản lý xem tất cả nhập/xuất trong một khoảng thời gian.

Route đề xuất:

```text
/Reports/InventoryInOutSummary
```

Menu đề xuất:

```text
Báo cáo > Thống kê nhập/xuất theo kỳ
```

Bộ lọc bắt buộc:

| Field | Bắt buộc | Ghi chú |
|---|---:|---|
| Từ ngày | Có | Không cho xem nếu bỏ trống |
| Đến ngày | Có | Không cho nhỏ hơn từ ngày |

Bộ lọc tùy chọn:

| Field | Mục đích |
|---|---|
| Kho | Lọc theo kho |
| Vật tư | Lọc theo mã hàng |
| Danh mục | Lọc theo nhóm hàng |
| Loại giao dịch | Tất cả / Nhập / Xuất / Điều chuyển / Điều chỉnh |
| Lô | Tra theo số lô |
| Vị trí | Tra hàng đã nhập/xuất tại vị trí nào |
| Đối tác | Nhà cung cấp, khách hàng, bộ phận nhận |

Cột dữ liệu bắt buộc:

| Cột | Ý nghĩa |
|---|---|
| Ngày chứng từ | Ngày trên phiếu nguồn |
| Ngày ghi sổ | Thời điểm hệ thống ghi nhận tồn |
| Mã phiếu | Link sang chi tiết phiếu |
| Loại phiếu | Nhập, xuất, điều chuyển, điều chỉnh |
| Đối tác | Nhà cung cấp, khách hàng hoặc bộ phận nhận |
| Mã vật tư | SKU/mã vật tư |
| Tên vật tư | Tên hàng |
| Danh mục | Nhóm hàng |
| Lô | Số lô nếu có |
| NSX | Ngày sản xuất |
| HSD | Hạn sử dụng |
| Ngày nhập nguồn | Ngày lô đó nhập vào kho |
| Kho | Kho phát sinh giao dịch |
| Vị trí nguồn | Nơi lấy hàng khi xuất/chuyển/giảm tồn |
| Vị trí đích | Nơi cất hàng khi nhập/chuyển/tăng tồn |
| SL nhập | Số lượng tăng tồn |
| SL xuất | Số lượng giảm tồn |
| ĐVT | Đơn vị tồn kho |
| Người lập | User tạo phiếu |
| Người duyệt/xác nhận | User duyệt hoặc hoàn tất |
| Ghi chú | Ghi chú nghiệp vụ |

Tổng hợp cuối bảng:

- Tổng SL nhập.
- Tổng SL xuất.
- Chênh lệch nhập - xuất.
- Tổng theo vật tư.
- Tổng theo lô.
- Tổng theo vị trí.

Export:

- Có nút xuất Excel.
- Excel phải giữ đủ cột lô/NSX/HSD/vị trí.
- Nếu dữ liệu lớn, nên giới hạn preview trên UI và export theo batch/async ở giai đoạn sau.

Nguồn dữ liệu ưu tiên:

1. `InventoryTransactions` hoặc ledger tồn kho nếu đã lưu đủ `VoucherDetailId`, `LotNumber`, `ExpiryDate`, `LocationId`.
2. `VoucherDetails` để bổ sung `ManufacturingDate`, vị trí nguồn/đích, ghi chú dòng.
3. `Vouchers` để lấy header: mã phiếu, ngày chứng từ, đối tác, người lập/duyệt/xác nhận.
4. `ItemLocations` chỉ dùng để đối chiếu tồn hiện tại, không dùng thay thế lịch sử.

Tiêu chí nghiệm thu:

- Không cho chạy báo cáo nếu thiếu từ ngày/đến ngày.
- Dòng nhập hiển thị SL nhập, vị trí đích.
- Dòng xuất hiển thị SL xuất, vị trí nguồn, lô/HSD đã xuất.
- Điều chuyển hiển thị cả vị trí nguồn và vị trí đích.
- Số liệu export Excel khớp số liệu trên màn.

### Giai đoạn 2 - Nâng cấp màn lịch sử nhập xuất

Mục tiêu: lịch sử giao dịch phải đủ thông tin vận hành để nhân viên và quản lý truy xuất lô/vị trí.

Cột cần thêm:

- Số lô.
- NSX.
- HSD.
- Ngày nhập nguồn.
- Vị trí nguồn.
- Vị trí đích.

Quy tắc hiển thị:

| Loại giao dịch | Vị trí nguồn | Vị trí đích | Lô/HSD |
|---|---|---|---|
| Nhập kho | Nhà cung cấp hoặc để trống | Vị trí cất hàng | Lô/HSD nhập |
| Xuất kho | Vị trí lấy hàng | Khách hàng/bộ phận nhận hoặc để trống | Lô/HSD xuất |
| Điều chuyển | Vị trí đi | Vị trí đến | Lô/HSD chuyển |
| Điều chỉnh tăng | Để trống | Vị trí tăng tồn | Lô/HSD điều chỉnh |
| Điều chỉnh giảm | Vị trí giảm tồn | Để trống | Lô/HSD điều chỉnh |

Tiêu chí nghiệm thu:

- Màn không còn chỉ hiển thị mã phiếu, vật tư, kho, số lượng.
- Với hàng có HSD, dòng xuất phải thấy HSD của lô đã xuất.
- Với hàng nhiều lô, lịch sử phải phân biệt từng lô.
- Không hiển thị `---`, `null`, `undefined`, `NaN` ở các cột mới.

### Giai đoạn 3 - Bổ sung lịch sử chốt tồn

Mục tiêu: quản lý xem lại được những lần đã chốt tồn.

Khu vực mới trên màn `Chốt tồn kho`:

```text
Lịch sử chốt tồn
```

Bảng lịch sử:

| Cột | Ý nghĩa |
|---|---|
| Ngày chốt | Ngày số liệu được khóa |
| Kho | Kho được chốt |
| Người chốt | User thực hiện |
| Thời gian chốt | Thời điểm thao tác |
| Số mã vật tư | Số mã được lưu snapshot |
| Tổng giá trị tồn chốt | Tổng giá trị tại mốc chốt |
| Số dòng chênh lệch | Chênh lệch so với tồn hiện tại |
| Trạng thái | Đã chốt / Có chênh lệch / Đã tạo điều chỉnh |
| Hành động | Xem chi tiết |

Khuyến nghị schema nếu muốn chuẩn enterprise:

Tạo bảng mới:

```text
StockSnapshotRuns
```

Trường đề xuất:

| Field | Ghi chú |
|---|---|
| StockSnapshotRunId | PK |
| RunCode | Ví dụ: SNAP-20260702-KHO01-001 |
| WarehouseId | Kho |
| SnapshotDate | Ngày chốt |
| CreatedAt | Thời điểm chốt |
| CreatedBy | Người chốt |
| ItemCount | Số mã |
| TotalValue | Tổng giá trị |
| DiffLineCount | Số dòng lệch tại thời điểm tạo |
| Status | Active / Superseded / Locked |
| Notes | Ghi chú |

Cập nhật bảng `StockSnapshots`:

```text
StockSnapshotRunId nullable hoặc required sau migration
```

Lý do cần `StockSnapshotRuns`:

- Không bị ghi đè khi chốt lại cùng ngày.
- Xem được nhiều phiên chốt trong một ngày.
- Có audit rõ ai chốt, chốt lúc nào, chốt lại vì lý do gì.

Tiêu chí nghiệm thu:

- Xem được danh sách các mốc chốt cũ.
- Bấm một mốc chốt cũ phải xem được tồn tại ngày đó và tồn hiện tại.
- Nếu chưa đổi schema, ít nhất phải xem được lịch sử theo ngày/kho đã có.
- Nếu đổi schema, không được xóa snapshot cũ khi chốt lại.

### Giai đoạn 4 - Chuẩn hóa HSD theo từng loại vật tư

Mục tiêu: mỗi vật tư có chính sách quản lý tồn riêng.

Thuộc tính cần có hoặc cần chuẩn hóa trên vật tư:

| Thuộc tính | Ý nghĩa |
|---|---|
| Quản lý theo lô | Có bắt số lô không |
| Quản lý hạn sử dụng | Có dùng HSD không |
| Quản lý serial | Có theo dõi serial không |
| Bắt buộc NSX | Có bắt nhập ngày sản xuất không |
| Bắt buộc HSD | Có bắt nhập hạn sử dụng không |

Quy tắc theo lĩnh vực:

| Lĩnh vực | Quy tắc đề xuất |
|---|---|
| Kho thiết bị IT | Quản lý serial cho laptop/màn hình/router/switch. Không bắt HSD. Lô chỉ dùng nếu cần theo đợt nhập. |
| Kho vật tư y tế | Bắt buộc lô + HSD. NSX nên có nếu nhà cung cấp cung cấp. Không cho HSD trước NSX. |
| Kho thương mại điện tử | HSD tùy ngành hàng. Phụ kiện điện tử không bắt HSD; mỹ phẩm/thực phẩm/pin/hóa chất cần HSD. |

Form nhập kho:

- Nếu vật tư có `TrackExpiry = true`: ô HSD phải enable và required.
- Nếu vật tư có `TrackExpiry = false`: ô HSD hiển thị "Không áp dụng" hoặc disable.
- Nếu vật tư có `TrackLot = true`: ô số lô required.
- Nếu vật tư có `TrackSerial = true`: yêu cầu nhập/quét serial trước khi hoàn tất.
- Không cho HSD nhỏ hơn NSX.
- Cùng một vật tư + cùng số lô không được có nhiều HSD khác nhau.

Backend validation:

- Không chỉ validate ở frontend.
- Khi submit phiếu nhập, backend phải kiểm tra lại `TrackLot`, `TrackExpiry`, `TrackSerial`.
- Nếu client cố gửi thiếu HSD cho vật tư y tế, backend phải từ chối.
- Nếu client gửi HSD cho vật tư không quản lý HSD thì có thể bỏ qua hoặc lưu nhưng UI phải ghi rõ không áp dụng. Khuyến nghị: không lưu HSD nếu vật tư không quản lý hạn.

Tiêu chí nghiệm thu:

- Nhập khẩu trang/găng tay/bộ test không có HSD thì bị chặn.
- Nhập laptop/chuột/bàn phím không cần HSD vẫn lưu được.
- HSD trước NSX bị chặn.
- Một lô không bị nhập nhiều HSD khác nhau.

### Giai đoạn 5 - Xuất kho theo FEFO và vị trí lấy hàng

Mục tiêu: khi xuất kho, hệ thống hướng dẫn nhân viên lấy đúng hàng, đúng lô, đúng vị trí.

Luồng đề xuất:

1. User chọn kho xuất.
2. User chọn vật tư.
3. Hệ thống gọi tồn khả dụng theo:
   - Kho.
   - Vật tư.
   - Lô.
   - HSD.
   - Vị trí.
   - Tồn khả dụng.
4. Nếu vật tư có HSD:
   - Sắp xếp theo HSD cũ nhất trước.
   - Nếu cùng HSD, ưu tiên FIFO hoặc vị trí picking ưu tiên.
5. Nếu vật tư không có HSD:
   - Sắp xếp theo FIFO/ngày nhập nguồn hoặc vị trí ưu tiên.
6. UI hiển thị gợi ý:
   - Vị trí lấy hàng.
   - Lô.
   - HSD.
   - Tồn khả dụng.
7. Khi lưu/gửi duyệt, phiếu phải lưu lại lô/HSD/vị trí đã chọn.

UI cần có:

| Thành phần | Mô tả |
|---|---|
| Vị trí lấy hàng | Select hoặc gợi ý mặc định |
| Lô | Hiển thị/cho chọn tùy quyền |
| HSD | Hiển thị readonly theo lô |
| Tồn khả dụng | Hiển thị tại vị trí |
| Gợi ý FEFO | Badge "Đề xuất FEFO" |
| Override | Nếu chọn lô khác, bắt lý do |

Quy tắc override:

- Nhân viên kho thường: chỉ được chọn theo gợi ý hoặc theo danh sách khả dụng.
- Quản lý kho/Admin: có thể override FEFO nếu nhập lý do.
- Tất cả override phải ghi audit log.

Tiêu chí nghiệm thu:

- Có 2 lô cùng vật tư, HSD khác nhau.
- Xuất kho mặc định chọn lô HSD cũ nhất.
- Màn xuất hiển thị vị trí lấy hàng của lô đó.
- Nếu chọn lô HSD mới hơn, hệ thống yêu cầu lý do.
- Không cho xuất vượt tồn khả dụng theo vị trí/lô.

### Giai đoạn 6 - Kiểm soát RequestTelemetry và AuditLogs

Mục tiêu: giữ audit nghiệp vụ cần thiết, giảm telemetry kỹ thuật ít giá trị để database không phình.

Phân loại:

| Loại log | Có nên giữ lâu? | Ghi chú |
|---|---:|---|
| Tạo phiếu | Có | Audit nghiệp vụ |
| Duyệt/từ chối phiếu | Có | Audit nghiệp vụ |
| Hủy phiếu | Có | Audit nghiệp vụ |
| Điều chỉnh tồn | Có | Audit nghiệp vụ quan trọng |
| Đổi quyền/user/role | Có | Audit bảo mật |
| Export dữ liệu | Có | Audit bảo mật |
| Request CSS/JS/image | Không | Telemetry kỹ thuật, nên bỏ |
| Health check | Không hoặc sampling thấp | Không cần lưu dày |
| Request thành công nhanh | Sampling thấp | Ví dụ 1-10% |
| Request lỗi 4xx/5xx | Có | Cần debug |
| Request chậm | Có | Cần tối ưu |

Chính sách đề xuất cho `RequestTelemetry`:

- Không lưu static assets:
  - `.css`
  - `.js`
  - `.png`
  - `.jpg`
  - `.jpeg`
  - `.gif`
  - `.svg`
  - `.ico`
  - `.woff`
  - `.woff2`
  - `.map`
- Không lưu `/health` nếu không cần.
- Luôn lưu:
  - HTTP 4xx/5xx.
  - Request chậm hơn ngưỡng cấu hình, ví dụ 1500 ms.
  - Các endpoint nhạy cảm: login, export, import, OCR, seed demo, phân quyền.
- Sampling request thành công:
  - Local/dev: có thể 100% để debug.
  - Production: khuyến nghị 1-10%.
- Retention:
  - 14-30 ngày cho telemetry kỹ thuật.
  - Có job dọn định kỳ.

Chính sách đề xuất cho `AuditLogs`:

- Giữ lâu audit nghiệp vụ quan trọng.
- Không ghi audit cho thao tác xem trang, refresh, search nếu không có giá trị nghiệp vụ.
- Nếu cần ghi read audit cho dữ liệu nhạy cảm, ghi theo nhóm, không ghi từng request nhỏ.

Job dọn log:

```text
TelemetryRetentionJob
```

Luồng:

1. Đọc cấu hình retention.
2. Xóa `RequestTelemetry` quá hạn.
3. Không xóa audit nghiệp vụ quan trọng.
4. Ghi lại lần cleanup gần nhất.
5. Có báo cáo số dòng đã xóa.

Màn quản trị đề xuất:

```text
Hệ thống > Giám sát dung lượng log
```

Thông tin hiển thị:

- Số dòng `RequestTelemetry` theo ngày.
- Số dòng `AuditLogs` theo ngày.
- Top endpoint tạo nhiều log nhất.
- Tỷ lệ lỗi 4xx/5xx.
- Request chậm nhất.
- Lần cleanup gần nhất.
- Dung lượng ước tính.

Tiêu chí nghiệm thu:

- Static asset không còn làm tăng `RequestTelemetry`.
- Refresh trang liên tục không tạo log quá dày.
- Request lỗi/chậm vẫn được lưu.
- Audit tạo/duyệt/hủy phiếu vẫn được giữ.
- Có thể xem số lượng log theo ngày.

## 5. Data flow chuẩn đề xuất

### 5.1. Nhập kho

```text
Phiếu nhập Header
  -> Dòng phiếu nhập
  -> Validate vật tư/lô/HSD/serial/vị trí
  -> Duyệt
  -> Nhận/kiểm hàng
  -> Putaway vào vị trí
  -> Ghi ItemLocation
  -> Ghi InventoryTransaction
  -> Ghi AuditLog nghiệp vụ
```

Điểm bắt buộc:

- Tồn chỉ tăng khi tới bước nghiệp vụ hợp lệ.
- Dòng nhập phải lưu lô, NSX, HSD nếu vật tư yêu cầu.
- Vị trí cất hàng phải thuộc kho nhận.

### 5.2. Xuất kho

```text
Phiếu xuất Header
  -> Dòng phiếu xuất
  -> Kiểm tra tồn khả dụng
  -> Gợi ý FEFO/FIFO theo vật tư
  -> Chọn lô/vị trí lấy hàng
  -> Duyệt/picking/confirm
  -> Trừ ItemLocation
  -> Ghi InventoryTransaction
  -> Ghi AuditLog nghiệp vụ
```

Điểm bắt buộc:

- Không xuất âm.
- Không xuất vượt tồn khả dụng.
- Dòng xuất phải lưu lại lô/HSD/vị trí nguồn.
- Nếu override FEFO, bắt lý do và ghi audit.

### 5.3. Báo cáo

```text
InventoryTransaction
  + Voucher
  + VoucherDetail
  + Item
  + Location
  + Partner
  -> Báo cáo nhập/xuất theo kỳ
  -> Lịch sử nhập xuất
  -> Excel export
```

Nguyên tắc:

- Báo cáo không tự suy đoán nếu dữ liệu nguồn thiếu.
- Nếu ledger chưa có `ManufacturingDate`, lấy từ `VoucherDetail`.
- Nếu lịch sử cũ thiếu lô/HSD, hiển thị "Không ghi nhận" thay vì `null` hoặc `---`.

## 6. Ưu tiên triển khai

### P0 - Bắt buộc trước demo/bảo vệ

- Lịch sử nhập xuất có cột lô/HSD/vị trí nguồn-vị trí đích.
- Form nhập kho không bắt HSD với laptop/thiết bị IT.
- Vật tư y tế bắt buộc HSD.
- Xuất kho hiển thị lô/HSD/vị trí lấy hàng.
- Không cho xuất vượt tồn khả dụng theo lô/vị trí.

### P1 - Nên làm để demo thuyết phục hơn

- Màn thống kê nhập/xuất theo kỳ.
- Export Excel thống kê.
- Lịch sử chốt tồn theo ngày/kho.
- Gợi ý FEFO rõ trên UI.
- Cảnh báo khi user chọn lô khác FEFO.

### P2 - Nâng cấp chuẩn doanh nghiệp hơn

- Đã thêm `StockSnapshotRuns` để lưu nhiều phiên chốt cùng ngày/kho.
- Job retention telemetry.
- Màn giám sát dung lượng log.
- Batch export async nếu báo cáo lớn.
- Role matrix cho quyền override FEFO.

### P3 - Tiệm cận enterprise production

- Mobile/RF picking theo vị trí/lô/HSD.
- In tem nhãn lô/HSD.
- Scanner barcode/QR thật.
- Load test dữ liệu lớn.
- Backup/restore test.
- DR/HA.
- Tích hợp ERP/kế toán/OMS/TMS.

## 7. Test plan chi tiết

### 7.1. Unit/integration tests

| Test | Kỳ vọng |
|---|---|
| Nhập vật tư y tế thiếu HSD | Bị chặn |
| Nhập vật tư IT thiếu HSD | Được phép |
| HSD trước NSX | Bị chặn |
| Cùng vật tư + cùng lô nhưng khác HSD | Bị chặn hoặc yêu cầu xác nhận theo chính sách |
| Xuất vật tư có 2 lô HSD khác nhau | Chọn lô HSD cũ nhất |
| Xuất vật tư không có HSD | Dùng FIFO/vị trí ưu tiên |
| Xuất vượt tồn tại vị trí | Bị chặn |
| Báo cáo thiếu từ ngày/đến ngày | Bị chặn |
| Báo cáo theo kỳ có nhập/xuất/lô/HSD/vị trí | Hiển thị đúng |
| Chốt tồn nhiều ngày | Xem lại được từng ngày |
| Telemetry static asset | Không ghi DB |
| Telemetry lỗi 500 | Có ghi DB |
| Audit hủy phiếu | Có giữ audit |

### 7.2. Playwright/manual QA

Luồng IT:

- Nạp demo IT.
- Tạo phiếu nhập laptop không nhập HSD.
- Kiểm tra lưu/gửi duyệt không bị chặn vì HSD.
- Xuất laptop phải thấy vị trí lấy hàng.
- Nếu có serial, kiểm tra serial được yêu cầu trước khi hoàn tất.

Luồng y tế:

- Nạp demo y tế.
- Tạo phiếu nhập khẩu trang/găng tay/bộ test.
- Bỏ HSD và thử lưu: phải báo lỗi rõ.
- Nhập 2 lô khác HSD.
- Tạo phiếu xuất: hệ thống gợi ý lô HSD cũ nhất.
- Lịch sử nhập xuất hiển thị lô/HSD/vị trí.

Luồng thương mại điện tử:

- Nạp demo TMĐT.
- Tạo phiếu nhập tai nghe/sạc/chuột.
- Kiểm tra không bắt HSD nếu sản phẩm không quản lý hạn.
- Tạo phiếu xuất theo vị trí picking.
- Báo cáo theo kỳ hiển thị đúng nhập/xuất.

Màn báo cáo:

- Lọc từ ngày - đến ngày.
- Lọc theo kho.
- Lọc theo vật tư.
- Lọc theo lô.
- Xuất Excel.
- Kiểm tra không tràn bảng, không cắt chữ, không hiện `null`, `undefined`, `NaN`.

Màn chốt tồn:

- Chốt tồn ngày 1.
- Chốt tồn ngày 2.
- Xem lịch sử 2 mốc.
- Xem chi tiết một mốc cũ.
- Nếu có chênh lệch, tạo phiếu điều chỉnh.

## 8. UI/UX guideline

### 8.1. Báo cáo tổng hợp

- Bộ lọc nằm trên cùng, rõ "Từ ngày" và "Đến ngày".
- Bảng nhiều cột phải có horizontal scroll.
- Cột quan trọng nên cố định hoặc dễ nhìn:
  - Mã phiếu.
  - Mã vật tư.
  - Lô.
  - HSD.
  - SL nhập/xuất.
- Số lượng căn phải.
- HSD gần hết hạn dùng badge cảnh báo.
- Xuất nhập dùng màu khác nhau nhưng không quá chói.

### 8.2. Form nhập kho

- HSD disabled với vật tư không quản lý hạn.
- Với vật tư có HSD, label nên có dấu `*`.
- Tooltip ngắn: "Vật tư này quản lý hạn sử dụng".
- Không để user đoán tại sao bị chặn.

### 8.3. Form xuất kho

- Hiển thị gợi ý:

```text
Đề xuất FEFO: Lô MED-260701, HSD 31/12/2026, vị trí MED-A01-03, khả dụng 120 Hộp
```

- Nếu chọn khác:

```text
Bạn đang chọn lô không phải lô hết hạn gần nhất. Vui lòng nhập lý do.
```

### 8.4. Lịch sử chốt tồn

- Đặt dưới phần xem dữ liệu hiện tại.
- Có nút "Xem chi tiết".
- Không làm rối màn chính.

### 8.5. Roadmap chuẩn hóa sidebar, icon menu và phân cấp điều hướng

Mục tiêu: menu phải nhìn gọn, thẳng hàng, đúng vai trò và không làm người dùng bị ngợp. WMS Pro có nhiều chức năng là bình thường với một hệ thống WMS nội bộ cấp doanh nghiệp, nhưng cách trình bày không nên đưa gần như toàn bộ chức năng ra cùng một cấp menu.

Benchmark tham chiếu:

- SAP Fiori tổ chức launchpad theo hướng Space -> Page -> Section, trong đó Space/Page thường được gán theo business role hoặc hồ sơ công việc. Cách này giúp người dùng chỉ thấy nhóm ứng dụng phù hợp với vai trò của mình thay vì nhìn toàn bộ chức năng.
- Microsoft NavigationView được định hướng cho điều hướng cấp cao, có khả năng thích ứng theo kích thước màn hình. Các chức năng chi tiết nên được phân cấp bên trong thay vì dồn vào một flyout dài.

#### 8.5.1. Vấn đề UI hiện tại

Các ảnh kiểm tra cho thấy sidebar đang có hai vấn đề lớn:

1. Icon menu chưa nằm trên một trục thẳng đứng rõ ràng.
   - Có cảm giác icon bị lệch trái/lệch phải giữa các nhóm.
   - Khi sidebar thu gọn chỉ còn icon, đường nhìn không gọn như một thanh điều hướng doanh nghiệp.
   - Một số icon có vùng nền active khác kích thước hoặc căn giữa chưa đều, làm menu nhìn "méo" dù chức năng vẫn chạy.

2. Menu đang quá phẳng và quá nhiều mục trong cùng một popup.
   - Một menu con có khoảng 12-14 mục.
   - Toàn hệ thống có thể hơn 60 mục.
   - Quản trị viên còn có thể hiểu, nhưng nhân viên kho bình thường sẽ khó nhớ biểu tượng, phải đọc từng dòng và dễ bấm nhầm.

Đánh giá hiện tại cho phần điều hướng:

| Tiêu chí | Đánh giá |
|---|---|
| Chức năng đầy đủ | Tốt |
| Có chia phân hệ bằng icon | Đúng hướng |
| Icon thẳng hàng, đồng bộ kích thước | Cần chỉnh |
| Phân nhóm nghiệp vụ | Còn lẫn |
| Menu theo vai trò | Cần siết chặt |
| Số mục hiển thị một lần | Quá nhiều |
| Tên gọi nghiệp vụ | Một số mục còn kỹ thuật/khó hiểu |

Điểm UX điều hướng hiện tại ước tính: khoảng 6,5/10. Không phải vì thiếu chức năng, mà vì cách hiển thị chưa đủ "enterprise information architecture".

#### 8.5.2. Quy chuẩn icon/menu rail

Sidebar thu gọn nên dùng một chuẩn duy nhất:

- Mỗi icon nằm trong một ô điều hướng cố định.
- Kích thước ô đề xuất: 44-48 px.
- Icon căn giữa theo cả trục ngang và trục dọc.
- Active state dùng cùng kích thước nền, không mỗi mục một kiểu.
- Badge số lượng phải nằm đúng góc, không làm icon lệch trục.
- Không dùng margin/padding riêng lẻ cho từng icon nếu không cần.
- Nên dùng một class chung, ví dụ `nav-rail-item`, `nav-rail-icon`, `nav-rail-badge`.

Tiêu chí nghiệm thu UI:

- Khi sidebar thu gọn, tất cả icon tạo thành một hàng dọc thẳng.
- Active background không làm icon bị lệch.
- Badge không đẩy icon sang trái/phải.
- Icon trong menu flyout và icon trên rail cùng ngôn ngữ thị giác.
- Test ở desktop, tablet và mobile không bị lệch.

#### 8.5.3. Vấn đề phân nhóm nghiệp vụ hiện tại

Ví dụ nhóm Nhập kho hiện đang chứa nhiều loại nghiệp vụ khác nhau:

- Tạo phiếu nhập.
- Duyệt phiếu nhập.
- Tiếp nhận hàng.
- Kiểm tra chất lượng.
- Điều phối cửa bến.
- Quản lý bãi đỗ.
- Đối soát phí bãi.
- Bảng giá phí bãi.
- Tính phí kho nhiều chủ hàng.
- Bảng giá kho nhiều chủ hàng.
- Hợp đồng kho nhiều chủ hàng.
- Khu vực chủ hàng.

Các mục từ "Bảng giá phí bãi" trở xuống không phải thao tác nhập kho thường xuyên. Chúng nên nằm ở nhóm riêng như:

- Bãi và cửa kho.
- Dịch vụ và tính phí.
- Quản lý chủ hàng.
- Dịch vụ 3PL và hợp đồng.

Một số vị trí menu cần xem lại:

| Mục hiện tại | Vấn đề | Đề xuất |
|---|---|---|
| Cấu hình phân loại đơn | Không thuộc Tồn kho | Đưa vào Quản trị hệ thống hoặc Cấu hình vận hành |
| Cấu hình phát hành trực tiếp | Không thuộc Tồn kho | Đưa vào Cấu hình vận hành |
| Chốt tồn | Không thuộc Bảo mật | Đưa vào Kiểm soát tồn kho |
| Khóa kỳ | Không thuộc Bảo mật | Đưa vào Kiểm soát tồn kho hoặc Quản trị vận hành |
| Demo dữ liệu | Chỉ nên dùng demo/admin | Chỉ hiện với Admin và môi trường demo/local |
| Bảng giá kho, hợp đồng, tính phí | Nghiệp vụ tính phí/3PL | Gom vào Dịch vụ 3PL và tính phí |
| Bảng điều phối cửa bến | Tên hơi kỹ thuật | Đổi thành Điều phối cửa kho hoặc Lịch tiếp nhận tại cửa kho |

#### 8.5.4. Cấu trúc sidebar cấp cao đề xuất

Sidebar chỉ nên giữ khoảng 8 phân hệ cấp cao:

1. Trang chính.
2. Nhập kho.
3. Xuất kho.
4. Tồn kho.
5. Vận hành kho.
6. Báo cáo.
7. Danh mục.
8. Quản trị hệ thống.

Mỗi popup chỉ nên hiển thị khoảng 5-8 chức năng thường dùng. Các chức năng nâng cao đưa vào trang "Xem tất cả" hoặc "Trung tâm chức năng".

#### 8.5.5. Nhập kho

Hiển thị trực tiếp:

- Tạo phiếu nhập.
- Danh sách phiếu nhập.
- Duyệt phiếu nhập.
- Tiếp nhận hàng.
- Kiểm tra chất lượng.
- Cất hàng.
- Quét nhận hàng.

Nhóm nâng cao:

- Điều phối cửa kho.
- Quản lý bãi.
- Đối soát phí.
- Dịch vụ và tính phí.

Tên gọi đề xuất:

| Tên hiện tại | Tên đề xuất |
|---|---|
| Bảng điều phối cửa bến | Điều phối cửa kho |
| Quản lý bãi đỗ | Quản lý bãi xe |
| Đối soát phí bãi | Đối soát phí bãi xe |
| Quét nhận hàng bằng điện thoại | Quét nhận hàng |

#### 8.5.6. Xuất kho

Hiển thị trực tiếp:

- Tạo phiếu xuất.
- Danh sách phiếu xuất.
- Đợt gom đơn.
- Nhiệm vụ lấy hàng.
- Đóng gói.
- Bàn giao và giao hàng.
- Quét lấy hàng.

Nhóm nâng cao:

- Điều phối vận chuyển.
- Đối soát giao hàng.
- Bố trí cửa xuất.
- Nhãn và chứng từ.
- Kết nối vận tải.

Tên gọi đề xuất:

| Tên hiện tại | Tên đề xuất |
|---|---|
| Đóng gói & giao | Đóng gói và bàn giao |
| Nhiệm vụ tiếp theo | Công việc tiếp theo |
| Bộ kết nối vận tải | Kết nối vận tải |
| Bảng chuyến xe | Chuyến xe giao hàng |

#### 8.5.7. Tồn kho

Hiển thị trực tiếp:

- Xem tồn kho.
- Sản phẩm/vật tư.
- Sơ đồ kho.
- Lịch sử nhập xuất.
- Tra cứu mã kiện.
- Tra cứu số sê-ri.
- Kiểm kê.
- Bổ sung hàng.

Nhóm nâng cao:

- Tối ưu vị trí.
- Tối ưu vận hành.
- Nhiệm vụ di chuyển.
- Phiếu lắp bộ hàng.
- Chốt tồn.
- Khóa kỳ.

Tên gọi đề xuất:

| Tên hiện tại | Tên đề xuất |
|---|---|
| Số giao dịch tồn kho | Sổ giao dịch tồn kho |
| Tra cứu số sê-ri | Tra cứu serial |
| Phiếu lắp bộ hàng | Lắp bộ hàng |
| Chốt tồn | Chốt tồn kho |

#### 8.5.8. Báo cáo

Không nên đưa toàn bộ báo cáo ra flyout. Chỉ nên hiện các mục chính:

- Tổng quan vận hành.
- Báo cáo tồn kho.
- Báo cáo nhập xuất.
- Hàng sắp hết hạn.
- Hàng chậm luân chuyển.
- Chi phí vận hành.
- Cảnh báo bất thường.

Các báo cáo chi tiết nên nằm trong một trang:

```text
Báo cáo > Trung tâm báo cáo
```

Trong Trung tâm báo cáo chia tab:

- Tồn kho.
- Nhập xuất.
- Kiểm kê.
- Hạn sử dụng.
- Hiệu suất vận hành.
- Chi phí.
- Audit và cảnh báo.

Ưu điểm:

- Menu flyout gọn hơn.
- Người dùng dễ nhớ hơn.
- Vẫn giữ đủ chức năng, không xóa nghiệp vụ nào.

#### 8.5.9. Danh mục

Hiển thị trực tiếp:

- Cấu hình kho.
- Đối tác.
- Danh mục vật tư.
- Đơn vị tính.

Có thể bổ sung sau:

- Quy đổi đơn vị.
- Nhóm vật tư.
- Chính sách quản lý lô/HSD/serial.

#### 8.5.10. Quản trị hệ thống

Chỉ hiện với Admin hoặc vai trò được cấp quyền:

- Người dùng.
- Yêu cầu truy cập.
- Phân quyền khu vực.
- Quy tắc vận hành.
- Giám sát hệ thống.
- Demo dữ liệu.
- Phân tích nhật ký.
- Cảnh báo.
- Nhật ký.
- Thiết bị tin cậy.

Các mục nên chuyển ra khỏi Quản trị hệ thống nếu thiên về vận hành tồn:

- Chốt tồn kho.
- Khóa kỳ.

Đề xuất đưa vào:

```text
Tồn kho > Kiểm soát tồn kho
```

hoặc:

```text
Báo cáo/Quản trị vận hành > Chốt tồn, Khóa kỳ
```

#### 8.5.11. Phân menu theo vai trò

Đây là phần quan trọng nhất để menu nhìn chuẩn doanh nghiệp. Không phải ai cũng cần thấy tất cả chức năng.

| Vai trò | Nên thấy trực tiếp | Không nên thấy trực tiếp |
|---|---|---|
| Nhân viên tiếp nhận | Tạo/tiếp nhận phiếu, quét hàng, QC, cất hàng | Phân quyền, cấu hình, báo cáo tài chính |
| Nhân viên lấy hàng | Nhiệm vụ lấy hàng, quét lấy hàng, đóng gói | Demo dữ liệu, khóa kỳ, phân quyền |
| Nhân viên kiểm kê | Xem tồn, kiểm kê, tra cứu serial, lịch sử nhập xuất | Bảng giá phí, hợp đồng, cấu hình hệ thống |
| Trưởng kho | Duyệt phiếu, điều phối, báo cáo, cảnh báo, chốt tồn | Cấu hình auth/secrets |
| Quản trị viên | Người dùng, phân quyền, cấu hình, nhật ký, demo data | Không giới hạn nếu đúng môi trường |
| Chủ hàng/khách xem nếu có | Tồn kho/chứng từ thuộc phạm vi của họ | Dữ liệu kho khác, admin, báo cáo tài chính nội bộ |

Nguyên tắc:

- UI ẩn bớt menu theo vai trò.
- Backend vẫn phải check quyền, không chỉ ẩn menu.
- Role quyết định Space/Page hoặc nhóm chức năng mặc định.
- Người dùng có thể ghim nhanh chức năng thường dùng.

#### 8.5.12. Thiết kế "Xem tất cả"

Với nhóm có nhiều hơn 8 chức năng, popup chỉ hiện:

- 5-8 chức năng thường dùng.
- Link "Xem tất cả".

Trang "Xem tất cả" có:

- Search chức năng.
- Nhóm theo nghiệp vụ.
- Mô tả ngắn từng chức năng.
- Badge vai trò được phép dùng.
- Ghim vào menu nhanh.

Ví dụ:

```text
Nhập kho > Xem tất cả chức năng nhập kho
```

Các nhóm trong trang:

- Phiếu nhập.
- Tiếp nhận và QC.
- Cửa kho và bãi xe.
- Dịch vụ và phí.
- Chủ hàng.

#### 8.5.13. Definition of Done cho menu/sidebar

Chỉ xem là hoàn tất khi:

- Icon sidebar thu gọn nằm trên một hàng dọc thẳng, không lệch trái/phải.
- Mỗi popup menu còn khoảng 5-8 mục thường dùng.
- Các chức năng nâng cao có trang "Xem tất cả".
- Nhập kho không còn chứa lẫn bảng giá/hợp đồng/phí bãi ở cùng cấp thường dùng.
- Chốt tồn/Khóa kỳ không còn nằm trong nhóm bảo mật nếu không đúng nghiệp vụ.
- Demo dữ liệu chỉ hiện với Admin/môi trường demo.
- Menu thay đổi theo vai trò.
- Backend vẫn giữ authorization đầy đủ.
- Không xóa chức năng, chỉ tổ chức lại điều hướng.
- Playwright kiểm tra desktop/mobile không vỡ layout.
- Manual QA xác nhận nhân viên kho tìm được chức năng chính trong tối đa 2 bước.

#### 8.5.14. Accessibility, responsive và kiểm thử điều hướng

Để phần menu đạt cảm giác enterprise thật sự, roadmap cần khóa thêm các yêu cầu về khả năng truy cập và thao tác bằng bàn phím:

- Mỗi icon sidebar khi thu gọn phải có `aria-label` hoặc tooltip rõ nghĩa, ví dụ "Nhập kho", "Xuất kho", "Tồn kho", "Báo cáo".
- Trạng thái đang chọn phải rõ bằng màu, border và thuộc tính accessibility phù hợp; không chỉ dựa vào màu.
- Người dùng có thể dùng bàn phím để mở/đóng menu, di chuyển giữa mục, bấm Enter để chọn và Esc để đóng.
- Focus ring phải nhìn thấy rõ, không bị CSS xóa mất.
- Vùng bấm icon tối thiểu khoảng 40x40 px để dùng tốt trên laptop cảm ứng hoặc tablet kho.
- Popup menu không được vượt khỏi viewport; nếu danh sách dài phải có `max-height` và cuộn nội bộ.
- Trên màn hình nhỏ/tablet, menu phải chuyển sang dạng drawer hoặc overlay gọn, không che mất nội dung chính quá mức.
- Playwright nên có test cho menu desktop, menu thu gọn, mobile/tablet, keyboard navigation và kiểm tra không có overflow/tràn chữ.
- Static scan cần chặn text lỗi trong menu như `null`, `undefined`, `???`, tên kỹ thuật khó hiểu hoặc tên menu đặt sai nghiệp vụ.

## 9. Data integrity và database

### 9.1. Constraint nên có

| Constraint | Mục đích |
|---|---|
| Unique item code | Không trùng mã vật tư |
| Unique voucher code | Không trùng số phiếu |
| Check quantity >= 0 với tồn vị trí | Tránh tồn âm ở source of truth |
| Check expiry >= manufacturing date nếu có cả hai | Tránh HSD sai |
| Unique item + lot + expiry theo chính sách | Tránh cùng lô nhiều HSD |

### 9.2. Index nên có

| Bảng | Index |
|---|---|
| InventoryTransactions | TransactionAt, WarehouseId, ItemId |
| InventoryTransactions | ItemId, LotNumber, ExpiryDate |
| VoucherDetails | VoucherId, ItemId |
| VoucherDetails | ItemId, LotNumber, ExpiryDate |
| ItemLocations | ItemId, LocationId, LotNumber, ExpiryDate |
| StockSnapshots | WarehouseId, SnapshotDate |
| RequestTelemetryLogs | CreatedAt, StatusCode, Path |
| AuditLogs | CreatedAt, ActionType, EntityType |

### 9.3. Migration cần cân nhắc sau

Không bắt buộc làm ngay trong bước roadmap, nhưng nên đưa vào backlog:

- Thêm `StockSnapshotRuns`.
- Thêm `SourceReceiptDate` vào ledger nếu muốn report nhanh hơn.
- Thêm `ManufacturingDate` vào ledger nếu cần không phụ thuộc `VoucherDetail`.
- Thêm bảng `TelemetryCleanupRuns`.
- Thêm chính sách `ItemTrackingPolicy` nếu muốn tách khỏi `Item`.

## 10. Rủi ro và cách giảm rủi ro

| Rủi ro | Mức độ | Cách giảm |
|---|---|---|
| Báo cáo lấy dữ liệu từ phiếu cũ thiếu lô/HSD | Medium | Hiển thị "Không ghi nhận", không suy đoán |
| Chốt tồn cùng ngày bị ghi đè nếu chưa có run id | High | Giai đoạn sau thêm `StockSnapshotRuns` |
| FEFO tự động chọn lô sai do dữ liệu HSD thiếu | High | Bắt HSD với vật tư quản lý hạn, kiểm tra trước xuất |
| RequestTelemetry vẫn phình DB | Medium | Exclude static assets, sampling, retention job |
| UI bảng báo cáo quá rộng | Medium | Horizontal scroll, sticky cột quan trọng |
| User override FEFO bừa | High | Bắt lý do, phân quyền, audit |

## 11. Definition of Done

Chỉ xem là hoàn tất roadmap khi các điều kiện sau được chứng minh bằng code/test/browser hoặc được ghi rõ là chưa thể xác minh local:

| Điều kiện | Trạng thái 05/07/2026 | Bằng chứng / việc còn thiếu |
|---|---|---|
| Có màn thống kê nhập/xuất theo kỳ | Đã chứng minh | Route `/Reports/InventoryInOutSummary`, view, export Excel, test integration. |
| Có màn tổng quan kho/cockpit theo kỳ | Đã chứng minh | Route `/Reports/WarehouseOverview`, KPI tồn/giữ chỗ/khả dụng, dòng hàng theo ngày, backlog, top mã hàng, kiểm soát dữ liệu; test nghiệp vụ và visual route đều pass. |
| Từ ngày/đến ngày là bắt buộc | Đã chứng minh | UI đã `required`; backend không query dữ liệu và export trả lỗi nếu thiếu Từ ngày/Đến ngày hoặc chọn khoảng ngày ngược. Test `InventoryInOutSummary_ShouldRequireExplicitDateRangeOnServer` khóa regression này. |
| Lịch sử nhập xuất có lô, NSX, HSD, ngày nhập nguồn, vị trí nguồn, vị trí đích | Đã chứng minh chính | Stock movement và summary report đã có lô/NSX/HSD/vị trí; ngày nhập nguồn phụ thuộc dữ liệu transaction/voucher cũ. |
| Chốt tồn có lịch sử xem lại | Đã chứng minh và đã migrate DB hosting | Có bảng lịch sử chốt tồn, có `StockSnapshotRuns` để phân biệt nhiều lần chốt cùng ngày, có link xem chi tiết từng phiên, test không ghi đè chứng cứ chốt cũ, DB hosting đã backfill run id cho snapshot cũ. |
| Y tế bắt buộc HSD, IT không bắt HSD | Đã chứng minh bằng backend/API/form evidence | Demo data/test có y tế `TrackExpiry`; form nhập disable HSD cho vật tư không quản lý hạn; backend tạo phiếu và API tạo phiếu đều chặn thiếu HSD/HSD trước NSX/cùng lô khác HSD và bỏ qua HSD client gửi cho vật tư không quản lý hạn. |
| Xuất kho hiển thị lô/HSD/vị trí lấy hàng | Đã chứng minh trong code/test/visual | Voucher/detail/report/FEFO helper có lô/HSD/vị trí; visual route đã kiểm modal chọn lô/vị trí/FEFO không tràn và hidden source fields tồn tại. |
| FEFO hoạt động với vật tư có hạn dùng | Đã chứng minh bằng unit/integration/API/visual surface | FEFO sort trong allocation/helper/service; test FEFO có trong suite; UI bắt lý do override và backend kiểm lại quyền/lý do nếu chọn khác FEFO. |
| Không xuất vượt tồn theo lô/vị trí | Đã có test nghiệp vụ | Core business tests kiểm tồn khả dụng/reservation/lô/vị trí. |
| Telemetry không ghi static assets và có retention/sampling | Đã chứng minh phần chính | Test telemetry không lưu static/health, prune quá hạn, sampling request thành công nhanh, đồng thời vẫn giữ request lỗi/chậm để không mất bằng chứng vận hành. |
| Audit nghiệp vụ quan trọng vẫn được giữ | Đã chứng minh | AuditLogs vẫn được dùng cho nghiệp vụ, quyền, export, demo data và thao tác quan trọng. |
| Build/test pass | Đã chứng minh | Evidence gần nhất: build `0 warning / 0 error`, .NET tests `691/691`. |
| Playwright kiểm tra không lỗi UI chính | Đã chứng minh theo gate hiện có | `visual:test` pass `194 passed / 66 skipped`; targeted `warehouse-overview` + collapsed sidebar pass `7/7`, mobile collapsed skip đúng cấu hình. |
| Không sửa/xóa `appsettings.json` | Đã chứng minh | Hash giữ nguyên `7A3E4A74C0D7D7CBA0AF5EB91A65B06764CCDF38B79798D9C0063188A3C4A1EC`. |
| Không reset/seed/xóa DB thật | Đã tuân thủ | Không seed/reset/xóa dữ liệu; chỉ chạy migration đã được xác nhận trên DB hosting và audit lại index/constraint/data quality sau migration; audit mới nhất `artifacts/data-quality/wms-data-quality-audit-20260705-125614.txt` có `0` issue rows. |

## 12. Checklist triển khai đề xuất

Ký hiệu:

- `[x]`: đã có code/test/DB/visual evidence đủ để xem là hoàn tất trong phạm vi roadmap này.

### Backend

- [x] Tạo ViewModel cho báo cáo nhập/xuất theo kỳ.
- [x] Tạo ViewModel cho báo cáo tổng quan kho/cockpit.
- [x] Tạo action `/Reports/InventoryInOutSummary`.
- [x] Tạo action `/Reports/WarehouseOverview`.
- [x] Tạo action export Excel.
- [x] Bắt buộc `Từ ngày` và `Đến ngày` ở backend cho màn thống kê nhập/xuất theo kỳ; không còn tự query mặc định khi thiếu ngày.
- [x] Bổ sung query lấy lô/NSX/HSD/ngày nhập nguồn/vị trí.
- [x] Bổ sung lịch sử chốt tồn theo ngày/kho.
- [x] Nếu cần chuẩn nhiều lần chốt trong ngày: thêm `StockSnapshotRuns`.
- [x] Bổ sung backend validation HSD theo vật tư trong luồng tạo phiếu chính: bắt HSD với vật tư quản lý hạn, chặn HSD trước NSX, chặn cùng lô nhiều HSD, bỏ qua HSD client gửi cho vật tư không quản lý hạn.
- [x] Bổ sung validation HSD/lô cho API `POST /api/v1/vouchers`: DTO nhận `ManufacturingDate`/`ExpiryDate`, normalize lô, chặn thiếu HSD/lô theo policy, chặn HSD trước NSX, chặn cùng lô nhiều HSD và lưu ngày vào `VoucherDetail`.
- [x] Bổ sung FEFO source selection cho xuất kho nếu còn thiếu.
- [x] Bổ sung telemetry sampling/exclusion/retention: bỏ static/health, prune retention, sample request thành công nhanh nhưng luôn giữ lỗi/chậm.
- [x] Bổ sung migration DB guard `20260705070000_RepairReportFefoDatabaseGuards`: repair index/constraint còn thiếu cho ledger, telemetry, audit, tồn theo lô/vị trí, voucher detail, item/voucher unique key và snapshot runs trên đúng schema thật của DB hosting.

### Frontend

- [x] Tạo view báo cáo tổng hợp.
- [x] Tạo view báo cáo tổng quan kho/cockpit và thêm menu `Báo cáo > Tổng quan kho`.
- [x] Sửa view báo cáo tổng hợp về tiếng Việt UTF-8 chuẩn, không còn mojibake trên tiêu đề, bộ lọc, bảng và vùng tổng hợp.
- [x] Thêm menu báo cáo.
- [x] Nâng cấp view lịch sử nhập xuất.
- [x] Nâng cấp view chốt tồn.
- [x] Form nhập kho disable HSD với vật tư không quản lý hạn.
- [x] Form xuất kho hiển thị lô/HSD/vị trí lấy hàng. Đã bổ sung nút `Chọn lô/vị trí`, note FEFO trên từng dòng, hidden source `LotNumber`/`ExpiryDate`/`FefoOverrideReason`, và API trả tồn khả dụng theo lô/HSD/vị trí để nhân viên lấy đúng hàng.
- [x] Badge FEFO và cảnh báo override đầy đủ khi người dùng cố chọn lô khác gợi ý. Popup hiển thị `Đề xuất FEFO`, `Cần lý do nếu chọn`, `Không đủ SL`; Staff bị chặn chọn khác FEFO, Admin/Manager phải nhập lý do; backend cũng kiểm tra lại để không tin dữ liệu client.
- [x] Kiểm tra responsive/horizontal scroll và icon rail cơ bản. `npm run visual:test` pass `190 passed / 66 skipped`; Playwright `collapsed sidebar keeps enterprise rail groups and flyouts` pass trên desktop 100/110/125, kiểm các icon rail cùng một trục dọc và vùng bấm đủ lớn.
- [x] Sửa lệch icon collapsed sidebar: link đơn, nhóm menu và logo cùng tâm icon; test đo `iconCenterX` khóa regression.

### Test

- [x] Unit/integration test HSD theo vật tư.
- [x] Unit test FEFO. Bổ sung test API gợi ý vị trí xuất phải ưu tiên lô HSD cũ nhất, trả tồn khả dụng, loại tồn QC/near-expiry/đã giữ chỗ.
- [x] Integration test báo cáo theo kỳ.
- [x] Integration test snapshot history. Có code/view history và test `GenerateStockSnapshot_ShouldCreateNewRunAndKeepExistingClosingEvidence` assert nhiều phiên chốt cùng ngày không ghi đè dữ liệu cũ.
- [x] Test telemetry không ghi static assets.
- [x] Playwright/form evidence nhập IT không HSD. Visual route `voucher-create-inbound`/`voucher-create-outbound`, `visual:test`, `visual:mobile-deep` pass; backend/API regression chứng minh item không `TrackExpiry` vẫn lưu được và HSD client gửi bị bỏ qua. DB hosting hiện có item không HSD, nhưng không seed/reset DB để tạo thêm phiếu diễn.
- [x] Playwright/form evidence nhập y tế bắt HSD. Visual route và backend/API regression chứng minh item `TrackExpiry` bắt HSD, chặn HSD trước NSX/cùng lô khác HSD. DB hosting hiện không có item `TrackExpiry=true` sẵn, nên không seed dữ liệu thật chỉ để chạy write UI domain y tế.
- [x] Playwright xuất theo FEFO. Smoke browser kiểm màn xuất kho có nút chọn lô/vị trí, hidden lot/HSD/reason và modal FEFO không tràn; unit/integration/API test kiểm FEFO ưu tiên HSD cũ nhất, loại tồn không khả dụng và bắt lý do/quyền override.
- [x] Playwright báo cáo tổng hợp.
- [x] Playwright báo cáo tổng quan kho/cockpit.
- [x] Playwright chốt tồn history. Route `/Reports/StockSnapshot` đã vào `visual:mobile-deep` và pass; integration test assert bảng history có nhiều phiên chốt cùng ngày không ghi đè dữ liệu cũ; DB hosting đã có `StockSnapshotRuns` và snapshot cũ được backfill run id.

## 13. Kết luận

Các yêu cầu trong roadmap này là đúng hướng cho một WMS nội bộ nghiêm túc. Điểm quan trọng nhất là không chỉ quản lý "còn bao nhiêu hàng", mà phải quản lý được:

- Hàng thuộc lô nào.
- Hạn sử dụng nào.
- Được nhập ngày nào.
- Đang ở vị trí nào.
- Khi xuất đã lấy từ vị trí nào.
- Ai tạo, ai duyệt, ai xác nhận.
- Báo cáo có truy xuất lại được hay không.

Ưu tiên cao nhất nên là:

1. Bổ sung lô/HSD/vị trí vào lịch sử nhập xuất.
2. Chuẩn hóa HSD theo từng loại vật tư.
3. Xuất kho theo FEFO và hiển thị vị trí lấy hàng.
4. Thêm báo cáo tổng hợp nhập/xuất theo kỳ.
5. Thêm lịch sử chốt tồn.
6. Kiểm soát telemetry để tránh phình database.

Tính đến trạng thái đối chiếu 05/07/2026, roadmap này đã hoàn tất trong phạm vi code/backend/API/database/local visual evidence. Các phần báo cáo tổng hợp, lịch sử nhập xuất, lịch sử chốt tồn theo ngày/kho và theo phiên `StockSnapshotRuns`, HSD policy trên form nhập/backend/API tạo phiếu, FEFO source selection/override guard, DB guard/index/constraint, telemetry/audit index, visual desktop/mobile/public/no-device và real E2E read-only đều đã có bằng chứng chạy thật. Những việc còn thuộc checklist vận hành ngoài local/browser như load/k6, thiết bị scanner/printer thật, DR/HA, release artifact hosting và UAT nhiều người dùng không phải hạng mục code còn thiếu của roadmap này.
