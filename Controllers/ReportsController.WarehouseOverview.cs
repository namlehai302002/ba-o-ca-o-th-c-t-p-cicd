using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WMS.Authorization;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Controllers;

public partial class ReportsController
{
    private const int WarehouseOverviewMaxDays = 180;

    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> WarehouseOverview(DateTime? dateFrom, DateTime? dateTo, int? warehouseId)
    {
        var model = await BuildWarehouseOverviewModelAsync(dateFrom, dateTo, warehouseId);
        return View(model);
    }

    private async Task<WarehouseOverviewPageViewModel> BuildWarehouseOverviewModelAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        int? warehouseId)
    {
        var today = VietnamNow.Date;
        var from = dateFrom?.Date ?? today.AddDays(-6);
        var to = dateTo?.Date ?? today;
        string? notice = null;

        if (from > to)
        {
            (from, to) = (to, from);
            notice = "Đã tự sắp xếp lại khoảng ngày để Từ ngày không lớn hơn Đến ngày.";
        }

        if ((to - from).TotalDays + 1 > WarehouseOverviewMaxDays)
        {
            from = to.AddDays(-(WarehouseOverviewMaxDays - 1));
            notice = $"Khoảng xem tổng quan được giới hạn tối đa {WarehouseOverviewMaxDays} ngày để bảo đảm tốc độ báo cáo.";
        }

        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue)
            warehouseId = scopedWh.Value;

        var model = new WarehouseOverviewPageViewModel
        {
            DateFrom = from,
            DateTo = to,
            WarehouseId = warehouseId,
            CanSeeFinancial = CanSeeFinancial(),
            Notice = notice
        };

        model.Warehouses = await _db.Warehouses.AsNoTracking()
            .Where(w => w.IsActive)
            .Where(w => !scopedWh.HasValue || w.WarehouseId == scopedWh.Value)
            .OrderBy(w => w.WarehouseCode)
            .ToListAsync();

        var stockRows = await _db.ItemLocations.AsNoTracking()
            .Where(il => il.Item != null
                && il.Item.IsActive
                && il.Location != null
                && il.Location.IsActive
                && il.Location.Zone != null
                && il.Location.Zone.IsActive
                && il.Location.Zone.Warehouse != null
                && il.Location.Zone.Warehouse.IsActive)
            .Where(il => !warehouseId.HasValue || il.Location!.Zone!.WarehouseId == warehouseId.Value)
            .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
            .Select(il => new
            {
                il.ItemId,
                il.OwnerPartnerId,
                il.LocationId,
                il.LotNumber,
                il.ExpiryDate,
                il.Quantity,
                il.ReservedQty,
                il.HoldStatus,
                il.Location!.AllowMixedSku,
                il.Item!.UnitCost,
                WarehouseId = il.Location!.Zone!.WarehouseId,
                WarehouseCode = il.Location.Zone.Warehouse!.WarehouseCode,
                WarehouseName = il.Location.Zone.Warehouse.WarehouseName
            })
            .ToListAsync();

        var toExclusive = to.AddDays(1);
        var movementRows = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.TransactionAt >= from && t.TransactionAt < toExclusive)
            .Where(t => t.QuantityDelta != 0)
            .Where(t => !warehouseId.HasValue || t.WarehouseId == warehouseId.Value)
            .Where(t => scopedOwnerIds.Count == 0 || (t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value)))
            .Select(t => new
            {
                t.TransactionAt,
                t.TransactionType,
                t.QuantityDelta,
                t.WarehouseId,
                WarehouseCode = t.Warehouse != null ? t.Warehouse.WarehouseCode : "",
                WarehouseName = t.Warehouse != null ? t.Warehouse.WarehouseName : "",
                t.ItemId,
                ItemCode = t.Item != null ? t.Item.ItemCode : "",
                ItemName = t.Item != null ? t.Item.ItemName : "",
                UomCode = t.Item != null && t.Item.BaseUom != null ? t.Item.BaseUom.UomCode : ""
            })
            .ToListAsync();

        var inboundTypes = new[]
        {
            VoucherTypeEnum.NhapKho,
            VoucherTypeEnum.KhachTra,
            VoucherTypeEnum.NhapThanhPham
        };
        var outboundTypes = new[]
        {
            VoucherTypeEnum.XuatKho,
            VoucherTypeEnum.TraNCC,
            VoucherTypeEnum.ChuyenKho,
            VoucherTypeEnum.XuatSanXuat
        };

        var voucherBaseQuery = _db.Vouchers.AsNoTracking()
            .Where(v => !v.IsCancelled)
            .Where(v => !warehouseId.HasValue || v.WarehouseId == warehouseId.Value)
            .Where(v => scopedOwnerIds.Count == 0 || (v.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(v.OwnerPartnerId.Value)));

        var voucherRangeQuery = voucherBaseQuery
            .Where(v => v.VoucherDate >= from && v.VoucherDate <= to);

        model.Kpi.OnHandQty = stockRows.Sum(x => x.Quantity);
        model.Kpi.ReservedQty = stockRows.Sum(x => x.ReservedQty);
        model.Kpi.AvailableQty = stockRows
            .Where(x => InventoryStatusEngine.IsAvailableForAllocation(x.HoldStatus))
            .Sum(x => Math.Max(0m, x.Quantity - x.ReservedQty));
        model.Kpi.TotalStockValue = model.CanSeeFinancial ? stockRows.Sum(x => x.Quantity * x.UnitCost) : 0m;
        model.Kpi.ActiveItemCount = stockRows.Where(x => x.Quantity != 0 || x.ReservedQty != 0).Select(x => x.ItemId).Distinct().Count();
        model.Kpi.ActiveLocationCount = stockRows.Where(x => x.Quantity != 0 || x.ReservedQty != 0).Select(x => x.LocationId).Distinct().Count();
        model.Kpi.InboundQty = movementRows.Where(x => IsOverviewInboundMovement(x.TransactionType, x.QuantityDelta)).Sum(x => x.QuantityDelta);
        model.Kpi.OutboundQty = movementRows.Where(x => IsOverviewOutboundMovement(x.TransactionType, x.QuantityDelta)).Sum(x => Math.Abs(x.QuantityDelta));
        model.Kpi.MovementLineCount = movementRows.Count;
        model.Kpi.PostedVoucherCount = await voucherRangeQuery.CountAsync(v => v.IsPosted);
        model.Kpi.OpenInboundVouchers = await voucherBaseQuery.CountAsync(v =>
            !v.IsPosted && inboundTypes.Contains(v.VoucherType) && v.InboundStatus != InboundStatusEnum.Completed && v.InboundStatus != InboundStatusEnum.Rejected);
        model.Kpi.OpenOutboundVouchers = await voucherBaseQuery.CountAsync(v =>
            !v.IsPosted && outboundTypes.Contains(v.VoucherType));
        model.Kpi.ExpiringLotCount = stockRows
            .Where(x => x.Quantity > 0 && x.ExpiryDate.HasValue && x.ExpiryDate.Value.Date >= today && x.ExpiryDate.Value.Date <= today.AddDays(30))
            .Select(x => new { x.ItemId, x.OwnerPartnerId, LotNumber = x.LotNumber ?? "", ExpiryDate = x.ExpiryDate!.Value.Date })
            .Distinct()
            .Count();
        model.Kpi.ExpiredLotCount = stockRows
            .Where(x => x.Quantity > 0 && x.ExpiryDate.HasValue && x.ExpiryDate.Value.Date < today)
            .Select(x => new { x.ItemId, x.OwnerPartnerId, LotNumber = x.LotNumber ?? "", ExpiryDate = x.ExpiryDate!.Value.Date })
            .Distinct()
            .Count();

        var dailyMap = movementRows
            .GroupBy(x => x.TransactionAt.Date)
            .ToDictionary(
                g => g.Key,
                g => new WarehouseOverviewDailyFlowRow
                {
                    Date = g.Key,
                    InboundQty = g.Where(x => IsOverviewInboundMovement(x.TransactionType, x.QuantityDelta)).Sum(x => x.QuantityDelta),
                    OutboundQty = g.Where(x => IsOverviewOutboundMovement(x.TransactionType, x.QuantityDelta)).Sum(x => Math.Abs(x.QuantityDelta)),
                    TransactionCount = g.Count()
                });

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            model.DailyFlow.Add(dailyMap.TryGetValue(day, out var row)
                ? row
                : new WarehouseOverviewDailyFlowRow { Date = day });
        }

        var openInboundByWarehouse = await voucherBaseQuery
            .Where(v => !v.IsPosted && inboundTypes.Contains(v.VoucherType) && v.InboundStatus != InboundStatusEnum.Completed && v.InboundStatus != InboundStatusEnum.Rejected)
            .GroupBy(v => v.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WarehouseId, x => x.Count);
        var openOutboundByWarehouse = await voucherBaseQuery
            .Where(v => !v.IsPosted && outboundTypes.Contains(v.VoucherType))
            .GroupBy(v => v.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WarehouseId, x => x.Count);

        var movementByWarehouse = movementRows
            .GroupBy(x => x.WarehouseId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    InboundQty = g.Where(x => IsOverviewInboundMovement(x.TransactionType, x.QuantityDelta)).Sum(x => x.QuantityDelta),
                    OutboundQty = g.Where(x => IsOverviewOutboundMovement(x.TransactionType, x.QuantityDelta)).Sum(x => Math.Abs(x.QuantityDelta))
                });

        model.WarehouseRows = model.Warehouses
            .Where(w => !warehouseId.HasValue || w.WarehouseId == warehouseId.Value)
            .Select(w =>
            {
                var stock = stockRows.Where(x => x.WarehouseId == w.WarehouseId).ToList();
                movementByWarehouse.TryGetValue(w.WarehouseId, out var flow);
                return new WarehouseOverviewWarehouseRow
                {
                    WarehouseId = w.WarehouseId,
                    WarehouseCode = w.WarehouseCode,
                    WarehouseName = w.WarehouseName,
                    OnHandQty = stock.Sum(x => x.Quantity),
                    ReservedQty = stock.Sum(x => x.ReservedQty),
                    AvailableQty = stock
                        .Where(x => InventoryStatusEngine.IsAvailableForAllocation(x.HoldStatus))
                        .Sum(x => Math.Max(0m, x.Quantity - x.ReservedQty)),
                    ActiveItemCount = stock.Where(x => x.Quantity != 0 || x.ReservedQty != 0).Select(x => x.ItemId).Distinct().Count(),
                    ActiveLocationCount = stock.Where(x => x.Quantity != 0 || x.ReservedQty != 0).Select(x => x.LocationId).Distinct().Count(),
                    OpenInboundVouchers = openInboundByWarehouse.GetValueOrDefault(w.WarehouseId),
                    OpenOutboundVouchers = openOutboundByWarehouse.GetValueOrDefault(w.WarehouseId),
                    InboundQty = flow?.InboundQty ?? 0m,
                    OutboundQty = flow?.OutboundQty ?? 0m
                };
            })
            .OrderBy(r => r.WarehouseCode)
            .ToList();

        model.TopItems = movementRows
            .Where(x => IsOverviewInboundMovement(x.TransactionType, x.QuantityDelta)
                || IsOverviewOutboundMovement(x.TransactionType, x.QuantityDelta))
            .GroupBy(x => new { x.ItemId, x.ItemCode, x.ItemName, x.UomCode })
            .Select(g => new WarehouseOverviewTopItemRow
            {
                ItemId = g.Key.ItemId,
                ItemCode = string.IsNullOrWhiteSpace(g.Key.ItemCode) ? g.Key.ItemId.ToString() : g.Key.ItemCode,
                ItemName = g.Key.ItemName,
                UomCode = g.Key.UomCode,
                InboundQty = g.Where(x => IsOverviewInboundMovement(x.TransactionType, x.QuantityDelta)).Sum(x => x.QuantityDelta),
                OutboundQty = g.Where(x => IsOverviewOutboundMovement(x.TransactionType, x.QuantityDelta)).Sum(x => Math.Abs(x.QuantityDelta)),
                TransactionCount = g.Count(x => IsOverviewInboundMovement(x.TransactionType, x.QuantityDelta)
                    || IsOverviewOutboundMovement(x.TransactionType, x.QuantityDelta))
            })
            .OrderByDescending(x => x.InboundQty + x.OutboundQty)
            .ThenBy(x => x.ItemCode)
            .Take(12)
            .ToList();

        var negativeLocationCount = stockRows.Count(x => x.Quantity < 0 || x.ReservedQty < 0);
        var overReservedLocationCount = stockRows.Count(x => x.ReservedQty > x.Quantity);
        var mixedStockKeyLocationCount = stockRows
            .Where(x => x.Quantity > 0)
            .GroupBy(x => new { x.LocationId, x.AllowMixedSku })
            .Count(group => group.Select(x => x.OwnerPartnerId).Distinct().Count() > 1
                || (!group.Key.AllowMixedSku && group.Select(x => x.ItemId).Distinct().Count() > 1));
        var postedWithoutLedgerCount = await voucherBaseQuery
            .Where(v => v.IsPosted && v.Details.Any(d => d.BaseQty != 0))
            .CountAsync(v => !_db.InventoryTransactions.Any(t => t.VoucherId == v.VoucherId && t.QuantityDelta != 0));
        var reservationMismatchCount = await CountReservationMismatchAsync(warehouseId, scopedOwnerIds);

        model.Exceptions = new List<WarehouseOverviewExceptionRow>
        {
            new()
            {
                Severity = negativeLocationCount > 0 ? "danger" : "success",
                Code = "NEGATIVE_STOCK",
                StatusLabel = "Tồn hoặc giữ chỗ âm",
                Title = "Tồn hoặc giữ chỗ âm",
                Description = "Có dòng tồn theo vị trí đang âm hoặc số lượng giữ chỗ âm. Cần kiểm tra chứng từ phát sinh và điều chỉnh tồn.",
                Count = negativeLocationCount,
                ActionController = "Reports",
                ActionName = "Inventory"
            },
            new()
            {
                Severity = overReservedLocationCount > 0 ? "danger" : "success",
                Code = "OVER_RESERVED",
                StatusLabel = "Giữ chỗ vượt tồn",
                Title = "Giữ chỗ vượt tồn",
                Description = "Số lượng đang giữ chỗ lớn hơn số lượng tồn tại cùng vị trí, lô hoặc hạn dùng. Cần giải phóng giữ chỗ hoặc bổ sung tồn.",
                Count = overReservedLocationCount,
                ActionController = "Reports",
                ActionName = "Inventory"
            },
            new()
            {
                Severity = reservationMismatchCount > 0 ? "warning" : "success",
                Code = "RESERVATION_MISMATCH",
                StatusLabel = "Lệch giữ chỗ",
                Title = "Lệch giữ chỗ",
                Description = "Tổng số lượng đang giữ chỗ từ đơn mở không khớp với số giữ chỗ ghi trên tồn kho.",
                Count = reservationMismatchCount,
                ActionController = "Operations",
                ActionName = "ExceptionCenter"
            },
            new()
            {
                Severity = postedWithoutLedgerCount > 0 ? "danger" : "success",
                Code = "POSTED_WITHOUT_LEDGER",
                StatusLabel = "Phiếu thiếu sổ kho",
                Title = "Phiếu đã ghi sổ thiếu giao dịch tồn",
                Description = "Phiếu đã ghi sổ nhưng chưa có dòng giao dịch tồn kho tương ứng. Cần rà lại lịch sử ghi sổ.",
                Count = postedWithoutLedgerCount,
                ActionController = "Reports",
                ActionName = "InventoryTransactions"
            },
            new()
            {
                Severity = mixedStockKeyLocationCount > 0 ? "danger" : "success",
                Code = "LOCATION_MULTIPLE_STOCK_KEYS",
                StatusLabel = "Trộn hàng sai cấu hình",
                Title = "Vị trí trộn hàng không đúng cấu hình",
                Description = "Có vị trí chứa nhiều chủ hàng, hoặc chứa nhiều mã hàng khi vị trí chưa được cấu hình cho phép trộn mã. Cần đối soát rồi điều chuyển bằng nghiệp vụ kho; không sửa trực tiếp số tồn.",
                Count = mixedStockKeyLocationCount,
                ActionController = "Reports",
                ActionName = "Inventory"
            },
            new()
            {
                Severity = model.Kpi.ExpiredLotCount > 0 ? "warning" : "success",
                Code = "EXPIRED_LOTS",
                StatusLabel = "Lô hết hạn còn tồn",
                Title = "Lô đã hết hạn còn tồn",
                Description = "Có lô đã quá hạn nhưng vẫn còn tồn. Cần cách ly, điều chỉnh hoặc xử lý theo quy trình.",
                Count = model.Kpi.ExpiredLotCount,
                ActionController = "Reports",
                ActionName = "ExpiryReport"
            }
        };
        return model;
    }

    private async Task<int> CountReservationMismatchAsync(int? warehouseId, IReadOnlyList<int> scopedOwnerIds)
    {
        var reservationRows = await _db.StockReservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatusEnum.Active)
            .Where(r => !warehouseId.HasValue || (r.Voucher != null && r.Voucher.WarehouseId == warehouseId.Value))
            .Where(r => scopedOwnerIds.Count == 0 || (r.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(r.OwnerPartnerId.Value)))
            .GroupBy(r => new { r.ItemId, r.OwnerPartnerId, r.LocationId, r.LotNumber, r.ExpiryDate })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.OwnerPartnerId,
                g.Key.LocationId,
                g.Key.LotNumber,
                g.Key.ExpiryDate,
                Qty = g.Sum(x => x.ReservedQty - x.ConsumedQty - x.ReleasedQty)
            })
            .ToListAsync();

        var stockReservedRows = await _db.ItemLocations.AsNoTracking()
            .Where(il => il.ReservedQty != 0)
            .Where(il => il.Location != null && il.Location.Zone != null)
            .Where(il => !warehouseId.HasValue || il.Location!.Zone!.WarehouseId == warehouseId.Value)
            .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
            .GroupBy(il => new { il.ItemId, il.OwnerPartnerId, il.LocationId, il.LotNumber, il.ExpiryDate })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.OwnerPartnerId,
                g.Key.LocationId,
                g.Key.LotNumber,
                g.Key.ExpiryDate,
                Qty = g.Sum(x => x.ReservedQty)
            })
            .ToListAsync();

        var reservationMap = reservationRows.ToDictionary(
            x => OverviewStockKey(x.ItemId, x.OwnerPartnerId, x.LocationId, x.LotNumber, x.ExpiryDate),
            x => x.Qty);
        var stockMap = stockReservedRows.ToDictionary(
            x => OverviewStockKey(x.ItemId, x.OwnerPartnerId, x.LocationId, x.LotNumber, x.ExpiryDate),
            x => x.Qty);

        var keys = reservationMap.Keys.ToHashSet(StringComparer.Ordinal);
        keys.UnionWith(stockMap.Keys);
        return keys.Count(key =>
        {
            reservationMap.TryGetValue(key, out var reservedFromReservations);
            stockMap.TryGetValue(key, out var reservedFromStock);
            return Math.Abs(reservedFromReservations - reservedFromStock) > 0.0001m;
        });
    }

    private static string OverviewStockKey(int itemId, int? ownerPartnerId, int locationId, string? lotNumber, DateTime? expiryDate)
        => string.Join("|",
            itemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ownerPartnerId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            locationId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            lotNumber ?? "",
            expiryDate?.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "");

    private static bool IsOverviewInboundMovement(InventoryTransactionTypeEnum transactionType, decimal quantityDelta)
        => quantityDelta > 0m && transactionType is
            InventoryTransactionTypeEnum.Receive or
            InventoryTransactionTypeEnum.TransferIn or
            InventoryTransactionTypeEnum.KitProduce;

    private static bool IsOverviewOutboundMovement(InventoryTransactionTypeEnum transactionType, decimal quantityDelta)
        => quantityDelta < 0m && transactionType is
            InventoryTransactionTypeEnum.Ship or
            InventoryTransactionTypeEnum.TransferOut or
            InventoryTransactionTypeEnum.KitConsume or
            InventoryTransactionTypeEnum.VasConsume;
}
