using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Services;

public interface IInventoryRiskRecommendationService
{
    Task<bool> IsPersistenceAvailableAsync(CancellationToken ct = default);
    Task<InventoryRiskRecommendationPageViewModel> BuildPageAsync(InventoryRiskRecommendationQuery query, CancellationToken ct = default);
    Task<InventoryRiskRecommendationGenerationResult> GenerateFromLatestBatchAsync(InventoryRiskQuery query, string actor, CancellationToken ct = default);
    Task DecideAsync(InventoryRiskRecommendationDecisionCommand command, string actor, int? scopedWarehouseId, IReadOnlyList<int> scopedOwnerPartnerIds, CancellationToken ct = default);
    Task<InventoryRiskRecommendationMaterializationResult> MaterializeAsync(long recommendationId, Guid concurrencyToken, string actor, int? scopedWarehouseId, IReadOnlyList<int> scopedOwnerPartnerIds, CancellationToken ct = default);
    Task<bool> SyncSheetStateAsync(long stockCountSheetId, StockCountStatusEnum stockCountState, string actor, string reasonCode, CancellationToken ct = default);
}

public sealed class InventoryRiskRecommendationService : IInventoryRiskRecommendationService
{
    private static readonly HashSet<string> ApprovalReasons = new(StringComparer.Ordinal)
    {
        "RISK_CONFIRMED",
        "ROUTINE_CONTROL",
        "MANAGER_JUDGMENT"
    };

    private static readonly HashSet<string> ModificationReasons = new(StringComparer.Ordinal)
    {
        "WORKLOAD_ADJUSTMENT",
        "SCOPE_PRIORITY_CHANGED",
        "ASSIGNMENT_CHANGED"
    };

    private static readonly HashSet<string> RejectionReasons = new(StringComparer.Ordinal)
    {
        "DUPLICATE_SCOPE",
        "RECENTLY_COUNTED",
        "LOW_BUSINESS_PRIORITY",
        "DATA_ISSUE",
        "OTHER_REVIEWED"
    };

    private static readonly HashSet<(CycleCountRecommendationStateEnum From, CycleCountRecommendationStateEnum To)> AllowedTransitions = new()
    {
        (CycleCountRecommendationStateEnum.Generated, CycleCountRecommendationStateEnum.PendingReview),
        (CycleCountRecommendationStateEnum.Generated, CycleCountRecommendationStateEnum.Expired),
        (CycleCountRecommendationStateEnum.Generated, CycleCountRecommendationStateEnum.Fallback),
        (CycleCountRecommendationStateEnum.Generated, CycleCountRecommendationStateEnum.BlockedByDataQuality),
        (CycleCountRecommendationStateEnum.Fallback, CycleCountRecommendationStateEnum.PendingReview),
        (CycleCountRecommendationStateEnum.Fallback, CycleCountRecommendationStateEnum.BlockedByDataQuality),
        (CycleCountRecommendationStateEnum.PendingReview, CycleCountRecommendationStateEnum.Approved),
        (CycleCountRecommendationStateEnum.PendingReview, CycleCountRecommendationStateEnum.Modified),
        (CycleCountRecommendationStateEnum.PendingReview, CycleCountRecommendationStateEnum.Rejected),
        (CycleCountRecommendationStateEnum.PendingReview, CycleCountRecommendationStateEnum.Expired),
        (CycleCountRecommendationStateEnum.PendingReview, CycleCountRecommendationStateEnum.Invalidated),
        (CycleCountRecommendationStateEnum.Approved, CycleCountRecommendationStateEnum.CountSheetCreated),
        (CycleCountRecommendationStateEnum.Approved, CycleCountRecommendationStateEnum.Expired),
        (CycleCountRecommendationStateEnum.Approved, CycleCountRecommendationStateEnum.Invalidated),
        (CycleCountRecommendationStateEnum.Modified, CycleCountRecommendationStateEnum.CountSheetCreated),
        (CycleCountRecommendationStateEnum.Modified, CycleCountRecommendationStateEnum.Expired),
        (CycleCountRecommendationStateEnum.Modified, CycleCountRecommendationStateEnum.Invalidated),
        (CycleCountRecommendationStateEnum.CountSheetCreated, CycleCountRecommendationStateEnum.InProgress),
        (CycleCountRecommendationStateEnum.CountSheetCreated, CycleCountRecommendationStateEnum.PendingVarianceReview),
        (CycleCountRecommendationStateEnum.InProgress, CycleCountRecommendationStateEnum.PendingVarianceReview),
        (CycleCountRecommendationStateEnum.PendingVarianceReview, CycleCountRecommendationStateEnum.InProgress),
        (CycleCountRecommendationStateEnum.PendingVarianceReview, CycleCountRecommendationStateEnum.Reconciled),
        (CycleCountRecommendationStateEnum.Reconciled, CycleCountRecommendationStateEnum.PendingVarianceReview),
        (CycleCountRecommendationStateEnum.Reconciled, CycleCountRecommendationStateEnum.Closed)
    };

    private readonly AppDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICycleCountPlanningService _cycleCountPlanningService;
    private readonly IInventoryRiskScoringService _inventoryRiskScoringService;

