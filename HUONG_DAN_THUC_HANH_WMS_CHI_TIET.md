# Hướng Dẫn Thực Hành WMS Pro Theo Vai Trò

> Phiên bản vận hành: 18/07/2026  
> Phạm vi: hệ thống quản lý kho nội bộ  
> Nguồn chuẩn trên ứng dụng: `Hướng dẫn sử dụng` trong menu trái  
> Nguyên tắc: thao tác qua nghiệp vụ ứng dụng, không sửa trực tiếp database

## Cách Dùng Tài Liệu

Tài liệu này dành cho quản trị viên, quản lý kho, nhân viên nhập kho, nhân viên xuất kho, nhân viên tồn kho/kiểm kê, nhân viên vận chuyển, nhân viên báo cáo và người chỉ xem. Chỉ thực hành bài phù hợp với vai trò được cấp. Việc không thấy menu hoặc nút có thể là kiểm soát quyền đúng, không mặc nhiên là lỗi.

Trước mỗi bài, kiểm tra tài khoản, vai trò, kho vận hành và phạm vi chủ hàng ở góc phải. Với dữ liệu demo trên database hosting, chỉ dùng bộ dữ liệu đã được chuẩn bị; không chạy SQL sửa tồn, không nạp lại dữ liệu khi đang có giao dịch dở dang và không dùng chứng từ thật để thử thao tác ghi.

Mỗi bài có năm tiêu chí kết thúc:

1. Màn hình không báo lỗi máy chủ hoặc quay vô hạn.
2. Trạng thái sau thao tác đúng với workflow.
3. Tồn, giữ chỗ, số sê-ri, mã kiện và sổ giao dịch chỉ đổi ở bước được phép.
4. Người thao tác và thời gian được ghi nhận khi nghiệp vụ yêu cầu audit.
5. Khi thao tác bị từ chối, dữ liệu phải giữ nguyên.

## Bài Thực Hành 1: Đăng Nhập, Vai Trò Và Phạm Vi Dữ Liệu

**Mục tiêu:** xác nhận đúng danh tính và phạm vi trước khi thao tác.

**Vai trò:** tất cả.

**Thực hiện:**

1. Đăng nhập bằng tài khoản cá nhân; không dùng chung tài khoản.
2. Kiểm tra tên, vai trò và kho vận hành ở góc phải.
3. Mở menu trái và đối chiếu với vai trò: nhập kho chỉ thấy luồng nhập; xuất kho chỉ thấy luồng xuất; tồn kho/kiểm kê chỉ thấy tồn; vận chuyển chỉ thấy giao nhận; báo cáo không có nút làm đổi tồn.
4. Mở `Hướng dẫn sử dụng`; kiểm tra tiêu đề vai trò và các lối vào nhanh được cá nhân hóa.
5. Thử mở một URL ngoài quyền nếu có kịch bản kiểm thử được phê duyệt. Kết quả đúng là bị từ chối hoặc chuyển về màn an toàn, không lộ dữ liệu.

**Kết quả đúng:** menu, nút và dữ liệu cùng tuân theo vai trò, kho, khu vực và chủ hàng. Admin có đầy đủ quyền chức năng nhưng vẫn phải tuân thủ tách nhiệm vụ và phạm vi dữ liệu của nghiệp vụ.

**Dừng và báo:** tài khoản thấy dữ liệu kho/chủ hàng không được gán, hoặc một URL trực tiếp cho phép vượt quyền.

## Bài Thực Hành 2: Khai Báo Dữ Liệu Nền

**Mục tiêu:** hiểu quan hệ giữa đối tác, vật tư, đơn vị tính, kho, khu vực và vị trí.

**Vai trò:** quản trị viên hoặc quản lý kho có quyền danh mục.

**Màn hình:** `Danh mục -> Đối tác`, `Danh mục vật tư`, `Đơn vị tính`, `Khu vực kho`, `Vị trí/kệ/khu chứa`.

**Thực hiện:**

1. Tra cứu trước khi tạo để tránh trùng mã.
2. Kiểm tra đơn vị cơ sở của vật tư; quy đổi phải dương và có ý nghĩa nghiệp vụ.
3. Bật quản lý lô, hạn dùng, số sê-ri hoặc cân thực tế trước khi vật tư phát sinh giao dịch.
4. Kiểm tra vị trí thuộc đúng kho, đang hoạt động và đúng loại khu vực.
5. Với dữ liệu đã phát sinh, ưu tiên ngừng sử dụng thay vì xóa.
6. Mở màn tạo phiếu và xác nhận vật tư/đơn vị/vị trí xuất hiện đúng.

**Kết quả đúng:** mã không trùng, quan hệ kho-vị trí hợp lệ và dữ liệu nền có thể truy vết. Một vị trí chỉ nhận khóa vật tư/chủ hàng phù hợp với chính sách lưu trữ.

**Dừng và báo:** đơn vị quy đổi bằng 0, vị trí thuộc kho khác, vật tư bị đổi cách quản lý sau khi đã có tồn hoặc hệ thống cho xóa dữ liệu đang được tham chiếu.

## Bài Thực Hành 3: Tạo Phiếu Nhập

