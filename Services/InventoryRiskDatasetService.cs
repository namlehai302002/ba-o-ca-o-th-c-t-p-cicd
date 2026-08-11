using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Services;

public enum InventoryRiskExperimentStatus
{
    Ready = 1,
    BaselineOnly = 2,
    BlockedData = 3,
    BlockedConfiguration = 4
}

public sealed class InventoryRiskDatasetQuery
{
    public const string CurrentDatasetSchemaVersion = "AI-DATASET-SCHEMA-0.1";

    public DateTime BuildAsOf { get; init; }
    public string DatasetSchemaVersion { get; init; } = CurrentDatasetSchemaVersion;
    public string FeatureSchemaVersion { get; init; } = "AI-FEATURE-SCHEMA-0.1";
    public int OutcomeHorizonDays { get; init; } = 90;
    public decimal QuantityTolerance { get; init; } = 0.0001m;
    public int Seed { get; init; } = 20260716;
    public bool IncludeIsolatedTestData { get; init; }
    public bool IncludeDemoData { get; init; }
    public IReadOnlyList<int> AllowedWarehouseIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> AllowedOwnerPartnerIds { get; init; } = Array.Empty<int>();
}

public sealed class InventoryRiskDatasetRow
{
    public long FeatureSnapshotId { get; init; }
    public long StockCountSheetId { get; init; }
    public long StockCountLineId { get; init; }
    public int WarehouseId { get; init; }
    public int? OwnerPartnerId { get; init; }
    public int ItemId { get; init; }
    public int LocationId { get; init; }
    public string? LotNumber { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public string EntityKeyHash { get; init; } = "";
    public string SampleKey { get; init; } = "";
    public DateTime PredictionCutoff { get; init; }
    public DateTime OutcomeCountedAt { get; init; }
    public DateTime OutcomeApprovedAt { get; init; }
    public string DatasetSchemaVersion { get; init; } = "";
    public string FeatureSchemaVersion { get; init; } = "";
    public string ModelVersion { get; init; } = "";
    public string FeatureJson { get; init; } = "{}";
    public string FeatureHash { get; init; } = "";
    public string SourceWatermark { get; init; } = "";
    public InventoryRiskFeatureVector Features { get; init; } = new();
    public decimal? RuleRiskScore { get; init; }
    public string BaseUomCode { get; init; } = "";
    public decimal SystemBaseQty { get; init; }
    public decimal CountedBaseQty { get; init; }
    public decimal VarianceBaseQty { get; init; }
    public decimal AbsoluteVarianceBaseQty { get; init; }
    public bool HasQuantityVariance { get; init; }
    public bool? HasMaterialVariance { get; init; }
    public string MaterialVarianceStatus { get; init; } = "UNKNOWN_THRESHOLD_SNAPSHOT_MISSING";
    public int? EstimatedEffortMinutes { get; init; }
    public bool IsDirectRecommendationOutcome { get; init; }
    public bool IsDemoData { get; init; }
}

public sealed class InventoryRiskDatasetBuildResult
{
    public InventoryRiskExperimentStatus Status { get; init; }
    public InventoryRiskDatasetQuery Query { get; init; } = new();
    public IReadOnlyList<InventoryRiskDatasetRow> Rows { get; init; } = Array.Empty<InventoryRiskDatasetRow>();
    public IReadOnlyDictionary<string, int> Exclusions { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> ReadinessCodes { get; init; } = Array.Empty<string>();
    public string DatasetHash { get; init; } = "";
    public string SourceWatermark { get; init; } = "";
    public int CandidateSnapshotCount { get; init; }
    public int CandidateOutcomeCount { get; init; }
    public int PositiveCount { get; init; }
    public int NegativeCount { get; init; }
    public int DemoRowCount { get; init; }
    public int DistinctPredictionDays { get; init; }
}

public interface IInventoryRiskDatasetService
{
    Task<InventoryRiskDatasetBuildResult> BuildAsync(
        InventoryRiskDatasetQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds an offline training/evaluation dataset only from immutable feature snapshots.
/// It never reconstructs historical features from current balances and never mutates WMS data.
/// </summary>
public sealed class InventoryRiskDatasetService : IInventoryRiskDatasetService
{
    private static readonly TimeSpan SnapshotPersistenceGrace = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions FeatureJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly HashSet<string> AllowedFeatureProperties = typeof(InventoryRiskFeatureVector)
        .GetProperties()
        .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
        .ToHashSet(StringComparer.Ordinal);

    private readonly AppDbContext _db;

    public InventoryRiskDatasetService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryRiskDatasetBuildResult> BuildAsync(
        InventoryRiskDatasetQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);

        var exclusions = new Dictionary<string, int>(StringComparer.Ordinal);
        var warehouseScope = query.AllowedWarehouseIds.Distinct().OrderBy(id => id).ToArray();
        var ownerScope = query.AllowedOwnerPartnerIds.Distinct().OrderBy(id => id).ToArray();

        var snapshotQuery = _db.InventoryRiskFeatureSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.PredictionCutoff <= query.BuildAsOf
                && snapshot.CreatedAt <= query.BuildAsOf);
        if (warehouseScope.Length > 0)
            snapshotQuery = snapshotQuery.Where(snapshot => warehouseScope.Contains(snapshot.WarehouseId));
        if (ownerScope.Length > 0)
            snapshotQuery = snapshotQuery.Where(snapshot => snapshot.OwnerPartnerId.HasValue && ownerScope.Contains(snapshot.OwnerPartnerId.Value));

        var snapshots = await snapshotQuery
            .Select(snapshot => new SnapshotSeed
            {
                FeatureSnapshotId = snapshot.InventoryRiskFeatureSnapshotId,
                WarehouseId = snapshot.WarehouseId,
                WarehouseCode = snapshot.Warehouse.WarehouseCode,
                OwnerPartnerId = snapshot.OwnerPartnerId,
                OwnerCode = snapshot.OwnerPartner == null ? null : snapshot.OwnerPartner.PartnerCode,
                ItemId = snapshot.ItemId,
                ItemCode = snapshot.Item.ItemCode,
                LocationId = snapshot.LocationId,
                LocationCode = snapshot.Location.LocationCode,
                LotNumber = snapshot.LotNumber,
                ExpiryDate = snapshot.ExpiryDate,
                ScopeKey = snapshot.ScopeKey,
                PredictionCutoff = snapshot.PredictionCutoff,
                CreatedAt = snapshot.CreatedAt,
                FeatureSchemaVersion = snapshot.ModelVersion.FeatureSchemaVersion,
                ModelVersion = snapshot.ModelVersion.Version,
                FeatureJson = snapshot.FeatureJson,
                FeatureHash = snapshot.FeatureHash,
                SourceWatermark = snapshot.SourceWatermark,
                DataQualityStatus = snapshot.DataQualityStatus,
                RuleRiskScore = snapshot.Prediction == null ? null : snapshot.Prediction.RiskScore,
                RecommendationSheetId = snapshot.Prediction == null || snapshot.Prediction.Recommendation == null
                    ? null
                    : snapshot.Prediction.Recommendation.StockCountSheetId,
                EstimatedEffortMinutes = snapshot.Prediction == null || snapshot.Prediction.Recommendation == null
                    ? null
                    : snapshot.Prediction.Recommendation.EstimatedEffortMinutes
            })
            .OrderBy(snapshot => snapshot.PredictionCutoff)
            .ThenBy(snapshot => snapshot.ScopeKey)
            .ThenBy(snapshot => snapshot.FeatureSnapshotId)
            .ToListAsync(cancellationToken);

        var candidateSnapshotCount = snapshots.Count;
        var validatedSnapshots = new List<ValidatedSnapshot>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            if (snapshot.CreatedAt > snapshot.PredictionCutoff.Add(SnapshotPersistenceGrace))
            {
                Increment(exclusions, "SNAPSHOT_BACKFILLED_AFTER_CUTOFF");
                continue;
            }

            if (!string.Equals(snapshot.FeatureSchemaVersion, query.FeatureSchemaVersion, StringComparison.Ordinal))
            {
                Increment(exclusions, "FEATURE_SCHEMA_MISMATCH");
                continue;
            }

            if (snapshot.DataQualityStatus == InventoryRiskDataQualityStatusEnum.Blocked)
            {
                Increment(exclusions, "DATA_QUALITY_BLOCKED");
                continue;
            }

            var isolatedTest = IsIsolatedTestCode(snapshot.WarehouseCode)
                || IsIsolatedTestCode(snapshot.OwnerCode)
                || IsIsolatedTestCode(snapshot.ItemCode)
                || IsIsolatedTestCode(snapshot.LocationCode);
            if (isolatedTest && !query.IncludeIsolatedTestData)
            {
                Increment(exclusions, "ISOLATED_TEST_DATA_EXCLUDED");
                continue;
            }

            if (!TryReadFeatures(snapshot.FeatureJson, snapshot.FeatureHash, out var features, out var featureError))
            {
                Increment(exclusions, featureError);
                continue;
            }

            if (features!.LotTrackingFlag && string.IsNullOrWhiteSpace(snapshot.LotNumber))
            {
                Increment(exclusions, "TRACKED_LOT_MISSING");
                continue;
            }

            if (features.ExpiryTrackingFlag && !snapshot.ExpiryDate.HasValue)
            {
                Increment(exclusions, "TRACKED_EXPIRY_MISSING");
                continue;
            }

            if (features.SerialTrackingFlag)
            {
                Increment(exclusions, "SERIAL_TRACKED_OUTCOME_COVERAGE_UNAVAILABLE");
                continue;
            }

            validatedSnapshots.Add(new ValidatedSnapshot(snapshot, features));
        }

