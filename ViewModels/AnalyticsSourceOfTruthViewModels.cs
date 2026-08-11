namespace WMS.ViewModels;

public sealed class SlowMovingItemRow
{
    public int ItemId { get; init; }
    public string ItemCode { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string CategoryName { get; init; } = "";
    public string UomCode { get; init; } = "";
    public decimal CurrentStock { get; init; }
    public decimal StockValue { get; init; }
    public DateTime? LastReceiptDate { get; init; }
    public DateTime? LastOutboundDate { get; init; }
    public int? DaysSinceLastOutbound { get; init; }
}

public sealed class AbcInventoryValueRow
{
    public int Rank { get; init; }
    public string ItemCode { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string CategoryName { get; init; } = "";
    public string UomCode { get; init; } = "";
    public decimal CurrentStock { get; init; }
    public decimal UnitCost { get; init; }
    public decimal TotalStockValue { get; init; }
    public decimal? CumulativePct { get; init; }
    public string AbcClass { get; init; } = "N";
}

public sealed class DaysOfSupplyItemRow
{
    public int ItemId { get; init; }
    public int? OwnerPartnerId { get; init; }
    public string OwnerPartnerName { get; init; } = "Nội bộ / chưa gán";
    public string ItemCode { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string UomCode { get; init; } = "";
    public decimal AvailableBaseQty { get; init; }
    public decimal OutboundBaseQty { get; init; }
    public decimal AverageDailyOutboundBaseQty { get; init; }
    public decimal DaysOfSupply { get; init; }
    public decimal Outbound7DayBaseQty { get; init; }
    public decimal Outbound30DayBaseQty { get; init; }
    public decimal Outbound90DayBaseQty { get; init; }
    public decimal Velocity7DayBaseQty { get; init; }
    public decimal Velocity30DayBaseQty { get; init; }
    public decimal Velocity90DayBaseQty { get; init; }
    public int DemandActiveDayCount90 { get; init; }
    public int? LeadTimeDays { get; init; }
    public int SupplierSampleCount { get; init; }
    public decimal? RiskDaysOfSupply { get; init; }
    public string DataQualityCode { get; init; } = "DEMAND_SAMPLE_INSUFFICIENT";
    public bool IsRiskEligible { get; init; }
    public bool IsReplenishmentRisk { get; init; }
}

public sealed class SupplierInboundScorecardRow
{
    public int PartnerId { get; init; }
    public string PartnerCode { get; init; } = "";
    public string PartnerName { get; init; } = "";
    public int InboundVoucherCount { get; init; }
    public int OnTimeSampleCount { get; init; }
    public int OnTimeCount { get; init; }
    public decimal? OnTimePercent { get; init; }
    public int InFullSampleCount { get; init; }
    public int InFullCount { get; init; }
    public decimal? InFullPercent { get; init; }
    public int QualitySampleCount { get; init; }
    public int QualityPassedCount { get; init; }
    public decimal? QualityPassPercent { get; init; }
    public int DocumentSampleCount { get; init; }
    public int DocumentAccurateCount { get; init; }
    public decimal? DocumentAccuracyPercent { get; init; }
    public decimal ReceivedBaseQty { get; init; }
    public decimal DefectOrShortBaseQty { get; init; }
    public int DockToStockSampleCount { get; init; }
    public decimal? MedianDockToStockHours { get; init; }
    public int AdjustmentTransactionCount { get; init; }
    public decimal AdjustmentAbsoluteBaseQty { get; init; }
    public IReadOnlyList<string> DataQualityCodes { get; init; } = Array.Empty<string>();
}

public sealed class SpaceUtilizationRow
{
    public int LocationId { get; init; }
    public string LocationCode { get; init; } = "";
    public string ZoneCode { get; init; } = "";
    public string ZoneName { get; init; } = "";
    public string WarehouseName { get; init; } = "";
    public bool IsOccupied { get; init; }
    public int ItemCount { get; init; }
    public decimal? CurrentLoad { get; init; }
    public decimal? MaxCapacity { get; init; }
    public decimal? UsedPercent { get; init; }
    public string? CapacityUnit { get; init; }
    public string Status { get; init; } = "capacity-missing";
    public string DataQualityCode { get; init; } = "CAPACITY_DATA_MISSING";
}

public sealed class DockToStockRow
{
    public long VoucherId { get; init; }
    public string VoucherCode { get; init; } = "";
    public string WarehouseName { get; init; } = "";
    public string PartnerName { get; init; } = "";
    public DateTime? DockArrival { get; init; }
    public DateTime? ReceiveStart { get; init; }
    public DateTime? Completed { get; init; }
    public decimal? DockToReceiveHours { get; init; }
    public decimal? ReceiveToStockHours { get; init; }
    public decimal? TotalHours { get; init; }
    public string Sla { get; init; } = "missing";
    public string MissingMilestones { get; init; } = "";
    public bool HasCompleteMilestones => TotalHours.HasValue;
}
