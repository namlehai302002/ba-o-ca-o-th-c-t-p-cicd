using WMS.Common;
using WMS.Models;

namespace WMS.Tests;

public class ReportedWorkflowUxRegressionTests
{
    [Fact]
    public void LocationStoragePolicy_ShouldAllowExistingKeyAndBlockNewOrDifferentOwnerKey()
    {
        var rows = new[]
        {
            new ItemLocation { ItemId = 1, OwnerPartnerId = 10, Quantity = 85 },
            new ItemLocation { ItemId = 2, OwnerPartnerId = 10, Quantity = 38 }
        };

        Assert.Null(LocationStoragePolicy.FindBlockingConflict(rows, 1, 10));
        Assert.NotNull(LocationStoragePolicy.FindBlockingConflict(rows, 3, 10));
        Assert.NotNull(LocationStoragePolicy.FindBlockingConflict(rows, 1, 20));
    }

    [Fact]
    public void OutboundViews_ShouldDistinguishPickCompletionFromInventoryPostingAndPrecheckSod()
    {
        var root = FindRepositoryRoot();
        var details = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Details.cshtml"));
        var pickTasks = File.ReadAllText(Path.Combine(root, "Views", "Operations", "PickTasks.cshtml"));
        var voucher = File.ReadAllText(Path.Combine(root, "Models", "Voucher.cs"));

        Assert.Contains("postBlockedBySod", details, StringComparison.Ordinal);
        Assert.Contains("postOutboundReady && !postBlockedBySod", details, StringComparison.Ordinal);
        Assert.Contains("Đã lấy đủ hàng, chưa ghi sổ xuất", details, StringComparison.Ordinal);
        Assert.Contains("tồn vật lý chưa giảm", details, StringComparison.Ordinal);
        Assert.Contains("Đã lấy đủ", pickTasks, StringComparison.Ordinal);
        Assert.Contains("Đã lấy đủ, chờ ghi sổ", voucher, StringComparison.Ordinal);
    }

    [Fact]
    public void InboundSerialReadiness_ShouldGuideCaptureAndBlockPrematurePosting()
    {
        var root = FindRepositoryRoot();
        var details = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Details.cshtml"));
        var inbound = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Inbound.cs"));

        Assert.Contains("hasInboundSerialReadinessIssue", details, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"SerialReceiving\"", details, StringComparison.Ordinal);
        Assert.Contains("Chưa đủ điều kiện tăng tồn", details, StringComparison.Ordinal);
        Assert.Contains("Xác nhận số lượng thực nhận và ghi nhận số sê-ri là hai bước riêng", details, StringComparison.Ordinal);
        Assert.Contains("Đây mới là bước xác nhận số lượng", inbound, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WMS.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