**Mục tiêu:** tạo chứng từ đầu vào đầy đủ nhưng chưa làm tăng tồn.

**Vai trò:** quản lý kho, nhân viên kho tổng hợp, nhân viên nhập kho.

**Màn hình:** `Nhập kho -> Tạo phiếu nhập`.

**Thực hiện:**

1. Chọn đúng kho, nguồn giao/đối tác và ngày chứng từ.
2. Nhập số chứng từ gốc duy nhất trong phạm vi nghiệp vụ.
3. Khai báo lịch xe đến, cửa nhận, đơn vị vận chuyển, phương tiện và liên hệ nếu luồng nhận yêu cầu.
4. Thêm dòng vật tư, đơn vị nhập, số lượng, lô, ngày sản xuất, hạn dùng và vị trí cất hàng.
5. Nếu để hệ thống đề xuất vị trí, kiểm tra vị trí hiển thị đã thuộc đúng kho và không chứa vật tư/chủ hàng xung đột.
6. Lưu tạm rồi mở lại để đối chiếu toàn bộ thông tin đầu phiếu và dòng hàng.

**Kết quả đúng:** phiếu ở trạng thái nháp/chờ duyệt; tồn kho chưa tăng; mỗi dòng giữ đúng đơn vị, lô và vị trí đã chọn.

**Dừng và báo:** ngày hết hạn trước ngày sản xuất, số lượng không dương, vật tư chỉ hiện dấu gạch, vị trí gợi ý không được lưu hoặc lưu lại làm đổi vật tư.

## Bài Thực Hành 4: Đọc Chứng Từ Bằng AI Và Chống Trùng

**Mục tiêu:** dùng AI để gợi ý dữ liệu, không để AI tự quyết định chứng từ.

**Vai trò:** người được quyền tạo phiếu nhập/xuất.

**Thực hiện:**

1. Chỉ tải tệp được phép; kiểm tra tên, loại và dung lượng trước khi gửi.
2. Khi tải nhiều ảnh, xem từng chứng từ ứng viên riêng. Không cộng dồn hai số chứng từ khác nhau vào một phiếu.
3. Chọn đúng một chứng từ có số, ngày, đối tác và dòng hàng phù hợp.
4. Kiểm tra các dòng AI đã khớp mã; dòng không chắc chắn phải được người dùng sửa hoặc bỏ.
5. Áp dụng vào các trường còn trống hoặc xác nhận rõ trước khi thay dữ liệu đang nhập.
6. Nếu nhà cung cấp AI trả giới hạn, timeout hoặc nội dung không hợp lệ, dùng fallback/manual; không bấm gửi hàng loạt.

**Kết quả đúng:** dữ liệu được gợi ý có nguồn tệp, không tạo dòng trùng, không tự cộng tồn và vẫn cần người dùng kiểm tra trước khi lưu.

**Dừng và báo:** hai file bị gộp sai số chứng từ, radio chọn chứng từ rỗng vẫn áp dụng được, tệp HTML giả ảnh được chấp nhận hoặc công thức nguy hiểm xuất hiện trong file Excel/CSV.

## Bài Thực Hành 5: Gửi Duyệt Và Duyệt Phiếu Nhập

**Mục tiêu:** chứng minh trạng thái và nguyên tắc người lập khác người duyệt khi chính sách yêu cầu.

**Vai trò:** người lập phiếu; Admin/Quản lý có quyền duyệt.

**Thực hiện:**

1. Người lập kiểm tra lại thông tin đầu phiếu, dòng hàng, vị trí, lô/hạn dùng và gửi duyệt.
2. Mở `Nhập kho -> Duyệt phiếu nhập` bằng tài khoản được phép.
3. Người duyệt kiểm tra số chứng từ, nhà cung cấp, số lượng, đơn vị và điều kiện nhận.
4. Nếu người duyệt trùng người lập và tách nhiệm vụ được bật, hệ thống phải từ chối; dùng một tài khoản khác có quyền, không đổi người tạo trong database.
5. Duyệt một lần và tải lại trang để xác nhận trạng thái.

**Kết quả đúng:** phiếu chuyển sang đã duyệt/sẵn sàng nhận; tồn vẫn chưa tăng; thao tác lặp không duyệt hai lần.

**Dừng và báo:** hệ thống báo từ chối nhưng trạng thái vẫn đổi, phát sinh tồn trước khi nhận hoặc cùng một request tạo hai audit event trái nghĩa.

## Bài Thực Hành 6: Lịch Nhận Hàng Và Bắt Đầu Nhận

**Mục tiêu:** chuyển phiếu đã duyệt sang nhận hàng với lịch xe/cửa nhận hợp lệ.

**Vai trò:** quản lý kho, nhân viên nhập kho.

**Màn hình:** `Nhập kho -> Tiếp nhận hàng`, `Quét nhận hàng`.

**Thực hiện:**

