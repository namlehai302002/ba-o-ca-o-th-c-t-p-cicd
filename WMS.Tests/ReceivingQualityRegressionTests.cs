namespace WMS.Tests;

public sealed class ReceivingQualityRegressionTests
{
    [Fact]
    public void QualityInspection_ShouldLoadItemNavigationUsedByDropdown()
    {
        var root = FindRepositoryRoot();
        var controller = ReadUtf8(Path.Combine(root, "Controllers", "OperationsController.Receiving.cs"));
        var view = ReadUtf8(Path.Combine(root, "Views", "Operations", "QualityInspection.cshtml"));
        var action = ExtractBetween(
            controller,
            "public async Task<IActionResult> QualityInspection(",
            "public async Task<IActionResult> Receiving(");

        Assert.Contains(".Include((Voucher v) => v.Details)", action, StringComparison.Ordinal);
        Assert.Contains(".ThenInclude((VoucherDetail d) => d.Item)", action, StringComparison.Ordinal);
        Assert.Contains("d.Item?.ItemCode", view, StringComparison.Ordinal);
        Assert.Contains("d.Item?.ItemName", view, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineQueue_ShouldDiscardBusinessRejectedOperationAndReleaseSubmitLoading()
    {
        var root = FindRepositoryRoot();
        var script = ReadUtf8(Path.Combine(root, "wwwroot", "js", "offline-scan-queue.js"));

        Assert.Contains("return { status: 'rejected', discard: true", script, StringComparison.Ordinal);
        Assert.Contains("discard: failure.discard === true", script, StringComparison.Ordinal);
        Assert.Contains("var shouldDiscard = error && error.discard === true", script, StringComparison.Ordinal);
        Assert.Contains("if (shouldDiscard)", script, StringComparison.Ordinal);
        Assert.Contains("storeDelete(operation.id)", script, StringComparison.Ordinal);
        Assert.Contains("form.dataset.noSubmitLoading = 'true'", script, StringComparison.Ordinal);
        Assert.Contains("window.wmsLoading.begin", script, StringComparison.Ordinal);
        Assert.Contains("window.wmsLoading.end", script, StringComparison.Ordinal);

        Assert.Contains("status: 'conflict'", script, StringComparison.Ordinal);
        Assert.Contains("status: 'blocked'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RfReceiving_ShouldExplainAndEnforceExactSerialRequirement()
    {
        var root = FindRepositoryRoot();
        var view = ReadUtf8(Path.Combine(root, "Views", "Operations", "RfReceiving.cshtml"));

        Assert.Contains("data-no-submit-loading=\"true\"", view, StringComparison.Ordinal);
        Assert.Contains("var registeredSerialCount", view, StringComparison.Ordinal);
        Assert.Contains("var serialsComplete", view, StringComparison.Ordinal);
        Assert.Contains("Đã ghi nhận @registeredSerialCount/@row.RequiredSerialCount sản phẩm; còn thiếu @row.PendingSerialCount số sê-ri.", view, StringComparison.Ordinal);
        Assert.Contains("if (!serialsComplete)", view, StringComparison.Ordinal);
        Assert.Contains("Còn thiếu @row.PendingSerialCount số sê-ri", view, StringComparison.Ordinal);
        Assert.DoesNotContain(">Serial", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" serial trước", view, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Không tìm thấy mốc bắt đầu: {startMarker}");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Không tìm thấy mốc kết thúc: {endMarker}");
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WMS.csproj"))
                && Directory.Exists(Path.Combine(directory.FullName, "WMS.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc của WMS.");
    }

    private static string ReadUtf8(string path)
        => File.ReadAllText(path);
}