        validatedSnapshots = RemoveDuplicateSnapshots(validatedSnapshots, exclusions);
        if (validatedSnapshots.Count == 0)
        {
            return EmptyResult(query, candidateSnapshotCount, 0, exclusions, "NO_VALID_FEATURE_SNAPSHOT");
        }

        var earliestCutoff = validatedSnapshots.Min(row => row.Seed.PredictionCutoff);
        var latestOutcomeAt = validatedSnapshots
            .Max(row => row.Seed.PredictionCutoff.AddDays(query.OutcomeHorizonDays));
        if (latestOutcomeAt > query.BuildAsOf)
            latestOutcomeAt = query.BuildAsOf;

        var outcomeQuery = _db.StockCountLines
            .AsNoTracking()
            .Where(line => line.StockCountSheet != null
                && line.StockCountSheet.ApprovedAt.HasValue
                && line.StockCountSheet.ApprovedAt.Value > earliestCutoff
                && line.StockCountSheet.ApprovedAt.Value <= latestOutcomeAt);
        if (warehouseScope.Length > 0)
            outcomeQuery = outcomeQuery.Where(line => warehouseScope.Contains(line.StockCountSheet!.WarehouseId));
        if (ownerScope.Length > 0)
            outcomeQuery = outcomeQuery.Where(line => line.OwnerPartnerId.HasValue && ownerScope.Contains(line.OwnerPartnerId.Value));