1. Lọc đúng kho và phiếu đã duyệt.
2. Kiểm tra mã lịch nhận, xe, cửa, giờ dự kiến và khung giờ.
3. Nếu thiếu lịch xe, mở chi tiết phiếu để bổ sung; không để nút quay mãi trong khi thao tác đã vào hàng đợi lỗi.
4. Bấm bắt đầu nhận một lần.
5. Nếu mạng yếu, mở `Hàng đợi quét`; xem thông báo và chỉ gửi lại sau khi dữ liệu bắt buộc đã đủ.

**Kết quả đúng:** phiếu chuyển sang đang nhận hàng; người nhận và thời điểm được ghi nhận; không cộng tồn ở bước bắt đầu nhận.

**Dừng và báo:** nút tiếp tục quay sau khi request kết thúc, thao tác lỗi vẫn đổi trạng thái hoặc hàng đợi gửi lặp cùng một lệnh.

## Bài Thực Hành 7: Ghi Số Lượng Thực Nhận, Lô, Cân Và Số Sê-ri

**Mục tiêu:** phân biệt số lượng thực nhận với dữ liệu theo dõi từng đơn vị.

**Vai trò:** nhân viên nhập kho, nhân viên kho tổng hợp.

**Thực hiện:**

1. Mở chi tiết phiếu đang nhận và nhập số lượng thực nhận cho từng dòng.
2. Nếu có chênh lệch, chọn lý do và ghi chú cụ thể.
3. Với hàng theo lô/HSD, kiểm tra lô, ngày sản xuất và hạn dùng.
4. Với hàng cân thực tế, ghi trọng lượng và đối chiếu dung sai.
5. Với hàng quản lý số sê-ri, mở `Nhận số sê-ri` và quét/nhập từng số duy nhất.
6. Đối chiếu bộ đếm: số lượng thực nhận 100 không đồng nghĩa đã có 100 số sê-ri. Chỉ hoàn tất khi bộ đếm số sê-ri cũng đủ 100/100.

**Kết quả đúng:** số lượng, lô, cân và số sê-ri được lưu riêng nhưng liên kết đúng cùng dòng phiếu. Thông báo phải nêu số đã ghi và số còn thiếu bằng ngôn ngữ dễ hiểu.

**Dừng và báo:** xác nhận số lượng tự sinh số sê-ri giả, số sê-ri trùng được chấp nhận, bộ đếm không đổi sau khi lưu thành công hoặc số thập phân được dùng cho hàng theo sê-ri.

## Bài Thực Hành 8: Kiểm Tra Chất Lượng

**Mục tiêu:** ghi kết quả kiểm phẩm đúng mã hàng và hướng xử lý.

**Vai trò:** quản lý kho hoặc nhân viên nhập kho được giao kiểm.

**Màn hình:** `Nhập kho -> Kiểm tra chất lượng`.

**Thực hiện:**

1. Chọn phiếu và dòng hàng; danh sách phải hiện mã lẫn tên, không chỉ dấu gạch.
2. Nhập số lượng mẫu, số đạt và số lỗi; tổng đạt/lỗi không vượt mẫu kiểm.
3. Chọn hướng xử lý: chấp nhận, giữ chất lượng, cách ly, trả hoặc xử lý theo quy trình cấu hình.
4. Mô tả khuyết tật bằng thông tin có thể xác minh.
5. Lưu kết quả và mở lại phiếu để kiểm tra người kiểm, thời gian và trạng thái.

**Kết quả đúng:** hàng đạt và hàng bị giữ/cách ly không bị trộn; kết quả kiểm gắn đúng dòng vật tư/chủ hàng/lô.

**Dừng và báo:** dropdown chỉ có dấu gạch, chọn một dòng nhưng lưu sang dòng khác, hoặc hàng chưa đạt vẫn vào tồn khả dụng.

## Bài Thực Hành 9: Chọn Vị Trí Cất Hàng

**Mục tiêu:** cất hàng đúng vị trí mà không phụ thuộc việc phải bấm lại nút gợi ý.

**Vai trò:** nhân viên nhập kho, quản lý kho.

**Thực hiện:**

1. Kiểm tra vị trí đã hiển thị trên dòng phiếu sau khi lưu.
2. Nếu vị trí trống và cùng kho, có thể giữ nguyên; không bắt buộc bấm lại `Gợi ý vị trí cất hàng`.
3. Nếu vị trí đang có tồn, chỉ dùng khi chính sách cho phép và khóa vật tư/chủ hàng hiện có tương thích.
4. Dùng nút gợi ý khi cần tìm vị trí khác, sau đó xác nhận lựa chọn đã được lưu.
5. Kiểm tra sức chứa, trạng thái giữ hàng và khu vực nhận/cất.

**Kết quả đúng:** cùng một vị trí hợp lệ cho kết quả giống nhau dù được chọn thủ công hay từ gợi ý. Validation chạy trên dữ liệu đã lưu, không dựa vào cờ tạm của trình duyệt.

**Dừng và báo:** cùng vị trí nhưng chỉ qua khi bấm gợi ý, hệ thống ghi hàng vào vị trí chứa mã khác, hoặc lỗi xuất hiện sau khi tồn đã tăng.

## Bài Thực Hành 10: Hoàn Tất Nhập Và Đối Chiếu Tồn

**Mục tiêu:** xác nhận điểm duy nhất làm tăng tồn nhập.

