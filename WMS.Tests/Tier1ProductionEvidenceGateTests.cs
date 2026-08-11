using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public sealed class Tier1ProductionEvidenceGateTests
{
    [Fact]
    public async Task DataQualityAudit_ShouldPassForConsistentCoreWmsData()
    {
        await using var db = CreateDb(nameof(DataQualityAudit_ShouldPassForConsistentCoreWmsData));
        SeedTopology(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "GOOD-ITEM",
            ItemName = "Good item",
            BaseUomId = 1,
            CurrentStock = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 1,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 1,
            VoucherId = 1,
            ItemId = 1,
            LocationId = 1,
            ReservedQty = 1,
            ConsumedQty = 0,
            ReleasedQty = 0,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "test"
        });
        db.UnitConversions.Add(new UnitConversion
        {
            ConversionId = 1,
            ItemId = 1,
            FromUomId = 1,
            ToUomId = 1,
            ConversionRate = 1,
            IsActive = true
        });
        db.SkipAudit = true;
        await db.SaveChangesAsync();

        var result = await new Tier1DataQualityAuditService(db).RunAsync();

        Assert.Equal("Passed", result.Status);
        Assert.DoesNotContain(result.Issues, i => i.Severity is "Critical" or "Error");
    }

    [Fact]
    public async Task DataQualityAudit_ShouldFlagCoreWmsDataDefects()
    {
        await using var db = CreateDb(nameof(DataQualityAudit_ShouldFlagCoreWmsDataDefects));
        SeedTopology(db);
        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true },
            new UnitOfMeasure { UomId = 2, UomCode = "BOX", UomName = "Box", IsActive = false });
        db.Items.AddRange(
            new Item
            {
                ItemId = 1,
                ItemCode = "BAD-BASE-UOM",
                ItemName = "Bad base UOM",
                Barcode = "DUP-BAR",
                BaseUomId = 999,
                CurrentStock = 10,
                IsActive = true,
                TrackLot = true,
                TrackExpiry = true
            },
            new Item
            {
                ItemId = 2,
                ItemCode = "BAD-DUP-BAR",
                ItemName = "Duplicate barcode",
                Barcode = "DUP-BAR",
                BaseUomId = 1,
                CurrentStock = 0,
                IsActive = true
            });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 10,
            ItemId = 1,
            LocationId = 999,
            Quantity = -1,
            ReservedQty = 2
        });
        db.UnitConversions.Add(new UnitConversion
        {
            ConversionId = 20,
            ItemId = 1,
            FromUomId = 2,
            ToUomId = 999,
            ConversionRate = 0,
            IsActive = true
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 30,
            VoucherId = 1,
            ItemId = 1,
            LocationId = 999,
            ReservedQty = 1,
            ConsumedQty = 2,
            ReleasedQty = 0,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "test"
        });
        db.SkipAudit = true;
        await db.SaveChangesAsync();

        var result = await new Tier1DataQualityAuditService(db).RunAsync();
        var codes = result.Issues.Select(i => i.Code).ToHashSet(StringComparer.Ordinal);

        Assert.Equal("Failed", result.Status);
        Assert.Contains("ITEM_BASE_UOM_INVALID", codes);
        Assert.Contains("ITEM_BARCODE_DUPLICATE", codes);
        Assert.Contains("ITEM_LOCATION_LOCATION_INVALID", codes);
        Assert.Contains("ITEM_LOCATION_NEGATIVE_QTY", codes);
        Assert.Contains("ITEM_LOCATION_RESERVED_EXCEEDS_QTY", codes);
        Assert.Contains("UOM_CONVERSION_RATE_INVALID", codes);
        Assert.Contains("UOM_CONVERSION_UOM_INVALID", codes);
        Assert.Contains("ITEM_CURRENT_STOCK_MISMATCH", codes);
        Assert.Contains("RESERVATION_OVER_CLOSED", codes);
        Assert.Contains("ITEM_LOCATION_RESERVED_CACHE_MISMATCH", codes);
    }

    [Fact]
    public async Task DataQualityAudit_ShouldFlagReservedCacheMismatchAcrossReservationSources()
    {
        await using var db = CreateDb(nameof(DataQualityAudit_ShouldFlagReservedCacheMismatchAcrossReservationSources));
        SeedTopology(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "RESERVED-ITEM",
            ItemName = "Reserved item",
            BaseUomId = 1,
            CurrentStock = 10,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 10,
            VoucherId = 10,
            ItemId = 1,
            LocationId = 1,
            ReservedQty = 2,
            ConsumedQty = 0,
            ReleasedQty = 0,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "test"
        });
        db.KittingWorkOrderLines.Add(new KittingWorkOrderLine
        {
            KittingWorkOrderLineId = 20,
            KittingWorkOrderId = 20,
            ComponentItemId = 1,
            SourceLocationId = 1,
            ReservedQty = 3,
            ConsumedQty = 0,
            ReleasedQty = 0,
            Status = KittingWorkOrderLineStatusEnum.Reserved
        });
        db.VasMaterialLines.Add(new VasMaterialLine
        {
            VasMaterialLineId = 30,
            VasWorkOrderId = 30,
            MaterialItemId = 1,
            SourceLocationId = 1,
            ReservedQty = 1,
            ConsumedQty = 0,
            ReleasedQty = 0,
            Status = VasMaterialLineStatusEnum.Reserved
        });
        db.SkipAudit = true;
        await db.SaveChangesAsync();

        var result = await new Tier1DataQualityAuditService(db).RunAsync();
        var issue = Assert.Single(result.Issues, i => i.Code == "ITEM_LOCATION_RESERVED_CACHE_MISMATCH");

        Assert.Equal("Failed", result.Status);
        Assert.Equal("ItemLocation", issue.Entity);
        Assert.Contains("open reservation sources", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataQualityAudit_ShouldFlagMultiplePositiveStockKeysInOneLocation()
    {
        await using var db = CreateDb(nameof(DataQualityAudit_ShouldFlagMultiplePositiveStockKeysInOneLocation));
        SeedTopology(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "MIX-A", ItemName = "Mixed A", BaseUomId = 1, CurrentStock = 5, IsActive = true },
            new Item { ItemId = 2, ItemCode = "MIX-B", ItemName = "Mixed B", BaseUomId = 1, CurrentStock = 3, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 1, ItemId = 1, LocationId = 1, Quantity = 5 },
            new ItemLocation { ItemLocationId = 2, ItemId = 2, LocationId = 1, Quantity = 3 });
        db.SkipAudit = true;
        await db.SaveChangesAsync();

        var result = await new Tier1DataQualityAuditService(db).RunAsync();
        var issue = Assert.Single(result.Issues, item => item.Code == "LOCATION_MULTIPLE_STOCK_KEYS");

        Assert.Equal("Failed", result.Status);
        Assert.Equal("Location", issue.Entity);
        Assert.Contains("MIX-A", issue.Message, StringComparison.Ordinal);
        Assert.Contains("MIX-B", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataQualityAudit_ShouldAllowMultipleSkusWhenLocationExplicitlyAllowsMixing()
    {
        await using var db = CreateDb(nameof(DataQualityAudit_ShouldAllowMultipleSkusWhenLocationExplicitlyAllowsMixing));
        SeedTopology(db);
        var location = db.Locations.Local.Single(row => row.LocationId == 1);
        location.AllowMixedSku = true;
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "MIX-A", ItemName = "Mixed A", BaseUomId = 1, CurrentStock = 5, IsActive = true },
            new Item { ItemId = 2, ItemCode = "MIX-B", ItemName = "Mixed B", BaseUomId = 1, CurrentStock = 3, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 1, ItemId = 1, LocationId = 1, Quantity = 5 },
            new ItemLocation { ItemLocationId = 2, ItemId = 2, LocationId = 1, Quantity = 3 });
        db.SkipAudit = true;
        await db.SaveChangesAsync();

        var result = await new Tier1DataQualityAuditService(db).RunAsync();

        Assert.DoesNotContain(result.Issues, item => item.Code == "LOCATION_MULTIPLE_STOCK_KEYS");
    }

    [Fact]
    public void Tier1EvidenceGateScript_ShouldCollectRequiredGatesWithoutAutoSeed()
    {
        var root = FindRepositoryRoot();
        var script = Read(Path.Combine(root, "scripts", "Invoke-Tier1EvidenceGate.ps1"));

        Assert.Contains("dotnet build WMS.sln --no-restore -v:minimal", script, StringComparison.Ordinal);
        Assert.Contains("dotnet test WMS.Tests\\WMS.Tests.csproj --no-restore", script, StringComparison.Ordinal);
        Assert.Contains("dotnet list WMS.sln package --vulnerable --include-transitive --no-restore", script, StringComparison.Ordinal);
        Assert.Contains("npm audit --json", script, StringComparison.Ordinal);
        Assert.Contains("Run-WmsVerification.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Build-ProductionPackage.ps1", script, StringComparison.Ordinal);
        Assert.Contains("WMS_DATA_QUALITY_AUDIT_URL", script, StringComparison.Ordinal);
        Assert.Contains("WebRequestSession", script, StringComparison.Ordinal);
        Assert.Contains("$headerName -ieq \"Cookie\"", script, StringComparison.Ordinal);
        Assert.Contains("RequireExternalEvidence", script, StringComparison.Ordinal);
        Assert.Contains("productionTier1CanBeMarked100", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SeedData", script, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalVerificationSeeder", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionEvidenceChecklist_ShouldUseAuditablePassBlockedFailedStatuses()
    {
        var root = FindRepositoryRoot();
        var checklist = Read(Path.Combine(root, "docs", "TIER1_PRODUCTION_EVIDENCE_CHECKLIST_2026_05_29.md"));

        foreach (var required in new[]
        {
            "Pass",
            "Blocked",
            "Failed",
            "HW-RF-001",
            "LOAD-001",
            "DR-001",
            "INT-ERP-001",
            "OBS-001",
            "96/100 repo/local readiness",
            "89-91% Tier-1 production equivalence"
        })
        {
            Assert.Contains(required, checklist, StringComparison.Ordinal);
        }

        Assert.Contains("Production Tier-1 can be marked **100%** only when every local gate and every external evidence ID is `Pass`.", checklist, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemController_ShouldExposeReadOnlyDataQualityAuditAndNotSeedRuntimeData()
    {
        var methods = typeof(SystemController).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var dataQualityAudit = methods.Single(m => m.Name == nameof(SystemController.DataQualityAudit));

        Assert.NotNull(dataQualityAudit.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(dataQualityAudit.GetCustomAttribute<HttpPostAttribute>());
        Assert.DoesNotContain(methods, m => m.Name == "SeedData");
    }

    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name + "-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedTopology(AppDbContext db)
    {
        db.Warehouses.Add(new Warehouse { WarehouseId = 1, WarehouseCode = "WH1", WarehouseName = "Main warehouse", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "STO", ZoneName = "Storage", IsActive = true });
        db.Locations.Add(new Location { LocationId = 1, ZoneId = 1, LocationCode = "A-01-01", IsActive = true });
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    private static string Read(string path) => File.ReadAllText(path);
}
