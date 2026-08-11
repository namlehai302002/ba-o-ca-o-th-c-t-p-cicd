using System.Text;

namespace WMS.Tests;

public sealed class EnterpriseUiTier1RedesignCompletionTests
{
    [Fact]
    public void Users_ShouldKeepResetAndLockActionsVisibleWithoutHorizontalScroll()
    {
        var root = FindRepositoryRoot();
        var users = Read(Path.Combine(root, "Views", "Users", "Index.cshtml"));
        var css = Read(Path.Combine(root, "wwwroot", "css", "site.css"));
        var visual = Read(Path.Combine(root, "tests", "visual", "wms-visual-regression.spec.ts"));

        foreach (var token in new[]
        {
            "enterprise-sticky-action-wrap",
            "enterprise-sticky-actions",
            "identity-action-cell",
            "identity-action-btn",
            "data-user-action=\"reset-password\"",
            "data-user-action=\"lock-account\"",
            "aria-label=\"Đổi mật khẩu cho @u.UserName\"",
            "aria-label=\"Khóa tài khoản @u.UserName\""
        })
        {
            Assert.Contains(token, users, StringComparison.Ordinal);
        }

        foreach (var token in new[]
        {
            ".identity-users-table th:last-child",
            ".enterprise-sticky-actions th:last-child",
            ".enterprise-sticky-actions td:last-child",
            "right: 0",
            "min-width: 980px",
            "min-width: 920px"
        })
        {
            Assert.Contains(token, css, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(".identity-users-table {\r\n    min-width: 1180px", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".identity-users-table {\n    min-width: 1180px", css, StringComparison.Ordinal);
        Assert.Contains("users action column is visible without horizontal scroll on desktop", visual, StringComparison.Ordinal);
        Assert.Contains("data-user-action=\"reset-password\"", visual, StringComparison.Ordinal);
        Assert.Contains("data-user-action=\"lock-account\"", visual, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedUiFoundation_ShouldEnhanceLegacyTablesActionsAndEmptyCharts()
    {
        var root = FindRepositoryRoot();
        var js = Read(Path.Combine(root, "wwwroot", "js", "site.js"));
        var css = Read(Path.Combine(root, "wwwroot", "css", "site.css"));

        foreach (var token in new[]
        {
            "enhanceEnterpriseActionColumns",
            "ensureAccessibleIconAction",
            "enterprise-sticky-actions",
            "enterprise-action-cell",
            "aria-label",
            "renderEmptyChartState",
            "enterprise-empty-chart",
            "hasPositiveSeriesData"
        })
        {
            Assert.Contains(token, js, StringComparison.Ordinal);
        }

        foreach (var token in new[]
        {
            ".enterprise-sticky-action-wrap",
            ".enterprise-sticky-actions th:last-child",
            ".enterprise-action-cell",
            ".enterprise-empty-chart",
            ".enterprise-list-report",
            ".enterprise-list-report-metrics",
            ".visually-hidden"
        })
        {
            Assert.Contains(token, css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MasterDataScreens_ShouldUseListReportMetricsStickyActionsAndAccessibleIconButtons()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "Views", "Warehouses", "Index.cshtml"),
            Path.Combine(root, "Views", "Partners", "Index.cshtml"),
            Path.Combine(root, "Views", "Categories", "Index.cshtml"),
            Path.Combine(root, "Views", "Units", "Index.cshtml")
        };

        foreach (var file in files)
        {
            var content = Read(file);
            Assert.Contains("enterprise-list-report-metrics", content, StringComparison.Ordinal);
            Assert.Contains("enterprise-sticky-actions", content, StringComparison.Ordinal);
            Assert.Contains("data-enterprise-screen=", content, StringComparison.Ordinal);
            Assert.Contains("aria-label=", content, StringComparison.Ordinal);
            Assert.DoesNotContain("style" + "=", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick=", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Analytics_ShouldAvoidLargeBlankChartsWhenSeriesAreEmpty()
    {
        var root = FindRepositoryRoot();
        var analytics = Read(Path.Combine(root, "Views", "Reports", "Analytics.cshtml"));
        var js = Read(Path.Combine(root, "wwwroot", "js", "site.js"));
        var css = Read(Path.Combine(root, "wwwroot", "css", "site.css"));

        Assert.Contains("data-wms-analytics-data=\"true\"", analytics, StringComparison.Ordinal);
        Assert.Contains("data-wms-analytics-chart=\"throughput\"", analytics, StringComparison.Ordinal);
        Assert.Contains("data-wms-analytics-chart=\"lines\"", analytics, StringComparison.Ordinal);
        Assert.Contains("renderEmptyChartState(throughput", js, StringComparison.Ordinal);
        Assert.Contains("renderEmptyChartState(lineChart", js, StringComparison.Ordinal);
        Assert.Contains(".analytics-chart-card.is-empty", css, StringComparison.Ordinal);
        Assert.Contains("max-height: 320px", css, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    private static string Read(string path) => File.ReadAllText(path, Encoding.UTF8);
}
