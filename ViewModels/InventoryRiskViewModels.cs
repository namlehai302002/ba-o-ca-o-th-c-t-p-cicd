using WMS.Models;

namespace WMS.ViewModels;

public sealed class InventoryRiskQuery
{
    public int? WarehouseId { get; init; }
    public int? OwnerPartnerId { get; init; }
    public int? ZoneId { get; init; }
    public InventoryRiskSeverityEnum? Severity { get; init; }
    public InventoryRiskDataQualityStatusEnum? DataQualityStatus { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public DateTime? PredictionCutoff { get; init; }
    public IReadOnlyList<int> AllowedOwnerPartnerIds { get; init; } = Array.Empty<int>();
}

public sealed class InventoryRiskPageViewModel
{
    public int? WarehouseId { get; set; }
    public int? OwnerPartnerId { get; set; }
    public int? ZoneId { get; set; }
    public InventoryRiskSeverityEnum? Severity { get; set; }
    public InventoryRiskDataQualityStatusEnum? DataQualityStatus { get; set; }
    public string Search { get; set; } = "";
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public DateTime PredictionCutoff { get; set; }
    public DateTime FreshUntil { get; set; }
    public string RuleVersion { get; set; } = "";
    public string FeatureSchemaVersion { get; set; } = "";
    public bool IsShadowMode { get; set; } = true;
    public bool PersistenceAvailable { get; set; }
    public int ScoredCount { get; set; }
    public int BlockedCount { get; set; }
    public int PartialCount { get; set; }
    public decimal CoveragePercent { get; set; }
    public List<Warehouse> Warehouses { get; set; } = new();
    public List<Partner> Owners { get; set; } = new();
    public List<InventoryRiskZoneOption> Zones { get; set; } = new();
    public List<InventoryRiskRowViewModel> Rows { get; set; } = new();
}

public sealed class InventoryRiskZoneOption
{
    public int ZoneId { get; set; }
    public int WarehouseId { get; set; }
    public string Label { get; set; } = "";
}

public sealed class InventoryRiskRowViewModel
{
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = "";
    public int? OwnerPartnerId { get; set; }
    public string OwnerName { get; set; } = "Không quản lý chủ hàng";
    public int ZoneId { get; set; }
    public string ZoneCode { get; set; } = "";
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = "";
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal OnHandQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal AvailableQty { get; set; }
    public string AbcClass { get; set; } = "Chưa phân hạng";
    public decimal? RiskScore { get; set; }
    public InventoryRiskSeverityEnum? Severity { get; set; }
    public InventoryRiskDataQualityStatusEnum DataQualityStatus { get; set; }
    public List<string> DataQualityCodes { get; set; } = new();
    public DateTime? LastApprovedCountAt { get; set; }
    public int? DaysSinceLastApprovedCount { get; set; }
    public string SourceWatermark { get; set; } = "";
    public string ScopeKey { get; set; } = "";
    public string FeatureJson { get; set; } = "{}";
    public string FeatureHash { get; set; } = "";
    public string OutputHash { get; set; } = "";
    public List<InventoryRiskReasonViewModel> Reasons { get; set; } = new();
}

public sealed class InventoryRiskReasonViewModel
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public string Evidence { get; set; } = "";
    public decimal Contribution { get; set; }
}

public sealed class InventoryRiskFeatureVector
{
    public decimal OnHandBaseQty { get; set; }
    public decimal ReservedBaseQty { get; set; }
    public decimal AvailableBaseQty { get; set; }
    public int MovementCount30D { get; set; }
    public int MovementCount90D { get; set; }
    public decimal AdjustmentAbsQty90D { get; set; }
    public int TransactionActorCount30D { get; set; }
    public int? DaysSinceLastApprovedCount { get; set; }
    public int PriorCountCount180D { get; set; }
    public decimal? PriorVarianceRate180D { get; set; }
    public decimal PriorAbsVarianceQty180D { get; set; }
    public string? AbcClass { get; set; }
    public int? DaysSinceLastReceipt { get; set; }
    public int? DaysSinceLastOutbound { get; set; }
    public int LocationMovementCount30D { get; set; }
    public int LocationDistinctSkuCount { get; set; }
    public int LotCountAtLocation { get; set; }
    public int? DaysToExpiry { get; set; }
    public bool LotTrackingFlag { get; set; }
    public bool ExpiryTrackingFlag { get; set; }
    public bool SerialTrackingFlag { get; set; }
    public decimal? HoldQtyRatio { get; set; }
}

