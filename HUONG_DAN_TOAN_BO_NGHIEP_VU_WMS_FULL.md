# Sổ Tay Vận Hành Toàn Bộ Nghiệp Vụ WMS Pro

> Phiên bản đối chiếu: 18/07/2026  
> Phạm vi: hệ thống quản lý kho nội bộ  
> Nguồn hướng dẫn theo vai trò: menu `Hướng dẫn sử dụng` trên ứng dụng

## 1. Mục Đích Và Nguyên Tắc

Tài liệu này mô tả cách vận hành WMS Pro từ dữ liệu nền, nhập kho, xuất kho, tồn kho, vận chuyển, báo cáo đến quản trị. Đây là sổ tay tổng hợp; người cần thực hành từng bước dùng thêm `HUONG_DAN_THUC_HANH_WMS_CHI_TIET.md`.

Năm nguyên tắc phải giữ trong mọi thao tác:

1. Chỉ làm việc trong kho, khu vực và chủ hàng được cấp.
2. Không sửa trực tiếp database để thay đổi tồn, trạng thái phiếu hoặc lịch sử.
3. Số lượng thực tế, số sê-ri, lô/hạn dùng, mã kiện và cân thực tế là các kiểm soát riêng; hoàn thành một kiểm soát không tự thay thế kiểm soát khác.
4. Nghiệp vụ bị từ chối phải giữ nguyên dữ liệu; không bấm lặp liên tục khi chưa hiểu nguyên nhân.
5. Tồn chỉ thay đổi sau bước ghi sổ/hoàn tất hợp lệ và phải đối chiếu được với sổ giao dịch tồn kho.

## 2. Kiểm Tra Trước Khi Làm Việc

Sau khi đăng nhập, kiểm tra ở góc phải:

- Đúng tên người dùng và vai trò.
- Đúng kho vận hành.
- Đúng phạm vi chủ hàng và khu vực nếu hệ thống có áp dụng.
- Không có cảnh báo kỳ khóa hoặc sự cố hệ thống chưa xử lý.

Không thấy menu/nút chưa chắc là lỗi. Hệ thống có thể đang ẩn chức năng do vai trò, permission hoặc data scope. Không mở URL trực tiếp để vượt kiểm soát; báo quản trị viên kiểm tra quyền.

## 3. Vai Trò Và Phạm Vi

| Vai trò | Công việc chính | Không nên thực hiện |
| --- | --- | --- |
| Quản trị viên (`Admin`) | Quản lý người dùng, phân quyền, cấu hình, giám sát và toàn bộ nghiệp vụ | Dùng tài khoản quản trị cho công việc quét thường ngày khi không cần thiết |
| Quản lý kho (`Manager`) | Duyệt, điều phối, xử lý ngoại lệ, theo dõi KPI và kiểm soát vận hành | Tự làm cả hai bước bị tách nhiệm vụ nếu policy không cho phép |
| Nhân viên kho (`Staff`) | Thực hiện các tác vụ kho cơ bản được cấp | Cấu hình hệ thống hoặc tự duyệt thao tác nhạy cảm |
| Nhân viên nhập kho (`InboundStaff`) | Tiếp nhận, quét nhận, ghi nhận thực tế, kiểm phẩm | Xuất kho hoặc điều chỉnh tồn ngoài luồng nhập |
| Nhân viên xuất kho (`OutboundStaff`) | Nhận nhiệm vụ, lấy hàng, đóng gói và bàn giao | Nhập kho hoặc thay đổi dữ liệu nền |
| Nhân viên tồn kho/kiểm kê (`InventoryStaff`) | Tra cứu tồn, di chuyển, kiểm kê, đề xuất điều chỉnh | Tự phê duyệt chênh lệch nếu policy yêu cầu người khác duyệt |
| Nhân viên vận chuyển (`TransportStaff`) | Điều phối, chuyến xe, chứng từ, đối soát giao hàng | Làm đổi tồn ngoài bước bàn giao được thiết kế |
| Nhân viên báo cáo (`ReportViewer`) | Xem tổng quan, KPI và báo cáo trong phạm vi | Ghi sổ hoặc thay đổi chứng từ |
| Người chỉ xem (`Viewer`) | Tra cứu dữ liệu được cấp | Mọi hành động làm đổi dữ liệu |

