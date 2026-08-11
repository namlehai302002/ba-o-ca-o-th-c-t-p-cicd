using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public sealed class InventoryRiskDatasetTests
{
    private static readonly DateTime Cutoff = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);
    private static readonly JsonSerializerOptions FeatureJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public async Task BuildAsync_ShouldUseImmutableSnapshotAndFirstEligibleApprovedOutcome()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        await AddSnapshotAsync(db, Cutoff, onHandQty: 100m, ruleScore: 88m);
        await AddApprovedOutcomeAsync(db, 1, Cutoff.AddDays(10), 100m, 95m, withAdjustment: true);
        await AddApprovedOutcomeAsync(db, 2, Cutoff.AddDays(20), 95m, 95m, withAdjustment: false);
        var service = new InventoryRiskDatasetService(db);
        var query = Query(Cutoff.AddDays(40));

        var first = await service.BuildAsync(query);
        var row = Assert.Single(first.Rows);

        Assert.Equal(1, row.StockCountSheetId);
        Assert.Equal(100m, row.Features.OnHandBaseQty);
        Assert.Equal(95m, row.CountedBaseQty);
        Assert.Equal(-5m, row.VarianceBaseQty);
        Assert.True(row.HasQuantityVariance);
        Assert.Null(row.HasMaterialVariance);
        Assert.Equal("UNKNOWN_THRESHOLD_SNAPSHOT_MISSING", row.MaterialVarianceStatus);
        Assert.Equal(88m, row.RuleRiskScore);

        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 999,
            ItemId = 1,
            OwnerPartnerId = 101,
            LocationId = 1,
            Quantity = 999m,
            UpdatedAt = Cutoff.AddDays(30)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var replay = await service.BuildAsync(query);
        Assert.Equal(first.DatasetHash, replay.DatasetHash);
        Assert.Equal(first.SourceWatermark, replay.SourceWatermark);
        Assert.Equal(100m, Assert.Single(replay.Rows).Features.OnHandBaseQty);
    }

    [Fact]
    public async Task BuildAsync_ShouldExcludeNonFinalAndUnreconciledOutcomes()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        await AddSnapshotAsync(db, Cutoff, onHandQty: 100m, ruleScore: 55m);

        await AddOutcomeAsync(db, 10, Cutoff.AddDays(3), StockCountStatusEnum.Counted, 2, 100m, 100m, 0m);
        await AddOutcomeAsync(db, 11, Cutoff.AddDays(4), StockCountStatusEnum.Approved, 1, 100m, 100m, 0m);
        await AddOutcomeAsync(db, 12, Cutoff.AddDays(5), StockCountStatusEnum.Approved, 2, 100m, 99m, -2m);
        await AddOutcomeAsync(db, 13, Cutoff.AddDays(6), StockCountStatusEnum.Approved, 2, 100m, 99m, -1m);
        await AddOutcomeAsync(db, 14, Cutoff.AddDays(7), StockCountStatusEnum.Approved, 2, 100m, 100m, 0m, unlockedAfterApproval: true);
        await AddApprovedOutcomeAsync(db, 15, Cutoff.AddDays(8), 100m, 100m, withAdjustment: false);

        var result = await new InventoryRiskDatasetService(db).BuildAsync(Query(Cutoff.AddDays(40)));

        var row = Assert.Single(result.Rows);
        Assert.Equal(15, row.StockCountSheetId);
        Assert.False(row.HasQuantityVariance);
        // The Counted sheet is rejected by the approved-only SQL allowlist; the approved
        // sheet with a non-final line status is rejected by the in-memory provenance guard.
        Assert.Equal(1, result.Exclusions["OUTCOME_NOT_APPROVED_FINAL"]);
        Assert.Equal(1, result.Exclusions["OUTCOME_VARIANCE_NOT_RECONCILED"]);
        Assert.Equal(1, result.Exclusions["OUTCOME_ADJUSTMENT_LEDGER_NOT_RECONCILED"]);
        Assert.Equal(1, result.Exclusions["OUTCOME_APPROVAL_TIMELINE_INVALID"]);
    }

    [Fact]
    public async Task BuildAsync_ShouldEnforceFeatureHashSchemaAndExactOwnerLotExpiryGrain()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db, trackLot: true, trackExpiry: true);
        var expiry = new DateTime(2027, 1, 1);
        await AddSnapshotAsync(db, Cutoff, 50m, 70m, lotNumber: "LOT-A", expiryDate: expiry);
        await AddApprovedOutcomeAsync(db, 20, Cutoff.AddDays(5), 50m, 40m, true, ownerPartnerId: null, lotNumber: "LOT-A", expiryDate: expiry);
        await AddApprovedOutcomeAsync(db, 21, Cutoff.AddDays(6), 50m, 40m, true, ownerPartnerId: 101, lotNumber: "LOT-B", expiryDate: expiry);
        await AddApprovedOutcomeAsync(db, 22, Cutoff.AddDays(7), 50m, 50m, false, ownerPartnerId: 101, lotNumber: "LOT-A", expiryDate: expiry);

        var valid = await new InventoryRiskDatasetService(db).BuildAsync(Query(Cutoff.AddDays(40)));
        Assert.Equal(22, Assert.Single(valid.Rows).StockCountSheetId);

        var snapshot = await db.InventoryRiskFeatureSnapshots.SingleAsync();
        snapshot.FeatureJson = snapshot.FeatureJson.TrimEnd('}') + ",\"countedQty\":50}";
        snapshot.FeatureHash = Hash(snapshot.FeatureJson);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var leaked = await new InventoryRiskDatasetService(db).BuildAsync(Query(Cutoff.AddDays(40)));
        Assert.Empty(leaked.Rows);
        Assert.Equal(1, leaked.Exclusions["UNSUPPORTED_FEATURE_PROPERTY"]);
    }

    [Fact]
    public async Task BuildAsync_ShouldRequireMatureOutcomeHorizonAndRemainDeterministic()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        await AddSnapshotAsync(db, Cutoff, 20m, 30m);
        await AddApprovedOutcomeAsync(db, 30, Cutoff.AddDays(10), 20m, 20m, false);

        var service = new InventoryRiskDatasetService(db);
        var immature = await service.BuildAsync(Query(Cutoff.AddDays(15)));
        var mature = await service.BuildAsync(Query(Cutoff.AddDays(40)));
        var replay = await service.BuildAsync(Query(Cutoff.AddDays(40)));

        Assert.Empty(immature.Rows);
        Assert.Equal(1, immature.Exclusions["OUTCOME_HORIZON_NOT_MATURE"]);
        Assert.Single(mature.Rows);
        Assert.Equal(mature.DatasetHash, replay.DatasetHash);
        Assert.Equal(mature.SourceWatermark, replay.SourceWatermark);
    }

    [Fact]
    public async Task BuildAsync_ShouldAllowPersistenceGraceThenRejectBackfilledAndIncompleteFeatureSnapshots()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        await AddSnapshotAsync(db, Cutoff, 20m, 30m);
        await AddApprovedOutcomeAsync(db, 35, Cutoff.AddDays(5), 20m, 20m, false);
        var snapshot = await db.InventoryRiskFeatureSnapshots.SingleAsync();
        snapshot.CreatedAt = Cutoff.AddMinutes(1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var withinPersistenceGrace = await new InventoryRiskDatasetService(db).BuildAsync(Query(Cutoff.AddDays(40)));

        Assert.Single(withinPersistenceGrace.Rows);

        snapshot = await db.InventoryRiskFeatureSnapshots.SingleAsync();
        snapshot.CreatedAt = Cutoff.AddMinutes(6);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var backfilled = await new InventoryRiskDatasetService(db).BuildAsync(Query(Cutoff.AddDays(40)));

        Assert.Empty(backfilled.Rows);
        Assert.Equal(1, backfilled.Exclusions["SNAPSHOT_BACKFILLED_AFTER_CUTOFF"]);

        snapshot = await db.InventoryRiskFeatureSnapshots.SingleAsync();
        snapshot.CreatedAt = Cutoff;
        snapshot.FeatureJson = "{\"onHandBaseQty\":20}";
        snapshot.FeatureHash = Hash(snapshot.FeatureJson);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var incomplete = await new InventoryRiskDatasetService(db).BuildAsync(Query(Cutoff.AddDays(40)));
        var otherScopeQuery = new InventoryRiskDatasetQuery
        {
            BuildAsOf = Cutoff.AddDays(40),
            OutcomeHorizonDays = 30,
            IncludeIsolatedTestData = true,
            AllowedWarehouseIds = new[] { 2 },
            Seed = 20260716
        };
        var otherScope = await new InventoryRiskDatasetService(db).BuildAsync(otherScopeQuery);

        Assert.Empty(incomplete.Rows);
        Assert.Equal(1, incomplete.Exclusions["MISSING_FEATURE_PROPERTY"]);
        Assert.NotEqual(incomplete.DatasetHash, otherScope.DatasetHash);
    }

    [Fact]
    public async Task BuildAsync_OnSqlite_ShouldRemainRelationalAndInventoryReadOnly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);
        await SeedScopeAsync(db);
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            OwnerPartnerId = 101,
            LocationId = 1,
            Quantity = 25m,
            ReservedQty = 2m,
            UpdatedAt = Cutoff
        });
        await db.SaveChangesAsync();
        await AddSnapshotAsync(db, Cutoff, 25m, 42m);
        await AddApprovedOutcomeAsync(db, 40, Cutoff.AddDays(5), 25m, 25m, false);
        var quantityBefore = (await db.ItemLocations
            .Select(row => row.Quantity)
            .ToListAsync())
            .Sum();
        var ledgerBefore = await db.InventoryTransactions.CountAsync();
        var sheetsBefore = await db.StockCountSheets.CountAsync();

        var result = await new InventoryRiskDatasetService(db).BuildAsync(Query(Cutoff.AddDays(40)));

        Assert.Single(result.Rows);
        var quantityAfter = (await db.ItemLocations
            .Select(row => row.Quantity)
            .ToListAsync())
            .Sum();
        Assert.Equal(quantityBefore, quantityAfter);
        Assert.Equal(ledgerBefore, await db.InventoryTransactions.CountAsync());
        Assert.Equal(sheetsBefore, await db.StockCountSheets.CountAsync());
        Assert.DoesNotContain(
            db.ChangeTracker.Entries(),
            entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task BuildAsync_ShouldUseAnApprovedCountOutcomeOnlyOnce()
    {
        await using var db = CreateDb();
        await SeedScopeAsync(db);
        await AddSnapshotAsync(db, Cutoff, 100m, 40m);
        await AddSnapshotAsync(db, Cutoff.AddDays(1), 100m, 50m);
        await AddApprovedOutcomeAsync(db, 45, Cutoff.AddDays(10), 100m, 100m, false);

        var result = await new InventoryRiskDatasetService(db).BuildAsync(Query(Cutoff.AddDays(45)));

        var row = Assert.Single(result.Rows);
        Assert.Equal(Cutoff.AddDays(1), row.PredictionCutoff);
        Assert.Equal(1, result.Exclusions["OUTCOME_REUSED_BY_MULTIPLE_SNAPSHOTS"]);
    }

    [Fact]
    public async Task BuildAsync_ShouldExcludeDemoAndSerialTrackedRowsFromPromotionData()
    {
        await using var demoDb = CreateDb();
        await SeedScopeAsync(demoDb);
        (await demoDb.Warehouses.SingleAsync()).WarehouseCode = "DEMO-AI4-WH";
        await demoDb.SaveChangesAsync();
        await AddSnapshotAsync(demoDb, Cutoff, 10m, 60m);
        await AddApprovedOutcomeAsync(demoDb, 46, Cutoff.AddDays(5), 10m, 10m, false);

        var defaultResult = await new InventoryRiskDatasetService(demoDb).BuildAsync(Query(Cutoff.AddDays(40)));
        var includeDemoQuery = new InventoryRiskDatasetQuery
        {
            BuildAsOf = Cutoff.AddDays(40),
            OutcomeHorizonDays = 30,
            IncludeIsolatedTestData = true,
            IncludeDemoData = true,
            AllowedWarehouseIds = new[] { 1 },
            AllowedOwnerPartnerIds = new[] { 101 },
            Seed = 20260716
        };
        var includedResult = await new InventoryRiskDatasetService(demoDb).BuildAsync(includeDemoQuery);

        Assert.Empty(defaultResult.Rows);
        Assert.Equal(1, defaultResult.Exclusions["DEMO_DATA_EXCLUDED"]);
        Assert.True(Assert.Single(includedResult.Rows).IsDemoData);
        Assert.Contains("DEMO_DATA_INCLUDED_NON_PROMOTABLE", includedResult.ReadinessCodes);

        await using var serialDb = CreateDb();
        await SeedScopeAsync(serialDb, trackSerial: true);
        await AddSnapshotAsync(serialDb, Cutoff, 10m, 60m, serialTracking: true);
        await AddApprovedOutcomeAsync(serialDb, 47, Cutoff.AddDays(5), 10m, 10m, false);

        var serialResult = await new InventoryRiskDatasetService(serialDb).BuildAsync(Query(Cutoff.AddDays(40)));

        Assert.Empty(serialResult.Rows);
        Assert.Equal(1, serialResult.Exclusions["SERIAL_TRACKED_OUTCOME_COVERAGE_UNAVAILABLE"]);
    }

    [Fact]
    public async Task BuildAsync_ShouldRejectPreCutoffCountAndLedgerMismatch()
    {
        await using var timingDb = CreateDb();
        await SeedScopeAsync(timingDb);
        await AddSnapshotAsync(timingDb, Cutoff, 20m, 30m);
        await AddOutcomeAsync(
            timingDb,
            48,
            Cutoff.AddDays(5),
            StockCountStatusEnum.Approved,
            2,
            20m,
            20m,
            0m,
            countedAt: Cutoff.AddMinutes(-1));

        var timingResult = await new InventoryRiskDatasetService(timingDb).BuildAsync(Query(Cutoff.AddDays(40)));

        Assert.Empty(timingResult.Rows);
        Assert.Equal(1, timingResult.Exclusions["APPROVED_OUTCOME_NOT_FOUND"]);

        await using var ledgerDb = CreateDb();
        await SeedScopeAsync(ledgerDb);
        await AddSnapshotAsync(ledgerDb, Cutoff, 20m, 30m);
        await AddApprovedOutcomeAsync(ledgerDb, 49, Cutoff.AddDays(5), 20m, 18m, true);
        var ledger = await ledgerDb.InventoryTransactions.SingleAsync();
        ledger.QuantityDelta = -1m;
        await ledgerDb.SaveChangesAsync();

        var ledgerResult = await new InventoryRiskDatasetService(ledgerDb).BuildAsync(Query(Cutoff.AddDays(40)));

        Assert.Empty(ledgerResult.Rows);
        Assert.Equal(1, ledgerResult.Exclusions["OUTCOME_ADJUSTMENT_LEDGER_NOT_RECONCILED"]);
    }

    [Fact]
    public void TemporalSplit_ShouldEnforceEmbargoAndPurgeEntityOverlapDeterministically()
    {
        var rows = new List<InventoryRiskDatasetRow>();
        rows.AddRange(CreatePartitionRows("TR", new DateTime(2025, 1, 10), 120));
        rows.AddRange(CreatePartitionRows("VA", new DateTime(2025, 5, 10), 120));
        rows.AddRange(CreatePartitionRows("TE", new DateTime(2025, 9, 10), 120));
        rows.Add(CreateRow("SHARED-TRAIN", "SHARED", new DateTime(2025, 1, 15), true, 90m));
        rows.Add(CreateRow("SHARED-VALID", "SHARED", new DateTime(2025, 5, 15), false, 50m));
        rows.Add(CreateRow("SHARED-TEST", "SHARED", new DateTime(2025, 9, 15), true, 80m));
        rows.Add(CreateRow("EMBARGO", "EMBARGO", new DateTime(2025, 3, 15), false, 10m));
        var config = new InventoryRiskTemporalSplitConfiguration
        {
            TrainEnd = new DateTime(2025, 1, 31),
            ValidationStart = new DateTime(2025, 5, 1),
            ValidationEnd = new DateTime(2025, 5, 31),
            TestStart = new DateTime(2025, 9, 1),
            TestEnd = new DateTime(2025, 9, 30),
            OutcomeHorizonDays = 90
        };
        var service = new InventoryRiskTemporalSplitService();

        var first = service.Split(rows, config);
        var replay = service.Split(rows.AsEnumerable().Reverse().ToArray(), config);

        Assert.Equal(InventoryRiskExperimentStatus.Ready, first.Status);
        Assert.Equal(2, first.PurgedEntityOverlapCount);
        Assert.Equal(1, first.EmbargoExcludedCount);
        Assert.DoesNotContain(first.TrainRows.Select(row => row.EntityKeyHash), first.ValidationRows.Select(row => row.EntityKeyHash).Contains);
        Assert.DoesNotContain(first.TrainRows.Select(row => row.EntityKeyHash), first.TestRows.Select(row => row.EntityKeyHash).Contains);
        Assert.DoesNotContain(first.ValidationRows.Select(row => row.EntityKeyHash), first.TestRows.Select(row => row.EntityKeyHash).Contains);
        Assert.Contains(first.TestRows, row => row.SampleKey == "SHARED-TEST");
        Assert.Equal(first.SplitHash, replay.SplitHash);
    }

    [Fact]
    public void TemporalSplit_ShouldBlockShortEmbargo()
    {
        var config = new InventoryRiskTemporalSplitConfiguration
        {
            TrainEnd = new DateTime(2025, 1, 31),
            ValidationStart = new DateTime(2025, 2, 1),
            ValidationEnd = new DateTime(2025, 2, 28),
            TestStart = new DateTime(2025, 3, 1),
            TestEnd = new DateTime(2025, 3, 31),
            OutcomeHorizonDays = 30
        };

        var result = new InventoryRiskTemporalSplitService().Split(Array.Empty<InventoryRiskDatasetRow>(), config);

        Assert.Equal(InventoryRiskExperimentStatus.BlockedConfiguration, result.Status);
        Assert.Contains("TRAIN_VALIDATION_EMBARGO_TOO_SHORT", result.ReadinessCodes);
        Assert.Contains("VALIDATION_TEST_EMBARGO_TOO_SHORT", result.ReadinessCodes);
    }

    [Fact]
    public void TemporalSplit_ShouldBlockLabelsThatCrossTheNextPartitionBoundary()
    {
        var rows = new List<InventoryRiskDatasetRow>();
        rows.AddRange(CreatePartitionRows("TR", new DateTime(2025, 1, 10), 120));
        rows.AddRange(CreatePartitionRows("VA", new DateTime(2025, 5, 10), 120));
        rows.AddRange(CreatePartitionRows("TE", new DateTime(2025, 9, 10), 120));
        rows.Add(CreateRow(
            "TRAIN-LATE-LABEL",
            "TRAIN-LATE-LABEL",
            new DateTime(2025, 1, 15),
            true,
            99m,
            outcomeApprovedAt: new DateTime(2025, 5, 1)));
        var config = new InventoryRiskTemporalSplitConfiguration
        {
            TrainEnd = new DateTime(2025, 1, 31),
            ValidationStart = new DateTime(2025, 5, 1),
            ValidationEnd = new DateTime(2025, 5, 31),
            TestStart = new DateTime(2025, 9, 1),
            TestEnd = new DateTime(2025, 9, 30),
            OutcomeHorizonDays = 90
        };

        var result = new InventoryRiskTemporalSplitService().Split(rows, config);

        Assert.Equal(InventoryRiskExperimentStatus.BlockedData, result.Status);
        Assert.Contains("TRAIN_LABEL_CROSSES_VALIDATION_BOUNDARY", result.ReadinessCodes);
    }

    [Fact]
    public void Benchmark_ShouldReportHandCalculatedRankingMetricsAndStableHash()
    {
        var rows = Enumerable.Range(0, 120)
            .Select(index => CreateRow(
                $"S-{index:D3}",
                $"E-{index:D3}",
                new DateTime(2025, 9, 1).AddMinutes(index),
                index < 12,
                120m - index))
            .ToArray();
        var service = new InventoryRiskBenchmarkService();

        var first = service.Evaluate(rows, 20260716);
        var replay = service.Evaluate(rows.Reverse().ToArray(), 20260716);

        Assert.Equal(InventoryRiskExperimentStatus.BaselineOnly, first.Status);
        Assert.Contains("MODEL_CANDIDATE_NOT_PROVIDED", first.ReadinessCodes);
        Assert.Equal(
            new[] { "ABC_DUE", "RANDOM", "RULE_BASELINE" },
            first.Candidates
                .Select(candidate => candidate.CandidateName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        Assert.All(first.Candidates, candidate =>
        {
            Assert.Equal(120, candidate.EligibleRowCount);
            Assert.Equal(120, candidate.ScoredRowCount);
            Assert.Equal(1m, candidate.Coverage);
            Assert.Equal(new[] { 10, 50, 100 }, candidate.TopK.Select(metric => metric.RequestedK).ToArray());
        });
        var rule = Assert.Single(first.Candidates, candidate => candidate.CandidateName == "RULE_BASELINE");
        Assert.Equal(1m, rule.PrAucAveragePrecision);
        var top10 = Assert.Single(rule.TopK, metric => metric.RequestedK == 10);
        Assert.True(top10.IsAvailable);
        Assert.Equal(10, top10.PositiveDetected);
        Assert.Equal(1m, top10.Precision);
        Assert.Equal(10m / 12m, top10.Recall);
        Assert.Equal(10m, top10.Lift);
        Assert.Equal(10m, top10.DetectedAbsoluteVarianceBaseQty);
        Assert.Equal(1m, top10.InspectionPerDetectedVariance);
        Assert.Equal(50, top10.EstimatedEffortMinutes);
        Assert.Equal(first.BenchmarkHash, replay.BenchmarkHash);
    }

    [Fact]
    public void Benchmark_ShouldNotManufactureMetricsForSmallSingleClassData()
    {
        var row = CreateRow("ONLY", "ONLY", new DateTime(2025, 9, 1), false, 10m);

        var result = new InventoryRiskBenchmarkService().Evaluate(new[] { row }, 7);

        Assert.Equal(InventoryRiskExperimentStatus.BlockedData, result.Status);
        Assert.Contains("TEST_NO_POSITIVE_CLASS", result.ReadinessCodes);
        Assert.Contains("TEST_ROWS_BELOW_K_100", result.ReadinessCodes);
        Assert.All(result.Candidates, candidate => Assert.All(candidate.TopK, metric => Assert.False(metric.IsAvailable)));
    }

    [Fact]
    public void Benchmark_ShouldRequireCompleteModelCoverageBeforeReady()
    {
        var rows = Enumerable.Range(0, 20)
            .Select(index => CreateRow(
                $"MODEL-{index:D2}",
                $"MODEL-{index:D2}",
                new DateTime(2025, 9, 1).AddMinutes(index),
                index < 4,
                20m - index))
            .ToArray();
        var missingScores = new InventoryRiskBenchmarkCandidate
        {
            Name = "ML_CANDIDATE",
            Version = "1.0.0",
            IsModelCandidate = true,
            ScoresBySampleKey = new Dictionary<string, decimal?>()
        };

        var blocked = new InventoryRiskBenchmarkService().Evaluate(rows, 7, new[] { missingScores }, new[] { 5 });

        Assert.Equal(InventoryRiskExperimentStatus.BaselineOnly, blocked.Status);
        Assert.Contains("MODEL_CANDIDATE_NOT_EVALUATION_READY", blocked.ReadinessCodes);

        var completeScores = new InventoryRiskBenchmarkCandidate
        {
            Name = "ML_CANDIDATE",
            Version = "1.0.0",
            IsModelCandidate = true,
            ScoresBySampleKey = rows.ToDictionary(row => row.SampleKey, row => row.RuleRiskScore, StringComparer.Ordinal)
        };
        var ready = new InventoryRiskBenchmarkService().Evaluate(rows, 7, new[] { completeScores }, new[] { 5 });

        Assert.Equal(InventoryRiskExperimentStatus.Ready, ready.Status);
        Assert.DoesNotContain("MODEL_CANDIDATE_NOT_EVALUATION_READY", ready.ReadinessCodes);
    }

    [Fact]
    public void Benchmark_ShouldSuppressQuantityAcrossMixedOrUnknownUom()
    {
        var rows = Enumerable.Range(0, 10)
            .Select(index => CreateRow(
                $"UOM-{index:D2}",
                $"UOM-{index:D2}",
                new DateTime(2025, 9, 1).AddMinutes(index),
                index < 2,
                10m - index,
                baseUomCode: index == 0 ? "CAI" : index == 1 ? "" : "CAI"))
            .ToArray();
        var model = new InventoryRiskBenchmarkCandidate
        {
            Name = "ML_CANDIDATE",
            Version = "1.0.0",
            IsModelCandidate = true,
            ScoresBySampleKey = rows.ToDictionary(row => row.SampleKey, row => row.RuleRiskScore, StringComparer.Ordinal)
        };

        var result = new InventoryRiskBenchmarkService().Evaluate(rows, 7, new[] { model }, new[] { 5 });
        var candidate = Assert.Single(result.Candidates, row => row.CandidateName == "ML_CANDIDATE");
        var top5 = Assert.Single(candidate.TopK);

        Assert.Equal(InventoryRiskExperimentStatus.Ready, result.Status);
        Assert.Null(top5.DetectedAbsoluteVarianceBaseQty);
        Assert.Null(top5.DetectedQuantityUomCode);
        Assert.Equal("MIXED_OR_UNKNOWN_UOM_SUPPRESSED", top5.QuantityMetricStatusCode);
    }

    private static InventoryRiskDatasetQuery Query(DateTime asOf)
        => new()
        {
            BuildAsOf = asOf,
            OutcomeHorizonDays = 30,
            IncludeIsolatedTestData = true,
            AllowedWarehouseIds = new[] { 1 },
            AllowedOwnerPartnerIds = new[] { 101 },
            Seed = 20260716
        };

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AI4-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options) { SkipAudit = true };
    }

    private static async Task SeedScopeAsync(
        AppDbContext db,
        bool trackLot = false,
        bool trackExpiry = false,
        bool trackSerial = false)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure
        {
            UomId = 1,
            UomCode = "CAI",
            UomName = "Cái",
            IsActive = true
        });
        db.Partners.Add(new Partner
        {
            PartnerId = 101,
            PartnerCode = "AUDIT_TEST_AI4_OWNER",
            PartnerName = "Chủ hàng kiểm thử AI-4",
            IsActive = true
        });
        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 1,
            WarehouseCode = "AUDIT_TEST_AI4_WH",
            WarehouseName = "Kho kiểm thử AI-4",
            IsActive = true
        });
        db.Zones.Add(new Zone
        {
            ZoneId = 1,
            WarehouseId = 1,
            ZoneCode = "AUDIT_TEST_AI4_ZONE",
            ZoneName = "Khu kiểm thử AI-4",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        });
        db.Locations.Add(new Location
        {
            LocationId = 1,
            ZoneId = 1,
            LocationCode = "AUDIT_TEST_AI4_BIN",
            IsActive = true
        });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "AUDIT_TEST_AI4_SKU",
            ItemName = "Vật tư kiểm thử AI-4",
            BaseUomId = 1,
            OwnerPartnerId = 101,
            TrackLot = trackLot,
            TrackExpiry = trackExpiry,
            TrackSerial = trackSerial,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddSnapshotAsync(
        AppDbContext db,
        DateTime cutoff,
        decimal onHandQty,
        decimal ruleScore,
        string? lotNumber = null,
        DateTime? expiryDate = null,
        bool serialTracking = false)
    {
        var model = await db.InventoryRiskModelVersions.SingleOrDefaultAsync();
        if (model == null)
        {
            model = new InventoryRiskModelVersion
            {
                InventoryRiskModelVersionId = 1,
                ModelKey = "inventory-discrepancy-risk",
                Version = "RULE-BASELINE-1.0",
                FeatureSchemaVersion = "AI-FEATURE-SCHEMA-0.1",
                ConfigurationJson = "{}",
                ArtifactHash = new string('A', 64),
                CreatedBy = "AUDIT_TEST_AI4",
                CreatedAt = cutoff
            };
            db.InventoryRiskModelVersions.Add(model);
            await db.SaveChangesAsync();
        }

        var features = new InventoryRiskFeatureVector
        {
            OnHandBaseQty = onHandQty,
            AvailableBaseQty = onHandQty,
            AbcClass = "A",
            DaysSinceLastApprovedCount = 180,
            LotTrackingFlag = !string.IsNullOrWhiteSpace(lotNumber),
            ExpiryTrackingFlag = expiryDate.HasValue,
            SerialTrackingFlag = serialTracking
        };
        var featureJson = JsonSerializer.Serialize(features, FeatureJsonOptions);
        var snapshot = new InventoryRiskFeatureSnapshot
        {
            InventoryRiskModelVersionId = model.InventoryRiskModelVersionId,
            BatchId = Guid.NewGuid(),
            PredictionCutoff = cutoff,
            WarehouseId = 1,
            OwnerPartnerId = 101,
            ItemId = 1,
            LocationId = 1,
            LotNumber = lotNumber,
            ExpiryDate = expiryDate,
            ScopeKey = $"1|101|1|1|{lotNumber ?? "NO_LOT"}|{expiryDate:yyyy-MM-dd}",
            FeatureJson = featureJson,
            FeatureHash = Hash(featureJson),
            SourceWatermark = "AUDIT_TEST_AI4_WATERMARK",
            DataQualityStatus = InventoryRiskDataQualityStatusEnum.Ok,
            CreatedAt = cutoff
        };
        snapshot.Prediction = new InventoryRiskPrediction
        {
            InventoryRiskModelVersionId = model.InventoryRiskModelVersionId,
            RiskScore = ruleScore,
            Severity = InventoryRiskSeverityEnum.High,
            ReasonCodesJson = "[]",
            GeneratedAt = cutoff,
            FreshUntil = cutoff.AddHours(1),
            IsShadowMode = true,
            OutputHash = new string('B', 64)
        };
        db.InventoryRiskFeatureSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
    }

    private static Task AddApprovedOutcomeAsync(
        AppDbContext db,
        long sheetId,
        DateTime approvedAt,
        decimal systemQty,
        decimal countedQty,
        bool withAdjustment,
        int? ownerPartnerId = 101,
        string? lotNumber = null,
        DateTime? expiryDate = null)
        => AddOutcomeAsync(
            db,
            sheetId,
            approvedAt,
            StockCountStatusEnum.Approved,
            2,
            systemQty,
            countedQty,
            countedQty - systemQty,
            withAdjustment,
            ownerPartnerId: ownerPartnerId,
            lotNumber: lotNumber,
            expiryDate: expiryDate);

    private static async Task AddOutcomeAsync(
        AppDbContext db,
        long sheetId,
        DateTime timestamp,
        StockCountStatusEnum sheetStatus,
        byte lineStatus,
        decimal systemQty,
        decimal countedQty,
        decimal? storedVariance,
        bool withAdjustment = false,
        bool unlockedAfterApproval = false,
        int? ownerPartnerId = 101,
        string? lotNumber = null,
        DateTime? expiryDate = null,
        DateTime? countedAt = null)
    {
        Voucher? adjustment = null;
        if (withAdjustment)
        {
            var variance = countedQty - systemQty;
            var adjustmentDetailId = sheetId + 20_000;
            adjustment = new Voucher
            {
                VoucherId = sheetId + 10_000,
                VoucherCode = $"AUDIT_TEST_AI4_ADJ_{sheetId}",
                VoucherType = VoucherTypeEnum.DieuChinh,
                VoucherDate = timestamp.Date,
                WarehouseId = 1,
                OwnerPartnerId = ownerPartnerId,
                IsPosted = true,
                IsCancelled = false,
                TotalLines = 1,
                CreatedBy = "AUDIT_TEST_AI4",
                CreatedAt = timestamp
            };
            adjustment.Details.Add(new VoucherDetail
            {
                VoucherDetailId = adjustmentDetailId,
                ItemId = 1,
                OwnerPartnerId = ownerPartnerId,
                LocationId = 1,
                TransactionQty = variance,
                TransactionUomId = 1,
                ConversionRate = 1m,
                BaseQty = variance,
                UnitPrice = 0m,
                LineAmount = 0m,
                LotNumber = lotNumber,
                ExpiryDate = expiryDate,
                LineNumber = 1
            });
            db.Vouchers.Add(adjustment);
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                InventoryTransactionId = sheetId + 30_000,
                TransactionType = InventoryTransactionTypeEnum.Adjust,
                TransactionGroupKey = $"AUDIT_TEST_AI4_COUNT_{sheetId}",
                IdempotencyKey = $"AUDIT_TEST_AI4_COUNT_{sheetId}_1",
                WarehouseId = 1,
                OwnerPartnerId = ownerPartnerId,
                ItemId = 1,
                LocationId = 1,
                LotNumber = lotNumber,
                ExpiryDate = expiryDate,
                QuantityDelta = variance,
                AvailableDelta = variance,
                QuantityBefore = systemQty,
                QuantityAfter = countedQty,
                AvailableBefore = systemQty,
                AvailableAfter = countedQty,
                VoucherId = adjustment.VoucherId,
                VoucherDetailId = adjustmentDetailId,
                ReferenceType = "StockCountSheet",
                ReferenceId = sheetId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ReferenceCode = $"AUDIT_TEST_AI4_CC_{sheetId}",
                Actor = "AUDIT_TEST_AI4_APPROVER",
                TransactionAt = timestamp,
                MetadataJson = "{}"
            });
        }

        var approvedAt = sheetStatus == StockCountStatusEnum.Approved ? timestamp : (DateTime?)null;
        var sheet = new StockCountSheet
        {
            StockCountSheetId = sheetId,
            SheetCode = $"AUDIT_TEST_AI4_CC_{sheetId}",
            WarehouseId = 1,
            CountDate = timestamp.Date,
            Status = sheetStatus,
            CreatedBy = "AUDIT_TEST_AI4_COUNTER",
            CreatedAt = timestamp.AddHours(-1),
            CompletedAt = approvedAt,
            ApprovedBy = approvedAt.HasValue ? "AUDIT_TEST_AI4_APPROVER" : null,
            ApprovedAt = approvedAt,
            ApprovalReason = approvedAt.HasValue ? "AUDIT_TEST_RECONCILED" : null,
            GeneratedAdjustmentVoucher = adjustment,
            UnlockedAt = unlockedAfterApproval ? timestamp.AddMinutes(1) : null,
            UnlockedBy = unlockedAfterApproval ? "AUDIT_TEST_AI4_UNLOCKER" : null,
            UnlockReason = unlockedAfterApproval ? "AUDIT_TEST_INVALID_TIMELINE" : null
        };
        sheet.Lines.Add(new StockCountLine
        {
            StockCountLineId = sheetId,
            ItemId = 1,
            OwnerPartnerId = ownerPartnerId,
            LocationId = 1,
            LotNumber = lotNumber,
            ExpiryDate = expiryDate,
            SystemQty = systemQty,
            CountedQty = countedQty,
            Variance = storedVariance,
            Status = lineStatus,
            CountedBy = "AUDIT_TEST_AI4_COUNTER",
            CountedAt = countedAt ?? timestamp.AddMinutes(-10)
        });
        db.StockCountSheets.Add(sheet);
        await db.SaveChangesAsync();
    }

    private static IEnumerable<InventoryRiskDatasetRow> CreatePartitionRows(string prefix, DateTime cutoff, int count)
        => Enumerable.Range(0, count)
            .Select(index => CreateRow(
                $"{prefix}-S-{index:D3}",
                $"{prefix}-E-{index:D3}",
                cutoff.AddMinutes(index),
                index % 10 == 0,
                count - index));

    private static InventoryRiskDatasetRow CreateRow(
        string sampleKey,
        string entityKey,
        DateTime cutoff,
        bool positive,
        decimal ruleScore,
        string baseUomCode = "CAI",
        DateTime? outcomeApprovedAt = null)
        => new()
        {
            SampleKey = sampleKey,
            EntityKeyHash = entityKey,
            PredictionCutoff = cutoff,
            OutcomeCountedAt = cutoff.AddHours(12),
            OutcomeApprovedAt = outcomeApprovedAt ?? cutoff.AddDays(1),
            ModelVersion = "RULE-BASELINE-1.0",
            FeatureSchemaVersion = "AI-FEATURE-SCHEMA-0.1",
            DatasetSchemaVersion = InventoryRiskDatasetQuery.CurrentDatasetSchemaVersion,
            FeatureHash = new string('C', 64),
            RuleRiskScore = ruleScore,
            BaseUomCode = baseUomCode,
            Features = new InventoryRiskFeatureVector
            {
                AbcClass = "A",
                DaysSinceLastApprovedCount = 180
            },
            HasQuantityVariance = positive,
            AbsoluteVarianceBaseQty = positive ? 1m : 0m,
            EstimatedEffortMinutes = 5
        };

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
