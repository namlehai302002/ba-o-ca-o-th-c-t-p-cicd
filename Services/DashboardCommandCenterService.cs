using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WMS.Data;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Services;

public interface IDashboardCommandCenterService
{
    Task<DashboardCommandCenterViewModel> BuildAsync(
        DashboardCommandCenterRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DashboardCommandCenterService : IDashboardCommandCenterService
{
    private static readonly VoucherTypeEnum[] InboundTypes =
    {
        VoucherTypeEnum.NhapKho,
        VoucherTypeEnum.NhapThanhPham
    };

    private static readonly VoucherTypeEnum[] OutboundTypes =
    {
        VoucherTypeEnum.XuatKho,
        VoucherTypeEnum.XuatSanXuat
    };

    private static readonly VoucherTypeEnum[] ReturnTypes =
    {
        VoucherTypeEnum.KhachTra,
        VoucherTypeEnum.TraNCC
    };

    private readonly AppDbContext _db;
    private readonly ILogger<DashboardCommandCenterService> _logger;

    public DashboardCommandCenterService(
        AppDbContext db,
        ILogger<DashboardCommandCenterService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<DashboardCommandCenterService>.Instance;
    }

    public async Task<DashboardCommandCenterViewModel> BuildAsync(
        DashboardCommandCenterRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = request.Now;
        var businessDate = now.Date;
        var start = businessDate;
        var end = start.AddDays(1);
        var ownerIds = request.OwnerPartnerIds.Where(id => id > 0).Distinct().ToArray();
        var role = request.Role?.Trim() ?? "";
        var model = new DashboardCommandCenterViewModel
        {
            AsOfAt = now,
            BusinessDate = businessDate,
            SelectedWarehouseId = request.WarehouseId,
            SelectedWorkState = NormalizeFilter(request.WorkState),
            SelectedSeverity = NormalizeFilter(request.Severity),
            SelectedAssignee = string.IsNullOrWhiteSpace(request.Assignee) ? null : request.Assignee.Trim()
        };

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["DashboardRole"] = role,
            ["DashboardWarehouseId"] = request.WarehouseId,
            ["DashboardOwnerScopeCount"] = ownerIds.Length,
            ["DashboardAsOf"] = now
        });

        await LoadWidgetAsync(model, "phạm vi dashboard", async () =>
        {
            await PopulateScopeAsync(model, request.WarehouseId, ownerIds, cancellationToken);
        }, cancellationToken);

        var canManageAll = WmsRoles.IsAdminOrManager(role) || WmsRoles.IsLegacyStaff(role);
        var canInbound = canManageAll || WmsRoles.IsInbound(role);
        var canOutbound = canManageAll || WmsRoles.IsOutbound(role);
        var canInventory = canManageAll || WmsRoles.IsInventory(role);
        var canTransport = canManageAll || WmsRoles.IsTransport(role);
        var canReportAll = WmsRoles.IsReportingSpecialist(role);
        var viewerOnly = WmsRoles.IsViewerOnly(role);

        var voucherQuery = ApplyVoucherScope(_db.Vouchers.AsNoTracking(), request.WarehouseId, ownerIds);
        var pickTaskQuery = ApplyPickTaskScope(_db.PickTasks.AsNoTracking(), request.WarehouseId, ownerIds);
        var movementTaskQuery = ApplyMovementScope(_db.MovementTasks.AsNoTracking(), request.WarehouseId, ownerIds);
        var stockCountQuery = ApplyStockCountScope(_db.StockCountSheets.AsNoTracking(), request.WarehouseId, ownerIds);
        var qualityQuery = ApplyQualityScope(_db.QualityInspections.AsNoTracking(), request.WarehouseId, ownerIds);

        var workItems = new List<DashboardWorkItemViewModel>();

        if (canInbound || canReportAll)
        {
            await LoadWidgetAsync(model, "nhập kho", async () =>
            {
                model.ProcessSummaries.Add(await BuildInboundSummaryAsync(voucherQuery, start, end, now, cancellationToken));
                workItems.AddRange(await LoadInboundWorkAsync(voucherQuery, now, start, end, canInbound, cancellationToken));
            }, cancellationToken);
        }

        if (canOutbound || canTransport || canReportAll)
        {
            await LoadWidgetAsync(model, "xuất kho", async () =>
            {
                model.ProcessSummaries.Add(await BuildOutboundSummaryAsync(voucherQuery, start, end, now, cancellationToken));
                workItems.AddRange(await LoadOutboundWorkAsync(voucherQuery, now, start, end, canOutbound || canTransport, cancellationToken));
            }, cancellationToken);

            await LoadWidgetAsync(model, "lấy hàng", async () =>
            {
                workItems.AddRange(await LoadPickWorkAsync(pickTaskQuery, now, canOutbound, cancellationToken));
            }, cancellationToken);
        }

        if (canInventory || canReportAll)
        {
            await LoadWidgetAsync(model, "di chuyển và bổ sung", async () =>
            {
                model.ProcessSummaries.Add(await BuildMovementSummaryAsync(movementTaskQuery, start, end, now, cancellationToken));
                workItems.AddRange(await LoadMovementWorkAsync(movementTaskQuery, now, canInventory, cancellationToken));
            }, cancellationToken);

            await LoadWidgetAsync(model, "kiểm kê và chất lượng", async () =>
            {
                model.ProcessSummaries.Add(await BuildCountQualitySummaryAsync(
                    stockCountQuery,
                    qualityQuery,
                    start,
                    end,
                    now,
                    cancellationToken));
                workItems.AddRange(await LoadCountWorkAsync(stockCountQuery, now, canInventory, cancellationToken));
                workItems.AddRange(await LoadQualityWorkAsync(qualityQuery, now, canInventory, cancellationToken));
            }, cancellationToken);
        }

        if (canManageAll || canInventory || canReportAll)
        {
            await LoadWidgetAsync(model, "chuyển kho", async () =>
            {
                model.ProcessSummaries.Add(await BuildTransferSummaryAsync(voucherQuery, start, end, now, cancellationToken));
                workItems.AddRange(await LoadTransferWorkAsync(voucherQuery, now, canInventory || canManageAll, cancellationToken));
            }, cancellationToken);
        }

        if (canManageAll || canInbound || canOutbound || canReportAll)
        {
            await LoadWidgetAsync(model, "hàng trả", async () =>
            {
                model.ProcessSummaries.Add(await BuildReturnSummaryAsync(voucherQuery, start, end, now, cancellationToken));
                workItems.AddRange(await LoadReturnWorkAsync(voucherQuery, now, canInbound || canOutbound || canManageAll, cancellationToken));
            }, cancellationToken);
        }

        // OperationExceptionCase does not carry OwnerPartnerId. Excluding it for owner-scoped
        // users prevents cross-owner disclosure until every generated exception has owner lineage.
        if (ownerIds.Length == 0 && (canManageAll || canInventory || canReportAll))
        {
            await LoadWidgetAsync(model, "ngoại lệ vận hành", async () =>
            {
                workItems.AddRange(await LoadExceptionWorkAsync(request.WarehouseId, now, canManageAll, cancellationToken));
            }, cancellationToken);
        }

        if (viewerOnly)
        {
            workItems = workItems
                .Where(item => item.KindKey is "inventory" or "cycle-count" or "quality")
                .ToList();
        }

        PopulateCounters(model, workItems);
        ApplyFiltersAndSort(model, workItems, request.WorkItemLimit);
        model.CompletedTodayCount = model.ProcessSummaries.Sum(summary => summary.CompletedToday);

        return model;
    }

