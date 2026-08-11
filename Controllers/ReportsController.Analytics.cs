using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

using Microsoft.EntityFrameworkCore;

using WMS.Data;

using WMS.ViewModels;

using ClosedXML.Excel;

using System.IO;

using WMS.Models;

using System.Data;

using WMS.Authorization;

using WMS.Common;

using WMS.Services;

using Microsoft.Extensions.Logging.Abstractions;

namespace WMS.Controllers;

public partial class ReportsController
{

    [Authorize(Roles = "Admin")]
    [Authorize(Policy = WmsPermissions.AuditTrailView)]
    public async Task<IActionResult> AuditTrail(string? tableName, string? changedBy, DateTime? dateFrom, DateTime? dateTo)
    {
        dateFrom ??= VietnamNow.Date.AddDays(-7);
        dateTo ??= VietnamNow.Date.AddDays(1);

        var query = _db.AuditLogs
            .Where(a => a.ChangedAt >= dateFrom.Value && a.ChangedAt <= dateTo.Value);

        if (!string.IsNullOrWhiteSpace(tableName))
            query = query.Where(a => a.TableName == tableName);
        if (!string.IsNullOrWhiteSpace(changedBy))
            query = query.Where(a => a.ChangedBy != null && a.ChangedBy.Contains(changedBy));

        ViewBag.TableName = tableName;
        ViewBag.ChangedBy = changedBy;
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;

        var logs = await query.OrderByDescending(a => a.ChangedAt).Take(200).ToListAsync();
        return View(logs);
    }


    [Authorize(Roles = "Admin")]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> Alerts(AlertTypeEnum? type, bool? unresolvedOnly, int days = 30)
    {
        unresolvedOnly ??= true;
        if (days < 1) days = 1;
        if (days > 365) days = 365;

        ViewBag.Type = type;
        ViewBag.UnresolvedOnly = unresolvedOnly;
        ViewBag.Days = days;

        var query = _db.StockAlerts.AsNoTracking()
            .Include(a => a.Item)
            .Where(a => a.Item != null && a.Item.IsActive);

        if (type.HasValue) query = query.Where(a => a.AlertType == type.Value);
        if (unresolvedOnly == true) query = query.Where(a => !a.IsResolved);

        var alerts = await query.OrderBy(a => a.IsResolved).ThenByDescending(a => a.CreatedAt).Take(500).ToListAsync();
        return View(alerts);
    }


    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshExpiryAlerts(int days = 30)
    {
        if (days < 1) days = 1;
        if (days > 365) days = 365;

        var today = VietnamNow.Date;
        var cutoff = today.AddDays(days);

        // Aggregate expiring quantity per item (within window) and the nearest expiry date
        var expiring = await _db.ItemLocations.AsNoTracking()
            .Include(il => il.Item)
            .Where(il => il.Quantity > 0
                && il.Item != null
                && il.Item.IsActive
                && il.ExpiryDate.HasValue
                && il.ExpiryDate.Value.Date <= cutoff)
            .GroupBy(il => il.ItemId)
            .Select(g => new
            {
                ItemId = g.Key,
                Qty = g.Sum(x => x.Quantity),
                NearestExpiry = g.Min(x => x.ExpiryDate)
            })
            .ToListAsync();

        var expiringMap = expiring.ToDictionary(x => x.ItemId, x => x);

        // Upsert unresolved expiry alerts
        var existing = await _db.StockAlerts
            .Where(a => a.AlertType == AlertTypeEnum.Expiry && !a.IsResolved)
            .ToListAsync();

        foreach (var alert in existing)
        {
            if (!expiringMap.TryGetValue(alert.ItemId, out var e) || e.NearestExpiry == null)
            {
                alert.IsResolved = true;
                alert.ResolvedAt = VietnamNow;
                continue;
            }

            var nearest = e.NearestExpiry.Value.Date;
            var daysLeft = (nearest - today).Days;
            alert.CurrentStock = e.Qty;
            alert.Threshold = daysLeft;
            alert.IsRead = false;
        }

        foreach (var e in expiring)
        {
            if (e.NearestExpiry == null) continue;
            if (existing.Any(a => a.ItemId == e.ItemId)) continue;

            var nearest = e.NearestExpiry.Value.Date;
            var daysLeft = (nearest - today).Days;

            _db.StockAlerts.Add(new StockAlert
            {
                ItemId = e.ItemId,
                AlertType = AlertTypeEnum.Expiry,
                CurrentStock = e.Qty, // expiring qty
                Threshold = daysLeft, // days left
                IsRead = false,
                IsResolved = false,
                CreatedAt = VietnamNow
            });
        }

        await _unitOfWork.SaveChangesAsync();

        TempData["Success"] = $"Đã làm mới cảnh báo hết hạn (<= {days} ngày).";
        return RedirectToAction(nameof(Alerts), new { type = (byte)3, unresolvedOnly = true, days });
    }


    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveAlert(long id, byte? type, bool? unresolvedOnly, int days = 30)
    {
        var alert = await _db.StockAlerts.FindAsync(id);
        if (alert == null) return NotFound();
        alert.IsResolved = true;
        alert.ResolvedAt = VietnamNow;
        await _unitOfWork.SaveChangesAsync();
        return RedirectToAction(nameof(Alerts), new { type, unresolvedOnly, days });
    }


    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> OpsKpi(int? warehouseId)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;

