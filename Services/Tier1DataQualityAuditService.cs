using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WMS.Common;
using WMS.Data;
using WMS.Models;

namespace WMS.Services;

public interface ITier1DataQualityAuditService
{
    Task<Tier1DataQualityAuditResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class Tier1DataQualityAuditResult
{
    public DateTime GeneratedAt { get; init; } = VietnamTime.Now;
    public string Status => Issues.Any(i => i.Severity is "Critical" or "Error") ? "Failed" : "Passed";
    public int CriticalCount => Issues.Count(i => i.Severity == "Critical");
    public int ErrorCount => Issues.Count(i => i.Severity == "Error");
    public int WarningCount => Issues.Count(i => i.Severity == "Warning");
    public IReadOnlyList<Tier1DataQualityAuditIssue> Issues { get; init; } = Array.Empty<Tier1DataQualityAuditIssue>();
}

public sealed record Tier1DataQualityAuditIssue(
    string Severity,
    string Code,
    string Entity,
    string EntityId,
    string Message);

public sealed class Tier1DataQualityAuditService(AppDbContext db) : ITier1DataQualityAuditService
{
    private const decimal Tolerance = 0.0001m;
    private sealed record StockReservationKey(
        int ItemId,
        int LocationId,
        string? LotNumber,
        DateTime? ExpiryDate,
        int? OwnerPartnerId);

    public async Task<Tier1DataQualityAuditResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<Tier1DataQualityAuditIssue>();

        var uoms = await db.UnitsOfMeasure.AsNoTracking()
            .Select(u => new { u.UomId, u.UomCode, u.IsActive })
            .ToListAsync(cancellationToken);
        var activeUomIds = uoms.Where(u => u.IsActive).Select(u => u.UomId).ToHashSet();

        var partners = await db.Partners.AsNoTracking()
            .Select(p => new { p.PartnerId, p.PartnerCode, p.IsActive })
            .ToListAsync(cancellationToken);
        var activePartnerIds = partners.Where(p => p.IsActive).Select(p => p.PartnerId).ToHashSet();

        var warehouses = await db.Warehouses.AsNoTracking()
            .Select(w => new { w.WarehouseId, w.IsActive })
            .ToListAsync(cancellationToken);
        var activeWarehouseIds = warehouses.Where(w => w.IsActive).Select(w => w.WarehouseId).ToHashSet();

        var zones = await db.Zones.AsNoTracking()
            .Select(z => new { z.ZoneId, z.WarehouseId, z.IsActive })
            .ToListAsync(cancellationToken);
        var activeZoneIds = zones
            .Where(z => z.IsActive && activeWarehouseIds.Contains(z.WarehouseId))
            .Select(z => z.ZoneId)
            .ToHashSet();

        var locations = await db.Locations.AsNoTracking()
            .Select(l => new { l.LocationId, l.ZoneId, l.LocationCode, l.AllowMixedSku, l.IsActive })
            .ToListAsync(cancellationToken);
        var locationById = locations.ToDictionary(location => location.LocationId);
        var activeLocationIds = locations
            .Where(l => l.IsActive && activeZoneIds.Contains(l.ZoneId))
            .Select(l => l.LocationId)
            .ToHashSet();

        var items = await db.Items.AsNoTracking()
            .Select(i => new
            {
                i.ItemId,
                i.ItemCode,
                i.Barcode,
                i.SkuCode,
                i.BaseUomId,
                i.CatchWeightUomId,
                i.CurrentStock,
                i.IsActive,
                i.OwnerPartnerId,
                i.TrackLot,
                i.TrackExpiry,
                i.TrackSerial,
                i.TrackCatchWeight
            })
            .ToListAsync(cancellationToken);
        var itemIds = items.Select(i => i.ItemId).ToHashSet();
        var activeItemIds = items.Where(i => i.IsActive).Select(i => i.ItemId).ToHashSet();
        var itemById = items.ToDictionary(i => i.ItemId);

