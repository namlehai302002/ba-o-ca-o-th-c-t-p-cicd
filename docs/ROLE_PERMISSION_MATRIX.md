# WMS Pro - Ma Trận Vai Trò, Quyền Và Route

Ngày đối chiếu runtime: 11/07/2026  
Nguồn chuẩn: `Models/WmsRoles.cs`, `Models/AuthorizationModels.cs`, `Services/RbacSeedService.cs`, authorization attributes và scope guard trong controller/service.

Tài liệu này mô tả implementation hiện tại. Nó không tự thay đổi role, permission, route, database hoặc tài khoản.

## 1. Nguyên Tắc Phân Quyền

- `Admin` được cấp toàn bộ permission và `PermissionHandler` cho phép role Admin vượt qua kiểm tra permission policy. Business state, kỳ khóa, transaction guard và validation vẫn có hiệu lực.
- Role và permission là hai lớp độc lập: route có cả `Roles` và `Policy` phải thỏa cả hai.
- Menu ẩn không phải biện pháp bảo mật. Truy cập URL/API trực tiếp vẫn phải bị controller/policy/scope từ chối.
- Mọi dữ liệu kho nhiều chủ hàng phải qua **warehouse scope** và **owner scope** ở query/read/export/mutation.
- Các thao tác maker/checker áp dụng **Segregation Of Duties**; người tạo không tự thực hiện bước kiểm soát được định nghĩa.
- `Staff` là vai trò tổng hợp cũ. Các role chuyên môn là lựa chọn mặc định để giảm quyền theo nguyên tắc least privilege.
- `ReportViewer` và `Viewer` không được tạo, post, hủy hoặc thay đổi tồn kho.

## 2. Danh Mục Vai Trò

| Role | Nhãn | Mục đích |
|---|---|---|
| `Admin` | Quản trị viên | Toàn quyền hệ thống, người dùng, phân quyền, bảo mật và cấu hình trọng yếu. |
| `Manager` | Quản lý kho | Điều phối vận hành, duyệt nghiệp vụ, xử lý ngoại lệ và xem báo cáo quản trị. |
| `Staff` | Nhân viên kho tổng hợp | Vai trò cũ cho nhân viên kiêm nhiệm nhiều nghiệp vụ kho. |
| `InboundStaff` | Nhân viên nhập kho | Tạo, tiếp nhận, quét nhận, kiểm hàng và theo dõi lịch sử nhập. |
| `OutboundStaff` | Nhân viên xuất kho | Lấy hàng, quét lấy hàng, đóng gói và bàn giao nội bộ. |
| `InventoryStaff` | Nhân viên tồn kho/kiểm kê | Tồn kho, mã kiện, sê-ri, kiểm kê, điều chỉnh và di chuyển nội bộ. |
| `TransportStaff` | Nhân viên vận chuyển | Giao hàng, chuyến xe, nhãn/chứng từ và đối soát vận chuyển. |
| `ReportViewer` | Nhân viên báo cáo | Dashboard và báo cáo theo permission/scope, không làm đổi tồn. |
| `Viewer` | Chỉ xem | Dữ liệu cơ bản và báo cáo được cấp, không có mutation. |

## 3. Permission Seed Theo Vai Trò

Thứ tự cột là implementation trong `RbacSeedService`. `Có` nghĩa là seed mặc định cấp permission; custom database grant vẫn phải được review riêng.