    private async Task PopulateScopeAsync(
        DashboardCommandCenterViewModel model,
        int? warehouseId,
        IReadOnlyCollection<int> ownerIds,
        CancellationToken cancellationToken)
    {
        var warehouseQuery = _db.Warehouses.AsNoTracking().Where(warehouse => warehouse.IsActive);
        if (ownerIds.Count > 0)
        {
            warehouseQuery = warehouseQuery.Where(warehouse =>
                _db.Vouchers.Any(voucher =>
                    voucher.WarehouseId == warehouse.WarehouseId
                    && voucher.OwnerPartnerId.HasValue
                    && ownerIds.Contains(voucher.OwnerPartnerId.Value))
                || _db.ItemLocations.Any(stock =>
                    stock.OwnerPartnerId.HasValue
                    && ownerIds.Contains(stock.OwnerPartnerId.Value)
                    && stock.Location != null
                    && stock.Location.Zone != null
                    && stock.Location.Zone.WarehouseId == warehouse.WarehouseId));
        }

        model.WarehouseOptions = await warehouseQuery
            .OrderBy(warehouse => warehouse.WarehouseCode)
            .Select(warehouse => new DashboardScopeOption
            {
                Id = warehouse.WarehouseId,
                Label = warehouse.WarehouseCode + " - " + warehouse.WarehouseName
            })
            .ToListAsync(cancellationToken);

        if (warehouseId.HasValue)
        {
            model.WarehouseScopeLabel = model.WarehouseOptions
                .FirstOrDefault(option => option.Id == warehouseId.Value)?.Label
                ?? $"Kho #{warehouseId.Value}";
        }

        if (ownerIds.Count > 0)
        {
            var ownerNames = await _db.Partners.AsNoTracking()
                .Where(partner => ownerIds.Contains(partner.PartnerId))
                .OrderBy(partner => partner.PartnerName)
                .Select(partner => partner.PartnerName)
                .ToListAsync(cancellationToken);

            model.OwnerScopeLabel = ownerNames.Count switch
            {
                0 => $"{ownerIds.Count} chủ hàng được phân quyền",
                1 => ownerNames[0],
                2 => string.Join(", ", ownerNames),
                _ => string.Join(", ", ownerNames.Take(2)) + $" và {ownerNames.Count - 2} chủ hàng khác"
            };
        }
    }

    private static IQueryable<Voucher> ApplyVoucherScope(
        IQueryable<Voucher> query,
        int? warehouseId,
        IReadOnlyCollection<int> ownerIds)
    {
        if (warehouseId.HasValue)
            query = query.Where(voucher => voucher.WarehouseId == warehouseId.Value);
        if (ownerIds.Count > 0)
            query = query.Where(voucher => voucher.OwnerPartnerId.HasValue && ownerIds.Contains(voucher.OwnerPartnerId.Value));
        return query;
    }

    private static IQueryable<PickTask> ApplyPickTaskScope(
        IQueryable<PickTask> query,
        int? warehouseId,
        IReadOnlyCollection<int> ownerIds)
    {
        if (warehouseId.HasValue)
        {
            query = query.Where(task => task.Voucher != null && task.Voucher.WarehouseId == warehouseId.Value);
        }

        if (ownerIds.Count > 0)
        {
            query = query.Where(task =>
                (task.OwnerPartnerId.HasValue && ownerIds.Contains(task.OwnerPartnerId.Value))
                || (!task.OwnerPartnerId.HasValue
                    && task.Voucher != null
                    && task.Voucher.OwnerPartnerId.HasValue
                    && ownerIds.Contains(task.Voucher.OwnerPartnerId.Value)));
        }

        return query;
    }

    private static IQueryable<MovementTask> ApplyMovementScope(
        IQueryable<MovementTask> query,
        int? warehouseId,
        IReadOnlyCollection<int> ownerIds)
    {
        if (warehouseId.HasValue)
            query = query.Where(task => task.WarehouseId == warehouseId.Value);
        if (ownerIds.Count > 0)
            query = query.Where(task => task.OwnerPartnerId.HasValue && ownerIds.Contains(task.OwnerPartnerId.Value));
        return query;
    }

    private static IQueryable<StockCountSheet> ApplyStockCountScope(
        IQueryable<StockCountSheet> query,
        int? warehouseId,
        IReadOnlyCollection<int> ownerIds)
    {
        if (warehouseId.HasValue)
            query = query.Where(sheet => sheet.WarehouseId == warehouseId.Value);
        if (ownerIds.Count > 0)
            query = query.Where(sheet => sheet.Lines.Any(line => line.OwnerPartnerId.HasValue && ownerIds.Contains(line.OwnerPartnerId.Value)));
        return query;
    }

    private static IQueryable<QualityInspection> ApplyQualityScope(
        IQueryable<QualityInspection> query,
        int? warehouseId,
        IReadOnlyCollection<int> ownerIds)
    {
        if (warehouseId.HasValue)
            query = query.Where(inspection => inspection.WarehouseId == warehouseId.Value);
        if (ownerIds.Count > 0)
        {
            query = query.Where(inspection =>
                inspection.Voucher != null
                && inspection.Voucher.OwnerPartnerId.HasValue
                && ownerIds.Contains(inspection.Voucher.OwnerPartnerId.Value));
        }
        return query;
    }

    private static async Task<DashboardProcessSummary> BuildInboundSummaryAsync(
        IQueryable<Voucher> vouchers,
        DateTime start,
        DateTime end,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var stats = await vouchers
            .Where(voucher => !voucher.IsCancelled && InboundTypes.Contains(voucher.VoucherType))
            .GroupBy(_ => 1)
            .Select(group => new ProcessStats
            {
                DueToday = group.Count(voucher => voucher.ExpectedArrivalAt >= start && voucher.ExpectedArrivalAt < end),
                CompletedToday = group.Count(voucher => voucher.CompletedAt >= start && voucher.CompletedAt < end),
                Open = group.Count(voucher => !voucher.IsPosted
                    && voucher.InboundStatus != InboundStatusEnum.Completed
                    && voucher.InboundStatus != InboundStatusEnum.Rejected),
                Overdue = group.Count(voucher => !voucher.IsPosted
                    && voucher.ExpectedArrivalAt.HasValue
                    && voucher.ExpectedArrivalAt.Value < now
                    && voucher.InboundStatus != InboundStatusEnum.Completed
                    && voucher.InboundStatus != InboundStatusEnum.Rejected)
            })
            .SingleOrDefaultAsync(cancellationToken) ?? new ProcessStats();

        return ToSummary("inbound", "Nhập kho", "fa-truck-ramp-box", stats, "/Operations/Receiving");
    }

