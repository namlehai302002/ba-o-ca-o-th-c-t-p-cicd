using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public sealed class SlottingVelocityAnalysisTests
{
    [Fact]
    public async Task Analyze_ShouldCalculateXyzFromClosedWeeksAndKeepSummaryConsistent()
    {
        await using var db = CreateDb();
        var today = VietnamTime.Now.Date;
        await SeedBaseAsync(db, today);

        for (var week = 1; week <= 5; week++)
        {
            AddCompletedPick(db, 100 + week, 1, 1, today.AddDays(-week * 7).AddHours(10), 10m);
            AddCompletedPick(db, 200 + week, 2, 1, today.AddDays(-week * 7).AddHours(11), week == 1 ? 30m : 1m);
        }

        AddCompletedPick(db, 301, 3, 1, today.AddDays(-7).AddHours(12), 5m);
        await db.SaveChangesAsync();

        var service = new SlottingPlanningService(db, new EfUnitOfWork(db));
        var result = await service.AnalyzeItemVelocityAsync(1, 42);
        var rows = await db.ItemVelocityClassifications
            .AsNoTracking()
            .Where(row => row.WarehouseId == 1 && row.IsActive)
            .OrderBy(row => row.ItemId)
            .ToListAsync();

        Assert.Equal(3, result.ClassifiedCount);
        Assert.Equal(result.AClassCount, rows.Count(row => row.AbcClass == 'A'));
        Assert.Equal(result.BClassCount, rows.Count(row => row.AbcClass == 'B'));
        Assert.Equal(result.CClassCount, rows.Count(row => row.AbcClass == 'C'));
        Assert.Equal(result.ClassifiedCount, result.AClassCount + result.BClassCount + result.CClassCount);

        Assert.Equal('X', rows[0].XyzClass);
        Assert.InRange(rows[0].DemandVariability, 0m, 0.50m);
        Assert.EndsWith("X", rows[0].CombinedClass, StringComparison.Ordinal);

        Assert.Equal('Z', rows[1].XyzClass);
        Assert.True(rows[1].DemandVariability > 1m);
        Assert.EndsWith("Z", rows[1].CombinedClass, StringComparison.Ordinal);

        Assert.Equal('N', rows[2].XyzClass);
        Assert.Equal(0m, rows[2].DemandVariability);
        Assert.EndsWith("N", rows[2].CombinedClass, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_ShouldDeactivateClassificationWithoutMovementInSelectedPeriod()
    {
        await using var db = CreateDb();
        var today = VietnamTime.Now.Date;
        await SeedBaseAsync(db, today);
        db.ItemVelocityClassifications.Add(new ItemVelocityClassification
        {
            ClassificationId = 10,
            ItemId = 1,
            WarehouseId = 1,
            AbcClass = 'A',
            XyzClass = 'X',
            CombinedClass = "AX",
            PickCount = 50,
            IsActive = true,
            LastAnalyzedAt = today.AddDays(-100)
        });
        AddCompletedPick(db, 401, 1, 1, today.AddDays(-60).AddHours(10), 5m);
        await db.SaveChangesAsync();

        var result = await new SlottingPlanningService(db, new EfUnitOfWork(db))
            .AnalyzeItemVelocityAsync(1, 28);

        Assert.Equal(0, result.ClassifiedCount);
        var stale = await db.ItemVelocityClassifications.SingleAsync();
        Assert.False(stale.IsActive);
        Assert.Equal(28, stale.AnalysisPeriodDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(27)]
    [InlineData(366)]
    public async Task Analyze_ShouldRejectUnsafeAnalysisPeriod(int periodDays)
    {
        await using var db = CreateDb();
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new SlottingPlanningService(db, new EfUnitOfWork(db))
                .AnalyzeItemVelocityAsync(1, periodDays));

        Assert.Equal("SLOTTING_PERIOD_INVALID", exception.Code);
    }

    [Theory]
    [InlineData("AN", 90)]
    [InlineData("BN", 65)]
    [InlineData("CN", 40)]
    public void SlottingScore_ShouldFallBackToAbcWhenXyzIsNotYetAvailable(string combinedClass, int expected)
    {
        using var db = CreateDb();
        var service = new SlottingPlanningService(db, new EfUnitOfWork(db));

        Assert.Equal(expected, service.GetSlottingScore(combinedClass));
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"slotting-velocity-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options) { SkipAudit = true };
    }

    private static async Task SeedBaseAsync(AppDbContext db, DateTime today)
    {
        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 1,
            WarehouseCode = "AUDIT_TEST_WH",
            WarehouseName = "Kho kiểm thử phân loại",
            IsActive = true
        });
        db.Items.AddRange(
            NewItem(1, "AUDIT_TEST_STABLE", today.AddDays(-120)),
            NewItem(2, "AUDIT_TEST_VARIABLE", today.AddDays(-120)),
            NewItem(3, "AUDIT_TEST_NEW", today.AddDays(-14)));
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 1,
            VoucherCode = "AUDIT_TEST_OUTBOUND",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            CreatedBy = "AUDIT_TEST"
        });
        await db.SaveChangesAsync();
    }

    private static Item NewItem(int itemId, string code, DateTime createdAt) => new()
    {
        ItemId = itemId,
        ItemCode = code,
        ItemName = code,
        BaseUomId = 1,
        CreatedAt = createdAt,
        IsActive = true
    };

    private static void AddCompletedPick(
        AppDbContext db,
        long pickTaskId,
        int itemId,
        long voucherId,
        DateTime completedAt,
        decimal pickedQty)
    {
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = pickTaskId,
            TaskCode = $"AUDIT_TEST_PICK_{pickTaskId}",
            VoucherId = voucherId,
            ItemId = itemId,
            SourceLocationId = 1,
            TargetQty = pickedQty,
            PickedQty = pickedQty,
            Status = PickTaskStatusEnum.Completed,
            CompletedAt = completedAt
        });
    }
}
