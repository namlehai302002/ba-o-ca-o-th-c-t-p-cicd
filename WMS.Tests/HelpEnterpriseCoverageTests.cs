using System.Text;
using System.Text.RegularExpressions;

namespace WMS.Tests;

public class HelpEnterpriseCoverageTests
{
    [Fact]
    public void Help_ShouldRenderEnterpriseManualBlocksForAllCoveredModules()
    {
        var root = FindRepositoryRoot();
        var help = ReadUtf8(Path.Combine(root, "Views", "Help", "Index.cshtml"));

        foreach (var block in new[]
        {
            "Mục đích",
            "Ai được dùng",
            "Bạn được thấy/làm gì",
            "Vào màn nào",
            "Điều kiện cần có trước khi làm",
            "Các bước thao tác chi tiết",
            "Hệ thống tự kiểm tra gì",
            "Lỗi thường gặp",
            "Kết quả đúng sau khi hoàn tất",
            "Không được làm"
        })
        {
            Assert.Contains(block, help, StringComparison.Ordinal);
        }

        var sectionCount = Regex.Matches(help, @"Key\s*=\s*""[^""]+""").Count;
        Assert.True(sectionCount >= 37, $"Help cần bao phủ đủ module nghiệp vụ; hiện chỉ có {sectionCount} mục.");

        foreach (var token in new[]
        {
            "Vật tư, đơn vị tính và quy cách đóng gói",
            "Chuyển kho, trả hàng, điều chỉnh và hủy phiếu",
            "Lô, ngày sản xuất và hạn sử dụng",
            "Đọc chứng từ bằng AI",
            "Kiểm kê thông minh và đề xuất từ AI",
            "Xử lý lỗi và ngoại lệ khi thao tác",
            "Tối ưu vận hành, bổ sung hàng và phân loại đơn",
            "Quy tắc vận hành và phân quyền khu vực",
            "Tích hợp, API, EDI, webhook và tự động hóa",
            "Lắp bộ hàng và gia công phụ trợ",
            "Kho nhiều chủ hàng và khách hàng thuê kho",
            "Nhãn, mẫu in và chứng từ kho",
            "Báo cáo nâng cao, lịch báo cáo và trợ lý dữ liệu",
            "Giám sát hệ thống và kiểm tra chất lượng dữ liệu"
        })
        {
            Assert.Contains(token, help, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Help_ShouldKeepRoleVisibilityExplicitForEveryBuiltInRole()
    {
        var root = FindRepositoryRoot();
        var help = ReadUtf8(Path.Combine(root, "Views", "Help", "Index.cshtml"));

        foreach (var token in new[]
        {
            "adminOnlyHelpSections",
            "commonOperatorHelpSections",
            "staffHelpSections",
            "inboundHelpSections",
            "outboundHelpSections",
            "inventoryHelpSections",
            "transportHelpSections",
            "reportingHelpSections",
            "viewerHelpSections",
            "CanSeeHelpSection",
            "sre-data-quality",
            "nguoi-dung-thiet-bi",
            "ai-doc-chung-tu",
            "nhan-chung-tu",
            "isAdmin",
            "isManager",
            "isWarehouseOperator",
            "isInboundStaff",
            "isOutboundStaff",
            "isInventoryStaff",
            "isTransportStaff",
            "isReportViewer",
            "chỉ đọc"
        })
        {
            Assert.Contains(token, help, StringComparison.Ordinal);
        }

        Assert.Contains("if (isAdmin) return true;", help, StringComparison.Ordinal);
        Assert.Contains("if (isManager) return !adminOnlyHelpSections.Contains(key);", help, StringComparison.Ordinal);
        Assert.Contains("if (isLegacyStaff) return staffHelpSections.Contains(key);", help, StringComparison.Ordinal);
        Assert.Contains("if (isInboundStaff) return inboundHelpSections.Contains(key);", help, StringComparison.Ordinal);
        Assert.Contains("if (isOutboundStaff) return outboundHelpSections.Contains(key);", help, StringComparison.Ordinal);
        Assert.Contains("if (isInventoryStaff) return inventoryHelpSections.Contains(key);", help, StringComparison.Ordinal);
        Assert.Contains("if (isTransportStaff) return transportHelpSections.Contains(key);", help, StringComparison.Ordinal);
        Assert.Contains("if (isReportViewer) return reportingHelpSections.Contains(key);", help, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_ShouldExposeRoleWorkspaceSafeRecoveryAndCurrentMenuNames()
    {
        var root = FindRepositoryRoot();
        var help = ReadUtf8(Path.Combine(root, "Views", "Help", "Index.cshtml"));
        var css = ReadUtf8(Path.Combine(root, "wwwroot", "css", "site.css"));

        foreach (var token in new[]
        {
            "help-role-dashboard",
            "help-quick-links",
            "roleReadingPath",
            "Lối vào nhanh theo vai trò",
            "Kiểm kê thông minh",
            "AI chỉ xếp hạng và đề xuất",
            "xác nhận số lượng thực nhận và đăng ký từng số sê-ri là hai bước riêng",
            "vi phạm tách nhiệm vụ",
            "kỳ đã khóa",
            "Hàng đợi quét",
            "Bất thường dữ liệu",
            "Vị trí/kệ/khu chứa"
        })
        {
            Assert.Contains(token, help, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(".help-role-dashboard", css, StringComparison.Ordinal);
        Assert.Contains(".help-quick-links", css, StringComparison.Ordinal);
        Assert.Contains(".help-toc-links a[hidden]", css, StringComparison.Ordinal);
        Assert.DoesNotContain("Screen = \"Báo cáo -> Tổng quan kho, Chỉ số vận hành, Kiểm kê, Định giá tồn kho", help, StringComparison.Ordinal);
        Assert.DoesNotContain("Screen = \"Danh mục -> Cấu hình kho, Đối tác", help, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFacingOperationalMessages_ShouldUseClearVietnamese()
    {
        var root = FindRepositoryRoot();
        var snapshot = ReadUtf8(Path.Combine(root, "Services", "InventorySnapshotService.cs"));
        var automation = ReadUtf8(Path.Combine(root, "Controllers", "OperationsController.Enterprise8910.cs"));
        var shipmentLoads = ReadUtf8(Path.Combine(root, "Controllers", "OperationsController.ShipmentLoads.cs"));
        var putaway = ReadUtf8(Path.Combine(root, "Services", "CoreWmsServices.cs"));
        var slotting = ReadUtf8(Path.Combine(root, "Services", "CoreControllerRefactorServices.cs"));
        var optimization = ReadUtf8(Path.Combine(root, "Services", "OptimizationAutomationIntegrationEnterpriseService.cs"));
        var movement = ReadUtf8(Path.Combine(root, "Services", "MovementTaskService.cs"));
        var labor = ReadUtf8(Path.Combine(root, "Services", "LaborManagementService.cs"));
        var billing = ReadUtf8(Path.Combine(root, "Services", "ThreePlEnterpriseBillingService.cs"));
        var dataQuality = ReadUtf8(Path.Combine(root, "Services", "Tier1DataQualityAuditService.cs"));
        var warehouseDetails = ReadUtf8(Path.Combine(root, "Views", "Warehouses", "Details.cshtml"));
        var voucherCreate = ReadUtf8(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("Đã tự đồng bộ ảnh chụp tồn", snapshot, StringComparison.Ordinal);
        Assert.Contains("cần kiểm tra thủ công", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("manual review required", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Đã chạy mô phỏng", automation, StringComparison.Ordinal);
        Assert.DoesNotContain("Mo phong", automation, StringComparison.Ordinal);
        Assert.Contains("Mã chuyến,Kho,Trạng thái", shipmentLoads, StringComparison.Ordinal);
        Assert.Contains("ShipmentLoadStatusLabel(row.Status)", shipmentLoads, StringComparison.Ordinal);
        Assert.Contains("Cùng vật tư, chủ hàng, lô hoặc hạn dùng", putaway, StringComparison.Ordinal);
        Assert.Contains("'consolidate same lot / expiry': 'Gom cùng lô hoặc hạn dùng'", voucherCreate, StringComparison.Ordinal);
        Assert.Contains("putawayStrategyLabel(s.strategy)", voucherCreate, StringComparison.Ordinal);
        Assert.Contains("Điểm {best.TotalScore}/100", slotting, StringComparison.Ordinal);
        Assert.DoesNotContain("Simulation horizon 30 days", slotting, StringComparison.Ordinal);
        Assert.Contains("Tồn khả dụng không đủ để phát hành nhiệm vụ", optimization, StringComparison.Ordinal);
        Assert.DoesNotContain("Insufficient available inventory", optimization, StringComparison.Ordinal);
        Assert.Contains("Di chuyển nguyên mã kiện", movement, StringComparison.Ordinal);
        Assert.Contains("Năng suất {activity.ProductivityPercent:N2}%", labor, StringComparison.Ordinal);
        Assert.Contains("Đã duyệt khiếu nại", billing, StringComparison.Ordinal);
        Assert.Contains("Số sê-ri đang hoạt động phải là duy nhất", dataQuality, StringComparison.Ordinal);
        Assert.DoesNotContain("Server trả về", warehouseDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("Server trả về", voucherCreate, StringComparison.Ordinal);
        Assert.DoesNotContain("Lỗi Engine", voucherCreate, StringComparison.Ordinal);
    }

    [Fact]
    public void Guides_ShouldNotDocumentSampleAiBillsAsOperatingProcedure()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "Views", "Help", "Index.cshtml"),
            Path.Combine(root, "HUONG_DAN_TOAN_BO_NGHIEP_VU_WMS_FULL.md"),
            Path.Combine(root, "HUONG_DAN_THUC_HANH_WMS_CHI_TIET.md")
        };

        foreach (var file in files)
        {
            var content = ReadUtf8(file);
            foreach (var forbidden in new[]
            {
                "bill mẫu",
                "sample-ai-bills",
                "wms-ai-inbound",
                "wms-ai-outbound",
                "BILL NHẬP",
                "BILL XUẤT"
            })
            {
                Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void VisualRoutes_ShouldHaveHelpCoverageOrExplicitManualToken()
    {
        var root = FindRepositoryRoot();
        var help = ReadUtf8(Path.Combine(root, "Views", "Help", "Index.cshtml"));
        var visualFiles = new[]
        {
            Path.Combine(root, "tests", "visual", "wms-visual-regression.spec.ts"),
            Path.Combine(root, "tests", "visual", "wms-mobile-deep.spec.ts")
        };

        var routeNames = visualFiles
            .SelectMany(path => Regex.Matches(ReadUtf8(path), @"\{\s*name:\s*'([^']+)'\s*,\s*(?:path|listPath):")
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var routeHelpTokens = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["home"] = new[] { "Trang chính" },
            ["home-rbac-impact"] = new[] { "Trang chính" },
            ["mobile-shell-home"] = new[] { "Trang chính" },
            ["help"] = new[] { "Hướng dẫn sử dụng" },
            ["trusted-devices"] = new[] { "Thiết bị tin cậy" },
            ["trusted-devices-rbac-impact"] = new[] { "Thiết bị tin cậy" },
            ["users"] = new[] { "Người dùng" },
            ["login-help-requests"] = new[] { "Yêu cầu truy cập" },
            ["items"] = new[] { "Danh mục vật tư" },
            ["item-create"] = new[] { "Danh mục vật tư" },
            ["item-details-first"] = new[] { "Danh mục vật tư" },
            ["categories"] = new[] { "Danh mục vật tư" },
            ["category-create"] = new[] { "Danh mục vật tư" },
            ["partners"] = new[] { "Đối tác" },
            ["partner-create"] = new[] { "Đối tác" },
            ["warehouses"] = new[] { "Cấu hình kho" },
            ["warehouse-create"] = new[] { "Cấu hình kho" },
            ["warehouse-details-first"] = new[] { "Cấu hình kho" },
            ["inventory-map"] = new[] { "Sơ đồ kho" },
            ["units"] = new[] { "Đơn vị tính" },
            ["vouchers"] = new[] { "Tra cứu phiếu" },
            ["voucher-details-first"] = new[] { "Chi tiết phiếu" },
            ["voucher-create"] = new[] { "Tạo phiếu nhập" },
            ["voucher-create-inbound"] = new[] { "Tạo phiếu nhập" },
            ["voucher-create-outbound"] = new[] { "Tạo phiếu xuất" },
            ["voucher-create-transfer"] = new[] { "Chuyển kho" },
            ["wave-planning"] = new[] { "Đợt gom đơn" },
            ["waves"] = new[] { "Đợt gom đơn" },
            ["receiving"] = new[] { "Tiếp nhận hàng" },
            ["rf-receiving"] = new[] { "Quét nhận hàng bằng điện thoại" },
            ["rf-receiving-focused"] = new[] { "Quét nhận hàng bằng điện thoại" },
            ["mobile-shell-rf-quick-dock"] = new[] { "Quét nhận hàng bằng điện thoại" },
            ["pick-tasks"] = new[] { "Nhiệm vụ lấy hàng" },
            ["picking"] = new[] { "Nhiệm vụ lấy hàng" },
            ["rf-picking"] = new[] { "Quét lấy hàng bằng điện thoại" },
            ["rf-picking-focused"] = new[] { "Quét lấy hàng bằng điện thoại" },
            ["rf-movement"] = new[] { "Quét di chuyển" },
            ["rf-movement-focused"] = new[] { "Quét di chuyển" },
            ["movement-tasks"] = new[] { "Nhiệm vụ di chuyển" },
            ["next-task"] = new[] { "Nhiệm vụ tiếp theo" },
            ["lpn-lookup"] = new[] { "Tra cứu mã kiện" },
            ["serial-lookup"] = new[] { "Tra cứu số sê-ri" },
            ["package-lookup"] = new[] { "Tra cứu mã kiện" },
            ["quality-inspection"] = new[] { "Kiểm tra chất lượng" },
            ["inbound-approvals"] = new[] { "Duyệt phiếu nhập" },
            ["shipping"] = new[] { "Đóng gói & giao" },
            ["shipping-dispatch"] = new[] { "Điều phối vận chuyển" },
            ["shipment-loads"] = new[] { "Chuyến xe" },
            ["shipment-load-details-first"] = new[] { "Chi tiết chuyến xe" },
            ["delivery-reconciliation"] = new[] { "Đối soát giao hàng" },
            ["zone-assignment"] = new[] { "Phân quyền khu vực" },
            ["dock-board"] = new[] { "cửa bến" },
            ["yard-management"] = new[] { "Bãi đỗ" },
            ["yard-billing-rates"] = new[] { "phí bãi" },
            ["yard-billing-charges"] = new[] { "phí bãi" },
            ["labor-productivity"] = new[] { "Năng suất lao động" },
            ["cross-dock-opportunities"] = new[] { "Chuyển thẳng" },
            ["replenishment"] = new[] { "Bổ sung hàng" },
            ["slotting"] = new[] { "Tối ưu vị trí" },
            ["slotting-simulation"] = new[] { "Mô phỏng slotting" },
            ["capacity-simulation"] = new[] { "Mô phỏng sức chứa" },
            ["optimization-dashboard"] = new[] { "Tối ưu vận hành" },
            ["optimization-rbac-impact"] = new[] { "Tối ưu vận hành" },
            ["automation-dashboard"] = new[] { "Tự động hóa" },
            ["automation-rbac-impact"] = new[] { "Tự động hóa thiết bị" },
            ["integration-dashboard"] = new[] { "Tích hợp hệ thống" },
            ["integration-rbac-impact"] = new[] { "Tích hợp hệ thống" },
            ["carrier-connectors"] = new[] { "bộ kết nối vận tải" },
            ["order-streaming-configs"] = new[] { "Cấu hình phát hành trực tiếp" },
            ["sortation-configs"] = new[] { "Cấu hình phân loại đơn" },
            ["exception-center"] = new[] { "Trung tâm ngoại lệ" },
            ["mhe-dashboard"] = new[] { "MHE" },
            ["tenant-owner-scopes"] = new[] { "Phân quyền chủ hàng" },
            ["kitting-work-orders"] = new[] { "Lắp bộ hàng" },
            ["create-kitting-work-order"] = new[] { "Lắp bộ hàng" },
            ["kitting-work-order-details-first"] = new[] { "Lắp bộ hàng" },
            ["vas-work-orders"] = new[] { "Gia công phụ trợ" },
            ["create-vas-work-order"] = new[] { "Gia công phụ trợ" },
            ["vas-work-order-details-first"] = new[] { "Gia công phụ trợ" },
            ["three-pl-runs"] = new[] { "Tính phí kho nhiều chủ hàng" },
            ["three-pl-run-details-first"] = new[] { "Tính phí kho nhiều chủ hàng" },
            ["three-pl-rates"] = new[] { "Bảng giá kho nhiều chủ hàng" },
            ["three-pl-contracts"] = new[] { "Hợp đồng kho nhiều chủ hàng" },
            ["three-pl-client-portal"] = new[] { "Khu vực chủ hàng" },
            ["workflow-profiles"] = new[] { "Quy tắc vận hành" },
            ["labels"] = new[] { "Nhãn & chứng từ" },
            ["label-templates"] = new[] { "Mẫu nhãn" },
            ["label-template-create"] = new[] { "Mẫu nhãn" },
            ["label-item-rules"] = new[] { "Quy tắc nhãn vật tư" },
            ["label-print-jobs"] = new[] { "Hàng đợi in" },
            ["label-print-jobs-focused"] = new[] { "Hàng đợi in" },
            ["inventory"] = new[] { "Xem tồn kho" },
            ["stock-movement"] = new[] { "Lịch sử nhập xuất" },
            ["inventory-in-out-summary"] = new[] { "Thống kê nhập/xuất theo kỳ" },
            ["warehouse-overview"] = new[] { "Tổng quan kho" },
            ["warehouse-overview-responsive"] = new[] { "Tổng quan kho" },
            ["inventory-transactions"] = new[] { "Sổ giao dịch tồn kho" },
            ["stock-valuation"] = new[] { "giá trị tồn" },
            ["stock-snapshot"] = new[] { "Chốt tồn" },
            ["stock-snapshot-rbac-impact"] = new[] { "Chốt tồn" },
            ["stock-count"] = new[] { "Kiểm kê" },
            ["period-locks"] = new[] { "Khóa kỳ" },
            ["period-locks-rbac-impact"] = new[] { "Khóa kỳ" },
            ["alerts"] = new[] { "Cảnh báo" },
            ["ops-kpi"] = new[] { "Chỉ số vận hành" },
            ["top-items"] = new[] { "Mã hàng phát sinh nhiều" },
            ["expiry-report"] = new[] { "Sắp hết hạn" },
            ["slow-moving-report"] = new[] { "Hàng chậm" },
            ["abc-analysis"] = new[] { "Phân nhóm quan trọng" },
            ["analytics"] = new[] { "Phân tích vận hành" },
            ["space-utilization"] = new[] { "Hiệu suất không gian" },
            ["dock-to-stock"] = new[] { "Thời gian nhập kho" },
            ["audit-trail"] = new[] { "Nhật ký" },
            ["audit-analytics"] = new[] { "Phân tích nhật ký" },
            ["scheduled-reports"] = new[] { "Lịch báo cáo" },
            ["semantic-bi"] = new[] { "Semantic BI" },
            ["financial-cost-dashboard"] = new[] { "Chi phí vận hành" },
            ["predictive-alerts"] = new[] { "Cảnh báo dự báo" },
            ["ai-assistant"] = new[] { "Trợ lý dữ liệu nội bộ" },
            ["sre-dashboard"] = new[] { "Giám sát hệ thống" },
            ["demo-data"] = new[] { "Demo dữ liệu" }
        };

        var failures = new List<string>();
        foreach (var routeName in routeNames)
        {
            if (!routeHelpTokens.TryGetValue(routeName, out var tokens))
            {
                failures.Add($"{routeName}: chưa khai báo token hướng dẫn");
                continue;
            }

            if (!tokens.Any(token => help.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{routeName}: Help thiếu một trong các token [{string.Join(", ", tokens)}]");
            }
        }

        Assert.True(failures.Count == 0, "Route visual/menu chưa được bao phủ trong Help:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void UserFacingSources_ShouldNotContainMojibakeMarkersOrLegacyOperationalWording()
    {
        var root = FindRepositoryRoot();
        var scannedRoots = new[]
        {
            "Controllers",
            "Services",
            "Views",
            "ViewModels",
            "Models",
            "wwwroot",
            "docs",
            "tests"
        };

        var allTextFiles = scannedRoots
            .Select(path => Path.Combine(root, path))
            .Where(Directory.Exists)
            .SelectMany(EnumerateTextFiles)
            .ToList();

        var mojibakeTokens = new[]
        {
            FromCodePoints(0x00c3, 0x0192),
            FromCodePoints(0x00c3, 0x201e),
            FromCodePoints(0x00c3, 0x2020),
            FromCodePoints(0x00c3, 0x00a1, 0x00c2, 0x00ba),
            FromCodePoints(0x00c3, 0x00a1, 0x00c2, 0x00bb),
            FromCodePoints(0x00c3, 0x201a),
            FromCodePoints(0x00ef, 0x00bf, 0x00bd)
        };

        var mojibakeFailures = FindContainsFailures(root, allTextFiles, mojibakeTokens);
        Assert.True(mojibakeFailures.Count == 0, "Phát hiện mojibake trong source user-facing:" + Environment.NewLine + string.Join(Environment.NewLine, mojibakeFailures));

        var productionTextFiles = allTextFiles
            .Where(path => !Path.GetRelativePath(root, path).StartsWith("tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var invalidUiMarkers = new[] { "@@media", "@@page" };
        var invalidMarkerFailures = FindContainsFailures(root, productionTextFiles, invalidUiMarkers);
        Assert.True(invalidMarkerFailures.Count == 0, "Phát hiện marker Razor/CSS sai trong source production:" + Environment.NewLine + string.Join(Environment.NewLine, invalidMarkerFailures));

        var productionCSharpFiles = productionTextFiles
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var debugTraceTokens = new[] { "System.Diagnostics.Debug.WriteLine", "Debug.WriteLine" };
        var debugTraceFailures = FindContainsFailures(root, productionCSharpFiles, debugTraceTokens);
        Assert.True(debugTraceFailures.Count == 0, "Debug trace remains in production source:" + Environment.NewLine + string.Join(Environment.NewLine, debugTraceFailures));

        var placeholderPersonNames = new[]
        {
            "Nguyen Van " + "A",
            FromCodePoints(0x004E, 0x0067, 0x0075, 0x0079, 0x1EC5, 0x006E, 0x0020, 0x0056, 0x0103, 0x006E, 0x0020, 0x0041),
            FromCodePoints(0x004E, 0x0067, 0x0075, 0x0079, 0x1EC5, 0x006E, 0x0020, 0x0056, 0x0103, 0x006E, 0x0020, 0x0042)
        };
        var placeholderNameFailures = FindContainsFailures(root, allTextFiles, placeholderPersonNames);
        Assert.True(placeholderNameFailures.Count == 0, "Placeholder person names remain in source/docs/tests:" + Environment.NewLine + string.Join(Environment.NewLine, placeholderNameFailures));

        var legacyWording = new[]
        {
            "Internal / unowned",
            "Nội bộ / chưa gán chủ hàng",
            "Chủ hàng kho dịch vụ",
            "Fixed Bin",
            "fixed bin",
            "Theo chủ hàng 3PL",
            "Chọn chủ hàng 3PL",
            "Chủ hàng 3PL",
            "Hàng 3PL",
            "Kho nội bộ"
        };
        var legacyFailures = FindContainsFailures(root, productionTextFiles, legacyWording);
        Assert.True(legacyFailures.Count == 0, "Phát hiện wording cũ/gây hiểu nhầm trong source production:" + Environment.NewLine + string.Join(Environment.NewLine, legacyFailures));

        var rawPlaceholderTokens = new[] { "\"---\"", "'---'", ">---</text>", ">---</span>", ">---</p>" };
        var rawPlaceholderFailures = FindContainsFailures(root, productionTextFiles, rawPlaceholderTokens);
        Assert.True(rawPlaceholderFailures.Count == 0, "Phát hiện placeholder --- còn hiển thị trong source production:" + Environment.NewLine + string.Join(Environment.NewLine, rawPlaceholderFailures));
    }

    private static List<string> FindContainsFailures(string root, IReadOnlyCollection<string> files, IReadOnlyCollection<string> tokens)
    {
        var failures = new List<string>();
        foreach (var file in files)
        {
            var content = ReadUtf8(file);
            foreach (var token in tokens)
            {
                if (content.Contains(token, StringComparison.Ordinal))
                {
                    failures.Add($"{Path.GetRelativePath(root, file)}: {token}");
                }
            }
        }

        return failures;
    }

    private static IEnumerable<string> EnumerateTextFiles(string root)
    {
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".cshtml",
            ".css",
            ".js",
            ".json",
            ".md",
            ".ps1",
            ".ts",
            ".txt",
            ".xml",
            ".yml",
            ".yaml"
        };

        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => allowedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "wwwroot" + Path.DirectorySeparatorChar + "vendor" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string FromCodePoints(params int[] codePoints)
    {
        return string.Concat(codePoints.Select(char.ConvertFromUtf32));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WMS.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Không tìm thấy WMS.sln từ thư mục test.");
    }

    private static string ReadUtf8(string path)
    {
        return File.ReadAllText(path, Encoding.UTF8);
    }
}