        var outcomes = await outcomeQuery
            .Select(line => new OutcomeSeed
            {
                StockCountSheetId = line.StockCountSheetId,
                StockCountLineId = line.StockCountLineId,
                WarehouseId = line.StockCountSheet!.WarehouseId,
                LocationWarehouseId = line.Location!.Zone.WarehouseId,
                WarehouseCode = line.StockCountSheet.Warehouse == null ? "" : line.StockCountSheet.Warehouse.WarehouseCode,
                OwnerPartnerId = line.OwnerPartnerId,
                OwnerCode = line.OwnerPartner == null ? null : line.OwnerPartner.PartnerCode,
                ItemId = line.ItemId,
                ItemCode = line.Item == null ? "" : line.Item.ItemCode,
                HasBaseUom = line.Item != null && line.Item.BaseUomId > 0,
                BaseUomCode = line.Item == null || line.Item.BaseUom == null ? "" : line.Item.BaseUom.UomCode,
                TrackLot = line.Item != null && line.Item.TrackLot,
                TrackExpiry = line.Item != null && line.Item.TrackExpiry,
                TrackSerial = line.Item != null && line.Item.TrackSerial,
                LocationId = line.LocationId,
                LocationCode = line.Location == null ? "" : line.Location.LocationCode,
                LotNumber = line.LotNumber,
                ExpiryDate = line.ExpiryDate,
                SystemQty = line.SystemQty,
                CountedQty = line.CountedQty,
                StoredVariance = line.Variance,
                CountedAt = line.CountedAt,
                LineStatus = line.Status,
                SheetStatus = line.StockCountSheet.Status,
                ApprovedAt = line.StockCountSheet.ApprovedAt,
                CompletedAt = line.StockCountSheet.CompletedAt,
                UnlockedAt = line.StockCountSheet.UnlockedAt,
                GeneratedAdjustmentVoucherId = line.StockCountSheet.GeneratedAdjustmentVoucherId,
                AdjustmentVoucherIsValid = line.StockCountSheet.GeneratedAdjustmentVoucher != null
                    && line.StockCountSheet.GeneratedAdjustmentVoucher.IsPosted
                    && !line.StockCountSheet.GeneratedAdjustmentVoucher.IsCancelled
            })
            .OrderBy(outcome => outcome.ApprovedAt)
            .ThenBy(outcome => outcome.StockCountSheetId)
            .ThenBy(outcome => outcome.StockCountLineId)
            .ToListAsync(cancellationToken);

        await PopulateAdjustmentReconciliationAsync(outcomes, cancellationToken);

        var candidateOutcomeCount = outcomes.Count;
        var eligibleOutcomes = new List<OutcomeSeed>(outcomes.Count);
        foreach (var outcome in outcomes)
        {
            var exclusionCode = ValidateOutcome(outcome, query.QuantityTolerance, query.IncludeIsolatedTestData);
            if (exclusionCode != null)
            {
                Increment(exclusions, exclusionCode);
                continue;
            }
            eligibleOutcomes.Add(outcome);
        }

        var outcomeGroups = eligibleOutcomes
            .GroupBy(outcome => EntityKey(
                outcome.WarehouseId,
                outcome.OwnerPartnerId,
                outcome.ItemId,
                outcome.LocationId,
                outcome.LotNumber,
                outcome.ExpiryDate))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(outcome => outcome.ApprovedAt)
                    .ThenBy(outcome => outcome.StockCountSheetId)
                    .ThenBy(outcome => outcome.StockCountLineId)
                    .ToArray(),
                StringComparer.Ordinal);

