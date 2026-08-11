using Microsoft.EntityFrameworkCore;
using WMS.Common;
using WMS.Data;
using WMS.Models;

namespace WMS.Services;

public interface IRbacSeedService
{
    Task EnsureSeededAsync(CancellationToken ct = default);
}

public sealed class RbacSeedService : IRbacSeedService
{
    private readonly AppDbContext _db;
    private readonly ILogger<RbacSeedService> _logger;

    public RbacSeedService(AppDbContext db, ILogger<RbacSeedService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsurePermissionsAsync(ct);
            await EnsureRolesAsync(ct);
            await EnsureRolePermissionsAsync(ct);
        }
        catch (Exception ex)
        {
            TryLogSeedWarning(ex);
        }
    }

    private void TryLogSeedWarning(Exception ex)
    {
        try
        {
            _logger.LogWarning(ex, "Không thể đồng bộ RBAC role/permission khi khởi động. Ứng dụng vẫn tiếp tục chạy.");
        }
        catch
        {
            // Windows EventLog or other providers may be unavailable in local/dev hosts.
            // RBAC seeding is idempotent and can run again on the next healthy startup.
        }
    }

    private async Task EnsurePermissionsAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct);
        foreach (var code in WmsPermissions.All)
        {
            if (existing.TryGetValue(code, out var permission))
            {
                if (string.IsNullOrWhiteSpace(permission.Description))
                    permission.Description = code;
                continue;
            }

            _db.Permissions.Add(new Permission
            {
                Code = code,
                Description = code,
                CreatedAt = VietnamTime.Now,
                CreatedBy = "rbac-seed"
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureRolesAsync(CancellationToken ct)
    {
        var roles = await _db.AppRoles.ToDictionaryAsync(r => r.RoleName, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var definition in WmsRoles.Definitions)
        {
            if (roles.TryGetValue(definition.Name, out var role))
            {
                if (string.IsNullOrWhiteSpace(role.Description))
                    role.Description = definition.Description;
                continue;
            }

            _db.AppRoles.Add(new AppRole
            {
                RoleName = definition.Name,
                Description = definition.Description
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureRolePermissionsAsync(CancellationToken ct)
    {
        var permissions = await _db.Permissions.ToDictionaryAsync(p => p.Code, p => p.PermissionId, StringComparer.Ordinal, ct);
        var roles = await _db.AppRoles.ToDictionaryAsync(r => r.RoleName, r => r.RoleId, StringComparer.OrdinalIgnoreCase, ct);
        var existing = await _db.RolePermissions
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(ct);
        var pairs = existing.Select(x => (x.RoleId, x.PermissionId)).ToHashSet();

        void Grant(string roleName, params string[] permissionCodes)
        {
            if (!roles.TryGetValue(roleName, out var roleId))
                return;

            foreach (var code in permissionCodes)
            {
                if (!permissions.TryGetValue(code, out var permissionId))
                    continue;

                if (pairs.Add((roleId, permissionId)))
                {
                    _db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = roleId,
                        PermissionId = permissionId,
                        CreatedAt = VietnamTime.Now
                    });
                }
            }
        }

        Grant(WmsRoles.Admin, WmsPermissions.All);
        Grant(WmsRoles.Manager,
            WmsPermissions.VoucherCreate,
            WmsPermissions.VoucherApproveInbound,
            WmsPermissions.VoucherApproveOutbound,
            WmsPermissions.VoucherCancel,
            WmsPermissions.VoucherPostOutbound,
            WmsPermissions.VoucherReleasePicking,
            WmsPermissions.VoucherConfirmShipping,
            WmsPermissions.QcSubmitInspection,
            WmsPermissions.QcResolveHold,
            WmsPermissions.StockCountApprove,
            WmsPermissions.MasterItemManage,
            WmsPermissions.MasterPartnerManage,
            WmsPermissions.MasterCategoryManage,
            WmsPermissions.MasterUomManage,
            WmsPermissions.WarehouseConfigManage,
            WmsPermissions.ReportView,
            WmsPermissions.ReportViewFinancial,
            WmsPermissions.PickTaskReassign,
            WmsPermissions.TenantScopeManage,
            WmsPermissions.ThreePlBillingManage,
            WmsPermissions.MheManage);

        Grant(WmsRoles.Staff, WmsPermissions.VoucherCreate, WmsPermissions.ReportView);
        Grant(WmsRoles.InboundStaff, WmsPermissions.VoucherCreate, WmsPermissions.QcSubmitInspection, WmsPermissions.ReportView);
        Grant(WmsRoles.OutboundStaff, WmsPermissions.VoucherCreate, WmsPermissions.ReportView);
        Grant(WmsRoles.InventoryStaff, WmsPermissions.VoucherCreate, WmsPermissions.ReportView);
        Grant(WmsRoles.TransportStaff, WmsPermissions.ReportView, WmsPermissions.VoucherConfirmShipping);
        Grant(WmsRoles.ReportViewer, WmsPermissions.ReportView);
        Grant(WmsRoles.Viewer, WmsPermissions.ReportView);

        await _db.SaveChangesAsync(ct);
    }
}
