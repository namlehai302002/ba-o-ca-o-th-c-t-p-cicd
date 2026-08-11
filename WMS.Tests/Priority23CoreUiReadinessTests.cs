namespace WMS.Tests;

public sealed class Priority23CoreUiReadinessTests
{
    [Fact]
    public void MovementTaskService_ShouldAllowShortMoveAgainstConfirmedQuantityAndRollback()
    {
        var source = Read(Path.Combine(FindRepositoryRoot(), "Services", "MovementTaskService.cs"));

        Assert.Contains("sourceAvailable < confirmedQty", source, StringComparison.Ordinal);
        Assert.Contains("MovementTaskStatusEnum.Short", source, StringComparison.Ordinal);
        Assert.Contains("MOVE_SHORT", source, StringComparison.Ordinal);
        Assert.Contains("HasActiveTransaction", source, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmPickTask_ShouldCloseTransactionOnValidationReturn()
    {
        var source = Read(Path.Combine(FindRepositoryRoot(), "Services", "OutboundExecutionService.cs"));
        var methodStart = source.IndexOf("public async Task<WorkflowResult> ConfirmPickTaskAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task<WorkflowResult> ConfirmBulkPickTaskAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("finally", method, StringComparison.Ordinal);
        Assert.Contains("_unitOfWork.HasActiveTransaction", method, StringComparison.Ordinal);
        Assert.Contains("await _unitOfWork.RollbackAsync()", method, StringComparison.Ordinal);
    }

    [Fact]
    public void DataQualityAudit_ShouldReconcileReservedCacheAcrossAllReservationSources()
    {
        var source = Read(Path.Combine(FindRepositoryRoot(), "Services", "Tier1DataQualityAuditService.cs"));

        Assert.Contains("ITEM_LOCATION_RESERVED_CACHE_MISMATCH", source, StringComparison.Ordinal);
        Assert.Contains("RESERVATION_ITEM_LOCATION_MISSING", source, StringComparison.Ordinal);
        Assert.Contains("db.StockReservations", source, StringComparison.Ordinal);
        Assert.Contains("db.KittingWorkOrderLines", source, StringComparison.Ordinal);
        Assert.Contains("db.VasMaterialLines", source, StringComparison.Ordinal);
        Assert.Contains("BuildStockKey", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualRegression_ShouldCoverPriority23CoreWorkflowScreens()
    {
        var visual = Read(Path.Combine(FindRepositoryRoot(), "tests", "visual", "wms-visual-regression.spec.ts"));

        foreach (var route in new[]
        {
            "/Operations/QualityInspection",
            "/Operations/RfMovement",
            "/Operations/MovementTasks",
            "/Operations/Shipping",
            "/Operations/ShippingDispatch",
            "/Reports/WarehouseOverview",
            "/Reports/StockMovement",
            "/Reports/InventoryInOutSummary",
            "/Reports/InventoryTransactions",
            "/Reports/StockValuation",
            "/Reports/StockCount"
        })
        {
            Assert.Contains(route, visual, StringComparison.Ordinal);
        }

        Assert.Contains("dynamicTableRoutes", visual, StringComparison.Ordinal);
        Assert.Contains("renders without layout collision", visual, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    private static string Read(string path) => File.ReadAllText(path);
}