        var rows = new List<InventoryRiskDatasetRow>(validatedSnapshots.Count);
        foreach (var validated in validatedSnapshots)
        {
            var snapshot = validated.Seed;
            var entityKey = EntityKey(
                snapshot.WarehouseId,
                snapshot.OwnerPartnerId,
                snapshot.ItemId,
                snapshot.LocationId,
                snapshot.LotNumber,
                snapshot.ExpiryDate);
            var horizonEnd = snapshot.PredictionCutoff.AddDays(query.OutcomeHorizonDays);
            if (horizonEnd > query.BuildAsOf)
            {
                Increment(exclusions, "OUTCOME_HORIZON_NOT_MATURE");
                continue;
            }

            if (!outcomeGroups.TryGetValue(entityKey, out var grainOutcomes))
            {
                Increment(exclusions, "APPROVED_OUTCOME_NOT_FOUND");
                continue;
            }

            var outcome = grainOutcomes.FirstOrDefault(candidate =>
                candidate.CountedAt!.Value > snapshot.PredictionCutoff
                && candidate.CountedAt.Value <= horizonEnd
                && candidate.ApprovedAt!.Value > snapshot.PredictionCutoff
                && candidate.ApprovedAt.Value <= horizonEnd);
            if (outcome == null)
            {
                Increment(exclusions, "APPROVED_OUTCOME_NOT_FOUND");
                continue;
            }

            var countedQty = outcome.CountedQty!.Value;
            var outcomeApprovedAt = outcome.ApprovedAt.GetValueOrDefault();
            var variance = countedQty - outcome.SystemQty;
            var absoluteVariance = Math.Abs(variance);
            var entityKeyHash = Sha256(entityKey);
            var sampleKey = Sha256(string.Join('|',
                query.DatasetSchemaVersion,
                query.FeatureSchemaVersion,
                entityKey,
                snapshot.PredictionCutoff.ToString("O", CultureInfo.InvariantCulture),
                snapshot.FeatureHash,
                outcome.StockCountLineId.ToString(CultureInfo.InvariantCulture)));
            var isDemo = IsDemoCode(snapshot.WarehouseCode)
                || IsDemoCode(snapshot.OwnerCode)
                || IsDemoCode(snapshot.ItemCode)
                || IsDemoCode(snapshot.LocationCode)
                || IsDemoCode(outcome.WarehouseCode)
                || IsDemoCode(outcome.OwnerCode)
                || IsDemoCode(outcome.ItemCode)
                || IsDemoCode(outcome.LocationCode);
            if (isDemo && !query.IncludeDemoData)
            {
                Increment(exclusions, "DEMO_DATA_EXCLUDED");
                continue;
            }

            rows.Add(new InventoryRiskDatasetRow
            {
                FeatureSnapshotId = snapshot.FeatureSnapshotId,
                StockCountSheetId = outcome.StockCountSheetId,
                StockCountLineId = outcome.StockCountLineId,
                WarehouseId = snapshot.WarehouseId,
                OwnerPartnerId = snapshot.OwnerPartnerId,
                ItemId = snapshot.ItemId,
                LocationId = snapshot.LocationId,
                LotNumber = NormalizeNullableLot(snapshot.LotNumber),
                ExpiryDate = snapshot.ExpiryDate?.Date,
                EntityKeyHash = entityKeyHash,
                SampleKey = sampleKey,
                PredictionCutoff = snapshot.PredictionCutoff,
                OutcomeCountedAt = outcome.CountedAt.GetValueOrDefault(),
                OutcomeApprovedAt = outcomeApprovedAt,
                DatasetSchemaVersion = query.DatasetSchemaVersion,
                FeatureSchemaVersion = snapshot.FeatureSchemaVersion,
                ModelVersion = snapshot.ModelVersion,
                FeatureJson = snapshot.FeatureJson,
                FeatureHash = snapshot.FeatureHash,
                SourceWatermark = snapshot.SourceWatermark,
                Features = validated.Features,
                RuleRiskScore = snapshot.RuleRiskScore,
                BaseUomCode = outcome.BaseUomCode,
                SystemBaseQty = outcome.SystemQty,
                CountedBaseQty = countedQty,
                VarianceBaseQty = variance,
                AbsoluteVarianceBaseQty = absoluteVariance,
                HasQuantityVariance = absoluteVariance > query.QuantityTolerance,
                HasMaterialVariance = null,
                EstimatedEffortMinutes = snapshot.EstimatedEffortMinutes,
                IsDirectRecommendationOutcome = snapshot.RecommendationSheetId == outcome.StockCountSheetId,
                IsDemoData = isDemo
            });
        }

        rows = RemoveReusedOutcomes(rows, exclusions);

        rows = rows
            .OrderBy(row => row.PredictionCutoff)
            .ThenBy(row => row.EntityKeyHash, StringComparer.Ordinal)
            .ThenBy(row => row.SampleKey, StringComparer.Ordinal)
            .ToList();

        var positiveCount = rows.Count(row => row.HasQuantityVariance);
        var negativeCount = rows.Count - positiveCount;
        var distinctDays = rows.Select(row => row.PredictionCutoff.Date).Distinct().Count();
        var readinessCodes = BuildReadinessCodes(
            rows.Count,
            positiveCount,
            negativeCount,
            distinctDays,
            query.IncludeDemoData && rows.Any(row => row.IsDemoData));
        var status = readinessCodes.Count == 0
            ? InventoryRiskExperimentStatus.Ready
            : InventoryRiskExperimentStatus.BlockedData;
        var datasetHash = HashDataset(query, rows);
        var sourceWatermark = Sha256(string.Join('|',
            snapshots.Count,
            outcomes.Count,
            snapshots.LastOrDefault()?.FeatureSnapshotId ?? 0,
            outcomes.LastOrDefault()?.StockCountLineId ?? 0,
            datasetHash));

        return new InventoryRiskDatasetBuildResult
        {
            Status = status,
            Query = query,
            Rows = rows,
            Exclusions = exclusions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            ReadinessCodes = readinessCodes,
            DatasetHash = datasetHash,
            SourceWatermark = sourceWatermark,
            CandidateSnapshotCount = candidateSnapshotCount,
            CandidateOutcomeCount = candidateOutcomeCount,
            PositiveCount = positiveCount,
            NegativeCount = negativeCount,
            DemoRowCount = rows.Count(row => row.IsDemoData),
            DistinctPredictionDays = distinctDays
        };
    }