**Vai trò:** người có quyền hoàn tất nhập; tuân thủ tách nhiệm vụ.

**Thực hiện:**

1. Kiểm tra số lượng thực nhận, lô/HSD, số sê-ri, cân, chất lượng và vị trí.
2. Đọc hộp xác nhận tổng số lượng chứng từ và số lượng tăng tồn.
3. Nếu người thao tác không được phép vì trùng vai trò trước đó, chuyển cho người khác có quyền.
4. Xác nhận một lần; chờ phản hồi hoàn tất.
5. Mở `Xem tồn kho` và `Lịch sử nhập xuất`; đối chiếu mã phiếu, số lượng, vị trí, lô và thời gian.
6. Tải lại chi tiết phiếu; nút hoàn tất không được tạo thêm giao dịch.

**Kết quả đúng:** phiếu hoàn tất, tồn tăng đúng một lần, số sê-ri khả dụng đúng vị trí và ledger có khóa chống lặp.

**Dừng và báo:** toast báo lỗi nhưng tồn tăng, phiếu hoàn tất mà ledger thiếu, hoặc bấm lại làm tăng tồn lần hai.

## Bài Thực Hành 11: Tạo Phiếu Xuất

**Mục tiêu:** tạo nhu cầu xuất nhưng chưa giảm tồn vật lý.

**Vai trò:** quản lý kho, nhân viên kho tổng hợp, nhân viên xuất kho nếu được cấp quyền tạo.

**Thực hiện:**

1. Chọn kho, nơi nhận/đối tác, ngày và số chứng từ.
2. Thêm vật tư, đơn vị, số lượng và yêu cầu lô/HSD nếu có.
3. Kiểm tra tồn hiện tại, tồn giữ chỗ và tồn khả dụng theo đúng chủ hàng.
4. Lưu phiếu và xác nhận không có giao dịch giảm tồn.
5. Nếu vật tư theo số sê-ri, chuẩn bị danh sách sê-ri khả dụng nhưng chưa tự chọn ngoài workflow.

**Kết quả đúng:** phiếu ở trạng thái ban đầu; tồn vật lý không đổi; không cho số lượng âm hoặc vượt quyền kho/chủ hàng.

**Dừng và báo:** tạo phiếu đã giảm tồn, đọc tồn của chủ hàng khác hoặc cùng số chứng từ tạo hai phiếu khi bấm lặp.

## Bài Thực Hành 12: Phát Hành, Giữ Chỗ Và Đợt Gom Đơn

**Mục tiêu:** biến nhu cầu xuất thành giữ chỗ và nhiệm vụ lấy hàng an toàn.

**Vai trò:** Admin/Quản lý kho; nhân viên xuất thực hiện nhiệm vụ đã giao.

**Thực hiện:**

1. Phát hành phiếu hoặc đưa vào đợt gom đơn theo quy trình.
2. Kiểm tra hệ thống chọn tồn theo FEFO/lô, hạn dùng, trạng thái giữ và chủ hàng.
3. Đối chiếu `Tồn hiện tại = Tồn khả dụng + Đã giữ chỗ` trong cùng phạm vi.
4. Kiểm tra nhiệm vụ lấy có vị trí nguồn, vật tư, số lượng và người được giao.
5. Thử trường hợp thiếu tồn trong dữ liệu test cô lập; toàn bộ wave không cho partial phải rollback, không để giữ chỗ dở dang.

**Kết quả đúng:** giữ chỗ không vượt tồn, không trùng allocation và không làm giảm tồn vật lý trước khi ghi sổ xuất.

**Dừng và báo:** hai nhiệm vụ cùng giữ một lượng, giữ chỗ âm, chủ hàng bị trộn hoặc thất bại vẫn để lại reservation.

## Bài Thực Hành 13: Lấy Hàng Và Quét Lấy Hàng

**Mục tiêu:** lấy đúng hàng, đúng vị trí và đúng số sê-ri.

**Vai trò:** nhân viên xuất kho, nhân viên kho tổng hợp.

**Màn hình:** `Xuất kho -> Nhiệm vụ lấy hàng`, `Quét lấy hàng`, `Nhiệm vụ tiếp theo`.

**Thực hiện:**

1. Mở nhiệm vụ được giao, kiểm tra phiếu, vị trí nguồn và số lượng.
2. Quét vị trí trước, sau đó mã hàng/mã kiện/số sê-ri theo hướng dẫn trên màn.
3. Nhập số lượng lấy không vượt số còn lại.
4. Với số sê-ri, chỉ quét sê-ri đã được giữ cho nhiệm vụ.
5. Hoàn tất nhiệm vụ và tải lại danh sách.

**Kết quả đúng:** nhiệm vụ phản ánh số đã lấy, người lấy và thời gian; tồn vật lý chỉ giảm ở bước ghi sổ theo thiết kế, reservation được tiêu thụ đúng.

**Dừng và báo:** quét sai vẫn được nhận, một sê-ri dùng cho hai nhiệm vụ, nút xác nhận gửi lặp hoặc trạng thái hoàn tất nhưng số đã lấy bằng 0.

## Bài Thực Hành 14: Xử Lý Lấy Thiếu Và Ghi Sổ Xuất