| Permission | Admin | Manager | Staff | InboundStaff | OutboundStaff | InventoryStaff | TransportStaff | ReportViewer | Viewer |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `voucher.create` | Có | Có | Có | Có | Có | Có | Không | Không | Không |
| `voucher.approve.inbound` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `voucher.cancel` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `voucher.post.outbound` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `voucher.release.picking` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `picktask.reassign` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `voucher.confirm.shipping` | Có | Có | Không | Không | Không | Không | Có | Không | Không |
| `voucher.approve.outbound` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `qc.submit.inspection` | Có | Có | Không | Có | Không | Không | Không | Không | Không |
| `qc.resolve.hold` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `stockcount.approve` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `stockcount.unlock` | Có | Không | Không | Không | Không | Không | Không | Không | Không |
| `master.item.manage` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `master.partner.manage` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `master.category.manage` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `master.uom.manage` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `warehouse.config.manage` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `report.view` | Có | Có | Có | Có | Có | Có | Có | Có | Có |
| `report.view.financial` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `audit.view` | Có | Không | Không | Không | Không | Không | Không | Không | Không |
| `user.manage` | Có | Không | Không | Không | Không | Không | Không | Không | Không |
| `system.danger.ops` | Có | Không | Không | Không | Không | Không | Không | Không | Không |
| `tenant.scope.manage` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `billing.3pl.manage` | Có | Có | Không | Không | Không | Không | Không | Không | Không |
| `mhe.manage` | Có | Có | Không | Không | Không | Không | Không | Không | Không |

## 4. Nhóm Role Dùng Trong Runtime

| Hằng số | Thành viên |
|---|---|
| `InboundRoles` | `Admin,Manager,Staff,InboundStaff` |
| `OutboundRoles` | `Admin,Manager,Staff,OutboundStaff` |
| `InventoryRoles` | `Admin,Manager,Staff,InventoryStaff` |
| `InventoryReadRoles` | `Admin,Manager,Staff,InventoryStaff,Viewer,ReportViewer` |
| `TransportRoles` | `Admin,Manager,Staff,TransportStaff` |
| `OutboundTransportRoles` | `Admin,Manager,Staff,OutboundStaff,TransportStaff` |
| `ReportRoles` | `Admin,Manager,Viewer,ReportViewer` |
| `ReportManagerRoles` | `Admin,Manager,ReportViewer` |
| `WarehouseReportRoles` | Tất cả role trừ `Viewer` |
| `WarehouseExecutionRoles` | Tất cả role vận hành, không gồm `ReportViewer`/`Viewer` |

## 5. Ma Trận Route Nghiệp Vụ Đại Diện

### 5.1 Ma Trận Menu, Action Và Backend Guard

| Nhóm menu | Role nhìn thấy theo `_SidebarNav.cshtml` | Action vận hành chính | Backend từ chối khi truy cập trực tiếp |
|---|---|---|---|
| Trang chính | Tất cả tài khoản đã xác thực | Xem công việc/KPI theo scope | Global authenticated filter; query tiếp tục áp kho/chủ hàng. |
| Nhập kho | `Admin`, `Manager`, `Staff`, `InboundStaff` | Tạo, nhận, quét nhận, QC; duyệt chỉ Admin/Manager | `InboundRoles`, `voucher.create`, `voucher.approve.inbound`, `qc.*`, scope và SoD. |
| Xuất kho | `Admin`, `Manager`, `Staff`, `OutboundStaff` | Tạo phiếu, lấy hàng, đóng gói; release/post chỉ Admin/Manager | `OutboundRoles`, `voucher.create`, `voucher.release.picking`, `voucher.post.outbound`, scope và SoD. |
| Tồn kho | Nhóm tồn kho; `Viewer` chỉ các màn đọc | Xem tồn, vị trí, mã kiện/sê-ri; kiểm kê/điều chỉnh/di chuyển chỉ nhóm vận hành | `InventoryReadRoles` cho đọc, `InventoryRoles`/permission riêng cho ghi; in nhãn vật tư cũng bị giới hạn `InventoryRoles`. |
| Vận chuyển | `Admin`, `Manager`, `Staff`, `TransportStaff` | Đóng gói/giao, chuyến xe, đối soát, nhãn/chứng từ | `TransportRoles` hoặc `OutboundTransportRoles` và `voucher.confirm.shipping`; cấu hình connector hẹp hơn. |
| Báo cáo | `Admin`, `Manager`, `ReportViewer`, `Viewer`; nội dung con tiếp tục lọc | Báo cáo tồn; KPI/chi phí/quản trị tùy role/permission | `report.view`, `report.view.financial`, `audit.view` và warehouse/owner scope trên query/export. |
| Danh mục | `Admin`, `Manager` | Đối tác, vật tư, ĐVT, kho/vị trí, phân loại và hợp đồng | `master.*.manage`, `warehouse.config.manage`, `billing.3pl.manage`; form create không được gán ID/status/audit field từ client. |
| Hệ thống | `Admin`, `Manager`; người dùng/yêu cầu truy cập chỉ Admin | Người dùng, scope, quy tắc, giám sát, chốt/khóa và cấu hình nâng cao | `user.manage`, `tenant.scope.manage`, `system.danger.ops` hoặc policy nghiệp vụ tương ứng; DangerOps Admin-only. |