    private static List<ValidatedSnapshot> RemoveDuplicateSnapshots(
        List<ValidatedSnapshot> snapshots,
        IDictionary<string, int> exclusions)
    {
        var result = new List<ValidatedSnapshot>(snapshots.Count);
        foreach (var group in snapshots.GroupBy(
                     snapshot => $"{EntityKey(snapshot.Seed.WarehouseId, snapshot.Seed.OwnerPartnerId, snapshot.Seed.ItemId, snapshot.Seed.LocationId, snapshot.Seed.LotNumber, snapshot.Seed.ExpiryDate)}|{snapshot.Seed.PredictionCutoff:O}",
                     StringComparer.Ordinal))
        {
            var featureHashes = group.Select(row => row.Seed.FeatureHash).Distinct(StringComparer.Ordinal).ToArray();
            if (featureHashes.Length > 1)
            {
                Increment(exclusions, "CONFLICTING_SNAPSHOT_AT_CUTOFF", group.Count());
                continue;
            }

            var ordered = group.OrderBy(row => row.Seed.FeatureSnapshotId).ToArray();
            result.Add(ordered[0]);
            if (ordered.Length > 1)
                Increment(exclusions, "DUPLICATE_SNAPSHOT_AT_CUTOFF", ordered.Length - 1);
        }

        return result;
    }

    private async Task PopulateAdjustmentReconciliationAsync(
        IReadOnlyCollection<OutcomeSeed> outcomes,
        CancellationToken cancellationToken)
    {
        var varianceOutcomes = outcomes
            .Where(outcome => outcome.StoredVariance.HasValue && Math.Abs(outcome.StoredVariance.Value) > 0.0001m)
            .ToArray();
        if (varianceOutcomes.Length == 0)
            return;

        var detailRows = new List<AdjustmentDetailSeed>();
        var voucherIds = varianceOutcomes
            .Where(outcome => outcome.GeneratedAdjustmentVoucherId.HasValue)
            .Select(outcome => outcome.GeneratedAdjustmentVoucherId!.Value)
            .Distinct()
            .ToArray();
        foreach (var voucherIdChunk in voucherIds.Chunk(500))
        {
            var ids = voucherIdChunk.ToArray();
            detailRows.AddRange(await _db.VoucherDetails
                .AsNoTracking()
                .Where(detail => ids.Contains(detail.VoucherId) && detail.LocationId.HasValue)
                .Select(detail => new AdjustmentDetailSeed
                {
                    VoucherId = detail.VoucherId,
                    OwnerPartnerId = detail.OwnerPartnerId,
                    ItemId = detail.ItemId,
                    LocationId = detail.LocationId!.Value,
                    LotNumber = detail.LotNumber,
                    ExpiryDate = detail.ExpiryDate,
                    BaseQty = detail.BaseQty
                })
                .ToListAsync(cancellationToken));
        }

        var ledgerRows = new List<AdjustmentLedgerSeed>();
        var sheetReferenceIds = varianceOutcomes
            .Select(outcome => outcome.StockCountSheetId.ToString(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var referenceChunk in sheetReferenceIds.Chunk(500))
        {
            var references = referenceChunk.ToArray();
            ledgerRows.AddRange(await _db.InventoryTransactions
                .AsNoTracking()
                .Where(transaction => transaction.TransactionType == InventoryTransactionTypeEnum.Adjust
                    && transaction.ReferenceType == "StockCountSheet"
                    && transaction.ReferenceId != null
                    && references.Contains(transaction.ReferenceId))
                .Select(transaction => new AdjustmentLedgerSeed
                {
                    StockCountSheetReference = transaction.ReferenceId!,
                    OwnerPartnerId = transaction.OwnerPartnerId,
                    ItemId = transaction.ItemId,
                    LocationId = transaction.LocationId,
                    LotNumber = transaction.LotNumber,
                    ExpiryDate = transaction.ExpiryDate,
                    QuantityDelta = transaction.QuantityDelta
                })
                .ToListAsync(cancellationToken));
        }

        var detailByGrain = detailRows
            .GroupBy(row => AdjustmentGrainKey(
                row.VoucherId.ToString(CultureInfo.InvariantCulture),
                row.OwnerPartnerId,
                row.ItemId,
                row.LocationId,
                row.LotNumber,
                row.ExpiryDate), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.BaseQty), StringComparer.Ordinal);
        var ledgerByGrain = ledgerRows
            .GroupBy(row => AdjustmentGrainKey(
                row.StockCountSheetReference,
                row.OwnerPartnerId,
                row.ItemId,
                row.LocationId,
                row.LotNumber,
                row.ExpiryDate), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new { Delta = group.Sum(row => row.QuantityDelta), Count = group.Count() },
                StringComparer.Ordinal);

        foreach (var outcome in varianceOutcomes)
        {
            if (outcome.GeneratedAdjustmentVoucherId.HasValue)
            {
                var detailKey = AdjustmentGrainKey(
                    outcome.GeneratedAdjustmentVoucherId.Value.ToString(CultureInfo.InvariantCulture),
                    outcome.OwnerPartnerId,
                    outcome.ItemId,
                    outcome.LocationId,
                    outcome.LotNumber,
                    outcome.ExpiryDate);
                outcome.AdjustmentDetailBaseQty = detailByGrain.GetValueOrDefault(detailKey);
            }

            var ledgerKey = AdjustmentGrainKey(
                outcome.StockCountSheetId.ToString(CultureInfo.InvariantCulture),
                outcome.OwnerPartnerId,
                outcome.ItemId,
                outcome.LocationId,
                outcome.LotNumber,
                outcome.ExpiryDate);
            if (ledgerByGrain.TryGetValue(ledgerKey, out var ledger))
            {
                outcome.AdjustmentLedgerDelta = ledger.Delta;
                outcome.AdjustmentLedgerEventCount = ledger.Count;
            }
        }
    }