Quyền thực tế là giao của vai trò, permission chi tiết, kho, chủ hàng và khu vực. Tên vai trò giống nhau không đảm bảo nhìn cùng dữ liệu nếu data scope khác nhau.

## 4. Bản Đồ Menu Chuẩn

### Trang chính

Hiển thị công việc cần xử lý, lối vào nhanh và chỉ số phù hợp vai trò. Trang chính không thay thế báo cáo chi tiết.

### Nhập kho

- Tạo phiếu nhập
- Duyệt phiếu nhập
- Tiếp nhận hàng
- Quét nhận hàng
- Kiểm tra chất lượng
- Lịch sử nhập kho

### Xuất kho

- Tạo phiếu xuất
- Đợt gom đơn
- Nhiệm vụ lấy hàng
- Quét lấy hàng
- Nhiệm vụ tiếp theo
- Đóng gói & giao

### Tồn kho

- Xem tồn kho
- Sơ đồ kho
- Tra cứu mã kiện
- Tra cứu số sê-ri
- Kiểm kê
- Kiểm kê thông minh
- Điều chỉnh tồn kho
- Nhiệm vụ di chuyển và quét di chuyển
- Hàng sắp thiếu, hàng chậm và lịch sử nhập xuất

### Vận chuyển

Gồm điều phối vận chuyển, bảng chuyến xe, đối soát giao hàng, nhãn/chứng từ và các chức năng vận tải nâng cao khi được cấp.

### Báo cáo

Gồm tổng quan kho, chỉ số vận hành, thống kê nhập/xuất, báo cáo tồn kho, vận chuyển, chi phí, quản trị dữ liệu và bất thường dữ liệu.

### Danh mục

Gồm đối tác, danh mục vật tư, đơn vị tính, khu vực kho, vị trí/kệ/khu chứa và cấu hình nền được phân quyền.

### Hệ thống

Gồm người dùng, yêu cầu truy cập, phân quyền khu vực, quy tắc vận hành, giám sát, nhật ký, cảnh báo, chốt tồn, khóa kỳ và công cụ quản trị nâng cao.

## 5. Dữ Liệu Nền

Trước khi tạo giao dịch, bảo đảm:

- Kho, khu vực và vị trí đang hoạt động và đúng mục đích.
- Vật tư có mã duy nhất, tên dễ nhận biết và đơn vị tồn đúng.
- Cấu hình quản lý lô, hạn dùng, số sê-ri, cân thực tế và quy đổi đơn vị phù hợp.
- Đối tác đúng loại và đúng phạm vi chủ hàng nếu dùng kho nhiều chủ hàng.
- Vị trí có sức chứa, quy tắc một vật tư hoặc trộn vật tư đúng policy vận hành.

Không đổi cấu hình theo dõi lô/số sê-ri giữa lúc còn giao dịch dở dang nếu chưa có kế hoạch chuyển đổi và đối chiếu dữ liệu.

## 6. Nhập Kho

### 6.1 Tạo và gửi duyệt

1. Mở `Nhập kho > Tạo phiếu nhập`.
2. Chọn kho, nguồn giao/đối tác, ngày chứng từ và thông tin xe/cửa nhận nếu đã biết.
3. Thêm từng dòng vật tư, đơn vị nhập, số lượng, lô/hạn dùng và vị trí cất dự kiến.
4. Lưu tạm, mở lại kiểm tra thông tin đầu phiếu và dòng hàng.
5. Gửi duyệt.

Vị trí hệ thống điền sẵn phải được xử lý như vị trí do nút gợi ý trả về: cùng một validator, cùng điều kiện kho/khu vực/sức chứa. Nếu hệ thống báo xung đột vật tư, không đổi dữ liệu tồn trực tiếp; chọn vị trí hợp lệ khác hoặc nhờ quản lý kiểm tra quy tắc vị trí.

### 6.2 Duyệt và tiếp nhận

- Người duyệt kiểm tra chứng từ, vật tư, số lượng, kho, chủ hàng và điều kiện nhận.
- Nếu quy tắc tách nhiệm vụ được bật, người lập/nhận/kiểm/ghi sổ phải là các danh tính phù hợp policy.
- Lập lịch nhận hàng hoặc bổ sung cửa/khung giờ trước khi bắt đầu nếu workflow yêu cầu.
- Nút đang xử lý phải kết thúc bằng thành công hoặc thông báo lỗi rõ ràng; khi mạng yếu, kiểm tra `Hàng đợi quét` trước khi gửi lại.

