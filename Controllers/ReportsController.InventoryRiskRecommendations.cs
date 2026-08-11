using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WMS.Authorization;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Controllers;

public partial class ReportsController
{
    [Authorize(Roles = WmsRoles.AdminManagerRoles)]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    [HttpGet]
    public async Task<IActionResult> InventoryRiskRecommendations(
        int? warehouseId,
        int? ownerPartnerId,
        CycleCountRecommendationStateEnum? state,
        string? search,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var scopedWarehouseId = GetScopedWarehouseId();
        if (scopedWarehouseId.HasValue)
            warehouseId = scopedWarehouseId.Value;

        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (ownerPartnerId.HasValue && scopedOwnerIds.Count > 0 && !scopedOwnerIds.Contains(ownerPartnerId.Value))
            return Forbid();

        var model = await _inventoryRiskRecommendationService.BuildPageAsync(
            new InventoryRiskRecommendationQuery
            {
                WarehouseId = warehouseId,
                OwnerPartnerId = ownerPartnerId,
                State = state,
                Search = search,
                Page = page,
                PageSize = pageSize,
                AllowedOwnerPartnerIds = scopedOwnerIds
            },
            cancellationToken);
        await PopulateInventoryRiskRecommendationFiltersAsync(model, scopedOwnerIds, scopedWarehouseId, cancellationToken);
        return View(model);
    }

    [Authorize(Roles = WmsRoles.AdminManagerRoles)]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InventoryRiskGenerateRecommendations(
        int? warehouseId,
        int? ownerPartnerId,
        int? zoneId,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var scopedWarehouseId = GetScopedWarehouseId();
        if (scopedWarehouseId.HasValue)
            warehouseId = scopedWarehouseId.Value;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (ownerPartnerId.HasValue && scopedOwnerIds.Count > 0 && !scopedOwnerIds.Contains(ownerPartnerId.Value))
            return Forbid();
        if (zoneId.HasValue)
        {
            var zoneAllowed = await _db.Zones.AsNoTracking().AnyAsync(zone =>
                zone.ZoneId == zoneId.Value
                && zone.IsActive
                && (!warehouseId.HasValue || zone.WarehouseId == warehouseId.Value), cancellationToken);
            if (!zoneAllowed)
                return Forbid();
        }

        try
        {
            var result = await _inventoryRiskRecommendationService.GenerateFromLatestBatchAsync(
                new InventoryRiskQuery
                {
                    WarehouseId = warehouseId,
                    OwnerPartnerId = ownerPartnerId,
                    ZoneId = zoneId,
                    Search = search,
                    AllowedOwnerPartnerIds = scopedOwnerIds
                },
                User.Identity?.Name ?? "system",
                cancellationToken);
            TempData["Success"] = $"Đã chuẩn bị {result.CreatedCount} đề xuất để quản lý xem xét. Có {result.ExistingCount} đề xuất đã tồn tại và {result.BlockedByDataQualityCount} phạm vi cần xử lý dữ liệu.";
        }
        catch (BusinessRuleException ex)
        {
            _logger.LogWarning(ex, "Inventory-risk recommendation generation rejected. Actor={Actor}", User.Identity?.Name ?? "system");
            TempData["Warning"] = ex.Message;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Inventory-risk recommendation generation failed. Actor={Actor}", User.Identity?.Name ?? "system");
            TempData["Error"] = "Không thể tạo danh sách đề xuất lúc này. Tồn kho và phiếu kiểm kê không bị thay đổi.";
        }

        return RedirectToAction(nameof(InventoryRiskRecommendations), new { warehouseId, ownerPartnerId, search });
    }

    [Authorize(Roles = WmsRoles.AdminManagerRoles)]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InventoryRiskRecommendationDecision(
        InventoryRiskRecommendationDecisionCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _inventoryRiskRecommendationService.DecideAsync(
                command,
                User.Identity?.Name ?? "system",
                GetScopedWarehouseId(),
                GetScopedOwnerPartnerIds(),
                cancellationToken);
            TempData["Success"] = command.Action.Equals("REJECT", StringComparison.OrdinalIgnoreCase)
                ? "Đã từ chối đề xuất và lưu lịch sử quyết định."
                : "Đã duyệt đề xuất. Chưa có phiếu kiểm kê hoặc thay đổi tồn kho nào được tạo.";
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent inventory-risk recommendation decision. RecommendationId={RecommendationId}", command.RecommendationId);
            TempData["Warning"] = "Đề xuất đã được cập nhật ở phiên khác. Vui lòng tải lại trước khi thao tác.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Warning"] = ex.Message;
        }

        return RedirectToAction(nameof(InventoryRiskRecommendations));
    }

    [Authorize(Roles = WmsRoles.AdminManagerRoles)]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InventoryRiskCreateCountSheet(
        long recommendationId,
        Guid concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _inventoryRiskRecommendationService.MaterializeAsync(
                recommendationId,
                concurrencyToken,
                User.Identity?.Name ?? "system",
                GetScopedWarehouseId(),
                GetScopedOwnerPartnerIds(),
                cancellationToken);
            TempData["Success"] = result.WasAlreadyCreated
                ? $"Đề xuất này đã có phiếu kiểm kê {result.StockCountSheetCode}."
                : $"Đã tạo phiếu kiểm kê ẩn số hệ thống {result.StockCountSheetCode}. Chưa điều chỉnh tồn kho.";
            return RedirectToAction(nameof(StockCountEntry), new { id = result.StockCountSheetId });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent count-sheet materialization. RecommendationId={RecommendationId}", recommendationId);
            TempData["Warning"] = "Đề xuất đã được cập nhật ở phiên khác. Vui lòng tải lại trước khi tạo phiếu.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Warning"] = ex.Message;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Count-sheet materialization failed. RecommendationId={RecommendationId}", recommendationId);
            TempData["Error"] = "Không thể tạo phiếu kiểm kê lúc này. Không có thay đổi tồn kho nào được thực hiện.";
        }

        return RedirectToAction(nameof(InventoryRiskRecommendations));
    }

    private async Task PopulateInventoryRiskRecommendationFiltersAsync(
        InventoryRiskRecommendationPageViewModel model,
        IReadOnlyList<int> scopedOwnerIds,
        int? scopedWarehouseId,
        CancellationToken cancellationToken)
    {
        var warehouses = _db.Warehouses.AsNoTracking().Where(warehouse => warehouse.IsActive);
        if (scopedWarehouseId.HasValue)
            warehouses = warehouses.Where(warehouse => warehouse.WarehouseId == scopedWarehouseId.Value);
        model.Warehouses = await warehouses.OrderBy(warehouse => warehouse.WarehouseCode).ToListAsync(cancellationToken);

        var owners = _db.Partners.AsNoTracking().Where(owner => owner.IsActive);
        if (scopedOwnerIds.Count > 0)
            owners = owners.Where(owner => scopedOwnerIds.Contains(owner.PartnerId));
        model.Owners = await owners.OrderBy(owner => owner.PartnerCode).ToListAsync(cancellationToken);
    }
}