public sealed class InventoryRiskShadowPersistResult
{
    public Guid BatchId { get; init; }
    public int SnapshotCount { get; init; }
    public int PredictionCount { get; init; }
    public string RuleVersion { get; init; } = "";
    public DateTime PredictionCutoff { get; init; }
}

public sealed class InventoryRiskRecommendationQuery
{
    public int? WarehouseId { get; init; }
    public int? OwnerPartnerId { get; init; }
    public CycleCountRecommendationStateEnum? State { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public IReadOnlyList<int> AllowedOwnerPartnerIds { get; init; } = Array.Empty<int>();
}

public sealed class InventoryRiskRecommendationPageViewModel
{
    public int? WarehouseId { get; set; }
    public int? OwnerPartnerId { get; set; }
    public CycleCountRecommendationStateEnum? State { get; set; }
    public string Search { get; set; } = "";
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool PersistenceAvailable { get; set; }
    public int PendingReviewCount { get; set; }
    public int ApprovedCount { get; set; }
    public int InProgressCount { get; set; }
    public int BlockedCount { get; set; }
    public List<Warehouse> Warehouses { get; set; } = new();
    public List<Partner> Owners { get; set; } = new();
    public List<InventoryRiskRecommendationRowViewModel> Rows { get; set; } = new();
}

public sealed class InventoryRiskRecommendationRowViewModel
{
    public long RecommendationId { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = "";
    public int? OwnerPartnerId { get; set; }
    public string OwnerName { get; set; } = "Không quản lý chủ hàng";
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string LocationCode { get; set; } = "";
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal? PriorityScore { get; set; }
    public decimal SnapshotSystemQty { get; set; }
    public int EstimatedEffortMinutes { get; set; }
    public string? AssignedTo { get; set; }
    public string? WorkPool { get; set; }
    public CycleCountRecommendationStateEnum State { get; set; }
    public bool IsBlindCount { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime FreshUntil { get; set; }
    public string ModelVersion { get; set; } = "";
    public string ReasonSummary { get; set; } = "";
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? DecisionReasonCode { get; set; }
    public string? DecisionNote { get; set; }
    public long? StockCountSheetId { get; set; }
    public string? StockCountSheetCode { get; set; }
    public IReadOnlyList<CycleCountRecommendationDecisionViewModel> Decisions { get; set; } = Array.Empty<CycleCountRecommendationDecisionViewModel>();
}

public sealed class CycleCountRecommendationDecisionViewModel
{
    public CycleCountRecommendationDecisionTypeEnum DecisionType { get; set; }
    public CycleCountRecommendationStateEnum? FromState { get; set; }
    public CycleCountRecommendationStateEnum ToState { get; set; }
    public string ReasonCode { get; set; } = "";
    public string? Note { get; set; }
    public string Actor { get; set; } = "";
    public DateTime DecidedAt { get; set; }
}

public sealed class InventoryRiskRecommendationDecisionCommand
{
    public long RecommendationId { get; init; }
    public Guid ConcurrencyToken { get; init; }
    public string Action { get; init; } = "";
    public string ReasonCode { get; init; } = "";
    public string? Note { get; init; }
    public int? EstimatedEffortMinutes { get; init; }
    public string? AssignedTo { get; init; }
    public string? WorkPool { get; init; }
}

public sealed class InventoryRiskRecommendationGenerationResult
{
    public Guid BatchId { get; init; }
    public int CreatedCount { get; init; }
    public int ExistingCount { get; init; }
    public int BlockedByDataQualityCount { get; init; }
}

public sealed class InventoryRiskRecommendationMaterializationResult
{
    public long RecommendationId { get; init; }
    public long StockCountSheetId { get; init; }
    public string StockCountSheetCode { get; init; } = "";
    public bool WasAlreadyCreated { get; init; }
}

public static class InventoryRiskUiLabels
{
    public static string Severity(InventoryRiskSeverityEnum? value) => value switch
    {
        InventoryRiskSeverityEnum.Critical => "Rất cao",
        InventoryRiskSeverityEnum.High => "Cao",
        InventoryRiskSeverityEnum.Medium => "Trung bình",
        InventoryRiskSeverityEnum.Low => "Thấp",
        _ => "Chưa chấm điểm"
    };

    public static string SeverityClass(InventoryRiskSeverityEnum? value) => value switch
    {
        InventoryRiskSeverityEnum.Critical => "badge-danger",
        InventoryRiskSeverityEnum.High => "badge-warning",
        InventoryRiskSeverityEnum.Medium => "badge-info",
        InventoryRiskSeverityEnum.Low => "badge-success",
        _ => "badge-secondary"
    };

    public static string DataQuality(InventoryRiskDataQualityStatusEnum value) => value switch
    {
        InventoryRiskDataQualityStatusEnum.Ok => "Đủ dữ liệu để chấm",
        InventoryRiskDataQualityStatusEnum.Partial => "Cần bổ sung dữ liệu",
        InventoryRiskDataQualityStatusEnum.Blocked => "Chưa thể chấm điểm",
        _ => "Chưa xác định"
    };

    public static string DataQualityDetail(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "BLOCKED_NEGATIVE_BALANCE" => "Tồn hoặc số lượng giữ chỗ đang âm",
        "BLOCKED_OVER_RESERVED" => "Số lượng giữ chỗ vượt số lượng tồn",
        "BLOCKED_TRACKED_LOT_MISSING" => "Thiếu số lô cho vật tư bắt buộc quản lý lô",
        "BLOCKED_TRACKED_EXPIRY_MISSING" => "Thiếu hạn sử dụng cho vật tư bắt buộc quản lý hạn",
        "BLOCKED_BALANCE_AFTER_CUTOFF" => "Tồn kho thay đổi sau thời điểm chốt dữ liệu",
        "BLOCKED_LEDGER_BALANCE_MISMATCH" => "Số dư tồn không khớp giao dịch tồn gần nhất",
        "BLOCKED_MULTIPLE_HOLD_BUCKETS" => "Phạm vi có nhiều trạng thái tồn; chưa thể tạo phiếu kiểm kê an toàn",
        "BLOCKED_SERIAL_COUNT_NOT_SUPPORTED" => "Vật tư quản lý số sê-ri; cần kiểm kê theo từng số sê-ri",
        "PARTIAL_MULTIPLE_HOLD_BUCKETS" => "Phạm vi có nhiều trạng thái tồn nên cần đối chiếu ở mức tổng",
        "PARTIAL_INTERNAL_OWNER_SCOPE" => "Phạm vi hàng nội bộ không gắn chủ hàng",
        "PARTIAL_LEDGER_HISTORY_MISSING" => "Chưa có giao dịch tồn trong kỳ lịch sử dùng để chấm điểm",
        "PARTIAL_COUNT_HISTORY_MISSING" => "Chưa có lần kiểm kê được duyệt để đối chiếu sai lệch",
        "PARTIAL_SERIAL_COVERAGE_NOT_SCORED" => "Chưa đưa mức độ đầy đủ số sê-ri vào điểm ưu tiên",
        _ => "Cảnh báo dữ liệu chưa được phân loại"
    };

    public static string DataQualityClass(InventoryRiskDataQualityStatusEnum value) => value switch
    {
        InventoryRiskDataQualityStatusEnum.Ok => "badge-success",
        InventoryRiskDataQualityStatusEnum.Partial => "badge-warning",
        InventoryRiskDataQualityStatusEnum.Blocked => "badge-danger",
        _ => "badge-secondary"
    };

    public static string RecommendationState(CycleCountRecommendationStateEnum value) => value switch
    {
        CycleCountRecommendationStateEnum.Generated => "Vừa tạo",
        CycleCountRecommendationStateEnum.PendingReview => "Chờ xem xét",
        CycleCountRecommendationStateEnum.Approved => "Đã duyệt",
        CycleCountRecommendationStateEnum.Modified => "Đã duyệt có sửa",
        CycleCountRecommendationStateEnum.Rejected => "Đã từ chối",
        CycleCountRecommendationStateEnum.CountSheetCreated => "Đã tạo phiếu kiểm kê",
        CycleCountRecommendationStateEnum.InProgress => "Đang kiểm đếm",
        CycleCountRecommendationStateEnum.PendingVarianceReview => "Chờ duyệt kết quả",
        CycleCountRecommendationStateEnum.Reconciled => "Đã đối soát",
        CycleCountRecommendationStateEnum.Closed => "Đã đóng",
        CycleCountRecommendationStateEnum.Expired => "Đã hết hạn",
        CycleCountRecommendationStateEnum.Invalidated => "Cần chấm lại",
        CycleCountRecommendationStateEnum.Fallback => "Theo quy tắc dự phòng",
        CycleCountRecommendationStateEnum.BlockedByDataQuality => "Thiếu dữ liệu",
        _ => "Chưa xác định"
    };

    public static string RecommendationStateClass(CycleCountRecommendationStateEnum value) => value switch
    {
        CycleCountRecommendationStateEnum.Approved or CycleCountRecommendationStateEnum.Modified or CycleCountRecommendationStateEnum.Reconciled or CycleCountRecommendationStateEnum.Closed => "badge-success",
        CycleCountRecommendationStateEnum.PendingReview or CycleCountRecommendationStateEnum.PendingVarianceReview => "badge-warning",
        CycleCountRecommendationStateEnum.CountSheetCreated or CycleCountRecommendationStateEnum.InProgress or CycleCountRecommendationStateEnum.Fallback => "badge-info",
        CycleCountRecommendationStateEnum.Rejected or CycleCountRecommendationStateEnum.Expired or CycleCountRecommendationStateEnum.Invalidated or CycleCountRecommendationStateEnum.BlockedByDataQuality => "badge-danger",
        _ => "badge-secondary"
    };

    public static string RecommendationReason(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "RISK_CONFIRMED" => "Rủi ro cần được kiểm tra",
        "ROUTINE_CONTROL" => "Kiểm soát định kỳ",
        "MANAGER_JUDGMENT" => "Đánh giá của quản lý",
        "WORKLOAD_ADJUSTMENT" => "Điều chỉnh khối lượng công việc",
        "SCOPE_PRIORITY_CHANGED" => "Thay đổi mức ưu tiên",
        "ASSIGNMENT_CHANGED" => "Thay đổi phân công",
        "DUPLICATE_SCOPE" => "Phạm vi đã có kế hoạch",
        "RECENTLY_COUNTED" => "Vừa được kiểm kê gần đây",
        "LOW_BUSINESS_PRIORITY" => "Ưu tiên nghiệp vụ thấp",
        "DATA_ISSUE" => "Dữ liệu cần xử lý",
        "OTHER_REVIEWED" => "Lý do khác đã xem xét",
        "BATCH_SCORING" => "Sinh từ lần chấm điểm",
        "READY_FOR_HUMAN_REVIEW" => "Sẵn sàng để quản lý xem xét",
        "DATA_QUALITY_BLOCKED" => "Bị chặn do chất lượng dữ liệu",
        "FRESHNESS_CONTRACT_RESCORE_REQUIRED" => "Cần chấm điểm lại bằng dữ liệu hiện hành",
        "FRESHNESS_WINDOW_EXPIRED" => "Kết quả chấm điểm đã hết hạn",
        "INVENTORY_CHANGED_AFTER_SCORING" => "Tồn kho thay đổi sau khi chấm điểm",
        "HUMAN_APPROVED_MATERIALIZATION" => "Tạo phiếu sau khi quản lý duyệt",
        "STOCK_COUNT_STARTED" => "Đã bắt đầu kiểm đếm",
        "STOCK_COUNT_SUBMITTED" => "Đã gửi kết quả kiểm đếm",
        "STOCK_COUNT_RECOUNT_REQUESTED" => "Yêu cầu kiểm đếm lại",
        "STOCK_COUNT_RECONCILED" => "Đã duyệt và đối soát kết quả",
        "STOCK_COUNT_APPROVAL_UNLOCKED" => "Đã mở khóa kết quả kiểm kê",
        _ => string.IsNullOrWhiteSpace(code) ? "Không có" : code
    };

    public static string RecommendationDecision(CycleCountRecommendationDecisionTypeEnum value) => value switch
    {
        CycleCountRecommendationDecisionTypeEnum.Generated => "Sinh đề xuất",
        CycleCountRecommendationDecisionTypeEnum.SubmittedForReview => "Chuyển quản lý xem xét",
        CycleCountRecommendationDecisionTypeEnum.Approved => "Duyệt",
        CycleCountRecommendationDecisionTypeEnum.Modified => "Duyệt có điều chỉnh",
        CycleCountRecommendationDecisionTypeEnum.Rejected => "Từ chối",
        CycleCountRecommendationDecisionTypeEnum.CountSheetCreated => "Tạo phiếu kiểm kê",
        CycleCountRecommendationDecisionTypeEnum.WorkflowSynchronized => "Cập nhật theo phiếu kiểm kê",
        CycleCountRecommendationDecisionTypeEnum.Expired => "Đánh dấu hết hạn",
        CycleCountRecommendationDecisionTypeEnum.Invalidated => "Yêu cầu chấm lại",
        _ => "Cập nhật"
    };
}
