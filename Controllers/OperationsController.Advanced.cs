using System;

using System.Collections.Generic;

using System.Data;

using System.IO;

using System.Linq;

using System.Text.Json;

using System.Threading.Tasks;

using System.Linq.Expressions;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using WMS.Common;

using WMS.Data;

using WMS.Models;

using WMS.Services;

using WMS.ViewModels;

namespace WMS.Controllers;

public partial class OperationsController
{

    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> LaborProductivity(int? warehouseId, int days = 7)
    {
        return View(await BuildLaborProductivityModelAsync(warehouseId, days));
    }


    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> CrossDockOpportunities(int? warehouseId)
    {
        int? scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue)
        {
            warehouseId = scopedWh.Value;
        }
        var allowedOwnerIds = await _tenantScopeService.GetAllowedOwnerIdsAsync(base.User);
        DateTime today = VietnamNow.Date;
        var inboundItems = await (from d in _db.VoucherDetails.AsNoTracking().Include((VoucherDetail d) => d.Voucher).Include((VoucherDetail d) => d.Item)
                                  where d.Voucher != null && d.Voucher.VoucherType == VoucherTypeEnum.NhapKho && !d.Voucher.IsPosted && !d.Voucher.IsCancelled && (d.Voucher.InboundStatus == InboundStatusEnum.Approved || d.Voucher.InboundStatus == InboundStatusEnum.Receiving) && ((d.Voucher.ReceivedAt.HasValue && d.Voucher.ReceivedAt.Value.Date == today) || (d.Voucher.ExpectedArrivalAt.HasValue && d.Voucher.ExpectedArrivalAt.Value.Date == today) || d.Voucher.VoucherDate == today) && d.Item != null && d.Item.IsActive && (!((int?)warehouseId).HasValue || d.Voucher.WarehouseId == ((int?)warehouseId).Value) && (allowedOwnerIds.Count == 0 || ((d.OwnerPartnerId ?? d.Voucher.OwnerPartnerId).HasValue && allowedOwnerIds.Contains((d.OwnerPartnerId ?? d.Voucher.OwnerPartnerId)!.Value)))
                                  select new
                                  {
                                      VoucherDetailId = d.VoucherDetailId,
                                      ItemId = d.ItemId,
                                      ItemCode = d.Item.ItemCode,
                                      ItemName = d.Item.ItemName,
                                      InboundQty = d.BaseQty - ((d.DefectBaseQty > 0m) ? d.DefectBaseQty : (d.DefectQty * ((d.ConversionRate == 0m) ? 1m : Math.Abs(d.ConversionRate)))),
                                      LotNumber = d.LotNumber,
                                      ExpiryDate = d.ExpiryDate,
                                      InboundVoucherCode = d.Voucher.VoucherCode,
                                      InboundVoucherId = d.VoucherId,
                                      WarehouseId = d.Voucher.WarehouseId,
                                      OwnerPartnerId = d.OwnerPartnerId ?? d.Voucher.OwnerPartnerId
                                  }).ToListAsync();
        List<long> inboundDetailIds = inboundItems.Select(i => i.VoucherDetailId).ToList();
        Dictionary<long, decimal> dictionary = ((inboundDetailIds.Count != 0) ? (await (from t in _db.CrossDockTasks.AsNoTracking()
                                                                                        where t.InboundVoucherDetailId.HasValue && inboundDetailIds.Contains(t.InboundVoucherDetailId.Value) && t.Status != CrossDockTaskStatusEnum.Cancelled
                                                                                        group t by t.InboundVoucherDetailId.GetValueOrDefault() into g
                                                                                        select new
                                                                                        {
                                                                                            DetailId = g.Key,
                                                                                            Qty = g.Sum((CrossDockTask t) => t.ScheduledQty)
                                                                                        }).ToDictionaryAsync(x => x.DetailId, x => x.Qty)) : new Dictionary<long, decimal>());
        Dictionary<long, decimal> matchedInboundQty = dictionary;
        var outboundDemand = await (from d in _db.VoucherDetails.AsNoTracking().Include((VoucherDetail d) => d.Voucher)
                                    where d.Voucher != null && d.Voucher.VoucherType == VoucherTypeEnum.XuatKho && !d.Voucher.IsPosted && !d.Voucher.IsCancelled && (int)d.Voucher.FulfillmentStatus < 2 && (!((int?)warehouseId).HasValue || d.Voucher.WarehouseId == ((int?)warehouseId).Value) && (allowedOwnerIds.Count == 0 || ((d.OwnerPartnerId ?? d.Voucher.OwnerPartnerId).HasValue && allowedOwnerIds.Contains((d.OwnerPartnerId ?? d.Voucher.OwnerPartnerId)!.Value)))
                                    select new
                                    {
                                        VoucherDetailId = d.VoucherDetailId,
                                        ItemId = d.ItemId,
                                        OutboundQty = Math.Abs(d.BaseQty),
                                        LotNumber = d.LotNumber,
                                        ExpiryDate = d.ExpiryDate,
                                        OutboundVoucherCode = d.Voucher.VoucherCode,
                                        OutboundVoucherId = d.VoucherId,
                                        OwnerPartnerId = d.OwnerPartnerId ?? d.Voucher.OwnerPartnerId,
                                        ServiceLevel = d.Voucher.ServiceLevel,
                                        Priority = d.Voucher.Priority,
                                        RequestedDeliveryDate = d.Voucher.RequestedDeliveryDate
                                    }).ToListAsync();
        List<long> outboundDetailIds = outboundDemand.Select(o => o.VoucherDetailId).ToList();
        Dictionary<long, decimal> dictionary2 = ((outboundDetailIds.Count != 0) ? (await (from r in _db.StockReservations.AsNoTracking()
                                                                                          where r.VoucherDetailId.HasValue && outboundDetailIds.Contains(r.VoucherDetailId.Value) && r.Status != ReservationStatusEnum.Released
                                                                                          group r by r.VoucherDetailId.GetValueOrDefault() into g
                                                                                          select new
                                                                                          {
                                                                                              DetailId = g.Key,
                                                                                              Qty = g.Sum((StockReservation r) => r.ReservedQty - r.ReleasedQty)
                                                                                          }).ToDictionaryAsync(x => x.DetailId, x => x.Qty)) : new Dictionary<long, decimal>());
        Dictionary<long, decimal> reservedOutboundQty = dictionary2;
        List<Location> stageLocations = await (from l in _db.Locations.AsNoTracking().Include((Location l) => l.Zone)
                                               where l.IsActive && l.Zone != null && (!((int?)warehouseId).HasValue || l.Zone.WarehouseId == ((int?)warehouseId).Value) && (l.Zone.ZoneType == ZoneTypeEnum.CrossDock || l.Zone.ZoneType == ZoneTypeEnum.Staging || l.Zone.ZoneType == ZoneTypeEnum.Shipping)
                                               orderby (l.Zone.ZoneType == ZoneTypeEnum.CrossDock) ? 0 : ((l.Zone.ZoneType == ZoneTypeEnum.Staging) ? 1 : 2), l.LocationCode
                                               select l).ToListAsync();
        Dictionary<int, Location> stageByWarehouse = (from l in stageLocations
                                                      where l.Zone != null
                                                      group l by l.Zone.WarehouseId).ToDictionary((IGrouping<int, Location> g) => g.Key, (IGrouping<int, Location> g) => g.First());
        List<object> opportunities = new List<object>();
        foreach (var inb in from x in inboundItems
                            orderby (!x.ExpiryDate.HasValue) ? 1 : 0, x.ExpiryDate
                            select x)
        {
            decimal matchedQty;
            decimal remainingInboundQty = Math.Max(0m, inb.InboundQty - (matchedInboundQty.TryGetValue(inb.VoucherDetailId, out matchedQty) ? matchedQty : 0m));
            if (remainingInboundQty <= 0m || !stageByWarehouse.TryGetValue(inb.WarehouseId, out var stageLocation))
            {
                continue;
            }
            var demands = (from o in outboundDemand
                           where o.ItemId == inb.ItemId && o.OwnerPartnerId == inb.OwnerPartnerId && (string.IsNullOrWhiteSpace(o.LotNumber) || string.Equals(o.LotNumber, inb.LotNumber, StringComparison.OrdinalIgnoreCase)) && (!o.ExpiryDate.HasValue || o.ExpiryDate == inb.ExpiryDate)
                           orderby (o.ServiceLevel == ServiceLevelEnum.SameDay) ? 100 : 0 descending, (o.ServiceLevel == ServiceLevelEnum.Express) ? 90 : 0 descending, o.Priority descending, o.RequestedDeliveryDate ?? today.AddDays(30.0)
                           select o).ToList();
            foreach (var dem in demands)
            {
                decimal reservedQty;
                decimal openDemand = Math.Max(0m, dem.OutboundQty - (reservedOutboundQty.TryGetValue(dem.VoucherDetailId, out reservedQty) ? reservedQty : 0m));
                decimal crossDockQty = Math.Min(remainingInboundQty, openDemand);
                if (!(crossDockQty <= 0m))
                {
                    opportunities.Add(new
                    {
                        ItemId = inb.ItemId,
                        ItemCode = inb.ItemCode,
                        ItemName = inb.ItemName,
                        InboundVoucherCode = inb.InboundVoucherCode,
                        InboundVoucherId = inb.InboundVoucherId,
                        InboundVoucherDetailId = inb.VoucherDetailId,
                        InboundQty = remainingInboundQty,
                        LotNumber = inb.LotNumber,
                        ExpiryDate = inb.ExpiryDate,
                        OutboundVoucherCode = dem.OutboundVoucherCode,
                        OutboundVoucherId = dem.OutboundVoucherId,
                        OutboundVoucherDetailId = dem.VoucherDetailId,
                        OutboundQty = openDemand,
                        CrossDockQty = crossDockQty,
                        StageLocationId = stageLocation.LocationId,
                        StageLocationCode = stageLocation.LocationCode,
                        WarehouseId = inb.WarehouseId
                    });
                    remainingInboundQty -= crossDockQty;
                    if (remainingInboundQty <= 0m)
                    {
                        break;
                    }
                }
            }
            stageLocation = null;
        }
        base.ViewBag.Warehouses = (await (from w in _db.Warehouses.AsNoTracking()
                                          where w.IsActive
                                          orderby w.WarehouseCode
                                          select w).ToListAsync());
        base.ViewBag.CrossDockTasks = (await (from t in _db.CrossDockTasks.AsNoTracking().Include((CrossDockTask t) => t.InboundVoucher).Include((CrossDockTask t) => t.OutboundVoucher)
                .Include((CrossDockTask t) => t.Item)
                .Include((CrossDockTask t) => t.StageLocation)
                                              where (!((int?)warehouseId).HasValue || (t.InboundVoucher != null && t.InboundVoucher.WarehouseId == ((int?)warehouseId).Value)) && t.Status != CrossDockTaskStatusEnum.Cancelled
                                                  && (allowedOwnerIds.Count == 0 || (t.OutboundVoucher != null && t.OutboundVoucher.OwnerPartnerId.HasValue && allowedOwnerIds.Contains(t.OutboundVoucher.OwnerPartnerId.Value)))
                                              orderby t.Status == CrossDockTaskStatusEnum.Pending descending, t.CreatedAt descending
                                              select t).Take(100).ToListAsync());
        base.ViewBag.WarehouseId = warehouseId;
        base.ViewBag.StageLocations = stageLocations;
        base.ViewBag.Opportunities = opportunities;
        return View();
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.VoucherReleasePicking)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExecuteCrossDock(long inboundVoucherId, long outboundVoucherId, int itemId, decimal qty, int stageLocationId, long? inboundVoucherDetailId, long? outboundVoucherDetailId)
    {
        try
        {
            WorkflowResult result = await _crossDockService.ExecuteCrossDockAsync(inboundVoucherId, outboundVoucherId, itemId, qty, stageLocationId, GetScopedWarehouseId(), base.User.Identity?.Name ?? "system", base.HttpContext.Connection.RemoteIpAddress?.ToString(), inboundVoucherDetailId, outboundVoucherDetailId);
            if (result.Forbidden)
            {
                return Forbid();
            }
            if (result.Succeeded)
            {
                base.TempData["Success"] = result.Message;
            }
            else
            {
                base.TempData["Error"] = result.Message;
            }
            return RedirectToAction(result.RedirectAction ?? "CrossDockOpportunities", result.RedirectRouteValues);
        }
        catch (Exception ex)
        {
            base.TempData["Error"] = UserSafeError.WithPrefix(ex, "Tạo nhiệm vụ cross-dock thất bại", "Không thể tạo nhiệm vụ cross-dock lúc này. Vui lòng thử lại.");
            return RedirectToAction("CrossDockOpportunities");
        }
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.VoucherReleasePicking)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteCrossDockTask(long id)
    {
        var allowedOwnerIds = await _tenantScopeService.GetAllowedOwnerIdsAsync(base.User);
        WorkflowResult result = await _crossDockService.CompleteCrossDockTaskAsync(id, GetScopedWarehouseId(), allowedOwnerIds);
        if (result.Forbidden)
            return Forbid();
        if (result.Succeeded)
        {
            base.TempData["Success"] = result.Message;
        }
        else
        {
            base.TempData["Error"] = result.Message;
        }
        return RedirectToAction("CrossDockOpportunities");
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCycleCountProgram(string programName, int frequencyA, int frequencyB, int frequencyC, bool isBlindCount, decimal varianceThresholdPct)
    {
        int? scopedWh = GetScopedWarehouseId();
        if (!scopedWh.HasValue)
        {
            base.TempData["Error"] = "Cần chỉ định kho.";
            return RedirectToAction("StockCount", "Reports");
        }
        CycleCountProgram program = new CycleCountProgram
        {
            ProgramName = programName,
            WarehouseId = scopedWh.Value,
            FrequencyA = ((frequencyA > 0) ? frequencyA : 30),
            FrequencyB = ((frequencyB > 0) ? frequencyB : 90),
            FrequencyC = ((frequencyC > 0) ? frequencyC : 180),
            IsBlindCount = isBlindCount,
            VarianceThresholdPct = varianceThresholdPct,
            IsActive = true,
            CreatedBy = (base.User.Identity?.Name ?? "system"),
            CreatedAt = VietnamNow
        };
        _db.CycleCountPrograms.Add(program);
        await _unitOfWork.SaveChangesAsync();
        base.TempData["Success"] = $"Đã tạo chương trình [{programName}] (A={frequencyA}d, B={frequencyB}d, C={frequencyC}d).";
        return RedirectToAction("StockCount", "Reports");
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunCycleCountProgram(int programId)
    {
        var program = await _db.CycleCountPrograms.AsNoTracking().FirstOrDefaultAsync(p => p.ProgramId == programId);
        if (program == null || !program.IsActive)
        {
            base.TempData["Error"] = "Chương trình không tồn tại.";
            return RedirectToAction("StockCount", "Reports");
        }
        var scopedWarehouseId = GetScopedWarehouseId();
        if (scopedWarehouseId.HasValue && program.WarehouseId != scopedWarehouseId.Value)
            return Forbid();
        if ((await _tenantScopeService.GetAllowedOwnerIdsAsync(base.User)).Count > 0)
            return Forbid();

        try
        {
            await _cycleCountPlanningService.CreateOrRefreshSchedulesAsync(programId, null);
            var sheet = await _cycleCountPlanningService.GenerateDueSheetAsync(
                programId,
                base.User.Identity?.Name ?? "system",
                maxLines: 50);
            var lineCount = await _db.StockCountLines.CountAsync(line => line.StockCountSheetId == sheet.StockCountSheetId);
            base.TempData["Success"] = $"Đã tạo phiếu [{sheet.SheetCode}] với {lineCount} dòng kiểm kê theo chủ hàng/lô/HSD.";
        }
        catch (BusinessRuleException ex) when (ex.Code == "CYCLE_NO_DUE_LINES")
        {
            base.TempData["Info"] = "Không có vị trí nào đến hạn kiểm kê.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run cycle count program {ProgramId} failed", programId);
            base.TempData["Error"] = UserSafeError.From(ex, "Không thể chạy chương trình kiểm kê lúc này. Vui lòng thử lại.");
        }
        return RedirectToAction("StockCount", "Reports");
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.QcResolveHold)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRecallCase(long[] affectedDetailIds, string reason, int severity, int? supplierId)
    {
        var detailIds = (affectedDetailIds ?? Array.Empty<long>()).Where(id => id > 0).Distinct().ToList();
        if (detailIds.Count == 0)
        {
            base.TempData["Error"] = "Vui lòng chọn ít nhất 1 dòng.";
            return RedirectToAction("QualityInspection");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            base.TempData["Error"] = "Vui lòng nhập lý do thu hồi.";
            return RedirectToAction("QualityInspection");
        }
        var recallSeverity = (RecallSeverityEnum)severity;
        if (!Enum.IsDefined(recallSeverity))
        {
            base.TempData["Error"] = "Mức độ thu hồi không hợp lệ.";
            return RedirectToAction("QualityInspection");
        }
        if (supplierId.HasValue && !await _db.Partners.AsNoTracking().AnyAsync(p => p.PartnerId == supplierId.Value && p.IsActive))
        {
            base.TempData["Error"] = "Nhà cung cấp không tồn tại hoặc đã ngừng hoạt động.";
            return RedirectToAction("QualityInspection");
        }

        var details = await _db.VoucherDetails
            .Include(d => d.Item)
            .Include(d => d.Voucher)
            .Where(d => detailIds.Contains(d.VoucherDetailId))
            .ToListAsync();
        if (details.Count != detailIds.Count
            || details.Any(d => d.Item == null || !d.Item.IsActive || d.Voucher == null || !d.LocationId.HasValue || d.BaseQty == 0))
        {
            base.TempData["Error"] = "Danh sách dòng thu hồi có dữ liệu thiếu, không hợp lệ hoặc không còn hoạt động.";
            return RedirectToAction("QualityInspection");
        }

        var scopedWarehouseId = GetScopedWarehouseId();
        if (scopedWarehouseId.HasValue && details.Any(d => d.Voucher!.WarehouseId != scopedWarehouseId.Value))
            return Forbid();
        var allowedOwnerIds = await _tenantScopeService.GetAllowedOwnerIdsAsync(base.User);
        if (allowedOwnerIds.Count > 0 && details.Any(d =>
        {
            var ownerId = d.OwnerPartnerId ?? d.Voucher!.OwnerPartnerId;
            return !ownerId.HasValue || !allowedOwnerIds.Contains(ownerId.Value);
        }))
        {
            return Forbid();
        }

        var actor = base.User.Identity?.Name ?? "system";
        var caseNumber = $"RCL-{VietnamNow:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant();
        var recallCase = new RecallCase
        {
            CaseNumber = caseNumber,
            Reason = reason.Trim(),
            Severity = recallSeverity,
            SupplierId = supplierId,
            Status = RecallStatusEnum.Issued,
            IssuedBy = actor,
            IssuedAt = VietnamNow
        };
        foreach (var detail in details)
        {
            recallCase.Lines.Add(new RecallLine
            {
                ItemId = detail.ItemId,
                OwnerPartnerId = detail.OwnerPartnerId ?? detail.Voucher!.OwnerPartnerId,
                LotNumber = detail.LotNumber,
                AffectedQty = Math.Abs(detail.BaseQty),
                Disposition = RecallDispositionEnum.Quarantine,
                LineStatus = RecallLineStatusEnum.InProgress,
                CreatedAt = VietnamNow
            });
        }

        await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            _db.RecallCases.Add(recallCase);
            await _unitOfWork.SaveChangesAsync();
            using var ledgerScope = _inventoryTransactionService.BeginScope(new InventoryTransactionContext
            {
                TransactionType = InventoryTransactionTypeEnum.Hold,
                TransactionGroupKey = $"recall:{recallCase.RecallCaseId}:hold",
                IdempotencyKeyPrefix = $"recall:{recallCase.RecallCaseId}:hold",
                WarehouseId = details.Select(d => d.Voucher!.WarehouseId).Distinct().Count() == 1
                    ? details[0].Voucher!.WarehouseId
                    : null,
                ReferenceType = "RecallCase",
                ReferenceId = recallCase.RecallCaseId.ToString(),
                ReferenceCode = recallCase.CaseNumber,
                Actor = actor
            });

            var touchedItemLocationIds = new HashSet<int>();
            foreach (var detail in details)
            {
                var ownerId = detail.OwnerPartnerId ?? detail.Voucher!.OwnerPartnerId;
                var affectedRows = await _db.ItemLocations
                    .Where(itemLocation => itemLocation.ItemId == detail.ItemId
                        && itemLocation.OwnerPartnerId == ownerId
                        && itemLocation.LocationId == detail.LocationId!.Value
                        && itemLocation.LotNumber == detail.LotNumber
                        && itemLocation.ExpiryDate == detail.ExpiryDate
                        && itemLocation.HoldStatus == InventoryHoldStatusEnum.Available)
                    .ToListAsync();
                foreach (var itemLocation in affectedRows.Where(row => touchedItemLocationIds.Add(row.ItemLocationId)))
                {
                    var oldStatus = itemLocation.HoldStatus;
                    itemLocation.HoldStatus = InventoryHoldStatusEnum.Quarantine;
                    itemLocation.UpdatedAt = VietnamNow;
                    _db.AuditLogs.Add(new AuditLog
                    {
                        TableName = "ItemLocation",
                        RecordId = itemLocation.ItemLocationId.ToString(),
                        ActionType = "QUARANTINE_BY_RECALL",
                        ColumnChanged = "HoldStatus",
                        OldValue = oldStatus.ToString(),
                        NewValue = "Recall:" + caseNumber,
                        ChangedBy = actor,
                        ChangedAt = VietnamNow,
                        IpAddress = base.HttpContext.Connection.RemoteIpAddress?.ToString(),
                        AppModule = "Recall"
                    });
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitAsync();
            base.TempData["Success"] = $"Đã tạo thu hồi [{caseNumber}] với {details.Count} dòng; cách ly {touchedItemLocationIds.Count} dòng tồn kho phù hợp.";
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Create recall case failed");
            base.TempData["Error"] = UserSafeError.From(ex, "Không thể tạo đợt thu hồi lúc này. Vui lòng thử lại.");
        }
        finally
        {
            if (_unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
        }
        return RedirectToAction("QualityInspection");
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.QcResolveHold)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveRecallCase(long id, string resolution, int disposition)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            base.TempData["Error"] = "Vui lòng nhập kết luận xử lý thu hồi.";
            return RedirectToAction("QualityInspection");
        }
        var recallDisposition = (RecallDispositionEnum)disposition;
        if (!Enum.IsDefined(recallDisposition))
        {
            base.TempData["Error"] = "Phương án xử lý thu hồi không hợp lệ.";
            return RedirectToAction("QualityInspection");
        }

        await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        IDisposable? ledgerScope = null;
        try
        {
            var recallCase = await _db.RecallCases
                .Include(recall => recall.Lines)
                .FirstOrDefaultAsync(recall => recall.RecallCaseId == id);
            if (recallCase == null)
            {
                base.TempData["Error"] = "Không tìm thấy đợt thu hồi.";
                return RedirectToAction("QualityInspection");
            }
            if (recallCase.Status is RecallStatusEnum.Resolved or RecallStatusEnum.Cancelled)
            {
                base.TempData["Error"] = "Đợt thu hồi đã kết thúc, không thể xử lý lại.";
                return RedirectToAction("QualityInspection");
            }

            var allowedOwnerIds = await _tenantScopeService.GetAllowedOwnerIdsAsync(base.User);
            if (allowedOwnerIds.Count > 0
                && recallCase.Lines.Any(line => !line.OwnerPartnerId.HasValue || !allowedOwnerIds.Contains(line.OwnerPartnerId.Value)))
            {
                return Forbid();
            }

            var auditedRecordIds = (await _db.AuditLogs.AsNoTracking()
                    .Where(log => log.TableName == "ItemLocation"
                        && log.ActionType == "QUARANTINE_BY_RECALL"
                        && log.NewValue == "Recall:" + recallCase.CaseNumber)
                    .Select(log => log.RecordId)
                    .ToListAsync())
                .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
                .Where(value => value > 0)
                .Distinct()
                .ToList();
            var auditedRows = auditedRecordIds.Count == 0
                ? new List<ItemLocation>()
                : await _db.ItemLocations
                    .Include(row => row.Location)!
                    .ThenInclude(location => location!.Zone)
                    .Where(row => auditedRecordIds.Contains(row.ItemLocationId))
                    .ToListAsync();

            var scopedWarehouseId = GetScopedWarehouseId();
            if (scopedWarehouseId.HasValue
                && (auditedRows.Count == 0 || auditedRows.Any(row => row.Location?.Zone?.WarehouseId != scopedWarehouseId.Value)))
            {
                return Forbid();
            }
            if (allowedOwnerIds.Count > 0
                && auditedRows.Any(row => !row.OwnerPartnerId.HasValue || !allowedOwnerIds.Contains(row.OwnerPartnerId.Value)))
            {
                return Forbid();
            }

            var releaseInventory = recallDisposition == RecallDispositionEnum.ReleaseUnderObservations;
            var releasableRows = auditedRows
                .Where(row => row.HoldStatus is InventoryHoldStatusEnum.Quarantine or InventoryHoldStatusEnum.QcHold)
                .ToList();
            if (releaseInventory && releasableRows.Count == 0)
                throw new BusinessRuleException("Không tìm thấy tồn kho do đợt thu hồi này cách ly để giải phóng.", "RECALL_HELD_STOCK_NOT_FOUND", "ItemLocation");

            var actor = base.User.Identity?.Name ?? "system";
            if (releaseInventory)
            {
                ledgerScope = _inventoryTransactionService.BeginScope(new InventoryTransactionContext
                {
                    TransactionType = InventoryTransactionTypeEnum.ReleaseHold,
                    TransactionGroupKey = $"recall:{recallCase.RecallCaseId}:release",
                    IdempotencyKeyPrefix = $"recall:{recallCase.RecallCaseId}:release",
                    WarehouseId = auditedRows.Select(row => row.Location!.Zone!.WarehouseId).Distinct().Count() == 1
                        ? auditedRows[0].Location!.Zone!.WarehouseId
                        : null,
                    ReferenceType = "RecallCaseRelease",
                    ReferenceId = recallCase.RecallCaseId.ToString(),
                    ReferenceCode = recallCase.CaseNumber,
                    Actor = actor
                });
                foreach (var itemLocation in releasableRows)
                {
                    var oldStatus = itemLocation.HoldStatus;
                    itemLocation.HoldStatus = InventoryHoldStatusEnum.Available;
                    itemLocation.UpdatedAt = VietnamNow;
                    _db.AuditLogs.Add(new AuditLog
                    {
                        TableName = "ItemLocation",
                        RecordId = itemLocation.ItemLocationId.ToString(),
                        ActionType = "RELEASE_RECALL_QUARANTINE",
                        ColumnChanged = "HoldStatus",
                        OldValue = oldStatus.ToString(),
                        NewValue = InventoryHoldStatusEnum.Available.ToString(),
                        ChangedBy = actor,
                        ChangedAt = VietnamNow,
                        IpAddress = base.HttpContext.Connection.RemoteIpAddress?.ToString(),
                        AppModule = "Recall"
                    });
                }
            }

            recallCase.Status = RecallStatusEnum.Resolved;
            recallCase.Resolution = resolution.Trim();
            recallCase.ResolvedBy = actor;
            recallCase.ResolvedAt = VietnamNow;
            foreach (var line in recallCase.Lines)
            {
                line.LineStatus = RecallLineStatusEnum.Dispositioned;
                line.Disposition = recallDisposition;
                line.CompletedAt = VietnamNow;
            }

            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitAsync();
            if (releaseInventory)
                base.TempData["Success"] = $"Đợt thu hồi [{recallCase.CaseNumber}] đã kết thúc; giải phóng {releasableRows.Count} dòng tồn kho có giám sát.";
            else
                base.TempData["Warning"] = $"Đợt thu hồi [{recallCase.CaseNumber}] đã ghi nhận phương án {recallDisposition}; hàng vẫn bị cách ly cho quy trình xử lý vật lý.";
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Resolve recall case {RecallCaseId} failed", id);
            base.TempData["Error"] = UserSafeError.From(ex, "Không thể xử lý đợt thu hồi lúc này. Vui lòng thử lại.");
        }
        finally
        {
            ledgerScope?.Dispose();
            if (_unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
        }
        return RedirectToAction("QualityInspection");
    }


    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> CapacitySimulation(int? warehouseId)
    {
        int? scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue)
        {
            warehouseId = scopedWh.Value;
        }
        base.ViewBag.Warehouses = (await (from w in _db.Warehouses
                                          where w.IsActive
                                          orderby w.WarehouseCode
                                          select w).ToListAsync());
        base.ViewBag.WarehouseId = warehouseId;
        if (!warehouseId.HasValue)
        {
            return View(new List<CapacityScenario>());
        }
        List<CapacityScenario> scenarios = await (from s in _db.CapacityScenarios
                                                  where (int?)s.WarehouseId == warehouseId && s.IsActive
                                                  orderby s.CreatedAt descending
                                                  select s).Take(20).ToListAsync();
        await _db.Warehouses.FindAsync(warehouseId.Value);
        int recentVouchers = await _db.Vouchers.Where((Voucher v) => (int?)v.WarehouseId == warehouseId && v.CreatedAt >= VietnamNow.AddDays(-30.0)).CountAsync();
        int dockCount = await _db.Locations.Where((Location l) => (int?)l.Zone.WarehouseId == warehouseId && l.Zone.ZoneType == ZoneTypeEnum.Shipping && l.IsActive).CountAsync();
        int laborCount = await _db.AppUsers.Where((AppUser u) => u.IsActive && u.WarehouseId == warehouseId).CountAsync();
        base.ViewBag.BaselineVolume = recentVouchers;
        base.ViewBag.DockCount = dockCount;
        base.ViewBag.LaborCount = laborCount;
        return View(scenarios);
    }

}
