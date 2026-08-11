using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public class LocationStoragePolicyWorkflowTests
{
    [Fact]
    public async Task StoragePolicy_ShouldAllowExistingKeyAndBlockDifferentItemOrOwner()
    {
        await using var db = CreateDb(nameof(StoragePolicy_ShouldAllowExistingKeyAndBlockDifferentItemOrOwner));
        SeedWarehouse(db);
        SeedItems(db);
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            OwnerPartnerId = 10,
            LocationId = 2,
            Quantity = 5
        });
        await db.SaveChangesAsync();

        await LocationStoragePolicy.EnsureStorageLocationCanAcceptAsync(db, 2, 1, 10);

        var itemConflict = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            LocationStoragePolicy.EnsureStorageLocationCanAcceptAsync(db, 2, 2, 10));
        Assert.Equal("ONE_LOCATION_ONE_ITEM", itemConflict.Code);

        var ownerConflict = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            LocationStoragePolicy.EnsureStorageLocationCanAcceptAsync(db, 2, 1, 20));
        Assert.Equal("ONE_LOCATION_ONE_ITEM", ownerConflict.Code);
    }

    [Fact]
    public async Task StoragePolicy_ShouldSeePendingRowsBeforeSave()
    {
        await using var db = CreateDb(nameof(StoragePolicy_ShouldSeePendingRowsBeforeSave));
        SeedWarehouse(db);
        SeedItems(db);
        await db.SaveChangesAsync();

        db.ItemLocations.Add(new ItemLocation
        {
            ItemId = 1,
            OwnerPartnerId = 10,
            LocationId = 2,
            Quantity = 3
        });

        var conflict = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            LocationStoragePolicy.EnsureStorageLocationCanAcceptAsync(db, 2, 2, 10));
        Assert.Equal("ONE_LOCATION_ONE_ITEM", conflict.Code);
    }

    [Fact]
    public async Task StoragePolicy_ShouldAllowConsolidationAreasToContainSeveralKeys()
    {
        await using var db = CreateDb(nameof(StoragePolicy_ShouldAllowConsolidationAreasToContainSeveralKeys));
        SeedWarehouse(db);
        SeedItems(db);
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            OwnerPartnerId = 10,
            LocationId = 3,
            Quantity = 5
        });
        await db.SaveChangesAsync();

        await LocationStoragePolicy.EnsureStorageLocationCanAcceptAsync(db, 3, 2, 20);
    }

    [Fact]
    public async Task MovementCompletion_ShouldRejectConflictingStorageDestinationBeforeStockMutation()
    {
        await using var db = CreateDb(nameof(MovementCompletion_ShouldRejectConflictingStorageDestinationBeforeStockMutation));
        SeedWarehouse(db);
        SeedItems(db);
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 1,
                ItemId = 1,
                OwnerPartnerId = 10,
                LocationId = 1,
                Quantity = 5,
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 2,
                ItemId = 2,
                OwnerPartnerId = 10,
                LocationId = 2,
                Quantity = 2,
                HoldStatus = InventoryHoldStatusEnum.Available
            });
        await db.SaveChangesAsync();

        var service = new MovementTaskService(db, new EfUnitOfWork(db));
        var task = await service.CreateMovementTaskAsync(new MovementTaskCreateRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            ItemId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            SourceItemLocationId = 1,
            TaskType = MovementTaskTypeEnum.Relocate,
            PlannedQty = 5
        }, 1, "AUDIT_TEST_planner");

        var conflict = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CompleteAsync(task.MovementTaskId, "SRC-01", "DST-01", 5, 1, "AUDIT_TEST_operator"));

        Assert.Equal("ONE_LOCATION_ONE_ITEM", conflict.Code);
        Assert.Equal(5, (await db.ItemLocations.FindAsync(1))!.Quantity);
        Assert.Equal(2, (await db.ItemLocations.FindAsync(2))!.Quantity);
        Assert.False(await db.ItemLocations.AnyAsync(row => row.ItemId == 1 && row.LocationId == 2));
    }

    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedWarehouse(AppDbContext db)
    {
        db.Warehouses.Add(new Warehouse { WarehouseId = 1, WarehouseCode = "AUDIT_TEST_WH", WarehouseName = "Kho kiểm thử", IsActive = true });
        db.Zones.AddRange(
            new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "AUDIT_TEST_STORAGE", ZoneName = "Khu lưu trữ", ZoneType = ZoneTypeEnum.Storage, IsActive = true },
            new Zone { ZoneId = 2, WarehouseId = 1, ZoneCode = "AUDIT_TEST_STAGE", ZoneName = "Khu tập kết", ZoneType = ZoneTypeEnum.Staging, IsActive = true });
        db.Locations.AddRange(
            new Location { LocationId = 1, ZoneId = 1, LocationCode = "SRC-01", IsActive = true },
            new Location { LocationId = 2, ZoneId = 1, LocationCode = "DST-01", IsActive = true },
            new Location { LocationId = 3, ZoneId = 2, LocationCode = "STAGE-01", IsActive = true });
    }

    private static void SeedItems(AppDbContext db)
    {
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "AUDIT_TEST_ITEM_A", ItemName = "Mặt hàng A", BaseUomId = 1, OwnerPartnerId = 10, IsActive = true },
            new Item { ItemId = 2, ItemCode = "AUDIT_TEST_ITEM_B", ItemName = "Mặt hàng B", BaseUomId = 1, OwnerPartnerId = 10, IsActive = true });
    }
}