        foreach (var item in items.Where(i => i.IsActive))
        {
            if (item.BaseUomId <= 0 || !activeUomIds.Contains(item.BaseUomId))
            {
                Add(issues, "Error", "ITEM_BASE_UOM_INVALID", "Item", item.ItemId,
                    $"Active item [{item.ItemCode}] must reference an active base UOM.");
            }

            if (item.OwnerPartnerId.HasValue && !activePartnerIds.Contains(item.OwnerPartnerId.Value))
            {
                Add(issues, "Error", "ITEM_OWNER_INVALID", "Item", item.ItemId,
                    $"Active item [{item.ItemCode}] references a missing or inactive owner partner.");
            }

            if (item.TrackCatchWeight && (!item.CatchWeightUomId.HasValue || !activeUomIds.Contains(item.CatchWeightUomId.Value)))
            {
                Add(issues, "Error", "ITEM_CATCH_WEIGHT_UOM_INVALID", "Item", item.ItemId,
                    $"Catch-weight item [{item.ItemCode}] must reference an active catch-weight UOM.");
            }
        }

        AddDuplicateTextIssues(issues, items.Where(i => i.IsActive).Select(i => (i.ItemId, i.ItemCode, Value: i.Barcode)),
            "ITEM_BARCODE_DUPLICATE", "Barcode");
        AddDuplicateTextIssues(issues, items.Where(i => i.IsActive).Select(i => (i.ItemId, i.ItemCode, Value: i.SkuCode)),
            "ITEM_SKU_DUPLICATE", "SKU");