### 6.3 Ghi nhận thực tế

Nhân viên nhập số thực nhận theo từng dòng. Hệ thống kiểm tra độc lập:

- Lô và hạn dùng.
- Vị trí cất cùng kho và đúng policy.
- Số sê-ri đủ và không trùng đối với hàng quản lý từng chiếc.
- Cân thực tế đối với mặt hàng bắt buộc cân.
- Số lượng đạt, lỗi và chênh lệch kiểm phẩm.

Thông báo “đã ghi nhận đủ số lượng” chỉ xác nhận số lượng. Nếu mặt hàng theo số sê-ri, phải mở chức năng nhận số sê-ri và đăng ký đủ từng mã trước khi hoàn tất.

### 6.4 Kiểm tra chất lượng và hoàn tất

1. Chọn đúng dòng hàng; danh sách phải hiển thị mã và tên vật tư, không chỉ dấu gạch.
2. Ghi số mẫu, số đạt, số lỗi, hướng xử lý và mô tả khiếm khuyết.
3. Hoàn tất nhập khi mọi điều kiện bắt buộc đã đạt.
4. Đối chiếu số lượng tăng tại `Xem tồn kho`, `Lịch sử nhập kho` và sổ giao dịch.

Không coi phiếu “đã nhận” là đã tăng tồn nếu bước ghi sổ/hoàn tất chưa thành công.

## 7. Xuất Kho

### 7.1 Tạo phiếu và giữ chỗ

1. Mở `Xuất kho > Tạo phiếu xuất`.
2. Chọn kho, đối tác/chủ hàng, vật tư, số lượng và yêu cầu giao.
3. Phát hành theo quyền để hệ thống giữ chỗ tồn khả dụng.
4. Kiểm tra lô/hạn dùng theo FEFO/FIFO và vị trí nguồn được đề xuất.

Không xuất vượt tồn khả dụng hoặc lấy chéo chủ hàng. Giữ chỗ phải nhất quán với tồn, lô, vị trí và số sê-ri.

### 7.2 Đợt gom và lấy hàng

- Quản lý tạo đợt gom đơn khi cần gom nhiều phiếu.
- Nhân viên nhận `Nhiệm vụ lấy hàng` hoặc `Nhiệm vụ tiếp theo`.
- Quét vị trí, vật tư, mã kiện/tote và số sê-ri theo yêu cầu.
- Nếu lấy thiếu, dùng luồng báo thiếu/phân bổ lại; không tự nhập số đã lấy lớn hơn thực tế.

### 7.3 Chốt xuất

Trước khi chốt:

- Nhiệm vụ lấy đã hoàn tất hoặc phần còn lại đã được xử lý đúng trạng thái.
- Người chốt đáp ứng quy tắc tách nhiệm vụ.
- Dữ liệu phiên bản hiện tại chưa bị người khác cập nhật.
- Giữ chỗ và số thực xuất còn khớp.

Nếu hệ thống từ chối do tách nhiệm vụ, thao tác không được làm giảm tồn. Chuyển cho người đủ quyền/danh tính khác; không coi trạng thái màn hình trung gian là đã ghi sổ.

### 7.4 Đóng gói và bàn giao

- Tạo kiện xuất/mã kiện phù hợp.
- Ghi cân thực tế nếu bắt buộc.
- In nhãn/chứng từ từ dữ liệu đã xác nhận.
- Chọn giao trực tiếp hoặc xếp lên chuyến xe theo cấu hình.
- Đối chiếu sau bàn giao; retry không được tạo trùng nhật ký hoặc giảm tồn lần hai.

## 8. Tồn Kho, Mã Kiện Và Di Chuyển

Tồn được phân tách theo kho, vị trí, vật tư, chủ hàng, lô/hạn dùng và trạng thái giữ chỗ. Các màn tra cứu phải cùng data scope.

### Mã kiện

