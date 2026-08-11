using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Models;

namespace WMS.Common;

public static class LocationStoragePolicy
{
    public static ItemLocation? FindBlockingConflict(
        IEnumerable<ItemLocation> locationRows,
        int itemId,
        int? ownerPartnerId)
    {
        var occupiedRows = locationRows.Where(row => row.Quantity > 0).ToList();
        if (occupiedRows.Count == 0)
            return null;

        // Legacy/demo locations can already contain multiple stock keys. Replenishing an
        // existing key does not introduce another SKU; a new item/owner key remains blocked.
        if (occupiedRows.Any(row => row.ItemId == itemId && row.OwnerPartnerId == ownerPartnerId))
            return null;

        return occupiedRows.FirstOrDefault(row => row.ItemId != itemId || row.OwnerPartnerId != ownerPartnerId);
    }

    public static async Task EnsureStorageLocationCanAcceptAsync(
        AppDbContext db,
        int locationId,
        int itemId,
        int? ownerPartnerId,
        CancellationToken cancellationToken = default)
    {
        var location = await db.Locations
            .AsNoTracking()
            .Where(row => row.LocationId == locationId)
            .Select(row => new
            {
                row.LocationCode,
                ZoneType = row.Zone == null ? (ZoneTypeEnum?)null : row.Zone.ZoneType
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (location == null || !location.ZoneType.HasValue)
        {
            throw new BusinessRuleException(
                "Vị trí nhận tồn không tồn tại hoặc chưa được gán khu vực kho.",
                "STOCK_DESTINATION_LOCATION_INVALID",
                nameof(Location));
        }

        // Receiving, staging, shipping and cross-dock locations intentionally consolidate
        // several stock keys. The one-location/one-stock-key rule applies to storage only.
        if (location.ZoneType.Value != ZoneTypeEnum.Storage)
            return;

        var effectiveRows = await db.ItemLocations
            .AsNoTracking()
            .Include(row => row.Item)
            .Where(row => row.LocationId == locationId && row.Quantity > 0)
            .ToListAsync(cancellationToken);

        // Include pending changes so two different stock keys cannot enter the same empty
        // storage location before the surrounding transaction reaches SaveChanges.
        foreach (var entry in db.ChangeTracker.Entries<ItemLocation>()
                     .Where(entry => entry.Entity.LocationId == locationId
                         && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity.ItemLocationId > 0)
                effectiveRows.RemoveAll(row => row.ItemLocationId == entry.Entity.ItemLocationId);

            if (entry.State != EntityState.Deleted && entry.Entity.Quantity > 0)
                effectiveRows.Add(entry.Entity);
        }

        var conflict = FindBlockingConflict(effectiveRows, itemId, ownerPartnerId);
        if (conflict == null)
            return;

        var itemCodes = await db.Items
            .AsNoTracking()
            .Where(item => item.ItemId == itemId || item.ItemId == conflict.ItemId)
            .Select(item => new { item.ItemId, item.ItemCode })
            .ToDictionaryAsync(item => item.ItemId, item => item.ItemCode, cancellationToken);
        var requestedItemCode = itemCodes.GetValueOrDefault(itemId) ?? itemId.ToString();
        var conflictItemCode = conflict.Item?.ItemCode
            ?? itemCodes.GetValueOrDefault(conflict.ItemId)
            ?? conflict.ItemId.ToString();

        throw WmsExceptions.OneLocationOneItemConflict(
            requestedItemCode,
            conflictItemCode,
            location.LocationCode);
    }
}