        var conversions = await db.UnitConversions.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.ConversionId, c.ItemId, c.FromUomId, c.ToUomId, c.ConversionRate })
            .ToListAsync(cancellationToken);
        foreach (var conversion in conversions)
        {
            if (conversion.ConversionRate <= 0)
            {
                Add(issues, "Critical", "UOM_CONVERSION_RATE_INVALID", "UnitConversion", conversion.ConversionId,
                    "Active UOM conversion must have a positive conversion rate.");
            }

            if (!activeUomIds.Contains(conversion.FromUomId) || !activeUomIds.Contains(conversion.ToUomId))
            {
                Add(issues, "Error", "UOM_CONVERSION_UOM_INVALID", "UnitConversion", conversion.ConversionId,
                    "Active UOM conversion must reference active source and target UOMs.");
            }

            if (conversion.ItemId.HasValue && !activeItemIds.Contains(conversion.ItemId.Value))
            {
                Add(issues, "Error", "UOM_CONVERSION_ITEM_INVALID", "UnitConversion", conversion.ConversionId,
                    "Item-specific active UOM conversion must reference an active item.");
            }
        }

        var itemLocations = await db.ItemLocations.AsNoTracking()
            .Select(il => new
            {
                il.ItemLocationId,
                il.ItemId,
                il.OwnerPartnerId,
                il.LocationId,
                il.Quantity,
                il.ReservedQty,
                il.LotNumber,
                il.ExpiryDate,
                il.HoldStatus
            })
            .ToListAsync(cancellationToken);

        foreach (var row in itemLocations)
        {
            if (!itemIds.Contains(row.ItemId))
            {
                Add(issues, "Error", "ITEM_LOCATION_ITEM_MISSING", "ItemLocation", row.ItemLocationId,
                    "ItemLocation references a missing item.");
            }
            else if (!activeItemIds.Contains(row.ItemId) && Math.Abs(row.Quantity) > Tolerance)
            {
                Add(issues, "Warning", "ITEM_LOCATION_INACTIVE_ITEM_HAS_STOCK", "ItemLocation", row.ItemLocationId,
                    "Inactive item still has location stock; confirm this is intentional.");
            }

            if (!activeLocationIds.Contains(row.LocationId))
            {
                Add(issues, "Error", "ITEM_LOCATION_LOCATION_INVALID", "ItemLocation", row.ItemLocationId,
                    "ItemLocation references a missing, inactive or inactive-warehouse location.");
            }

            if (row.OwnerPartnerId.HasValue && !activePartnerIds.Contains(row.OwnerPartnerId.Value))
            {
                Add(issues, "Error", "ITEM_LOCATION_OWNER_INVALID", "ItemLocation", row.ItemLocationId,
                    "ItemLocation references a missing or inactive owner partner.");
            }

            if (row.Quantity < 0 || row.ReservedQty < 0)
            {
                Add(issues, "Critical", "ITEM_LOCATION_NEGATIVE_QTY", "ItemLocation", row.ItemLocationId,
                    "ItemLocation quantity and reserved quantity must never be negative.");
            }

            if (row.ReservedQty - row.Quantity > Tolerance)
            {
                Add(issues, "Critical", "ITEM_LOCATION_RESERVED_EXCEEDS_QTY", "ItemLocation", row.ItemLocationId,
                    "ItemLocation reserved quantity must not exceed physical quantity.");
            }

            if (itemById.TryGetValue(row.ItemId, out var item) && row.Quantity > Tolerance)
            {
                if (item.TrackLot && string.IsNullOrWhiteSpace(row.LotNumber))
                {
                    Add(issues, "Error", "TRACKED_LOT_MISSING", "ItemLocation", row.ItemLocationId,
                        $"Lot-tracked item [{item.ItemCode}] has positive stock without a lot number.");
                }

                if (item.TrackExpiry && row.ExpiryDate == null)
                {
                    Add(issues, "Error", "TRACKED_EXPIRY_MISSING", "ItemLocation", row.ItemLocationId,
                        $"Expiry-tracked item [{item.ItemCode}] has positive stock without an expiry date.");
                }
            }
        }

        foreach (var locationGroup in itemLocations
            .Where(row => row.Quantity > Tolerance)
            .GroupBy(row => row.LocationId))
        {
            var stockKeys = locationGroup
                .Select(row => (row.ItemId, row.OwnerPartnerId))
                .Distinct()
                .ToList();
            locationById.TryGetValue(locationGroup.Key, out var location);
            var mixesOwners = stockKeys.Select(key => key.OwnerPartnerId).Distinct().Count() > 1;
            var mixesItemsWithoutPermission = location?.AllowMixedSku != true
                && stockKeys.Select(key => key.ItemId).Distinct().Count() > 1;
            if (!mixesOwners && !mixesItemsWithoutPermission)
                continue;

            var locationCode = location?.LocationCode
                ?? locationGroup.Key.ToString(CultureInfo.InvariantCulture);
            var itemCodes = stockKeys
                .Select(key => itemById.TryGetValue(key.ItemId, out var item) ? item.ItemCode : key.ItemId.ToString(CultureInfo.InvariantCulture))
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase);
            Add(issues, "Error", "LOCATION_MULTIPLE_STOCK_KEYS", "Location", locationGroup.Key,
                $"Location [{locationCode}] contains multiple positive-stock item/owner keys: {string.Join(", ", itemCodes)}.");
        }

        var stockByItem = itemLocations
            .GroupBy(il => il.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        foreach (var item in items)
        {
            var computed = stockByItem.TryGetValue(item.ItemId, out var qty) ? qty : 0m;
            if (Math.Abs(item.CurrentStock - computed) > Tolerance)
            {
                Add(issues, "Error", "ITEM_CURRENT_STOCK_MISMATCH", "Item", item.ItemId,
                    $"Item [{item.ItemCode}] CurrentStock={item.CurrentStock:N4} does not match ItemLocation sum={computed:N4}.");
            }
        }

        var reservations = await db.StockReservations.AsNoTracking()
            .Select(r => new
            {
                r.StockReservationId,
                r.ItemId,
                r.OwnerPartnerId,
                r.LocationId,
                r.LotNumber,
                r.ExpiryDate,
                r.ReservedQty,
                r.ConsumedQty,
                r.ReleasedQty,
                r.Status
            })
            .ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            if (!activeItemIds.Contains(reservation.ItemId))
            {
                Add(issues, "Error", "RESERVATION_ITEM_INVALID", "StockReservation", reservation.StockReservationId,
                    "Stock reservation references a missing or inactive item.");
            }

            if (!activeLocationIds.Contains(reservation.LocationId))
            {
                Add(issues, "Error", "RESERVATION_LOCATION_INVALID", "StockReservation", reservation.StockReservationId,
                    "Stock reservation references a missing, inactive or inactive-warehouse location.");
            }

            if (reservation.OwnerPartnerId.HasValue && !activePartnerIds.Contains(reservation.OwnerPartnerId.Value))
            {
                Add(issues, "Error", "RESERVATION_OWNER_INVALID", "StockReservation", reservation.StockReservationId,
                    "Stock reservation references a missing or inactive owner partner.");
            }

            if (reservation.ReservedQty < 0 || reservation.ConsumedQty < 0 || reservation.ReleasedQty < 0)
            {
                Add(issues, "Critical", "RESERVATION_NEGATIVE_QTY", "StockReservation", reservation.StockReservationId,
                    "Reservation quantities must never be negative.");
            }

            if (reservation.ConsumedQty + reservation.ReleasedQty - reservation.ReservedQty > Tolerance)
            {
                Add(issues, "Critical", "RESERVATION_OVER_CLOSED", "StockReservation", reservation.StockReservationId,
                    "Consumed plus released quantity must not exceed reserved quantity.");
            }

            if (reservation.Status == ReservationStatusEnum.Active
                && reservation.ReservedQty - reservation.ConsumedQty - reservation.ReleasedQty < -Tolerance)
            {
                Add(issues, "Critical", "RESERVATION_ACTIVE_OPEN_NEGATIVE", "StockReservation", reservation.StockReservationId,
                    "Active reservation open quantity must not be negative.");
            }
        }

        var kittingReservations = await db.KittingWorkOrderLines.AsNoTracking()
            .Where(l => l.Status == KittingWorkOrderLineStatusEnum.Reserved && l.SourceLocationId.HasValue)
            .Select(l => new
            {
                l.KittingWorkOrderLineId,
                ItemId = l.ComponentItemId,
                l.OwnerPartnerId,
                LocationId = l.SourceLocationId!.Value,
                l.LotNumber,
                l.ExpiryDate,
                l.ReservedQty,
                l.ConsumedQty,
                l.ReleasedQty
            })
            .ToListAsync(cancellationToken);

        var vasReservations = await db.VasMaterialLines.AsNoTracking()
            .Where(l => l.Status == VasMaterialLineStatusEnum.Reserved && l.SourceLocationId.HasValue)
            .Select(l => new
            {
                l.VasMaterialLineId,
                ItemId = l.MaterialItemId,
                l.OwnerPartnerId,
                LocationId = l.SourceLocationId!.Value,
                l.LotNumber,
                l.ExpiryDate,
                l.ReservedQty,
                l.ConsumedQty,
                l.ReleasedQty
            })
            .ToListAsync(cancellationToken);

        var expectedReservedByKey = new Dictionary<StockReservationKey, decimal>();
        foreach (var reservation in reservations.Where(r => r.Status == ReservationStatusEnum.Active))
        {
            AddOpenReserved(expectedReservedByKey,
                BuildStockKey(reservation.ItemId, reservation.LocationId, reservation.LotNumber, reservation.ExpiryDate, reservation.OwnerPartnerId),
                reservation.ReservedQty,
                reservation.ConsumedQty,
                reservation.ReleasedQty);
        }

        foreach (var reservation in kittingReservations)
        {
            AddOpenReserved(expectedReservedByKey,
                BuildStockKey(reservation.ItemId, reservation.LocationId, reservation.LotNumber, reservation.ExpiryDate, reservation.OwnerPartnerId),
                reservation.ReservedQty,
                reservation.ConsumedQty,
                reservation.ReleasedQty);
        }

        foreach (var reservation in vasReservations)
        {
            AddOpenReserved(expectedReservedByKey,
                BuildStockKey(reservation.ItemId, reservation.LocationId, reservation.LotNumber, reservation.ExpiryDate, reservation.OwnerPartnerId),
                reservation.ReservedQty,
                reservation.ConsumedQty,
                reservation.ReleasedQty);
        }

        var itemLocationReservedByKey = itemLocations
            .GroupBy(row => BuildStockKey(row.ItemId, row.LocationId, row.LotNumber, row.ExpiryDate, row.OwnerPartnerId))
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    ItemLocationIds = string.Join(",", group.Select(row => row.ItemLocationId)),
                    ReservedQty = group.Sum(row => row.ReservedQty)
                });

        foreach (var (key, cached) in itemLocationReservedByKey)
        {
            var expected = expectedReservedByKey.TryGetValue(key, out var qty) ? qty : 0m;
            if (Math.Abs(cached.ReservedQty - expected) > Tolerance)
            {
                Add(issues, "Error", "ITEM_LOCATION_RESERVED_CACHE_MISMATCH", "ItemLocation", cached.ItemLocationIds,
                    $"ItemLocation ReservedQty={cached.ReservedQty:N4} must match open reservation sources={expected:N4} for {FormatStockKey(key)}.");
            }
        }

        foreach (var (key, expected) in expectedReservedByKey)
        {
            if (expected > Tolerance && !itemLocationReservedByKey.ContainsKey(key))
            {
                Add(issues, "Error", "RESERVATION_ITEM_LOCATION_MISSING", "Reservation", FormatStockKey(key),
                    $"Open reservation quantity={expected:N4} has no matching ItemLocation snapshot.");
            }
        }

        var serials = await db.SerialNumbers.AsNoTracking()
            .Select(s => new
            {
                s.SerialNumberId,
                s.SerialCode,
                s.WarehouseId,
                s.OwnerPartnerId,
                s.ItemId,
                s.LocationId,
                s.LotNumber,
                s.ExpiryDate,
                s.Status,
                s.ConsumedAt,
                s.VoidedAt
            })
            .ToListAsync(cancellationToken);
        var activeSerialStatuses = new[] { SerialNumberStatusEnum.Active, SerialNumberStatusEnum.Allocated, SerialNumberStatusEnum.Picked };
        var activeSerials = serials.Where(s => activeSerialStatuses.Contains(s.Status)).ToList();
        var duplicateSerials = activeSerials
            .Where(s => !string.IsNullOrWhiteSpace(s.SerialCode))
            .GroupBy(s => new
            {
                Code = Normalize(s.SerialCode),
                s.WarehouseId,
                s.OwnerPartnerId,
                s.ItemId
            })
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var group in duplicateSerials)
        {
            Add(issues, "Critical", "SERIAL_ACTIVE_DUPLICATE", "SerialNumber", string.Join(",", group.Select(s => s.SerialNumberId)),
                "Số sê-ri đang hoạt động phải là duy nhất theo vật tư, kho và chủ hàng.");
        }

        foreach (var serial in serials)
        {
            if (!activeWarehouseIds.Contains(serial.WarehouseId))
            {
                Add(issues, "Error", "SERIAL_WAREHOUSE_INVALID", "SerialNumber", serial.SerialNumberId,
                    "Serial references a missing or inactive warehouse.");
            }

            if (!activeItemIds.Contains(serial.ItemId))
            {
                Add(issues, "Error", "SERIAL_ITEM_INVALID", "SerialNumber", serial.SerialNumberId,
                    "Serial references a missing or inactive item.");
                continue;
            }

            if (serial.OwnerPartnerId.HasValue && !activePartnerIds.Contains(serial.OwnerPartnerId.Value))
            {
                Add(issues, "Error", "SERIAL_OWNER_INVALID", "SerialNumber", serial.SerialNumberId,
                    "Serial references a missing or inactive owner partner.");
            }

            if (serial.LocationId.HasValue && !activeLocationIds.Contains(serial.LocationId.Value))
            {
                Add(issues, "Error", "SERIAL_LOCATION_INVALID", "SerialNumber", serial.SerialNumberId,
                    "Serial references a missing, inactive or inactive-warehouse location.");
            }

            var item = itemById[serial.ItemId];
            if (!item.TrackSerial)
            {
                Add(issues, "Warning", "SERIAL_FOR_NON_SERIAL_ITEM", "SerialNumber", serial.SerialNumberId,
                    $"Serial exists for item [{item.ItemCode}] that is not marked as serial-tracked.");
            }

            if (activeSerialStatuses.Contains(serial.Status) && serial.LocationId == null)
            {
                Add(issues, "Warning", "SERIAL_ACTIVE_LOCATION_MISSING", "SerialNumber", serial.SerialNumberId,
                    "Active, allocated or picked serial should keep its last known location.");
            }

            if (item.TrackLot && string.IsNullOrWhiteSpace(serial.LotNumber))
            {
                Add(issues, "Error", "SERIAL_LOT_MISSING", "SerialNumber", serial.SerialNumberId,
                    $"Serial for lot-tracked item [{item.ItemCode}] is missing lot number.");
            }

            if (item.TrackExpiry && serial.ExpiryDate == null)
            {
                Add(issues, "Error", "SERIAL_EXPIRY_MISSING", "SerialNumber", serial.SerialNumberId,
                    $"Serial for expiry-tracked item [{item.ItemCode}] is missing expiry date.");
            }

            if (serial.Status == SerialNumberStatusEnum.Consumed && serial.ConsumedAt == null)
            {
                Add(issues, "Warning", "SERIAL_CONSUMED_TIMESTAMP_MISSING", "SerialNumber", serial.SerialNumberId,
                    "Consumed serial should have ConsumedAt evidence.");
            }

            if (serial.Status == SerialNumberStatusEnum.Voided && serial.VoidedAt == null)
            {
                Add(issues, "Warning", "SERIAL_VOIDED_TIMESTAMP_MISSING", "SerialNumber", serial.SerialNumberId,
                    "Voided serial should have VoidedAt evidence.");
            }
        }

        return new Tier1DataQualityAuditResult { Issues = issues };
    }

    private static void AddDuplicateTextIssues(
        ICollection<Tier1DataQualityAuditIssue> issues,
        IEnumerable<(int ItemId, string ItemCode, string? Value)> rows,
        string code,
        string fieldName)
    {
        foreach (var group in rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .GroupBy(r => Normalize(r.Value!))
            .Where(g => g.Count() > 1))
        {
            Add(issues, "Error", code, "Item", string.Join(",", group.Select(x => x.ItemId)),
                $"Active item {fieldName} [{group.First().Value}] is duplicated by item codes: {string.Join(", ", group.Select(x => x.ItemCode))}.");
        }
    }

    private static string Normalize(string value)
        => value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Normalize(value);

    private static StockReservationKey BuildStockKey(
        int itemId,
        int locationId,
        string? lotNumber,
        DateTime? expiryDate,
        int? ownerPartnerId)
        => new(itemId, locationId, NormalizeOptional(lotNumber), expiryDate?.Date, ownerPartnerId);

    private static void AddOpenReserved(
        IDictionary<StockReservationKey, decimal> expectedReservedByKey,
        StockReservationKey key,
        decimal reservedQty,
        decimal consumedQty,
        decimal releasedQty)
    {
        var openQty = reservedQty - consumedQty - releasedQty;
        if (openQty <= Tolerance)
            return;

        expectedReservedByKey[key] = expectedReservedByKey.TryGetValue(key, out var existing)
            ? existing + openQty
            : openQty;
    }

    private static string FormatStockKey(StockReservationKey key)
    {
        var expiryDate = key.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
        return $"Item={key.ItemId}; Location={key.LocationId}; Lot={key.LotNumber ?? "-"}; Expiry={expiryDate}; Owner={key.OwnerPartnerId?.ToString() ?? "-"}";
    }

    private static void Add(
        ICollection<Tier1DataQualityAuditIssue> issues,
        string severity,
        string code,
        string entity,
        object entityId,
        string message)
    {
        issues.Add(new Tier1DataQualityAuditIssue(severity, code, entity, Convert.ToString(entityId) ?? "", message));
    }
}
