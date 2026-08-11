using Microsoft.AspNetCore.Http;
using WMS.Models;

namespace WMS.Tests;

public sealed class FinalSystemUiReliabilityRegressionTests
{
    [Fact]
    public void SharedLayout_ShouldUseExistingBreadcrumbLandingsAndPermissionAwareCreateShortcut()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");

        Assert.DoesNotContain("@Url.Action(\"Index\", bcCtrl)", layout, StringComparison.Ordinal);
        Assert.Contains("{\"Operations\", \"NextTask\"}", layout, StringComparison.Ordinal);
        Assert.Contains("{\"Reports\", \"Inventory\"}", layout, StringComparison.Ordinal);
        Assert.Contains("{\"System\", \"SreDashboard\"}", layout, StringComparison.Ordinal);
        Assert.Contains("layoutCanCreateInbound", layout, StringComparison.Ordinal);
        Assert.Contains("&& quickSearchUrls.createInbound", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedLayout_ShouldExposeKeyboardSortingAndAccessibleFallbackDialogs()
    {
        var layout = Read("Views", "Shared", "_Layout.cshtml");

        Assert.Contains("box.setAttribute('role', 'dialog')", layout, StringComparison.Ordinal);
        Assert.Contains("box.setAttribute('aria-modal', 'true')", layout, StringComparison.Ordinal);
        Assert.Contains("document.removeEventListener('keydown', handleKeydown, true)", layout, StringComparison.Ordinal);
        Assert.Contains("th.setAttribute('aria-sort', 'none')", layout, StringComparison.Ordinal);
        Assert.Contains("event.key !== 'Enter' && event.key !== ' '", layout, StringComparison.Ordinal);
        Assert.Contains("Date.UTC(Number(viDate[3])", layout, StringComparison.Ordinal);
        Assert.Contains("new Intl.Collator('vi'", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherExcelImportAndInboundRejection_ShouldNotUseDisabledNativeDialogs()
    {
        var create = Read("Views", "Vouchers", "Create.cshtml");
        var approvals = Read("Views", "Operations", "InboundApprovals.cshtml");

        Assert.DoesNotContain("window.confirm(", create, StringComparison.Ordinal);
        Assert.Contains("window.enterpriseConfirm", create, StringComparison.Ordinal);
        Assert.DoesNotContain("reason = prompt(", approvals, StringComparison.Ordinal);
        Assert.Contains("window.enterprisePrompt", approvals, StringComparison.Ordinal);
    }

    [Fact]
    public void ProblemDetails_UserFacingTitles_ShouldBeVietnamese()
    {
        var context = new DefaultHttpContext();

        Assert.Equal("Không có quyền truy cập", WMS.Models.ProblemDetails.FromException(new UnauthorizedAccessException(), context).Title);
        Assert.Equal("Không tìm thấy dữ liệu", WMS.Models.ProblemDetails.FromException(new KeyNotFoundException(), context).Title);
        Assert.Equal(
            "Vi phạm nguyên tắc phân tách nhiệm vụ",
            WMS.Models.ProblemDetails.FromException(new SodViolationException("Không hợp lệ", "maker", "approve"), context).Title);
    }

    [Theory]
    [InlineData("Views", "Reports", "FinancialCostDashboard.cshtml", "for=\"financialWarehouseFilter\"", "id=\"financialWarehouseFilter\"")]
    [InlineData("Views", "Reports", "SemanticBi.cshtml", "for=\"semanticDaysFilter\"", "id=\"semanticDaysFilter\"")]
    [InlineData("Views", "System", "SreDashboard.cshtml", "for=\"srePeriodMinutes\"", "id=\"srePeriodMinutes\"")]
    [InlineData("Views", "Operations", "AutomationDashboard.cshtml", "for=\"automationWarehouseFilter\"", "id=\"automationWarehouseFilter\"")]
    [InlineData("Views", "Operations", "WorkflowProfiles.cshtml", "for=\"workflowProfileName\"", "id=\"workflowProfileName\"")]
    [InlineData("Views", "Vouchers", "WavePlanning.cshtml", "for=\"waveProfile\"", "id=\"waveProfile\"")]
    public void PriorityFilters_ShouldAssociateLabelsWithControls(string first, string second, string third, string labelToken, string controlToken)
    {
        var view = Read(first, second, third);

        Assert.Contains(labelToken, view, StringComparison.Ordinal);
        Assert.Contains(controlToken, view, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray()));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WMS.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc WMS.");
    }
}
