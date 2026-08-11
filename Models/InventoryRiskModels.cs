using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WMS.Common;

namespace WMS.Models;

public enum InventoryRiskModelTypeEnum : byte
{
    RuleBaseline = 1,
    MachineLearning = 2
}

public enum InventoryRiskModelLifecycleStatusEnum : byte
{
    Champion = 1,
    Challenger = 2,
    Retired = 3
}

public enum InventoryRiskDataQualityStatusEnum : byte
{
    Ok = 1,
    Partial = 2,
    Blocked = 3
}

public enum InventoryRiskSeverityEnum : byte
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum CycleCountRecommendationStateEnum : byte
{
    Generated = 1,
    PendingReview = 2,
    Approved = 3,
    Modified = 4,
    Rejected = 5,
    CountSheetCreated = 6,
    InProgress = 7,
    PendingVarianceReview = 8,
    Reconciled = 9,
    Closed = 10,
    Expired = 11,
    Invalidated = 12,
    Fallback = 13,
    BlockedByDataQuality = 14
}

public enum CycleCountRecommendationDecisionTypeEnum : byte
{
    Generated = 1,
    SubmittedForReview = 2,
    Approved = 3,
    Modified = 4,
    Rejected = 5,
    CountSheetCreated = 6,
    WorkflowSynchronized = 7,
    Expired = 8,
    Invalidated = 9
}

[Table("InventoryRiskModelVersions")]
public sealed class InventoryRiskModelVersion
{
    [Key]
    public long InventoryRiskModelVersionId { get; set; }

    [Required, MaxLength(80)]
    public string ModelKey { get; set; } = "inventory-discrepancy-risk";

    [Required, MaxLength(40)]
    public string Version { get; set; } = "";

    public InventoryRiskModelTypeEnum ModelType { get; set; } = InventoryRiskModelTypeEnum.RuleBaseline;

    public InventoryRiskModelLifecycleStatusEnum LifecycleStatus { get; set; } = InventoryRiskModelLifecycleStatusEnum.Challenger;

    [Required, MaxLength(40)]
    public string FeatureSchemaVersion { get; set; } = "";

    public DateTime? TrainingCutoff { get; set; }

    [Required]
    public string ConfigurationJson { get; set; } = "{}";

    [Required, MaxLength(64)]
    public string ArtifactHash { get; set; } = "";

    [Required, MaxLength(100)]
    public string CreatedBy { get; set; } = "system";

    public DateTime CreatedAt { get; set; } = VietnamTime.Now;

    public ICollection<InventoryRiskFeatureSnapshot> FeatureSnapshots { get; set; } = new List<InventoryRiskFeatureSnapshot>();
}

[Table("InventoryRiskFeatureSnapshots")]
public sealed class InventoryRiskFeatureSnapshot
{
    [Key]
    public long InventoryRiskFeatureSnapshotId { get; set; }

    public long InventoryRiskModelVersionId { get; set; }

    public Guid BatchId { get; set; }

    public DateTime PredictionCutoff { get; set; }

    public int WarehouseId { get; set; }

    public int? OwnerPartnerId { get; set; }

    public int ItemId { get; set; }

    public int LocationId { get; set; }

    [MaxLength(50)]
    public string? LotNumber { get; set; }

    [Column(TypeName = "date")]
    public DateTime? ExpiryDate { get; set; }

    [Required, MaxLength(360)]
    public string ScopeKey { get; set; } = "";

    [Required]
    public string FeatureJson { get; set; } = "{}";

    [Required, MaxLength(64)]
    public string FeatureHash { get; set; } = "";

    [Required, MaxLength(160)]
    public string SourceWatermark { get; set; } = "";

    public InventoryRiskDataQualityStatusEnum DataQualityStatus { get; set; }

    [Required, MaxLength(1000)]
    public string DataQualityCodes { get; set; } = "";

    public DateTime CreatedAt { get; set; } = VietnamTime.Now;

    [ForeignKey(nameof(InventoryRiskModelVersionId))]
    public InventoryRiskModelVersion ModelVersion { get; set; } = null!;

    [ForeignKey(nameof(WarehouseId))]
    public Warehouse Warehouse { get; set; } = null!;

    [ForeignKey(nameof(OwnerPartnerId))]
    public Partner? OwnerPartner { get; set; }

    [ForeignKey(nameof(ItemId))]
    public Item Item { get; set; } = null!;

    [ForeignKey(nameof(LocationId))]
    public Location Location { get; set; } = null!;

    public InventoryRiskPrediction? Prediction { get; set; }
}

[Table("InventoryRiskPredictions")]
public sealed class InventoryRiskPrediction
{
    [Key]
    public long InventoryRiskPredictionId { get; set; }

    public long InventoryRiskFeatureSnapshotId { get; set; }