**Mục tiêu:** chốt phần thực tế mà không che giấu thiếu hàng và không vượt tách nhiệm vụ.

**Vai trò:** nhân viên lấy báo thiếu; người có quyền ghi sổ xuất thực hiện chốt.

**Thực hiện:**

1. Nếu không đủ hàng tại vị trí, dùng `Báo thiếu` và nhập số lượng/lý do.
2. Quản lý quyết định phân bổ lại, chốt phần đã lấy hoặc chốt và hủy phần còn lại.
3. Kiểm tra người lập phiếu. Nếu người đang đăng nhập là người lập và SoD chặn ghi sổ, chuyển cho người khác có quyền.
4. Xác nhận chốt một lần.
5. Đối chiếu tồn, reservation, số thực xuất và ledger.

**Kết quả đúng:** khi bị chặn SoD, trạng thái và tồn không đổi. Khi chốt hợp lệ, tồn giảm đúng phần đã ghi sổ, phần hủy giải phóng giữ chỗ và không còn nhiệm vụ mồ côi.

**Dừng và báo:** hệ thống hiển thị lỗi nhưng vẫn hoàn thành, trạng thái đã lấy mà tồn/reservation không khớp, hoặc cùng người bị cấm vẫn ghi sổ thành công.

## Bài Thực Hành 15: Đóng Gói, Mã Kiện Và Nhãn

**Mục tiêu:** tạo kiện xuất có nội dung, cân và nhãn truy vết được.

**Vai trò:** nhân viên xuất kho hoặc vận chuyển được phân quyền.

**Thực hiện:**

1. Mở `Đóng gói & giao` và chọn phiếu đã sẵn sàng.
2. Tạo kiện, gắn đúng dòng hàng/số sê-ri và số lượng.
3. Ghi cân thực tế nếu bắt buộc.
4. Chọn mẫu nhãn đúng chủ hàng/quy cách và gửi một job in.
5. Xác nhận trạng thái hàng đợi in; việc gửi request không đồng nghĩa đã in thành công.
6. Dùng tra cứu mã kiện để đối chiếu nội dung.

**Kết quả đúng:** kiện không vượt số đã lấy, số sê-ri không nằm hai kiện, nhãn/barcode trỏ đúng kiện và lỗi in không làm đổi tồn.

**Dừng và báo:** job in lỗi bị đánh dấu hoàn thành, kiện rỗng vẫn giao được hoặc cân bắt buộc bị bỏ qua.

## Bài Thực Hành 16: Điều Phối Vận Chuyển Và Đối Soát Giao Hàng

**Mục tiêu:** xếp kiện lên đúng chuyến, bàn giao và đối soát trạng thái giao.

**Vai trò:** nhân viên vận chuyển, quản lý kho.

**Màn hình:** `Vận chuyển -> Điều phối vận chuyển`, `Bảng chuyến xe`, `Đối soát giao hàng`.

**Thực hiện:**

1. Tạo/chọn chuyến trong đúng kho, khai báo đơn vị vận chuyển, xe, tuyến và giờ dự kiến.
2. Chỉ xếp phiếu/kiện đã đủ điều kiện và chưa thuộc chuyến hoạt động khác.
3. Quét kiện thực tế, so sánh số kiện và số lượng.
4. Chuyển trạng thái theo thứ tự: kế hoạch, gom khu chờ, đang xếp, đã xếp, rời kho, đóng chuyến.
5. Ghi nhận vận đơn và sự kiện giao; retry phải chống gửi trùng.
6. Mở đối soát để xử lý thiếu kiện, thất bại giao hoặc lệch trạng thái.

**Kết quả đúng:** CSV/chứng từ xuất dùng tiêu đề và trạng thái tiếng Việt; mỗi kiện chỉ thuộc một chuyến hoạt động; trạng thái giao có nguồn và thời gian.

**Dừng và báo:** đóng chuyến trước khi rời kho, một kiện nằm hai chuyến hoặc gọi lại hãng vận chuyển tạo hai vận đơn.

## Bài Thực Hành 17: Tra Cứu Mã Kiện Và Số Sê-ri

**Mục tiêu:** truy vết một đơn vị hàng từ hiện tại về giao dịch nguồn.

**Vai trò:** quản lý, nhân viên tồn kho/kiểm kê, người chỉ xem theo quyền.

**Thực hiện:**

1. Mở `Tồn kho -> Tra cứu mã kiện` và nhập mã đầy đủ.
2. Kiểm tra kho, vị trí, chủ hàng, trạng thái và nội dung kiện.
3. Mở `Tra cứu số sê-ri`; kiểm tra vật tư, vị trí, kiện, trạng thái giữ và tham chiếu phiếu.
4. Đối chiếu lịch sử di chuyển/nhận/xuất với chi tiết phiếu.
5. Thử một mã không tồn tại; hệ thống phải trả trạng thái rỗng dễ hiểu, không lỗi 500.

**Kết quả đúng:** một mã trả về một chuỗi truy vết nhất quán và không lộ dữ liệu ngoài scope.