        var wavesQuery = _db.Waves.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue) wavesQuery = wavesQuery.Where(w => w.WarehouseId == warehouseId.Value);
        var waveIds = await wavesQuery.Select(w => w.WaveId).ToListAsync();

        var tasksQuery = _db.PickTasks.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
        {
            tasksQuery = tasksQuery.Where(t =>
                (t.WaveId.HasValue && waveIds.Contains(t.WaveId.Value))
                || (!t.WaveId.HasValue && t.Voucher != null && t.Voucher.WarehouseId == warehouseId.Value));
        }
        var totalTasks = await tasksQuery.CountAsync();
        var doneTasks = await tasksQuery.CountAsync(t => t.Status == PickTaskStatusEnum.Completed);
        var shortTasks = await tasksQuery.CountAsync(t => t.Status == PickTaskStatusEnum.Short);
        var openTasks = await tasksQuery.CountAsync(t => t.Status == PickTaskStatusEnum.Pending || t.Status == PickTaskStatusEnum.Assigned || t.Status == PickTaskStatusEnum.InProgress);
        var completedDurations = await tasksQuery
            .Where(t => t.CompletedAt.HasValue && t.AssignedAt.HasValue)
            .Select(t => EF.Functions.DateDiffMinute(t.AssignedAt!.Value, t.CompletedAt!.Value))
            .ToListAsync();
        var avgMinutes = completedDurations.Count > 0 ? completedDurations.Average() : 0;

        var reservations = await _db.StockReservations.AsNoTracking()
            .Where(r => !warehouseId.HasValue || (r.Voucher != null && r.Voucher.WarehouseId == warehouseId.Value))
            .ToListAsync();
        var reserved = reservations.Sum(r => r.ReservedQty);
        var consumed = reservations.Sum(r => r.ConsumedQty);
        var fillRate = reserved > 0 ? (consumed / reserved) * 100m : 0m;

        var shippingQuery = _db.Vouchers.AsNoTracking()
            .Where(v => !v.IsCancelled
                && v.IsPosted
                && (v.VoucherType == VoucherTypeEnum.XuatKho
                    || v.VoucherType == VoucherTypeEnum.TraNCC
                    || v.VoucherType == VoucherTypeEnum.ChuyenKho
                    || v.VoucherType == VoucherTypeEnum.XuatSanXuat));
        if (warehouseId.HasValue)
            shippingQuery = shippingQuery.Where(v => v.WarehouseId == warehouseId.Value);

        var shippingRows = await shippingQuery
            .Select(v => new
            {
                v.VoucherId,
                v.WarehouseId,
                v.VoucherCode,
                v.VoucherDate,
                v.RequestedDeliveryDate,
                v.PackedAt,
                v.ShippedAt,
                v.TrackingNumber,
                v.ManifestCode,
                v.VoucherType
            })
            .ToListAsync();

        var waitingPacking = shippingRows.Count(v => !v.PackedAt.HasValue);
        var readyToShip = shippingRows.Count(v => v.PackedAt.HasValue && !v.ShippedAt.HasValue);
        var shippedCount = shippingRows.Count(v => v.ShippedAt.HasValue);
        var overdueShipping = shippingRows.Count(v => v.RequestedDeliveryDate.HasValue && v.RequestedDeliveryDate.Value.Date < VietnamNow.Date && !v.ShippedAt.HasValue);
        var shippedToday = shippingRows.Count(v => v.ShippedAt.HasValue && v.ShippedAt.Value.Date == VietnamNow.Date);
        var onTimeShipCandidates = shippingRows.Where(v => v.ShippedAt.HasValue && v.RequestedDeliveryDate.HasValue).ToList();
        var onTimeShipRate = onTimeShipCandidates.Count > 0
            ? onTimeShipCandidates.Count(v => v.ShippedAt!.Value.Date <= v.RequestedDeliveryDate!.Value.Date) * 100m / onTimeShipCandidates.Count
            : 0m;
        var packLeadHours = shippingRows
            .Where(v => v.PackedAt.HasValue)
            .Select(v => (v.PackedAt!.Value - v.VoucherDate).TotalHours)
            .ToList();
        var shipLeadHours = shippingRows
            .Where(v => v.ShippedAt.HasValue)
            .Select(v => (v.ShippedAt!.Value - (v.PackedAt ?? v.VoucherDate)).TotalHours)
            .ToList();
        var avgPackLeadHours = packLeadHours.Count > 0 ? packLeadHours.Average() : 0;
        var avgShipLeadHours = shipLeadHours.Count > 0 ? shipLeadHours.Average() : 0;

        var recentHandoverQuery = _db.ShippingHandoverLogs.AsNoTracking()
            .Include(x => x.Voucher)
            .Include(x => x.Warehouse)
            .AsQueryable();
        if (warehouseId.HasValue)
            recentHandoverQuery = recentHandoverQuery.Where(x => x.WarehouseId == warehouseId.Value);

        var recentHandovers = await recentHandoverQuery
            .OrderByDescending(x => x.HandedOverAt)
            .Take(15)
            .ToListAsync();

        // ── SLA theo đơn vị vận chuyển (Carrier) ──
        var allHandovers = await _db.ShippingHandoverLogs.AsNoTracking()
            .Include(h => h.Voucher)
            .Where(h => !string.IsNullOrEmpty(h.CarrierName)
                && (!warehouseId.HasValue || h.WarehouseId == warehouseId.Value))
            .ToListAsync();

        var carrierSlaRows = allHandovers
            .GroupBy(h => h.CarrierName ?? "Không xác định")
            .Select(g =>
            {
                var shipped = g.ToList();
                var total = shipped.Count;
                var onTime = shipped.Count(h =>
                    h.Voucher != null
                    && h.Voucher.RequestedDeliveryDate.HasValue
                    && h.HandedOverAt.Date <= h.Voucher.RequestedDeliveryDate.Value.Date);
                var overdue = shipped.Count(h =>
                    h.Voucher != null
                    && h.Voucher.RequestedDeliveryDate.HasValue
                    && h.HandedOverAt.Date > h.Voucher.RequestedDeliveryDate.Value.Date);
                var leadHours = shipped
                    .Where(h => h.Voucher != null)
                    .Select(h => (h.HandedOverAt - h.Voucher!.VoucherDate).TotalHours)
                    .ToList();
                var packToShipHours = shipped
                    .Where(h => h.Voucher != null && h.Voucher.PackedAt.HasValue)
                    .Select(h => (h.HandedOverAt - h.Voucher!.PackedAt!.Value).TotalHours)
                    .ToList();
                return new CarrierSlaRow
                {
                    CarrierName = g.Key,
                    TotalShipped = total,
                    OnTimeCount = onTime,
                    OverdueCount = overdue,
                    OnTimeRate = total > 0 ? Math.Round(onTime * 100m / total, 1) : 0m,
                    AvgLeadHours = leadHours.Count > 0 ? Math.Round(leadHours.Average(), 1) : 0,
                    AvgPackToShipHours = packToShipHours.Count > 0 ? Math.Round(packToShipHours.Average(), 1) : 0
                };
            })
            .OrderByDescending(r => r.TotalShipped)
            .ToList();

        ViewBag.Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.TotalTasks = totalTasks;
        ViewBag.DoneTasks = doneTasks;
        ViewBag.ShortTasks = shortTasks;
        ViewBag.OpenTasks = openTasks;
        ViewBag.AvgMinutes = avgMinutes;
        ViewBag.FillRate = fillRate;
        ViewBag.WaveCount = waveIds.Count;
        ViewBag.WaitingPacking = waitingPacking;
        ViewBag.ReadyToShip = readyToShip;
        ViewBag.ShippedCount = shippedCount;
        ViewBag.OverdueShipping = overdueShipping;
        ViewBag.ShippedToday = shippedToday;
        ViewBag.OnTimeShipRate = onTimeShipRate;
        ViewBag.AvgPackLeadHours = avgPackLeadHours;
        ViewBag.AvgShipLeadHours = avgShipLeadHours;
        ViewBag.RecentHandovers = recentHandovers;
        ViewBag.CarrierSlaRows = carrierSlaRows;

        var recentWaves = await wavesQuery
            .OrderByDescending(w => w.CreatedAt)
            .Take(10)
            .Select(w => new WaveBoardRow
            {
                WaveId = w.WaveId,
                WaveCode = w.WaveCode,
                WarehouseId = w.WarehouseId,
                WarehouseName = w.Warehouse != null ? w.Warehouse.WarehouseCode + " - " + w.Warehouse.WarehouseName : "",
                Status = w.Status,
                OpenTasks = 0,
                DoneTasks = 0,
                CreatedAt = w.CreatedAt,
                CompletedAt = w.CompletedAt
            })
            .ToListAsync();
        var recentWaveIds = recentWaves.Select(w => w.WaveId).Where(id => id.HasValue).Select(id => id!.Value).ToList();
        var taskStatsByWave = recentWaveIds.Count == 0
            ? new Dictionary<long, (int Open, int Done)>()
            : await _db.PickTasks.AsNoTracking()
                .Where(t => t.WaveId.HasValue && recentWaveIds.Contains(t.WaveId.Value))
                .GroupBy(t => t.WaveId!.Value)
                .Select(g => new
                {
                    WaveId = g.Key,
                    Open = g.Count(t => t.Status == PickTaskStatusEnum.Pending || t.Status == PickTaskStatusEnum.Assigned || t.Status == PickTaskStatusEnum.InProgress),
                    Done = g.Count(t => t.Status == PickTaskStatusEnum.Completed)
                })
                .ToDictionaryAsync(x => x.WaveId, x => (x.Open, x.Done));
        foreach (var wave in recentWaves)
        {
            if (wave.WaveId.HasValue && taskStatsByWave.TryGetValue(wave.WaveId.Value, out var stats))
            {
                wave.OpenTasks = stats.Open;
                wave.DoneTasks = stats.Done;
            }
        }

        var recentTasksQuery = _db.PickTasks.AsNoTracking()
            .Include(t => t.Wave)
            .Include(t => t.Voucher)
            .Include(t => t.Item)
            .Include(t => t.SourceLocation)
            .AsQueryable();
        if (warehouseId.HasValue)
            recentTasksQuery = recentTasksQuery.Where(t =>
                (t.Wave != null && t.Wave.WarehouseId == warehouseId.Value)
                || (t.Wave == null && t.Voucher != null && t.Voucher.WarehouseId == warehouseId.Value));

        var recentTasks = await recentTasksQuery
            .OrderByDescending(t => t.AssignedAt ?? t.CompletedAt ?? DateTime.MinValue)
            .ThenByDescending(t => t.PickTaskId)
            .Take(20)
            .Select(t => new PickTaskBoardRow
            {
                PickTaskId = t.PickTaskId,
                TaskCode = t.TaskCode,
                WaveId = t.WaveId,
                WaveCode = t.Wave != null ? t.Wave.WaveCode : "Phát hành trực tiếp",
                VoucherCode = t.Voucher != null ? t.Voucher.VoucherCode : "",
                ItemCode = t.Item != null ? t.Item.ItemCode : "",
                LocationCode = t.SourceLocation != null ? t.SourceLocation.LocationCode : "",
                TargetQty = t.TargetQty,
                PickedQty = t.PickedQty,
                Status = t.Status,
                AssignedTo = t.AssignedTo,
                CompletedAt = t.CompletedAt
            })
            .ToListAsync();

        ViewBag.RecentWaves = (object)recentWaves;
        ViewBag.RecentTasks = (object)recentTasks;
        return View();
    }


    // ═══════════════════════════════════════════════════════════════
    // ENTERPRISE: Supplier inbound scorecard
    // ═══════════════════════════════════════════════════════════════
    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> SupplierInboundScorecard(int? warehouseId, int? days)
    {
        var scopedWarehouseId = GetScopedWarehouseId();
        if (scopedWarehouseId.HasValue) warehouseId = scopedWarehouseId.Value;
        days = Math.Clamp(days ?? 90, 1, 365);
        var fromDate = VietnamNow.Date.AddDays(-days.Value);
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        var voucherQuery = _db.Vouchers.AsNoTracking()
            .Include(voucher => voucher.Partner)
            .Include(voucher => voucher.Details)
                .ThenInclude(detail => detail.Item)
            .Where(voucher => voucher.VoucherType == VoucherTypeEnum.NhapKho
                && !voucher.IsCancelled
                && voucher.PartnerId.HasValue
                && voucher.Partner != null
                && voucher.VoucherDate >= fromDate
                && voucher.VoucherDate <= VietnamNow.Date);
        if (warehouseId.HasValue)
            voucherQuery = voucherQuery.Where(voucher => voucher.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
        {
            voucherQuery = voucherQuery.Where(voucher =>
                (voucher.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(voucher.OwnerPartnerId.Value))
                || voucher.Details.Any(detail => scopedOwnerIds.Contains(
                    detail.OwnerPartnerId ?? voucher.OwnerPartnerId ?? -1)));
        }

        var vouchers = await voucherQuery.ToListAsync();
        var voucherIds = vouchers.Select(voucher => voucher.VoucherId).ToList();

        var inspectionQuery = _db.QualityInspections.AsNoTracking()
            .Where(inspection => voucherIds.Contains(inspection.VoucherId));
        if (scopedOwnerIds.Count > 0)
        {
            inspectionQuery = inspectionQuery.Where(inspection => scopedOwnerIds.Contains(
                (inspection.VoucherDetail != null ? inspection.VoucherDetail.OwnerPartnerId : null)
                ?? (inspection.Voucher != null ? inspection.Voucher.OwnerPartnerId : null)
                ?? -1));
        }
        var inspections = await inspectionQuery.ToListAsync();

        var adjustmentQuery = _db.InventoryTransactions.AsNoTracking()
            .Where(transaction => transaction.VoucherId.HasValue
                && voucherIds.Contains(transaction.VoucherId.Value)
                && transaction.TransactionType == InventoryTransactionTypeEnum.Adjust);
        if (scopedOwnerIds.Count > 0)
            adjustmentQuery = adjustmentQuery.Where(transaction => transaction.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(transaction.OwnerPartnerId.Value));
        var adjustments = await adjustmentQuery.ToListAsync();

        IReadOnlyList<VoucherDetail> VisibleDetails(Voucher voucher)
            => scopedOwnerIds.Count == 0
                ? voucher.Details.ToList()
                : voucher.Details
                    .Where(detail => scopedOwnerIds.Contains(detail.OwnerPartnerId ?? voucher.OwnerPartnerId ?? -1))
                    .ToList();

        static decimal? Percent(int numerator, int denominator)
            => denominator > 0
                ? Math.Round(numerator * 100m / denominator, 1, MidpointRounding.AwayFromZero)
                : null;

        var rows = vouchers
            .GroupBy(voucher => new
            {
                PartnerId = voucher.PartnerId!.Value,
                PartnerCode = voucher.Partner!.PartnerCode,
                PartnerName = voucher.Partner.PartnerName
            })
            .Select(group =>
            {
                var supplierVouchers = group.ToList();
                var completedVouchers = supplierVouchers
                    .Where(voucher => voucher.IsPosted || voucher.InboundStatus == InboundStatusEnum.Completed)
                    .ToList();
                var detailsByVoucher = completedVouchers.ToDictionary(
                    voucher => voucher.VoucherId,
                    voucher => VisibleDetails(voucher));

                var onTimeSamples = supplierVouchers
                    .Select(voucher => new
                    {
                        Expected = voucher.DockAppointmentEnd ?? voucher.DockAppointmentStart ?? voucher.ExpectedArrivalAt,
                        Actual = voucher.GateInAt ?? voucher.DockArrivalAt ?? voucher.ReceivedAt
                    })
                    .Where(sample => sample.Expected.HasValue && sample.Actual.HasValue)
                    .ToList();
                var onTimeCount = onTimeSamples.Count(sample => sample.Actual!.Value <= sample.Expected!.Value);

                var inFullSamples = completedVouchers
                    .Where(voucher => detailsByVoucher[voucher.VoucherId].Count > 0)
                    .ToList();
                var inFullCount = inFullSamples.Count(voucher =>
                    detailsByVoucher[voucher.VoucherId].All(detail => detail.BaseQty > 0m && detail.DefectBaseQty <= 0m));

                var supplierInspectionGroups = inspections
                    .Where(inspection => supplierVouchers.Any(voucher => voucher.VoucherId == inspection.VoucherId))
                    .Where(inspection => inspection.OverallResult is QualityStatusEnum.Passed or QualityStatusEnum.Failed)
                    .GroupBy(inspection => inspection.VoucherId)
                    .ToList();
                var qualityPassedCount = supplierInspectionGroups.Count(inspectionGroup =>
                    inspectionGroup.All(inspection => inspection.OverallResult == QualityStatusEnum.Passed));

                var documentSamples = inFullSamples;
                var documentAccurateCount = documentSamples.Count(voucher =>
                    !string.IsNullOrWhiteSpace(voucher.ReferenceNo)
                    && detailsByVoucher[voucher.VoucherId].All(detail =>
                        (!detail.Item.TrackLot || !string.IsNullOrWhiteSpace(detail.LotNumber))
                        && (!detail.Item.TrackExpiry || detail.ExpiryDate.HasValue)));

                var dockToStockHours = completedVouchers
                    .Select(voucher => new
                    {
                        Start = voucher.DockArrivalAt ?? voucher.GateInAt ?? voucher.ReceivedAt,
                        End = voucher.CompletedAt
                    })
                    .Where(sample => sample.Start.HasValue && sample.End.HasValue && sample.End.Value >= sample.Start.Value)
                    .Select(sample => Math.Round((decimal)(sample.End!.Value - sample.Start!.Value).TotalHours, 2))
                    .ToList();

                var supplierAdjustments = adjustments
                    .Where(transaction => transaction.VoucherId.HasValue
                        && supplierVouchers.Any(voucher => voucher.VoucherId == transaction.VoucherId.Value))
                    .ToList();
                var visibleDetails = detailsByVoucher.Values.SelectMany(value => value).ToList();
                var dataQualityCodes = new List<string> { "DAMAGE_REASON_TAXONOMY_MISSING" };
                if (onTimeSamples.Count < supplierVouchers.Count)
                    dataQualityCodes.Add("APPOINTMENT_TIMESTAMP_MISSING");
                if (supplierInspectionGroups.Count == 0)
                    dataQualityCodes.Add("QC_SAMPLE_MISSING");
                if (dockToStockHours.Count == 0)
                    dataQualityCodes.Add("DOCK_TO_STOCK_MILESTONE_MISSING");

                return new SupplierInboundScorecardRow
                {
                    PartnerId = group.Key.PartnerId,
                    PartnerCode = group.Key.PartnerCode,
                    PartnerName = group.Key.PartnerName,
                    InboundVoucherCount = supplierVouchers.Count,
                    OnTimeSampleCount = onTimeSamples.Count,
                    OnTimeCount = onTimeCount,
                    OnTimePercent = Percent(onTimeCount, onTimeSamples.Count),
                    InFullSampleCount = inFullSamples.Count,
                    InFullCount = inFullCount,
                    InFullPercent = Percent(inFullCount, inFullSamples.Count),
                    QualitySampleCount = supplierInspectionGroups.Count,
                    QualityPassedCount = qualityPassedCount,
                    QualityPassPercent = Percent(qualityPassedCount, supplierInspectionGroups.Count),
                    DocumentSampleCount = documentSamples.Count,
                    DocumentAccurateCount = documentAccurateCount,
                    DocumentAccuracyPercent = Percent(documentAccurateCount, documentSamples.Count),
                    ReceivedBaseQty = visibleDetails.Sum(detail => detail.BaseQty),
                    DefectOrShortBaseQty = visibleDetails.Sum(detail => detail.DefectBaseQty),
                    DockToStockSampleCount = dockToStockHours.Count,
                    MedianDockToStockHours = AnalyticsPercentile(dockToStockHours, 0.50m),
                    AdjustmentTransactionCount = supplierAdjustments.Count,
                    AdjustmentAbsoluteBaseQty = supplierAdjustments.Sum(transaction => Math.Abs(transaction.QuantityDelta)),
                    DataQualityCodes = dataQualityCodes
                };
            })
            .OrderByDescending(row => row.InboundVoucherCount)
            .ThenBy(row => row.PartnerCode)
            .ToList();

        ViewBag.Warehouses = await _db.Warehouses.AsNoTracking()
            .Where(warehouse => warehouse.IsActive)
            .OrderBy(warehouse => warehouse.WarehouseCode)
            .ToListAsync();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.Days = days.Value;
        ViewBag.SupplierCount = rows.Count;
        ViewBag.VoucherCount = rows.Sum(row => row.InboundVoucherCount);
        ViewBag.OnTimeSampleCount = rows.Sum(row => row.OnTimeSampleCount);
        ViewBag.QcSampleCount = rows.Sum(row => row.QualitySampleCount);
        return View(rows);
    }


    // ═══════════════════════════════════════════════════════════════
    // Top hàng nhập / xuất nhiều nhất
    // ═══════════════════════════════════════════════════════════════
    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> TopItems(DateTime? dateFrom, DateTime? dateTo, string direction = "in", int top = 20, string sortBy = "qty")
    {
        var canSeeFinancial = CanSeeFinancial();
        var scopedWarehouseId = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        dateFrom ??= VietnamNow.Date.AddDays(-30);
        dateTo ??= VietnamNow.Date;
        top = Math.Clamp(top, 1, 500);
        direction = string.Equals(direction, "out", StringComparison.OrdinalIgnoreCase) ? "out" : "in";
        sortBy = canSeeFinancial && string.Equals(sortBy, "value", StringComparison.OrdinalIgnoreCase) ? "value" : "qty";
        var endDate = dateTo.Value.AddDays(1);

        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        ViewBag.Direction = direction;
        ViewBag.Top = top;
        ViewBag.SortBy = sortBy;
        ViewBag.CanSeeFinancial = canSeeFinancial;

        // Inbound = VoucherType 1,4,7 ; Outbound = 2,3,8
        VoucherTypeEnum[] types = direction == "out"
            ? new VoucherTypeEnum[] { VoucherTypeEnum.XuatKho, VoucherTypeEnum.TraNCC, VoucherTypeEnum.XuatSanXuat }
            : new VoucherTypeEnum[] { VoucherTypeEnum.NhapKho, VoucherTypeEnum.KhachTra, VoucherTypeEnum.NhapThanhPham };

        var query = _db.VoucherDetails.AsNoTracking()
            .Include(d => d.Voucher)
            .Include(d => d.Item).ThenInclude(i => i!.Category)
            .Include(d => d.Item).ThenInclude(i => i!.BaseUom)
            .Where(d => d.Voucher != null
                && !d.Voucher.IsCancelled
                && d.Voucher.IsPosted
                && types.Contains(d.Voucher.VoucherType)
                && d.Voucher.VoucherDate >= dateFrom.Value
                && d.Voucher.VoucherDate < endDate
                && d.Item != null);

        if (scopedWarehouseId.HasValue)
            query = query.Where(d => d.Voucher!.WarehouseId == scopedWarehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            query = query.Where(d =>
                (d.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(d.OwnerPartnerId.Value))
                || (d.Voucher!.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(d.Voucher.OwnerPartnerId.Value)));

        var grouped = await query
            .GroupBy(d => new
            {
                d.ItemId,
                ItemCode = d.Item!.ItemCode,
                ItemName = d.Item!.ItemName,
                CategoryName = d.Item!.Category != null ? d.Item.Category.CategoryName : "Chưa phân loại",
                UomCode = d.Item!.BaseUom != null ? d.Item.BaseUom.UomCode : ""
            })
            .Select(g => new TopItemRow
            {
                ItemId = g.Key.ItemId,
                ItemCode = g.Key.ItemCode,
                ItemName = g.Key.ItemName,
                CategoryName = g.Key.CategoryName,
                UomCode = g.Key.UomCode,
                TotalQty = g.Sum(d => d.BaseQty),
                TotalValue = g.Sum(d => d.LineAmount),
                VoucherCount = g.Select(d => d.VoucherId).Distinct().Count()
            })
            .ToListAsync();

        // Sort and take top N
        var data = sortBy == "value"
            ? grouped.OrderByDescending(x => x.TotalValue).Take(top).ToList()
            : grouped.OrderByDescending(x => x.TotalQty).Take(top).ToList();

        if (!canSeeFinancial)
            foreach (var row in data) row.TotalValue = 0m;

        return View(data);
    }


    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> ExportTopItems(DateTime? dateFrom, DateTime? dateTo, string direction = "in", int top = 50, string sortBy = "qty")
    {
        var canSeeFinancial = CanSeeFinancial();
        var scopedWarehouseId = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        dateFrom ??= VietnamNow.Date.AddDays(-30);
        dateTo ??= VietnamNow.Date;
        top = Math.Clamp(top, 1, 500);
        direction = string.Equals(direction, "out", StringComparison.OrdinalIgnoreCase) ? "out" : "in";
        sortBy = canSeeFinancial && string.Equals(sortBy, "value", StringComparison.OrdinalIgnoreCase) ? "value" : "qty";
        var endDate = dateTo.Value.AddDays(1);

        VoucherTypeEnum[] types = direction == "out"
            ? new VoucherTypeEnum[] { VoucherTypeEnum.XuatKho, VoucherTypeEnum.TraNCC, VoucherTypeEnum.XuatSanXuat }
            : new VoucherTypeEnum[] { VoucherTypeEnum.NhapKho, VoucherTypeEnum.KhachTra, VoucherTypeEnum.NhapThanhPham };

        var query = _db.VoucherDetails.AsNoTracking()
            .Include(d => d.Voucher)
            .Include(d => d.Item).ThenInclude(i => i!.Category)
            .Include(d => d.Item).ThenInclude(i => i!.BaseUom)
            .Where(d => d.Voucher != null
                && !d.Voucher.IsCancelled
                && d.Voucher.IsPosted
                && types.Contains(d.Voucher.VoucherType)
                && d.Voucher.VoucherDate >= dateFrom.Value
                && d.Voucher.VoucherDate < endDate
                && d.Item != null);

        if (scopedWarehouseId.HasValue)
            query = query.Where(d => d.Voucher!.WarehouseId == scopedWarehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            query = query.Where(d =>
                (d.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(d.OwnerPartnerId.Value))
                || (d.Voucher!.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(d.Voucher.OwnerPartnerId.Value)));

        var grouped = await query
            .GroupBy(d => new
            {
                d.ItemId,
                ItemCode = d.Item!.ItemCode,
                ItemName = d.Item!.ItemName,
                CategoryName = d.Item!.Category != null ? d.Item.Category.CategoryName : "Chưa phân loại",
                UomCode = d.Item!.BaseUom != null ? d.Item.BaseUom.UomCode : ""
            })
            .Select(g => new TopItemRow
            {
                ItemId = g.Key.ItemId,
                ItemCode = g.Key.ItemCode,
                ItemName = g.Key.ItemName,
                CategoryName = g.Key.CategoryName,
                UomCode = g.Key.UomCode,
                TotalQty = g.Sum(d => d.BaseQty),
                TotalValue = g.Sum(d => d.LineAmount),
                VoucherCount = g.Select(d => d.VoucherId).Distinct().Count()
            })
            .ToListAsync();

        var data = sortBy == "value"
            ? grouped.OrderByDescending(x => x.TotalValue).Take(top).ToList()
            : grouped.OrderByDescending(x => x.TotalQty).Take(top).ToList();

        var dirLabel = direction == "out" ? "Xuất" : "Nhập";

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add($"Top{dirLabel}");

        ws.Cell(1, 1).Value = $"Top {top} Hàng {dirLabel} Nhiều Nhất";
        ws.Cell(2, 1).Value = $"Từ {dateFrom:dd/MM/yyyy} đến {dateTo:dd/MM/yyyy}";

        var row = 4;
        ws.Cell(row, 1).Value = "#";
        ws.Cell(row, 2).Value = "Mã VT";
        ws.Cell(row, 3).Value = "Tên Vật Tư";
        ws.Cell(row, 4).Value = "Danh Mục";
        ws.Cell(row, 5).Value = "ĐVT";
        ws.Cell(row, 6).Value = "Tổng SL";
        ws.Cell(row, 7).Value = "Số Phiếu";
        if (canSeeFinancial) ws.Cell(row, 8).Value = "Tổng Tiền";

        ws.Range(row, 1, row, canSeeFinancial ? 8 : 7).Style.Font.Bold = true;
        ws.Range(row, 1, row, canSeeFinancial ? 8 : 7).Style.Fill.BackgroundColor = XLColor.AirForceBlue;
        ws.Range(row, 1, row, canSeeFinancial ? 8 : 7).Style.Font.FontColor = XLColor.White;

        var rank = 0;
        foreach (var item in data)
        {
            row++; rank++;
            ws.Cell(row, 1).Value = rank;
            ws.Cell(row, 2).Value = item.ItemCode;
            ws.Cell(row, 3).Value = item.ItemName;
            ws.Cell(row, 4).Value = item.CategoryName ?? "";
            ws.Cell(row, 5).Value = item.UomCode;
            ws.Cell(row, 6).Value = item.TotalQty;
            ws.Cell(row, 7).Value = item.VoucherCount;
            if (canSeeFinancial) ws.Cell(row, 8).Value = item.TotalValue;
        }

        ws.Column(6).Style.NumberFormat.Format = "#,##0.00";
        if (canSeeFinancial) ws.Column(8).Style.NumberFormat.Format = "#,##0";
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Top{dirLabel}_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.xlsx");
    }


    // ═══════════════════════════════════════════════════════════════
    // RPT-06: BÁO CÁO HÀNG SẮP HẾT HẠN (đặc tả 7.2)
    // ═══════════════════════════════════════════════════════════════
    [Authorize(Roles = WmsRoles.WarehouseReportRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> ExpiryReport(int? warehouseId)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var today = VietnamNow.Date;
        var d30 = today.AddDays(30);
        var d60 = today.AddDays(60);
        var d90 = today.AddDays(90);
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        var query = _db.ItemLocations.AsNoTracking()
            .Include(il => il.Item)
            .Include(il => il.Location).ThenInclude(l => l!.Zone)
            .Where(il => il.Quantity > 0
                && il.ExpiryDate.HasValue
                && il.ExpiryDate.Value <= d90
                && il.Location != null && il.Location.Zone != null);

        if (warehouseId.HasValue)
            query = query.Where(il => il.Location!.Zone!.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            query = query.Where(il => il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value));

        var summaryRows = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Expired = g.Count(il => il.ExpiryDate!.Value < today),
                Within30 = g.Count(il => il.ExpiryDate!.Value >= today && il.ExpiryDate.Value <= d30),
                Within60 = g.Count(il => il.ExpiryDate!.Value > d30 && il.ExpiryDate.Value <= d60),
                Within90 = g.Count(il => il.ExpiryDate!.Value > d60 && il.ExpiryDate.Value <= d90),
                TotalQty = g.Sum(il => il.Quantity)
            })
            .ToListAsync();
        var summary = summaryRows.FirstOrDefault() ?? new
        {
            Expired = 0,
            Within30 = 0,
            Within60 = 0,
            Within90 = 0,
            TotalQty = 0m
        };

        var data = await query
            .OrderBy(il => il.ExpiryDate)
            .ThenBy(il => il.Item!.ItemCode)
            .Select(il => new
            {
                il.ItemId,
                ItemCode = il.Item!.ItemCode,
                ItemName = il.Item.ItemName,
                LocationCode = il.Location!.LocationCode,
                ZoneName = il.Location.Zone!.ZoneName,
                WarehouseName = il.Location.Zone.Warehouse != null ? il.Location.Zone.Warehouse.WarehouseName : "",
                il.LotNumber,
                il.ExpiryDate,
                il.Quantity,
                DaysToExpiry = il.ExpiryDate.HasValue ? (int)(il.ExpiryDate.Value - today).TotalDays : 999
            })
            .Take(500)
            .ToListAsync();

        ViewBag.Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.Data = data;
        ViewBag.Summary = summary;
        ViewBag.Today = today;
        return View();
    }


    // ═══════════════════════════════════════════════════════════════
    // RPT-07: BÁO CÁO HÀNG CHẬM LUÂN CHUYỂN (đặc tả 7.2)
    // ═══════════════════════════════════════════════════════════════
    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> SlowMovingReport(int? warehouseId, int days = 90)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        days = Math.Clamp(days, 1, 3650);
        var cutoff = VietnamNow.Date.AddDays(-days);
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        var stockMap = await _inventoryBalanceService.GetStockByItemAsync(
            warehouseId,
            ownerPartnerIds: scopedOwnerIds.Count > 0 ? scopedOwnerIds : null);
        var itemIds = stockMap.Keys.ToList();
        var itemsWithStock = itemIds.Count == 0
            ? new List<Item>()
            : await _db.Items.AsNoTracking()
                .Include(i => i.Category).Include(i => i.BaseUom)
                .Where(i => i.IsActive && itemIds.Contains(i.ItemId))
                .OrderBy(i => i.ItemCode)
                .ToListAsync();

        var itemIdsWithStock = itemsWithStock.Select(i => i.ItemId).ToList();
        var movementQuery = _db.InventoryTransactions.AsNoTracking()
            .Where(t => itemIdsWithStock.Contains(t.ItemId));
        if (warehouseId.HasValue)
            movementQuery = movementQuery.Where(t => t.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            movementQuery = movementQuery.Where(t => t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value));

        var lastReceiptDates = itemIdsWithStock.Count == 0
            ? new Dictionary<int, DateTime>()
            : await movementQuery
                .Where(t => t.TransactionType == InventoryTransactionTypeEnum.Receive && t.QuantityDelta > 0)
                .GroupBy(t => t.ItemId)
                .Select(g => new { ItemId = g.Key, LastDate = g.Max(t => t.TransactionAt) })
                .ToDictionaryAsync(x => x.ItemId, x => x.LastDate);

        var lastOutboundDates = itemIdsWithStock.Count == 0
            ? new Dictionary<int, DateTime>()
            : await movementQuery
                .Where(t => t.QuantityDelta < 0
                    && (t.TransactionType == InventoryTransactionTypeEnum.Ship
                        || t.TransactionType == InventoryTransactionTypeEnum.TransferOut
                        || t.TransactionType == InventoryTransactionTypeEnum.KitConsume
                        || t.TransactionType == InventoryTransactionTypeEnum.VasConsume))
                .GroupBy(t => t.ItemId)
                .Select(g => new { ItemId = g.Key, LastDate = g.Max(t => t.TransactionAt) })
                .ToDictionaryAsync(x => x.ItemId, x => x.LastDate);

        var slowItems = itemsWithStock
            .Where(i =>
            {
                if (!lastOutboundDates.TryGetValue(i.ItemId, out var lastOutbound))
                    return true;
                return lastOutbound < cutoff;
            })
            .Select(i => new SlowMovingItemRow
            {
                ItemId = i.ItemId,
                ItemCode = i.ItemCode,
                ItemName = i.ItemName,
                CategoryName = i.Category?.CategoryName ?? "—",
                UomCode = i.BaseUom?.UomCode ?? "—",
                CurrentStock = stockMap.TryGetValue(i.ItemId, out var qty) ? qty : 0m,
                StockValue = (stockMap.TryGetValue(i.ItemId, out var stockQty) ? stockQty : 0m) * i.UnitCost,
                LastReceiptDate = lastReceiptDates.TryGetValue(i.ItemId, out var receiptDate) ? receiptDate : null,
                LastOutboundDate = lastOutboundDates.TryGetValue(i.ItemId, out var outboundDate) ? outboundDate : null,
                DaysSinceLastOutbound = lastOutboundDates.TryGetValue(i.ItemId, out var lastOutbound)
                    ? Math.Max(0, (int)(VietnamNow.Date - lastOutbound.Date).TotalDays)
                    : null
            })
            .OrderByDescending(x => x.DaysSinceLastOutbound ?? int.MaxValue)
            .ToList();

        ViewBag.Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.Days = days;
        ViewBag.Data = slowItems;
        ViewBag.CanSeeFinancial = CanSeeFinancial();
        return View();
    }


    // ═══════════════════════════════════════════════════════════════
    // RPT-12: ABC theo giá trị tồn hiện tại. Đây không phải ABC theo giá trị sử dụng/tốc độ xuất.
    // ═══════════════════════════════════════════════════════════════
    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> AbcAnalysis(int? warehouseId)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        var canSeeFinancial = CanSeeFinancial();
        var stockMap = await _inventoryBalanceService.GetStockByItemAsync(
            warehouseId,
            ownerPartnerIds: scopedOwnerIds.Count > 0 ? scopedOwnerIds : null);
        var stockedItemIds = stockMap.Where(row => row.Value > 0m).Select(row => row.Key).ToList();
        var itemRows = await _db.Items.AsNoTracking()
            .Include(i => i.Category).Include(i => i.BaseUom)
            .Where(i => i.IsActive && stockedItemIds.Contains(i.ItemId))
            .ToListAsync();
        _inventoryBalanceService.ApplyStockBalances(itemRows, stockMap);

        var items = itemRows
            .Where(i => stockMap.TryGetValue(i.ItemId, out var scopedQty) && scopedQty > 0)
            .OrderByDescending(i => i.TotalStockValue)
            .Select(i => new
            {
                i.ItemId,
                i.ItemCode,
                i.ItemName,
                CategoryName = i.Category != null ? i.Category.CategoryName : "—",
                UomCode = i.BaseUom != null ? i.BaseUom.UomCode : "—",
                i.CurrentStock,
                i.UnitCost,
                i.TotalStockValue
            })
            .ToList();

        var totalValue = items.Sum(i => i.TotalStockValue);
        var hasValuationData = totalValue > 0m;

        var results = new List<AbcInventoryValueRow>();
        decimal cumulative = 0;
        int rank = 0;
        foreach (var item in items)
        {
            rank++;
            var cumulativeBeforePct = hasValuationData ? cumulative / totalValue * 100m : (decimal?)null;
            cumulative += item.TotalStockValue;
            decimal? cumulativePct = hasValuationData ? cumulative / totalValue * 100m : null;
            string abcClass;
            if (!cumulativePct.HasValue || item.TotalStockValue <= 0m) abcClass = "N";
            else if (cumulativeBeforePct < 80m) abcClass = "A";
            else if (cumulativeBeforePct < 95m) abcClass = "B";
            else abcClass = "C";

            results.Add(new AbcInventoryValueRow
            {
                Rank = rank,
                ItemCode = item.ItemCode,
                ItemName = item.ItemName,
                CategoryName = item.CategoryName,
                UomCode = item.UomCode,
                CurrentStock = item.CurrentStock,
                UnitCost = item.UnitCost,
                TotalStockValue = item.TotalStockValue,
                CumulativePct = cumulativePct,
                AbcClass = abcClass
            });
        }

        var countA = results.Count(r => r.AbcClass == "A");
        var countB = results.Count(r => r.AbcClass == "B");
        var countC = results.Count(r => r.AbcClass == "C");
        var valueA = results.Where(r => r.AbcClass == "A").Sum(r => r.TotalStockValue);
        var valueB = results.Where(r => r.AbcClass == "B").Sum(r => r.TotalStockValue);
        var valueC = results.Where(r => r.AbcClass == "C").Sum(r => r.TotalStockValue);

        ViewBag.Data = results;
        ViewBag.TotalValue = totalValue;
        ViewBag.CountA = countA; ViewBag.CountB = countB; ViewBag.CountC = countC;
        ViewBag.ValueA = valueA; ViewBag.ValueB = valueB; ViewBag.ValueC = valueC;
        ViewBag.MissingValuationCount = results.Count(r => r.AbcClass == "N");
        ViewBag.HasValuationData = hasValuationData;
        ViewBag.CanSeeFinancial = canSeeFinancial;
        ViewBag.Warehouses = await _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync();
        ViewBag.WarehouseId = warehouseId;
        return View();
    }


    // ═══════════════════════════════════════════════════════════════
    // ENTERPRISE: Analytics Dashboard (BI)
    // ═══════════════════════════════════════════════════════════════
    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> Analytics(int? warehouseId, int? days)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        days = Math.Clamp(days ?? 30, 1, 180);
        var fromDate = VietnamNow.Date.AddDays(-(days.Value - 1));
        var toDate = VietnamNow.Date;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        var canSeeFinancial = CanSeeFinancial();

        ViewBag.Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.Days = days.Value;

        var vq = _db.Vouchers.AsNoTracking().Where(v => !v.IsCancelled && v.IsPosted && v.VoucherDate >= fromDate && v.VoucherDate <= toDate);
        if (warehouseId.HasValue) vq = vq.Where(v => v.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            vq = vq.Where(v => v.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(v.OwnerPartnerId.Value));

        // Throughput by day
        var dailyThroughput = await vq
            .GroupBy(v => new { v.VoucherDate, v.VoucherType })
            .Select(g => new { g.Key.VoucherDate, g.Key.VoucherType, Count = g.Count(), Lines = g.Sum(v => v.TotalLines) })
            .ToListAsync();

        var dates = Enumerable.Range(0, days.Value).Select(i => fromDate.AddDays(i)).ToList();
        ViewBag.ChartDates = dates.Select(d => d.ToString("dd/MM")).ToList();
        ViewBag.InboundByDay = dates.Select(d => dailyThroughput.Where(t => t.VoucherDate == d && (t.VoucherType == VoucherTypeEnum.NhapKho || t.VoucherType == VoucherTypeEnum.KhachTra || t.VoucherType == VoucherTypeEnum.NhapThanhPham)).Sum(t => t.Count)).ToList();
        ViewBag.OutboundByDay = dates.Select(d => dailyThroughput.Where(t => t.VoucherDate == d && (t.VoucherType == VoucherTypeEnum.XuatKho || t.VoucherType == VoucherTypeEnum.TraNCC || t.VoucherType == VoucherTypeEnum.XuatSanXuat)).Sum(t => t.Count)).ToList();
        ViewBag.LinesByDay = dates.Select(d => dailyThroughput.Where(t => t.VoucherDate == d).Sum(t => t.Lines)).ToList();

        // Summary KPIs
        var totalInbound = await vq.CountAsync(v => v.VoucherType == VoucherTypeEnum.NhapKho || v.VoucherType == VoucherTypeEnum.KhachTra || v.VoucherType == VoucherTypeEnum.NhapThanhPham);
        var totalOutbound = await vq.CountAsync(v => v.VoucherType == VoucherTypeEnum.XuatKho || v.VoucherType == VoucherTypeEnum.TraNCC || v.VoucherType == VoucherTypeEnum.XuatSanXuat);
        var totalLines = await vq.SumAsync(v => v.TotalLines);
        var totalValue = canSeeFinancial ? await vq.SumAsync(v => v.TotalAmount) : (decimal?)null;
        ViewBag.TotalInbound = totalInbound;
        ViewBag.TotalOutbound = totalOutbound;
        ViewBag.TotalLines = totalLines;
        ViewBag.TotalValue = totalValue;
        ViewBag.CanSeeFinancial = canSeeFinancial;

        // Days of supply must be calculated per item in base UOM. Summing voucher counts
        // or dividing quantities from different UOMs produces a dimensionally invalid KPI.
        var inventoryQuery = _db.ItemLocations.AsNoTracking()
            .Where(il => il.HoldStatus == InventoryHoldStatusEnum.Available
                || il.HoldStatus == InventoryHoldStatusEnum.Consigned);
        if (warehouseId.HasValue)
            inventoryQuery = inventoryQuery.Where(il => il.Location != null
                && il.Location.Zone != null
                && il.Location.Zone.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            inventoryQuery = inventoryQuery.Where(il => il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value));

        var availableRows = await inventoryQuery
            .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.OwnerPartnerId,
                AvailableQty = g.Sum(il => il.Quantity - il.ReservedQty)
            })
            .ToListAsync();
        var availableByItemOwner = availableRows.ToDictionary(
            row => (row.ItemId, row.OwnerPartnerId),
            row => Math.Max(0m, row.AvailableQty));

        var analyticsNow = VietnamNow;
        var sevenDayStart = analyticsNow.AddDays(-7);
        var thirtyDayStart = analyticsNow.AddDays(-30);
        var ninetyDayStart = analyticsNow.AddDays(-90);
        var ledgerStart = fromDate < ninetyDayStart ? fromDate : ninetyDayStart;
        var outboundLedgerQuery = _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.TransactionAt >= ledgerStart
                && t.TransactionAt <= analyticsNow
                && t.QuantityDelta < 0m
                && (t.TransactionType == InventoryTransactionTypeEnum.Ship
                    || t.TransactionType == InventoryTransactionTypeEnum.TransferOut
                    || t.TransactionType == InventoryTransactionTypeEnum.KitConsume
                    || t.TransactionType == InventoryTransactionTypeEnum.VasConsume));
        if (warehouseId.HasValue)
            outboundLedgerQuery = outboundLedgerQuery.Where(t => t.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            outboundLedgerQuery = outboundLedgerQuery.Where(t => t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value));

        var outboundRows = await outboundLedgerQuery
            .GroupBy(t => new { t.ItemId, t.OwnerPartnerId })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.OwnerPartnerId,
                OutboundQty = -g.Sum(t => t.TransactionAt >= fromDate ? t.QuantityDelta : 0m),
                Outbound7DayQty = -g.Sum(t => t.TransactionAt >= sevenDayStart ? t.QuantityDelta : 0m),
                Outbound30DayQty = -g.Sum(t => t.TransactionAt >= thirtyDayStart ? t.QuantityDelta : 0m),
                Outbound90DayQty = -g.Sum(t => t.TransactionAt >= ninetyDayStart ? t.QuantityDelta : 0m),
                DemandActiveDayCount90 = g
                    .Where(t => t.TransactionAt >= ninetyDayStart)
                    .Select(t => t.TransactionAt.Date)
                    .Distinct()
                    .Count()
            })
            .Where(row => row.OutboundQty > 0m)
            .ToListAsync();
        var outboundByItemOwner = outboundRows.ToDictionary(
            row => (row.ItemId, row.OwnerPartnerId),
            row => row);

        var demandItemIds = outboundByItemOwner.Keys.Select(key => key.ItemId).Distinct().ToList();
        var demandItems = demandItemIds.Count == 0
            ? new Dictionary<int, (string Code, string Name, string Uom)>()
            : await _db.Items.AsNoTracking()
                .Where(item => demandItemIds.Contains(item.ItemId))
                .Select(item => new
                {
                    item.ItemId,
                    item.ItemCode,
                    item.ItemName,
                    UomCode = item.BaseUom != null ? item.BaseUom.UomCode : "—"
                })
                .ToDictionaryAsync(item => item.ItemId, item =>
                    (Code: item.ItemCode, Name: item.ItemName, Uom: item.UomCode));
        var ownerIds = outboundByItemOwner.Keys
            .Where(key => key.OwnerPartnerId.HasValue)
            .Select(key => key.OwnerPartnerId!.Value)
            .Distinct()
            .ToList();
        var ownerNames = ownerIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Partners.AsNoTracking()
                .Where(partner => ownerIds.Contains(partner.PartnerId))
                .ToDictionaryAsync(partner => partner.PartnerId, partner => partner.PartnerName);

        // Replenishment risk is only valid when the item-owner pair has enough recent
        // demand observations and one unambiguous configured supplier lead time.
        var supplierHistoryStart = analyticsNow.Date.AddDays(-365);
        var supplierLeadTimeQuery = _db.VoucherDetails.AsNoTracking()
            .Where(detail => detail.Voucher != null
                && detail.Voucher.VoucherType == VoucherTypeEnum.NhapKho
                && detail.Voucher.IsPosted
                && !detail.Voucher.IsCancelled
                && detail.Voucher.VoucherDate >= supplierHistoryStart
                && detail.Voucher.PartnerId.HasValue
                && detail.Voucher.Partner != null
                && detail.Voucher.Partner.IsActive
                && detail.Voucher.Partner.LeadTimeDays.HasValue
                && detail.Voucher.Partner.LeadTimeDays.Value > 0);
        if (warehouseId.HasValue)
            supplierLeadTimeQuery = supplierLeadTimeQuery.Where(detail => detail.Voucher!.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
        {
            supplierLeadTimeQuery = supplierLeadTimeQuery.Where(detail => scopedOwnerIds.Contains(
                detail.OwnerPartnerId ?? detail.Voucher!.OwnerPartnerId ?? -1));
        }

        var supplierLeadTimeRows = await supplierLeadTimeQuery
            .Select(detail => new
            {
                detail.ItemId,
                OwnerPartnerId = detail.OwnerPartnerId ?? detail.Voucher!.OwnerPartnerId,
                PartnerId = detail.Voucher!.PartnerId!.Value,
                LeadTimeDays = detail.Voucher.Partner!.LeadTimeDays!.Value
            })
            .Distinct()
            .ToListAsync();
        var supplierLeadTimeProfiles = supplierLeadTimeRows
            .GroupBy(row => (row.ItemId, row.OwnerPartnerId))
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    SupplierCount = group.Select(row => row.PartnerId).Distinct().Count(),
                    LeadTimes = group.Select(row => row.LeadTimeDays).Distinct().OrderBy(value => value).ToList()
                });

        var daysOfSupplyRows = outboundByItemOwner
            .Where(row => demandItems.ContainsKey(row.Key.ItemId))
            .Select(row =>
            {
                const int minimumDemandActiveDays = 4;
                var averageDailyOutbound = row.Value.OutboundQty / days.Value;
                var availableQty = availableByItemOwner.GetValueOrDefault(row.Key);
                var item = demandItems[row.Key.ItemId];
                var velocity7 = row.Value.Outbound7DayQty / 7m;
                var velocity30 = row.Value.Outbound30DayQty / 30m;
                var velocity90 = row.Value.Outbound90DayQty / 90m;
                var riskDaysOfSupply = velocity30 > 0m ? availableQty / velocity30 : (decimal?)null;
                supplierLeadTimeProfiles.TryGetValue(row.Key, out var leadTimeProfile);
                var leadTimeDays = leadTimeProfile?.LeadTimes.Count == 1
                    ? leadTimeProfile.LeadTimes[0]
                    : (int?)null;
                var dataQualityCode = row.Value.DemandActiveDayCount90 < minimumDemandActiveDays
                    || row.Value.Outbound30DayQty <= 0m
                        ? "DEMAND_SAMPLE_INSUFFICIENT"
                        : leadTimeProfile == null || leadTimeProfile.LeadTimes.Count == 0
                            ? "LEAD_TIME_DATA_MISSING"
                            : leadTimeProfile.LeadTimes.Count > 1
                                ? "LEAD_TIME_CONFLICT"
                                : "READY";
                var isRiskEligible = dataQualityCode == "READY" && riskDaysOfSupply.HasValue && leadTimeDays.HasValue;
                return new DaysOfSupplyItemRow
                {
                    ItemId = row.Key.ItemId,
                    OwnerPartnerId = row.Key.OwnerPartnerId,
                    OwnerPartnerName = row.Key.OwnerPartnerId.HasValue
                        ? ownerNames.GetValueOrDefault(row.Key.OwnerPartnerId.Value, $"Chủ hàng #{row.Key.OwnerPartnerId.Value}")
                        : "Nội bộ / chưa gán",
                    ItemCode = item.Code,
                    ItemName = item.Name,
                    UomCode = item.Uom,
                    AvailableBaseQty = availableQty,
                    OutboundBaseQty = row.Value.OutboundQty,
                    AverageDailyOutboundBaseQty = averageDailyOutbound,
                    DaysOfSupply = averageDailyOutbound > 0m ? availableQty / averageDailyOutbound : 0m,
                    Outbound7DayBaseQty = row.Value.Outbound7DayQty,
                    Outbound30DayBaseQty = row.Value.Outbound30DayQty,
                    Outbound90DayBaseQty = row.Value.Outbound90DayQty,
                    Velocity7DayBaseQty = velocity7,
                    Velocity30DayBaseQty = velocity30,
                    Velocity90DayBaseQty = velocity90,
                    DemandActiveDayCount90 = row.Value.DemandActiveDayCount90,
                    LeadTimeDays = leadTimeDays,
                    SupplierSampleCount = leadTimeProfile?.SupplierCount ?? 0,
                    RiskDaysOfSupply = riskDaysOfSupply,
                    DataQualityCode = dataQualityCode,
                    IsRiskEligible = isRiskEligible,
                    IsReplenishmentRisk = isRiskEligible && riskDaysOfSupply!.Value <= leadTimeDays!.Value
                };
            })
            .OrderByDescending(row => row.IsReplenishmentRisk)
            .ThenBy(row => row.RiskDaysOfSupply ?? decimal.MaxValue)
            .ToList();

        ViewBag.StockKeepingUnitCount = availableByItemOwner
            .Where(row => row.Value > 0m)
            .Select(row => row.Key.ItemId)
            .Distinct()
            .Count();
        ViewBag.DaysOfSupply = AnalyticsPercentile(daysOfSupplyRows.Select(row => row.DaysOfSupply), 0.50m);
        ViewBag.DaysOfSupplySampleCount = daysOfSupplyRows.Count;
        ViewBag.ReplenishmentEligibleCount = daysOfSupplyRows.Count(row => row.IsRiskEligible);
        ViewBag.ReplenishmentRiskCount = daysOfSupplyRows.Count(row => row.IsReplenishmentRisk);
        ViewBag.DaysOfSupplyRows = daysOfSupplyRows;

        // QC summary
        var qcQuery = _db.QualityInspections.AsNoTracking()
            .Where(qi => qi.CreatedAt >= fromDate && qi.CreatedAt <= VietnamNow);
        if (warehouseId.HasValue)
            qcQuery = qcQuery.Where(qi => qi.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
        {
            qcQuery = qcQuery.Where(qi => scopedOwnerIds.Contains(
                (qi.VoucherDetail != null ? qi.VoucherDetail.OwnerPartnerId : null)
                ?? (qi.Voucher != null ? qi.Voucher.OwnerPartnerId : null)
                ?? (qi.Item != null ? qi.Item.OwnerPartnerId : null)
                ?? -1));
        }
        var qcInspections = await qcQuery.ToListAsync();
        ViewBag.QcTotal = qcInspections.Count;
        ViewBag.QcPassed = qcInspections.Count(qi => qi.OverallResult == QualityStatusEnum.Passed);
        ViewBag.QcFailed = qcInspections.Count(qi => qi.OverallResult == QualityStatusEnum.Failed);

        return View();
    }


    // ═══════════════════════════════════════════════════════════════
    // ENTERPRISE: Space Utilization
    // ═══════════════════════════════════════════════════════════════
    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> SpaceUtilization(int? warehouseId)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        ViewBag.Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync();
        ViewBag.WarehouseId = warehouseId;

        var lq = _db.Locations.AsNoTracking()
            .Include(l => l.Zone).ThenInclude(z => z!.Warehouse)
            .Where(l => l.IsActive && l.Zone != null && l.Zone.IsActive);
        if (warehouseId.HasValue) lq = lq.Where(l => l.Zone!.WarehouseId == warehouseId.Value);

        var locations = await lq.OrderBy(l => l.Zone!.ZoneCode).ThenBy(l => l.LocationCode).ToListAsync();
        var locationIds = locations.Select(l => l.LocationId).ToList();

        var stockQuery = _db.ItemLocations.AsNoTracking()
            .Include(il => il.Item)
            .Where(il => locationIds.Contains(il.LocationId) && il.Quantity > 0m);
        if (scopedOwnerIds.Count > 0)
            stockQuery = stockQuery.Where(il => il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value));
        var stockRows = locationIds.Count > 0 ? await stockQuery.ToListAsync() : new List<ItemLocation>();
        var stockByLocation = stockRows.GroupBy(row => row.LocationId).ToDictionary(group => group.Key, group => group.ToList());

        var rows = locations.Select(l =>
        {
            var locationStock = stockByLocation.GetValueOrDefault(l.LocationId) ?? new List<ItemLocation>();
            var hasWeightCapacity = l.MaxWeightCapacityKg.HasValue && l.MaxWeightCapacityKg.Value > 0m;
            var hasWeightMasterData = locationStock.All(row => row.Item?.Weight is > 0m);
            var canMeasureCapacity = hasWeightCapacity && hasWeightMasterData;
            var currentLoad = canMeasureCapacity
                ? locationStock.Sum(row => row.Quantity * row.Item!.Weight!.Value)
                : (decimal?)null;
            var maxCapacity = canMeasureCapacity ? l.MaxWeightCapacityKg : null;
            var usedPercent = currentLoad.HasValue && maxCapacity is > 0m
                ? Math.Round(currentLoad.Value / maxCapacity.Value * 100m, 1)
                : (decimal?)null;
            var isOccupied = locationStock.Count > 0;
            var status = !usedPercent.HasValue
                ? "capacity-missing"
                : usedPercent.Value >= 100m ? "critical"
                : usedPercent.Value >= 70m ? "warning"
                : isOccupied ? "ok" : "empty";
            return new SpaceUtilizationRow
            {
                LocationId = l.LocationId,
                LocationCode = l.LocationCode,
                ZoneCode = l.Zone?.ZoneCode ?? "",
                ZoneName = l.Zone?.ZoneName ?? "",
                WarehouseName = l.Zone?.Warehouse?.WarehouseName ?? "",
                IsOccupied = isOccupied,
                CurrentLoad = currentLoad,
                MaxCapacity = maxCapacity,
                UsedPercent = usedPercent,
                CapacityUnit = canMeasureCapacity ? "kg" : null,
                ItemCount = locationStock.Select(row => row.ItemId).Distinct().Count(),
                Status = status,
                DataQualityCode = canMeasureCapacity ? "CAPACITY_OK" : "CAPACITY_DATA_MISSING"
            };
        }).ToList();

        var measurableRows = rows.Where(row => row.UsedPercent.HasValue).ToList();
        ViewBag.Rows = rows;
        ViewBag.TotalLocations = rows.Count;
        ViewBag.OccupiedLocations = rows.Count(r => r.IsOccupied);
        ViewBag.OccupancyRate = rows.Count > 0 ? Math.Round(rows.Count(r => r.IsOccupied) * 100m / rows.Count, 1) : 0m;
        ViewBag.AvgUtilization = AnalyticsPercentile(measurableRows.Select(row => row.UsedPercent!.Value), 0.50m);
        ViewBag.CapacitySampleCount = measurableRows.Count;
        ViewBag.MissingCapacityCount = rows.Count - measurableRows.Count;
        ViewBag.CriticalCount = rows.Count(r => r.Status == "critical");

        return View();
    }


    // ═══════════════════════════════════════════════════════════════
    // ENTERPRISE: Dock-to-Stock Time
    // ═══════════════════════════════════════════════════════════════
    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> DockToStock(int? warehouseId, int? days)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        days = Math.Clamp(days ?? 30, 1, 365);
        var fromDate = VietnamNow.Date.AddDays(-(days.Value - 1));
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        ViewBag.Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.Days = days.Value;

        var q = _db.Vouchers.AsNoTracking()
            .Include(v => v.Warehouse).Include(v => v.Partner)
            .Where(v => !v.IsCancelled
                && v.IsPosted
                && (v.VoucherType == VoucherTypeEnum.NhapKho || v.VoucherType == VoucherTypeEnum.KhachTra || v.VoucherType == VoucherTypeEnum.NhapThanhPham)
                && v.VoucherDate >= fromDate);
        if (warehouseId.HasValue) q = q.Where(v => v.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            q = q.Where(v => v.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(v.OwnerPartnerId.Value));

        var vouchers = await q.OrderByDescending(v => v.CreatedAt).ToListAsync();

        var rows = vouchers.Select(v =>
        {
            var dockArrival = v.DockArrivalAt ?? v.GateInAt;
            var receiveStart = v.ReceivedAt ?? v.UnloadStartAt;
            var completed = v.CompletedAt;
            var missing = new List<string>();
            if (!dockArrival.HasValue) missing.Add("Giờ đến thực tế");
            if (!receiveStart.HasValue) missing.Add("Bắt đầu nhận");
            if (!completed.HasValue) missing.Add("Hoàn tất nhập kho");
            var ordered = dockArrival.HasValue && receiveStart.HasValue && completed.HasValue
                && dockArrival.Value <= receiveStart.Value
                && receiveStart.Value <= completed.Value;
            if (missing.Count == 0 && !ordered)
                missing.Add("Thứ tự mốc thời gian không hợp lệ");

            decimal? dockToReceiveHours = ordered
                ? Math.Round((decimal)(receiveStart!.Value - dockArrival!.Value).TotalHours, 2)
                : null;
            decimal? receiveToStockHours = ordered
                ? Math.Round((decimal)(completed!.Value - receiveStart!.Value).TotalHours, 2)
                : null;
            decimal? totalHours = ordered
                ? Math.Round((decimal)(completed!.Value - dockArrival!.Value).TotalHours, 2)
                : null;
            return new DockToStockRow
            {
                VoucherId = v.VoucherId,
                VoucherCode = v.VoucherCode,
                WarehouseName = v.Warehouse?.WarehouseName ?? "",
                PartnerName = v.Partner?.PartnerName ?? "Chưa có",
                DockArrival = dockArrival,
                ReceiveStart = receiveStart,
                Completed = completed,
                DockToReceiveHours = dockToReceiveHours,
                ReceiveToStockHours = receiveToStockHours,
                TotalHours = totalHours,
                Sla = !totalHours.HasValue ? "missing" : totalHours.Value <= 4m ? "good" : totalHours.Value <= 8m ? "warning" : "critical",
                MissingMilestones = string.Join(", ", missing)
            };
        }).ToList();

        var validRows = rows.Where(row => row.TotalHours.HasValue).ToList();
        ViewBag.Rows = rows.Take(200).ToList();
        ViewBag.SampleCount = validRows.Count;
        ViewBag.MissingMilestoneCount = rows.Count - validRows.Count;
        ViewBag.MedianDockToReceive = AnalyticsPercentile(validRows.Select(row => row.DockToReceiveHours!.Value), 0.50m);
        ViewBag.MedianReceiveToStock = AnalyticsPercentile(validRows.Select(row => row.ReceiveToStockHours!.Value), 0.50m);
        ViewBag.MedianTotal = AnalyticsPercentile(validRows.Select(row => row.TotalHours!.Value), 0.50m);
        ViewBag.P90Total = AnalyticsPercentile(validRows.Select(row => row.TotalHours!.Value), 0.90m);
        ViewBag.P95Total = AnalyticsPercentile(validRows.Select(row => row.TotalHours!.Value), 0.95m);
        ViewBag.GoodCount = rows.Count(r => r.Sla == "good");
        ViewBag.WarningCount = rows.Count(r => r.Sla == "warning");
        ViewBag.CriticalCount = rows.Count(r => r.Sla == "critical");

        return View();
    }

    private static decimal? AnalyticsPercentile(IEnumerable<decimal> source, decimal percentile)
    {
        var values = source.OrderBy(value => value).ToList();
        if (values.Count == 0)
            return null;

        var bounded = Math.Clamp(percentile, 0m, 1m);
        var position = (values.Count - 1) * bounded;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return Math.Round(values[lower], 2);

        var fraction = position - lower;
        return Math.Round(values[lower] + (values[upper] - values[lower]) * fraction, 2);
    }

}
