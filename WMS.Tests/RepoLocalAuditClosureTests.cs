using System.Text;
using System.Text.RegularExpressions;
using WMS.Models;

namespace WMS.Tests;

public sealed class RepoLocalAuditClosureTests
{
    [Fact]
    public void VoucherStateMachine_ShouldBeCanonicalUtf8AndTraceCurrentRuntimeContracts()
    {
        var root = FindRepositoryRoot();
        var canonicalPath = Path.Combine(root, "docs", "domain", "voucher-state-machine.md");
        var stateMachine = ReadUtf8Strict(canonicalPath);
        var legacy = ReadUtf8Strict(Path.Combine(root, "docs", "VOUCHER_STATE_MACHINE.md"));

        foreach (var role in WmsRoles.Definitions)
        {
            Assert.Contains(role.Name, stateMachine, StringComparison.Ordinal);
            Assert.Contains(role.Label, stateMachine, StringComparison.Ordinal);
        }

        foreach (var enumType in new[]
        {
            typeof(InboundStatusEnum),
            typeof(FulfillmentStatusEnum),
            typeof(ReservationStatusEnum),
            typeof(PickTaskStatusEnum),
            typeof(StockCountStatusEnum),
            typeof(QualityStatusEnum),
            typeof(QcDispositionEnum),
            typeof(MovementTaskStatusEnum)
        })
        {
            Assert.Contains(enumType.Name, stateMachine, StringComparison.Ordinal);
            foreach (var value in Enum.GetNames(enumType))
                Assert.Contains($"`{value}`", stateMachine, StringComparison.Ordinal);
        }

        foreach (var route in new[]
        {
            "/Vouchers/SubmitForApproval",
            "/Vouchers/ApproveInbound",
            "/Vouchers/RejectInbound",
            "/Vouchers/ConfirmReceiving",
            "/Vouchers/CompleteInbound",
            "/Operations/UpdateDockMilestone",
            "/Vouchers/ConfirmForPicking",
            "/Vouchers/ConfirmPickTask",
            "/Vouchers/PostReservedOutbound",
            "/Vouchers/ConfirmPacking",
            "/Vouchers/ConfirmShipping",
            "/Vouchers/Cancel"
        })
        {
            Assert.Contains(route, stateMachine, StringComparison.Ordinal);
        }

        Assert.Contains("IsCancelled > IsPosted", stateMachine, StringComparison.Ordinal);
        Assert.Contains("server-side", stateMachine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Không nhận trạng thái đích từ client", stateMachine, StringComparison.Ordinal);
        Assert.Contains("`Voucher` không có trạng thái đích `Failed`", stateMachine, StringComparison.Ordinal);
        Assert.Contains("giữ nguyên trạng thái trước lệnh", stateMachine, StringComparison.Ordinal);
        Assert.Contains("Business rejection 400/422 phải rời hàng đợi retry", stateMachine, StringComparison.Ordinal);
        Assert.Contains("docs/domain/voucher-state-machine.md", legacy, StringComparison.Ordinal);
    }

    [Fact]
    public void RolePermissionMatrix_ShouldDocumentEverySeededPermissionAndCriticalRouteGroup()
    {
        var root = FindRepositoryRoot();
        var matrix = ReadUtf8Strict(Path.Combine(root, "docs", "ROLE_PERMISSION_MATRIX.md"));

        foreach (var role in WmsRoles.Definitions)
        {
            Assert.Contains(role.Name, matrix, StringComparison.Ordinal);
            Assert.Contains(role.Label, matrix, StringComparison.Ordinal);
        }

        foreach (var seededGrantRow in new[]
        {
            "| `voucher.create` | Có | Có | Có | Có | Có | Có | Không | Không | Không |",
            "| `voucher.confirm.shipping` | Có | Có | Không | Không | Không | Không | Có | Không | Không |",
            "| `qc.submit.inspection` | Có | Có | Không | Có | Không | Không | Không | Không | Không |",
            "| `stockcount.approve` | Có | Có | Không | Không | Không | Không | Không | Không | Không |",
            "| `report.view` | Có | Có | Có | Có | Có | Có | Có | Có | Có |",
            "| `audit.view` | Có | Không | Không | Không | Không | Không | Không | Không | Không |"
        })
        {
            Assert.Contains(seededGrantRow, matrix, StringComparison.Ordinal);
        }

        foreach (var permission in WmsPermissions.All)
            Assert.Contains(permission, matrix, StringComparison.Ordinal);

        foreach (var routeGroup in new[]
        {
            "/Vouchers/Create",
            "/Vouchers/ApproveInbound",
            "/Vouchers/ReleaseForPicking",
            "/Vouchers/PostReservedOutbound",
            "/Operations/RfReceiving",
            "/Operations/RfPicking",
            "/Operations/MovementTasks",
            "/Reports/Inventory",
            "/Reports/StockValuation",
            "/Reports/AuditTrail",
            "/System/DataQualityAudit",
            "/api/integration/*"
        })
        {
            Assert.Contains(routeGroup, matrix, StringComparison.Ordinal);
        }

        Assert.Contains("warehouse scope", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("owner scope", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AppUserOwnerScopes", matrix, StringComparison.Ordinal);
        Assert.Contains("Owner/Đối Tác Là Chiều Dữ Liệu, Không Phải Role", matrix, StringComparison.Ordinal);
        Assert.Contains("full permission không tự bỏ qua data scope/SoD", matrix, StringComparison.Ordinal);
        Assert.Contains("Segregation Of Duties", matrix, StringComparison.Ordinal);
        Assert.Contains("GET/read-only", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4 role", matrix, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardImpactMap_ShouldTraceStaffHomeAndManagementWarehouseOverview()
    {
        var root = FindRepositoryRoot();
        var impactMap = ReadUtf8Strict(Path.Combine(root, "docs", "audit", "DASHBOARD_FILE_IMPACT_MAP.md"));

        foreach (var token in new[]
        {
            "GET /Home/Index",
            "HomeController.Index",
            "GET /Reports/WarehouseOverview",
            "ReportsController.WarehouseOverview",
            "BuildWarehouseOverviewModelAsync",
            "WarehouseOverviewPageViewModel",
            "Views/Reports/WarehouseOverview.cshtml",
            "WmsRoles.ReportManagerRoles",
            "report.view",
            "canSeeOperationalReports"
        })
        {
            Assert.Contains(token, impactMap, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UiReferenceClassification_ShouldKeepDesktopCropsSeparateFromMobileEvidence()
    {
        var root = FindRepositoryRoot();
        var classification = ReadUtf8Strict(Path.Combine(root, "docs", "audit", "UI_REFERENCE_IMAGE_CLASSIFICATION.md"));

        foreach (var token in new[]
        {
            "Desktop expanded shell",
            "Desktop collapsed rail crop",
            "Desktop collapsed flyout",
            "Crop width is not viewport width",
            "not a mobile drawer",
            "wms-mobile-deep.spec.ts"
        })
        {
            Assert.Contains(token, classification, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FinalEnterpriseQaReport_ShouldMarkEvidenceWithoutAbsoluteProductionClaim()
    {
        var root = FindRepositoryRoot();
        var report = ReadUtf8Strict(Path.Combine(root, "FINAL_WMS_ENTERPRISE_QA_REPORT.md"));

        foreach (var token in new[]
        {
            "96/100",
            "repo/local enterprise readiness",
            "Đã chứng minh pass local/repo",
            "Không có bằng chứng hiện tại cho phép tuyên bố",
            "ROLE_PERMISSION_MATRIX.md",
            "data quality",
            "Tier-1",
            "RF scanner",
            "DR/HA",
            "certified integration",
            "OCR multi-document",
            "Markdown Cleanup"
        })
        {
            Assert.Contains(token, report, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("100/100", report, StringComparison.Ordinal);
        Assert.DoesNotContain("đạt 100% production", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("đã pass 100% production", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("User Id=", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrimaryMarkdownEvidence_ShouldBeStrictUtf8AndFreeOfKnownMojibake()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            "README.md",
            "FINAL_WMS_ENTERPRISE_QA_REPORT.md",
            Path.Combine("docs", "ROLE_PERMISSION_MATRIX.md"),
            Path.Combine("docs", "domain", "voucher-state-machine.md"),
            Path.Combine("docs", "EXPORT_DOWNLOAD_API_SCOPE_REGISTRY.md"),
            Path.Combine("docs", "TIER1_PRODUCTION_EVIDENCE_CHECKLIST_2026_05_29.md")
        };

        var mojibakeMarkers = new[]
        {
            FromCodePoints(0x00C3, 0x0192),
            FromCodePoints(0x00C3, 0x201E),
            FromCodePoints(0x00C3, 0x2020),
            FromCodePoints(0x00E1, 0x00BA),
            FromCodePoints(0x00E1, 0x00BB),
            FromCodePoints(0x00C4, 0x2018),
            FromCodePoints(0x00C6, 0x00B0),
            FromCodePoints(0x00E2, 0x20AC),
            char.ConvertFromUtf32(0xFFFD)
        };

        var failures = new List<string>();
        foreach (var relative in files)
        {
            var fullPath = Path.Combine(root, relative);
            var content = ReadUtf8Strict(fullPath);
            foreach (var marker in mojibakeMarkers)
            {
                if (content.Contains(marker, StringComparison.Ordinal))
                    failures.Add($"{relative} contains mojibake marker.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void ProductionRazorAndJavaScript_ShouldNotSilentlySwallowErrors()
    {
        var root = FindRepositoryRoot();
        var files = Directory
            .EnumerateFiles(Path.Combine(root, "Views"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "wwwroot", "js"), "*.js", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase));

        var emptyCatch = new Regex(
            @"(?:catch\s*(?:\([^)]*\))?\s*\{\s*\}|\.catch\s*\(\s*function\s*\([^)]*\)\s*\{\s*\}\s*\))",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        var failures = files
            .Select(path => new { Path = path, Content = ReadUtf8Strict(path) })
            .Where(file => emptyCatch.IsMatch(file.Content))
            .Select(file => Path.GetRelativePath(root, file.Path))
            .ToList();

        Assert.True(
            failures.Count == 0,
            "Production UI contains empty catch handlers:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    private static string ReadUtf8Strict(string path)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        return encoding.GetString(File.ReadAllBytes(path));
    }

    private static string FromCodePoints(params int[] codePoints)
        => string.Concat(codePoints.Select(char.ConvertFromUtf32));
}