**Dừng và báo:** cùng số sê-ri hiện ở hai vị trí khả dụng, kiện cha/con tạo vòng lặp hoặc viewer xem được chủ hàng ngoài phạm vi.

## Bài Thực Hành 18: Di Chuyển Tồn Và Bổ Sung Hàng

**Mục tiêu:** chuyển tồn giữa vị trí bằng nhiệm vụ và ledger cân bằng.

**Vai trò:** nhân viên tồn kho/kiểm kê, quản lý kho.

**Thực hiện:**

1. Tạo/chọn nhiệm vụ di chuyển có nguồn, đích và số lượng.
2. Kiểm tra tồn khả dụng nguồn, sức chứa đích và chính sách một vị trí/một khóa vật tư-chủ hàng.
3. Quét nguồn, vật tư/mã kiện và đích theo đúng thứ tự.
4. Hoàn tất một lần; đối chiếu nguồn giảm, đích tăng cùng số lượng.
5. Với bổ sung khu lấy hàng, xác nhận đích là pick-face phù hợp và reservation hiện tại không bị phá.

**Kết quả đúng:** tổng tồn kho không đổi, chỉ vị trí đổi; giao dịch nguồn/đích cùng nhóm và rollback toàn bộ nếu đích xung đột.

**Dừng và báo:** nguồn giảm nhưng đích không tăng, vị trí chứa mã khác vẫn nhận hoặc thất bại để lại nhiệm vụ hoàn tất giả.

## Bài Thực Hành 19: Kiểm Kê Kín, Đếm Lại Và Duyệt Chênh Lệch

**Mục tiêu:** kiểm kê độc lập và chỉ điều chỉnh sau phê duyệt.

**Vai trò:** nhân viên tồn kho đếm; quản lý duyệt.

**Màn hình:** `Tồn kho -> Kiểm kê`.

**Thực hiện:**

1. Tạo phiếu kiểm kê theo kho/khu/vị trí/vật tư/lô; tránh trùng phiếu đang hoạt động.
2. Khi chính sách yêu cầu đếm kín, nhân viên không xem số hệ thống trước khi nhập số đếm.
3. Nhập số đếm và bằng chứng; gửi duyệt.
4. Nếu vượt ngưỡng, quản lý yêu cầu đếm lại hoặc điều tra nguyên nhân.
5. Quản lý khác người đếm duyệt chênh lệch theo policy.
6. Chỉ sau phê duyệt, workflow điều chỉnh mới làm đổi tồn và ghi ledger.

**Kết quả đúng:** lưu đủ lần đếm, người đếm, người duyệt, số trước/sau và lý do. Từ chối hoặc đếm lại không tự điều chỉnh tồn.

**Dừng và báo:** người đếm thấy số hệ thống trong blind count, cùng người tự duyệt khi policy cấm hoặc chênh lệch chưa duyệt đã đổi tồn.

## Bài Thực Hành 20: Kiểm Kê Thông Minh Và Đề Xuất Từ AI

**Mục tiêu:** ưu tiên kiểm kê theo rủi ro nhưng giữ con người quyết định.

**Vai trò:** quản lý kho, nhân viên tồn kho/kiểm kê; Admin giám sát.

**Màn hình:** `Tồn kho -> Kiểm kê thông minh`.

**Thực hiện:**

1. Chọn kho, chủ hàng, khu vực và mức rủi ro; đọc thời điểm dữ liệu.
2. Mở dòng rủi ro, xem reason code, feature, giao dịch nguồn và trạng thái chất lượng dữ liệu.
3. Không diễn giải điểm rủi ro thành xác suất nếu màn không công bố calibration.
4. Quản lý duyệt, sửa hoặc từ chối đề xuất kèm lý do.
5. Tạo phiếu kiểm kê chỉ từ đề xuất đã duyệt và chưa hết hạn.
6. Thực hiện đếm theo Bài 19; kết quả cuối quay lại làm outcome, không ghi đè prediction cũ.
7. Khi model/rule thiếu dữ liệu, dùng fallback ABC/lịch định kỳ.

**Kết quả đúng:** proposal có phiên bản, cutoff, scope, người duyệt và phiếu liên kết. AI không tự ghi sổ, tự điều chỉnh hoặc bỏ qua scope.

**Dừng và báo:** đề xuất stale vẫn tạo phiếu, dữ liệu lỗi vẫn được duyệt, AI tự thay tồn hoặc danh sách trộn chủ hàng.

## Bài Thực Hành 21: Điều Chỉnh Tồn, Chốt Tồn Và Khóa Kỳ

**Mục tiêu:** xử lý sai lệch có kiểm soát và bảo vệ kỳ đã đóng.

**Vai trò:** quản lý kho/Admin theo quyền.

**Thực hiện:**

1. Chỉ tạo điều chỉnh khi đã xác định nguyên nhân; không dùng để che lỗi phiếu/số sê-ri/mã kiện.
2. Nhập lý do, tham chiếu và bằng chứng.
3. Duyệt theo tách nhiệm vụ; kiểm tra ledger trước/sau.
4. Dùng `Hệ thống -> Chốt tồn` để tạo ảnh chụp theo quy trình.
5. Dùng `Khóa kỳ` sau khi đối soát. Thử giao dịch ngày thuộc kỳ khóa trong dữ liệu test: hệ thống phải từ chối và giữ nguyên dữ liệu.
6. Nếu cần mở kỳ, thực hiện qua quyền và audit; không đổi ngày để lách khóa.

