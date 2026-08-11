using WMS.Models;

namespace WMS.ViewModels;

public sealed class WarehouseOverviewPageViewModel
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public int? WarehouseId { get; set; }
    public bool CanSeeFinancial { get; set; }
    public string? Notice { get; set; }
    public List<Warehouse> Warehouses { get; set; } = new();
    public WarehouseOverviewKpi Kpi { get; set; } = new();
    public List<WarehouseOverviewDailyFlowRow> DailyFlow { get; set; } = new();
    public List<WarehouseOverviewWarehouseRow> WarehouseRows { get; set; } = new();
    public List<WarehouseOverviewTopItemRow> TopItems { get; set; } = new();
    public List<WarehouseOverviewExceptionRow> Exceptions { get; set; } = new();
    public bool HasExceptions => Exceptions.Any(x => x.Count > 0);
}

public sealed class WarehouseOverviewKpi
{
    public decimal OnHandQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal AvailableQty { get; set; }
    public decimal TotalStockValue { get; set; }
    public decimal InboundQty { get; set; }
    public decimal OutboundQty { get; set; }
    public decimal NetMovementQty => InboundQty - OutboundQty;
    public int MovementLineCount { get; set; }
    public int ActiveItemCount { get; set; }
    public int ActiveLocationCount { get; set; }
    public int OpenInboundVouchers { get; set; }
    public int OpenOutboundVouchers { get; set; }
    public int PostedVoucherCount { get; set; }
    public int ExpiringLotCount { get; set; }
    public int ExpiredLotCount { get; set; }
}

public sealed class WarehouseOverviewDailyFlowRow
{
    public DateTime Date { get; set; }
    public decimal InboundQty { get; set; }
    public decimal OutboundQty { get; set; }
    public int TransactionCount { get; set; }
    public decimal NetQty => InboundQty - OutboundQty;
}

public sealed class WarehouseOverviewWarehouseRow
{
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = "";
    public string WarehouseName { get; set; } = "";
    public decimal OnHandQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal AvailableQty { get; set; }
    public int ActiveItemCount { get; set; }
    public int ActiveLocationCount { get; set; }
    public int OpenInboundVouchers { get; set; }
    public int OpenOutboundVouchers { get; set; }
    public decimal InboundQty { get; set; }
    public decimal OutboundQty { get; set; }
    public decimal NetMovementQty => InboundQty - OutboundQty;
}

public sealed class WarehouseOverviewTopItemRow
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string UomCode { get; set; } = "";
    public decimal InboundQty { get; set; }
    public decimal OutboundQty { get; set; }
    public decimal NetQty => InboundQty - OutboundQty;
    public int TransactionCount { get; set; }
}

public sealed class WarehouseOverviewExceptionRow
{
    public string Severity { get; set; } = "info";
    public string Code { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int Count { get; set; }
    public string ActionController { get; set; } = "";
    public string ActionName { get; set; } = "";
}
