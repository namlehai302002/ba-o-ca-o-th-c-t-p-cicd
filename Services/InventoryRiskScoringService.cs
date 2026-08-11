using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Services;

public sealed class InventoryRiskRuleOptions
{
    public const string SectionName = "InventoryRisk:RuleBaseline";

    public string Version { get; set; } = "RULE-BASELINE-1.0";
    public string FeatureSchemaVersion { get; set; } = "AI-FEATURE-SCHEMA-0.1";
    public int HistoryWindowDays { get; set; } = 180;
    public int FreshnessMinutes { get; set; } = 60;
    public int MovementHighWatermark90D { get; set; } = 50;
    public int CountStalenessHighWatermarkDays { get; set; } = 180;
    public decimal AdjustmentWeight { get; set; } = 25m;
    public decimal PriorVarianceWeight { get; set; } = 25m;
    public decimal MovementWeight { get; set; } = 20m;
    public decimal CountStalenessWeight { get; set; } = 20m;
    public decimal ComplexityWeight { get; set; } = 10m;
    public decimal MediumThreshold { get; set; } = 35m;
    public decimal HighThreshold { get; set; } = 60m;
    public decimal CriticalThreshold { get; set; } = 80m;
}

public interface IInventoryRiskScoringService
{
    Task<InventoryRiskPageViewModel> BuildPageAsync(InventoryRiskQuery query, CancellationToken ct = default);
    Task<InventoryRiskShadowPersistResult> PersistShadowBatchAsync(InventoryRiskQuery query, string actor, CancellationToken ct = default);
    Task<InventoryRiskFreshnessFingerprint?> BuildFreshnessFingerprintAsync(
        int warehouseId,
        int? ownerPartnerId,
        string scopeKey,
        CancellationToken ct = default);
    Task<bool> IsPersistenceAvailableAsync(CancellationToken ct = default);
}

public sealed record InventoryRiskFreshnessFingerprint(string FeatureHash, string SourceWatermark);

public sealed class InventoryRiskScoringService : IInventoryRiskScoringService
{
    private const decimal QuantityTolerance = 0.0001m;
    private const string ModelKey = "inventory-discrepancy-risk";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly AppDbContext _db;
    private readonly InventoryRiskRuleOptions _options;

    public InventoryRiskScoringService(AppDbContext db, IOptions<InventoryRiskRuleOptions>? options = null)
    {
        _db = db;
        _options = NormalizeOptions(options?.Value ?? new InventoryRiskRuleOptions());
    }

    public async Task<InventoryRiskPageViewModel> BuildPageAsync(InventoryRiskQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var build = await BuildRowsAsync(query, ct);
        IEnumerable<InventoryRiskRowViewModel> filtered = build.Rows;

        if (query.Severity.HasValue)
            filtered = filtered.Where(row => row.Severity == query.Severity.Value);
        if (query.DataQualityStatus.HasValue)
            filtered = filtered.Where(row => row.DataQualityStatus == query.DataQualityStatus.Value);

        var ordered = filtered
            .OrderBy(row => row.DataQualityStatus == InventoryRiskDataQualityStatusEnum.Blocked ? 1 : 0)
            .ThenByDescending(row => row.RiskScore ?? decimal.MinValue)
            .ThenBy(row => row.WarehouseCode, StringComparer.Ordinal)
            .ThenBy(row => row.OwnerName, StringComparer.Ordinal)
            .ThenBy(row => row.ItemCode, StringComparer.Ordinal)
            .ThenBy(row => row.LocationCode, StringComparer.Ordinal)
            .ThenBy(row => row.LotNumber ?? "", StringComparer.Ordinal)
            .ToList();

        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        var totalCount = ordered.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);
        var scoredCount = ordered.Count(row => row.RiskScore.HasValue);
        var blockedCount = ordered.Count(row => row.DataQualityStatus == InventoryRiskDataQualityStatusEnum.Blocked);

