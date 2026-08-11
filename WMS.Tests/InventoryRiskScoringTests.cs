using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using WMS.Authorization;
using WMS.Common;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public sealed class InventoryRiskScoringTests
{
    private static readonly DateTime Cutoff = new(2026, 7, 16, 9, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task RuleBaseline_WithFixedCutoff_IsDeterministicAndVersioned()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = new InventoryRiskScoringService(db);
        var query = Query(warehouseId: 1, ownerPartnerId: 101, allowedOwners: [101]);

        var first = await service.BuildPageAsync(query);
        var second = await service.BuildPageAsync(query);

        var firstRow = Assert.Single(first.Rows);
        var secondRow = Assert.Single(second.Rows);
        Assert.Equal("RULE-BASELINE-1.0", first.RuleVersion);
        Assert.Equal("AI-FEATURE-SCHEMA-0.1", first.FeatureSchemaVersion);
        Assert.Equal(firstRow.FeatureHash, secondRow.FeatureHash);
        Assert.Equal(firstRow.OutputHash, secondRow.OutputHash);
        Assert.Equal(firstRow.RiskScore, secondRow.RiskScore);
        Assert.Equal(firstRow.Reasons.Select(reason => reason.Code), secondRow.Reasons.Select(reason => reason.Code));
        Assert.NotEmpty(firstRow.Reasons);
    }

    [Fact]
    public async Task RuleBaseline_EnforcesWarehouseAndOwnerScopeBeforeScoring()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = new InventoryRiskScoringService(db);

        var owner101 = await service.BuildPageAsync(Query(1, 101, [101]));
        var owner202 = await service.BuildPageAsync(Query(1, 202, [202]));
        var forbiddenOwner = await service.BuildPageAsync(Query(1, 202, [101]));
        var otherWarehouse = await service.BuildPageAsync(Query(2, null, []));

        Assert.All(owner101.Rows, row => Assert.Equal(101, row.OwnerPartnerId));
        Assert.All(owner202.Rows, row => Assert.Equal(202, row.OwnerPartnerId));
        Assert.Empty(forbiddenOwner.Rows);
        Assert.Empty(otherWarehouse.Rows);
    }

    [Fact]
    public async Task RuleBaseline_BlocksInvalidBalanceInsteadOfReturningZeroRisk()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var invalid = await db.ItemLocations.SingleAsync(row => row.ItemLocationId == 2);
        invalid.ReservedQty = invalid.Quantity + 1m;
        await db.SaveChangesAsync();

        var model = await new InventoryRiskScoringService(db)
            .BuildPageAsync(Query(1, 202, [202]));

        var row = Assert.Single(model.Rows);
        Assert.Equal(InventoryRiskDataQualityStatusEnum.Blocked, row.DataQualityStatus);
        Assert.Contains("BLOCKED_OVER_RESERVED", row.DataQualityCodes);
        Assert.Null(row.RiskScore);
        Assert.Null(row.Severity);
        Assert.Empty(row.Reasons);
    }

    [Fact]
    public async Task RuleBaseline_FilterMetrics_ShouldUseTheSameFilteredScopeAsRows()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var invalid = await db.ItemLocations.SingleAsync(row => row.ItemLocationId == 2);
        invalid.ReservedQty = invalid.Quantity + 1m;
        await db.SaveChangesAsync();

        var model = await new InventoryRiskScoringService(db)
            .BuildPageAsync(Query(1, null, [101, 202], InventoryRiskDataQualityStatusEnum.Blocked));

        Assert.Single(model.Rows);
        Assert.Equal(1, model.TotalCount);
        Assert.Equal(1, model.BlockedCount);
        Assert.Equal(0, model.ScoredCount);
        Assert.Equal(0, model.PartialCount);
        Assert.Equal(0m, model.CoveragePercent);
    }

    [Fact]
    public async Task RuleBaseline_InternalInventoryScope_ShouldNotBeMarkedAsMissingOwnerData()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var stock = await db.ItemLocations.SingleAsync(row => row.ItemLocationId == 1);
        stock.OwnerPartnerId = null;
        var item = await db.Items.SingleAsync(row => row.ItemId == 1);
        item.OwnerPartnerId = null;
        await db.SaveChangesAsync();

        var model = await new InventoryRiskScoringService(db).BuildPageAsync(new InventoryRiskQuery
        {
            WarehouseId = 1,
            Search = "AUDIT_TEST_AI2_SKU_101",
            PredictionCutoff = Cutoff,
            PageSize = 100
        });

        var row = Assert.Single(model.Rows);
        Assert.DoesNotContain("PARTIAL_INTERNAL_OWNER_SCOPE", row.DataQualityCodes);
    }

    [Fact]
    public async Task RuleBaseline_SerialTrackedScope_ShouldBeBlockedUntilSerialAwareCountingExists()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var item = await db.Items.SingleAsync(row => row.ItemId == 1);
        item.TrackSerial = true;
        await db.SaveChangesAsync();

        var model = await new InventoryRiskScoringService(db)
            .BuildPageAsync(Query(1, 101, [101]));

        var row = Assert.Single(model.Rows);
        Assert.Equal(InventoryRiskDataQualityStatusEnum.Blocked, row.DataQualityStatus);
        Assert.Contains("BLOCKED_SERIAL_COUNT_NOT_SUPPORTED", row.DataQualityCodes);
        Assert.Null(row.RiskScore);
    }

    [Fact]
    public async Task RuleBaseline_MultipleHoldBuckets_ShouldBeBlockedFromCountRecommendation()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 3,
            ItemId = 1,
            OwnerPartnerId = 101,
            LocationId = 1,
            Quantity = 2m,
            HoldStatus = InventoryHoldStatusEnum.Quarantine,
            UpdatedAt = Cutoff.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var model = await new InventoryRiskScoringService(db)
            .BuildPageAsync(Query(1, 101, [101]));

        var row = Assert.Single(model.Rows);
        Assert.Equal(InventoryRiskDataQualityStatusEnum.Blocked, row.DataQualityStatus);
        Assert.Contains("BLOCKED_MULTIPLE_HOLD_BUCKETS", row.DataQualityCodes);
        Assert.Null(row.RiskScore);
    }

    [Theory]
    [InlineData("PARTIAL_COUNT_HISTORY_MISSING", "Chưa có lần kiểm kê được duyệt để đối chiếu sai lệch")]
    [InlineData("PARTIAL_SERIAL_COVERAGE_NOT_SCORED", "Chưa đưa mức độ đầy đủ số sê-ri vào điểm ưu tiên")]
    [InlineData("BLOCKED_MULTIPLE_HOLD_BUCKETS", "Phạm vi có nhiều trạng thái tồn; chưa thể tạo phiếu kiểm kê an toàn")]
    [InlineData("BLOCKED_SERIAL_COUNT_NOT_SUPPORTED", "Vật tư quản lý số sê-ri; cần kiểm kê theo từng số sê-ri")]
    [InlineData("BLOCKED_OVER_RESERVED", "Số lượng giữ chỗ vượt số lượng tồn")]
    [InlineData("UNRECOGNIZED_INTERNAL_CODE", "Cảnh báo dữ liệu chưa được phân loại")]
    public void DataQualityDetail_ShouldReturnOperatorFriendlyVietnamese(string code, string expected)
    {
        Assert.Equal(expected, InventoryRiskUiLabels.DataQualityDetail(code));
        Assert.DoesNotContain(code, InventoryRiskUiLabels.DataQualityDetail(code), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShadowPersistence_WritesOnlyImmutableAiRecords()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = new InventoryRiskScoringService(db);
        var query = Query(1, 101, [101]);
        var stockBefore = await db.ItemLocations.AsNoTracking().SumAsync(row => row.Quantity);
        var reservationsBefore = await db.ItemLocations.AsNoTracking().SumAsync(row => row.ReservedQty);
        var ledgerBefore = await db.InventoryTransactions.CountAsync();
        var countSheetsBefore = await db.StockCountSheets.CountAsync();
        var persistedAfter = VietnamTime.Now.AddSeconds(-1);

        var result = await service.PersistShadowBatchAsync(query, "AUDIT_TEST_AI2");
        var persistedBefore = VietnamTime.Now.AddSeconds(1);

        Assert.Equal(1, result.SnapshotCount);
        Assert.Equal(1, result.PredictionCount);
        Assert.Equal(stockBefore, await db.ItemLocations.AsNoTracking().SumAsync(row => row.Quantity));
        Assert.Equal(reservationsBefore, await db.ItemLocations.AsNoTracking().SumAsync(row => row.ReservedQty));
        Assert.Equal(ledgerBefore, await db.InventoryTransactions.CountAsync());
        Assert.Equal(countSheetsBefore, await db.StockCountSheets.CountAsync());
        var model = Assert.Single(await db.InventoryRiskModelVersions.AsNoTracking().ToListAsync());
        Assert.Equal(InventoryRiskModelLifecycleStatusEnum.Champion, model.LifecycleStatus);
        var snapshot = Assert.Single(await db.InventoryRiskFeatureSnapshots.AsNoTracking().ToListAsync());
        var prediction = Assert.Single(await db.InventoryRiskPredictions.AsNoTracking().ToListAsync());
        Assert.Equal(result.BatchId, snapshot.BatchId);
        Assert.Equal(snapshot.InventoryRiskFeatureSnapshotId, prediction.InventoryRiskFeatureSnapshotId);
        Assert.True(prediction.IsShadowMode);
        Assert.InRange(snapshot.CreatedAt, persistedAfter, persistedBefore);
        Assert.InRange(prediction.GeneratedAt, persistedAfter, persistedBefore);
        Assert.NotEqual(query.PredictionCutoff, snapshot.CreatedAt);
    }

    [Fact]
    public async Task NewRuleVersion_ShouldRemainChallengerAndNeverAutoPromote()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var query = Query(1, 101, [101]);
        await new InventoryRiskScoringService(db).PersistShadowBatchAsync(query, "AUDIT_TEST_AI6");

        var challengerOptions = new InventoryRiskRuleOptions
        {
            Version = "RULE-BASELINE-2.0"
        };
        await new InventoryRiskScoringService(db, Options.Create(challengerOptions))
            .PersistShadowBatchAsync(query, "AUDIT_TEST_AI6");

        var versions = await db.InventoryRiskModelVersions
            .AsNoTracking()
            .OrderBy(row => row.Version)
            .ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(InventoryRiskModelLifecycleStatusEnum.Champion, versions[0].LifecycleStatus);
        Assert.Equal(InventoryRiskModelLifecycleStatusEnum.Challenger, versions[1].LifecycleStatus);
        Assert.Single(versions, row => row.LifecycleStatus == InventoryRiskModelLifecycleStatusEnum.Champion);
    }

    [Fact]
    public async Task RetiredRuleVersion_ShouldNotProduceNewShadowPredictions()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = new InventoryRiskScoringService(db);
        var query = Query(1, 101, [101]);
        await service.PersistShadowBatchAsync(query, "AUDIT_TEST_AI6");
        var version = await db.InventoryRiskModelVersions.SingleAsync();
        version.LifecycleStatus = InventoryRiskModelLifecycleStatusEnum.Retired;
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.PersistShadowBatchAsync(query, "AUDIT_TEST_AI6"));

        Assert.Equal("INVENTORY_RISK_MODEL_RETIRED", exception.Code);
        Assert.Single(await db.InventoryRiskPredictions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ExistingVersion_WithDifferentConfiguration_RequiresVersionBump()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var query = Query(1, 101, [101]);
        await new InventoryRiskScoringService(db).PersistShadowBatchAsync(query, "AUDIT_TEST_AI2");
        var changed = new InventoryRiskRuleOptions
        {
            Version = "RULE-BASELINE-1.0",
            AdjustmentWeight = 20m,
            PriorVarianceWeight = 30m
        };

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new InventoryRiskScoringService(db, Options.Create(changed))
                .PersistShadowBatchAsync(query, "AUDIT_TEST_AI2"));

        Assert.Equal("INVENTORY_RISK_VERSION_CONFIGURATION_MISMATCH", exception.Code);
    }

    [Fact]
    public async Task HistoricalRiskCutoff_ShouldNotUseFutureCycleCountScheduleTimestamp()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.CycleCountPrograms.Add(new CycleCountProgram
        {
            ProgramId = 10,
            ProgramName = "AUDIT_TEST_HISTORICAL_CUTOFF",
            WarehouseId = 1,
            CreatedBy = "AUDIT_TEST_AI2",
            IsActive = true
        });
        db.CycleCountSchedules.Add(new CycleCountSchedule
        {
            ScheduleId = 10,
            ProgramId = 10,
            ItemId = 1,
            OwnerPartnerId = 101,
            LocationId = 1,
            AbcClass = 'A',
            LastCountedAt = Cutoff.AddDays(1),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var model = await new InventoryRiskScoringService(db)
            .BuildPageAsync(Query(1, 101, [101]));

        var row = Assert.Single(model.Rows);
        Assert.Null(row.LastApprovedCountAt);
        Assert.Null(row.DaysSinceLastApprovedCount);
    }

    [Fact]
    public void InventoryRiskEndpoints_RequireOperationalRoleAndPermission()
    {
        AssertEndpoint(nameof(ReportsController.InventoryRisk), WmsPermissions.ReportView);
        AssertEndpoint(nameof(ReportsController.InventoryRiskShadowRefresh), WmsPermissions.StockCountApprove);
    }

    private static void AssertEndpoint(string actionName, string requiredPolicy)
    {
        var method = typeof(ReportsController).GetMethod(actionName);
        Assert.NotNull(method);
        var attributes = method!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ToList();
        Assert.Contains(attributes, attribute => attribute.Roles == WmsRoles.InventoryRoles
            || attribute.Roles == WmsRoles.AdminManagerRoles);
        Assert.Contains(attributes, attribute => attribute.Policy == requiredPolicy);
    }

    private static InventoryRiskQuery Query(
        int? warehouseId,
        int? ownerPartnerId,
        IReadOnlyList<int> allowedOwners,
        InventoryRiskDataQualityStatusEnum? dataQualityStatus = null)
        => new()
        {
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerPartnerId,
            AllowedOwnerPartnerIds = allowedOwners,
            DataQualityStatus = dataQualityStatus,
            PredictionCutoff = Cutoff,
            PageSize = 100
        };

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AI2-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options) { SkipAudit = true };
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure
        {
            UomId = 1,
            UomCode = "CAI",
            UomName = "Cái",
            IsActive = true
        });
        db.Partners.AddRange(
            new Partner { PartnerId = 101, PartnerCode = "AUDIT_TEST_OWNER_101", PartnerName = "Chủ hàng kiểm thử 101", IsActive = true },
            new Partner { PartnerId = 202, PartnerCode = "AUDIT_TEST_OWNER_202", PartnerName = "Chủ hàng kiểm thử 202", IsActive = true });
        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 1,
            WarehouseCode = "AUDIT_TEST_AI2_WH",
            WarehouseName = "Kho kiểm thử AI-2",
            IsActive = true
        });
        db.Zones.Add(new Zone
        {
            ZoneId = 1,
            WarehouseId = 1,
            ZoneCode = "AUDIT_TEST_AI2_ZONE",
            ZoneName = "Khu lưu trữ kiểm thử",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        });
        db.Locations.Add(new Location
        {
            LocationId = 1,
            ZoneId = 1,
            LocationCode = "AUDIT_TEST_AI2_BIN",
            IsActive = true
        });
        db.Items.AddRange(
            new Item
            {
                ItemId = 1,
                ItemCode = "AUDIT_TEST_AI2_SKU_101",
                ItemName = "Vật tư kiểm thử AI-2 chủ hàng 101",
                BaseUomId = 1,
                OwnerPartnerId = 101,
                AbcClass = "A",
                IsActive = true
            },
            new Item
            {
                ItemId = 2,
                ItemCode = "AUDIT_TEST_AI2_SKU_202",
                ItemName = "Vật tư kiểm thử AI-2 chủ hàng 202",
                BaseUomId = 1,
                OwnerPartnerId = 202,
                AbcClass = "C",
                IsActive = true
            });
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 1,
                ItemId = 1,
                OwnerPartnerId = 101,
                LocationId = 1,
                Quantity = 40m,
                ReservedQty = 5m,
                UpdatedAt = Cutoff.AddDays(-1)
            },
            new ItemLocation
            {
                ItemLocationId = 2,
                ItemId = 2,
                OwnerPartnerId = 202,
                LocationId = 1,
                Quantity = 20m,
                UpdatedAt = Cutoff.AddDays(-1)
            });
        await db.SaveChangesAsync();
    }
}
