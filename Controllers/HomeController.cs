using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Models;
using WMS.Authorization;
using WMS.ViewModels;
using WMS.Common;
using WMS.Services;

namespace WMS.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryBalanceService _inventoryBalanceService;
    private readonly IRoleWorkspaceService _roleWorkspaceService;
    private readonly IDashboardCommandCenterService _dashboardCommandCenterService;

    private static DateTime VietnamNow => VietnamTime.Now;

    public HomeController(
        AppDbContext db,
        IInventoryBalanceService inventoryBalanceService,
        IRoleWorkspaceService? roleWorkspaceService = null,
        IDashboardCommandCenterService? dashboardCommandCenterService = null)
    {
        _db = db;
        _inventoryBalanceService = inventoryBalanceService;
        _roleWorkspaceService = roleWorkspaceService ?? new RoleWorkspaceService();
        _dashboardCommandCenterService = dashboardCommandCenterService ?? new DashboardCommandCenterService(db);
    }

    private bool CanSeeFinancial()
        => User.IsInRole(WmsRoles.Admin)
            || string.Equals(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, WmsRoles.Admin, StringComparison.OrdinalIgnoreCase)
            || User.Claims.Any(c =>
                c.Type == PermissionClaimTypes.Permission &&
                string.Equals(c.Value, WmsPermissions.ReportViewFinancial, StringComparison.Ordinal));

    private int? GetScopedWarehouseId()
    {
        var warehouseClaim = User.FindFirst("WarehouseId")?.Value;
        return int.TryParse(warehouseClaim, out var warehouseId) ? warehouseId : null;
    }

    private IReadOnlyList<int> GetScopedOwnerPartnerIds()
        => User.FindAll(TenantClaimTypes.OwnerPartnerId)
            .Select(c => int.TryParse(c.Value, out var ownerPartnerId) ? ownerPartnerId : 0)
            .Where(ownerPartnerId => ownerPartnerId > 0)
            .Distinct()
            .ToList();

    public async Task<IActionResult> Index(
        int? warehouseId = null,
        string? workState = null,
        string? severity = null,
        string? assignee = null,
        CancellationToken cancellationToken = default)
    {
        var canSeeFinancial = CanSeeFinancial();
        ViewBag.CanSeeFinancial = canSeeFinancial;
        ViewBag.RoleWorkspace = _roleWorkspaceService.Build(User);

        var now = VietnamNow;
        var today = now.Date;
        var claimScopedWarehouseId = GetScopedWarehouseId();
        var scopedWh = claimScopedWarehouseId ?? warehouseId;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        var vm = scopedWh.HasValue
            ? await BuildScopedDashboardAsync(scopedWh.Value, scopedOwnerIds, today, canSeeFinancial)
            : await BuildEnterpriseDashboardAsync(scopedOwnerIds, today, canSeeFinancial);

        var currentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        vm.CommandCenter = await _dashboardCommandCenterService.BuildAsync(
            new DashboardCommandCenterRequest
            {
                Role = currentRole,
                WarehouseId = scopedWh,
                OwnerPartnerIds = scopedOwnerIds,
                Now = now,
                WorkState = workState,
                Severity = severity,
                Assignee = assignee
            },
            cancellationToken);

        ViewBag.WarehouseFilterLocked = claimScopedWarehouseId.HasValue;

        return View(vm);
    }

    private async Task<DashboardViewModel> BuildScopedDashboardAsync(
        int warehouseId,
        IReadOnlyCollection<int> scopedOwnerIds,
        DateTime today,
        bool canSeeFinancial)
    {
        var hasOwnerScope = scopedOwnerIds.Count > 0;
        var stockMap = await _inventoryBalanceService.GetStockByItemAsync(
            warehouseId,
            ownerPartnerIds: hasOwnerScope ? scopedOwnerIds : null);

        var defaultLocationItemIds = await _db.Items.AsNoTracking()
            .Where(i => i.IsActive
                && i.DefaultLocationId.HasValue
                && i.DefaultLocation != null
                && i.DefaultLocation.Zone != null
                && i.DefaultLocation.Zone.WarehouseId == warehouseId
                && (!hasOwnerScope || (i.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(i.OwnerPartnerId.Value))))
            .Select(i => i.ItemId)
            .ToListAsync();

        var warehouseItemIds = stockMap.Keys
            .Union(defaultLocationItemIds)
            .Distinct()
            .ToList();

        var warehouseItems = warehouseItemIds.Count == 0
            ? new List<Item>()
            : await _db.Items.AsNoTracking()
                .Include(i => i.Category)
                .Include(i => i.BaseUom)
                .Where(i => i.IsActive && warehouseItemIds.Contains(i.ItemId))
                .OrderBy(i => i.ItemCode)
                .ToListAsync();

        _inventoryBalanceService.ApplyStockBalances(warehouseItems, stockMap);

        var voucherQuery = _db.Vouchers.Where(v => v.WarehouseId == warehouseId);
        if (hasOwnerScope)
            voucherQuery = voucherQuery.Where(v => v.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(v.OwnerPartnerId.Value));

        var vm = new DashboardViewModel
        {
            TotalItems = warehouseItems.Count,
            TotalWarehouses = 1,
            TotalPartners = await voucherQuery.AsNoTracking()
                .Where(v => v.PartnerId.HasValue && !v.IsCancelled)
                .Select(v => v.PartnerId!.Value)
                .Distinct()
                .CountAsync(),
            TodayVouchers = await voucherQuery.CountAsync(v => v.VoucherDate == today && !v.IsCancelled),
            TotalStockValue = canSeeFinancial ? warehouseItems.Sum(i => i.TotalStockValue) : 0m,
            LowStockCount = warehouseItems.Count(i => i.MinThreshold > 0 && i.CurrentStock > 0 && i.CurrentStock <= i.MinThreshold),
            OutOfStockCount = warehouseItems.Count(i => i.CurrentStock <= 0),
            OverStockCount = warehouseItems.Count(i => i.MaxThreshold.HasValue && i.CurrentStock >= i.MaxThreshold.Value),
            LowStockItems = warehouseItems
                .Where(i => i.MinThreshold > 0 && i.CurrentStock > 0 && i.CurrentStock <= i.MinThreshold)
                .OrderBy(i => i.CurrentStock)
                .Take(10)
                .ToList(),
            RecentVouchers = await voucherQuery
                .AsNoTracking()
                .Include(v => v.Warehouse)
                .Include(v => v.Partner)
                .OrderByDescending(v => v.CreatedAt)
                .Take(10)
                .ToListAsync(),
            UnresolvedAlerts = warehouseItemIds.Count == 0
                ? new List<StockAlert>()
                : await _db.StockAlerts
                    .AsNoTracking()
                    .Include(a => a.Item)
                    .Where(a => !a.IsResolved && warehouseItemIds.Contains(a.ItemId))
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(10)
                    .ToListAsync()
        };

        var waveQuery = _db.Waves.Where(w => w.WarehouseId == warehouseId);
        if (hasOwnerScope)
            waveQuery = waveQuery.Where(w => w.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(w.OwnerPartnerId.Value));

        var pickTaskQuery = _db.PickTasks.Where(t =>
            (t.Wave != null && t.Wave.WarehouseId == warehouseId)
            || (t.Wave == null && t.Voucher != null && t.Voucher.WarehouseId == warehouseId));
        if (hasOwnerScope)
        {
            pickTaskQuery = pickTaskQuery.Where(t =>
                (t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value))
                || (!t.OwnerPartnerId.HasValue && t.Wave != null && t.Wave.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.Wave.OwnerPartnerId.Value))
                || (!t.OwnerPartnerId.HasValue && t.Voucher != null && t.Voucher.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.Voucher.OwnerPartnerId.Value)));
        }

        var movementTaskQuery = _db.MovementTasks.Where(t => t.WarehouseId == warehouseId);
        if (hasOwnerScope)
            movementTaskQuery = movementTaskQuery.Where(t => t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value));

        vm.OpenWaves = await waveQuery.CountAsync(w =>
            w.Status == WaveStatusEnum.Released || w.Status == WaveStatusEnum.InProgress);
        vm.OpenPickTasks = await pickTaskQuery.CountAsync(t =>
            t.Status == PickTaskStatusEnum.Pending || t.Status == PickTaskStatusEnum.Assigned || t.Status == PickTaskStatusEnum.InProgress);
        vm.ShortPickTasks = await pickTaskQuery.CountAsync(t => t.Status == PickTaskStatusEnum.Short);
        vm.OpenMovementTasks = await movementTaskQuery.CountAsync(t =>
            t.Status == MovementTaskStatusEnum.Pending
            || t.Status == MovementTaskStatusEnum.Assigned
            || t.Status == MovementTaskStatusEnum.InProgress);

        var activeReservationQuery = _db.StockReservations
            .AsNoTracking()
            .Where(r => r.Status == ReservationStatusEnum.Active
                && r.Voucher != null
                && r.Voucher.WarehouseId == warehouseId);
        if (hasOwnerScope)
            activeReservationQuery = activeReservationQuery.Where(r => r.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(r.OwnerPartnerId.Value));

        var activeReservations = await activeReservationQuery
            .Select(r => new { r.ReservedQty, r.ConsumedQty })
            .ToListAsync();
        var totalReserved = activeReservations.Sum(x => x.ReservedQty);
        var totalConsumed = activeReservations.Sum(x => x.ConsumedQty);
        vm.ReservationFillRate = totalReserved > 0 ? (totalConsumed / totalReserved) * 100m : 0m;

        vm.PendingOutboundVouchers = await voucherQuery.CountAsync(v =>
            !v.IsCancelled && !v.IsPosted
            && (v.VoucherType == VoucherTypeEnum.XuatKho || v.VoucherType == VoucherTypeEnum.TraNCC
                || v.VoucherType == VoucherTypeEnum.ChuyenKho || v.VoucherType == VoucherTypeEnum.XuatSanXuat));

        vm.PendingInboundApprovals = await voucherQuery.CountAsync(v =>
            !v.IsCancelled
            && (v.VoucherType == VoucherTypeEnum.NhapKho || v.VoucherType == VoucherTypeEnum.KhachTra || v.VoucherType == VoucherTypeEnum.NhapThanhPham)
            && v.InboundStatus == InboundStatusEnum.PendingApproval);

        vm.StalePickTasks = await pickTaskQuery.CountAsync(t =>
            (t.Status == PickTaskStatusEnum.Pending || t.Status == PickTaskStatusEnum.Assigned)
            && t.DueAt.HasValue && t.DueAt.Value < VietnamNow);

        vm.UnassignedPickTasks = await pickTaskQuery.CountAsync(t =>
            t.Status == PickTaskStatusEnum.Pending
            && string.IsNullOrEmpty(t.AssignedTo));

        vm.OverdueVouchers = await voucherQuery.CountAsync(v =>
            !v.IsCancelled && !v.IsPosted
            && v.RequestedDeliveryDate.HasValue
            && v.RequestedDeliveryDate.Value < today);

        var thirtyDaysAgo = today.AddDays(-30);
        var vouchersByType = await voucherQuery
            .AsNoTracking()
            .Where(v => v.VoucherDate >= thirtyDaysAgo && !v.IsCancelled)
            .GroupBy(v => v.VoucherType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var typeNames = new Dictionary<VoucherTypeEnum, string>
        {
            { VoucherTypeEnum.NhapKho, "Nhập kho" },
            { VoucherTypeEnum.XuatKho, "Xuất kho" },
            { VoucherTypeEnum.TraNCC, "Trả NCC" },
            { VoucherTypeEnum.KhachTra, "Khách trả" },
            { VoucherTypeEnum.DieuChinh, "Điều chỉnh" },
            { VoucherTypeEnum.ChuyenKho, "Chuyển kho" },
            { VoucherTypeEnum.NhapThanhPham, "Nhập TP" },
            { VoucherTypeEnum.XuatSanXuat, "Xuất SX" }
        };
        vm.VouchersByType = vouchersByType.ToDictionary(
            v => typeNames.GetValueOrDefault(v.Type, "Khác"),
            v => v.Count);

        if (canSeeFinancial)
        {
            vm.StockByCategory = warehouseItems
                .GroupBy(i => i.Category?.CategoryName ?? "Chưa phân loại")
                .ToDictionary(g => g.Key, g => g.Sum(i => i.TotalStockValue));
        }
        else
        {
            vm.StockByCategory = warehouseItems
                .GroupBy(i => i.Category?.CategoryName ?? "Chưa phân loại")
                .ToDictionary(g => g.Key, _ => 0m);
        }

        return vm;
    }

    private async Task<DashboardViewModel> BuildEnterpriseDashboardAsync(
        IReadOnlyCollection<int> scopedOwnerIds,
        DateTime today,
        bool canSeeFinancial)
    {
        var hasOwnerScope = scopedOwnerIds.Count > 0;
        var stockMap = await _inventoryBalanceService.GetStockByItemAsync(
            ownerPartnerIds: hasOwnerScope ? scopedOwnerIds : null);
        var activeItemQuery = _db.Items.AsNoTracking()
            .Include(i => i.Category)
            .Include(i => i.BaseUom)
            .Where(i => i.IsActive);
        if (hasOwnerScope)
        {
            var scopedStockItemIds = stockMap.Keys.ToList();
            activeItemQuery = activeItemQuery.Where(i =>
                (i.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(i.OwnerPartnerId.Value))
                || scopedStockItemIds.Contains(i.ItemId));
        }

        var activeItems = await activeItemQuery
            .OrderBy(i => i.ItemCode)
            .ToListAsync();
        _inventoryBalanceService.ApplyStockBalances(activeItems, stockMap);
        var activeItemIds = activeItems.Select(i => i.ItemId).ToList();

        var voucherQuery = _db.Vouchers.AsQueryable();
        if (hasOwnerScope)
            voucherQuery = voucherQuery.Where(v => v.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(v.OwnerPartnerId.Value));

        var vm = new DashboardViewModel
        {
            TotalItems = activeItems.Count,
            TotalWarehouses = hasOwnerScope
                ? await _db.Warehouses.CountAsync(w => w.IsActive
                    && (_db.ItemLocations.Any(il => il.OwnerPartnerId.HasValue
                            && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)
                            && il.Location != null
                            && il.Location.Zone != null
                            && il.Location.Zone.WarehouseId == w.WarehouseId)
                        || _db.Vouchers.Any(v => v.WarehouseId == w.WarehouseId
                            && v.OwnerPartnerId.HasValue
                            && scopedOwnerIds.Contains(v.OwnerPartnerId.Value))))
                : await _db.Warehouses.CountAsync(w => w.IsActive),
            TotalPartners = await voucherQuery.AsNoTracking()
                .Where(v => v.PartnerId.HasValue && !v.IsCancelled)
                .Select(v => v.PartnerId!.Value)
                .Distinct()
                .CountAsync(),
            TodayVouchers = await voucherQuery.CountAsync(v => v.VoucherDate == today && !v.IsCancelled),
            TotalStockValue = canSeeFinancial ? activeItems.Sum(i => i.TotalStockValue) : 0m,
            LowStockCount = activeItems.Count(i => i.MinThreshold > 0 && i.CurrentStock <= i.MinThreshold && i.CurrentStock > 0),
            OutOfStockCount = activeItems.Count(i => i.CurrentStock <= 0),
            OverStockCount = activeItems.Count(i => i.MaxThreshold.HasValue && i.CurrentStock >= i.MaxThreshold.Value),
            LowStockItems = activeItems
                .Where(i => i.MinThreshold > 0 && i.CurrentStock > 0 && i.CurrentStock <= i.MinThreshold)
                .OrderBy(i => i.CurrentStock)
                .Take(10).ToList(),
            RecentVouchers = await voucherQuery
                .Include(v => v.Warehouse).Include(v => v.Partner)
                .OrderByDescending(v => v.CreatedAt)
                .Take(10).ToListAsync(),
            UnresolvedAlerts = await _db.StockAlerts
                .Include(a => a.Item)
                .Where(a => !a.IsResolved && (!hasOwnerScope || activeItemIds.Contains(a.ItemId)))
                .OrderByDescending(a => a.CreatedAt)
                .Take(10).ToListAsync()
        };

        var waveQuery = _db.Waves.AsQueryable();
        var pickTaskQuery = _db.PickTasks.AsQueryable();
        var movementTaskQuery = _db.MovementTasks.AsQueryable();
        var activeReservationQuery = _db.StockReservations.Where(r => r.Status == ReservationStatusEnum.Active);
        if (hasOwnerScope)
        {
            waveQuery = waveQuery.Where(w => w.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(w.OwnerPartnerId.Value));
            pickTaskQuery = pickTaskQuery.Where(t =>
                (t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value))
                || (!t.OwnerPartnerId.HasValue && t.Wave != null && t.Wave.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.Wave.OwnerPartnerId.Value))
                || (!t.OwnerPartnerId.HasValue && t.Voucher != null && t.Voucher.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.Voucher.OwnerPartnerId.Value)));
            movementTaskQuery = movementTaskQuery.Where(t => t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value));
            activeReservationQuery = activeReservationQuery.Where(r => r.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(r.OwnerPartnerId.Value));
        }

        vm.OpenWaves = await waveQuery.CountAsync(w => w.Status == WaveStatusEnum.Released || w.Status == WaveStatusEnum.InProgress);
        vm.OpenPickTasks = await pickTaskQuery.CountAsync(t => t.Status == PickTaskStatusEnum.Pending || t.Status == PickTaskStatusEnum.Assigned || t.Status == PickTaskStatusEnum.InProgress);
        vm.ShortPickTasks = await pickTaskQuery.CountAsync(t => t.Status == PickTaskStatusEnum.Short);
        vm.OpenMovementTasks = await movementTaskQuery.CountAsync(t =>
            t.Status == MovementTaskStatusEnum.Pending
            || t.Status == MovementTaskStatusEnum.Assigned
            || t.Status == MovementTaskStatusEnum.InProgress);
        var activeReservations = await activeReservationQuery
            .Select(r => new { r.ReservedQty, r.ConsumedQty })
            .ToListAsync();
        var totalReserved = activeReservations.Sum(x => x.ReservedQty);
        var totalConsumed = activeReservations.Sum(x => x.ConsumedQty);
        vm.ReservationFillRate = totalReserved > 0 ? (totalConsumed / totalReserved) * 100m : 0m;

        vm.PendingOutboundVouchers = await voucherQuery.CountAsync(v =>
            !v.IsCancelled && !v.IsPosted
            && (v.VoucherType == VoucherTypeEnum.XuatKho || v.VoucherType == VoucherTypeEnum.TraNCC
                || v.VoucherType == VoucherTypeEnum.ChuyenKho || v.VoucherType == VoucherTypeEnum.XuatSanXuat));

        vm.PendingInboundApprovals = await voucherQuery.CountAsync(v =>
            !v.IsCancelled
            && (v.VoucherType == VoucherTypeEnum.NhapKho || v.VoucherType == VoucherTypeEnum.KhachTra || v.VoucherType == VoucherTypeEnum.NhapThanhPham)
            && v.InboundStatus == InboundStatusEnum.PendingApproval);

        vm.StalePickTasks = await pickTaskQuery.CountAsync(t =>
            (t.Status == PickTaskStatusEnum.Pending || t.Status == PickTaskStatusEnum.Assigned)
            && t.DueAt.HasValue && t.DueAt.Value < VietnamNow);

        vm.UnassignedPickTasks = await pickTaskQuery.CountAsync(t =>
            (t.Status == PickTaskStatusEnum.Pending)
            && string.IsNullOrEmpty(t.AssignedTo));

        vm.OverdueVouchers = await voucherQuery.CountAsync(v =>
            !v.IsCancelled && !v.IsPosted
            && v.RequestedDeliveryDate.HasValue
            && v.RequestedDeliveryDate.Value < today);

        var thirtyDaysAgo = today.AddDays(-30);
        var vouchersByType = await voucherQuery
            .Where(v => v.VoucherDate >= thirtyDaysAgo && !v.IsCancelled)
            .GroupBy(v => v.VoucherType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var typeNames = new Dictionary<VoucherTypeEnum, string>
        {
            { VoucherTypeEnum.NhapKho, "Nhập kho" },
            { VoucherTypeEnum.XuatKho, "Xuất kho" },
            { VoucherTypeEnum.TraNCC, "Trả NCC" },
            { VoucherTypeEnum.KhachTra, "Khách trả" },
            { VoucherTypeEnum.DieuChinh, "Điều chỉnh" },
            { VoucherTypeEnum.ChuyenKho, "Chuyển kho" },
            { VoucherTypeEnum.NhapThanhPham, "Nhập TP" },
            { VoucherTypeEnum.XuatSanXuat, "Xuất SX" }
        };
        vm.VouchersByType = vouchersByType.ToDictionary(
            v => typeNames.GetValueOrDefault(v.Type, "Khác"),
            v => v.Count);

        if (canSeeFinancial)
        {
            // P0-03: Use already-balanced in-memory items (ApplyStockBalances was called above)
            vm.StockByCategory = activeItems
                .GroupBy(i => i.Category?.CategoryName ?? "Chưa phân loại")
                .ToDictionary(g => g.Key, g => g.Sum(i => i.TotalStockValue));
        }
        else
        {
            var categories = activeItems
                .Select(i => i.Category?.CategoryName ?? "Chưa phân loại")
                .Distinct()
                .ToList();
            vm.StockByCategory = categories.ToDictionary(c => c, _ => 0m);
        }

        return vm;
    }
}