- Mỗi mã kiện có kho, vị trí, trạng thái và nội dung.
- Không trộn chủ hàng hoặc nội dung trái quy tắc.
- Di chuyển nguyên kiện phải cập nhật vị trí kiện và tồn liên quan trong cùng transaction.

### Số sê-ri

- Mỗi số sê-ri là duy nhất trong phạm vi được định nghĩa.
- Trạng thái, vị trí, mã kiện và lịch sử phải khớp với tồn.
- Không tái sử dụng số sê-ri đang hoạt động cho một sản phẩm khác.

### Di chuyển và bổ sung hàng

- Chọn đúng vị trí nguồn/đích, vật tư, lô và số lượng.
- Kiểm tra sức chứa và quy tắc vùng.
- Hoàn tất nhiệm vụ mới làm đổi vị trí tồn.
- Bổ sung hàng về khu lấy hàng không được tạo tồn mới; tổng tồn toàn kho phải giữ nguyên.

## 9. Kiểm Kê Và Điều Chỉnh

Luồng chuẩn:

1. Tạo đợt kiểm kê theo phạm vi kho/khu/vị trí/vật tư.
2. Giao nhiệm vụ cho người đếm.
3. Thực hiện đếm mù nếu policy yêu cầu.
4. Nhập số đếm và gửi chênh lệch.
5. Kiểm tra lại/recount khi vượt ngưỡng.
6. Người có quyền phê duyệt điều chỉnh.
7. Đối chiếu tồn và sổ giao dịch sau điều chỉnh.

Không điều chỉnh để “khớp báo cáo” khi chưa có nguyên nhân, bằng chứng và phê duyệt.

## 10. Kiểm Kê Thông Minh Và AI

Màn `Tồn kho > Kiểm kê thông minh` hỗ trợ ưu tiên vị trí/vật tư có rủi ro sai lệch.

AI được phép:

- Xếp hạng rủi ro.
- Nêu yếu tố ảnh hưởng và độ mới dữ liệu.
- Đề xuất phạm vi kiểm kê và mức ưu tiên.

AI không được phép:

- Tự điều chỉnh tồn.
- Tự ghi sổ giao dịch.
- Bỏ qua quyền, data scope, kỳ khóa hoặc phê duyệt của con người.

Trước khi dùng đề xuất, kiểm tra thời điểm tính, phiên bản mô hình/quy tắc, dữ liệu nguồn, lý do và trạng thái còn hiệu lực. Nếu dữ liệu cũ, thiếu hoặc mô hình không sẵn sàng, dùng quy tắc dự phòng và quy trình kiểm kê thông thường.

## 11. Vận Chuyển

1. Kiểm tra kiện đã sẵn sàng bàn giao.
2. Điều phối đơn/vận đơn theo tuyến và đơn vị vận chuyển.
3. Tạo chuyến xe, gắn kiện/phiếu và quét xếp xe.
4. In nhãn, bản kê hoặc biên bản bàn giao.
5. Xác nhận rời kho/giao hàng theo quyền.
6. Đối soát trạng thái, số kiện, phí và ngoại lệ giao hàng.

Không giao trực tiếp một kiện đang thuộc chuyến hoạt động. Retry tích hợp phải có idempotency; không tạo vận đơn, nhật ký bàn giao hoặc phí trùng.

## 12. Báo Cáo Và Đối Soát

Khi đọc báo cáo:

- Chọn đúng từ ngày/đến ngày và kho.
- Kiểm tra thời điểm dữ liệu, đơn vị tính và tiền tệ.
- Phân biệt tồn hiện tại với dòng phát sinh trong kỳ.
- Mở drill-down để đối chiếu chứng từ/sổ giao dịch.
- Đảm bảo export dùng cùng bộ lọc và data scope với màn hình.

`Tổng quan kho` phục vụ điều hành nhanh. `Thống kê nhập/xuất` phục vụ đối chiếu phát sinh theo kỳ. Hai màn có thể dùng chung nguồn dữ liệu nhưng không được hiểu là cùng mục đích.

## 13. Quản Trị Hệ Thống

Quản trị viên chịu trách nhiệm:

- Tạo/khóa tài khoản và cấp quyền tối thiểu.
- Gán kho, chủ hàng, khu vực đúng nhiệm vụ.
- Theo dõi yêu cầu truy cập, thiết bị tin cậy và nhật ký.
- Cấu hình quy tắc vận hành, chốt tồn và khóa kỳ theo quy trình phê duyệt.
- Giám sát cảnh báo, lỗi nền và chất lượng dữ liệu.