Ẩn menu chỉ là UX. Contract test `Gate2SecurityContractTests.UnsafeBusinessActions_ShouldHaveRoleOrPermissionGuardBeyondGlobalAuthentication` phản chiếu toàn bộ unsafe action và yêu cầu role/policy hoặc API-key boundary ở backend.

### 5.2 Route Nghiệp Vụ Đại Diện

Ký hiệu: `R` đọc, `W` thao tác, `D` duyệt/ghi sổ, `-` không được route/policy mặc định cho phép. Mỗi ô vẫn phụ thuộc warehouse/owner scope và trạng thái entity.

| Nhóm route | Admin | Manager | Staff | InboundStaff | OutboundStaff | InventoryStaff | TransportStaff | ReportViewer | Viewer | Guard chính |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| `/`, `/Home` | R | R | R | R | R | R | R | R | R | Dashboard role/action và scope. |
| `/Vouchers/Create` | W | W | W | W nhập | W xuất | W tồn/điều chỉnh | - | - | - | `voucher.create` + `CanAccessVoucherType`. |
| `/Vouchers/SubmitForApproval`, `/Vouchers/ConfirmReceiving` | W | W | W | W | - | - | - | - | - | `InboundRoles` + `voucher.create`. |
| `/Vouchers/ApproveInbound`, `/Vouchers/RejectInbound` | D | D | - | - | - | - | - | - | - | `voucher.approve.inbound`, SoD. |
| `/Operations/UpdateDockMilestone` | W | W | W | W | - | - | - | - | - | `InboundRoles`, `voucher.create`, warehouse/owner scope. |
| `/Vouchers/ReleaseForPicking` (runtime: `ConfirmForPicking`/`ReleaseDirect`) | D | D | - | - | - | - | - | - | - | `voucher.release.picking`, SoD, FEFO. |
| `/Vouchers/ConfirmPickTask`, `/Operations/RfPicking` | W | W | W | - | W | - | - | - | - | Outbound role/task/owner/scan guard. |
| `/Vouchers/PostReservedOutbound` | D | D | - | - | - | - | - | - | - | `voucher.post.outbound`, reservation, SoD. |
| `/Vouchers/ConfirmPacking` | W | W | W | - | W | - | - | - | - | `OutboundRoles`, `voucher.create`, package/LPN guard. |
| Putaway lookup/suggestion routes | W | W | W | W | - | - | - | - | - | `InboundRoles`, `voucher.create`, warehouse/owner scope. |
| Source-location lookup for outbound | W | W | W | - | W | - | - | - | - | `OutboundRoles`, `voucher.create`, warehouse/owner/FEFO scope. |
| OCR/Excel import and catch-weight capture | W | W | Theo loại phiếu | Theo phiếu nhập | Theo phiếu xuất | Theo phiếu tồn | - | - | - | `voucher.create`; catch weight also checks voucher type and scope. |
| `/Vouchers/AssignDock` | D | D | - | - | - | - | - | - | - | `voucher.approve.inbound`, warehouse/owner scope. |
| `/Vouchers/CreateBackorder` | D | D | - | - | - | - | - | - | - | `voucher.approve.outbound`, warehouse/owner scope. |
| `/Vouchers/ConfirmShipping`, shipment-load mutations | W | W | - | - | - | - | W | - | - | Transport role + `voucher.confirm.shipping`, SoD. |
| `/Vouchers/Cancel` | D | D | - | - | - | - | - | - | - | `voucher.cancel`, reversal, kỳ khóa, SoD. |
| `/Operations/RfReceiving` | W | W | W | W | - | - | - | - | - | Inbound role, scan/lot/HSD/owner. |
| `/Operations/MovementTasks`, `/Operations/RfMovement` | W | W | W | - | - | W | - | R tùy route | R tùy route | Inventory role/read role, location scope. |
| Mutation movement/LPN nội bộ | W | W | W | - | - | W | - | - | - | `voucher.create`; gán lại dùng `picktask.reassign`, hủy dùng `voucher.cancel`. |
| `/Reports/StockCount` lưu nháp | W | W | W | - | - | W | - | - | - | `InventoryRoles` + `report.view`. |
| `/Reports/StockCountApproveDraft` | D | D | - | - | - | - | - | - | - | `stockcount.approve`, SoD. |
| `/Reports/StockValuation` | R | R | - | - | - | - | - | - | - | `report.view.financial`. |
| `/Reports/Inventory` và báo cáo vận hành có policy đọc | R | R | R | R | R | R | R | R | R | `report.view`; một số route còn role hẹp hơn. |
| `/Reports/AuditTrail` | R | - | - | - | - | - | - | - | - | `audit.view`. |
| `/Users` | W | - | - | - | - | - | - | - | - | `user.manage`. |
| `/System/DataQualityAudit` | R | - | - | - | - | - | - | - | - | Admin, GET/read-only. |
| `/System/ResetDatabase` và DangerOps | W có điều kiện | - | - | - | - | - | - | - | - | `system.danger.ops` + cấu hình môi trường. |
| `/api/integration/*` | Theo API key | Theo API key | Theo API key | Theo API key | Theo API key | Theo API key | Theo API key | Theo API key | Theo API key | API key, warehouse/owner scope, idempotency; không kế thừa session role. |

