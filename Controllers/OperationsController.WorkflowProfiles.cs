using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Controllers;

public partial class OperationsController
{
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> WorkflowProfiles(int? warehouseId = null)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;

        var allowedOwnerIds = await _tenantScopeService.GetAllowedOwnerIdsAsync(User);
        var query = _db.WarehouseWorkflowProfiles
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.OwnerPartner)
            .AsQueryable();
        if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId.Value);
        if (allowedOwnerIds.Count > 0)
            query = query.Where(x => !x.OwnerPartnerId.HasValue || allowedOwnerIds.Contains(x.OwnerPartnerId.Value));

        var model = new WorkflowProfilesViewModel
        {
            WarehouseId = warehouseId,
            WorkflowScopeMode = "Warehouse",
            Warehouses = await _db.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.WarehouseCode).ToListAsync(),
            Owners = await _tenantScopeService.GetVisibleOwnersAsync(User),
            Profiles = await query.OrderBy(x => x.Warehouse.WarehouseCode).ThenBy(x => x.OwnerPartner != null ? x.OwnerPartner.PartnerCode : "").ThenBy(x => x.ModuleKey).ToListAsync()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> SaveWorkflowProfile(
        int? id,
        int warehouseId,
        string? workflowScopeMode,
        int? ownerPartnerId,
        string moduleKey,
        string profileName,
        bool requireLocationScan = false,
        bool requireItemScan = false,
        bool requireToteScan = false,
        bool requireSerialScan = false,
        bool requireQc = false,
        bool requireApproval = false,
        bool requirePacking = false,
        bool isActive = true)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        workflowScopeMode = NormalizeWorkflowScopeMode(workflowScopeMode, ownerPartnerId);
        if (workflowScopeMode == "Warehouse")
            ownerPartnerId = null;

        if (string.IsNullOrWhiteSpace(moduleKey) || string.IsNullOrWhiteSpace(profileName))
        {
            TempData["Error"] = "Vui lòng nhập phân hệ và tên quy tắc vận hành.";
            return RedirectToAction(nameof(WorkflowProfiles), new { warehouseId });
        }

        if (workflowScopeMode == "ThreePl" && !ownerPartnerId.HasValue)
        {
            TempData["Error"] = "Vui lòng chọn chủ hàng cho phạm vi khách hàng thuê kho.";
            return RedirectToAction(nameof(WorkflowProfiles), new { warehouseId });
        }

        if (ownerPartnerId.HasValue)
        {
            try
            {
                await _tenantScopeService.EnsureCanAccessOwnerAsync(ownerPartnerId.Value, User);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            var ownerOk = await _db.Partners.AsNoTracking()
                .AnyAsync(p => p.PartnerId == ownerPartnerId.Value && p.IsThreePlClient && p.IsActive);
            if (!ownerOk)
            {
                TempData["Error"] = "Chủ hàng không hợp lệ hoặc chưa hoạt động.";
                return RedirectToAction(nameof(WorkflowProfiles), new { warehouseId });
            }
        }

        var normalizedModuleKey = moduleKey.Trim();
        if (!IsAllowedWorkflowModule(normalizedModuleKey))
        {
            TempData["Error"] = "Phân hệ nghiệp vụ không hợp lệ.";
            return RedirectToAction(nameof(WorkflowProfiles), new { warehouseId });
        }

        var profile = id.HasValue
            ? await _db.WarehouseWorkflowProfiles.FirstOrDefaultAsync(x => x.WarehouseWorkflowProfileId == id.Value)
            : await _db.WarehouseWorkflowProfiles.FirstOrDefaultAsync(x => x.WarehouseId == warehouseId && x.OwnerPartnerId == ownerPartnerId && x.ModuleKey == normalizedModuleKey);

        if (profile == null)
        {
            profile = new WarehouseWorkflowProfile { WarehouseId = warehouseId };
            _db.WarehouseWorkflowProfiles.Add(profile);
        }
        else if (scopedWh.HasValue && profile.WarehouseId != scopedWh.Value)
        {
            return Forbid();
        }

        profile.WarehouseId = warehouseId;
        profile.OwnerPartnerId = ownerPartnerId;
        profile.ModuleKey = normalizedModuleKey;
        profile.ProfileName = profileName.Trim();
        profile.RequireLocationScan = requireLocationScan;
        profile.RequireItemScan = requireItemScan;
        profile.RequireToteScan = requireToteScan;
        profile.RequireSerialScan = requireSerialScan;
        profile.RequireQc = requireQc;
        profile.RequireApproval = requireApproval;
        profile.RequirePacking = requirePacking;
        profile.IsActive = isActive;
        profile.UpdatedBy = User.Identity?.Name ?? "system";
        profile.UpdatedAt = VietnamNow;

        await _unitOfWork.SaveChangesAsync();
        TempData["Success"] = "Đã lưu quy tắc vận hành kho.";
        return RedirectToAction(nameof(WorkflowProfiles), new { warehouseId });
    }

    private static string NormalizeWorkflowScopeMode(string? mode, int? ownerPartnerId)
    {
        var normalized = (mode ?? "").Trim();
        if (normalized.Equals("ThreePl", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("3PL", StringComparison.OrdinalIgnoreCase))
        {
            return "ThreePl";
        }

        if (normalized.Equals("Warehouse", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Internal", StringComparison.OrdinalIgnoreCase))
        {
            return "Warehouse";
        }

        return ownerPartnerId.HasValue ? "ThreePl" : "Warehouse";
    }

    private static bool IsAllowedWorkflowModule(string moduleKey)
    {
        var normalized = moduleKey.Trim().ToLowerInvariant();
        return normalized is "inbound" or "outbound" or "picking" or "movement" or "shipping" or "stockcount" or "quality" or "packing";
    }
}