        return new InventoryRiskPageViewModel
        {
            WarehouseId = query.WarehouseId,
            OwnerPartnerId = query.OwnerPartnerId,
            ZoneId = query.ZoneId,
            Severity = query.Severity,
            DataQualityStatus = query.DataQualityStatus,
            Search = query.Search?.Trim() ?? "",
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            PredictionCutoff = build.PredictionCutoff,
            FreshUntil = build.FreshUntil,
            RuleVersion = _options.Version,
            FeatureSchemaVersion = _options.FeatureSchemaVersion,
            IsShadowMode = true,
            PersistenceAvailable = await IsPersistenceAvailableAsync(ct),
            ScoredCount = scoredCount,
            BlockedCount = blockedCount,
            PartialCount = ordered.Count(row => row.DataQualityStatus == InventoryRiskDataQualityStatusEnum.Partial),
            CoveragePercent = totalCount == 0 ? 0m : Math.Round(scoredCount * 100m / totalCount, 1),
            Rows = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList()
        };
    }

    public async Task<InventoryRiskFreshnessFingerprint?> BuildFreshnessFingerprintAsync(
        int warehouseId,
        int? ownerPartnerId,
        string scopeKey,
        CancellationToken ct = default)
    {
        if (warehouseId <= 0 || string.IsNullOrWhiteSpace(scopeKey))
            return null;

        var build = await BuildRowsAsync(new InventoryRiskQuery
        {
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerPartnerId,
            Page = 1,
            PageSize = 100
        }, ct);
        var row = build.Rows.SingleOrDefault(candidate =>
            string.Equals(candidate.ScopeKey, scopeKey, StringComparison.Ordinal));
        return row == null
            ? null
            : new InventoryRiskFreshnessFingerprint(row.FeatureHash, row.SourceWatermark);
    }

    public async Task<InventoryRiskShadowPersistResult> PersistShadowBatchAsync(InventoryRiskQuery query, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await IsPersistenceAvailableAsync(ct))
        {
            throw new BusinessRuleException(
                "Chưa có schema lưu lịch sử chấm điểm kiểm kê thông minh.",
                "INVENTORY_RISK_SCHEMA_NOT_READY",
                nameof(InventoryRiskFeatureSnapshot));
        }

        var build = await BuildRowsAsync(query, ct);
        var batchId = Guid.NewGuid();
        var persistedAt = VietnamTime.Now;
        var normalizedActor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
        var configurationJson = SerializeConfiguration(_options);
        var artifactHash = Sha256(configurationJson);

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        var modelVersion = await _db.InventoryRiskModelVersions
            .SingleOrDefaultAsync(row => row.ModelKey == ModelKey && row.Version == _options.Version, ct);
        var championCount = await _db.InventoryRiskModelVersions
            .CountAsync(row => row.ModelKey == ModelKey
                && row.LifecycleStatus == InventoryRiskModelLifecycleStatusEnum.Champion, ct);
        if (championCount > 1)
        {
            throw new BusinessRuleException(
                "Sổ đăng ký mô hình đang có nhiều phiên bản được đánh dấu vận hành. Cần quản trị viên xử lý trước khi chấm điểm tiếp.",
                "INVENTORY_RISK_MULTIPLE_CHAMPIONS",
                nameof(InventoryRiskModelVersion));
        }

        if (modelVersion == null)
        {
            modelVersion = new InventoryRiskModelVersion
            {
                ModelKey = ModelKey,
                Version = _options.Version,
                ModelType = InventoryRiskModelTypeEnum.RuleBaseline,
                LifecycleStatus = championCount == 0
                    ? InventoryRiskModelLifecycleStatusEnum.Champion
                    : InventoryRiskModelLifecycleStatusEnum.Challenger,
                FeatureSchemaVersion = _options.FeatureSchemaVersion,
                ConfigurationJson = configurationJson,
                ArtifactHash = artifactHash,
                CreatedBy = normalizedActor,
                CreatedAt = persistedAt
            };
            _db.InventoryRiskModelVersions.Add(modelVersion);
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.Equals(modelVersion.ArtifactHash, artifactHash, StringComparison.Ordinal))
        {
            throw new BusinessRuleException(
                "Phiên bản quy tắc đã tồn tại nhưng cấu hình không khớp. Hãy tăng phiên bản trước khi chấm lại.",
                "INVENTORY_RISK_VERSION_CONFIGURATION_MISMATCH",
                nameof(InventoryRiskModelVersion));
        }
        else if (modelVersion.LifecycleStatus == InventoryRiskModelLifecycleStatusEnum.Retired)
        {
            throw new BusinessRuleException(
                "Phiên bản quy tắc này đã ngừng sử dụng. Hãy chọn phiên bản đang vận hành hoặc tạo một challenger mới.",
                "INVENTORY_RISK_MODEL_RETIRED",
                nameof(InventoryRiskModelVersion));
        }

        foreach (var row in build.Rows)
        {
            var snapshot = new InventoryRiskFeatureSnapshot
            {
                InventoryRiskModelVersionId = modelVersion.InventoryRiskModelVersionId,
                BatchId = batchId,
                PredictionCutoff = build.PredictionCutoff,
                WarehouseId = row.WarehouseId,
                OwnerPartnerId = row.OwnerPartnerId,
                ItemId = row.ItemId,
                LocationId = row.LocationId,
                LotNumber = row.LotNumber,
                ExpiryDate = row.ExpiryDate,
                ScopeKey = row.ScopeKey,
                FeatureJson = row.FeatureJson,
                FeatureHash = row.FeatureHash,
                SourceWatermark = row.SourceWatermark,
                DataQualityStatus = row.DataQualityStatus,
                DataQualityCodes = string.Join(',', row.DataQualityCodes),
                CreatedAt = persistedAt
            };

            snapshot.Prediction = new InventoryRiskPrediction
            {
                InventoryRiskModelVersionId = modelVersion.InventoryRiskModelVersionId,
                RiskScore = row.RiskScore,
                Severity = row.Severity,
                ReasonCodesJson = JsonSerializer.Serialize(row.Reasons, JsonOptions),
                GeneratedAt = persistedAt,
                FreshUntil = build.FreshUntil,
                IsShadowMode = true,
                OutputHash = row.OutputHash
            };
            _db.InventoryRiskFeatureSnapshots.Add(snapshot);
        }

        await _db.SaveChangesAsync(ct);
        if (transaction != null)
            await transaction.CommitAsync(ct);

        return new InventoryRiskShadowPersistResult
        {
            BatchId = batchId,
            SnapshotCount = build.Rows.Count,
            PredictionCount = build.Rows.Count,
            RuleVersion = _options.Version,
            PredictionCutoff = build.PredictionCutoff
        };
    }

    public async Task<bool> IsPersistenceAvailableAsync(CancellationToken ct = default)
    {
        if (!_db.Database.IsRelational())
            return true;

        var provider = _db.Database.ProviderName ?? "";
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText = """
                    SELECT CASE WHEN
                        OBJECT_ID(N'[InventoryRiskModelVersions]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[InventoryRiskFeatureSnapshots]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[InventoryRiskPredictions]', N'U') IS NOT NULL
                    THEN 1 ELSE 0 END
                    """;
            }
            else if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText = """
                    SELECT CASE WHEN COUNT(*) = 3 THEN 1 ELSE 0 END
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name IN ('InventoryRiskModelVersions', 'InventoryRiskFeatureSnapshots', 'InventoryRiskPredictions')
                    """;
            }
            else
            {
                return false;
            }

            var result = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task<RiskBuildResult> BuildRowsAsync(InventoryRiskQuery query, CancellationToken ct)
    {
        var cutoff = query.PredictionCutoff ?? VietnamTime.Now;
        var from180 = cutoff.AddDays(-_options.HistoryWindowDays);
        var from90 = cutoff.AddDays(-90);
        var from30 = cutoff.AddDays(-30);
        var ownerScope = query.AllowedOwnerPartnerIds.Distinct().ToList();
        var normalizedSearch = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        var stockQuery = _db.ItemLocations.AsNoTracking()
            .Where(row => row.Item != null && row.Item.IsActive
                && row.Location != null && row.Location.IsActive
                && row.Location.Zone != null && row.Location.Zone.IsActive
                && row.Location.Zone.Warehouse != null && row.Location.Zone.Warehouse.IsActive
                && (row.Quantity != 0 || row.ReservedQty != 0));

        if (query.WarehouseId.HasValue)
            stockQuery = stockQuery.Where(row => row.Location!.Zone.WarehouseId == query.WarehouseId.Value);
        if (query.ZoneId.HasValue)
            stockQuery = stockQuery.Where(row => row.Location!.ZoneId == query.ZoneId.Value);
        if (ownerScope.Count > 0)
            stockQuery = stockQuery.Where(row => row.OwnerPartnerId.HasValue && ownerScope.Contains(row.OwnerPartnerId.Value));
        if (query.OwnerPartnerId.HasValue)
            stockQuery = stockQuery.Where(row => row.OwnerPartnerId == query.OwnerPartnerId.Value);
        if (normalizedSearch != null)
        {
            stockQuery = stockQuery.Where(row => row.Item!.ItemCode.Contains(normalizedSearch)
                || row.Item.ItemName.Contains(normalizedSearch)
                || row.Location!.LocationCode.Contains(normalizedSearch)
                || (row.LotNumber != null && row.LotNumber.Contains(normalizedSearch)));
        }

        var stockRows = await stockQuery
            .Select(row => new StockSeed
            {
                ItemLocationId = row.ItemLocationId,
                WarehouseId = row.Location!.Zone.WarehouseId,
                WarehouseCode = row.Location.Zone.Warehouse.WarehouseCode,
                OwnerPartnerId = row.OwnerPartnerId,
                OwnerName = row.OwnerPartner != null ? row.OwnerPartner.PartnerName : "Không quản lý chủ hàng",
                ZoneId = row.Location.ZoneId,
                ZoneCode = row.Location.Zone.ZoneCode,
                ItemId = row.ItemId,
                ItemCode = row.Item!.ItemCode,
                ItemName = row.Item.ItemName,
                ItemAbcClass = row.Item.AbcClass,
                TrackLot = row.Item.TrackLot,
                TrackExpiry = row.Item.TrackExpiry,
                TrackSerial = row.Item.TrackSerial,
                LocationId = row.LocationId,
                LocationCode = row.Location.LocationCode,
                LotNumber = row.LotNumber,
                ExpiryDate = row.ExpiryDate,
                HoldStatus = row.HoldStatus,
                Quantity = row.Quantity,
                ReservedQty = row.ReservedQty,
                UpdatedAt = row.UpdatedAt
            })
            .ToListAsync(ct);

        if (stockRows.Count == 0)
            return new RiskBuildResult(cutoff, cutoff.AddMinutes(_options.FreshnessMinutes), new List<InventoryRiskRowViewModel>());

        var stockGroups = stockRows
            .GroupBy(row => ScopeKey(row.WarehouseId, row.OwnerPartnerId, row.ItemId, row.LocationId, row.LotNumber, row.ExpiryDate))
            .ToList();
        var itemIds = stockRows.Select(row => row.ItemId).Distinct().ToList();
        var locationIds = stockRows.Select(row => row.LocationId).Distinct().ToList();

        var transactionQuery = _db.InventoryTransactions.AsNoTracking()
            .Where(row => row.TransactionAt >= from180 && row.TransactionAt <= cutoff
                && itemIds.Contains(row.ItemId) && locationIds.Contains(row.LocationId));
        if (query.WarehouseId.HasValue)
            transactionQuery = transactionQuery.Where(row => row.WarehouseId == query.WarehouseId.Value);
        if (ownerScope.Count > 0)
            transactionQuery = transactionQuery.Where(row => row.OwnerPartnerId.HasValue && ownerScope.Contains(row.OwnerPartnerId.Value));
        if (query.OwnerPartnerId.HasValue)
            transactionQuery = transactionQuery.Where(row => row.OwnerPartnerId == query.OwnerPartnerId.Value);

        var transactions = await transactionQuery
            .Select(row => new TransactionSeed
            {
                InventoryTransactionId = row.InventoryTransactionId,
                WarehouseId = row.WarehouseId,
                OwnerPartnerId = row.OwnerPartnerId,
                ItemId = row.ItemId,
                LocationId = row.LocationId,
                LotNumber = row.LotNumber,
                ExpiryDate = row.ExpiryDate,
                TransactionType = row.TransactionType,
                QuantityDelta = row.QuantityDelta,
                QuantityAfter = row.QuantityAfter,
                Actor = row.Actor,
                TransactionAt = row.TransactionAt
            })
            .ToListAsync(ct);

        var countQuery = _db.StockCountLines.AsNoTracking()
            .Where(line => line.StockCountSheet != null
                && line.StockCountSheet.Status == StockCountStatusEnum.Approved
                && line.StockCountSheet.ApprovedAt.HasValue
                && line.StockCountSheet.ApprovedAt.Value >= from180
                && line.StockCountSheet.ApprovedAt.Value <= cutoff
                && itemIds.Contains(line.ItemId)
                && locationIds.Contains(line.LocationId));
        if (query.WarehouseId.HasValue)
            countQuery = countQuery.Where(line => line.StockCountSheet!.WarehouseId == query.WarehouseId.Value);
        if (ownerScope.Count > 0)
            countQuery = countQuery.Where(line => line.OwnerPartnerId.HasValue && ownerScope.Contains(line.OwnerPartnerId.Value));
        if (query.OwnerPartnerId.HasValue)
            countQuery = countQuery.Where(line => line.OwnerPartnerId == query.OwnerPartnerId.Value);

        var countRows = await countQuery
            .Select(line => new CountSeed
            {
                StockCountSheetId = line.StockCountSheetId,
                WarehouseId = line.StockCountSheet!.WarehouseId,
                OwnerPartnerId = line.OwnerPartnerId,
                ItemId = line.ItemId,
                LocationId = line.LocationId,
                LotNumber = line.LotNumber,
                ExpiryDate = line.ExpiryDate,
                SystemQty = line.SystemQty,
                CountedQty = line.CountedQty,
                Variance = line.Variance,
                ApprovedAt = line.StockCountSheet.ApprovedAt!.Value
            })
            .ToListAsync(ct);

        var scheduleQuery = _db.CycleCountSchedules.AsNoTracking()
            .Where(schedule => schedule.IsActive && itemIds.Contains(schedule.ItemId) && locationIds.Contains(schedule.LocationId));
        if (query.WarehouseId.HasValue)
            scheduleQuery = scheduleQuery.Where(schedule => schedule.Program != null && schedule.Program.WarehouseId == query.WarehouseId.Value);
        if (ownerScope.Count > 0)
            scheduleQuery = scheduleQuery.Where(schedule => schedule.OwnerPartnerId.HasValue && ownerScope.Contains(schedule.OwnerPartnerId.Value));
        if (query.OwnerPartnerId.HasValue)
            scheduleQuery = scheduleQuery.Where(schedule => schedule.OwnerPartnerId == query.OwnerPartnerId.Value);

        var schedules = await scheduleQuery
            .Select(schedule => new ScheduleSeed
            {
                WarehouseId = schedule.Program!.WarehouseId,
                OwnerPartnerId = schedule.OwnerPartnerId,
                ItemId = schedule.ItemId,
                LocationId = schedule.LocationId,
                AbcClass = schedule.AbcClass,
                LastCountedAt = schedule.LastCountedAt.HasValue && schedule.LastCountedAt.Value <= cutoff
                    ? schedule.LastCountedAt
                    : null
            })
            .ToListAsync(ct);

        var transactionGroups = transactions
            .GroupBy(row => ScopeKey(row.WarehouseId, row.OwnerPartnerId, row.ItemId, row.LocationId, row.LotNumber, row.ExpiryDate))
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.TransactionAt).ThenBy(row => row.InventoryTransactionId).ToList());
        var countGroups = countRows
            .GroupBy(row => ScopeKey(row.WarehouseId, row.OwnerPartnerId, row.ItemId, row.LocationId, row.LotNumber, row.ExpiryDate))
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.ApprovedAt).ThenBy(row => row.StockCountSheetId).ToList());
        var scheduleGroups = schedules
            .GroupBy(row => ScheduleKey(row.WarehouseId, row.OwnerPartnerId, row.ItemId, row.LocationId))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.LastCountedAt).First());
        var locationMovement30 = transactions
            .Where(row => row.TransactionAt >= from30 && Math.Abs(row.QuantityDelta) > QuantityTolerance)
            .GroupBy(row => (row.WarehouseId, row.OwnerPartnerId, row.LocationId))
            .ToDictionary(group => group.Key, group => group.Count());
        var locationSkuCount = stockRows
            .Where(row => row.Quantity > QuantityTolerance)
            .GroupBy(row => (row.WarehouseId, row.OwnerPartnerId, row.LocationId))
            .ToDictionary(group => group.Key, group => group.Select(row => row.ItemId).Distinct().Count());
        var locationLotCount = stockRows
            .Where(row => row.Quantity > QuantityTolerance)
            .GroupBy(row => (row.WarehouseId, row.OwnerPartnerId, row.ItemId, row.LocationId))
            .ToDictionary(group => group.Key, group => group.Select(row => (NormalizeLot(row.LotNumber), row.ExpiryDate?.Date)).Distinct().Count());

        var result = new List<InventoryRiskRowViewModel>(stockGroups.Count);
        foreach (var stockGroup in stockGroups)
        {
            var first = stockGroup.First();
            var key = stockGroup.Key;
            var movementRows = transactionGroups.GetValueOrDefault(key) ?? new List<TransactionSeed>();
            var approvedCounts = countGroups.GetValueOrDefault(key) ?? new List<CountSeed>();
            scheduleGroups.TryGetValue(ScheduleKey(first.WarehouseId, first.OwnerPartnerId, first.ItemId, first.LocationId), out var schedule);

            var onHandQty = stockGroup.Sum(row => row.Quantity);
            var reservedQty = stockGroup.Sum(row => row.ReservedQty);
            var availableQty = stockGroup
                .Where(row => row.HoldStatus is InventoryHoldStatusEnum.Available or InventoryHoldStatusEnum.Consigned)
                .Sum(row => row.Quantity - row.ReservedQty);
            var heldQty = stockGroup
                .Where(row => row.HoldStatus is not (InventoryHoldStatusEnum.Available or InventoryHoldStatusEnum.Consigned))
                .Sum(row => Math.Max(0m, row.Quantity));
            var lastApprovedAt = MaxDate(schedule?.LastCountedAt, approvedCounts.LastOrDefault()?.ApprovedAt);
            var priorVarianceRows = approvedCounts.Where(row => Math.Abs(ResolveVariance(row)) > QuantityTolerance).ToList();
            var latestMovement = movementRows.LastOrDefault();
            var latestStockUpdate = stockGroup.Max(row => row.UpdatedAt);
            var dataQualityCodes = BuildDataQualityCodes(
                stockGroup.ToList(),
                latestMovement,
                onHandQty,
                reservedQty,
                approvedCounts.Count,
                cutoff);
            var dataQualityStatus = dataQualityCodes.Any(code => code.StartsWith("BLOCKED_", StringComparison.Ordinal))
                ? InventoryRiskDataQualityStatusEnum.Blocked
                : dataQualityCodes.Count > 0
                    ? InventoryRiskDataQualityStatusEnum.Partial
                    : InventoryRiskDataQualityStatusEnum.Ok;

            var lastReceipt = movementRows
                .Where(IsInboundMovement)
                .Select(row => (DateTime?)row.TransactionAt)
                .Max();
            var lastOutbound = movementRows
                .Where(IsOutboundMovement)
                .Select(row => (DateTime?)row.TransactionAt)
                .Max();
            var daysSinceCount = lastApprovedAt.HasValue ? Math.Max(0, (cutoff.Date - lastApprovedAt.Value.Date).Days) : (int?)null;
            var locationKey = (first.WarehouseId, first.OwnerPartnerId, first.LocationId);
            var itemLocationKey = (first.WarehouseId, first.OwnerPartnerId, first.ItemId, first.LocationId);

            var features = new InventoryRiskFeatureVector
            {
                OnHandBaseQty = onHandQty,
                ReservedBaseQty = reservedQty,
                AvailableBaseQty = availableQty,
                MovementCount30D = movementRows.Count(row => row.TransactionAt >= from30 && Math.Abs(row.QuantityDelta) > QuantityTolerance),
                MovementCount90D = movementRows.Count(row => row.TransactionAt >= from90 && Math.Abs(row.QuantityDelta) > QuantityTolerance),
                AdjustmentAbsQty90D = movementRows
                    .Where(row => row.TransactionAt >= from90 && row.TransactionType == InventoryTransactionTypeEnum.Adjust)
                    .Sum(row => Math.Abs(row.QuantityDelta)),
                TransactionActorCount30D = movementRows
                    .Where(row => row.TransactionAt >= from30 && !string.IsNullOrWhiteSpace(row.Actor))
                    .Select(row => row.Actor)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                DaysSinceLastApprovedCount = daysSinceCount,
                PriorCountCount180D = approvedCounts.Count,
                PriorVarianceRate180D = approvedCounts.Count == 0 ? null : priorVarianceRows.Count / (decimal)approvedCounts.Count,
                PriorAbsVarianceQty180D = approvedCounts.Sum(row => Math.Abs(ResolveVariance(row))),
                AbcClass = schedule == null ? first.ItemAbcClass : schedule.AbcClass.ToString(),
                DaysSinceLastReceipt = lastReceipt.HasValue ? Math.Max(0, (cutoff.Date - lastReceipt.Value.Date).Days) : null,
                DaysSinceLastOutbound = lastOutbound.HasValue ? Math.Max(0, (cutoff.Date - lastOutbound.Value.Date).Days) : null,
                LocationMovementCount30D = locationMovement30.GetValueOrDefault(locationKey),
                LocationDistinctSkuCount = locationSkuCount.GetValueOrDefault(locationKey),
                LotCountAtLocation = locationLotCount.GetValueOrDefault(itemLocationKey),
                DaysToExpiry = first.ExpiryDate.HasValue ? (first.ExpiryDate.Value.Date - cutoff.Date).Days : null,
                LotTrackingFlag = first.TrackLot,
                ExpiryTrackingFlag = first.TrackExpiry,
                SerialTrackingFlag = first.TrackSerial,
                HoldQtyRatio = onHandQty > QuantityTolerance ? Math.Clamp(heldQty / onHandQty, 0m, 1m) : null
            };

            var featureJson = JsonSerializer.Serialize(features, JsonOptions);
            var featureHash = Sha256(featureJson);
            var scoring = Score(features, dataQualityStatus);
            var outputHash = Sha256(string.Join('|',
                _options.Version,
                featureHash,
                scoring.Score?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "BLOCKED",
                scoring.Severity?.ToString() ?? "BLOCKED",
                string.Join(',', scoring.Reasons.Select(reason => reason.Code))));
            // FeatureHash covers quantities, reservations, holds, movement/count history,
            // item tracking flags, schedules and location complexity for this exact grain.
            var sourceWatermark = $"feature:{featureHash};ledger:{latestMovement?.InventoryTransactionId ?? 0};count:{approvedCounts.LastOrDefault()?.StockCountSheetId ?? 0}";

            result.Add(new InventoryRiskRowViewModel
            {
                WarehouseId = first.WarehouseId,
                WarehouseCode = first.WarehouseCode,
                OwnerPartnerId = first.OwnerPartnerId,
                OwnerName = first.OwnerName,
                ZoneId = first.ZoneId,
                ZoneCode = first.ZoneCode,
                ItemId = first.ItemId,
                ItemCode = first.ItemCode,
                ItemName = first.ItemName,
                LocationId = first.LocationId,
                LocationCode = first.LocationCode,
                LotNumber = string.IsNullOrWhiteSpace(first.LotNumber) ? null : first.LotNumber.Trim(),
                ExpiryDate = first.ExpiryDate?.Date,
                OnHandQty = onHandQty,
                ReservedQty = reservedQty,
                AvailableQty = availableQty,
                AbcClass = string.IsNullOrWhiteSpace(features.AbcClass) ? "Chưa phân hạng" : features.AbcClass!,
                RiskScore = scoring.Score,
                Severity = scoring.Severity,
                DataQualityStatus = dataQualityStatus,
                DataQualityCodes = dataQualityCodes,
                LastApprovedCountAt = lastApprovedAt,
                DaysSinceLastApprovedCount = daysSinceCount,
                SourceWatermark = sourceWatermark,
                ScopeKey = key,
                FeatureJson = featureJson,
                FeatureHash = featureHash,
                OutputHash = outputHash,
                Reasons = scoring.Reasons
            });
        }

        return new RiskBuildResult(cutoff, cutoff.AddMinutes(_options.FreshnessMinutes), result);
    }

    private ScoringResult Score(InventoryRiskFeatureVector features, InventoryRiskDataQualityStatusEnum dataQualityStatus)
    {
        if (dataQualityStatus == InventoryRiskDataQualityStatusEnum.Blocked)
            return new ScoringResult(null, null, new List<InventoryRiskReasonViewModel>());

        var reasons = new List<InventoryRiskReasonViewModel>();
        var stockDenominator = Math.Max(Math.Abs(features.OnHandBaseQty), 1m);
        var adjustmentContribution = _options.AdjustmentWeight
            * Math.Clamp(features.AdjustmentAbsQty90D / stockDenominator, 0m, 1m);
        AddReason(reasons, "RECENT_ADJUSTMENTS", "Có điều chỉnh tồn gần đây", $"Tổng điều chỉnh tuyệt đối 90 ngày: {features.AdjustmentAbsQty90D:N4}", adjustmentContribution);

        var varianceContribution = _options.PriorVarianceWeight * (features.PriorVarianceRate180D ?? 0m);
        AddReason(reasons, "HISTORICAL_VARIANCE", "Từng phát sinh sai lệch kiểm kê", $"Tỷ lệ dòng lệch: {(features.PriorVarianceRate180D ?? 0m):P1}", varianceContribution);

        var movementContribution = _options.MovementWeight * Math.Clamp(
            features.MovementCount90D / (decimal)_options.MovementHighWatermark90D,
            0m,
            1m);
        AddReason(reasons, "HIGH_MOVEMENT", "Tần suất biến động cao", $"{features.MovementCount90D} giao dịch vật lý trong 90 ngày", movementContribution);

        var stalenessContribution = features.DaysSinceLastApprovedCount.HasValue
            ? _options.CountStalenessWeight * Math.Clamp(
                features.DaysSinceLastApprovedCount.Value / (decimal)_options.CountStalenessHighWatermarkDays,
                0m,
                1m)
            : _options.CountStalenessWeight;
        AddReason(
            reasons,
            features.DaysSinceLastApprovedCount.HasValue ? "COUNT_OVERDUE" : "NEVER_COUNTED",
            features.DaysSinceLastApprovedCount.HasValue ? "Đã lâu chưa kiểm kê" : "Chưa có lần kiểm kê được duyệt",
            features.DaysSinceLastApprovedCount.HasValue
                ? $"{features.DaysSinceLastApprovedCount.Value} ngày từ lần duyệt gần nhất"
                : "Không có mốc kiểm kê được duyệt",
            stalenessContribution);

        var complexityFactors = 0;
        if (features.LotTrackingFlag) complexityFactors++;
        if (features.ExpiryTrackingFlag) complexityFactors++;
        if (features.SerialTrackingFlag) complexityFactors++;
        if (features.LotCountAtLocation > 1) complexityFactors++;
        if (features.LocationDistinctSkuCount > 1) complexityFactors++;
        var complexityContribution = _options.ComplexityWeight * complexityFactors / 5m;
        AddReason(reasons, "TRACKING_COMPLEXITY", "Phạm vi lưu trữ phức tạp", $"{complexityFactors}/5 yếu tố theo dõi hoặc trộn vị trí", complexityContribution);

        var score = Math.Round(Math.Clamp(reasons.Sum(reason => reason.Contribution), 0m, 100m), 4);
        var severity = score >= _options.CriticalThreshold
            ? InventoryRiskSeverityEnum.Critical
            : score >= _options.HighThreshold
                ? InventoryRiskSeverityEnum.High
                : score >= _options.MediumThreshold
                    ? InventoryRiskSeverityEnum.Medium
                    : InventoryRiskSeverityEnum.Low;

        return new ScoringResult(
            score,
            severity,
            reasons.OrderByDescending(reason => reason.Contribution).ThenBy(reason => reason.Code, StringComparer.Ordinal).Take(5).ToList());
    }

    private static void AddReason(List<InventoryRiskReasonViewModel> reasons, string code, string label, string evidence, decimal contribution)
    {
        if (contribution <= 0m)
            return;

        reasons.Add(new InventoryRiskReasonViewModel
        {
            Code = code,
            Label = label,
            Evidence = evidence,
            Contribution = Math.Round(contribution, 4)
        });
    }

    private static List<string> BuildDataQualityCodes(
        List<StockSeed> stockRows,
        TransactionSeed? latestMovement,
        decimal onHandQty,
        decimal reservedQty,
        int approvedCount,
        DateTime cutoff)
    {
        var first = stockRows[0];
        var codes = new List<string>();
        if (stockRows.Any(row => row.Quantity < -QuantityTolerance || row.ReservedQty < -QuantityTolerance))
            codes.Add("BLOCKED_NEGATIVE_BALANCE");
        if (reservedQty > onHandQty + QuantityTolerance)
            codes.Add("BLOCKED_OVER_RESERVED");
        if (first.TrackLot && string.IsNullOrWhiteSpace(first.LotNumber))
            codes.Add("BLOCKED_TRACKED_LOT_MISSING");
        if (first.TrackExpiry && !first.ExpiryDate.HasValue)
            codes.Add("BLOCKED_TRACKED_EXPIRY_MISSING");
        if (stockRows.Any(row => row.UpdatedAt > cutoff.AddSeconds(1)))
            codes.Add("BLOCKED_BALANCE_AFTER_CUTOFF");
        if (stockRows.Count == 1 && latestMovement != null && Math.Abs(latestMovement.QuantityAfter - onHandQty) > QuantityTolerance)
            codes.Add("BLOCKED_LEDGER_BALANCE_MISMATCH");
        if (stockRows.Select(row => row.HoldStatus).Distinct().Count() > 1)
            codes.Add("BLOCKED_MULTIPLE_HOLD_BUCKETS");
        if (latestMovement == null)
            codes.Add("PARTIAL_LEDGER_HISTORY_MISSING");
        if (approvedCount == 0)
            codes.Add("PARTIAL_COUNT_HISTORY_MISSING");
        if (first.TrackSerial)
            codes.Add("BLOCKED_SERIAL_COUNT_NOT_SUPPORTED");
        return codes.Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToList();
    }

    private static decimal ResolveVariance(CountSeed row)
        => row.Variance ?? (row.CountedQty.HasValue ? row.CountedQty.Value - row.SystemQty : 0m);

    private static bool IsInboundMovement(TransactionSeed row)
        => row.QuantityDelta > QuantityTolerance && row.TransactionType is
            InventoryTransactionTypeEnum.Receive or
            InventoryTransactionTypeEnum.TransferIn or
            InventoryTransactionTypeEnum.KitProduce;

    private static bool IsOutboundMovement(TransactionSeed row)
        => row.QuantityDelta < -QuantityTolerance && row.TransactionType is
            InventoryTransactionTypeEnum.Ship or
            InventoryTransactionTypeEnum.TransferOut or
            InventoryTransactionTypeEnum.KitConsume or
            InventoryTransactionTypeEnum.VasConsume;

    private static DateTime? MaxDate(DateTime? left, DateTime? right)
        => !left.HasValue ? right : !right.HasValue ? left : left.Value >= right.Value ? left : right;

    private static InventoryRiskRuleOptions NormalizeOptions(InventoryRiskRuleOptions source)
    {
        var weights = source.AdjustmentWeight + source.PriorVarianceWeight + source.MovementWeight
            + source.CountStalenessWeight + source.ComplexityWeight;
        if (weights != 100m)
            throw new InvalidOperationException("Tổng trọng số rule baseline kiểm kê thông minh phải bằng 100.");
        if (source.HistoryWindowDays < 90 || source.FreshnessMinutes <= 0
            || source.MovementHighWatermark90D <= 0 || source.CountStalenessHighWatermarkDays <= 0)
            throw new InvalidOperationException("Cấu hình cửa sổ dữ liệu và freshness của rule baseline không hợp lệ.");
        if (!(source.MediumThreshold >= 0m && source.MediumThreshold < source.HighThreshold
            && source.HighThreshold < source.CriticalThreshold && source.CriticalThreshold <= 100m))
            throw new InvalidOperationException("Ngưỡng mức rủi ro của rule baseline không hợp lệ.");
        if (string.IsNullOrWhiteSpace(source.Version) || string.IsNullOrWhiteSpace(source.FeatureSchemaVersion))
            throw new InvalidOperationException("Rule version và feature schema version là bắt buộc.");
        return source;
    }

    private static string SerializeConfiguration(InventoryRiskRuleOptions options)
        => JsonSerializer.Serialize(new
        {
            options.Version,
            options.FeatureSchemaVersion,
            options.HistoryWindowDays,
            options.FreshnessMinutes,
            options.MovementHighWatermark90D,
            options.CountStalenessHighWatermarkDays,
            options.AdjustmentWeight,
            options.PriorVarianceWeight,
            options.MovementWeight,
            options.CountStalenessWeight,
            options.ComplexityWeight,
            options.MediumThreshold,
            options.HighThreshold,
            options.CriticalThreshold
        }, JsonOptions);

    private static string ScopeKey(int warehouseId, int? ownerPartnerId, int itemId, int locationId, string? lotNumber, DateTime? expiryDate)
        => string.Join('|',
            warehouseId.ToString(CultureInfo.InvariantCulture),
            ownerPartnerId?.ToString(CultureInfo.InvariantCulture) ?? "INTERNAL",
            itemId.ToString(CultureInfo.InvariantCulture),
            locationId.ToString(CultureInfo.InvariantCulture),
            Uri.EscapeDataString(NormalizeLot(lotNumber)),
            expiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "NO_EXPIRY");

    private static string ScheduleKey(int warehouseId, int? ownerPartnerId, int itemId, int locationId)
        => string.Join('|', warehouseId, ownerPartnerId?.ToString(CultureInfo.InvariantCulture) ?? "INTERNAL", itemId, locationId);

    private static string NormalizeLot(string? value)
        => string.IsNullOrWhiteSpace(value) ? "NO_LOT" : value.Trim().ToUpperInvariant();

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record RiskBuildResult(DateTime PredictionCutoff, DateTime FreshUntil, List<InventoryRiskRowViewModel> Rows);
    private sealed record ScoringResult(decimal? Score, InventoryRiskSeverityEnum? Severity, List<InventoryRiskReasonViewModel> Reasons);

    private sealed class StockSeed
    {
        public int ItemLocationId { get; set; }
        public int WarehouseId { get; set; }
        public string WarehouseCode { get; set; } = "";
        public int? OwnerPartnerId { get; set; }
        public string OwnerName { get; set; } = "";
        public int ZoneId { get; set; }
        public string ZoneCode { get; set; } = "";
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string? ItemAbcClass { get; set; }
        public bool TrackLot { get; set; }
        public bool TrackExpiry { get; set; }
        public bool TrackSerial { get; set; }
        public int LocationId { get; set; }
        public string LocationCode { get; set; } = "";
        public string? LotNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public InventoryHoldStatusEnum HoldStatus { get; set; }
        public decimal Quantity { get; set; }
        public decimal ReservedQty { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class TransactionSeed
    {
        public long InventoryTransactionId { get; set; }
        public int WarehouseId { get; set; }
        public int? OwnerPartnerId { get; set; }
        public int ItemId { get; set; }
        public int LocationId { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public InventoryTransactionTypeEnum TransactionType { get; set; }
        public decimal QuantityDelta { get; set; }
        public decimal QuantityAfter { get; set; }
        public string Actor { get; set; } = "";
        public DateTime TransactionAt { get; set; }
    }

    private sealed class CountSeed
    {
        public long StockCountSheetId { get; set; }
        public int WarehouseId { get; set; }
        public int? OwnerPartnerId { get; set; }
        public int ItemId { get; set; }
        public int LocationId { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public decimal SystemQty { get; set; }
        public decimal? CountedQty { get; set; }
        public decimal? Variance { get; set; }
        public DateTime ApprovedAt { get; set; }
    }

    private sealed class ScheduleSeed
    {
        public int WarehouseId { get; set; }
        public int? OwnerPartnerId { get; set; }
        public int ItemId { get; set; }
        public int LocationId { get; set; }
        public char AbcClass { get; set; }
        public DateTime? LastCountedAt { get; set; }
    }
}
