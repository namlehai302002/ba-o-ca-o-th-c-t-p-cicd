using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WMS.Authorization;
using WMS.Common;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public sealed class InventoryRiskRecommendationTests
{
    [Fact]
    public async Task ApprovedRecommendation_ShouldCreateOneBlindSheetWithoutMutatingInventory()
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations
            .Include(row => row.Decisions)
            .SingleAsync();

        Assert.Equal(CycleCountRecommendationStateEnum.PendingReview, recommendation.State);
        Assert.Equal(2, recommendation.Decisions.Count);
        Assert.Equal(
            new[]
            {
                CycleCountRecommendationDecisionTypeEnum.Generated,
                CycleCountRecommendationDecisionTypeEnum.SubmittedForReview
            },
            recommendation.Decisions.OrderBy(row => row.CycleCountRecommendationDecisionId).Select(row => row.DecisionType));

        await service.DecideAsync(
            Decision(recommendation, "APPROVE", "RISK_CONFIRMED"),
            "audit.manager",
            1,
            [101]);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);

        var quantityBefore = await db.ItemLocations.SumAsync(row => row.Quantity);
        var reservedBefore = await db.ItemLocations.SumAsync(row => row.ReservedQty);
        var ledgerBefore = await db.InventoryTransactions.CountAsync();
        var created = await service.MaterializeAsync(
            recommendation.CycleCountRecommendationId,
            recommendation.ConcurrencyToken,
            "audit.manager",
            1,
            [101]);

        Assert.False(created.WasAlreadyCreated);
        Assert.Equal(quantityBefore, await db.ItemLocations.SumAsync(row => row.Quantity));
        Assert.Equal(reservedBefore, await db.ItemLocations.SumAsync(row => row.ReservedQty));
        Assert.Equal(ledgerBefore, await db.InventoryTransactions.CountAsync());
        var sheet = await db.StockCountSheets.Include(row => row.Lines).SingleAsync();
        Assert.Equal(StockCountStatusEnum.Draft, sheet.Status);
        Assert.Equal("audit.manager", sheet.CreatedBy);
        Assert.Contains("blind=True", sheet.Notes, StringComparison.OrdinalIgnoreCase);
        var line = Assert.Single(sheet.Lines);
        Assert.Null(line.CountedQty);
        Assert.Equal(40m, line.SystemQty);

        var replay = await service.MaterializeAsync(
            recommendation.CycleCountRecommendationId,
            Guid.Empty,
            "audit.manager",
            1,
            [101]);
        Assert.True(replay.WasAlreadyCreated);
        Assert.Equal(created.StockCountSheetId, replay.StockCountSheetId);
        Assert.Single(await db.StockCountSheets.ToListAsync());

        Assert.True(await service.SyncSheetStateAsync(sheet.StockCountSheetId, StockCountStatusEnum.Counting, "audit.counter", "STOCK_COUNT_STARTED"));
        await db.SaveChangesAsync();
        Assert.Equal(CycleCountRecommendationStateEnum.InProgress, (await ReloadAsync(db, recommendation.CycleCountRecommendationId)).State);

        Assert.True(await service.SyncSheetStateAsync(sheet.StockCountSheetId, StockCountStatusEnum.Counted, "audit.counter", "STOCK_COUNT_SUBMITTED"));
        await db.SaveChangesAsync();
        Assert.Equal(CycleCountRecommendationStateEnum.PendingVarianceReview, (await ReloadAsync(db, recommendation.CycleCountRecommendationId)).State);

        Assert.True(await service.SyncSheetStateAsync(sheet.StockCountSheetId, StockCountStatusEnum.Approved, "audit.approver", "STOCK_COUNT_APPROVED"));
        await db.SaveChangesAsync();
        var reconciled = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        Assert.Equal(CycleCountRecommendationStateEnum.Reconciled, reconciled.State);
        Assert.Contains(reconciled.Decisions, row => row.ReasonCode == "STOCK_COUNT_APPROVED" && row.Actor == "audit.approver");
    }

    [Fact]
    public async Task InProgressCountTask_ShouldRejectRecommendationModification()
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();

        await service.DecideAsync(
            Decision(recommendation, "APPROVE", "RISK_CONFIRMED"),
            "audit.manager",
            1,
            [101]);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        var materialized = await service.MaterializeAsync(
            recommendation.CycleCountRecommendationId,
            recommendation.ConcurrencyToken,
            "audit.manager",
            1,
            [101]);
        Assert.True(await service.SyncSheetStateAsync(
            materialized.StockCountSheetId,
            StockCountStatusEnum.Counting,
            "audit.counter",
            "STOCK_COUNT_STARTED"));
        await db.SaveChangesAsync();

        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        var decisionCount = recommendation.Decisions.Count;
        var originalEffort = recommendation.EstimatedEffortMinutes;
        var originalAssignee = recommendation.AssignedTo;
        var originalWorkPool = recommendation.WorkPool;
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DecideAsync(
            new InventoryRiskRecommendationDecisionCommand
            {
                RecommendationId = recommendation.CycleCountRecommendationId,
                ConcurrencyToken = recommendation.ConcurrencyToken,
                Action = "MODIFY",
                ReasonCode = "WORKLOAD_ADJUSTMENT",
                Note = "AUDIT_TEST must not modify active work",
                EstimatedEffortMinutes = 120,
                AssignedTo = "audit.other-counter",
                WorkPool = "AUDIT_TEST_OTHER_POOL"
            },
            "audit.manager",
            1,
            [101]));

        Assert.Equal("AI_RECOMMENDATION_STATE_INVALID", exception.Code);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        Assert.Equal(CycleCountRecommendationStateEnum.InProgress, recommendation.State);
        Assert.Equal(originalEffort, recommendation.EstimatedEffortMinutes);
        Assert.Equal(originalAssignee, recommendation.AssignedTo);
        Assert.Equal(originalWorkPool, recommendation.WorkPool);
        Assert.Equal(decisionCount, recommendation.Decisions.Count);
    }

    [Theory]
    [InlineData("MODIFY", "WORKLOAD_ADJUSTMENT", CycleCountRecommendationStateEnum.Modified)]
    [InlineData("REJECT", "LOW_BUSINESS_PRIORITY", CycleCountRecommendationStateEnum.Rejected)]
    public async Task HumanDecision_ShouldPersistStructuredReasonAndImmutableAudit(
        string action,
        string reason,
        CycleCountRecommendationStateEnum expectedState)
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();

        await service.DecideAsync(
            new InventoryRiskRecommendationDecisionCommand
            {
                RecommendationId = recommendation.CycleCountRecommendationId,
                ConcurrencyToken = recommendation.ConcurrencyToken,
                Action = action,
                ReasonCode = reason,
                Note = "AUDIT_TEST quyết định có giải trình",
                EstimatedEffortMinutes = 17,
                AssignedTo = "audit.counter",
                WorkPool = "AUDIT_TEST_POOL"
            },
            "audit.manager",
            1,
            [101]);

        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        Assert.Equal(expectedState, recommendation.State);
        Assert.Equal(reason, recommendation.DecisionReasonCode);
        Assert.Equal("audit.manager", recommendation.ReviewedBy);
        var decision = recommendation.Decisions.OrderByDescending(row => row.DecidedAt).First();
        Assert.Equal(reason, decision.ReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(decision.BeforeJson));
        Assert.False(string.IsNullOrWhiteSpace(decision.AfterJson));
        Assert.NotEqual(decision.BeforeJson, decision.AfterJson);
        if (action == "MODIFY")
        {
            Assert.Equal(17, recommendation.EstimatedEffortMinutes);
            Assert.Equal("audit.counter", recommendation.AssignedTo);
            Assert.Equal("AUDIT_TEST_POOL", recommendation.WorkPool);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.MaterializeAsync(
                recommendation.CycleCountRecommendationId,
                recommendation.ConcurrencyToken,
                "audit.manager",
                1,
                [101]));
            Assert.Equal("AI_RECOMMENDATION_NOT_APPROVED", ex.Code);
        }
    }

    [Fact]
    public async Task LegacyFreshnessWatermark_ShouldRequireRescoringBeforeHumanReview()
    {
        await using var db = CreateDb();
        await SeedWarehouseScopeAsync(db);
        var cutoff = VietnamTime.Now;
        var query = new InventoryRiskQuery
        {
            WarehouseId = 1,
            OwnerPartnerId = 101,
            AllowedOwnerPartnerIds = [101],
            PredictionCutoff = cutoff,
            PageSize = 100
        };
        await new InventoryRiskScoringService(db).PersistShadowBatchAsync(query, "AUDIT_TEST_AI3_SCORER");
        var snapshot = await db.InventoryRiskFeatureSnapshots.SingleAsync();
        snapshot.SourceWatermark = "legacy-watermark";
        await db.SaveChangesAsync();

        var generated = await CreateService(db).GenerateFromLatestBatchAsync(query, "audit.manager");

        Assert.Equal(1, generated.CreatedCount);
        Assert.Equal(1, generated.BlockedByDataQualityCount);
        var recommendation = await db.CycleCountRecommendations
            .Include(row => row.Decisions)
            .SingleAsync();
        Assert.Equal(CycleCountRecommendationStateEnum.BlockedByDataQuality, recommendation.State);
        Assert.Contains(
            recommendation.Decisions,
            row => row.ReasonCode == "FRESHNESS_CONTRACT_RESCORE_REQUIRED");
    }

    [Fact]
    public async Task ChallengerBatch_ShouldNotCreateOperationalRecommendations()
    {
        await using var db = CreateDb();
        await SeedWarehouseScopeAsync(db);
        var query = new InventoryRiskQuery
        {
            WarehouseId = 1,
            OwnerPartnerId = 101,
            AllowedOwnerPartnerIds = [101],
            PredictionCutoff = VietnamTime.Now,
            PageSize = 100
        };
        await new InventoryRiskScoringService(db).PersistShadowBatchAsync(query, "AUDIT_TEST_AI6");
        var version = await db.InventoryRiskModelVersions.SingleAsync();
        version.LifecycleStatus = InventoryRiskModelLifecycleStatusEnum.Challenger;
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService(db).GenerateFromLatestBatchAsync(query, "audit.manager"));

        Assert.Equal("AI_RECOMMENDATION_MODEL_NOT_CHAMPION", exception.Code);
        Assert.Empty(await db.CycleCountRecommendations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ChangedBalance_ShouldInvalidateRecommendationAndRequireRescore()
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();
        var stock = await db.ItemLocations.SingleAsync();
        stock.Quantity += 1m;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DecideAsync(
            Decision(recommendation, "APPROVE", "RISK_CONFIRMED"),
            "audit.manager",
            1,
            [101]));

        Assert.Equal("AI_RECOMMENDATION_STALE", ex.Code);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        Assert.Equal(CycleCountRecommendationStateEnum.Invalidated, recommendation.State);
        Assert.Contains(recommendation.Decisions, row => row.ReasonCode == "INVENTORY_CHANGED_AFTER_SCORING");
        Assert.Empty(await db.StockCountSheets.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedReservationOrHold_ShouldInvalidateRecommendationAndRequireRescore(bool changeReservation)
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();
        var stock = await db.ItemLocations.SingleAsync();
        if (changeReservation)
            stock.ReservedQty += 1m;
        else
            stock.HoldStatus = InventoryHoldStatusEnum.QcHold;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DecideAsync(
            Decision(recommendation, "APPROVE", "RISK_CONFIRMED"),
            "audit.manager",
            1,
            [101]));

        Assert.Equal("AI_RECOMMENDATION_STALE", ex.Code);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        Assert.Equal(CycleCountRecommendationStateEnum.Invalidated, recommendation.State);
    }

    [Fact]
    public async Task ScopeAndConcurrency_ShouldRejectUnauthorizedOrReplayedDecision()
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();
        var command = Decision(recommendation, "APPROVE", "RISK_CONFIRMED");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DecideAsync(
            command,
            "audit.manager",
            1,
            [202]));
        await service.DecideAsync(command, "audit.manager", 1, [101]);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.DecideAsync(
            command,
            "audit.manager.second",
            1,
            [101]));
        Assert.Equal(CycleCountRecommendationStateEnum.Approved, (await ReloadAsync(db, recommendation.CycleCountRecommendationId)).State);
    }

    [Fact]
    public async Task ActiveCountForSameScope_ShouldBlockRecommendationMaterialization()
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();
        await service.DecideAsync(
            Decision(recommendation, "APPROVE", "RISK_CONFIRMED"),
            "audit.manager",
            1,
            [101]);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);

        var existing = new StockCountSheet
        {
            SheetCode = "AUDIT_TEST_ACTIVE_COUNT",
            WarehouseId = 1,
            CountDate = VietnamTime.Today,
            Status = StockCountStatusEnum.Counting,
            CreatedBy = "audit.counter"
        };
        existing.Lines.Add(new StockCountLine
        {
            ItemId = 1,
            OwnerPartnerId = 101,
            LocationId = 1,
            SystemQty = 40m
        });
        db.StockCountSheets.Add(existing);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.MaterializeAsync(
            recommendation.CycleCountRecommendationId,
            recommendation.ConcurrencyToken,
            "audit.manager",
            1,
            [101]));

        Assert.Equal("AI_COUNT_ACTIVE_DUPLICATE", ex.Code);
        Assert.Single(await db.StockCountSheets.ToListAsync());
        Assert.Null((await ReloadAsync(db, recommendation.CycleCountRecommendationId)).StockCountSheetId);
    }

    [Fact]
    public async Task ActiveCountForDifferentLot_ShouldNotBlockRecommendationMaterialization()
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();
        await service.DecideAsync(
            Decision(recommendation, "APPROVE", "RISK_CONFIRMED"),
            "audit.manager",
            1,
            [101]);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);

        var existing = new StockCountSheet
        {
            SheetCode = "AUDIT_TEST_OTHER_LOT_COUNT",
            WarehouseId = 1,
            CountDate = VietnamTime.Today,
            Status = StockCountStatusEnum.Counting,
            CreatedBy = "audit.counter"
        };
        existing.Lines.Add(new StockCountLine
        {
            ItemId = 1,
            OwnerPartnerId = 101,
            LocationId = 1,
            LotNumber = "AUDIT_TEST_OTHER_LOT",
            SystemQty = 1m
        });
        db.StockCountSheets.Add(existing);
        await db.SaveChangesAsync();

        var result = await service.MaterializeAsync(
            recommendation.CycleCountRecommendationId,
            recommendation.ConcurrencyToken,
            "audit.manager",
            1,
            [101]);

        Assert.False(result.WasAlreadyCreated);
        Assert.Equal(2, await db.StockCountSheets.CountAsync());
        Assert.Null((await db.StockCountLines.SingleAsync(line => line.StockCountSheetId == result.StockCountSheetId)).LotNumber);
    }

    [Fact]
    public async Task ExpiredPendingRecommendation_ShouldUseEffectiveStateInFilterAndCounters()
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();
        recommendation.FreshUntil = VietnamTime.Now.AddMinutes(-1);
        await db.SaveChangesAsync();

        var pending = await service.BuildPageAsync(new InventoryRiskRecommendationQuery
        {
            WarehouseId = 1,
            OwnerPartnerId = 101,
            AllowedOwnerPartnerIds = [101],
            State = CycleCountRecommendationStateEnum.PendingReview
        });
        Assert.Empty(pending.Rows);
        Assert.Equal(0, pending.PendingReviewCount);
        Assert.Equal(1, pending.BlockedCount);

        var expired = await service.BuildPageAsync(new InventoryRiskRecommendationQuery
        {
            WarehouseId = 1,
            OwnerPartnerId = 101,
            AllowedOwnerPartnerIds = [101],
            State = CycleCountRecommendationStateEnum.Expired
        });
        var row = Assert.Single(expired.Rows);
        Assert.Equal(CycleCountRecommendationStateEnum.Expired, row.State);
    }

    [Fact]
    public async Task InvalidWorkflowJump_ShouldBeRejectedByStateMachine()
    {
        await using var db = CreateDb();
        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.SingleAsync();
        await service.DecideAsync(
            Decision(recommendation, "APPROVE", "RISK_CONFIRMED"),
            "audit.manager",
            1,
            [101]);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        var materialized = await service.MaterializeAsync(
            recommendation.CycleCountRecommendationId,
            recommendation.ConcurrencyToken,
            "audit.manager",
            1,
            [101]);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SyncSheetStateAsync(
            materialized.StockCountSheetId,
            StockCountStatusEnum.Approved,
            "audit.approver",
            "STOCK_COUNT_APPROVED"));

        Assert.Equal("AI_RECOMMENDATION_TRANSITION_INVALID", ex.Code);
        Assert.Equal(CycleCountRecommendationStateEnum.CountSheetCreated, (await ReloadAsync(db, recommendation.CycleCountRecommendationId)).State);
    }

    [Fact]
    public void RecommendationEndpoints_ShouldRequireManagerRoleAndApprovalPermission()
    {
        foreach (var actionName in new[]
                 {
                     nameof(ReportsController.InventoryRiskRecommendations),
                     nameof(ReportsController.InventoryRiskGenerateRecommendations),
                     nameof(ReportsController.InventoryRiskRecommendationDecision),
                     nameof(ReportsController.InventoryRiskCreateCountSheet)
                 })
        {
            var method = typeof(ReportsController).GetMethod(actionName);
            Assert.NotNull(method);
            var attributes = method!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>()
                .ToList();
            Assert.Contains(attributes, attribute => attribute.Roles == WmsRoles.AdminManagerRoles);
            Assert.Contains(attributes, attribute => attribute.Policy == WmsPermissions.StockCountApprove);
        }
    }

    [Fact]
    public async Task SqliteRelationalWorkflow_ShouldEnforceUniquePredictionAndIdempotentMaterialization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);

        var service = await SeedScoredRecommendationAsync(db);
        var recommendation = await db.CycleCountRecommendations.AsNoTracking().SingleAsync();
        db.ChangeTracker.Clear();
        db.CycleCountRecommendations.Add(new CycleCountRecommendation
        {
            InventoryRiskPredictionId = recommendation.InventoryRiskPredictionId,
            WarehouseId = recommendation.WarehouseId,
            OwnerPartnerId = recommendation.OwnerPartnerId,
            ItemId = recommendation.ItemId,
            LocationId = recommendation.LocationId,
            ScopeKey = recommendation.ScopeKey,
            SnapshotWatermark = recommendation.SnapshotWatermark,
            PredictionCutoff = recommendation.PredictionCutoff,
            GeneratedAt = recommendation.GeneratedAt,
            FreshUntil = recommendation.FreshUntil,
            CreatedBy = "AUDIT_TEST_DUPLICATE",
            CreatedAt = VietnamTime.Now,
            UpdatedAt = VietnamTime.Now,
            ConcurrencyToken = Guid.NewGuid()
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        recommendation = await db.CycleCountRecommendations.SingleAsync();
        await service.DecideAsync(
            Decision(recommendation, "APPROVE", "RISK_CONFIRMED"),
            "audit.manager",
            1,
            [101]);
        recommendation = await ReloadAsync(db, recommendation.CycleCountRecommendationId);
        var first = await service.MaterializeAsync(
            recommendation.CycleCountRecommendationId,
            recommendation.ConcurrencyToken,
            "audit.manager",
            1,
            [101]);
        var replay = await service.MaterializeAsync(
            recommendation.CycleCountRecommendationId,
            Guid.Empty,
            "audit.manager",
            1,
            [101]);

        Assert.False(first.WasAlreadyCreated);
        Assert.True(replay.WasAlreadyCreated);
        Assert.Equal(first.StockCountSheetId, replay.StockCountSheetId);
        Assert.Equal(1, await db.StockCountSheets.CountAsync());
        Assert.Equal(1, await db.CycleCountRecommendations.CountAsync());
    }

    private static InventoryRiskRecommendationDecisionCommand Decision(
        CycleCountRecommendation recommendation,
        string action,
        string reason)
        => new()
        {
            RecommendationId = recommendation.CycleCountRecommendationId,
            ConcurrencyToken = recommendation.ConcurrencyToken,
            Action = action,
            ReasonCode = reason,
            Note = "AUDIT_TEST quyết định đã được xem xét"
        };

    private static InventoryRiskRecommendationService CreateService(AppDbContext db)
    {
        var unitOfWork = new EfUnitOfWork(db);
        return new InventoryRiskRecommendationService(
            db,
            unitOfWork,
            new CycleCountPlanningService(db, unitOfWork));
    }

    private static async Task<InventoryRiskRecommendationService> SeedScoredRecommendationAsync(AppDbContext db)
    {
        await SeedWarehouseScopeAsync(db);
        var cutoff = VietnamTime.Now;
        var query = new InventoryRiskQuery
        {
            WarehouseId = 1,
            OwnerPartnerId = 101,
            AllowedOwnerPartnerIds = [101],
            PredictionCutoff = cutoff,
            PageSize = 100
        };
        await new InventoryRiskScoringService(db).PersistShadowBatchAsync(query, "AUDIT_TEST_AI3_SCORER");
        var service = CreateService(db);
        var generated = await service.GenerateFromLatestBatchAsync(query, "audit.manager");
        Assert.Equal(1, generated.CreatedCount);
        return service;
    }

    private static async Task SeedWarehouseScopeAsync(AppDbContext db)
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
            WarehouseCode = "AUDIT_TEST_AI3_WH",
            WarehouseName = "Kho kiểm thử AI-3",
            IsActive = true
        });
        db.Zones.Add(new Zone
        {
            ZoneId = 1,
            WarehouseId = 1,
            ZoneCode = "AUDIT_TEST_AI3_ZONE",
            ZoneName = "Khu kiểm thử AI-3",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        });
        db.Locations.Add(new Location
        {
            LocationId = 1,
            ZoneId = 1,
            LocationCode = "AUDIT_TEST_AI3_BIN",
            IsActive = true
        });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "AUDIT_TEST_AI3_SKU",
            ItemName = "Vật tư kiểm thử AI-3",
            BaseUomId = 1,
            OwnerPartnerId = 101,
            CurrentStock = 40m,
            AbcClass = "A",
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            OwnerPartnerId = 101,
            LocationId = 1,
            Quantity = 40m,
            ReservedQty = 2m,
            HoldStatus = InventoryHoldStatusEnum.Available,
            UpdatedAt = VietnamTime.Now.AddDays(-1)
        });
        await db.SaveChangesAsync();
    }

    private static async Task<CycleCountRecommendation> ReloadAsync(AppDbContext db, long id)
    {
        db.ChangeTracker.Clear();
        return await db.CycleCountRecommendations
            .Include(row => row.Decisions)
            .Include(row => row.StockCountSheet)
            .Include(row => row.Prediction).ThenInclude(row => row.ModelVersion)
            .SingleAsync(row => row.CycleCountRecommendationId == id);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AI3-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options) { SkipAudit = true };
    }

    private static async Task CreateSqliteSchemaAsync(AppDbContext db)
    {
        var script = db.Database.GenerateCreateScript()
            .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
            .Replace("varchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
            .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
            .Replace("\"RowVersion\" BLOB NOT NULL", "\"RowVersion\" BLOB NOT NULL DEFAULT X''", StringComparison.OrdinalIgnoreCase);
        var commands = script
            .Split(new[] { ";\r\n", ";\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(command => command.Trim())
            .Where(command => command.Length > 0 && !command.StartsWith("INSERT INTO ", StringComparison.OrdinalIgnoreCase));

        foreach (var command in commands)
        {
            await using var dbCommand = db.Database.GetDbConnection().CreateCommand();
            dbCommand.CommandText = command;
            await dbCommand.ExecuteNonQueryAsync();
        }
    }
}