    private static async Task<DashboardProcessSummary> BuildOutboundSummaryAsync(
        IQueryable<Voucher> vouchers,
        DateTime start,
        DateTime end,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var stats = await vouchers
            .Where(voucher => !voucher.IsCancelled && OutboundTypes.Contains(voucher.VoucherType))
            .GroupBy(_ => 1)
            .Select(group => new ProcessStats
            {
                DueToday = group.Count(voucher => voucher.RequestedDeliveryDate >= start && voucher.RequestedDeliveryDate < end),
                CompletedToday = group.Count(voucher =>
                    (voucher.ShippedAt >= start && voucher.ShippedAt < end)
                    || (!voucher.ShippedAt.HasValue && voucher.CompletedAt >= start && voucher.CompletedAt < end)),
                Open = group.Count(voucher => !voucher.IsPosted),
                Overdue = group.Count(voucher => !voucher.IsPosted
                    && voucher.RequestedDeliveryDate.HasValue
                    && voucher.RequestedDeliveryDate.Value < start)
            })
            .SingleOrDefaultAsync(cancellationToken) ?? new ProcessStats();

        return ToSummary("outbound", "Xuất kho", "fa-box-open", stats, "/Operations/PickTasks");
    }

    private static async Task<DashboardProcessSummary> BuildMovementSummaryAsync(
        IQueryable<MovementTask> tasks,
        DateTime start,
        DateTime end,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var stats = await tasks
            .GroupBy(_ => 1)
            .Select(group => new ProcessStats
            {
                DueToday = group.Count(task => task.DueAt >= start && task.DueAt < end),
                CompletedToday = group.Count(task => task.CompletedAt >= start && task.CompletedAt < end),
                Open = group.Count(task => task.Status != MovementTaskStatusEnum.Completed && task.Status != MovementTaskStatusEnum.Cancelled),
                Overdue = group.Count(task => task.DueAt.HasValue
                    && task.DueAt.Value < now
                    && task.Status != MovementTaskStatusEnum.Completed
                    && task.Status != MovementTaskStatusEnum.Cancelled)
            })
            .SingleOrDefaultAsync(cancellationToken) ?? new ProcessStats();

        return ToSummary("movement", "Di chuyển & bổ sung", "fa-dolly", stats, "/Operations/MovementTasks");
    }

    private static async Task<DashboardProcessSummary> BuildCountQualitySummaryAsync(
        IQueryable<StockCountSheet> sheets,
        IQueryable<QualityInspection> inspections,
        DateTime start,
        DateTime end,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var countStats = await sheets
            .GroupBy(_ => 1)
            .Select(group => new ProcessStats
            {
                DueToday = group.Count(sheet => sheet.CountDate >= start && sheet.CountDate < end),
                CompletedToday = group.Count(sheet => sheet.ApprovedAt >= start && sheet.ApprovedAt < end),
                Open = group.Count(sheet => sheet.Status != StockCountStatusEnum.Approved),
                Overdue = group.Count(sheet => sheet.CountDate < start && sheet.Status != StockCountStatusEnum.Approved)
            })
            .SingleOrDefaultAsync(cancellationToken) ?? new ProcessStats();

        var qualityStats = await inspections
            .GroupBy(_ => 1)
            .Select(group => new ProcessStats
            {
                DueToday = group.Count(inspection => inspection.CreatedAt >= start && inspection.CreatedAt < end),
                CompletedToday = group.Count(inspection => inspection.InspectedAt >= start && inspection.InspectedAt < end),
                Open = group.Count(inspection =>
                    inspection.OverallResult == QualityStatusEnum.Pending
                    || inspection.OverallResult == QualityStatusEnum.Inspecting
                    || inspection.OverallResult == QualityStatusEnum.Quarantine
                    || inspection.OverallResult == QualityStatusEnum.OnHold),
                Overdue = group.Count(inspection =>
                    !inspection.InspectedAt.HasValue
                    && inspection.CreatedAt < now.AddHours(-4)
                    && (inspection.OverallResult == QualityStatusEnum.Pending
                        || inspection.OverallResult == QualityStatusEnum.Inspecting
                        || inspection.OverallResult == QualityStatusEnum.Quarantine
                        || inspection.OverallResult == QualityStatusEnum.OnHold))
            })
            .SingleOrDefaultAsync(cancellationToken) ?? new ProcessStats();

        var combined = new ProcessStats
        {
            DueToday = countStats.DueToday + qualityStats.DueToday,
            CompletedToday = countStats.CompletedToday + qualityStats.CompletedToday,
            Open = countStats.Open + qualityStats.Open,
            Overdue = countStats.Overdue + qualityStats.Overdue
        };
        return ToSummary("count-quality", "Kiểm kê & chất lượng", "fa-clipboard-check", combined, "/Reports/StockCount");
    }

    private static async Task<DashboardProcessSummary> BuildTransferSummaryAsync(
        IQueryable<Voucher> vouchers,
        DateTime start,
        DateTime end,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var stats = await vouchers
            .Where(voucher => !voucher.IsCancelled && voucher.VoucherType == VoucherTypeEnum.ChuyenKho)
            .GroupBy(_ => 1)
            .Select(group => new ProcessStats
            {
                DueToday = group.Count(voucher => voucher.RequestedDeliveryDate >= start && voucher.RequestedDeliveryDate < end),
                CompletedToday = group.Count(voucher => voucher.CompletedAt >= start && voucher.CompletedAt < end),
                Open = group.Count(voucher => !voucher.IsPosted),
                Overdue = group.Count(voucher => !voucher.IsPosted
                    && voucher.RequestedDeliveryDate.HasValue
                    && voucher.RequestedDeliveryDate.Value < start)
            })
            .SingleOrDefaultAsync(cancellationToken) ?? new ProcessStats();
        return ToSummary("transfer", "Chuyển kho", "fa-right-left", stats, "/Vouchers?type=6");
    }

    private static async Task<DashboardProcessSummary> BuildReturnSummaryAsync(
        IQueryable<Voucher> vouchers,
        DateTime start,
        DateTime end,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var stats = await vouchers
            .Where(voucher => !voucher.IsCancelled && ReturnTypes.Contains(voucher.VoucherType))
            .GroupBy(_ => 1)
            .Select(group => new ProcessStats
            {
                DueToday = group.Count(voucher =>
                    (voucher.VoucherType == VoucherTypeEnum.KhachTra && voucher.ExpectedArrivalAt >= start && voucher.ExpectedArrivalAt < end)
                    || (voucher.VoucherType == VoucherTypeEnum.TraNCC && voucher.RequestedDeliveryDate >= start && voucher.RequestedDeliveryDate < end)),
                CompletedToday = group.Count(voucher => voucher.CompletedAt >= start && voucher.CompletedAt < end),
                Open = group.Count(voucher => !voucher.IsPosted),
                Overdue = group.Count(voucher => !voucher.IsPosted
                    && ((voucher.VoucherType == VoucherTypeEnum.KhachTra && voucher.ExpectedArrivalAt.HasValue && voucher.ExpectedArrivalAt.Value < now)
                        || (voucher.VoucherType == VoucherTypeEnum.TraNCC && voucher.RequestedDeliveryDate.HasValue && voucher.RequestedDeliveryDate.Value < start)))
            })
            .SingleOrDefaultAsync(cancellationToken) ?? new ProcessStats();
        return ToSummary("return", "Hàng trả", "fa-rotate-left", stats, "/Vouchers");
    }

