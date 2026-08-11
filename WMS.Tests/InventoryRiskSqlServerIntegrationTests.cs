using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public sealed class InventoryRiskSqlServerIntegrationTests
{
    private const string ConnectionEnvironmentVariable = "WMS_AI2_SQLSERVER_TEST_CONNECTION";

    [Fact]
    public async Task MigrationAndShadowPersistence_ShouldRemainInventoryReadOnly()
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

            var scope = await SeedAsync(options);
            await using var db = new AppDbContext(options) { SkipAudit = true };
            var service = new InventoryRiskScoringService(db);
            Assert.True(await service.IsPersistenceAvailableAsync());
            var quantityBefore = await db.ItemLocations.AsNoTracking().SumAsync(row => row.Quantity);
            var ledgerBefore = await db.InventoryTransactions.CountAsync();

            var result = await service.PersistShadowBatchAsync(new InventoryRiskQuery
            {
                WarehouseId = scope.WarehouseId,
                OwnerPartnerId = scope.OwnerPartnerId,
                AllowedOwnerPartnerIds = [scope.OwnerPartnerId],
                PredictionCutoff = new DateTime(2026, 7, 16, 10, 0, 0, DateTimeKind.Unspecified)
            }, "AUDIT_TEST_AI2_SQL");

            Assert.Equal(1, result.SnapshotCount);
            Assert.Equal(1, await db.InventoryRiskModelVersions.CountAsync());
            Assert.Equal(1, await db.InventoryRiskFeatureSnapshots.CountAsync());
            Assert.Equal(1, await db.InventoryRiskPredictions.CountAsync());
            Assert.Equal(quantityBefore, await db.ItemLocations.AsNoTracking().SumAsync(row => row.Quantity));
            Assert.Equal(ledgerBefore, await db.InventoryTransactions.CountAsync());
            Assert.Equal(0, await db.StockCountSheets.CountAsync());
        }
        finally
        {
            await lifecycle.Database.CloseConnectionAsync();
            await lifecycle.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<ScopeSeed> SeedAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options) { SkipAudit = true };
        var uom = new UnitOfMeasure
        {
            UomCode = "A2U",
            UomName = "Đơn vị kiểm thử AI-2",
            IsActive = true
        };
        var owner = new Partner
        {
            PartnerCode = "A2OWNER",
            PartnerName = "AUDIT_TEST_AI2_OWNER",
            IsActive = true
        };
        var warehouse = new Warehouse
        {
            WarehouseCode = "A2WH",
            WarehouseName = "AUDIT_TEST_AI2_WAREHOUSE",
            IsActive = true
        };
        var zone = new Zone
        {
            Warehouse = warehouse,
            ZoneCode = "A2Z",
            ZoneName = "AUDIT_TEST_AI2_ZONE",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        };
        var location = new Location
        {
            Zone = zone,
            LocationCode = "AUDIT_TEST_AI2_BIN",
            IsActive = true
        };
        var item = new Item
        {
            ItemCode = "AUDIT_TEST_AI2_SKU",
            ItemName = "Vật tư kiểm thử SQL AI-2",
            BaseUom = uom,
            OwnerPartner = owner,
            AbcClass = "A",
            IsActive = true
        };
        db.ItemLocations.Add(new ItemLocation
        {
            Item = item,
            OwnerPartner = owner,
            Location = location,
            Quantity = 25m,
            ReservedQty = 2m,
            UpdatedAt = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Unspecified)
        });
        await db.SaveChangesAsync();
        return new ScopeSeed(warehouse.WarehouseId, owner.PartnerId);
    }

    private static SqlConnectionStringBuilder ValidateDisposableLocalConnection(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        Assert.True(IsLocalSqlServer(connection.DataSource),
            "AI-2 SQL integration test refuses a non-local SQL Server.");
        Assert.StartsWith("AUDIT_TEST_AI2_", connection.InitialCatalog, StringComparison.Ordinal);
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

    private sealed record ScopeSeed(int WarehouseId, int OwnerPartnerId);
}