**Kết quả đúng:** adjustment có nguồn/lý do/người duyệt; kỳ khóa ngăn mọi mutation áp dụng; chốt tồn không thay thế ledger.

**Dừng và báo:** giao dịch bị từ chối nhưng tồn đổi, lock chỉ chặn một route hoặc snapshot tự sửa sai lệch vượt dung sai mà không mở cảnh báo.

## Bài Thực Hành 22: Trang Chính, Tổng Quan Kho Và Thống Kê

**Mục tiêu:** đọc KPI đúng công thức, thời điểm và phạm vi; drill-down về dữ liệu nguồn.

**Vai trò:** quản lý, nhân viên báo cáo, Admin; viewer theo phạm vi.

**Thực hiện:**

1. Mở Trang chính; đọc thời điểm dữ liệu, kho/chủ hàng và công việc cần xử lý.
2. Mở `Báo cáo -> Tổng quan kho`, chọn Từ ngày, Đến ngày và kho.
3. Đối chiếu tồn hiện tại, đã giữ chỗ, khả dụng và chênh lệch nhập-xuất.
4. Mở `Thống kê nhập/xuất`; đối chiếu mã phiếu, lô/HSD và vị trí với lịch sử.
5. Với KPI, đọc đúng mẫu số và tên gọi. `Tỷ lệ đáp ứng giữ chỗ` không được gọi thành fill rate đơn hàng nếu chưa có dữ liệu tương ứng.
6. Xuất Excel/CSV; kiểm tra tiêu đề tiếng Việt, dữ liệu theo scope và ô bắt đầu bằng công thức đã được làm an toàn.

**Kết quả đúng:** số tổng hợp bằng chi tiết tại cùng bộ lọc/snapshot; không có `null`, `undefined`, `NaN` hoặc tên trạng thái tiếng Anh trên giao diện người dùng.

**Dừng và báo:** KPI không nêu thời điểm, tổng không reconcile, filter bị mất khi refresh hoặc export chứa dữ liệu ngoài scope.

## Bài Thực Hành 23: Bất Thường Dữ Liệu Và Nhật Ký

**Mục tiêu:** xử lý nguyên nhân thay vì đóng cảnh báo cho sạch danh sách.

**Vai trò:** quản lý; Admin xem giám sát/nhật ký; báo cáo chỉ đọc theo quyền.

**Thực hiện:**

1. Mở `Báo cáo -> Bất thường dữ liệu`; lọc kho, mức độ và trạng thái.
2. Mở tham chiếu nguồn: phiếu, vị trí, giao dịch, chuyến hoặc tích hợp.
3. Phân công người xử lý và ghi nhận tiến độ.
4. Sửa dữ liệu bằng workflow gốc; không update trực tiếp DB.
5. Chạy lại kiểm tra/đối soát và chỉ đóng khi điều kiện lỗi không còn.
6. Admin dùng nhật ký/request ID để truy vết nếu thông báo chung chung.

**Kết quả đúng:** ngoại lệ có người phụ trách, nguyên nhân, bằng chứng và trạng thái; lỗi tái xuất hiện sẽ được mở lại thay vì bị che.

**Dừng và báo:** đóng cảnh báo làm thay tồn, audit có thể bị người vận hành sửa/xóa hoặc thông báo chứa secret/connection string.

## Bài Thực Hành 24: Quét Trên Điện Thoại Và Hàng Đợi Khi Mạng Yếu

**Mục tiêu:** thao tác RF an toàn trên màn nhỏ và phục hồi sau mất mạng.

**Vai trò:** nhân viên nhập, xuất, tồn/kiểm kê hoặc vận chuyển theo menu được cấp.

**Thực hiện:**

1. Dùng đúng màn quét cho nghiệp vụ; cấp camera khi cần.
2. Kiểm tra ô đang nhận dữ liệu trước khi quét.
3. Quét từng bước và chờ phản hồi âm thanh/màu/chữ; không quét tiếp khi bước trước lỗi.
4. Khi mất mạng, thao tác chờ phải hiện trong `Hàng đợi quét` với loại, tham chiếu và lỗi.
5. Sau khi có mạng, tải trạng thái mới; chỉ gửi lại thao tác còn hợp lệ.
6. Bỏ thao tác đã lỗi nghiệp vụ hoặc đã được xử lý ở thiết bị khác.

**Kết quả đúng:** widget không che nút, không gửi trùng, không giữ spinner vô hạn; session hết hạn sẽ yêu cầu đăng nhập lại thay vì gửi dữ liệu âm thầm.

**Dừng và báo:** hàng đợi chứa mật khẩu/token, retry vượt quyền, layout bị tràn/che ở viewport hỗ trợ hoặc hai thiết bị cùng hoàn tất một nhiệm vụ.

## Bài Thực Hành 25: Nạp Dữ Liệu Demo An Toàn Và Bàn Giao Cuối Ca