    private static DashboardProcessSummary ToSummary(
        string key,
        string label,
        string icon,
        ProcessStats stats,
        string drillDownUrl)
        => new()
        {
            Key = key,
            Label = label,
            Icon = icon,
            DueToday = stats.DueToday,
            CompletedToday = stats.CompletedToday,
            Open = stats.Open,
            Overdue = stats.Overdue,
            DrillDownUrl = drillDownUrl
        };

    private static async Task<List<DashboardWorkItemViewModel>> LoadInboundWorkAsync(
        IQueryable<Voucher> vouchers,
        DateTime now,
        DateTime start,
        DateTime end,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var rows = await vouchers
            .Where(voucher => !voucher.IsCancelled
                && !voucher.IsPosted
                && InboundTypes.Contains(voucher.VoucherType)
                && voucher.InboundStatus != InboundStatusEnum.Completed
                && voucher.InboundStatus != InboundStatusEnum.Rejected
                && (voucher.InboundStatus == InboundStatusEnum.PendingApproval
                    || voucher.InboundStatus == InboundStatusEnum.Receiving
                    || (voucher.ExpectedArrivalAt.HasValue && voucher.ExpectedArrivalAt.Value < end)))
            .OrderBy(voucher => voucher.ExpectedArrivalAt ?? voucher.SubmittedAt ?? voucher.CreatedAt)
            .Take(40)
            .Select(voucher => new VoucherWorkProjection
            {
                VoucherId = voucher.VoucherId,
                VoucherCode = voucher.VoucherCode,
                VoucherType = voucher.VoucherType,
                WarehouseLabel = voucher.Warehouse != null ? voucher.Warehouse.WarehouseCode : "",
                InboundStatus = voucher.InboundStatus,
                FulfillmentStatus = voucher.FulfillmentStatus,
                Priority = voucher.Priority,
                CreatedAt = voucher.CreatedAt,
                SubmittedAt = voucher.SubmittedAt,
                ExpectedArrivalAt = voucher.ExpectedArrivalAt,
                DockAppointmentEnd = voucher.DockAppointmentEnd,
                RequestedDeliveryDate = voucher.RequestedDeliveryDate,
                TotalLines = voucher.TotalLines,
                AssignedTo = voucher.ReceivedBy
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => MapInbound(row, now, canAct)).ToList();
    }

    private static async Task<List<DashboardWorkItemViewModel>> LoadOutboundWorkAsync(
        IQueryable<Voucher> vouchers,
        DateTime now,
        DateTime start,
        DateTime end,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var rows = await vouchers
            .Where(voucher => !voucher.IsCancelled
                && !voucher.IsPosted
                && OutboundTypes.Contains(voucher.VoucherType)
                && (voucher.FulfillmentStatus != FulfillmentStatusEnum.Draft
                    || (voucher.RequestedDeliveryDate.HasValue && voucher.RequestedDeliveryDate.Value < end)))
            .OrderBy(voucher => voucher.RequestedDeliveryDate ?? voucher.CreatedAt)
            .Take(40)
            .Select(voucher => new VoucherWorkProjection
            {
                VoucherId = voucher.VoucherId,
                VoucherCode = voucher.VoucherCode,
                VoucherType = voucher.VoucherType,
                WarehouseLabel = voucher.Warehouse != null ? voucher.Warehouse.WarehouseCode : "",
                FulfillmentStatus = voucher.FulfillmentStatus,
                Priority = voucher.Priority,
                CreatedAt = voucher.CreatedAt,
                RequestedDeliveryDate = voucher.RequestedDeliveryDate,
                TotalLines = voucher.TotalLines,
                AssignedTo = voucher.PackedBy ?? voucher.ShippedBy
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => MapOutbound(row, now, canAct, "outbound", "Xuất kho")).ToList();
    }

    private static async Task<List<DashboardWorkItemViewModel>> LoadTransferWorkAsync(
        IQueryable<Voucher> vouchers,
        DateTime now,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var rows = await vouchers
            .Where(voucher => !voucher.IsCancelled && !voucher.IsPosted && voucher.VoucherType == VoucherTypeEnum.ChuyenKho)
            .OrderBy(voucher => voucher.RequestedDeliveryDate ?? voucher.CreatedAt)
            .Take(25)
            .Select(voucher => new VoucherWorkProjection
            {
                VoucherId = voucher.VoucherId,
                VoucherCode = voucher.VoucherCode,
                VoucherType = voucher.VoucherType,
                WarehouseLabel = voucher.Warehouse != null ? voucher.Warehouse.WarehouseCode : "",
                FulfillmentStatus = voucher.FulfillmentStatus,
                Priority = voucher.Priority,
                CreatedAt = voucher.CreatedAt,
                RequestedDeliveryDate = voucher.RequestedDeliveryDate,
                TotalLines = voucher.TotalLines,
                AssignedTo = voucher.PackedBy ?? voucher.ShippedBy
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => MapOutbound(row, now, canAct, "transfer", "Chuyển kho")).ToList();
    }

    private static async Task<List<DashboardWorkItemViewModel>> LoadReturnWorkAsync(
        IQueryable<Voucher> vouchers,
        DateTime now,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var rows = await vouchers
            .Where(voucher => !voucher.IsCancelled && !voucher.IsPosted && ReturnTypes.Contains(voucher.VoucherType))
            .OrderBy(voucher => voucher.ExpectedArrivalAt ?? voucher.RequestedDeliveryDate ?? voucher.CreatedAt)
            .Take(25)
            .Select(voucher => new VoucherWorkProjection
            {
                VoucherId = voucher.VoucherId,
                VoucherCode = voucher.VoucherCode,
                VoucherType = voucher.VoucherType,
                WarehouseLabel = voucher.Warehouse != null ? voucher.Warehouse.WarehouseCode : "",
                InboundStatus = voucher.InboundStatus,
                FulfillmentStatus = voucher.FulfillmentStatus,
                Priority = voucher.Priority,
                CreatedAt = voucher.CreatedAt,
                SubmittedAt = voucher.SubmittedAt,
                ExpectedArrivalAt = voucher.ExpectedArrivalAt,
                RequestedDeliveryDate = voucher.RequestedDeliveryDate,
                TotalLines = voucher.TotalLines,
                AssignedTo = voucher.ReceivedBy ?? voucher.PackedBy
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => row.VoucherType == VoucherTypeEnum.KhachTra
                ? MapInbound(row, now, canAct, "return", "Khách trả hàng")
                : MapOutbound(row, now, canAct, "return", "Trả nhà cung cấp"))
            .ToList();
    }

    private static async Task<List<DashboardWorkItemViewModel>> LoadPickWorkAsync(
        IQueryable<PickTask> tasks,
        DateTime now,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var rows = await tasks
            .Where(task => task.Status != PickTaskStatusEnum.Completed && task.Status != PickTaskStatusEnum.Cancelled)
            .OrderBy(task => task.DueAt ?? task.AssignedAt ?? task.Voucher.VoucherDate)
            .Take(50)
            .Select(task => new PickWorkProjection
            {
                PickTaskId = task.PickTaskId,
                TaskCode = task.TaskCode,
                VoucherCode = task.Voucher != null ? task.Voucher.VoucherCode : "",
                WarehouseLabel = task.Voucher != null && task.Voucher.Warehouse != null ? task.Voucher.Warehouse.WarehouseCode : "",
                ItemCode = task.Item != null ? task.Item.ItemCode : "",
                Status = task.Status,
                TargetQty = task.TargetQty,
                PickedQty = task.PickedQty,
                AssignedTo = task.AssignedTo,
                StartedAt = task.StartedAt,
                AssignedAt = task.AssignedAt,
                DueAt = task.DueAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => MapPick(row, now, canAct)).ToList();
    }

    private static async Task<List<DashboardWorkItemViewModel>> LoadMovementWorkAsync(
        IQueryable<MovementTask> tasks,
        DateTime now,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var rows = await tasks
            .Where(task => task.Status != MovementTaskStatusEnum.Completed && task.Status != MovementTaskStatusEnum.Cancelled)
            .OrderBy(task => task.DueAt ?? task.CreatedAt)
            .Take(40)
            .Select(task => new MovementWorkProjection
            {
                MovementTaskId = task.MovementTaskId,
                TaskCode = task.TaskCode,
                WarehouseLabel = task.Warehouse != null ? task.Warehouse.WarehouseCode : "",
                ItemCode = task.Item != null ? task.Item.ItemCode : "",
                TaskType = task.TaskType,
                Status = task.Status,
                Priority = task.Priority,
                PlannedQty = task.PlannedQty,
                ConfirmedQty = task.ConfirmedQty,
                AssignedTo = task.AssignedTo,
                CreatedAt = task.CreatedAt,
                DueAt = task.DueAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => MapMovement(row, now, canAct)).ToList();
    }

    private static async Task<List<DashboardWorkItemViewModel>> LoadCountWorkAsync(
        IQueryable<StockCountSheet> sheets,
        DateTime now,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var rows = await sheets
            .Where(sheet => sheet.Status != StockCountStatusEnum.Approved)
            .OrderBy(sheet => sheet.CountDate)
            .Take(30)
            .Select(sheet => new CountWorkProjection
            {
                StockCountSheetId = sheet.StockCountSheetId,
                SheetCode = sheet.SheetCode ?? ("CC-" + sheet.StockCountSheetId),
                WarehouseLabel = sheet.Warehouse != null ? sheet.Warehouse.WarehouseCode : "",
                Status = sheet.Status,
                CountDate = sheet.CountDate,
                CreatedAt = sheet.CreatedAt,
                TotalLines = sheet.Lines.Count,
                CountedLines = sheet.Lines.Count(line => line.CountedQty.HasValue),
                HasVariance = sheet.Lines.Any(line => line.CountedQty.HasValue && line.CountedQty.Value != line.SystemQty)
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => MapCount(row, now, canAct)).ToList();
    }

    private static async Task<List<DashboardWorkItemViewModel>> LoadQualityWorkAsync(
        IQueryable<QualityInspection> inspections,
        DateTime now,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var rows = await inspections
            .Where(inspection =>
                inspection.OverallResult == QualityStatusEnum.Pending
                || inspection.OverallResult == QualityStatusEnum.Inspecting
                || inspection.OverallResult == QualityStatusEnum.Failed
                || inspection.OverallResult == QualityStatusEnum.Quarantine
                || inspection.OverallResult == QualityStatusEnum.OnHold)
            .OrderBy(inspection => inspection.CreatedAt)
            .Take(30)
            .Select(inspection => new QualityWorkProjection
            {
                QualityInspectionId = inspection.QualityInspectionId,
                VoucherId = inspection.VoucherId,
                VoucherCode = inspection.Voucher != null ? inspection.Voucher.VoucherCode : "",
                WarehouseLabel = inspection.Warehouse != null ? inspection.Warehouse.WarehouseCode : "",
                ItemCode = inspection.Item != null ? inspection.Item.ItemCode : "",
                OverallResult = inspection.OverallResult,
                SampleQty = inspection.SampleQty,
                PassedQty = inspection.PassedQty,
                FailedQty = inspection.FailedQty,
                InspectorName = inspection.InspectorName,
                DefectDescription = inspection.DefectDescription,
                CreatedAt = inspection.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => MapQuality(row, now, canAct)).ToList();
    }

    private async Task<List<DashboardWorkItemViewModel>> LoadExceptionWorkAsync(
        int? warehouseId,
        DateTime now,
        bool canAct,
        CancellationToken cancellationToken)
    {
        var query = _db.OperationExceptionCases.AsNoTracking()
            .Where(item => item.Status == OperationExceptionStatusEnum.Open
                || item.Status == OperationExceptionStatusEnum.Acknowledged);
        if (warehouseId.HasValue)
            query = query.Where(item => item.WarehouseId == warehouseId.Value);

        var rows = await query
            .OrderBy(item => item.FirstDetectedAt)
            .Take(40)
            .Select(item => new ExceptionWorkProjection
            {
                OperationExceptionCaseId = item.OperationExceptionCaseId,
                CategoryLabel = item.CategoryLabel,
                ReferenceCode = item.ReferenceCode,
                WarehouseLabel = item.Warehouse != null ? item.Warehouse.WarehouseCode : "",
                Status = item.Status,
                AssignedTo = item.AssignedTo,
                FirstDetectedAt = item.FirstDetectedAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => MapException(row, now, canAct)).ToList();
    }

    private static DashboardWorkItemViewModel MapInbound(
        VoucherWorkProjection row,
        DateTime now,
        bool canAct,
        string kindKey = "inbound",
        string kindLabel = "Nhập kho")
    {
        var deadline = row.DockAppointmentEnd ?? row.ExpectedArrivalAt
            ?? (row.SubmittedAt.HasValue ? row.SubmittedAt.Value.AddHours(4) : row.CreatedAt.AddHours(8));
        var state = row.InboundStatus switch
        {
            InboundStatusEnum.PendingApproval => ("waiting", "Chờ duyệt", 2m),
            InboundStatusEnum.Receiving => ("in-progress", "Đang tiếp nhận", 4m),
            InboundStatusEnum.Approved => ("not-started", "Chưa tiếp nhận", 3m),
            _ => ("not-started", "Chưa bắt đầu", 1m)
        };
        if (deadline < now && state.Item1 != "in-progress")
            state = ("overdue", "Trễ hạn", state.Item3);

        var item = NewVoucherItem(row, kindKey, kindLabel, state.Item1, state.Item2, state.Item3, deadline, canAct);
        item.Title = row.InboundStatus == InboundStatusEnum.PendingApproval
            ? "Phiếu nhập cần duyệt trước khi nhận hàng"
            : "Phiếu nhập cần tiếp nhận hoặc hoàn tất";
        item.ActionUrl = row.InboundStatus == InboundStatusEnum.PendingApproval
            ? "/Operations/InboundApprovals"
            : $"/Vouchers/Details/{row.VoucherId}";
        item.ActionLabel = canAct
            ? (row.InboundStatus == InboundStatusEnum.PendingApproval ? "Mở hàng đợi duyệt" : "Tiếp tục xử lý")
            : "Xem chi tiết";
        ApplySeverity(item, now, row.Priority);
        return item;
    }

    private static DashboardWorkItemViewModel MapOutbound(
        VoucherWorkProjection row,
        DateTime now,
        bool canAct,
        string kindKey,
        string kindLabel)
    {
        var deadline = row.RequestedDeliveryDate?.Date.AddDays(1) ?? row.CreatedAt.AddHours(8);
        var state = row.FulfillmentStatus switch
        {
            FulfillmentStatusEnum.Picking => ("in-progress", "Đang lấy hàng", 2m),
            FulfillmentStatusEnum.Picked => ("waiting", "Chờ chốt xuất", 3m),
            FulfillmentStatusEnum.Packed => ("waiting", "Chờ bàn giao", 4m),
            FulfillmentStatusEnum.PartiallyIssued => ("blocked", "Còn phần chưa xử lý", 3m),
            FulfillmentStatusEnum.Shipped => ("in-progress", "Đang vận chuyển", 4m),
            _ => ("not-started", "Chưa bắt đầu", 1m)
        };
        if (deadline < now && state.Item1 is not "blocked")
            state = ("overdue", "Trễ hạn", state.Item3);

        var item = NewVoucherItem(row, kindKey, kindLabel, state.Item1, state.Item2, state.Item3, deadline, canAct);
        item.Title = kindKey == "transfer" ? "Phiếu chuyển kho cần hoàn tất" : "Phiếu xuất cần hoàn tất trước hạn giao";
        item.ActionUrl = $"/Vouchers/Details/{row.VoucherId}";
        item.ActionLabel = canAct ? "Tiếp tục xử lý" : "Xem chi tiết";
        if (row.FulfillmentStatus == FulfillmentStatusEnum.PartiallyIssued)
            item.BlockerReason = "Đã xuất một phần, còn khối lượng chưa hoàn tất";
        ApplySeverity(item, now, row.Priority);
        return item;
    }

    private static DashboardWorkItemViewModel NewVoucherItem(
        VoucherWorkProjection row,
        string kindKey,
        string kindLabel,
        string stateKey,
        string stateLabel,
        decimal workflowStep,
        DateTime deadline,
        bool canAct)
        => new()
        {
            Key = $"voucher:{row.VoucherId}",
            KindKey = kindKey,
            KindLabel = kindLabel,
            ReferenceCode = row.VoucherCode,
            WarehouseLabel = row.WarehouseLabel,
            StateKey = stateKey,
            StateLabel = stateLabel,
            ProgressDone = workflowStep,
            ProgressTotal = 5,
            ProgressUnit = "bước",
            Deadline = deadline,
            WaitingSince = row.SubmittedAt ?? row.CreatedAt,
            Assignee = string.IsNullOrWhiteSpace(row.AssignedTo) ? "Chưa phân công" : row.AssignedTo,
            CanAct = canAct
        };

    private static DashboardWorkItemViewModel MapPick(PickWorkProjection row, DateTime now, bool canAct)
    {
        var deadline = row.DueAt ?? (row.AssignedAt ?? now).AddHours(4);
        var state = row.Status switch
        {
            PickTaskStatusEnum.Short => ("blocked", "Thiếu hàng"),
            PickTaskStatusEnum.InProgress => ("in-progress", "Đang lấy hàng"),
            PickTaskStatusEnum.Assigned => ("not-started", "Đã phân công"),
            PickTaskStatusEnum.WaitingForBulk => ("waiting", "Chờ gom hàng"),
            _ => ("not-started", "Chưa bắt đầu")
        };
        if (deadline < now && state.Item1 is not "blocked")
            state = ("overdue", "Trễ hạn");

        var item = new DashboardWorkItemViewModel
        {
            Key = $"pick:{row.PickTaskId}",
            KindKey = "pick",
            KindLabel = "Lấy hàng",
            ReferenceCode = row.TaskCode,
            Title = string.IsNullOrWhiteSpace(row.ItemCode)
                ? $"Nhiệm vụ lấy hàng cho {row.VoucherCode}"
                : $"Lấy {row.ItemCode} cho {row.VoucherCode}",
            WarehouseLabel = row.WarehouseLabel,
            StateKey = state.Item1,
            StateLabel = state.Item2,
            ProgressDone = row.PickedQty,
            ProgressTotal = row.TargetQty,
            ProgressUnit = "đơn vị gốc",
            Deadline = deadline,
            WaitingSince = row.StartedAt ?? row.AssignedAt ?? now,
            Assignee = string.IsNullOrWhiteSpace(row.AssignedTo) ? "Chưa phân công" : row.AssignedTo,
            BlockerReason = row.Status == PickTaskStatusEnum.Short ? "Thiếu tồn hoặc không lấy đủ số lượng yêu cầu" : null,
            ActionUrl = "/Operations/PickTasks",
            ActionLabel = canAct ? "Xử lý nhiệm vụ" : "Xem nhiệm vụ",
            CanAct = canAct
        };
        ApplySeverity(item, now, row.Status == PickTaskStatusEnum.Short ? 100 : 50);
        return item;
    }

    private static DashboardWorkItemViewModel MapMovement(MovementWorkProjection row, DateTime now, bool canAct)
    {
        var deadline = row.DueAt ?? row.CreatedAt.AddHours(8);
        var state = row.Status switch
        {
            MovementTaskStatusEnum.Short => ("blocked", "Không đủ hàng"),
            MovementTaskStatusEnum.InProgress => ("in-progress", "Đang di chuyển"),
            MovementTaskStatusEnum.Assigned => ("not-started", "Đã phân công"),
            _ => ("not-started", "Chưa bắt đầu")
        };
        if (deadline < now && state.Item1 is not "blocked")
            state = ("overdue", "Trễ hạn");

        var item = new DashboardWorkItemViewModel
        {
            Key = $"movement:{row.MovementTaskId}",
            KindKey = row.TaskType == MovementTaskTypeEnum.Replenishment ? "replenishment" : "movement",
            KindLabel = row.TaskType == MovementTaskTypeEnum.Replenishment ? "Bổ sung hàng" : "Di chuyển tồn",
            ReferenceCode = row.TaskCode,
            Title = string.IsNullOrWhiteSpace(row.ItemCode) ? "Nhiệm vụ di chuyển tồn kho" : $"Di chuyển {row.ItemCode}",
            WarehouseLabel = row.WarehouseLabel,
            StateKey = state.Item1,
            StateLabel = state.Item2,
            ProgressDone = row.ConfirmedQty,
            ProgressTotal = row.PlannedQty,
            ProgressUnit = "đơn vị gốc",
            Deadline = deadline,
            WaitingSince = row.CreatedAt,
            Assignee = string.IsNullOrWhiteSpace(row.AssignedTo) ? "Chưa phân công" : row.AssignedTo,
            BlockerReason = row.Status == MovementTaskStatusEnum.Short ? "Vị trí nguồn không còn đủ số lượng" : null,
            ActionUrl = "/Operations/MovementTasks",
            ActionLabel = canAct ? "Xử lý nhiệm vụ" : "Xem nhiệm vụ",
            CanAct = canAct
        };
        ApplySeverity(item, now, row.Priority == MovementTaskPriorityEnum.Urgent ? 100 : row.Priority == MovementTaskPriorityEnum.High ? 90 : 50);
        return item;
    }

    private static DashboardWorkItemViewModel MapCount(CountWorkProjection row, DateTime now, bool canAct)
    {
        var deadline = row.CountDate.Date.AddDays(1);
        var state = row.Status switch
        {
            StockCountStatusEnum.Counting => ("in-progress", "Đang kiểm đếm"),
            StockCountStatusEnum.Counted => ("waiting", "Chờ duyệt chênh lệch"),
            _ => ("not-started", "Chưa kiểm đếm")
        };
        if (deadline < now && state.Item1 != "waiting")
            state = ("overdue", "Trễ hạn");

        var item = new DashboardWorkItemViewModel
        {
            Key = $"count:{row.StockCountSheetId}",
            KindKey = "cycle-count",
            KindLabel = "Kiểm kê",
            ReferenceCode = row.SheetCode,
            Title = row.HasVariance ? "Phiếu kiểm kê có chênh lệch cần duyệt" : "Phiếu kiểm kê cần hoàn tất",
            WarehouseLabel = row.WarehouseLabel,
            StateKey = state.Item1,
            StateLabel = state.Item2,
            ProgressDone = row.CountedLines,
            ProgressTotal = Math.Max(row.TotalLines, 1),
            ProgressUnit = "dòng",
            Deadline = deadline,
            WaitingSince = row.CreatedAt,
            Assignee = "Nhóm kiểm kê",
            BlockerReason = row.HasVariance ? "Có chênh lệch giữa số đếm và số hệ thống" : null,
            ActionUrl = "/Reports/StockCount",
            ActionLabel = canAct ? "Mở phiếu kiểm kê" : "Xem phiếu kiểm kê",
            CanAct = canAct
        };
        ApplySeverity(item, now, row.HasVariance ? 90 : 50);
        return item;
    }

    private static DashboardWorkItemViewModel MapQuality(QualityWorkProjection row, DateTime now, bool canAct)
    {
        var deadline = row.CreatedAt.AddHours(4);
        var blocked = row.OverallResult is QualityStatusEnum.Failed or QualityStatusEnum.Quarantine or QualityStatusEnum.OnHold;
        var state = blocked
            ? ("blocked", "Chờ quyết định xử lý")
            : row.OverallResult == QualityStatusEnum.Inspecting
                ? ("in-progress", "Đang kiểm tra")
                : ("not-started", "Chưa kiểm tra");
        if (deadline < now && !blocked)
            state = ("overdue", "Trễ hạn");

        var item = new DashboardWorkItemViewModel
        {
            Key = $"quality:{row.QualityInspectionId}",
            KindKey = "quality",
            KindLabel = "Chất lượng",
            ReferenceCode = string.IsNullOrWhiteSpace(row.VoucherCode) ? $"QC-{row.QualityInspectionId}" : row.VoucherCode,
            Title = string.IsNullOrWhiteSpace(row.ItemCode) ? "Kiểm tra chất lượng hàng nhập" : $"Kiểm tra chất lượng {row.ItemCode}",
            WarehouseLabel = row.WarehouseLabel,
            StateKey = state.Item1,
            StateLabel = state.Item2,
            ProgressDone = row.PassedQty + row.FailedQty,
            ProgressTotal = Math.Max(row.SampleQty, 1),
            ProgressUnit = "mẫu",
            Deadline = deadline,
            WaitingSince = row.CreatedAt,
            Assignee = string.IsNullOrWhiteSpace(row.InspectorName) ? "Chưa phân công" : row.InspectorName,
            BlockerReason = blocked
                ? (string.IsNullOrWhiteSpace(row.DefectDescription) ? "Hàng đang bị giữ để chờ quyết định chất lượng" : row.DefectDescription)
                : null,
            ActionUrl = "/Operations/QualityInspection",
            ActionLabel = canAct ? "Xử lý kiểm tra" : "Xem kiểm tra",
            CanAct = canAct
        };
        ApplySeverity(item, now, blocked ? 100 : 50);
        return item;
    }

    private static DashboardWorkItemViewModel MapException(ExceptionWorkProjection row, DateTime now, bool canAct)
    {
        var deadline = row.FirstDetectedAt.AddHours(8);
        var item = new DashboardWorkItemViewModel
        {
            Key = $"exception:{row.OperationExceptionCaseId}",
            KindKey = "exception",
            KindLabel = "Ngoại lệ",
            ReferenceCode = string.IsNullOrWhiteSpace(row.ReferenceCode) ? $"EX-{row.OperationExceptionCaseId}" : row.ReferenceCode,
            Title = string.IsNullOrWhiteSpace(row.CategoryLabel) ? "Ngoại lệ vận hành cần xử lý" : row.CategoryLabel,
            WarehouseLabel = row.WarehouseLabel,
            StateKey = row.Status == OperationExceptionStatusEnum.Acknowledged ? "in-progress" : deadline < now ? "overdue" : "not-started",
            StateLabel = row.Status == OperationExceptionStatusEnum.Acknowledged ? "Đang xử lý" : deadline < now ? "Trễ hạn" : "Mới phát hiện",
            ProgressDone = row.Status == OperationExceptionStatusEnum.Acknowledged ? 1 : 0,
            ProgressTotal = 2,
            ProgressUnit = "bước",
            Deadline = deadline,
            WaitingSince = row.FirstDetectedAt,
            Assignee = string.IsNullOrWhiteSpace(row.AssignedTo) ? "Chưa phân công" : row.AssignedTo,
            BlockerReason = "Cần xác minh nguyên nhân và ghi nhận cách xử lý",
            ActionUrl = "/Operations/ExceptionCenter",
            ActionLabel = canAct ? "Xử lý ngoại lệ" : "Xem ngoại lệ",
            CanAct = canAct
        };
        ApplySeverity(item, now, 90);
        return item;
    }

    private static void ApplySeverity(DashboardWorkItemViewModel item, DateTime now, int priority)
    {
        var overdueHours = item.Deadline.HasValue && item.Deadline.Value < now
            ? (now - item.Deadline.Value).TotalHours
            : 0;

        if (item.StateKey == "blocked" || overdueHours >= 8)
        {
            item.SeverityKey = "critical";
            item.SeverityLabel = "Khẩn cấp";
            item.SeverityRank = 0;
        }
        else if (item.StateKey == "overdue"
            || priority >= 90
            || (item.Deadline.HasValue && item.Deadline.Value >= now && item.Deadline.Value <= now.AddHours(2)))
        {
            item.SeverityKey = "high";
            item.SeverityLabel = "Cao";
            item.SeverityRank = 1;
        }
        else if (item.Deadline.HasValue && item.Deadline.Value <= now.AddHours(8))
        {
            item.SeverityKey = "medium";
            item.SeverityLabel = "Trung bình";
            item.SeverityRank = 2;
        }
        else
        {
            item.SeverityKey = "low";
            item.SeverityLabel = "Thấp";
            item.SeverityRank = 3;
        }
    }

    private static void PopulateCounters(
        DashboardCommandCenterViewModel model,
        IReadOnlyCollection<DashboardWorkItemViewModel> items)
    {
        model.TotalWorkItems = items.Count;
        model.NotStartedCount = items.Count(item => item.StateKey == "not-started");
        model.InProgressCount = items.Count(item => item.StateKey == "in-progress");
        model.WaitingCount = items.Count(item => item.StateKey == "waiting");
        model.BlockedCount = items.Count(item => item.StateKey == "blocked");
        model.OverdueCount = items.Count(item => item.StateKey == "overdue");
        model.CriticalCount = items.Count(item => item.SeverityKey == "critical");
        model.HighCount = items.Count(item => item.SeverityKey == "high");
        model.MediumCount = items.Count(item => item.SeverityKey == "medium");
        model.LowCount = items.Count(item => item.SeverityKey == "low");
    }

    private static void ApplyFiltersAndSort(
        DashboardCommandCenterViewModel model,
        IEnumerable<DashboardWorkItemViewModel> source,
        int requestedLimit)
    {
        var filtered = source;
        if (!string.IsNullOrWhiteSpace(model.SelectedWorkState))
            filtered = filtered.Where(item => string.Equals(item.StateKey, model.SelectedWorkState, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(model.SelectedSeverity))
            filtered = filtered.Where(item => string.Equals(item.SeverityKey, model.SelectedSeverity, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(model.SelectedAssignee))
            filtered = filtered.Where(item => item.Assignee.Contains(model.SelectedAssignee, StringComparison.OrdinalIgnoreCase));

        var ordered = filtered
            .OrderBy(item => item.SeverityRank)
            .ThenBy(item => item.Deadline ?? DateTime.MaxValue)
            .ThenBy(item => item.WaitingSince)
            .ThenBy(item => item.ReferenceCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var limit = Math.Clamp(requestedLimit, 5, 50);
        model.FilteredWorkItems = ordered.Count;
        model.HiddenByLimitCount = Math.Max(0, ordered.Count - limit);
        model.WorkItems = ordered.Take(limit).ToList();
    }

    private async Task LoadWidgetAsync(
        DashboardCommandCenterViewModel model,
        string widgetName,
        Func<Task> loader,
        CancellationToken cancellationToken)
    {
        try
        {
            await loader();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            model.IsPartial = true;
            model.DataWarnings.Add($"Chưa tải được dữ liệu {widgetName}.");
            _logger.LogWarning(ex, "Dashboard widget {DashboardWidget} failed", widgetName);
        }
    }

    private static string? NormalizeFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private sealed class ProcessStats
    {
        public int DueToday { get; set; }
        public int CompletedToday { get; set; }
        public int Open { get; set; }
        public int Overdue { get; set; }
    }

    private sealed class VoucherWorkProjection
    {
        public long VoucherId { get; set; }
        public string VoucherCode { get; set; } = "";
        public VoucherTypeEnum VoucherType { get; set; }
        public string WarehouseLabel { get; set; } = "";
        public InboundStatusEnum InboundStatus { get; set; }
        public FulfillmentStatusEnum FulfillmentStatus { get; set; }
        public int Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ExpectedArrivalAt { get; set; }
        public DateTime? DockAppointmentEnd { get; set; }
        public DateTime? RequestedDeliveryDate { get; set; }
        public int TotalLines { get; set; }
        public string? AssignedTo { get; set; }
    }

    private sealed class PickWorkProjection
    {
        public long PickTaskId { get; set; }
        public string TaskCode { get; set; } = "";
        public string VoucherCode { get; set; } = "";
        public string WarehouseLabel { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public PickTaskStatusEnum Status { get; set; }
        public decimal TargetQty { get; set; }
        public decimal PickedQty { get; set; }
        public string? AssignedTo { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? DueAt { get; set; }
    }

    private sealed class MovementWorkProjection
    {
        public long MovementTaskId { get; set; }
        public string TaskCode { get; set; } = "";
        public string WarehouseLabel { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public MovementTaskTypeEnum TaskType { get; set; }
        public MovementTaskStatusEnum Status { get; set; }
        public MovementTaskPriorityEnum Priority { get; set; }
        public decimal PlannedQty { get; set; }
        public decimal ConfirmedQty { get; set; }
        public string? AssignedTo { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? DueAt { get; set; }
    }

    private sealed class CountWorkProjection
    {
        public long StockCountSheetId { get; set; }
        public string SheetCode { get; set; } = "";
        public string WarehouseLabel { get; set; } = "";
        public StockCountStatusEnum Status { get; set; }
        public DateTime CountDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalLines { get; set; }
        public int CountedLines { get; set; }
        public bool HasVariance { get; set; }
    }

    private sealed class QualityWorkProjection
    {
        public long QualityInspectionId { get; set; }
        public long VoucherId { get; set; }
        public string VoucherCode { get; set; } = "";
        public string WarehouseLabel { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public QualityStatusEnum OverallResult { get; set; }
        public decimal SampleQty { get; set; }
        public decimal PassedQty { get; set; }
        public decimal FailedQty { get; set; }
        public string? InspectorName { get; set; }
        public string? DefectDescription { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class ExceptionWorkProjection
    {
        public long OperationExceptionCaseId { get; set; }
        public string CategoryLabel { get; set; } = "";
        public string ReferenceCode { get; set; } = "";
        public string WarehouseLabel { get; set; } = "";
        public OperationExceptionStatusEnum Status { get; set; }
        public string? AssignedTo { get; set; }
        public DateTime FirstDetectedAt { get; set; }
    }
}
