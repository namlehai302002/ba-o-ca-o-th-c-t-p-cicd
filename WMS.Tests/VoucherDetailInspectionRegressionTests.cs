using Xunit;

namespace WMS.Tests;

public class VoucherDetailInspectionRegressionTests
{
    [Fact]
    public void VoucherDetails_ShouldUseBusinessFallbacksAndDedicatedInspectionPanel()
    {
        var details = ReadRepoFile("Views", "Vouchers", "Details.cshtml");

        Assert.Contains("Chưa đến bước kiểm", details, StringComparison.Ordinal);
        Assert.Contains("Chưa phân công", details, StringComparison.Ordinal);
        Assert.Contains("voucher-check-panel", details, StringComparison.Ordinal);
        Assert.Contains("SL chứng từ", details, StringComparison.Ordinal);
        Assert.Contains("SL thực nhập", details, StringComparison.Ordinal);
        Assert.Contains("Chênh lệch", details, StringComparison.Ordinal);
        Assert.Contains("moneyVN(Model.TotalAmount)", details, StringComparison.Ordinal);
        Assert.DoesNotContain("\"???\"", details, StringComparison.Ordinal);
        Assert.DoesNotContain("\"---\"", details, StringComparison.Ordinal);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(Model.ReviewedBy) ? \"---\"", details, StringComparison.Ordinal);
    }

    [Fact]
    public void InboundController_ShouldAssignInspectorAndBlockCompletionBeforeInspection()
    {
        var controller = ReadRepoFile("Controllers", "VouchersController.Inbound.cs");

        Assert.Contains("[Authorize(Roles = WmsRoles.InboundRoles)]", controller, StringComparison.Ordinal);
        Assert.Contains("voucher.ReviewedBy = checker;", controller, StringComparison.Ordinal);
        Assert.Contains("voucher.ReviewedAt = VietnamNow;", controller, StringComparison.Ordinal);
        Assert.Contains("[ACTUAL:", controller, StringComparison.Ordinal);
        Assert.Contains("AdjustmentWithVarianceRequiresNotes", controller, StringComparison.Ordinal);
        Assert.Contains("Phiếu chưa có người kiểm hàng", controller, StringComparison.Ordinal);
        Assert.Contains("Còn dòng chưa kiểm hàng", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void InboundExecutionService_ShouldPreserveInspectorWhenManagerPostsStock()
    {
        var service = ReadRepoFile("Services", "InboundExecutionService.cs");

        Assert.Contains("voucher.ReviewedBy = string.IsNullOrWhiteSpace(voucher.ReviewedBy) ? actor : voucher.ReviewedBy;", service, StringComparison.Ordinal);
        Assert.Contains("voucher.CompletedBy = actor;", service, StringComparison.Ordinal);
    }

    [Fact]
    public void EnterpriseSampleBills_ShouldExistForEachDemoDomainAndVoucherDirection()
    {
        var root = FindRepoRoot();
        var billDir = Path.Combine(root, "docs", "sample-ai-bills");
        var expectedPrefixes = new[]
        {
            "it-inbound-bill-01", "it-inbound-bill-02", "it-outbound-bill-01", "it-outbound-bill-02",
            "medical-inbound-bill-01", "medical-inbound-bill-02", "medical-outbound-bill-01", "medical-outbound-bill-02",
            "ecommerce-inbound-bill-01", "ecommerce-inbound-bill-02", "ecommerce-outbound-bill-01", "ecommerce-outbound-bill-02"
        };

        foreach (var prefix in expectedPrefixes)
        {
            Assert.True(File.Exists(Path.Combine(billDir, $"{prefix}.html")), $"Thiếu HTML bill mẫu: {prefix}");
            Assert.True(File.Exists(Path.Combine(billDir, $"{prefix}.png")), $"Thiếu PNG bill mẫu: {prefix}");
            Assert.True(File.Exists(Path.Combine(billDir, $"{prefix}.jpg")), $"Thiếu JPG bill mẫu: {prefix}");
        }
    }

    [Fact]
    public void EnterpriseSampleBills_ShouldUseReadableVietnameseBusinessText()
    {
        var root = FindRepoRoot();
        var billDir = Path.Combine(root, "docs", "sample-ai-bills");
        var expectedPrefixes = new[]
        {
            "it-inbound-bill-01", "it-inbound-bill-02", "it-outbound-bill-01", "it-outbound-bill-02",
            "medical-inbound-bill-01", "medical-inbound-bill-02", "medical-outbound-bill-01", "medical-outbound-bill-02",
            "ecommerce-inbound-bill-01", "ecommerce-inbound-bill-02", "ecommerce-outbound-bill-01", "ecommerce-outbound-bill-02"
        };

        foreach (var prefix in expectedPrefixes)
        {
            var html = File.ReadAllText(Path.Combine(billDir, $"{prefix}.html"));

            Assert.DoesNotContain("?", html, StringComparison.Ordinal);
            Assert.DoesNotContain("&#x", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("&#x", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Số chứng từ", html, StringComparison.Ordinal);
            Assert.Contains("Kho Tổng Hợp Miền Nam", html, StringComparison.Ordinal);
            Assert.Contains("ĐVT", html, StringComparison.Ordinal);
            Assert.Contains("Đơn giá", html, StringComparison.Ordinal);
            Assert.Contains("VNĐ", html, StringComparison.Ordinal);
            Assert.Contains("Ký và ghi rõ họ tên", html, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy WMS.sln từ thư mục test.");
    }
}
