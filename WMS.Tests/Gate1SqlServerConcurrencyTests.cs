using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Models;

namespace WMS.Tests;

public sealed class Gate1SqlServerConcurrencyTests
{
    private const string ConnectionEnvironmentVariable = "WMS_GATE1_SQLSERVER_TEST_CONNECTION";

    [Fact]
    public async Task ConcurrentInventoryAndVoucherCommands_ShouldConflictWithoutPartialWrites()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var connection = ValidateDisposableLocalConnection(connectionString);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection.ConnectionString)
            .Options;

        await using var lifecycle = new AppDbContext(options) { SkipAudit = true };
        try
        {
            await lifecycle.Database.EnsureDeletedAsync();
            await lifecycle.Database.MigrateAsync();
            var seed = await SeedAsync(options);

            await AssertConcurrentReservationAsync(options, seed);
            await AssertConcurrentOutboundAsync(options, seed);
            await AssertCountVersusOutboundAsync(options, seed);
            await AssertTransferVersusOutboundAsync(options, seed);
            await AssertConcurrentVoucherEditAsync(options, seed);
        }
        finally
        {
            await lifecycle.Database.CloseConnectionAsync();
            await lifecycle.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertConcurrentReservationAsync(DbContextOptions<AppDbContext> options, Gate1Seed seed)
    {
        await using var first = CreateRuntimeContext(options, "AUDIT_TEST_GATE1_RESERVE_FIRST");
        await using var second = CreateRuntimeContext(options, "AUDIT_TEST_GATE1_RESERVE_SECOND");
        var firstStock = await first.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.ReservationStockId);
        var secondStock = await second.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.ReservationStockId);
        firstStock.ReservedQty = 80m;
        secondStock.ReservedQty = 80m;
        first.CurrentInventoryTransactionContext = LedgerContext(seed, "reserve-first", InventoryTransactionTypeEnum.Pick);
        second.CurrentInventoryTransactionContext = LedgerContext(seed, "reserve-second", InventoryTransactionTypeEnum.Pick);

        var outcomes = await SaveConcurrentlyAsync(first, second);

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        var stock = await verify.ItemLocations.AsNoTracking().SingleAsync(x => x.ItemLocationId == seed.ReservationStockId);
        var ledgerCount = await verify.InventoryTransactions.CountAsync(x =>
            x.ItemId == seed.ItemId && x.LocationId == seed.ReservationLocationId);
        var ledgerMetadata = await verify.InventoryTransactions
            .Where(x => x.ItemId == seed.ItemId && x.LocationId == seed.ReservationLocationId)
            .Select(x => x.MetadataJson)
            .SingleAsync();
        Assert.Equal(1, outcomes.Count(x => x));
        Assert.Equal(1, outcomes.Count(x => !x));
        Assert.Equal(80m, stock.ReservedQty);
        Assert.True(stock.ReservedQty <= stock.Quantity);
        Assert.Equal(1, ledgerCount);
        Assert.Contains("AUDIT_TEST_GATE1_RESERVE_", ledgerMetadata, StringComparison.Ordinal);
        Assert.Contains("correlationId", ledgerMetadata, StringComparison.Ordinal);
    }

    private static async Task AssertConcurrentOutboundAsync(DbContextOptions<AppDbContext> options, Gate1Seed seed)
    {
        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        var firstStock = await first.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.OutboundStockId);
        var secondStock = await second.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.OutboundStockId);
        firstStock.Quantity = 3m;
        secondStock.Quantity = 3m;
        first.CurrentInventoryTransactionContext = LedgerContext(seed, "outbound-first", InventoryTransactionTypeEnum.Ship);
        second.CurrentInventoryTransactionContext = LedgerContext(seed, "outbound-second", InventoryTransactionTypeEnum.Ship);

        var outcomes = await SaveConcurrentlyAsync(first, second);

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        var stock = await verify.ItemLocations.AsNoTracking().SingleAsync(x => x.ItemLocationId == seed.OutboundStockId);
        var ledgerCount = await verify.InventoryTransactions.CountAsync(x =>
            x.ItemId == seed.ItemId && x.LocationId == seed.OutboundLocationId);
        Assert.Equal(1, outcomes.Count(x => x));
        Assert.Equal(1, outcomes.Count(x => !x));
        Assert.Equal(3m, stock.Quantity);
        Assert.True(stock.Quantity >= 0m);
        Assert.Equal(1, ledgerCount);
    }

    private static async Task AssertCountVersusOutboundAsync(DbContextOptions<AppDbContext> options, Gate1Seed seed)
    {
        await using var countContext = new AppDbContext(options);
        await using var outboundContext = new AppDbContext(options);
        var countStock = await countContext.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.CountStockId);
        var outboundStock = await outboundContext.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.CountStockId);
        countStock.Quantity = 18m;
        outboundStock.Quantity = 15m;
        countContext.CurrentInventoryTransactionContext = LedgerContext(seed, "count", InventoryTransactionTypeEnum.Adjust);
        outboundContext.CurrentInventoryTransactionContext = LedgerContext(seed, "count-race-outbound", InventoryTransactionTypeEnum.Ship);

        var outcomes = await SaveConcurrentlyAsync(countContext, outboundContext);

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        var stock = await verify.ItemLocations.AsNoTracking().SingleAsync(x => x.ItemLocationId == seed.CountStockId);
        var ledgerCount = await verify.InventoryTransactions.CountAsync(x =>
            x.ItemId == seed.ItemId && x.LocationId == seed.CountLocationId);
        Assert.Equal(1, outcomes.Count(x => x));
        Assert.Equal(1, outcomes.Count(x => !x));
        Assert.Contains(stock.Quantity, new[] { 18m, 15m });
        Assert.Equal(1, ledgerCount);
    }

    private static async Task AssertTransferVersusOutboundAsync(DbContextOptions<AppDbContext> options, Gate1Seed seed)
    {
        await using var transferContext = new AppDbContext(options);
        await using var outboundContext = new AppDbContext(options);
        var transferSource = await transferContext.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.TransferSourceStockId);
        var transferDestination = await transferContext.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.TransferDestinationStockId);
        var outboundSource = await outboundContext.ItemLocations.SingleAsync(x => x.ItemLocationId == seed.TransferSourceStockId);
        transferSource.Quantity = 10m;
        transferDestination.Quantity = 10m;
        outboundSource.Quantity = 5m;
        transferContext.CurrentInventoryTransactionContext = LedgerContext(
            seed,
            "transfer",
            InventoryTransactionTypeEnum.TransferOut,
            forceTransactionType: false);
        outboundContext.CurrentInventoryTransactionContext = LedgerContext(seed, "transfer-race-outbound", InventoryTransactionTypeEnum.Ship);

        var outcomes = await SaveConcurrentlyAsync(transferContext, outboundContext);

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        var source = await verify.ItemLocations.AsNoTracking().SingleAsync(x => x.ItemLocationId == seed.TransferSourceStockId);
        var destination = await verify.ItemLocations.AsNoTracking().SingleAsync(x => x.ItemLocationId == seed.TransferDestinationStockId);
        var isCompleteTransfer = source.Quantity == 10m && destination.Quantity == 10m;
        var isCompleteOutbound = source.Quantity == 5m && destination.Quantity == 0m;
        Assert.Equal(1, outcomes.Count(x => x));
        Assert.Equal(1, outcomes.Count(x => !x));
        Assert.True(isCompleteTransfer || isCompleteOutbound,
            $"Expected an atomic transfer or outbound result, actual source={source.Quantity}, destination={destination.Quantity}.");
    }

    private static async Task AssertConcurrentVoucherEditAsync(DbContextOptions<AppDbContext> options, Gate1Seed seed)
    {
        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        var firstVoucher = await first.Vouchers.SingleAsync(x => x.VoucherId == seed.VoucherId);
        var secondVoucher = await second.Vouchers.SingleAsync(x => x.VoucherId == seed.VoucherId);
        firstVoucher.Description = "AUDIT_TEST_GATE1_FIRST_EDIT";
        secondVoucher.Description = "AUDIT_TEST_GATE1_SECOND_EDIT";

        var outcomes = await SaveConcurrentlyAsync(first, second);

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        var voucher = await verify.Vouchers.AsNoTracking().SingleAsync(x => x.VoucherId == seed.VoucherId);
        Assert.Equal(1, outcomes.Count(x => x));
        Assert.Equal(1, outcomes.Count(x => !x));
        Assert.Contains(voucher.Description, new[] { "AUDIT_TEST_GATE1_FIRST_EDIT", "AUDIT_TEST_GATE1_SECOND_EDIT" });
        Assert.NotNull(voucher.UpdatedAt);
    }

    private static async Task<bool[]> SaveConcurrentlyAsync(AppDbContext first, AppDbContext second)
    {
        var firstSave = SaveWithConcurrencyResultAsync(first);
        var secondSave = SaveWithConcurrencyResultAsync(second);
        return await Task.WhenAll(firstSave, secondSave);
    }

    private static async Task<bool> SaveWithConcurrencyResultAsync(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static AppDbContext CreateRuntimeContext(
        DbContextOptions<AppDbContext> options,
        string traceIdentifier)
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = traceIdentifier
        };
        httpContext.Request.Path = "/AUDIT_TEST_GATE1/concurrency";
        return new AppDbContext(options, new HttpContextAccessor { HttpContext = httpContext });
    }

    private static InventoryTransactionContext LedgerContext(
        Gate1Seed seed,
        string operation,
        InventoryTransactionTypeEnum transactionType,
        bool forceTransactionType = true)
        => new()
        {
            TransactionType = transactionType,
            ForceTransactionType = forceTransactionType,
            TransactionGroupKey = $"AUDIT_TEST_GATE1_{operation}",
            IdempotencyKeyPrefix = $"AUDIT_TEST_GATE1_{operation}_{Guid.NewGuid():N}",
            WarehouseId = seed.WarehouseId,
            Actor = "AUDIT_TEST_GATE1",
            ReferenceType = "Gate1ConcurrencyTest",
            ReferenceId = operation,
            ReferenceCode = "AUDIT_TEST_GATE1"
        };

    private static async Task<Gate1Seed> SeedAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options) { SkipAudit = true };
        var warehouse = new Warehouse
        {
            WarehouseCode = "AUDIT_TEST_GATE1_WH",
            WarehouseName = "Audit Test Gate 1 Warehouse",
            Address = "Local SQL Server only"
        };
        var zone = new Zone
        {
            Warehouse = warehouse,
            ZoneCode = "AUDIT_TEST_G1_ZONE",
            ZoneName = "Audit Test Gate 1 Zone"
        };
        var locationCodes = new[] { "RESERVE", "OUTBOUND", "COUNT", "TRANSFER_SOURCE", "TRANSFER_DEST" };
        var locations = locationCodes
            .Select(code => new Location
            {
                Zone = zone,
                LocationCode = $"AUDIT_TEST_GATE1_{code}",
                MaxCapacity = 1000m
            })
            .ToArray();
        var uom = new UnitOfMeasure
        {
            UomCode = "ATG1",
            UomName = "Audit Test Gate 1 Unit"
        };
        var category = new ItemCategory
        {
            CategoryCode = "AUDIT_TEST_GATE1_CAT",
            CategoryName = "Audit Test Gate 1 Category"
        };
        var item = new Item
        {
            ItemCode = "AUDIT_TEST_GATE1_ITEM",
            ItemName = "Audit Test Gate 1 Item",
            BaseUom = uom,
            Category = category,
            CurrentStock = 150m,
            CreatedBy = "AUDIT_TEST_GATE1"
        };
        db.AddRange(warehouse, zone);
        db.Locations.AddRange(locations);
        db.AddRange(uom, category, item);
        await db.SaveChangesAsync();

        var stocks = new[]
        {
            new ItemLocation { ItemId = item.ItemId, LocationId = locations[0].LocationId, Quantity = 100m },
            new ItemLocation { ItemId = item.ItemId, LocationId = locations[1].LocationId, Quantity = 10m },
            new ItemLocation { ItemId = item.ItemId, LocationId = locations[2].LocationId, Quantity = 20m },
            new ItemLocation { ItemId = item.ItemId, LocationId = locations[3].LocationId, Quantity = 20m },
            new ItemLocation { ItemId = item.ItemId, LocationId = locations[4].LocationId, Quantity = 0m }
        };
        db.ItemLocations.AddRange(stocks);
        var voucher = new Voucher
        {
            VoucherCode = "AUDIT_TEST_GATE1_VOUCHER",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = warehouse.WarehouseId,
            CreatedBy = "AUDIT_TEST_GATE1",
            Description = "AUDIT_TEST_GATE1_ORIGINAL"
        };
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();

        return new Gate1Seed(
            warehouse.WarehouseId,
            item.ItemId,
            stocks[0].ItemLocationId,
            stocks[1].ItemLocationId,
            stocks[2].ItemLocationId,
            stocks[3].ItemLocationId,
            stocks[4].ItemLocationId,
            locations[0].LocationId,
            locations[1].LocationId,
            locations[2].LocationId,
            voucher.VoucherId);
    }

    private static SqlConnectionStringBuilder ValidateDisposableLocalConnection(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        Assert.True(IsLocalSqlServer(connection.DataSource),
            "Gate 1 SQL integration test refuses a non-local SQL Server.");
        Assert.StartsWith("AUDIT_TEST_", connection.InitialCatalog, StringComparison.Ordinal);
        return connection;
    }

    private static bool IsLocalSqlServer(string dataSource)
    {
        var trimmed = dataSource.Trim();
        if (trimmed == "." || trimmed.StartsWith(@".\", StringComparison.Ordinal))
            return true;

        var host = trimmed.Split('\\', 2)[0].Split(',', 2)[0].Trim();
        if (host.Equals("(local)", StringComparison.OrdinalIgnoreCase)
            || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var address))
            return IPAddress.IsLoopback(address);

        try
        {
            return Dns.GetHostAddresses(host).Any(IPAddress.IsLoopback);
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private sealed record Gate1Seed(
        int WarehouseId,
        int ItemId,
        int ReservationStockId,
        int OutboundStockId,
        int CountStockId,
        int TransferSourceStockId,
        int TransferDestinationStockId,
        int ReservationLocationId,
        int OutboundLocationId,
        int CountLocationId,
        long VoucherId);
}