Các route read/export còn phải đối chiếu `docs/EXPORT_DOWNLOAD_API_SCOPE_REGISTRY.md`. Route không xuất hiện trong bảng này không mặc nhiên được coi là an toàn; authorization attributes và scope guard của chính action là nguồn thực thi.

## 6. Segregation Of Duties

| Maker permission | Verifier permission | Quy tắc |
|---|---|---|
| `voucher.create` | `voucher.approve.inbound` | Người tạo không tự hoàn tất/duyệt phiếu nhập. |
| `voucher.create` | `voucher.release.picking` | Người tạo không tự phát hành lấy hàng. |
| `voucher.create` | `voucher.post.outbound` | Người tạo không tự ghi sổ xuất. |
| `voucher.create` | `voucher.confirm.shipping` | Người tạo không tự xác nhận giao hàng. |
| `voucher.create` | `voucher.cancel` | Người tạo không tự hủy phiếu. |
| `voucher.create` | `qc.submit.inspection` | Người tạo không tự kiểm phẩm chứng từ của mình. |
| `voucher.create` | `stockcount.approve` | Người tạo không tự phê duyệt kiểm kê. |

SoD được kiểm tra server-side bằng actor hiện tại và maker của entity. Việc Admin có toàn permission không tự động bỏ qua SoD.

## 7. Scope Và Truy Cập Trực Tiếp