    private static string AdjustmentGrainKey(
        string reference,
        int? ownerPartnerId,
        int itemId,
        int locationId,
        string? lotNumber,
        DateTime? expiryDate)
        => $"{reference}|{EntityKey(0, ownerPartnerId, itemId, locationId, lotNumber, expiryDate)}";

    private static List<InventoryRiskDatasetRow> RemoveReusedOutcomes(
        IEnumerable<InventoryRiskDatasetRow> rows,
        IDictionary<string, int> exclusions)
    {
        var result = new List<InventoryRiskDatasetRow>();
        foreach (var group in rows.GroupBy(row => row.StockCountLineId))
        {
            var ordered = group
                .OrderByDescending(row => row.IsDirectRecommendationOutcome)
                .ThenByDescending(row => row.PredictionCutoff)
                .ThenByDescending(row => row.FeatureSnapshotId)
                .ToArray();
            result.Add(ordered[0]);
            if (ordered.Length > 1)
                Increment(exclusions, "OUTCOME_REUSED_BY_MULTIPLE_SNAPSHOTS", ordered.Length - 1);
        }
        return result;
    }

    private static string? ValidateOutcome(
        OutcomeSeed outcome,
        decimal quantityTolerance,
        bool includeIsolatedTestData)
    {
        if (outcome.SheetStatus != StockCountStatusEnum.Approved
            || !outcome.ApprovedAt.HasValue
            || !outcome.CompletedAt.HasValue
            || outcome.LineStatus != 2)
        {
            return "OUTCOME_NOT_APPROVED_FINAL";
        }

        if (outcome.CompletedAt.Value > outcome.ApprovedAt.Value
            || (outcome.UnlockedAt.HasValue && outcome.UnlockedAt.Value >= outcome.ApprovedAt.Value))
        {
            return "OUTCOME_APPROVAL_TIMELINE_INVALID";
        }

        if (!outcome.CountedQty.HasValue || !outcome.StoredVariance.HasValue)
            return "OUTCOME_QUANTITY_INCOMPLETE";
        if (!outcome.CountedAt.HasValue
            || outcome.CountedAt.Value > outcome.CompletedAt.Value
            || outcome.CountedAt.Value > outcome.ApprovedAt.Value)
        {
            return "OUTCOME_COUNT_TIMELINE_INVALID";
        }
        if (!outcome.HasBaseUom || string.IsNullOrWhiteSpace(outcome.BaseUomCode))
            return "OUTCOME_BASE_UOM_MISSING";
        if (outcome.LocationWarehouseId != outcome.WarehouseId)
            return "OUTCOME_WAREHOUSE_LOCATION_MISMATCH";
        if (outcome.TrackLot && string.IsNullOrWhiteSpace(outcome.LotNumber))
            return "OUTCOME_TRACKED_LOT_MISSING";
        if (outcome.TrackExpiry && !outcome.ExpiryDate.HasValue)
            return "OUTCOME_TRACKED_EXPIRY_MISSING";
        if (outcome.TrackSerial)
            return "SERIAL_TRACKED_OUTCOME_COVERAGE_UNAVAILABLE";

        var calculatedVariance = outcome.CountedQty.Value - outcome.SystemQty;
        if (Math.Abs(calculatedVariance - outcome.StoredVariance.Value) > quantityTolerance)
            return "OUTCOME_VARIANCE_NOT_RECONCILED";
        if (Math.Abs(calculatedVariance) > quantityTolerance
            && (!outcome.GeneratedAdjustmentVoucherId.HasValue
                || !outcome.AdjustmentVoucherIsValid
                || Math.Abs(outcome.AdjustmentDetailBaseQty - calculatedVariance) > quantityTolerance
                || outcome.AdjustmentLedgerEventCount == 0
                || Math.Abs(outcome.AdjustmentLedgerDelta - calculatedVariance) > quantityTolerance))
        {
            return "OUTCOME_ADJUSTMENT_LEDGER_NOT_RECONCILED";
        }

        var isolatedTest = IsIsolatedTestCode(outcome.WarehouseCode)
            || IsIsolatedTestCode(outcome.OwnerCode)
            || IsIsolatedTestCode(outcome.ItemCode)
            || IsIsolatedTestCode(outcome.LocationCode);
        return isolatedTest && !includeIsolatedTestData
            ? "ISOLATED_TEST_OUTCOME_EXCLUDED"
            : null;
    }