**Mục tiêu:** chuẩn bị buổi demo và kết thúc ca mà không phá dữ liệu hiện có.

**Vai trò:** Admin nạp demo; quản lý/nhân viên bàn giao ca.

**Thực hiện demo:**

1. Xác nhận đúng môi trường và database hosting đã chuẩn bị cho demo.
2. Dừng mọi giao dịch demo đang chạy; không nạp lại khi có phiếu/nhiệm vụ đã hoàn tất cần giữ.
3. Mở `Hệ thống -> Dữ liệu mẫu`, đọc phạm vi cleanup và chọn đúng bối cảnh.
4. Nếu hệ thống từ chối vì có giao dịch đã hoàn tất, không xóa trực tiếp DB; dùng bộ dữ liệu hiện có hoặc chuẩn bị lại ở môi trường được phép.
5. Sau nạp, kiểm tra kho, vật tư, phiếu, tồn theo vị trí và tài khoản demo.

**Thực hiện cuối ca:**

1. Mở Trang chính và xem công việc quá hạn.
2. Kiểm tra phiếu nhập đang nhận, số sê-ri còn thiếu, nhiệm vụ lấy/di chuyển đang mở và chuyến chưa đóng.
3. Kiểm tra bất thường dữ liệu, đối soát giao hàng và lịch sử nhập xuất.
4. Ghi bàn giao bằng mã phiếu/nhiệm vụ/chuyến, người phụ trách và bước tiếp theo.

**Kết quả đúng:** demo dùng dữ liệu có thể giải thích, không cần SQL sửa tay; ca sau nhận danh sách việc mở rõ ràng.

**Dừng và báo:** nạp demo có nguy cơ xóa dữ liệu thật, ứng dụng đang giữ file log làm lỗi đóng gói ZIP, hoặc background worker có thể đổi dữ liệu trong lúc trình diễn.

## Checklist Cuối Ca

- [ ] Đúng tài khoản, vai trò, kho và chủ hàng.
- [ ] Không còn phiếu nhập đang nhận dở mà không có người phụ trách.
- [ ] Không còn dòng hàng thiếu lô, hạn dùng, cân hoặc số sê-ri.
- [ ] Nhiệm vụ lấy hàng, di chuyển và bổ sung hàng có trạng thái thực tế.
- [ ] Không còn reservation vượt tồn hoặc giữ chỗ mồ côi.
- [ ] Kiện xuất, vận đơn và chuyến xe được đối soát.
- [ ] Ngoại lệ mở có người phụ trách và ghi chú bước tiếp theo.
- [ ] Tồn, giữ chỗ, khả dụng và ledger được đối chiếu cho giao dịch quan trọng trong ca.
- [ ] Không có thao tác chờ/lỗi trong Hàng đợi quét bị bỏ quên.
- [ ] Không ghi secret, mật khẩu, API key hoặc connection string vào ảnh/log bàn giao.

## Checklist Rà Soát Lỗi Thường Gặp

- [ ] Nút loading luôn được mở lại sau validation hoặc lỗi mạng.
- [ ] Bấm lặp không tạo phiếu, reservation, ledger, vận đơn hoặc job in trùng.
- [ ] Lỗi tách nhiệm vụ không làm đổi trạng thái/tồn.
- [ ] Lỗi kỳ khóa không làm đổi dữ liệu.
- [ ] Xác nhận số lượng không được coi là đã đăng ký số sê-ri.
- [ ] Vị trí cất hàng hợp lệ hoạt động như nhau dù chọn thủ công hay qua gợi ý.
- [ ] Vị trí xung đột bị chặn trước mutation và rollback toàn bộ.
- [ ] Xuất thiếu/partial giải phóng phần giữ chỗ đúng.
- [ ] Trạng thái hiển thị bằng tiếng Việt; mã kỹ thuật chỉ đi kèm giải thích dễ hiểu.
- [ ] Không có `null`, `undefined`, `NaN`, dấu gạch vô nghĩa hoặc chữ lỗi mã hóa.
- [ ] Menu active chỉ mở đúng nhóm hiện tại.
- [ ] Modal, toast, bảng và widget không che nhau ở desktop, laptop, tablet và mobile hỗ trợ.
- [ ] Export không lộ dữ liệu ngoài phạm vi và chống công thức bảng tính.
- [ ] AI chỉ đề xuất, có thời điểm/nguồn/version và luôn có human approval/fallback.
- [ ] Khi không đủ bằng chứng thiết bị, UAT hoặc dữ liệu, ghi `BLOCKED`; không tự công bố 100%.

## Quy Tắc Hỗ Trợ Và Báo Lỗi

Khi cần hỗ trợ, cung cấp mã phiếu/nhiệm vụ/chuyến, thời điểm, vai trò, kho, bước thao tác, nội dung thông báo và ảnh chụp đã kiểm tra không chứa bí mật. Không gửi mật khẩu, cookie, token, API key hoặc connection string. Nếu lỗi có thể làm sai tồn, dừng riêng workflow đó, giữ nguyên dữ liệu và chuyển quản lý/Admin đối soát trước khi tiếp tục.