- **Warehouse scope:** non-admin có claim kho thì query/mutation phải giới hạn đúng kho đó.
- **Owner scope:** người dùng có danh sách owner được cấp chỉ được đọc/mutate/export entity cùng owner; owner rỗng cũng không tự động được phép.
- **IDOR:** route nhận ID phải load entity, kiểm tra warehouse/owner rồi mới đọc file, xuất dữ liệu hoặc mutation.
- **Cache/job/export:** cache key và background query phải mang warehouse/owner scope tương đương request.
- **GET/read-only:** chỉ có nghĩa không ghi dữ liệu; vẫn phải bảo vệ thông tin tài chính, audit và tenant scope.

### 7.1 Owner/Đối Tác Là Chiều Dữ Liệu, Không Phải Role

Owner scope được gán độc lập với role qua claim `OwnerPartnerId` hoặc bảng `AppUserOwnerScopes`. Vì vậy không tạo role giả như `OwnerStaff`; cùng một role có thể được giới hạn ở một hoặc nhiều chủ hàng.

| Nhóm người dùng | Nguồn owner scope | Khi có danh sách owner active | Khi không có owner scope |
|---|---|---|---|
| `Admin`, `Manager` | Claim hoặc `AppUserOwnerScopes` | Query, mutation, export và file access chỉ được phép trong owner đã gán; full permission không tự bỏ qua data scope/SoD. | Không áp owner filter bổ sung; warehouse, role, permission và business guard vẫn áp dụng. |
| `Staff`, `InboundStaff`, `OutboundStaff`, `InventoryStaff`, `TransportStaff` | Claim hoặc `AppUserOwnerScopes` | Chỉ vận hành entity `IOwnerScoped` thuộc owner đã gán và đúng warehouse. | Phạm vi theo warehouse/role/permission hiện tại. |
| `ReportViewer`, `Viewer` | Claim hoặc `AppUserOwnerScopes` | Chỉ đọc/report owner đã gán; mutation vẫn bị chặn bởi role/policy. | Chỉ đọc theo warehouse và route được cấp. |

Các entity chính mang `IOwnerScoped` gồm voucher/line, item/tồn theo vị trí, reservation, pick/movement task, LPN/sê-ri, shipment, yard/3PL và các artifact vận hành liên quan. Route nhận owner từ client phải gọi scope guard trước query/mutation; owner rỗng không được dùng để lách danh sách owner đã gán.

## 8. Trạng Thái Evidence

- Contract tài liệu: `RepoLocalAuditClosureTests.RolePermissionMatrix_ShouldDocumentEverySeededPermissionAndCriticalRouteGroup`.
- Role/policy reflection: `AuthorizationMatrixTests` kiểm tra route nhạy cảm và role group chuyên môn.
- Scope regression: các test warehouse/owner/API/file download trong `BusinessLogicHardeningTests` và `ApiIntegrationScopeHardeningTests`.
- Role E2E local: `artifacts/role-e2e/role-access-summary.txt` xác nhận 7 role cô lập `AUDIT_TEST_`; 14/14 kiểm tra menu và URL trực tiếp pass.
- Scheduled report: scoped Manager chỉ thấy cấu hình và bộ lọc đúng kho; Admin vẫn giữ phạm vi toàn hệ thống (`gate2-scheduled-report-scope-20260713.trx`).
- Chưa thể ký UAT production nếu chưa có tài khoản cô lập cho đủ 9 role, dữ liệu owner/kho test và evidence HTTP 200/403 trên môi trường mục tiêu.

## 9. Checklist Go-Live Còn Phụ Thuộc Môi Trường

- [ ] Chủ hệ thống duyệt danh sách người dùng của đủ 9 role.
- [ ] Không còn tài khoản dùng chung hoặc cấp `Admin`/`Manager` vượt nhu cầu.
- [ ] Chạy route-role-owner-warehouse matrix bằng tài khoản test cô lập.
- [ ] Xác minh trực tiếp URL/API khi menu bị ẩn.
- [ ] Ký UAT và lưu evidence 200/403 theo build/environment cụ thể.