    private static bool TryReadFeatures(
        string featureJson,
        string expectedHash,
        out InventoryRiskFeatureVector? features,
        out string errorCode)
    {
        features = null;
        errorCode = "INVALID_FEATURE_JSON";
        if (string.IsNullOrWhiteSpace(featureJson)
            || string.IsNullOrWhiteSpace(expectedHash)
            || !string.Equals(Sha256(featureJson), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "INVALID_FEATURE_HASH";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(featureJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    errorCode = "DUPLICATE_FEATURE_PROPERTY";
                    return false;
                }
                if (!AllowedFeatureProperties.Contains(property.Name))
                {
                    errorCode = "UNSUPPORTED_FEATURE_PROPERTY";
                    return false;
                }
            }

            if (!seen.SetEquals(AllowedFeatureProperties))
            {
                errorCode = "MISSING_FEATURE_PROPERTY";
                return false;
            }

            features = JsonSerializer.Deserialize<InventoryRiskFeatureVector>(featureJson, FeatureJsonOptions);
            return features != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> BuildReadinessCodes(
        int rowCount,
        int positiveCount,
        int negativeCount,
        int distinctPredictionDays,
        bool includesDemoData)
    {
        var codes = new List<string>();
        if (rowCount < 100)
            codes.Add("INSUFFICIENT_ROWS_FOR_PRECISION_AT_100");
        if (positiveCount == 0)
            codes.Add("NO_POSITIVE_QUANTITY_VARIANCE");
        if (negativeCount == 0)
            codes.Add("NO_NEGATIVE_CLASS");
        if (distinctPredictionDays < 3)
            codes.Add("INSUFFICIENT_TEMPORAL_DEPTH");
        if (includesDemoData)
            codes.Add("DEMO_DATA_INCLUDED_NON_PROMOTABLE");
        return codes;
    }

    private static InventoryRiskDatasetBuildResult EmptyResult(
        InventoryRiskDatasetQuery query,
        int candidateSnapshotCount,
        int candidateOutcomeCount,
        IReadOnlyDictionary<string, int> exclusions,
        string readinessCode)
        => new()
        {
            Status = InventoryRiskExperimentStatus.BlockedData,
            Query = query,
            CandidateSnapshotCount = candidateSnapshotCount,
            CandidateOutcomeCount = candidateOutcomeCount,
            Exclusions = exclusions,
            ReadinessCodes = new[] { readinessCode },
            DatasetHash = HashDataset(query, Array.Empty<InventoryRiskDatasetRow>()),
            SourceWatermark = Sha256($"{candidateSnapshotCount}|{candidateOutcomeCount}|EMPTY")
        };

    private static void ValidateQuery(InventoryRiskDatasetQuery query)
    {
        if (query.BuildAsOf == default)
            throw new ArgumentException("BuildAsOf is required for a reproducible dataset cutoff.", nameof(query));
        if (string.IsNullOrWhiteSpace(query.DatasetSchemaVersion)
            || string.IsNullOrWhiteSpace(query.FeatureSchemaVersion))
        {
            throw new ArgumentException("Dataset and feature schema versions are required.", nameof(query));
        }
        if (query.OutcomeHorizonDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(query), "Outcome horizon must be between 1 and 3650 days.");
        if (query.QuantityTolerance <= 0m)
            throw new ArgumentOutOfRangeException(nameof(query), "Quantity tolerance must be positive.");
    }

    private static string HashDataset(InventoryRiskDatasetQuery query, IReadOnlyList<InventoryRiskDatasetRow> rows)
    {
        var builder = new StringBuilder();
        builder.Append(query.DatasetSchemaVersion).Append('|')
            .Append(query.FeatureSchemaVersion).Append('|')
            .Append(query.BuildAsOf.ToString("O", CultureInfo.InvariantCulture)).Append('|')
            .Append(query.OutcomeHorizonDays).Append('|')
            .Append(query.QuantityTolerance.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(query.Seed).Append('|')
            .Append(query.IncludeIsolatedTestData ? '1' : '0').Append('|')
            .Append(query.IncludeDemoData ? '1' : '0').Append('|')
            .Append(string.Join(',', query.AllowedWarehouseIds.Distinct().OrderBy(id => id))).Append('|')
            .Append(string.Join(',', query.AllowedOwnerPartnerIds.Distinct().OrderBy(id => id)))
            .Append('\n');
        foreach (var row in rows)
        {
            builder.Append(row.SampleKey).Append('|')
                .Append(row.EntityKeyHash).Append('|')
                .Append(row.FeatureHash).Append('|')
                .Append(row.FeatureJson).Append('|')
                .Append(row.ModelVersion).Append('|')
                .Append(row.RuleRiskScore?.ToString(CultureInfo.InvariantCulture) ?? "NA").Append('|')
                .Append(row.StockCountSheetId).Append('|')
                .Append(row.StockCountLineId).Append('|')
                .Append(row.PredictionCutoff.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(row.OutcomeCountedAt.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(row.OutcomeApprovedAt.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(row.BaseUomCode).Append('|')
                .Append(row.SystemBaseQty.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(row.CountedBaseQty.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(row.VarianceBaseQty.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(row.AbsoluteVarianceBaseQty.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(row.HasQuantityVariance ? '1' : '0').Append('|')
                .Append(row.HasMaterialVariance?.ToString() ?? "NA").Append('|')
                .Append(row.MaterialVarianceStatus).Append('|')
                .Append(row.EstimatedEffortMinutes?.ToString(CultureInfo.InvariantCulture) ?? "NA").Append('|')
                .Append(row.IsDirectRecommendationOutcome ? '1' : '0').Append('|')
                .Append(row.IsDemoData ? '1' : '0')
                .Append('\n');
        }
        return Sha256(builder.ToString());
    }

    private static string EntityKey(
        int warehouseId,
        int? ownerPartnerId,
        int itemId,
        int locationId,
        string? lotNumber,
        DateTime? expiryDate)
        => string.Join('|',
            warehouseId.ToString(CultureInfo.InvariantCulture),
            ownerPartnerId?.ToString(CultureInfo.InvariantCulture) ?? "INTERNAL",
            itemId.ToString(CultureInfo.InvariantCulture),
            locationId.ToString(CultureInfo.InvariantCulture),
            NormalizeNullableLot(lotNumber) ?? "NO_LOT",
            expiryDate?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "NO_EXPIRY");

    private static string? NormalizeNullableLot(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static bool IsIsolatedTestCode(string? value)
        => value?.StartsWith("AUDIT_TEST_", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsDemoCode(string? value)
        => value?.StartsWith("DEMO-", StringComparison.OrdinalIgnoreCase) == true
            || value?.StartsWith("DEMO_", StringComparison.OrdinalIgnoreCase) == true;

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void Increment(IDictionary<string, int> counts, string code, int amount = 1)
        => counts[code] = (counts.TryGetValue(code, out var current) ? current : 0) + amount;

    private sealed record ValidatedSnapshot(SnapshotSeed Seed, InventoryRiskFeatureVector Features);

    private sealed class SnapshotSeed
    {
        public long FeatureSnapshotId { get; init; }
        public int WarehouseId { get; init; }
        public string WarehouseCode { get; init; } = "";
        public int? OwnerPartnerId { get; init; }
        public string? OwnerCode { get; init; }
        public int ItemId { get; init; }
        public string ItemCode { get; init; } = "";
        public int LocationId { get; init; }
        public string LocationCode { get; init; } = "";
        public string? LotNumber { get; init; }
        public DateTime? ExpiryDate { get; init; }
        public string ScopeKey { get; init; } = "";
        public DateTime PredictionCutoff { get; init; }
        public DateTime CreatedAt { get; init; }
        public string FeatureSchemaVersion { get; init; } = "";
        public string ModelVersion { get; init; } = "";
        public string FeatureJson { get; init; } = "{}";
        public string FeatureHash { get; init; } = "";
        public string SourceWatermark { get; init; } = "";
        public InventoryRiskDataQualityStatusEnum DataQualityStatus { get; init; }
        public decimal? RuleRiskScore { get; init; }
        public long? RecommendationSheetId { get; init; }
        public int? EstimatedEffortMinutes { get; init; }
    }

    private sealed class OutcomeSeed
    {
        public long StockCountSheetId { get; init; }
        public long StockCountLineId { get; init; }
        public int WarehouseId { get; init; }
        public int LocationWarehouseId { get; init; }
        public string WarehouseCode { get; init; } = "";
        public int? OwnerPartnerId { get; init; }
        public string? OwnerCode { get; init; }
        public int ItemId { get; init; }
        public string ItemCode { get; init; } = "";
        public bool HasBaseUom { get; init; }
        public string BaseUomCode { get; init; } = "";
        public bool TrackLot { get; init; }
        public bool TrackExpiry { get; init; }
        public bool TrackSerial { get; init; }
        public int LocationId { get; init; }
        public string LocationCode { get; init; } = "";
        public string? LotNumber { get; init; }
        public DateTime? ExpiryDate { get; init; }
        public decimal SystemQty { get; init; }
        public decimal? CountedQty { get; init; }
        public decimal? StoredVariance { get; init; }
        public DateTime? CountedAt { get; init; }
        public byte LineStatus { get; init; }
        public StockCountStatusEnum SheetStatus { get; init; }
        public DateTime? ApprovedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public DateTime? UnlockedAt { get; init; }
        public long? GeneratedAdjustmentVoucherId { get; init; }
        public bool AdjustmentVoucherIsValid { get; init; }
        public decimal AdjustmentDetailBaseQty { get; set; }
        public decimal AdjustmentLedgerDelta { get; set; }
        public int AdjustmentLedgerEventCount { get; set; }
    }

    private sealed class AdjustmentDetailSeed
    {
        public long VoucherId { get; init; }
        public int? OwnerPartnerId { get; init; }
        public int ItemId { get; init; }
        public int LocationId { get; init; }
        public string? LotNumber { get; init; }
        public DateTime? ExpiryDate { get; init; }
        public decimal BaseQty { get; init; }
    }

    private sealed class AdjustmentLedgerSeed
    {
        public string StockCountSheetReference { get; init; } = "";
        public int? OwnerPartnerId { get; init; }
        public int ItemId { get; init; }
        public int LocationId { get; init; }
        public string? LotNumber { get; init; }
        public DateTime? ExpiryDate { get; init; }
        public decimal QuantityDelta { get; init; }
    }
}