    public long InventoryRiskModelVersionId { get; set; }

    [Column(TypeName = "decimal(9,4)")]
    public decimal? RiskScore { get; set; }

    public InventoryRiskSeverityEnum? Severity { get; set; }

    [Required, MaxLength(2000)]
    public string ReasonCodesJson { get; set; } = "[]";

    public DateTime GeneratedAt { get; set; } = VietnamTime.Now;

    public DateTime FreshUntil { get; set; }

    public bool IsShadowMode { get; set; } = true;

    [Required, MaxLength(64)]
    public string OutputHash { get; set; } = "";

    [ForeignKey(nameof(InventoryRiskFeatureSnapshotId))]
    public InventoryRiskFeatureSnapshot FeatureSnapshot { get; set; } = null!;

    [ForeignKey(nameof(InventoryRiskModelVersionId))]
    public InventoryRiskModelVersion ModelVersion { get; set; } = null!;

    public CycleCountRecommendation? Recommendation { get; set; }
}

[Table("CycleCountRecommendations")]
public sealed class CycleCountRecommendation
{
    [Key]
    public long CycleCountRecommendationId { get; set; }

    public long InventoryRiskPredictionId { get; set; }

    public int WarehouseId { get; set; }

    public int? OwnerPartnerId { get; set; }

    public int ItemId { get; set; }

    public int LocationId { get; set; }

    [MaxLength(50)]
    public string? LotNumber { get; set; }

    [Column(TypeName = "date")]
    public DateTime? ExpiryDate { get; set; }

    [Required, MaxLength(360)]
    public string ScopeKey { get; set; } = "";

    [Column(TypeName = "decimal(9,4)")]
    public decimal? PriorityScore { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal SnapshotSystemQty { get; set; }

    public int EstimatedEffortMinutes { get; set; } = 5;

    public CycleCountRecommendationStateEnum State { get; set; } = CycleCountRecommendationStateEnum.Generated;

    public bool IsBlindCount { get; set; } = true;

    [MaxLength(100)]
    public string? AssignedTo { get; set; }

    [MaxLength(80)]
    public string? WorkPool { get; set; }

    [Required, MaxLength(160)]
    public string SnapshotWatermark { get; set; } = "";

    public DateTime PredictionCutoff { get; set; }

    public DateTime GeneratedAt { get; set; } = VietnamTime.Now;

    public DateTime FreshUntil { get; set; }

    [MaxLength(100)]
    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(60)]
    public string? DecisionReasonCode { get; set; }

    [MaxLength(500)]
    public string? DecisionNote { get; set; }

    public long? StockCountSheetId { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string CreatedBy { get; set; } = "inventory-risk-engine";

    public DateTime CreatedAt { get; set; } = VietnamTime.Now;

    public DateTime UpdatedAt { get; set; } = VietnamTime.Now;

    [ForeignKey(nameof(InventoryRiskPredictionId))]
    public InventoryRiskPrediction Prediction { get; set; } = null!;

    [ForeignKey(nameof(WarehouseId))]
    public Warehouse Warehouse { get; set; } = null!;

    [ForeignKey(nameof(OwnerPartnerId))]
    public Partner? OwnerPartner { get; set; }

    [ForeignKey(nameof(ItemId))]
    public Item Item { get; set; } = null!;

    [ForeignKey(nameof(LocationId))]
    public Location Location { get; set; } = null!;

    [ForeignKey(nameof(StockCountSheetId))]
    public StockCountSheet? StockCountSheet { get; set; }

    public ICollection<CycleCountRecommendationDecision> Decisions { get; set; } = new List<CycleCountRecommendationDecision>();
}

[Table("CycleCountRecommendationDecisions")]
public sealed class CycleCountRecommendationDecision
{
    [Key]
    public long CycleCountRecommendationDecisionId { get; set; }

    public long CycleCountRecommendationId { get; set; }

    public CycleCountRecommendationDecisionTypeEnum DecisionType { get; set; }

    public CycleCountRecommendationStateEnum? FromState { get; set; }

    public CycleCountRecommendationStateEnum ToState { get; set; }

    [Required, MaxLength(360)]
    public string ScopeKey { get; set; } = "";

    [Required, MaxLength(40)]
    public string ModelVersion { get; set; } = "";

    [Required, MaxLength(60)]
    public string ReasonCode { get; set; } = "";

    [MaxLength(500)]
    public string? Note { get; set; }

    [Required]
    public string BeforeJson { get; set; } = "{}";

    [Required]
    public string AfterJson { get; set; } = "{}";

    [Required, MaxLength(100)]
    public string Actor { get; set; } = "system";

    public DateTime DecidedAt { get; set; } = VietnamTime.Now;

    [ForeignKey(nameof(CycleCountRecommendationId))]
    public CycleCountRecommendation Recommendation { get; set; } = null!;
}
