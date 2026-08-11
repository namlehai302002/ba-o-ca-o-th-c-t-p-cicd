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

    [Authorize(Roles = WmsRoles.InventoryRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [HttpGet]
    public async Task<IActionResult> StockCount(int? warehouseId, DateTime? countDate)
    {
        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        countDate ??= VietnamNow.Date;

        var vm = new StockCountPageViewModel
        {
            WarehouseId = warehouseId,
            CountDate = countDate.Value,
            Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync()
        };
        if (scopedWh.HasValue)
            vm.Warehouses = vm.Warehouses.Where(w => w.WarehouseId == scopedWh.Value).ToList();

        if (!warehouseId.HasValue)
            return View(vm);

        vm.ExistingSheets = await _db.StockCountSheets.AsNoTracking()
            .Include(s => s.GeneratedAdjustmentVoucher)
            .Where(s => s.WarehouseId == warehouseId.Value && s.CountDate == countDate.Value.Date)
            .Where(s => scopedOwnerIds.Count == 0 || s.Lines.Any(l => l.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(l.OwnerPartnerId.Value)))
            .OrderByDescending(s => s.StockCountSheetId)
            .Take(50)
            .Select(s => new StockCountSheetSummary
            {
                StockCountSheetId = s.StockCountSheetId,
                CountDate = s.CountDate,
                CreatedBy = s.CreatedBy,
                CreatedAt = s.CreatedAt,
                Status = s.Status,
                ApprovedBy = s.ApprovedBy,
                ApprovedAt = s.ApprovedAt,
                ApprovalReason = s.ApprovalReason,
                UnlockedBy = s.UnlockedBy,
                UnlockedAt = s.UnlockedAt,
                UnlockReason = s.UnlockReason,
                VoucherCode = s.GeneratedAdjustmentVoucher != null ? s.GeneratedAdjustmentVoucher.VoucherCode : null
            })
            .ToListAsync();
        var sheetIds = vm.ExistingSheets.Select(s => s.StockCountSheetId).ToList();
        var lineStats = sheetIds.Count == 0
            ? new Dictionary<long, (int Total, int Counted, int Diff)>()
            : await _db.StockCountLines.AsNoTracking()
                .Where(l => sheetIds.Contains(l.StockCountSheetId))
                .GroupBy(l => l.StockCountSheetId)
                .Select(g => new
                {
                    SheetId = g.Key,
                    Total = g.Count(),
                    Counted = g.Count(x => x.CountedQty != null),
                    Diff = g.Count(x => x.Variance != null && x.Variance != 0)
                })
                .ToDictionaryAsync(x => x.SheetId, x => (x.Total, x.Counted, x.Diff));
        foreach (var sheet in vm.ExistingSheets)
        {
            if (lineStats.TryGetValue(sheet.StockCountSheetId, out var stats))
            {
                sheet.TotalLines = stats.Total;
                sheet.CountedLines = stats.Counted;
                sheet.DiffLines = stats.Diff;
            }
        }

        vm.Lines = await _db.ItemLocations.AsNoTracking()
            .Include(il => il.Location).ThenInclude(l => l!.Zone)
            .Include(il => il.Item)
            .Where(il => il.Quantity != 0
                && il.Location != null
                && il.Location.Zone != null
                && il.Location.Zone.WarehouseId == warehouseId.Value)
            .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
            .OrderBy(il => il.ItemId).ThenBy(il => il.LocationId)
            .Select(il => new StockCountLineInput
            {
                ItemId = il.ItemId,
                ItemCode = il.Item != null ? il.Item.ItemCode : "",
                ItemName = il.Item != null ? il.Item.ItemName : "",
                OwnerPartnerId = il.OwnerPartnerId,
                LocationId = il.LocationId,
                LocationCode = il.Location != null ? il.Location.LocationCode : "",
                LotNumber = il.LotNumber,
                ExpiryDate = il.ExpiryDate,
                SystemQty = il.Quantity,
                CountedQty = null
            })
            .ToListAsync();

        return View(vm);
    }

    [Authorize(Roles = WmsRoles.InventoryRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [HttpGet]
    public async Task<IActionResult> StockCountEntry(long id)
    {
        var sheet = await _db.StockCountSheets.AsNoTracking()
            .Include(s => s.Warehouse)
            .Include(s => s.Lines).ThenInclude(l => l.Item)
            .Include(s => s.Lines).ThenInclude(l => l.Location)
            .FirstOrDefaultAsync(s => s.StockCountSheetId == id);
        if (sheet == null)
        {
            TempData["Error"] = "Không tìm thấy phiếu kiểm kê.";
            return RedirectToAction(nameof(StockCount));
        }

        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue && sheet.WarehouseId != scopedWh.Value)
            return Forbid();
        if (scopedOwnerIds.Count > 0
            && sheet.Lines.Any(l => !l.OwnerPartnerId.HasValue || !scopedOwnerIds.Contains(l.OwnerPartnerId.Value)))
            return Forbid();

        var isBlindCount = sheet.Notes?.Contains("blind=True", StringComparison.OrdinalIgnoreCase) == true;
        var isCountInProgress = sheet.Status is StockCountStatusEnum.Draft or StockCountStatusEnum.Counting;
        var canReviewExpectedQty = User.IsInRole(WmsRoles.Admin) || User.IsInRole(WmsRoles.Manager);
        var vm = new StockCountEntryViewModel
        {
            StockCountSheetId = sheet.StockCountSheetId,
            SheetCode = string.IsNullOrWhiteSpace(sheet.SheetCode) ? $"#{sheet.StockCountSheetId}" : sheet.SheetCode,
            WarehouseId = sheet.WarehouseId,
            WarehouseCode = sheet.Warehouse?.WarehouseCode ?? "",
            WarehouseName = sheet.Warehouse?.WarehouseName ?? "",
            CountDate = sheet.CountDate,
            Status = sheet.Status,
            IsBlindCount = isBlindCount,
            CanViewExpectedQty = !isBlindCount || (!isCountInProgress && canReviewExpectedQty),
            Notes = sheet.Notes,
            Lines = sheet.Lines
                .OrderBy(l => l.Location != null ? l.Location.LocationCode : "")
                .ThenBy(l => l.Item != null ? l.Item.ItemCode : "")
                .ThenBy(l => l.LotNumber)
                .Select(l => new StockCountEntryLineInput
                {
                    StockCountLineId = l.StockCountLineId,
                    ItemCode = l.Item?.ItemCode ?? l.ItemId.ToString(),
                    ItemName = l.Item?.ItemName ?? "",
                    LocationCode = l.Location?.LocationCode ?? l.LocationId.ToString(),
                    LotNumber = l.LotNumber,
                    ExpiryDate = l.ExpiryDate,
                    SystemQty = l.SystemQty,
                    CountedQty = l.CountedQty,
                    Variance = l.Variance
                })
                .ToList()
        };
        return View(vm);
    }

    [Authorize(Roles = WmsRoles.InventoryRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StockCountStart(long id)
    {
        var sheet = await _db.StockCountSheets
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.StockCountSheetId == id);
        if (sheet == null)
        {
            TempData["Error"] = "Không tìm thấy phiếu kiểm kê.";
            return RedirectToAction(nameof(StockCount));
        }

        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue && sheet.WarehouseId != scopedWh.Value)
            return Forbid();
        if (scopedOwnerIds.Count > 0
            && sheet.Lines.Any(l => !l.OwnerPartnerId.HasValue || !scopedOwnerIds.Contains(l.OwnerPartnerId.Value)))
            return Forbid();
        if (sheet.Status != StockCountStatusEnum.Draft)
        {
            TempData["Error"] = "Chỉ phiếu chưa bắt đầu mới có thể chuyển sang bước kiểm đếm.";
            return RedirectToAction(nameof(StockCountEntry), new { id });
        }
        if (sheet.Lines.Count == 0)
        {
            TempData["Error"] = "Phiếu kiểm kê không có dòng hàng để kiểm đếm.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
        }

        sheet.Status = StockCountStatusEnum.Counting;
        await _inventoryRiskRecommendationService.SyncSheetStateAsync(
            sheet.StockCountSheetId,
            sheet.Status,
            User.Identity?.Name ?? "system",
            "STOCK_COUNT_STARTED");
        await _unitOfWork.SaveChangesAsync();
        TempData["Success"] = $"Đã bắt đầu kiểm đếm phiếu {sheet.SheetCode ?? $"#{sheet.StockCountSheetId}"}.";
        return RedirectToAction(nameof(StockCountEntry), new { id });
    }

    [Authorize(Roles = WmsRoles.InventoryRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StockCountSubmit(StockCountEntryViewModel vm)
    {
        var actor = User.Identity?.Name ?? "system";
        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var sheet = await _db.StockCountSheets
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.StockCountSheetId == vm.StockCountSheetId);
            if (sheet == null)
            {
                TempData["Error"] = "Không tìm thấy phiếu kiểm kê.";
                return RedirectToAction(nameof(StockCount));
            }

            var scopedWh = GetScopedWarehouseId();
            var scopedOwnerIds = GetScopedOwnerPartnerIds();
            if (scopedWh.HasValue && sheet.WarehouseId != scopedWh.Value)
                return Forbid();
            if (scopedOwnerIds.Count > 0
                && sheet.Lines.Any(l => !l.OwnerPartnerId.HasValue || !scopedOwnerIds.Contains(l.OwnerPartnerId.Value)))
                return Forbid();
            if (sheet.Status is not (StockCountStatusEnum.Draft or StockCountStatusEnum.Counting))
            {
                TempData["Error"] = "Phiếu kiểm kê không ở trạng thái cho phép nhập số đếm.";
                return RedirectToAction(nameof(StockCountEntry), new { id = sheet.StockCountSheetId });
            }

            var submittedLines = vm.Lines ?? new List<StockCountEntryLineInput>();
            var submittedIds = submittedLines.Select(l => l.StockCountLineId).ToList();
            var expectedIds = sheet.Lines.Select(l => l.StockCountLineId).OrderBy(id => id).ToList();
            if (submittedIds.Count != submittedIds.Distinct().Count()
                || !submittedIds.OrderBy(id => id).SequenceEqual(expectedIds))
            {
                TempData["Error"] = "Danh sách dòng kiểm kê không còn khớp với phiếu. Vui lòng tải lại trang.";
                return RedirectToAction(nameof(StockCountEntry), new { id = sheet.StockCountSheetId });
            }
            if (submittedLines.Any(l => !l.CountedQty.HasValue))
            {
                TempData["Error"] = "Vui lòng nhập số lượng thực tế cho tất cả các dòng.";
                return RedirectToAction(nameof(StockCountEntry), new { id = sheet.StockCountSheetId });
            }
            if (submittedLines.Any(l => l.CountedQty!.Value < 0m))
            {
                TempData["Error"] = "Số lượng thực tế không được âm.";
                return RedirectToAction(nameof(StockCountEntry), new { id = sheet.StockCountSheetId });
            }

            var submittedById = submittedLines.ToDictionary(l => l.StockCountLineId);
            var countedAt = VietnamNow;
            foreach (var line in sheet.Lines)
            {
                var countedQty = submittedById[line.StockCountLineId].CountedQty!.Value;
                line.CountedQty = countedQty;
                line.Variance = countedQty - line.SystemQty;
                line.CountedBy = actor;
                line.CountedAt = countedAt;
                line.Status = 1;
            }
            sheet.Status = StockCountStatusEnum.Counted;
            sheet.CompletedAt = null;
            await _inventoryRiskRecommendationService.SyncSheetStateAsync(
                sheet.StockCountSheetId,
                sheet.Status,
                actor,
                "STOCK_COUNT_SUBMITTED");
            await _unitOfWork.SaveChangesAsync();
            if (startedTransaction)
                await _unitOfWork.CommitAsync();

            TempData["Success"] = $"Đã ghi nhận đủ {sheet.Lines.Count} dòng kiểm kê. Phiếu đang chờ quản lý duyệt.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
            _logger.LogWarning(ex, "Concurrency conflict when submitting stock count. SheetId={SheetId}, Actor={Actor}", vm.StockCountSheetId, actor);
            TempData["Error"] = "Phiếu kiểm kê đã thay đổi ở phiên khác. Vui lòng tải lại trước khi gửi kết quả.";
            return RedirectToAction(nameof(StockCountEntry), new { id = vm.StockCountSheetId });
        }
        catch (Exception ex)
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Stock count submission failed. SheetId={SheetId}, Actor={Actor}", vm.StockCountSheetId, actor);
            TempData["Error"] = UserSafeError.WithPrefix(ex, "Lỗi ghi nhận kiểm kê", "Không thể ghi nhận kết quả kiểm kê lúc này. Vui lòng thử lại.");
            return RedirectToAction(nameof(StockCountEntry), new { id = vm.StockCountSheetId });
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
        }
    }


    [Authorize(Roles = WmsRoles.InventoryRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StockCountSaveDraft(StockCountPageViewModel vm)
    {
        if (!vm.WarehouseId.HasValue || vm.WarehouseId.Value <= 0)
        {
            TempData["Error"] = "Vui lòng chọn kho kiểm kê.";
            return RedirectToAction(nameof(StockCount));
        }

        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue && vm.WarehouseId.Value != scopedWh.Value)
            return Forbid();

        var lockDate = await _db.WarehousePeriodLocks.AsNoTracking()
            .Where(l => l.WarehouseId == vm.WarehouseId.Value && l.IsActive)
            .OrderByDescending(l => l.LockDate)
            .Select(l => (DateTime?)l.LockDate)
            .FirstOrDefaultAsync();
        if (lockDate.HasValue && vm.CountDate.Date <= lockDate.Value.Date)
        {
            TempData["Error"] = $"Kho đã khóa kỳ đến {lockDate.Value:dd/MM/yyyy}. Không thể tạo điều chỉnh kiểm kê cho ngày {vm.CountDate:dd/MM/yyyy}.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }
        var approvedExists = await _db.StockCountSheets.AsNoTracking()
            .AnyAsync(s => s.WarehouseId == vm.WarehouseId.Value && s.CountDate == vm.CountDate.Date && s.Status == StockCountStatusEnum.Approved);
        if (approvedExists)
        {
            TempData["Error"] = "Ngày kiểm kê này đã được duyệt. Hệ thống khóa không cho tạo/sửa phiếu kiểm kê mới.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }

        var normalizedLines = (vm.Lines ?? new List<StockCountLineInput>())
            .Where(l => l.ItemId > 0 && l.LocationId > 0)
            .Select(l => new StockCountLineInput
            {
                ItemId = l.ItemId,
                OwnerPartnerId = l.OwnerPartnerId,
                LocationId = l.LocationId,
                LotNumber = string.IsNullOrWhiteSpace(l.LotNumber) ? null : l.LotNumber.Trim(),
                ExpiryDate = l.ExpiryDate?.Date,
                CountedQty = l.CountedQty
            })
            .GroupBy(l => new { l.ItemId, l.OwnerPartnerId, l.LocationId, l.LotNumber, l.ExpiryDate })
            .Select(g => g.Last())
            .ToList();

        if (normalizedLines.Count == 0)
        {
            TempData["Error"] = "Không có dòng kiểm kê hợp lệ.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }
        if (normalizedLines.Any(l => !l.CountedQty.HasValue))
        {
            TempData["Error"] = "Vui lòng nhập số lượng thực tế cho tất cả các dòng kiểm kê.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }
        if (normalizedLines.Any(l => l.CountedQty.HasValue && l.CountedQty.Value < 0))
        {
            TempData["Error"] = "Số lượng thực tế không được âm.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }
        if (scopedOwnerIds.Count > 0
            && normalizedLines.Any(l => !l.OwnerPartnerId.HasValue || !scopedOwnerIds.Contains(l.OwnerPartnerId.Value)))
        {
            TempData["Error"] = "Có dòng kiểm kê không thuộc chủ hàng bạn được phép thao tác.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }
        var inputLocationIds = normalizedLines.Select(l => l.LocationId).Distinct().ToList();
        var validLocationIds = await _db.Locations.AsNoTracking()
            .Include(l => l.Zone)
            .Where(l => inputLocationIds.Contains(l.LocationId)
                && l.Zone != null
                && l.Zone.WarehouseId == vm.WarehouseId.Value)
            .Select(l => l.LocationId)
            .ToListAsync();
        if (validLocationIds.Count != inputLocationIds.Count)
        {
            TempData["Error"] = "Có vị trí không thuộc kho đang kiểm kê.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }

        // Validate items still exist and are active
        var inputItemIds = normalizedLines.Select(l => l.ItemId).Distinct().ToList();
        var activeItemIds = await _db.Items.AsNoTracking()
            .Where(i => inputItemIds.Contains(i.ItemId) && i.IsActive)
            .Select(i => i.ItemId)
            .ToListAsync();
        var inactiveItems = inputItemIds.Except(activeItemIds).ToList();
        if (inactiveItems.Count > 0)
        {
            var inactiveNames = await _db.Items.AsNoTracking()
                .Where(i => inactiveItems.Contains(i.ItemId))
                .Select(i => i.ItemCode)
                .ToListAsync();
            TempData["Error"] = $"Vật tư đã bị vô hiệu hóa hoặc không tồn tại: {string.Join(", ", inactiveNames)}. Vui lòng loại bỏ khỏi phiếu kiểm kê.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }

        // Security/integrity: never trust client SystemQty; recompute from DB by batch key.
        var itemIds = normalizedLines.Select(x => x.ItemId).Distinct().ToList();
        var locationIds = normalizedLines.Select(x => x.LocationId).Distinct().ToList();
        var currentRows = await _db.ItemLocations.AsNoTracking()
            .Where(il => itemIds.Contains(il.ItemId) && locationIds.Contains(il.LocationId))
            .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
            .Select(il => new { il.ItemId, il.OwnerPartnerId, il.LocationId, il.LotNumber, il.ExpiryDate, il.Quantity })
            .ToListAsync();
        foreach (var l in normalizedLines)
        {
            l.SystemQty = currentRows
                .Where(r => r.ItemId == l.ItemId
                    && r.OwnerPartnerId == l.OwnerPartnerId
                    && r.LocationId == l.LocationId
                    && r.LotNumber == l.LotNumber
                    && r.ExpiryDate == l.ExpiryDate)
                .Sum(r => r.Quantity);
        }

        await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var sheet = new StockCountSheet
            {
                WarehouseId = vm.WarehouseId.Value,
                CountDate = vm.CountDate.Date,
                Notes = vm.Notes,
                Status = StockCountStatusEnum.Counted,
                CreatedBy = User.Identity?.Name ?? "system",
                CreatedAt = VietnamNow
            };
            _db.StockCountSheets.Add(sheet);
            await _unitOfWork.SaveChangesAsync();

            foreach (var l in normalizedLines)
            {
                _db.StockCountLines.Add(new StockCountLine
                {
                    StockCountSheetId = sheet.StockCountSheetId,
                    ItemId = l.ItemId,
                    OwnerPartnerId = l.OwnerPartnerId,
                    LocationId = l.LocationId,
                    LotNumber = l.LotNumber,
                    ExpiryDate = l.ExpiryDate,
                    SystemQty = l.SystemQty,
                    CountedQty = l.CountedQty,
                    Variance = l.CountedQty!.Value - l.SystemQty,
                    CountedBy = User.Identity?.Name ?? "system",
                    CountedAt = VietnamNow
                });
            }
            await _unitOfWork.SaveChangesAsync();

            await _unitOfWork.CommitAsync();
            TempData["Success"] = $"Đã ghi nhận kết quả kiểm kê #{sheet.StockCountSheetId}. Phiếu đang chờ quản lý duyệt.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync();
            TempData["Error"] = UserSafeError.WithPrefix(ex, "Lỗi lưu phiếu kiểm kê", "Không thể lưu phiếu kiểm kê lúc này. Vui lòng thử lại.");
            return RedirectToAction(nameof(StockCount), new { warehouseId = vm.WarehouseId, countDate = vm.CountDate });
        }
    }

    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StockCountRequestRecount(long id, string? recountReason)
    {
        if (string.IsNullOrWhiteSpace(recountReason))
        {
            TempData["Error"] = "Vui lòng nhập lý do yêu cầu kiểm đếm lại.";
            return RedirectToAction(nameof(StockCount));
        }
        recountReason = recountReason.Trim();
        if (recountReason.Length > 300)
            recountReason = recountReason[..300];

        var actor = User.Identity?.Name ?? "system";
        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var sheet = await _db.StockCountSheets
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.StockCountSheetId == id);
            if (sheet == null)
            {
                TempData["Error"] = "Không tìm thấy phiếu kiểm kê.";
                return RedirectToAction(nameof(StockCount));
            }

            var scopedWh = GetScopedWarehouseId();
            var scopedOwnerIds = GetScopedOwnerPartnerIds();
            if (scopedWh.HasValue && sheet.WarehouseId != scopedWh.Value)
                return Forbid();
            if (scopedOwnerIds.Count > 0
                && sheet.Lines.Any(l => !l.OwnerPartnerId.HasValue || !scopedOwnerIds.Contains(l.OwnerPartnerId.Value)))
                return Forbid();
            if (sheet.Status != StockCountStatusEnum.Counted)
            {
                TempData["Error"] = "Chỉ phiếu đã gửi kết quả và chưa duyệt mới được yêu cầu kiểm đếm lại.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }

            foreach (var line in sheet.Lines)
            {
                line.CountedQty = null;
                line.Variance = null;
                line.CountedBy = null;
                line.CountedAt = null;
                line.Status = 3;
            }
            sheet.Status = StockCountStatusEnum.Counting;
            sheet.CompletedAt = null;
            var recountAudit = $"[{VietnamNow:dd/MM/yyyy HH:mm}] {actor} yêu cầu kiểm đếm lại: {recountReason}";
            var existingNotes = sheet.Notes?.Trim();
            if (string.IsNullOrWhiteSpace(existingNotes))
            {
                sheet.Notes = recountAudit.Length <= 500 ? recountAudit : recountAudit[..500];
            }
            else
            {
                var availableExistingLength = Math.Max(0, 500 - recountAudit.Length - Environment.NewLine.Length);
                var retainedNotes = existingNotes.Length <= availableExistingLength
                    ? existingNotes
                    : existingNotes[..availableExistingLength];
                sheet.Notes = string.IsNullOrEmpty(retainedNotes)
                    ? recountAudit[..Math.Min(500, recountAudit.Length)]
                    : retainedNotes + Environment.NewLine + recountAudit;
            }

            await _inventoryRiskRecommendationService.SyncSheetStateAsync(
                sheet.StockCountSheetId,
                sheet.Status,
                actor,
                "STOCK_COUNT_RECOUNT_REQUESTED");
            await _unitOfWork.SaveChangesAsync();
            if (startedTransaction)
                await _unitOfWork.CommitAsync();
            TempData["Success"] = $"Đã chuyển phiếu #{sheet.StockCountSheetId} sang kiểm đếm lại.";
            return RedirectToAction(nameof(StockCountEntry), new { id = sheet.StockCountSheetId });
        }
        catch (Exception ex)
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Stock count recount request failed. SheetId={SheetId}, Actor={Actor}", id, actor);
            TempData["Error"] = UserSafeError.WithPrefix(ex, "Lỗi yêu cầu kiểm đếm lại", "Không thể chuyển phiếu sang kiểm đếm lại lúc này.");
            return RedirectToAction(nameof(StockCount));
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
        }
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StockCountApproveDraft(long id, string? approvalReason)
    {
        if (string.IsNullOrWhiteSpace(approvalReason))
        {
            TempData["Error"] = "Vui lòng nhập lý do duyệt kiểm kê.";
            return RedirectToAction(nameof(StockCount));
        }
        approvalReason = approvalReason.Trim();
        if (approvalReason.Length > 500)
            approvalReason = approvalReason[..500];
        var approver = User.Identity?.Name ?? "system";
        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        int? redirectWarehouseId = null;
        DateTime? redirectCountDate = null;
        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var sheet = await _db.StockCountSheets
                .FirstOrDefaultAsync(s => s.StockCountSheetId == id);
            if (sheet == null)
            {
                TempData["Error"] = "Không tìm thấy phiếu kiểm kê.";
                return RedirectToAction(nameof(StockCount));
            }
            using var ledgerScope = _inventoryTransactionService.BeginScope(new InventoryTransactionContext
            {
                TransactionType = InventoryTransactionTypeEnum.Adjust,
                TransactionGroupKey = $"stock-count:{sheet.StockCountSheetId}:approve",
                IdempotencyKeyPrefix = $"stock-count:{sheet.StockCountSheetId}:approve",
                WarehouseId = sheet.WarehouseId,
                ReferenceType = "StockCountSheet",
                ReferenceId = sheet.StockCountSheetId.ToString(),
                ReferenceCode = $"COUNT-{sheet.StockCountSheetId}",
                Actor = approver
            });
            if (scopedWh.HasValue && sheet.WarehouseId != scopedWh.Value)
                return Forbid();
            if (scopedOwnerIds.Count > 0
                && await _db.StockCountLines.AsNoTracking().AnyAsync(l => l.StockCountSheetId == sheet.StockCountSheetId
                    && (!l.OwnerPartnerId.HasValue || !scopedOwnerIds.Contains(l.OwnerPartnerId.Value))))
                return Forbid();
            redirectWarehouseId = sheet.WarehouseId;
            redirectCountDate = sheet.CountDate;
            if (sheet.Status is not (StockCountStatusEnum.Draft or StockCountStatusEnum.Counted))
            {
                TempData["Error"] = "Phiếu kiểm kê chưa ở trạng thái sẵn sàng duyệt hoặc đã được xử lý.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }
            if (sheet.GeneratedAdjustmentVoucherId.HasValue)
            {
                TempData["Error"] = "Phiếu kiểm kê này đã có phiếu điều chỉnh.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }
            if (string.Equals(sheet.CreatedBy, approver, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Người tạo phiếu kiểm kê không được tự duyệt. Vui lòng nhờ Manager/Admin khác duyệt.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }

            var sheetLines = await _db.StockCountLines
                .Where(l => l.StockCountSheetId == sheet.StockCountSheetId)
                .ToListAsync();
            if (sheetLines.Count == 0)
            {
                TempData["Error"] = "Phiếu kiểm kê không có dòng hàng để duyệt.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }

            var approverAlsoCounted = sheetLines.Any(line =>
                !string.IsNullOrWhiteSpace(line.CountedBy)
                && string.Equals(line.CountedBy.Trim(), approver.Trim(), StringComparison.OrdinalIgnoreCase));
            if (approverAlsoCounted)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    TableName = nameof(StockCountSheet),
                    RecordId = sheet.StockCountSheetId.ToString(),
                    ActionType = "SOD_BLOCK",
                    OldValue = sheet.Status.ToString(),
                    NewValue = "COUNTED_BY_EQUALS_APPROVER",
                    ChangedBy = approver,
                    ChangedAt = VietnamNow,
                    AppModule = "StockCountApproval"
                });
                await _unitOfWork.SaveChangesAsync();
                if (startedTransaction)
                    await _unitOfWork.CommitAsync();
                TempData["Error"] = "Người trực tiếp kiểm đếm không được tự duyệt kết quả của mình. Vui lòng chuyển phiếu cho Manager/Admin khác duyệt.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }

            var countScopes = sheetLines
                .Select(line => new
                {
                    line.OwnerPartnerId,
                    line.ItemId,
                    line.LocationId,
                    line.LotNumber,
                    ExpiryDate = line.ExpiryDate.HasValue ? line.ExpiryDate.Value.Date : (DateTime?)null
                })
                .Distinct()
                .ToList();
            var duplicatedApproved = false;
            foreach (var scope in countScopes)
            {
                duplicatedApproved = await _db.StockCountLines
                    .AsNoTracking()
                    .AnyAsync(line => line.StockCountSheetId != sheet.StockCountSheetId
                        && line.StockCountSheet != null
                        && line.StockCountSheet.WarehouseId == sheet.WarehouseId
                        && line.StockCountSheet.CountDate == sheet.CountDate
                        && line.StockCountSheet.Status == StockCountStatusEnum.Approved
                        && line.ItemId == scope.ItemId
                        && line.LocationId == scope.LocationId
                        && line.OwnerPartnerId == scope.OwnerPartnerId
                        && line.LotNumber == scope.LotNumber
                        && line.ExpiryDate == scope.ExpiryDate);
                if (duplicatedApproved)
                    break;
            }
            if (duplicatedApproved)
            {
                TempData["Error"] = "Đã có phiếu kiểm kê cùng ngày được duyệt cho đúng chủ hàng, vật tư, vị trí, lô và hạn sử dụng này. Không thể duyệt trùng phạm vi kiểm kê.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }

            var lockDate = await _db.WarehousePeriodLocks.AsNoTracking()
                .Where(l => l.WarehouseId == sheet.WarehouseId && l.IsActive)
                .OrderByDescending(l => l.LockDate)
                .Select(l => (DateTime?)l.LockDate)
                .FirstOrDefaultAsync();
            if (lockDate.HasValue && sheet.CountDate.Date <= lockDate.Value.Date)
            {
                TempData["Error"] = $"Kho đã khóa kỳ đến {lockDate.Value:dd/MM/yyyy}. Không thể duyệt kiểm kê ngày {sheet.CountDate:dd/MM/yyyy}.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }

            if (sheetLines.Any(line => !line.CountedQty.HasValue))
            {
                TempData["Error"] = "Phiếu kiểm kê còn dòng chưa ghi nhận số lượng thực tế. Vui lòng hoàn tất việc đếm trước khi gửi duyệt.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }
            var stockLineLocationIds = sheetLines.Select(l => l.LocationId).Distinct().ToList();
            var stockLineItemIds = sheetLines.Select(l => l.ItemId).Distinct().ToList();
            var stockRowsForApproval = stockLineLocationIds.Count == 0 || stockLineItemIds.Count == 0
                ? new List<ItemLocation>()
                : await _db.ItemLocations.AsNoTracking()
                    .Where(il => stockLineLocationIds.Contains(il.LocationId)
                        && stockLineItemIds.Contains(il.ItemId))
                    .ToListAsync();
            foreach (var line in sheetLines)
            {
                var matchingStockRows = stockRowsForApproval
                    .Where(il => il.ItemId == line.ItemId
                        && il.OwnerPartnerId == line.OwnerPartnerId
                        && il.LocationId == line.LocationId
                        && il.LotNumber == line.LotNumber
                        && il.ExpiryDate == line.ExpiryDate)
                    .ToList();
                var currentSystemQty = matchingStockRows.Sum(il => il.Quantity);
                if (Math.Abs(currentSystemQty - line.SystemQty) > 0.0001m)
                {
                    TempData["Error"] = "Tồn hệ thống đã thay đổi từ khi lưu phiếu kiểm kê. Vui lòng tạo lại phiếu kiểm kê trước khi duyệt.";
                    return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
                }

                line.Variance = line.CountedQty.HasValue
                    ? line.CountedQty.Value - line.SystemQty
                    : null;
                if (Math.Abs(line.Variance ?? 0m) > 0.0001m
                    && matchingStockRows.Select(row => row.HoldStatus).Distinct().Count() > 1)
                {
                    TempData["Error"] = "Phạm vi kiểm kê có nhiều trạng thái tồn. Hãy tách phiếu theo trạng thái tồn trước khi duyệt chênh lệch để tránh điều chỉnh nhầm lớp tồn.";
                    return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
                }
            }

            var diffLines = sheetLines
                .Where(l => l.Variance != null && l.Variance != 0)
                .ToList();
            if (diffLines.Count > 0)
            {
                var diffLocationIds = diffLines.Select(x => x.LocationId).Distinct().ToList();
                var validDiffLocationIds = await _db.Locations.AsNoTracking()
                    .Include(l => l.Zone)
                    .Where(l => diffLocationIds.Contains(l.LocationId)
                        && l.Zone != null
                        && l.Zone.WarehouseId == sheet.WarehouseId)
                    .Select(l => l.LocationId)
                    .ToListAsync();
                if (validDiffLocationIds.Count != diffLocationIds.Count)
                    throw WmsExceptions.StockCountLocationMismatch();
            }

            Voucher? createdVoucher = null;
            if (diffLines.Count > 0)
            {
                var adjustmentOwnerIds = diffLines
                    .Select(l => l.OwnerPartnerId)
                    .Distinct()
                    .ToList();
                var items = await _db.Items
                    .Where(i => diffLines.Select(d => d.ItemId).Contains(i.ItemId))
                    .ToDictionaryAsync(i => i.ItemId, i => i);

                var prefix = "PDC";
                var dateStr = VietnamNow.ToString("yyyyMMdd");
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var seq = await _db.Vouchers.CountAsync(v => v.VoucherCode.StartsWith(prefix + "-" + dateStr)) + 1;
                    var random = Random.Shared.Next(0, 100).ToString("D2");
                    var voucherCode = $"{prefix}-{dateStr}-{seq:D5}{random}";
                    var voucher = new Voucher
                    {
                        VoucherCode = voucherCode,
                        VoucherType = VoucherTypeEnum.DieuChinh,
                        VoucherDate = sheet.CountDate.Date,
                        WarehouseId = sheet.WarehouseId,
                        OwnerPartnerId = adjustmentOwnerIds.Count == 1 ? adjustmentOwnerIds.Single() : null,
                        Description = $"Điều chỉnh từ kiểm kê #{sheet.StockCountSheetId}" + (string.IsNullOrWhiteSpace(sheet.Notes) ? "" : $" - {sheet.Notes}"),
                        SourceType = SourceTypeEnum.Manual,
                        CreatedBy = approver,
                        CreatedAt = VietnamNow,
                        IsPosted = true
                    };
                    _db.Vouchers.Add(voucher);
                    try
                    {
                        await _unitOfWork.SaveChangesAsync();
                        createdVoucher = voucher;
                        break;
                    }
                    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
                        || ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                        || ex.InnerException?.Message.Contains("2627", StringComparison.OrdinalIgnoreCase) == true
                        || ex.InnerException?.Message.Contains("2601", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _db.Entry(voucher).State = EntityState.Detached;
                    }
                }
                if (createdVoucher == null)
                    throw WmsExceptions.ReportAdjustmentCodeFailed();

                int lineNo = 0;
                decimal totalAmount = 0;
                foreach (var l in diffLines)
                {
                    if (!items.TryGetValue(l.ItemId, out var item))
                        throw WmsExceptions.ItemDisabled(l.ItemId);
                    lineNo++;
                    decimal baseQty = l.DiffQty ?? 0m;
                    if (baseQty == 0) continue;
                    var abs = Math.Abs(baseQty);
                    var lineAmount = item.UnitCost * abs;
                    var unitPrice = abs > 0 ? lineAmount / abs : 0m;

                    _db.VoucherDetails.Add(new VoucherDetail
                    {
                        VoucherId = createdVoucher.VoucherId,
                        ItemId = l.ItemId,
                        OwnerPartnerId = l.OwnerPartnerId,
                        LocationId = l.LocationId,
                        TransactionQty = abs,
                        TransactionUomId = item.BaseUomId,
                        ConversionRate = 1m,
                        BaseQty = baseQty,
                        UnitPrice = unitPrice,
                        LineAmount = lineAmount,
                        QualityStatus = QualityStatusEnum.Good,
                        ExpiryDate = l.ExpiryDate,
                        LotNumber = l.LotNumber,
                        Notes = $"Kiểm kê #{sheet.StockCountSheetId}: hệ thống {l.SystemQty:N2}, thực tế {l.CountedQty:N2}",
                        LineNumber = lineNo,
                        DefectQty = 0,
                        DefectBaseQty = 0
                    });
                    totalAmount += lineAmount;

                    var itemLoc = await _db.ItemLocations.FirstOrDefaultAsync(il =>
                        il.ItemId == l.ItemId
                        && il.OwnerPartnerId == l.OwnerPartnerId
                        && il.LocationId == l.LocationId
                        && il.LotNumber == l.LotNumber
                        && il.ExpiryDate == l.ExpiryDate);
                    if (itemLoc == null)
                    {
                        itemLoc = new ItemLocation
                        {
                            ItemId = l.ItemId,
                            OwnerPartnerId = l.OwnerPartnerId,
                            LocationId = l.LocationId,
                            LotNumber = l.LotNumber,
                            ExpiryDate = l.ExpiryDate,
                            Quantity = 0,
                            UpdatedAt = VietnamNow
                        };
                        _db.ItemLocations.Add(itemLoc);
                    }
                    itemLoc.Quantity += baseQty;
                    if (itemLoc.Quantity < 0)
                        throw WmsExceptions.StockAdjustmentMakesNegativeLocation(item.ItemCode);

                    item.CurrentStock += baseQty;
                    if (item.CurrentStock < 0)
                        throw WmsExceptions.StockAdjustmentMakesNegativeItem(item.ItemCode);
                    item.TotalStockValue = item.CurrentStock * item.UnitCost;
                    item.UpdatedAt = VietnamNow;
                }

                createdVoucher.TotalLines = lineNo;
                createdVoucher.TotalAmount = totalAmount;
                sheet.GeneratedAdjustmentVoucherId = createdVoucher.VoucherId;
            }

            sheet.Status = StockCountStatusEnum.Approved;
            sheet.ApprovedBy = approver;
            sheet.ApprovedAt = VietnamNow;
            sheet.ApprovalReason = approvalReason;
            sheet.CompletedAt = VietnamNow;
            foreach (var line in sheetLines)
                line.Status = 2;

            await _inventoryRiskRecommendationService.SyncSheetStateAsync(
                sheet.StockCountSheetId,
                sheet.Status,
                approver,
                "STOCK_COUNT_RECONCILED");
            await _unitOfWork.SaveChangesAsync();
            await _cycleCountPlanningService.CompleteApprovedSheetAsync(sheet.StockCountSheetId);

            // P0-03: Sync CurrentStock from ItemLocation source of truth
            if (diffLines.Count > 0)
            {
                var stockCountAffectedItemIds = diffLines.Select(l => l.ItemId).Distinct();
                await _inventoryBalanceService.SyncCurrentStockAsync(stockCountAffectedItemIds);
            }

            await _unitOfWork.SaveChangesAsync();
            if (startedTransaction)
                await _unitOfWork.CommitAsync();

            if (sheet.GeneratedAdjustmentVoucherId.HasValue)
            {
                TempData["Success"] = $"Đã duyệt phiếu kiểm kê #{sheet.StockCountSheetId} và sinh phiếu điều chỉnh.";
                return RedirectToAction("Details", "Vouchers", new { id = sheet.GeneratedAdjustmentVoucherId.Value });
            }

            TempData["Success"] = $"Đã duyệt phiếu kiểm kê #{sheet.StockCountSheetId}. Không có chênh lệch.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
            _logger.LogWarning(ex, "Concurrency conflict when approving stock count. SheetId={SheetId}, Actor={Actor}", id, User.Identity?.Name);
            TempData["Error"] = "Dữ liệu kiểm kê đã thay đổi bởi phiên khác. Vui lòng tải lại và thử duyệt lại.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = redirectWarehouseId, countDate = redirectCountDate });
        }
        catch (Exception ex)
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Stock count approval failed. SheetId={SheetId}, Actor={Actor}", id, User.Identity?.Name);
            TempData["Error"] = UserSafeError.WithPrefix(ex, "Lỗi duyệt phiếu kiểm kê", "Không thể duyệt phiếu kiểm kê lúc này. Vui lòng thử lại.");
            return RedirectToAction(nameof(StockCount), new { warehouseId = redirectWarehouseId, countDate = redirectCountDate });
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
        }
    }


    [Authorize(Roles = "Admin")]
    [Authorize(Policy = WmsPermissions.StockCountUnlock)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StockCountUnlockApproved(long id, string? unlockReason)
    {
        if (string.IsNullOrWhiteSpace(unlockReason))
        {
            TempData["Error"] = "Vui lòng nhập lý do mở khóa.";
            return RedirectToAction(nameof(StockCount));
        }
        unlockReason = unlockReason.Trim();
        if (unlockReason.Length > 500)
            unlockReason = unlockReason[..500];
        var actor = User.Identity?.Name ?? "system";

        var sheet = await _db.StockCountSheets
            .FirstOrDefaultAsync(s => s.StockCountSheetId == id);
        if (sheet == null)
        {
            TempData["Error"] = "Không tìm thấy phiếu kiểm kê.";
            return RedirectToAction(nameof(StockCount));
        }
        if (sheet.Status != StockCountStatusEnum.Approved)
        {
            TempData["Error"] = "Chỉ phiếu đã duyệt mới được mở khóa.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
        }
        if (!string.IsNullOrWhiteSpace(sheet.ApprovedBy)
            && string.Equals(sheet.ApprovedBy, actor, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Người duyệt phiếu kiểm kê không được tự mở khóa. Vui lòng chuyển Admin khác xử lý.";
            return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
        }

        if (sheet.GeneratedAdjustmentVoucherId.HasValue)
        {
            var voucher = await _db.Vouchers
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.VoucherId == sheet.GeneratedAdjustmentVoucherId.Value);
            if (voucher != null && !voucher.IsCancelled)
            {
                TempData["Error"] = "Phiếu điều chỉnh phát sinh từ kiểm kê chưa hủy. Vui lòng hủy phiếu điều chỉnh trước khi mở khóa kiểm kê.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }

            var hasChildren = await _db.Vouchers.AsNoTracking()
                .AnyAsync(v => v.ParentVoucherId == sheet.GeneratedAdjustmentVoucherId.Value && !v.IsCancelled);
            if (hasChildren)
            {
                TempData["Error"] = "Phiếu điều chỉnh đã được tham chiếu bởi nghiệp vụ khác. Không thể mở khóa.";
                return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
            }
        }

        sheet.Status = StockCountStatusEnum.Counted;
        sheet.ApprovedBy = null;
        sheet.ApprovedAt = null;
        sheet.ApprovalReason = null;
        sheet.CompletedAt = null;
        sheet.GeneratedAdjustmentVoucherId = null;
        sheet.UnlockedBy = actor;
        sheet.UnlockedAt = VietnamNow;
        sheet.UnlockReason = unlockReason;
        await _inventoryRiskRecommendationService.SyncSheetStateAsync(
            sheet.StockCountSheetId,
            sheet.Status,
            actor,
            "STOCK_COUNT_APPROVAL_UNLOCKED");
        await _unitOfWork.SaveChangesAsync();

        TempData["Success"] = $"Đã mở khóa phiếu kiểm kê #{sheet.StockCountSheetId}.";
        return RedirectToAction(nameof(StockCount), new { warehouseId = sheet.WarehouseId, countDate = sheet.CountDate });
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [HttpGet]
    public async Task<IActionResult> PeriodLocks()
    {
        var scopedWh = GetScopedWarehouseId();
        var whQuery = _db.Warehouses.Where(w => w.IsActive);
        if (scopedWh.HasValue) whQuery = whQuery.Where(w => w.WarehouseId == scopedWh.Value);

        ViewBag.Warehouses = await whQuery.OrderBy(w => w.WarehouseCode).ToListAsync();
        var locksQuery = _db.WarehousePeriodLocks
            .Include(l => l.Warehouse)
            .AsQueryable();
        if (scopedWh.HasValue) locksQuery = locksQuery.Where(l => l.WarehouseId == scopedWh.Value);

        var locks = await locksQuery
            .OrderByDescending(l => l.IsActive)
            .ThenByDescending(l => l.LockDate)
            .Take(200)
            .ToListAsync();
        return View(locks);
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPeriodLock(int warehouseId, DateTime lockDate, string? reason)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue && warehouseId != scopedWh.Value) return Forbid();

        if (lockDate == default)
        {
            TempData["Error"] = "Vui lòng chọn ngày khóa kỳ hợp lệ.";
            return RedirectToAction(nameof(PeriodLocks));
        }

        var wh = await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId == warehouseId && w.IsActive);
        if (wh == null)
        {
            TempData["Error"] = "Kho không hợp lệ.";
            return RedirectToAction(nameof(PeriodLocks));
        }

        var normalizedDate = lockDate.Date;
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason?.Length > 300)
            normalizedReason = normalizedReason[..300];
        var actor = User.Identity?.Name ?? "system";
        var changedAt = VietnamNow;
        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var history = await _db.WarehousePeriodLocks
                .Where(row => row.WarehouseId == warehouseId)
                .OrderBy(row => row.WarehousePeriodLockId)
                .ToListAsync();
            var target = history.FirstOrDefault(row => row.LockDate.Date == normalizedDate);
            var targetWasActive = target?.IsActive == true;
            var targetOldValue = target == null
                ? null
                : $"LockDate={target.LockDate:yyyy-MM-dd};IsActive={target.IsActive};Reason={target.Reason}";

            var supersededRows = history
                .Where(row => row.IsActive && !ReferenceEquals(row, target))
                .ToList();
            foreach (var superseded in supersededRows)
            {
                superseded.IsActive = false;
                superseded.UnlockedAt = changedAt;
                superseded.UnlockedBy = actor;
            }

            string targetAction;
            if (target == null)
            {
                target = new WarehousePeriodLock
                {
                    WarehouseId = warehouseId,
                    LockDate = normalizedDate,
                    Reason = normalizedReason,
                    LockedBy = actor,
                    LockedAt = changedAt,
                    IsActive = true
                };
                _db.WarehousePeriodLocks.Add(target);
                targetAction = "PERIOD_LOCK_SET";
            }
            else
            {
                target.Reason = normalizedReason;
                target.LockedBy = actor;
                target.LockedAt = changedAt;
                target.IsActive = true;
                target.UnlockedAt = null;
                target.UnlockedBy = null;
                targetAction = targetWasActive ? "PERIOD_LOCK_UPDATE" : "PERIOD_LOCK_REOPEN";
            }

            await _unitOfWork.SaveChangesAsync();

            foreach (var superseded in supersededRows)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    TableName = "WarehousePeriodLocks",
                    RecordId = superseded.WarehousePeriodLockId.ToString(),
                    ActionType = "PERIOD_LOCK_SUPERSEDE",
                    ColumnChanged = nameof(WarehousePeriodLock.IsActive),
                    OldValue = "IsActive=True",
                    NewValue = $"IsActive=False;SupersededBy={normalizedDate:yyyy-MM-dd}",
                    ChangedBy = actor,
                    ChangedAt = changedAt,
                    AppModule = "InventoryControl"
                });
            }

            _db.AuditLogs.Add(new AuditLog
            {
                TableName = "WarehousePeriodLocks",
                RecordId = target.WarehousePeriodLockId.ToString(),
                ActionType = targetAction,
                ColumnChanged = $"{nameof(WarehousePeriodLock.LockDate)},{nameof(WarehousePeriodLock.IsActive)},{nameof(WarehousePeriodLock.Reason)}",
                OldValue = targetOldValue,
                NewValue = $"LockDate={normalizedDate:yyyy-MM-dd};IsActive=True;Reason={normalizedReason}",
                ChangedBy = actor,
                ChangedAt = changedAt,
                AppModule = "InventoryControl"
            });
            await _unitOfWork.SaveChangesAsync();

            if (startedTransaction)
                await _unitOfWork.CommitAsync();

            TempData["Success"] = $"Đã khóa kỳ kho {wh.WarehouseCode} đến ngày {normalizedDate:dd/MM/yyyy}.";
            return RedirectToAction(nameof(PeriodLocks));
        }
        catch (Exception ex)
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Failed to set warehouse period lock. WarehouseId={WarehouseId}, LockDate={LockDate}, Actor={Actor}", warehouseId, normalizedDate, actor);
            TempData["Error"] = "Không thể lưu khóa kỳ lúc này. Dữ liệu chưa bị thay đổi; vui lòng tải lại trang và thử lại.";
            return RedirectToAction(nameof(PeriodLocks));
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
        }
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearPeriodLock(long id)
    {
        var actor = User.Identity?.Name ?? "system";
        var changedAt = VietnamNow;
        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var lockRow = await _db.WarehousePeriodLocks
                .Include(l => l.Warehouse)
                .FirstOrDefaultAsync(l => l.WarehousePeriodLockId == id);
            if (lockRow == null)
                return NotFound();

            var scopedWh = GetScopedWarehouseId();
            if (scopedWh.HasValue && lockRow.WarehouseId != scopedWh.Value)
                return Forbid();

            if (!lockRow.IsActive)
            {
                if (startedTransaction)
                    await _unitOfWork.CommitAsync();
                TempData["Success"] = $"Khóa kỳ của kho {lockRow.Warehouse?.WarehouseCode} đã được mở trước đó.";
                return RedirectToAction(nameof(PeriodLocks));
            }

            lockRow.IsActive = false;
            lockRow.UnlockedAt = changedAt;
            lockRow.UnlockedBy = actor;
            _db.AuditLogs.Add(new AuditLog
            {
                TableName = "WarehousePeriodLocks",
                RecordId = lockRow.WarehousePeriodLockId.ToString(),
                ActionType = "PERIOD_LOCK_CLEAR",
                ColumnChanged = nameof(WarehousePeriodLock.IsActive),
                OldValue = "IsActive=True",
                NewValue = "IsActive=False",
                ChangedBy = actor,
                ChangedAt = changedAt,
                AppModule = "InventoryControl"
            });
            await _unitOfWork.SaveChangesAsync();

            if (startedTransaction)
                await _unitOfWork.CommitAsync();

            TempData["Success"] = $"Đã mở khóa kỳ cho kho {lockRow.Warehouse?.WarehouseCode}.";
            return RedirectToAction(nameof(PeriodLocks));
        }
        catch (Exception ex)
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Failed to clear warehouse period lock. WarehousePeriodLockId={WarehousePeriodLockId}, Actor={Actor}", id, actor);
            TempData["Error"] = "Không thể mở khóa kỳ lúc này. Dữ liệu chưa bị thay đổi; vui lòng tải lại trang và thử lại.";
            return RedirectToAction(nameof(PeriodLocks));
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync();
        }
    }

}
