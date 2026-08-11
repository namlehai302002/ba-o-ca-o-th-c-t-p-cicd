using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Controllers;

public partial class ReportsController
{
    [HttpGet]
    [Authorize(Roles = WmsRoles.InventoryRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> InventoryRisk(
        int? warehouseId,
        int? ownerPartnerId,
        int? zoneId,
        InventoryRiskSeverityEnum? severity,
        InventoryRiskDataQualityStatusEnum? dataQualityStatus,
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

        if (zoneId.HasValue)
        {
            var zoneAllowed = await _db.Zones.AsNoTracking().AnyAsync(zone =>
                zone.ZoneId == zoneId.Value
                && (!warehouseId.HasValue || zone.WarehouseId == warehouseId.Value), cancellationToken);
            if (!zoneAllowed)
                return Forbid();
        }

        var query = new InventoryRiskQuery
        {
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerPartnerId,
            ZoneId = zoneId,
            Severity = severity,
            DataQualityStatus = dataQualityStatus,
            Search = search,
            Page = page,
            PageSize = pageSize,
            AllowedOwnerPartnerIds = scopedOwnerIds
        };

        var model = await _inventoryRiskScoringService.BuildPageAsync(query, cancellationToken);
        await PopulateInventoryRiskFiltersAsync(model, scopedOwnerIds, scopedWarehouseId, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = WmsRoles.AdminManagerRoles)]
    [Authorize(Policy = WmsPermissions.StockCountApprove)]
    public async Task<IActionResult> InventoryRiskShadowRefresh(
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
                && (!warehouseId.HasValue || zone.WarehouseId == warehouseId.Value), cancellationToken);
            if (!zoneAllowed)
                return Forbid();
        }

        try
        {
            var result = await _inventoryRiskScoringService.PersistShadowBatchAsync(new InventoryRiskQuery
            {
                WarehouseId = warehouseId,
                OwnerPartnerId = ownerPartnerId,
                ZoneId = zoneId,
                Search = search,
                AllowedOwnerPartnerIds = scopedOwnerIds
            }, User.Identity?.Name ?? "system", cancellationToken);

            TempData["Success"] = $"Đã lưu lần chấm điểm thử nghiệm {result.BatchId:N} với {result.PredictionCount} phạm vi. Không tạo phiếu và không thay đổi tồn kho.";
        }
        catch (BusinessRuleException ex)
        {
            _logger.LogWarning(ex, "Inventory risk shadow refresh rejected. Actor={Actor}", User.Identity?.Name ?? "system");
            TempData["Warning"] = ex.Message;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Inventory risk shadow refresh persistence failed. Actor={Actor}", User.Identity?.Name ?? "system");
            TempData["Error"] = "Không thể lưu lần chấm điểm thử nghiệm lúc này. Tồn kho không bị thay đổi.";
        }

        return RedirectToAction(nameof(InventoryRisk), new { warehouseId, ownerPartnerId, zoneId, search });
    }

    private async Task PopulateInventoryRiskFiltersAsync(
        InventoryRiskPageViewModel model,
        IReadOnlyList<int> scopedOwnerIds,
        int? scopedWarehouseId,
        CancellationToken cancellationToken)
    {
        var warehouseQuery = _db.Warehouses.AsNoTracking().Where(warehouse => warehouse.IsActive);
        if (scopedWarehouseId.HasValue)
            warehouseQuery = warehouseQuery.Where(warehouse => warehouse.WarehouseId == scopedWarehouseId.Value);
        model.Warehouses = await warehouseQuery
            .OrderBy(warehouse => warehouse.WarehouseCode)
            .ToListAsync(cancellationToken);

        var zoneQuery = _db.Zones.AsNoTracking().Where(zone => zone.IsActive);
        if (scopedWarehouseId.HasValue)
            zoneQuery = zoneQuery.Where(zone => zone.WarehouseId == scopedWarehouseId.Value);
        else if (model.WarehouseId.HasValue)
            zoneQuery = zoneQuery.Where(zone => zone.WarehouseId == model.WarehouseId.Value);
        model.Zones = await zoneQuery
            .OrderBy(zone => zone.ZoneCode)
            .Select(zone => new InventoryRiskZoneOption
            {
                ZoneId = zone.ZoneId,
                WarehouseId = zone.WarehouseId,
                Label = zone.ZoneCode + " - " + zone.ZoneName
            })
            .ToListAsync(cancellationToken);

        var ownerIdsQuery = _db.ItemLocations.AsNoTracking()
            .Where(row => row.OwnerPartnerId.HasValue && row.Location != null && row.Location.Zone != null);
        if (scopedWarehouseId.HasValue)
            ownerIdsQuery = ownerIdsQuery.Where(row => row.Location!.Zone.WarehouseId == scopedWarehouseId.Value);
        else if (model.WarehouseId.HasValue)
            ownerIdsQuery = ownerIdsQuery.Where(row => row.Location!.Zone.WarehouseId == model.WarehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            ownerIdsQuery = ownerIdsQuery.Where(row => scopedOwnerIds.Contains(row.OwnerPartnerId!.Value));
        var visibleOwnerIds = await ownerIdsQuery
            .Select(row => row.OwnerPartnerId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        model.Owners = await _db.Partners.AsNoTracking()
            .Where(partner => partner.IsActive && visibleOwnerIds.Contains(partner.PartnerId))
            .OrderBy(partner => partner.PartnerCode)
            .ToListAsync(cancellationToken);
    }
}