    public InventoryRiskRecommendationService(
        AppDbContext db,
        IUnitOfWork unitOfWork,
        ICycleCountPlanningService cycleCountPlanningService,
        IInventoryRiskScoringService? inventoryRiskScoringService = null)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _cycleCountPlanningService = cycleCountPlanningService;
        _inventoryRiskScoringService = inventoryRiskScoringService ?? new InventoryRiskScoringService(db);
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
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText = """
                    SELECT CASE WHEN
                        OBJECT_ID(N'[InventoryRiskModelVersions]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[InventoryRiskFeatureSnapshots]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[InventoryRiskPredictions]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[CycleCountRecommendations]', N'U') IS NOT NULL AND
                        OBJECT_ID(N'[CycleCountRecommendationDecisions]', N'U') IS NOT NULL
                    THEN 1 ELSE 0 END
                    """;
            }
            else if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText = """
                    SELECT CASE WHEN COUNT(*) = 5 THEN 1 ELSE 0 END
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name IN (
                        'InventoryRiskModelVersions',
                        'InventoryRiskFeatureSnapshots',
                        'InventoryRiskPredictions',
                        'CycleCountRecommendations',
                        'CycleCountRecommendationDecisions')
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

    public async Task<InventoryRiskRecommendationPageViewModel> BuildPageAsync(
        InventoryRiskRecommendationQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        if (!await IsPersistenceAvailableAsync(ct))
        {
            return new InventoryRiskRecommendationPageViewModel
            {
                WarehouseId = query.WarehouseId,
                OwnerPartnerId = query.OwnerPartnerId,
                State = query.State,
                Search = query.Search?.Trim() ?? "",
                Page = 1,
                PageSize = pageSize,
                TotalPages = 1,
                PersistenceAvailable = false
            };
        }

        var ownerScope = query.AllowedOwnerPartnerIds.Distinct().ToList();
        var normalizedSearch = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var now = VietnamTime.Now;
        var recommendations = _db.CycleCountRecommendations
            .AsNoTracking()
            .Include(row => row.Warehouse)
            .Include(row => row.OwnerPartner)
            .Include(row => row.Item)
            .Include(row => row.Location)
            .Include(row => row.StockCountSheet)
            .Include(row => row.Prediction).ThenInclude(prediction => prediction.ModelVersion)
            .Include(row => row.Decisions)
            .AsQueryable();

        if (query.WarehouseId.HasValue)
            recommendations = recommendations.Where(row => row.WarehouseId == query.WarehouseId.Value);
        if (query.OwnerPartnerId.HasValue)
            recommendations = recommendations.Where(row => row.OwnerPartnerId == query.OwnerPartnerId.Value);
        if (ownerScope.Count > 0)
            recommendations = recommendations.Where(row => row.OwnerPartnerId.HasValue && ownerScope.Contains(row.OwnerPartnerId.Value));
        if (normalizedSearch != null)
        {
            recommendations = recommendations.Where(row =>
                row.Item.ItemCode.Contains(normalizedSearch)
                || row.Item.ItemName.Contains(normalizedSearch)
                || row.Location.LocationCode.Contains(normalizedSearch)
                || (row.OwnerPartner != null && (row.OwnerPartner.PartnerCode.Contains(normalizedSearch)
                    || row.OwnerPartner.PartnerName.Contains(normalizedSearch))));
        }

        var scopedRecommendations = recommendations;
        if (query.State == CycleCountRecommendationStateEnum.PendingReview)
        {
            recommendations = recommendations.Where(row =>
                row.State == CycleCountRecommendationStateEnum.PendingReview
                && row.FreshUntil > now);
        }
        else if (query.State == CycleCountRecommendationStateEnum.Expired)
        {
            recommendations = recommendations.Where(row =>
                row.State == CycleCountRecommendationStateEnum.Expired
                || (row.State == CycleCountRecommendationStateEnum.PendingReview && row.FreshUntil <= now));
        }
        else if (query.State.HasValue)
        {
            recommendations = recommendations.Where(row => row.State == query.State.Value);
        }

        var totalCount = await recommendations.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);
        var entities = await recommendations
            .OrderBy(row => row.State == CycleCountRecommendationStateEnum.PendingReview && row.FreshUntil > now ? 0 : 1)
            .ThenByDescending(row => row.PriorityScore)
            .ThenByDescending(row => row.GeneratedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var rows = entities.Select(row => new InventoryRiskRecommendationRowViewModel
        {
            RecommendationId = row.CycleCountRecommendationId,
            ConcurrencyToken = row.ConcurrencyToken,
            WarehouseId = row.WarehouseId,
            WarehouseCode = row.Warehouse.WarehouseCode,
            OwnerPartnerId = row.OwnerPartnerId,
            OwnerName = row.OwnerPartner?.PartnerName ?? "Không quản lý chủ hàng",
            ItemCode = row.Item.ItemCode,
            ItemName = row.Item.ItemName,
            LocationCode = row.Location.LocationCode,
            LotNumber = row.LotNumber,
            ExpiryDate = row.ExpiryDate,
            PriorityScore = row.PriorityScore,
            SnapshotSystemQty = row.SnapshotSystemQty,
            EstimatedEffortMinutes = row.EstimatedEffortMinutes,
            AssignedTo = row.AssignedTo,
            WorkPool = row.WorkPool,
            State = row.State == CycleCountRecommendationStateEnum.PendingReview && row.FreshUntil < now
                ? CycleCountRecommendationStateEnum.Expired
                : row.State,
            IsBlindCount = row.IsBlindCount,
            GeneratedAt = row.GeneratedAt,
            FreshUntil = row.FreshUntil,
            ModelVersion = row.Prediction.ModelVersion.Version,
            ReasonSummary = BuildReasonSummary(row.Prediction.ReasonCodesJson),
            ReviewedBy = row.ReviewedBy,
            ReviewedAt = row.ReviewedAt,
            DecisionReasonCode = row.DecisionReasonCode,
            DecisionNote = row.DecisionNote,
            StockCountSheetId = row.StockCountSheetId,
            StockCountSheetCode = row.StockCountSheet?.SheetCode,
            Decisions = row.Decisions
                .OrderByDescending(decision => decision.DecidedAt)
                .Select(decision => new CycleCountRecommendationDecisionViewModel
                {
                    DecisionType = decision.DecisionType,
                    FromState = decision.FromState,
                    ToState = decision.ToState,
                    ReasonCode = decision.ReasonCode,
                    Note = decision.Note,
                    Actor = decision.Actor,
                    DecidedAt = decision.DecidedAt
                })
                .ToList()
        }).ToList();

        return new InventoryRiskRecommendationPageViewModel
        {
            WarehouseId = query.WarehouseId,
            OwnerPartnerId = query.OwnerPartnerId,
            State = query.State,
            Search = query.Search?.Trim() ?? "",
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            PersistenceAvailable = true,
            PendingReviewCount = await scopedRecommendations.CountAsync(row =>
                row.State == CycleCountRecommendationStateEnum.PendingReview && row.FreshUntil > now, ct),
            ApprovedCount = await scopedRecommendations.CountAsync(row =>
                row.State == CycleCountRecommendationStateEnum.Approved
                || row.State == CycleCountRecommendationStateEnum.Modified, ct),
            InProgressCount = await scopedRecommendations.CountAsync(row =>
                row.State == CycleCountRecommendationStateEnum.CountSheetCreated
                || row.State == CycleCountRecommendationStateEnum.InProgress
                || row.State == CycleCountRecommendationStateEnum.PendingVarianceReview, ct),
            BlockedCount = await scopedRecommendations.CountAsync(row =>
                row.State == CycleCountRecommendationStateEnum.Expired
                || row.State == CycleCountRecommendationStateEnum.Invalidated
                || row.State == CycleCountRecommendationStateEnum.BlockedByDataQuality
                || (row.State == CycleCountRecommendationStateEnum.PendingReview && row.FreshUntil <= now), ct),
            Rows = rows
        };
    }

    public async Task<InventoryRiskRecommendationGenerationResult> GenerateFromLatestBatchAsync(
        InventoryRiskQuery query,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureActor(actor);
        if (!await IsPersistenceAvailableAsync(ct))
            throw SchemaNotReady();

        var ownerScope = query.AllowedOwnerPartnerIds.Distinct().ToList();
        var snapshots = _db.InventoryRiskFeatureSnapshots
            .Where(snapshot => snapshot.Prediction != null);
        snapshots = ApplySnapshotScope(snapshots, query, ownerScope);

        var latestBatch = await snapshots
            .Select(snapshot => new { snapshot.BatchId, snapshot.PredictionCutoff })
            .OrderByDescending(row => row.PredictionCutoff)
            .ThenByDescending(row => row.BatchId)
            .FirstOrDefaultAsync(ct);
        if (latestBatch == null)
            throw new BusinessRuleException(
                "Chưa có lần chấm điểm đã lưu trong phạm vi được chọn.",
                "AI_RECOMMENDATION_NO_SCORED_BATCH",
                nameof(InventoryRiskFeatureSnapshot));

        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var batchRows = await ApplySnapshotScope(
                    _db.InventoryRiskFeatureSnapshots
                        .Include(snapshot => snapshot.Prediction)!.ThenInclude(prediction => prediction!.ModelVersion)
                        .Where(snapshot => snapshot.BatchId == latestBatch.BatchId),
                    query,
                    ownerScope)
                .ToListAsync(ct);
            batchRows = batchRows
                .OrderByDescending(snapshot => snapshot.Prediction!.RiskScore)
                .ThenBy(snapshot => snapshot.ScopeKey, StringComparer.Ordinal)
                .ToList();
            var batchModelStatuses = batchRows
                .Select(snapshot => new
                {
                    snapshot.Prediction!.InventoryRiskModelVersionId,
                    snapshot.Prediction.ModelVersion.LifecycleStatus
                })
                .Distinct()
                .ToArray();
            if (batchModelStatuses.Length != 1
                || batchModelStatuses[0].LifecycleStatus != InventoryRiskModelLifecycleStatusEnum.Champion)
            {
                throw new BusinessRuleException(
                    "Lần chấm điểm mới nhất dùng phiên bản đang thử nghiệm hoặc đã ngừng sử dụng. Chưa thể tạo công việc kiểm kê từ lần chấm điểm này.",
                    "AI_RECOMMENDATION_MODEL_NOT_CHAMPION",
                    nameof(InventoryRiskModelVersion));
            }

            var predictionIds = batchRows.Select(row => row.Prediction!.InventoryRiskPredictionId).ToList();
            var existingIds = await _db.CycleCountRecommendations
                .Where(row => predictionIds.Contains(row.InventoryRiskPredictionId))
                .Select(row => row.InventoryRiskPredictionId)
                .ToListAsync(ct);
            var existingSet = existingIds.ToHashSet();
            var now = VietnamTime.Now;
            var created = 0;
            var blocked = 0;

            foreach (var snapshot in batchRows.Where(row => !existingSet.Contains(row.Prediction!.InventoryRiskPredictionId)))
            {
                var prediction = snapshot.Prediction!;
                var featureQty = TryReadSnapshotQuantity(snapshot.FeatureJson, out var snapshotQty);
                var usesCurrentFreshnessContract = snapshot.SourceWatermark.StartsWith(
                    "feature:",
                    StringComparison.Ordinal);
                var isBlocked = snapshot.DataQualityStatus == InventoryRiskDataQualityStatusEnum.Blocked
                    || !prediction.RiskScore.HasValue
                    || !featureQty
                    || !usesCurrentFreshnessContract;
                var isExpired = !isBlocked && prediction.FreshUntil <= now;
                var finalState = isBlocked
                    ? CycleCountRecommendationStateEnum.BlockedByDataQuality
                    : isExpired
                        ? CycleCountRecommendationStateEnum.Expired
                        : CycleCountRecommendationStateEnum.PendingReview;

                var recommendation = new CycleCountRecommendation
                {
                    InventoryRiskPredictionId = prediction.InventoryRiskPredictionId,
                    WarehouseId = snapshot.WarehouseId,
                    OwnerPartnerId = snapshot.OwnerPartnerId,
                    ItemId = snapshot.ItemId,
                    LocationId = snapshot.LocationId,
                    LotNumber = snapshot.LotNumber,
                    ExpiryDate = snapshot.ExpiryDate,
                    ScopeKey = snapshot.ScopeKey,
                    PriorityScore = prediction.RiskScore,
                    SnapshotSystemQty = featureQty ? snapshotQty : 0m,
                    EstimatedEffortMinutes = 5,
                    State = CycleCountRecommendationStateEnum.Generated,
                    IsBlindCount = true,
                    SnapshotWatermark = snapshot.SourceWatermark,
                    PredictionCutoff = snapshot.PredictionCutoff,
                    GeneratedAt = now,
                    FreshUntil = prediction.FreshUntil,
                    ConcurrencyToken = Guid.NewGuid(),
                    CreatedBy = "inventory-risk-engine",
                    CreatedAt = now,
                    UpdatedAt = now
                };

                recommendation.Decisions.Add(CreateDecision(
                    recommendation,
                    CycleCountRecommendationDecisionTypeEnum.Generated,
                    null,
                    CycleCountRecommendationStateEnum.Generated,
                    "BATCH_SCORING",
                    null,
                    actor,
                    prediction.ModelVersion.Version,
                    "{}",
                    SerializeRecommendation(recommendation)));

                var generatedStateJson = SerializeRecommendation(recommendation);
                EnsureTransitionAllowed(recommendation.State, finalState);
                recommendation.State = finalState;
                recommendation.UpdatedAt = now;
                recommendation.ConcurrencyToken = Guid.NewGuid();
                var transitionType = isBlocked
                    ? CycleCountRecommendationDecisionTypeEnum.Invalidated
                    : isExpired
                        ? CycleCountRecommendationDecisionTypeEnum.Expired
                        : CycleCountRecommendationDecisionTypeEnum.SubmittedForReview;
                var transitionReason = isBlocked
                    ? usesCurrentFreshnessContract
                        ? "DATA_QUALITY_BLOCKED"
                        : "FRESHNESS_CONTRACT_RESCORE_REQUIRED"
                    : isExpired
                        ? "FRESHNESS_WINDOW_EXPIRED"
                        : "READY_FOR_HUMAN_REVIEW";
                var transitionNote = isBlocked
                    ? usesCurrentFreshnessContract
                        ? snapshot.DataQualityCodes
                        : "Lần chấm điểm dùng watermark cũ; cần lưu một lần chấm điểm mới trước khi lập phiếu kiểm kê."
                    : null;
                recommendation.Decisions.Add(CreateDecision(
                    recommendation,
                    transitionType,
                    CycleCountRecommendationStateEnum.Generated,
                    finalState,
                    transitionReason,
                    transitionNote,
                    actor,
                    prediction.ModelVersion.Version,
                    generatedStateJson,
                    SerializeRecommendation(recommendation)));

                _db.CycleCountRecommendations.Add(recommendation);
                created++;
                if (isBlocked)
                    blocked++;
            }

            await _unitOfWork.SaveChangesAsync();
            if (startedTransaction)
                await _unitOfWork.CommitAsync(ct);

            return new InventoryRiskRecommendationGenerationResult
            {
                BatchId = latestBatch.BatchId,
                CreatedCount = created,
                ExistingCount = existingSet.Count,
                BlockedByDataQualityCount = blocked
            };
        }
        catch (DbUpdateException ex) when (startedTransaction && IsUniqueConstraintViolation(ex))
        {
            if (_unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            var racedPredictionIds = await ApplySnapshotScope(
                    _db.InventoryRiskFeatureSnapshots
                        .Where(snapshot => snapshot.BatchId == latestBatch.BatchId && snapshot.Prediction != null),
                    query,
                    ownerScope)
                .Select(snapshot => snapshot.Prediction!.InventoryRiskPredictionId)
                .ToListAsync(ct);
            var racedRows = await _db.CycleCountRecommendations
                .AsNoTracking()
                .Where(row => racedPredictionIds.Contains(row.InventoryRiskPredictionId))
                .Select(row => row.State)
                .ToListAsync(ct);
            if (racedRows.Count != racedPredictionIds.Count)
            {
                throw new BusinessRuleException(
                    "Một phiên khác vừa tạo đề xuất cho cùng đợt chấm điểm. Vui lòng gửi lại thao tác để hoàn tất các dòng còn lại.",
                    "AI_RECOMMENDATION_GENERATION_RACE_RETRY",
                    nameof(CycleCountRecommendation));
            }

            return new InventoryRiskRecommendationGenerationResult
            {
                BatchId = latestBatch.BatchId,
                CreatedCount = 0,
                ExistingCount = racedRows.Count,
                BlockedByDataQualityCount = racedRows.Count(state => state == CycleCountRecommendationStateEnum.BlockedByDataQuality)
            };
        }
        catch
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
    }

    public async Task DecideAsync(
        InventoryRiskRecommendationDecisionCommand command,
        string actor,
        int? scopedWarehouseId,
        IReadOnlyList<int> scopedOwnerPartnerIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalizedActor = EnsureActor(actor);
        if (!await IsPersistenceAvailableAsync(ct))
            throw SchemaNotReady();

        var action = command.Action.Trim().ToUpperInvariant();
        var reason = command.ReasonCode.Trim().ToUpperInvariant();
        var allowedReasons = action switch
        {
            "APPROVE" => ApprovalReasons,
            "MODIFY" => ModificationReasons,
            "REJECT" => RejectionReasons,
            _ => throw new BusinessRuleException("Thao tác xem xét đề xuất không hợp lệ.", "AI_RECOMMENDATION_ACTION_INVALID", nameof(CycleCountRecommendation))
        };
        if (!allowedReasons.Contains(reason))
            throw new BusinessRuleException("Vui lòng chọn lý do hợp lệ cho quyết định.", "AI_RECOMMENDATION_REASON_INVALID", nameof(CycleCountRecommendationDecision));

        var note = NormalizeOptional(command.Note, 500);
        if (reason == "OTHER_REVIEWED" && string.IsNullOrWhiteSpace(note))
            throw new BusinessRuleException("Vui lòng ghi chú khi chọn lý do khác.", "AI_RECOMMENDATION_NOTE_REQUIRED", nameof(CycleCountRecommendationDecision));

        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var recommendation = await _db.CycleCountRecommendations
                .Include(row => row.Prediction).ThenInclude(prediction => prediction.ModelVersion)
                .FirstOrDefaultAsync(row => row.CycleCountRecommendationId == command.RecommendationId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy đề xuất kiểm kê.", "AI_RECOMMENDATION_NOT_FOUND", nameof(CycleCountRecommendation));
            EnsureScope(recommendation, scopedWarehouseId, scopedOwnerPartnerIds);
            EnsureConcurrency(recommendation, command.ConcurrencyToken);
            if (recommendation.State != CycleCountRecommendationStateEnum.PendingReview)
                throw new BusinessRuleException("Đề xuất không còn ở trạng thái chờ xem xét.", "AI_RECOMMENDATION_STATE_INVALID", nameof(CycleCountRecommendation));

            var staleState = await DetectStaleStateAsync(recommendation, ct);
            if (staleState.HasValue)
            {
                ApplyTransition(
                    recommendation,
                    staleState.Value,
                    staleState == CycleCountRecommendationStateEnum.Expired
                        ? CycleCountRecommendationDecisionTypeEnum.Expired
                        : CycleCountRecommendationDecisionTypeEnum.Invalidated,
                    staleState == CycleCountRecommendationStateEnum.Expired
                        ? "FRESHNESS_WINDOW_EXPIRED"
                        : "INVENTORY_CHANGED_AFTER_SCORING",
                    "Hãy lưu lần chấm điểm mới trước khi xem xét lại.",
                    normalizedActor);
                await _unitOfWork.SaveChangesAsync();
                if (startedTransaction)
                    await _unitOfWork.CommitAsync(ct);
                throw new BusinessRuleException(
                    "Đề xuất đã hết hiệu lực do quá hạn hoặc tồn kho thay đổi. Vui lòng chấm điểm lại.",
                    "AI_RECOMMENDATION_STALE",
                    nameof(CycleCountRecommendation));
            }

            var targetState = action switch
            {
                "APPROVE" => CycleCountRecommendationStateEnum.Approved,
                "MODIFY" => CycleCountRecommendationStateEnum.Modified,
                _ => CycleCountRecommendationStateEnum.Rejected
            };
            var decisionType = action switch
            {
                "APPROVE" => CycleCountRecommendationDecisionTypeEnum.Approved,
                "MODIFY" => CycleCountRecommendationDecisionTypeEnum.Modified,
                _ => CycleCountRecommendationDecisionTypeEnum.Rejected
            };
            var before = SerializeRecommendation(recommendation);
            EnsureTransitionAllowed(recommendation.State, targetState);

            if (action == "MODIFY")
            {
                recommendation.EstimatedEffortMinutes = Math.Clamp(command.EstimatedEffortMinutes ?? recommendation.EstimatedEffortMinutes, 1, 480);
                recommendation.AssignedTo = NormalizeOptional(command.AssignedTo, 100);
                recommendation.WorkPool = NormalizeOptional(command.WorkPool, 80);
            }

            recommendation.State = targetState;
            recommendation.ReviewedBy = normalizedActor;
            recommendation.ReviewedAt = VietnamTime.Now;
            recommendation.DecisionReasonCode = reason;
            recommendation.DecisionNote = note;
            recommendation.UpdatedAt = VietnamTime.Now;
            recommendation.ConcurrencyToken = Guid.NewGuid();
            recommendation.Decisions.Add(CreateDecision(
                recommendation,
                decisionType,
                CycleCountRecommendationStateEnum.PendingReview,
                targetState,
                reason,
                note,
                normalizedActor,
                recommendation.Prediction.ModelVersion.Version,
                before,
                SerializeRecommendation(recommendation)));

            await _unitOfWork.SaveChangesAsync();
            if (startedTransaction)
                await _unitOfWork.CommitAsync(ct);
        }
        catch
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
    }

    public async Task<InventoryRiskRecommendationMaterializationResult> MaterializeAsync(
        long recommendationId,
        Guid concurrencyToken,
        string actor,
        int? scopedWarehouseId,
        IReadOnlyList<int> scopedOwnerPartnerIds,
        CancellationToken ct = default)
    {
        var normalizedActor = EnsureActor(actor);
        if (!await IsPersistenceAvailableAsync(ct))
            throw SchemaNotReady();

        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var recommendation = await _db.CycleCountRecommendations
                .Include(row => row.Prediction).ThenInclude(prediction => prediction.ModelVersion)
                .Include(row => row.StockCountSheet)
                .FirstOrDefaultAsync(row => row.CycleCountRecommendationId == recommendationId, ct)
                ?? throw new BusinessRuleException("Không tìm thấy đề xuất kiểm kê.", "AI_RECOMMENDATION_NOT_FOUND", nameof(CycleCountRecommendation));
            EnsureScope(recommendation, scopedWarehouseId, scopedOwnerPartnerIds);

            if (recommendation.StockCountSheetId.HasValue && recommendation.StockCountSheet != null)
            {
                if (startedTransaction)
                    await _unitOfWork.CommitAsync(ct);
                return new InventoryRiskRecommendationMaterializationResult
                {
                    RecommendationId = recommendation.CycleCountRecommendationId,
                    StockCountSheetId = recommendation.StockCountSheetId.Value,
                    StockCountSheetCode = recommendation.StockCountSheet.SheetCode ?? $"#{recommendation.StockCountSheetId.Value}",
                    WasAlreadyCreated = true
                };
            }

            EnsureConcurrency(recommendation, concurrencyToken);
            if (recommendation.State is not (CycleCountRecommendationStateEnum.Approved or CycleCountRecommendationStateEnum.Modified))
                throw new BusinessRuleException("Chỉ đề xuất đã duyệt mới được tạo phiếu kiểm kê.", "AI_RECOMMENDATION_NOT_APPROVED", nameof(CycleCountRecommendation));

            var staleState = await DetectStaleStateAsync(recommendation, ct);
            if (staleState.HasValue)
            {
                ApplyTransition(
                    recommendation,
                    staleState.Value,
                    staleState == CycleCountRecommendationStateEnum.Expired
                        ? CycleCountRecommendationDecisionTypeEnum.Expired
                        : CycleCountRecommendationDecisionTypeEnum.Invalidated,
                    staleState == CycleCountRecommendationStateEnum.Expired
                        ? "FRESHNESS_WINDOW_EXPIRED"
                        : "INVENTORY_CHANGED_AFTER_SCORING",
                    "Đề xuất chưa tạo phiếu vì snapshot không còn hợp lệ.",
                    normalizedActor);
                await _unitOfWork.SaveChangesAsync();
                if (startedTransaction)
                    await _unitOfWork.CommitAsync(ct);
                throw new BusinessRuleException(
                    "Đề xuất đã hết hiệu lực do quá hạn hoặc tồn kho thay đổi. Vui lòng chấm điểm lại.",
                    "AI_RECOMMENDATION_STALE",
                    nameof(CycleCountRecommendation));
            }

            var approvedState = recommendation.State;
            var before = SerializeRecommendation(recommendation);
            EnsureTransitionAllowed(approvedState, CycleCountRecommendationStateEnum.CountSheetCreated);
            var sheet = await _cycleCountPlanningService.GenerateRecommendationSheetAsync(
                new CycleCountRecommendationSheetRequest(
                    recommendation.CycleCountRecommendationId,
                    recommendation.WarehouseId,
                    recommendation.OwnerPartnerId,
                    recommendation.ItemId,
                    recommendation.LocationId,
                    recommendation.LotNumber,
                    recommendation.ExpiryDate,
                    recommendation.SnapshotSystemQty,
                    recommendation.PredictionCutoff,
                    recommendation.Prediction.ModelVersion.Version,
                    true,
                    normalizedActor),
                ct);

            recommendation.StockCountSheetId = sheet.StockCountSheetId;
            recommendation.State = CycleCountRecommendationStateEnum.CountSheetCreated;
            recommendation.IsBlindCount = true;
            recommendation.UpdatedAt = VietnamTime.Now;
            recommendation.ConcurrencyToken = Guid.NewGuid();
            recommendation.Decisions.Add(CreateDecision(
                recommendation,
                CycleCountRecommendationDecisionTypeEnum.CountSheetCreated,
                approvedState,
                CycleCountRecommendationStateEnum.CountSheetCreated,
                "HUMAN_APPROVED_MATERIALIZATION",
                null,
                normalizedActor,
                recommendation.Prediction.ModelVersion.Version,
                before,
                SerializeRecommendation(recommendation)));

            await _unitOfWork.SaveChangesAsync();
            if (startedTransaction)
                await _unitOfWork.CommitAsync(ct);

            return new InventoryRiskRecommendationMaterializationResult
            {
                RecommendationId = recommendation.CycleCountRecommendationId,
                StockCountSheetId = sheet.StockCountSheetId,
                StockCountSheetCode = sheet.SheetCode ?? $"#{sheet.StockCountSheetId}",
                WasAlreadyCreated = false
            };
        }
        catch (DbUpdateConcurrencyException) when (startedTransaction)
        {
            var recovered = await RecoverMaterializationAfterRaceAsync(recommendationId, ct);
            if (recovered != null)
                return recovered;
            throw;
        }
        catch (DbUpdateException ex) when (startedTransaction && IsUniqueConstraintViolation(ex))
        {
            var recovered = await RecoverMaterializationAfterRaceAsync(recommendationId, ct);
            if (recovered != null)
                return recovered;
            throw;
        }
        catch
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
    }

    public async Task<bool> SyncSheetStateAsync(
        long stockCountSheetId,
        StockCountStatusEnum stockCountState,
        string actor,
        string reasonCode,
        CancellationToken ct = default)
    {
        if (!await IsPersistenceAvailableAsync(ct))
            return false;

        var targetState = stockCountState switch
        {
            StockCountStatusEnum.Draft => CycleCountRecommendationStateEnum.CountSheetCreated,
            StockCountStatusEnum.Counting => CycleCountRecommendationStateEnum.InProgress,
            StockCountStatusEnum.Counted => CycleCountRecommendationStateEnum.PendingVarianceReview,
            StockCountStatusEnum.Approved => CycleCountRecommendationStateEnum.Reconciled,
            _ => throw new ArgumentOutOfRangeException(nameof(stockCountState), stockCountState, null)
        };
        var recommendation = await _db.CycleCountRecommendations
            .Include(row => row.Prediction).ThenInclude(prediction => prediction.ModelVersion)
            .FirstOrDefaultAsync(row => row.StockCountSheetId == stockCountSheetId, ct);
        if (recommendation == null || recommendation.State == targetState)
            return false;

        ApplyTransition(
            recommendation,
            targetState,
            CycleCountRecommendationDecisionTypeEnum.WorkflowSynchronized,
            NormalizeReasonCode(reasonCode),
            null,
            EnsureActor(actor));
        return true;
    }

    private async Task<CycleCountRecommendationStateEnum?> DetectStaleStateAsync(
        CycleCountRecommendation recommendation,
        CancellationToken ct)
    {
        if (recommendation.FreshUntil <= VietnamTime.Now)
            return CycleCountRecommendationStateEnum.Expired;

        var currentFingerprint = await _inventoryRiskScoringService.BuildFreshnessFingerprintAsync(
            recommendation.WarehouseId,
            recommendation.OwnerPartnerId,
            recommendation.ScopeKey,
            ct);
        if (currentFingerprint == null
            || !string.Equals(
                currentFingerprint.SourceWatermark,
                recommendation.SnapshotWatermark,
                StringComparison.Ordinal))
        {
            return CycleCountRecommendationStateEnum.Invalidated;
        }

        return null;
    }

    private async Task<InventoryRiskRecommendationMaterializationResult?> RecoverMaterializationAfterRaceAsync(
        long recommendationId,
        CancellationToken ct)
    {
        if (_unitOfWork.HasActiveTransaction)
            await _unitOfWork.RollbackAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();

        var linked = await _db.CycleCountRecommendations
            .AsNoTracking()
            .Include(row => row.StockCountSheet)
            .FirstOrDefaultAsync(row => row.CycleCountRecommendationId == recommendationId, ct);
        if (linked?.StockCountSheetId is not long stockCountSheetId || linked.StockCountSheet == null)
            return null;

        return new InventoryRiskRecommendationMaterializationResult
        {
            RecommendationId = linked.CycleCountRecommendationId,
            StockCountSheetId = stockCountSheetId,
            StockCountSheetCode = linked.StockCountSheet.SheetCode ?? $"#{stockCountSheetId}",
            WasAlreadyCreated = true
        };
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Cannot insert duplicate", StringComparison.OrdinalIgnoreCase)
                || message.Contains("2601", StringComparison.Ordinal)
                || message.Contains("2627", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyTransition(
        CycleCountRecommendation recommendation,
        CycleCountRecommendationStateEnum targetState,
        CycleCountRecommendationDecisionTypeEnum decisionType,
        string reasonCode,
        string? note,
        string actor)
    {
        var fromState = recommendation.State;
        EnsureTransitionAllowed(fromState, targetState);
        var before = SerializeRecommendation(recommendation);
        recommendation.State = targetState;
        recommendation.UpdatedAt = VietnamTime.Now;
        recommendation.ConcurrencyToken = Guid.NewGuid();
        recommendation.Decisions.Add(CreateDecision(
            recommendation,
            decisionType,
            fromState,
            targetState,
            reasonCode,
            note,
            actor,
            recommendation.Prediction.ModelVersion.Version,
            before,
            SerializeRecommendation(recommendation)));
    }

    private static IQueryable<InventoryRiskFeatureSnapshot> ApplySnapshotScope(
        IQueryable<InventoryRiskFeatureSnapshot> query,
        InventoryRiskQuery filter,
        IReadOnlyCollection<int> ownerScope)
    {
        if (filter.WarehouseId.HasValue)
            query = query.Where(row => row.WarehouseId == filter.WarehouseId.Value);
        if (filter.OwnerPartnerId.HasValue)
            query = query.Where(row => row.OwnerPartnerId == filter.OwnerPartnerId.Value);
        if (filter.ZoneId.HasValue)
            query = query.Where(row => row.Location.ZoneId == filter.ZoneId.Value);
        if (ownerScope.Count > 0)
            query = query.Where(row => row.OwnerPartnerId.HasValue && ownerScope.Contains(row.OwnerPartnerId.Value));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(row => row.Item.ItemCode.Contains(search)
                || row.Item.ItemName.Contains(search)
                || row.Location.LocationCode.Contains(search));
        }
        return query;
    }

    private static void EnsureScope(
        CycleCountRecommendation recommendation,
        int? scopedWarehouseId,
        IReadOnlyList<int> scopedOwnerPartnerIds)
    {
        if (scopedWarehouseId.HasValue && recommendation.WarehouseId != scopedWarehouseId.Value)
            throw new UnauthorizedAccessException("Recommendation warehouse is outside the authenticated scope.");
        if (scopedOwnerPartnerIds.Count > 0
            && (!recommendation.OwnerPartnerId.HasValue || !scopedOwnerPartnerIds.Contains(recommendation.OwnerPartnerId.Value)))
            throw new UnauthorizedAccessException("Recommendation owner is outside the authenticated scope.");
    }

    private static void EnsureConcurrency(CycleCountRecommendation recommendation, Guid expectedToken)
    {
        if (expectedToken == Guid.Empty || recommendation.ConcurrencyToken != expectedToken)
            throw new DbUpdateConcurrencyException("Cycle-count recommendation changed in another session.");
    }

    private static void EnsureTransitionAllowed(
        CycleCountRecommendationStateEnum fromState,
        CycleCountRecommendationStateEnum toState)
    {
        if (!AllowedTransitions.Contains((fromState, toState)))
        {
            throw new BusinessRuleException(
                $"Không thể chuyển đề xuất kiểm kê từ {fromState} sang {toState}.",
                "AI_RECOMMENDATION_TRANSITION_INVALID",
                nameof(CycleCountRecommendation));
        }
    }

    private static string EnsureActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return "system";
        var normalized = actor.Trim();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static string NormalizeReasonCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "WORKFLOW_STATE_CHANGED";
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length <= 60 ? normalized : normalized[..60];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static bool TryReadSnapshotQuantity(string featureJson, out decimal quantity)
    {
        quantity = 0m;
        try
        {
            using var document = JsonDocument.Parse(featureJson);
            if (!document.RootElement.TryGetProperty("onHandBaseQty", out var value)
                || value.ValueKind != JsonValueKind.Number)
                return false;
            return value.TryGetDecimal(out quantity);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildReasonSummary(string reasonCodesJson)
    {
        try
        {
            var reasons = JsonSerializer.Deserialize<List<InventoryRiskReasonViewModel>>(
                reasonCodesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            return reasons.Count == 0
                ? "Không có yếu tố rủi ro nổi bật"
                : string.Join("; ", reasons.Take(2).Select(reason => reason.Label));
        }
        catch (JsonException)
        {
            return "Không đọc được phần giải thích đã lưu";
        }
    }

    private static CycleCountRecommendationDecision CreateDecision(
        CycleCountRecommendation recommendation,
        CycleCountRecommendationDecisionTypeEnum decisionType,
        CycleCountRecommendationStateEnum? fromState,
        CycleCountRecommendationStateEnum toState,
        string reasonCode,
        string? note,
        string actor,
        string modelVersion,
        string beforeJson,
        string afterJson)
        => new()
        {
            Recommendation = recommendation,
            DecisionType = decisionType,
            FromState = fromState,
            ToState = toState,
            ScopeKey = recommendation.ScopeKey,
            ModelVersion = modelVersion,
            ReasonCode = NormalizeReasonCode(reasonCode),
            Note = NormalizeOptional(note, 500),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Actor = EnsureActor(actor),
            DecidedAt = VietnamTime.Now
        };

    private static string SerializeRecommendation(CycleCountRecommendation recommendation)
        => JsonSerializer.Serialize(new
        {
            recommendation.State,
            recommendation.PriorityScore,
            recommendation.EstimatedEffortMinutes,
            recommendation.AssignedTo,
            recommendation.WorkPool,
            recommendation.IsBlindCount,
            recommendation.StockCountSheetId
        });

    private static BusinessRuleException SchemaNotReady()
        => new(
            "Chưa có schema cho quy trình đề xuất kiểm kê. Chức năng chấm điểm chỉ đọc vẫn hoạt động.",
            "AI_RECOMMENDATION_SCHEMA_NOT_READY",
            nameof(CycleCountRecommendation));
}