Không xóa tài khoản đã có lịch sử; khóa tài khoản để giữ audit. Không thay đổi secret hoặc connection string trong tài liệu, log, ảnh chụp hay artifact.

## 14. Xử Lý Lỗi Và Ngoại Lệ

| Hiện tượng | Cách xử lý an toàn |
| --- | --- |
| Nút quay lâu | Chờ phản hồi; kiểm tra thông báo và hàng đợi quét; không bấm lặp |
| Thiếu số sê-ri | Mở chức năng nhận/quét số sê-ri và đăng ký đủ từng chiếc |
| Vi phạm tách nhiệm vụ | Chuyển bước cho người đủ quyền/danh tính khác |
| Xung đột vị trí một vật tư | Chọn vị trí hợp lệ hoặc nhờ quản lý kiểm tra nội dung vị trí |
| Kỳ đã khóa | Không sửa lùi ngày; báo quản lý thực hiện quy trình mở/điều chỉnh hợp lệ |
| Dữ liệu vừa thay đổi | Tải lại màn hình, đối chiếu trạng thái mới rồi thao tác lại |
| Mất mạng khi quét | Giữ thao tác trong hàng đợi, gửi lại một lần khi kết nối ổn định |
| AI không có kết quả | Dùng quy tắc dự phòng và kiểm kê thủ công; không bỏ qua kiểm soát |
| Báo cáo lệch màn chi tiết | So bộ lọc, snapshot time, scope và sổ giao dịch; lập ngoại lệ nếu vẫn lệch |

Khi báo lỗi, cung cấp mã phiếu/mã nhiệm vụ, thời điểm, tài khoản/vai trò, kho, bước thực hiện và correlation ID nếu giao diện hiển thị. Không chụp hoặc sao chép secret.

## 15. Checklist Cuối Ca

- Không còn thao tác quét chờ hoặc lỗi chưa xử lý.
- Phiếu dở dang có người chịu trách nhiệm và bước tiếp theo rõ ràng.
- Số lượng nhận/xuất trong ca đối chiếu được với sổ giao dịch.
- Nhiệm vụ lấy, di chuyển và kiểm kê quá hạn đã được bàn giao.
- Chuyến xe/kiện giao dở dang có trạng thái đúng.
- Ngoại lệ số sê-ri, lô/hạn dùng, giữ chỗ và vị trí đã được ghi nhận.
- Không để tài khoản đăng nhập trên thiết bị dùng chung.

## 16. Quy Tắc Demo Với Database Hosting

- Dùng đúng connection đã được chủ hệ thống cấu hình; không sao chép giá trị vào tài liệu hoặc chat.
- Không chạy migration, reset, truncate, cleanup hàng loạt hoặc SQL sửa trực tiếp.
- Không nạp lại dữ liệu mẫu khi còn giao dịch dở dang hoặc dữ liệu demo cũ chưa được xử lý bằng workflow an toàn.
- Ưu tiên kịch bản đọc và kịch bản ghi đã được chuẩn bị riêng, có thể nhận diện và đảo giao dịch.
- Trước buổi demo, chạy build/test, kiểm tra đăng nhập, vai trò, các route chính và đối chiếu tồn ở chế độ an toàn.

## 17. Tiêu Chí Kết Quả Đúng

Một luồng chỉ được coi là hoàn thành khi:

1. Trạng thái đúng workflow và không bỏ bước kiểm soát.
2. Tồn, giữ chỗ, số sê-ri, mã kiện và giao dịch thay đổi đúng một lần.
3. Người thao tác, thời gian, lý do và audit cần thiết đã được ghi.
4. Báo cáo/drill-down khớp cùng phạm vi và thời điểm dữ liệu.
5. Retry, double-click hoặc lỗi giữa transaction không tạo dữ liệu trùng hay trạng thái nửa chừng.

Không có tài liệu nào tự chứng minh hệ thống không còn lỗi. Trước mỗi bản bàn giao phải dùng build, test, data-quality và kiểm thử giao diện của đúng phiên bản để xác nhận phạm vi đã kiểm chứng.
