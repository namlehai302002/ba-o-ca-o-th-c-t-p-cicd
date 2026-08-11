using System.Text;
using System.Text.RegularExpressions;

namespace WMS.Tests;

public sealed class EnterpriseUiUxPolishTests
{
    [Fact]
    public void BlindStockCount_ShouldNotRenderExpectedQuantityAsPostedFormData()
    {
        var root = FindRepositoryRoot();
        var stockCount = Read(Path.Combine(root, "Views", "Reports", "StockCount.cshtml"));

        Assert.Contains("@if (Model.IsBlindCount)", stockCount, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Số lượng hệ thống được ẩn\"", stockCount, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Lines[@i].SystemQty\"", stockCount, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_ShouldLoadGlobalEnterpriseUiEnhancer()
    {
        var root = FindRepositoryRoot();
        var layout = Read(Path.Combine(root, "Views", "Shared", "_Layout.cshtml"));
        var siteJs = Read(Path.Combine(root, "wwwroot", "js", "site.js"));

        Assert.Contains("~/js/site.js", layout, StringComparison.Ordinal);
        Assert.Contains("enhanceTables", siteJs, StringComparison.Ordinal);
        Assert.Contains("enhanceForms", siteJs, StringComparison.Ordinal);
        Assert.Contains("enhanceStatusBadges", siteJs, StringComparison.Ordinal);
        Assert.Contains("enterprise-table-wrap", siteJs, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingFeedback_ShouldUseDelayedScopedEnterpriseHelper()
    {
        var root = FindRepositoryRoot();
        var siteJs = Read(Path.Combine(root, "wwwroot", "js", "site.js"));
        var css = Read(Path.Combine(root, "wwwroot", "css", "site.css"));
        var voucherCreate = Read(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));
        var itemCreate = Read(Path.Combine(root, "Views", "Items", "Create.cshtml"));
        var dockBoard = Read(Path.Combine(root, "Views", "Operations", "DockBoard.cshtml"));
        var slotting = Read(Path.Combine(root, "Views", "Operations", "Slotting.cshtml"));
        var lpnLookup = Read(Path.Combine(root, "Views", "Operations", "LpnLookup.cshtml"));
        var serialLookup = Read(Path.Combine(root, "Views", "Operations", "SerialLookup.cshtml"));

        Assert.Contains("window.wmsLoading", siteJs, StringComparison.Ordinal);
        Assert.Contains("beginLoading", siteJs, StringComparison.Ordinal);
        Assert.Contains("endLoading", siteJs, StringComparison.Ordinal);
        Assert.Contains("withBusy", siteJs, StringComparison.Ordinal);
        Assert.Contains("form.dataset.noSubmitLoading === 'true'", siteJs, StringComparison.Ordinal);
        Assert.Contains("submitter.dataset.noSubmitLoading === 'true'", siteJs, StringComparison.Ordinal);
        Assert.Contains("\\u0110ang x\\u1eed l\\u00fd...", siteJs, StringComparison.Ordinal);

        foreach (var token in new[]
        {
            ".wms-loading-overlay",
            ".wms-loading-spinner",
            ".enterprise-submit-loading",
            ".offline-queue-widget:not(.is-ready)",
            ".offline-queue-widget.is-empty"
        })
        {
            Assert.Contains(token, css, StringComparison.Ordinal);
        }

        Assert.Contains("id=\"voucherForm\" novalidate data-no-submit-loading=\"true\"", voucherCreate, StringComparison.Ordinal);

        foreach (var view in new[] { itemCreate, dockBoard, slotting, lpnLookup, serialLookup })
        {
            Assert.DoesNotContain("data-loading-delay=\"0\"", view, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("enterprise-submit-loading\" disabled", view, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DockBoard_ShouldExposeAnActionableEmptyStateWithoutConfiguredDoors()
    {
        var root = FindRepositoryRoot();
        var dockBoard = Read(Path.Combine(root, "Views", "Operations", "DockBoard.cshtml"));
        var visualSpec = Read(Path.Combine(root, "tests", "visual", "wms-visual-regression.spec.ts"));
        var css = Read(Path.Combine(root, "wwwroot", "css", "site.css"));

        Assert.Contains("@if (!Model.Doors.Any())", dockBoard, StringComparison.Ordinal);
        Assert.Contains("dock-board-empty-state", dockBoard, StringComparison.Ordinal);
        Assert.Contains("Chưa cấu hình cửa bến", dockBoard, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"Warehouses\"", dockBoard, StringComparison.Ordinal);
        Assert.Contains(".dock-board-empty-state", css, StringComparison.Ordinal);
        Assert.Contains("dock-board empty state", visualSpec, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineScanQueue_ShouldStayHiddenUntilReadyAndAvoidEmptyNavigationFlash()
    {
        var root = FindRepositoryRoot();
        var layout = Read(Path.Combine(root, "Views", "Shared", "_Layout.cshtml"));
        var queueJs = Read(Path.Combine(root, "wwwroot", "js", "offline-scan-queue.js"));
        var css = Read(Path.Combine(root, "wwwroot", "css", "site.css"));

        Assert.Contains("data-wms-operational=\"@(layoutCanOperate ? \"true\" : \"false\")\"", layout, StringComparison.Ordinal);
        Assert.Contains("document.body?.dataset?.wmsOperational !== 'true'", queueJs, StringComparison.Ordinal);
        Assert.Contains("widget.classList.add('is-ready')", queueJs, StringComparison.Ordinal);
        Assert.Contains("widget.classList.toggle('is-empty', count === 0)", queueJs, StringComparison.Ordinal);
        Assert.Contains(".offline-queue-widget:not(.is-ready)", css, StringComparison.Ordinal);
        Assert.Contains(".offline-queue-widget.is-empty", css, StringComparison.Ordinal);
    }

    [Fact]
    public void LaborManagement_ShouldKeepVietnameseMessagesAndConcurrencyGuards()
    {
        var root = FindRepositoryRoot();
        var labor = Read(Path.Combine(root, "Services", "LaborManagementService.cs"));

        foreach (var token in new[]
        {
            "Cần loại tác vụ cho chuẩn năng suất.",
            "Cần chuẩn năng suất lớn hơn 0.",
            "Cần nhân viên thực hiện tác vụ.",
            "Cần loại tác vụ lao động.",
            "Không tìm thấy tác vụ lao động.",
            "Không thể ghi nhận tác vụ lao động sau nhiều lần thử sinh mã."
        })
        {
            Assert.Contains(token, labor, StringComparison.Ordinal);
        }

        foreach (var banned in new[]
        {
            "Can loai",
            "Can chuan",
            "Can nhan",
            "C\u00E1\u00BA\u00A7n",
            "Kh\u00C3\u00B4ng",
            "t\u00C3\u00A1c",
            "\u00C3\u201E\u00E2\u20AC\u02DC"
        })
        {
            Assert.DoesNotContain(banned, labor, StringComparison.Ordinal);
        }

        Assert.Contains("FindExistingActivityBySourceAsync", labor, StringComparison.Ordinal);
        Assert.Contains("IsLaborActivityUniqueCollision", labor, StringComparison.Ordinal);
        Assert.Contains("retry", labor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthoredSource_ShouldNotContainObviousRuntimeTextPlaceholdersOrBrokenEntities()
    {
        var root = FindRepositoryRoot();
        var bannedPattern = new Regex(
            @"(&#x|&#x|\?\?\?|\[object Object\]|ForFun|INVOICE11111|Internal / unowned|Chủ hàng kho dịch vụ|Fixed Bin|Hàng 3PL|Chủ hàng 3PL)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var offenders = EnumerateRuntimeTextFiles(root)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, index }))
            .Where(x => bannedPattern.IsMatch(x.line))
            .Where(x => !x.line.Contains("JsonValueKind.Undefined", StringComparison.Ordinal))
            .Where(x => !x.line.Contains("ReviewResultEnum.Undefined", StringComparison.Ordinal))
            .Where(x => !x.line.Contains("typeof ", StringComparison.Ordinal))
            .Where(x => !x.path.EndsWith("EnterpriseUiUxPolishTests.cs", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{Path.GetRelativePath(root, x.path)}:{x.index + 1}: {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Tìm thấy text runtime lỗi:\n" + string.Join("\n", offenders.Take(50)));
    }

    [Fact]
    public void InventoryInOutSummaryView_ShouldKeepReadableVietnameseLabels()
    {
        var root = FindRepositoryRoot();
        var view = Read(Path.Combine(root, "Views", "Reports", "InventoryInOutSummary.cshtml"));

        foreach (var expected in new[]
        {
            "Thống kê nhập/xuất theo kỳ",
            "Từ ngày *",
            "Đến ngày *",
            "Ngày chứng từ",
            "Ngày ghi sổ",
            "Ngày nhập nguồn",
            "Vị trí nguồn",
            "Vị trí đích",
            "Người duyệt/xác nhận"
        })
        {
            Assert.Contains(expected, view, StringComparison.Ordinal);
        }

        foreach (var mojibake in new[]
        {
            "Th\u00C3\u00A1\u00C2\u00BB",
            "nh\u00C3\u00A1\u00C2\u00BA",
            "xu\u00C3\u00A1\u00C2\u00BA",
            "\u00C4\u0090",
            "t\u00C6\u00B0",
            "Ng\u00C3\u0083",
            "C\u00C3\u00A1ch"
        })
        {
            Assert.DoesNotContain(mojibake, view, StringComparison.Ordinal);
        }

        Assert.Contains("inventory-flow-report", view, StringComparison.Ordinal);
        Assert.Contains("inventory-flow-filter", view, StringComparison.Ordinal);
        Assert.Contains("inventory-flow-table", view, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"user-guide\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryInOutSummaryNavigation_ShouldNotAppearTwiceInEnterpriseMenu()
    {
        var root = FindRepositoryRoot();
        var layout = Read(Path.Combine(root, "Views", "Shared", "_Layout.cshtml"));
        var sidebar = Read(Path.Combine(root, "Views", "Shared", "_SidebarNav.cshtml"));
        var navigation = layout + sidebar;
        var help = Read(Path.Combine(root, "Views", "Help", "Index.cshtml"));

        Assert.Single(Regex.Matches(navigation, "asp-action=\"InventoryInOutSummary\"").Cast<Match>());
        Assert.Contains("Báo cáo ->", help, StringComparison.Ordinal);
        Assert.Contains("Thống kê nhập/xuất", help, StringComparison.Ordinal);
        Assert.DoesNotContain("Lịch sử nhập xuất, Thống kê nhập/xuất, Sổ giao dịch tồn kho", help, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationArchitecture_ShouldGroupWarehouseFunctionsByBusinessDomain()
    {
        var root = FindRepositoryRoot();
        var sidebar = Read(Path.Combine(root, "Views", "Shared", "_SidebarNav.cshtml"));

        var inbound = Section("inbound");
        var outbound = Section("outbound");
        var inventory = Section("inventory");
        var transport = Section("transport");
        var master = Section("master");
        var system = Section("system");

        Assert.Contains("data-nav-label=\"Vận chuyển\"", sidebar, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"Labels\"", transport, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"ShippingDispatch\"", transport, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"ShipmentLoads\"", transport, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"DeliveryReconciliation\"", transport, StringComparison.Ordinal);

        foreach (var misplaced in new[] { "YardBilling", "ThreePl", "YardManagement", "DockBoard" })
            Assert.DoesNotContain(misplaced, inbound, StringComparison.Ordinal);

        foreach (var misplaced in new[] { "ShippingDispatch", "ShipmentLoads", "DeliveryReconciliation", "CarrierConnectors" })
            Assert.DoesNotContain(misplaced, outbound, StringComparison.Ordinal);

        foreach (var misplaced in new[] { "SortationConfigs", "OrderStreamingConfigs", "AutomationDashboard", "IntegrationDashboard", "ThreePl" })
            Assert.DoesNotContain(misplaced, inventory, StringComparison.Ordinal);

        Assert.Contains("asp-action=\"SortationConfigs\"", master, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"ThreePlContracts\"", master, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"YardBillingRates\"", master, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"OrderStreamingConfigs\"", system, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"StockSnapshot\"", system, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"PeriodLocks\"", system, StringComparison.Ordinal);

        string Section(string section)
        {
            var marker = $"<div class=\"nav-section nav-rail-group\" data-section=\"{section}\"";
            var start = sidebar.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Không tìm thấy menu section {section}.");
            var next = sidebar.IndexOf("<div class=\"nav-section nav-rail", start + marker.Length, StringComparison.Ordinal);
            return next < 0 ? sidebar[start..] : sidebar[start..next];
        }
    }

    [Fact]
    public void NavigationActiveState_ShouldSeparateSharedRouteContexts()
    {
        var root = FindRepositoryRoot();
        var sidebar = Read(Path.Combine(root, "Views", "Shared", "_SidebarNav.cshtml"));
        var stockMovement = Read(Path.Combine(root, "Views", "Reports", "StockMovement.cshtml"));
        var inventoryMap = Read(Path.Combine(root, "Views", "Warehouses", "InventoryMap.cshtml"));

        foreach (var token in new[]
        {
            "var stockStatusQuery = Context.Request.Query[\"stockStatus\"].FirstOrDefault();",
            "var navContextQuery = Context.Request.Query[\"nav\"].FirstOrDefault();",
            "var mapContextQuery = Context.Request.Query[\"map\"].FirstOrDefault();",
            "var isInboundStockMovement = isStockMovement && string.Equals(navContextQuery, \"inbound\", StringComparison.OrdinalIgnoreCase);",
            "var isInventoryStockMovement = isStockMovement && !isInboundStockMovement;",
            "var isLowStockItems = ctrl == \"Items\" && act == \"Index\" && string.Equals(stockStatusQuery, \"low\", StringComparison.OrdinalIgnoreCase);",
            "var isMasterItems = ctrl == \"Items\" && act == \"Index\" && string.IsNullOrWhiteSpace(stockStatusQuery);",
            "var isMasterLocationMap = ctrl == \"Warehouses\" && act == \"InventoryMap\" && string.Equals(mapContextQuery, \"master\", StringComparison.OrdinalIgnoreCase);",
            "var isInventoryMap = ctrl == \"Warehouses\" && act == \"InventoryMap\" && !isMasterLocationMap;",
            "asp-route-nav=\"inbound\"",
            "asp-route-nav=\"inventory\"",
            "asp-route-stockStatus=\"low\"",
            "asp-route-map=\"inventory\"",
            "asp-route-map=\"master\""
        })
        {
            Assert.Contains(token, sidebar, StringComparison.Ordinal);
        }

        Assert.Contains("var navContext = Context.Request.Query[\"nav\"].FirstOrDefault();", stockMovement, StringComparison.Ordinal);
        Assert.Contains("name=\"nav\" value=\"@navContext\"", stockMovement, StringComparison.Ordinal);
        Assert.Contains("asp-route-nav=\"@navContext\"", stockMovement, StringComparison.Ordinal);
        Assert.Contains("var mapContext = Context.Request.Query[\"map\"].FirstOrDefault();", inventoryMap, StringComparison.Ordinal);
        Assert.Contains("asp-route-map=\"@mapContext\"", inventoryMap, StringComparison.Ordinal);

        Assert.DoesNotContain("act == \"StockMovement\" ? \"active\"", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("ctrl == \"Items\" && act == \"Index\" ? \"active\"", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("ctrl == \"Warehouses\" && act == \"InventoryMap\" ? \"active\"", sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void WarehouseOverview_ShouldBeEnterpriseCockpitAndMenuEntry()
    {
        var root = FindRepositoryRoot();
        var layout = Read(Path.Combine(root, "Views", "Shared", "_Layout.cshtml"));
        var sidebar = Read(Path.Combine(root, "Views", "Shared", "_SidebarNav.cshtml"));
        var navigation = layout + sidebar;
        var view = Read(Path.Combine(root, "Views", "Reports", "WarehouseOverview.cshtml"));
        var controller = Read(Path.Combine(root, "Controllers", "ReportsController.WarehouseOverview.cs"));
        var css = Read(Path.Combine(root, "wwwroot", "css", "site.css"));
        var visual = Read(Path.Combine(root, "tests", "visual", "wms-visual-regression.spec.ts"));

        Assert.Contains("asp-action=\"WarehouseOverview\"", navigation, StringComparison.Ordinal);
        Assert.Contains("Tổng quan kho", navigation, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-report", view, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-filter", view, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-metrics", view, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-panel-header", view, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-table-scroll", view, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-data-table-warehouses", view, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-data-table-daily", view, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-data-table-items", view, StringComparison.Ordinal);
        Assert.Contains("warehouse-overview-data-table-exceptions", view, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Bộ lọc tổng quan kho\"", view, StringComparison.Ordinal);
        Assert.Contains("scope=\"col\"", view, StringComparison.Ordinal);
        Assert.Contains("Kiểm soát dữ liệu", view, StringComparison.Ordinal);
        Assert.Contains("Dòng hàng theo ngày", view, StringComparison.Ordinal);
        Assert.Contains("StatusLabel", view, StringComparison.Ordinal);
        Assert.Contains("Giữ chỗ vượt tồn", controller, StringComparison.Ordinal);
        Assert.Contains("Phiếu thiếu sổ kho", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("@row.Code", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemLocations không", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ReservedQty không", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryTransactions số", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"user-guide\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("hero", view, StringComparison.OrdinalIgnoreCase);

        foreach (var kpiExpression in new[]
        {
            "@Qty(Model.Kpi.OnHandQty)",
            "@Qty(Model.Kpi.ReservedQty)",
            "@Qty(Model.Kpi.AvailableQty)",
            "@Qty(Model.Kpi.NetMovementQty)",
            "Model.Kpi.OpenInboundVouchers + Model.Kpi.OpenOutboundVouchers",
            "Model.Exceptions.Sum(x => x.Count)",
            "Model.Kpi.TotalStockValue.ToString(\"N0\")",
            "@Count(Model.Kpi.ExpiringLotCount)"
        })
        {
            Assert.Contains(kpiExpression, view, StringComparison.Ordinal);
        }

        Assert.Contains("body.sidebar-collapsed .nav-link", css, StringComparison.Ordinal);
        Assert.Contains("gap: 0;", css, StringComparison.Ordinal);
        Assert.Contains(".warehouse-overview-panel", css, StringComparison.Ordinal);
        Assert.Contains(".warehouse-overview-data-table-daily", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1180px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 640px)", css, StringComparison.Ordinal);
        Assert.Contains("iconCenterX", visual, StringComparison.Ordinal);
        Assert.Contains("/Reports/WarehouseOverview", visual, StringComparison.Ordinal);
        Assert.Contains("warehouse overview stays cohesive at desktop laptop tablet and mobile widths", visual, StringComparison.Ordinal);

        var mobileDeep = Read(Path.Combine(root, "tests", "visual", "wms-mobile-deep.spec.ts"));
        Assert.Contains("{ name: 'warehouse-overview', path: '/Reports/WarehouseOverview' }", mobileDeep, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    private static string Read(string path) => File.ReadAllText(path);

    private static IEnumerable<string> EnumerateAuthoredTextFiles(string root)
    {
        var excludedSegments = new[]
        {
            "bin",
            "obj",
            "node_modules",
            "vendor",
            "artifacts",
            "test-results",
            "uploads",
            "App_Data"
        };
        var includedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".cshtml",
            ".js",
            ".css",
            ".json",
            ".md",
            ".ps1",
            ".ts",
            ".csproj",
            ".sln",
            ".webmanifest",
            ".html",
            ".yml",
            ".yaml",
            ".config"
        };

        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => includedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !excludedSegments.Any(segment => path.Contains($"{Path.DirectorySeparatorChar}{segment}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}.auth{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateRuntimeTextFiles(string root)
    {
        var roots = new[]
        {
            Path.Combine(root, "Controllers"),
            Path.Combine(root, "Services"),
            Path.Combine(root, "Models"),
            Path.Combine(root, "ViewModels"),
            Path.Combine(root, "Views"),
            Path.Combine(root, "wwwroot", "js"),
            Path.Combine(root, "wwwroot", "css")
        };

        var includedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".cshtml",
            ".js",
            ".css"
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            .Where(path => includedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string TextFromCodePoints(params int[] codePoints)
    {
        var builder = new StringBuilder();
        foreach (var codePoint in codePoints)
            builder.Append(char.ConvertFromUtf32(codePoint));
        return builder.ToString();
    }
}
