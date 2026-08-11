using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public sealed class OutboundPostingSqlServerRegressionTests
{
    private const string ConnectionEnvironmentVariable = "WMS_OUTBOUND_SQLSERVER_TEST_CONNECTION";

    [Fact]
    public async Task PostReservedOutbound_ShouldPersistQuantityAndClosedReservationAtomically()
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
            var seed = await SeedOutboundAsync(options);

            await using (var db = new AppDbContext(options))
            {
                var unitOfWork = new EfUnitOfWork(db);
                var transactionService = new InventoryTransactionService(db);
                var reservationService = new InventoryReservationService(db, transactionService);
                var outbound = new OutboundExecutionService(
                    db,
                    unitOfWork,
                    reservationService,
                    new InventoryBalanceService(db),
                    new SerialInventoryService(db),
                    transactionService);

                var result = await outbound.PostReservedOutboundAsync(
                    seed.VoucherId,
                    cancelRemaining: false,
                    seed.WarehouseId,
                    "AUDIT_TEST_OUTBOUND_POSTER",
                    IPAddress.Loopback.ToString());

                Assert.True(result.Succeeded, result.Message);
            }

            await using var verify = new AppDbContext(options) { SkipAudit = true };
            var stock = await verify.ItemLocations.AsNoTracking()
                .SingleAsync(x => x.ItemLocationId == seed.ItemLocationId);
            var reservation = await verify.StockReservations.AsNoTracking()
                .SingleAsync(x => x.StockReservationId == seed.StockReservationId);
            var voucher = await verify.Vouchers.AsNoTracking()
                .SingleAsync(x => x.VoucherId == seed.VoucherId);
            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(x => x.VoucherId == seed.VoucherId)
                .ToListAsync();

            Assert.Equal(30m, stock.Quantity);
            Assert.Equal(0m, stock.ReservedQty);
            Assert.Equal(100m, reservation.ConsumedQty);
            Assert.Equal(ReservationStatusEnum.Consumed, reservation.Status);
            Assert.True(voucher.IsPosted);
            Assert.Equal(FulfillmentStatusEnum.Completed, voucher.FulfillmentStatus);
            Assert.Equal(-100m, ledger.Sum(x => x.QuantityDelta));
            Assert.Equal(-100m, ledger.Sum(x => x.ReservedDelta));
        }
        finally
        {
            await lifecycle.Database.CloseConnectionAsync();
            await lifecycle.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task PartialPostWithCancelRemaining_ShouldPersistPickedAndReleasedQuantitiesAtomically()
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
            var seed = await SeedOutboundAsync(options);

            await using (var prepare = new AppDbContext(options) { SkipAudit = true })
            {
                var preparedVoucher = await prepare.Vouchers.SingleAsync(v => v.VoucherId == seed.VoucherId);
                preparedVoucher.PartialShipmentAllowed = true;

                var task = await prepare.PickTasks.SingleAsync(t => t.VoucherId == seed.VoucherId);
                task.PickedQty = 40m;
                task.Status = PickTaskStatusEnum.Short;

                var allocation = await prepare.PickTaskAllocations.SingleAsync(a => a.VoucherId == seed.VoucherId);
                allocation.PickedQty = 40m;
                await prepare.SaveChangesAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var unitOfWork = new EfUnitOfWork(db);
                var transactionService = new InventoryTransactionService(db);
                var reservationService = new InventoryReservationService(db, transactionService);
                var outbound = new OutboundExecutionService(
                    db,
                    unitOfWork,
                    reservationService,
                    new InventoryBalanceService(db),
                    new SerialInventoryService(db),
                    transactionService);

                var result = await outbound.PostReservedOutboundAsync(
                    seed.VoucherId,
                    cancelRemaining: true,
                    seed.WarehouseId,
                    "AUDIT_TEST_PARTIAL_CANCEL_POSTER",
                    IPAddress.Loopback.ToString());
                Assert.True(result.Succeeded, result.Message);

                var retry = await outbound.PostReservedOutboundAsync(
                    seed.VoucherId,
                    cancelRemaining: true,
                    seed.WarehouseId,
                    "AUDIT_TEST_PARTIAL_CANCEL_POSTER",
                    IPAddress.Loopback.ToString());
                Assert.False(retry.Succeeded);
            }

            await using var verify = new AppDbContext(options) { SkipAudit = true };
            var stock = await verify.ItemLocations.AsNoTracking()
                .SingleAsync(x => x.ItemLocationId == seed.ItemLocationId);
            var reservation = await verify.StockReservations.AsNoTracking()
                .SingleAsync(x => x.StockReservationId == seed.StockReservationId);
            var verifiedVoucher = await verify.Vouchers.AsNoTracking()
                .SingleAsync(x => x.VoucherId == seed.VoucherId);
            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(x => x.VoucherId == seed.VoucherId
                    && x.TransactionType == InventoryTransactionTypeEnum.Ship)
                .ToListAsync();

            Assert.Equal(90m, stock.Quantity);
            Assert.Equal(0m, stock.ReservedQty);
            Assert.Equal(40m, reservation.ConsumedQty);
            Assert.Equal(60m, reservation.ReleasedQty);
            Assert.Equal(ReservationStatusEnum.Consumed, reservation.Status);
            Assert.Equal(reservation.ReservedQty, reservation.ConsumedQty + reservation.ReleasedQty);
            Assert.True(verifiedVoucher.IsPosted);
            Assert.Equal(FulfillmentStatusEnum.Completed, verifiedVoucher.FulfillmentStatus);
            Assert.False(await verify.Vouchers.AnyAsync(v => v.ParentVoucherId == seed.VoucherId));
            Assert.Equal(-40m, Assert.Single(ledger).QuantityDelta);
        }
        finally
        {
            await lifecycle.Database.CloseConnectionAsync();
            await lifecycle.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task PostedOutboundCancellation_ShouldReverseStockAndLedgerExactlyOnce()
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
            var seed = await SeedOutboundAsync(options);

            await using (var db = new AppDbContext(options))
            {
                var unitOfWork = new EfUnitOfWork(db);
                var transactionService = new InventoryTransactionService(db);
                var reservationService = new InventoryReservationService(db, transactionService);
                var balanceService = new InventoryBalanceService(db);
                var serialService = new SerialInventoryService(db);
                var outbound = new OutboundExecutionService(
                    db,
                    unitOfWork,
                    reservationService,
                    balanceService,
                    serialService,
                    transactionService);
                var cancellation = new VoucherCancellationService(
                    db,
                    unitOfWork,
                    reservationService,
                    balanceService,
                    serialService,
                    transactionService);

                var posted = await outbound.PostReservedOutboundAsync(
                    seed.VoucherId,
                    cancelRemaining: false,
                    seed.WarehouseId,
                    "AUDIT_TEST_OUTBOUND_POSTER",
                    IPAddress.Loopback.ToString());
                Assert.True(posted.Succeeded, posted.Message);

                var cancelled = await cancellation.CancelVoucherAsync(
                    seed.VoucherId,
                    "AUDIT_TEST_REVERSAL",
                    CancelReasonEnum.OperationalError,
                    seed.WarehouseId,
                    "AUDIT_TEST_REVERSAL_MANAGER",
                    IPAddress.Loopback.ToString(),
                    null);
                Assert.True(cancelled.Succeeded, cancelled.Message);

                await Assert.ThrowsAsync<BusinessRuleException>(() => cancellation.CancelVoucherAsync(
                    seed.VoucherId,
                    "AUDIT_TEST_REVERSAL",
                    CancelReasonEnum.OperationalError,
                    seed.WarehouseId,
                    "AUDIT_TEST_REVERSAL_MANAGER",
                    IPAddress.Loopback.ToString(),
                    null));
            }

            await using var verify = new AppDbContext(options) { SkipAudit = true };
            var stock = await verify.ItemLocations.AsNoTracking()
                .SingleAsync(x => x.ItemLocationId == seed.ItemLocationId);
            var reservation = await verify.StockReservations.AsNoTracking()
                .SingleAsync(x => x.StockReservationId == seed.StockReservationId);
            var voucher = await verify.Vouchers.AsNoTracking()
                .SingleAsync(x => x.VoucherId == seed.VoucherId);
            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(x => x.VoucherId == seed.VoucherId)
                .OrderBy(x => x.InventoryTransactionId)
                .ToListAsync();

            Assert.Equal(130m, stock.Quantity);
            Assert.Equal(0m, stock.ReservedQty);
            Assert.Equal(0m, reservation.ConsumedQty);
            Assert.Equal(100m, reservation.ReleasedQty);
            Assert.Equal(ReservationStatusEnum.Released, reservation.Status);
            Assert.True(voucher.IsPosted);
            Assert.True(voucher.IsCancelled);
            Assert.Equal("AUDIT_TEST_REVERSAL_MANAGER", voucher.CancelledBy);
            Assert.Equal("AUDIT_TEST_REVERSAL", voucher.CancelReason);
            Assert.Equal(CancelReasonEnum.OperationalError, voucher.CancelReasonCode);

            var ship = Assert.Single(ledger, x => x.TransactionType == InventoryTransactionTypeEnum.Ship);
            var reversal = Assert.Single(ledger, x => x.TransactionType == InventoryTransactionTypeEnum.Cancel);
            Assert.Equal(-100m, ship.QuantityDelta);
            Assert.Equal(100m, reversal.QuantityDelta);
            Assert.Equal(0m, ledger.Sum(x => x.QuantityDelta));
            using var metadata = JsonDocument.Parse(reversal.MetadataJson);
            Assert.Equal("AUDIT_TEST_REVERSAL", metadata.RootElement.GetProperty("cancelReason").GetString());
            Assert.Contains(
                ship.InventoryTransactionId,
                metadata.RootElement.GetProperty("originalInventoryTransactionIds")
                    .EnumerateArray()
                    .Select(value => value.GetInt64()));
        }
        finally
        {
            await lifecycle.Database.CloseConnectionAsync();
            await lifecycle.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CreateWave_ShouldRollbackNonPartialShortageAndCapPartialReservationsOnSqlServer()
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
            var seed = await SeedWaveAllocationAsync(options);

            await using (var db = new AppDbContext(options))
            {
                var unitOfWork = new EfUnitOfWork(db);
                var transactionService = new InventoryTransactionService(db);
                var reservationService = new InventoryReservationService(db, transactionService);
                var outbound = new OutboundExecutionService(
                    db,
                    unitOfWork,
                    reservationService,
                    new InventoryBalanceService(db),
                    new SerialInventoryService(db),
                    transactionService);

                var rejected = await outbound.CreateWaveAsync(
                    "Standard", null, null, null, null, WavePriorityEnum.Normal,
                    seed.NonPartialVoucherIds, "AUDIT_TEST_NON_PARTIAL_SQL", seed.WarehouseId, "AUDIT_TEST_PICKER");
                Assert.False(rejected.Succeeded);
                Assert.False(unitOfWork.HasActiveTransaction);

                var accepted = await outbound.CreateWaveAsync(
                    "Standard", null, null, null, null, WavePriorityEnum.Normal,
                    seed.PartialVoucherIds, "AUDIT_TEST_PARTIAL_SQL", seed.WarehouseId, "AUDIT_TEST_PICKER");
                Assert.True(accepted.Succeeded, accepted.Message);
                Assert.False(unitOfWork.HasActiveTransaction);
            }

            await using var verify = new AppDbContext(options) { SkipAudit = true };
            var rejectedVouchers = await verify.Vouchers.AsNoTracking()
                .Where(v => seed.NonPartialVoucherIds.Contains(v.VoucherId))
                .ToListAsync();
            Assert.All(rejectedVouchers, voucher =>
            {
                Assert.Null(voucher.WaveId);
                Assert.NotEqual(FulfillmentStatusEnum.WaitingForPick, voucher.FulfillmentStatus);
            });
            Assert.Empty(await verify.StockReservations.AsNoTracking()
                .Where(r => seed.NonPartialVoucherIds.Contains(r.VoucherId))
                .ToListAsync());

            var partialReservations = await verify.StockReservations.AsNoTracking()
                .Where(r => seed.PartialVoucherIds.Contains(r.VoucherId))
                .ToListAsync();
            Assert.Equal(10m, partialReservations.Sum(r => r.ReservedQty));
            Assert.All(partialReservations, reservation => Assert.Equal(ReservationStatusEnum.Active, reservation.Status));

            var partialStock = await verify.ItemLocations.AsNoTracking()
                .SingleAsync(x => x.ItemLocationId == seed.PartialItemLocationId);
            Assert.Equal(10m, partialStock.Quantity);
            Assert.Equal(10m, partialStock.ReservedQty);
            Assert.Equal(0m, partialStock.Quantity - partialStock.ReservedQty);

            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(x => x.ItemId == seed.PartialItemId)
                .ToListAsync();
            Assert.Equal(10m, ledger.Sum(x => x.ReservedDelta));
            Assert.Equal(-10m, ledger.Sum(x => x.AvailableDelta));
        }
        finally
        {
            await lifecycle.Database.CloseConnectionAsync();
            await lifecycle.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task PartialPost_ShouldPersistRepresentableBaseUomBackorderOnSqlServer()
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
            var seed = await SeedPartialBackorderAsync(options);

            await using (var db = new AppDbContext(options))
            {
                var unitOfWork = new EfUnitOfWork(db);
                var transactionService = new InventoryTransactionService(db);
                var reservationService = new InventoryReservationService(db, transactionService);
                var outbound = new OutboundExecutionService(
                    db,
                    unitOfWork,
                    reservationService,
                    new InventoryBalanceService(db),
                    new SerialInventoryService(db),
                    transactionService);

                var result = await outbound.PostReservedOutboundAsync(
                    seed.VoucherId,
                    cancelRemaining: false,
                    seed.WarehouseId,
                    "AUDIT_TEST_PARTIAL_POSTER",
                    IPAddress.Loopback.ToString());
                Assert.True(result.Succeeded, result.Message);
            }

            await using var verify = new AppDbContext(options) { SkipAudit = true };
            var parent = await verify.Vouchers.AsNoTracking().SingleAsync(v => v.VoucherId == seed.VoucherId);
            var backorder = await verify.Vouchers.AsNoTracking()
                .Include(v => v.Details)
                .SingleAsync(v => v.ParentVoucherId == seed.VoucherId);
            var line = Assert.Single(backorder.Details);
            var stock = await verify.ItemLocations.AsNoTracking().SingleAsync(il => il.ItemLocationId == seed.ItemLocationId);

            Assert.True(parent.IsPosted);
            Assert.Equal(0.0002m, stock.Quantity);
            Assert.Equal(0m, stock.ReservedQty);
            Assert.Equal(0.0002m, line.TransactionQty);
            Assert.Equal(seed.BaseUomId, line.TransactionUomId);
            Assert.Equal(1m, line.ConversionRate);
            Assert.Equal(0.0002m, line.BaseQty);
            Assert.Equal(1, await verify.Vouchers.CountAsync(v => v.ParentVoucherId == seed.VoucherId));
        }
        finally
        {
            await lifecycle.Database.CloseConnectionAsync();
            await lifecycle.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<OutboundSeed> SeedOutboundAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options) { SkipAudit = true };
        var warehouse = new Warehouse
        {
            WarehouseCode = "ATOP-WH",
            WarehouseName = "Kho thử nghiệm chốt xuất",
            Address = "Local SQL Server only",
            IsActive = true
        };
        var zone = new Zone
        {
            Warehouse = warehouse,
            ZoneCode = "ATOP-ZONE",
            ZoneName = "Khu thử nghiệm chốt xuất",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        };
        var location = new Location
        {
            Zone = zone,
            LocationCode = "ATOP-BIN-01",
            MaxCapacity = 1000m,
            IsActive = true
        };
        var uom = new UnitOfMeasure
        {
            UomCode = "ATOP",
            UomName = "Đơn vị thử nghiệm",
            IsActive = true
        };
        var category = new ItemCategory
        {
            CategoryCode = "ATOP-CAT",
            CategoryName = "Danh mục thử nghiệm",
            IsActive = true
        };
        var item = new Item
        {
            ItemCode = "ATOP-ITEM",
            ItemName = "Vật tư thử nghiệm chốt xuất",
            BaseUom = uom,
            Category = category,
            CurrentStock = 130m,
            IsActive = true,
            CreatedBy = "AUDIT_TEST_OUTBOUND"
        };
        db.AddRange(warehouse, zone, location, uom, category, item);
        await db.SaveChangesAsync();

        var stock = new ItemLocation
        {
            ItemId = item.ItemId,
            LocationId = location.LocationId,
            Quantity = 130m,
            ReservedQty = 100m,
            HoldStatus = InventoryHoldStatusEnum.Available
        };
        var voucher = new Voucher
        {
            VoucherCode = "ATOP-PX",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = warehouse.WarehouseId,
            CreatedBy = "AUDIT_TEST_MAKER",
            FulfillmentStatus = FulfillmentStatusEnum.Picking,
            PartialShipmentAllowed = false
        };
        var detail = new VoucherDetail
        {
            Voucher = voucher,
            ItemId = item.ItemId,
            LocationId = location.LocationId,
            TransactionQty = 100m,
            TransactionUomId = uom.UomId,
            ConversionRate = 1m,
            BaseQty = 100m,
            LineNumber = 1
        };
        db.AddRange(stock, voucher, detail);
        await db.SaveChangesAsync();

        var reservation = new StockReservation
        {
            VoucherId = voucher.VoucherId,
            VoucherDetailId = detail.VoucherDetailId,
            ItemId = item.ItemId,
            LocationId = location.LocationId,
            ReservedQty = 100m,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "AUDIT_TEST_PICKER"
        };
        var pickTask = new PickTask
        {
            TaskCode = "ATOP-PICK",
            VoucherId = voucher.VoucherId,
            VoucherDetailId = detail.VoucherDetailId,
            ItemId = item.ItemId,
            SourceLocationId = location.LocationId,
            TargetQty = 100m,
            PickedQty = 100m,
            Status = PickTaskStatusEnum.Completed,
            PickTaskMode = PickTaskModeEnum.Single,
            AssignedTo = "AUDIT_TEST_PICKER",
            CompletedAt = DateTime.UtcNow
        };
        db.AddRange(reservation, pickTask);
        await db.SaveChangesAsync();

        db.PickTaskAllocations.Add(new PickTaskAllocation
        {
            PickTaskId = pickTask.PickTaskId,
            StockReservationId = reservation.StockReservationId,
            VoucherId = voucher.VoucherId,
            VoucherDetailId = detail.VoucherDetailId,
            AllocatedQty = 100m,
            PickedQty = 100m
        });
        await db.SaveChangesAsync();

        return new OutboundSeed(
            warehouse.WarehouseId,
            voucher.VoucherId,
            stock.ItemLocationId,
            reservation.StockReservationId);
    }

    private static async Task<PartialBackorderSeed> SeedPartialBackorderAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options) { SkipAudit = true };
        var warehouse = new Warehouse
        {
            WarehouseCode = "AUDIT_TEST_BO_WH",
            WarehouseName = "Kho thử nghiệm phiếu bổ sung",
            Address = "Local SQL Server only",
            IsActive = true
        };
        var zone = new Zone
        {
            Warehouse = warehouse,
            ZoneCode = "AUDIT_TEST_BO_ZONE",
            ZoneName = "Khu thử nghiệm phiếu bổ sung",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        };
        var location = new Location
        {
            Zone = zone,
            LocationCode = "AUDIT_TEST_BO_BIN",
            MaxCapacity = 1000m,
            IsActive = true
        };
        var baseUom = new UnitOfMeasure
        {
            UomCode = "ATBEA",
            UomName = "Đơn vị cơ sở thử nghiệm",
            IsActive = true
        };
        var boxUom = new UnitOfMeasure
        {
            UomCode = "ATBBOX",
            UomName = "Đơn vị phụ thử nghiệm",
            IsActive = true
        };
        var category = new ItemCategory
        {
            CategoryCode = "AUDIT_TEST_BO_CAT",
            CategoryName = "Danh mục phiếu bổ sung",
            IsActive = true
        };
        var item = new Item
        {
            ItemCode = "AUDIT_TEST_BO_ITEM",
            ItemName = "Vật tư phiếu bổ sung",
            BaseUom = baseUom,
            Category = category,
            CurrentStock = 0.0100m,
            UnitCost = 5m,
            IsActive = true,
            CreatedBy = "AUDIT_TEST_BACKORDER"
        };
        db.AddRange(warehouse, zone, location, baseUom, boxUom, category, item);
        await db.SaveChangesAsync();

        var stock = new ItemLocation
        {
            ItemId = item.ItemId,
            LocationId = location.LocationId,
            Quantity = 0.0100m,
            ReservedQty = 0.0100m,
            HoldStatus = InventoryHoldStatusEnum.Available
        };
        var voucher = new Voucher
        {
            VoucherCode = "AUDIT_TEST_BO_PX",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = warehouse.WarehouseId,
            CreatedBy = "AUDIT_TEST_MAKER",
            FulfillmentStatus = FulfillmentStatusEnum.Picking,
            PartialShipmentAllowed = true
        };
        var detail = new VoucherDetail
        {
            Voucher = voucher,
            ItemId = item.ItemId,
            LocationId = location.LocationId,
            TransactionQty = 0.0001m,
            TransactionUomId = boxUom.UomId,
            ConversionRate = 100m,
            BaseQty = 0.0100m,
            UnitPrice = 5m,
            LineAmount = 0.0005m,
            LineNumber = 1
        };
        db.AddRange(stock, voucher, detail);
        await db.SaveChangesAsync();

        var reservation = new StockReservation
        {
            VoucherId = voucher.VoucherId,
            VoucherDetailId = detail.VoucherDetailId,
            ItemId = item.ItemId,
            LocationId = location.LocationId,
            ReservedQty = 0.0100m,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "AUDIT_TEST_PICKER"
        };
        var pickTask = new PickTask
        {
            TaskCode = "AUDIT_TEST_BO_PICK",
            VoucherId = voucher.VoucherId,
            VoucherDetailId = detail.VoucherDetailId,
            ItemId = item.ItemId,
            SourceLocationId = location.LocationId,
            TargetQty = 0.0100m,
            PickedQty = 0.0098m,
            Status = PickTaskStatusEnum.Short,
            PickTaskMode = PickTaskModeEnum.Single,
            AssignedTo = "AUDIT_TEST_PICKER",
            CompletedAt = DateTime.UtcNow
        };
        db.AddRange(reservation, pickTask);
        await db.SaveChangesAsync();

        db.PickTaskAllocations.Add(new PickTaskAllocation
        {
            PickTaskId = pickTask.PickTaskId,
            StockReservationId = reservation.StockReservationId,
            VoucherId = voucher.VoucherId,
            VoucherDetailId = detail.VoucherDetailId,
            AllocatedQty = 0.0100m,
            PickedQty = 0.0098m
        });
        await db.SaveChangesAsync();

        return new PartialBackorderSeed(
            warehouse.WarehouseId,
            voucher.VoucherId,
            stock.ItemLocationId,
            baseUom.UomId);
    }

    private static async Task<WaveAllocationSeed> SeedWaveAllocationAsync(DbContextOptions<AppDbContext> options)
    {
        await using var db = new AppDbContext(options) { SkipAudit = true };
        var warehouse = new Warehouse
        {
            WarehouseCode = "AUDIT_TEST_WAVE_WH",
            WarehouseName = "Kho thử nghiệm cấp phát wave",
            Address = "Local SQL Server only",
            IsActive = true
        };
        var zone = new Zone
        {
            Warehouse = warehouse,
            ZoneCode = "AUDIT_TEST_WAVE_ZONE",
            ZoneName = "Khu thử nghiệm cấp phát wave",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        };
        var location = new Location
        {
            Zone = zone,
            LocationCode = "AUDIT_TEST_WAVE_BIN",
            MaxCapacity = 1000m,
            IsActive = true
        };
        var uom = new UnitOfMeasure
        {
            UomCode = "ATWV",
            UomName = "Đơn vị wave thử nghiệm",
            IsActive = true
        };
        var category = new ItemCategory
        {
            CategoryCode = "AUDIT_TEST_WAVE_CAT",
            CategoryName = "Danh mục wave thử nghiệm",
            IsActive = true
        };
        var nonPartialItem = new Item
        {
            ItemCode = "AUDIT_TEST_WAVE_NON_PARTIAL",
            ItemName = "Vật tư wave không giao thiếu",
            BaseUom = uom,
            Category = category,
            CurrentStock = 10m,
            IsActive = true,
            CreatedBy = "AUDIT_TEST_WAVE"
        };
        var partialItem = new Item
        {
            ItemCode = "AUDIT_TEST_WAVE_PARTIAL",
            ItemName = "Vật tư wave giao từng phần",
            BaseUom = uom,
            Category = category,
            CurrentStock = 10m,
            IsActive = true,
            CreatedBy = "AUDIT_TEST_WAVE"
        };
        db.AddRange(warehouse, zone, location, uom, category, nonPartialItem, partialItem);
        await db.SaveChangesAsync();

        var nonPartialStock = new ItemLocation
        {
            ItemId = nonPartialItem.ItemId,
            LocationId = location.LocationId,
            Quantity = 10m,
            ReservedQty = 0m,
            HoldStatus = InventoryHoldStatusEnum.Available,
            ExpiryDate = DateTime.UtcNow.Date.AddDays(60)
        };
        var partialStock = new ItemLocation
        {
            ItemId = partialItem.ItemId,
            LocationId = location.LocationId,
            Quantity = 10m,
            ReservedQty = 0m,
            HoldStatus = InventoryHoldStatusEnum.Available,
            ExpiryDate = DateTime.UtcNow.Date.AddDays(60)
        };
        db.AddRange(nonPartialStock, partialStock);

        var nonPartialVouchers = CreateWaveVouchers(
            warehouse.WarehouseId, uom.UomId, nonPartialItem.ItemId, "AUDIT_TEST_NP", partialAllowed: false);
        var partialVouchers = CreateWaveVouchers(
            warehouse.WarehouseId, uom.UomId, partialItem.ItemId, "AUDIT_TEST_P", partialAllowed: true);
        db.Vouchers.AddRange(nonPartialVouchers.Concat(partialVouchers));
        await db.SaveChangesAsync();

        return new WaveAllocationSeed(
            warehouse.WarehouseId,
            nonPartialVouchers.Select(v => v.VoucherId).ToArray(),
            partialVouchers.Select(v => v.VoucherId).ToArray(),
            partialItem.ItemId,
            partialStock.ItemLocationId);
    }

    private static Voucher[] CreateWaveVouchers(
        int warehouseId,
        int uomId,
        int itemId,
        string codePrefix,
        bool partialAllowed)
        => Enumerable.Range(1, 2)
            .Select(index =>
            {
                var voucher = new Voucher
                {
                    VoucherCode = $"{codePrefix}_{index}",
                    VoucherType = VoucherTypeEnum.XuatKho,
                    WarehouseId = warehouseId,
                    CreatedBy = "AUDIT_TEST_MAKER",
                    PartialShipmentAllowed = partialAllowed
                };
                voucher.Details.Add(new VoucherDetail
                {
                    ItemId = itemId,
                    TransactionQty = 6m,
                    TransactionUomId = uomId,
                    ConversionRate = 1m,
                    BaseQty = 6m,
                    LineNumber = 1
                });
                return voucher;
            })
            .ToArray();

    private static SqlConnectionStringBuilder ValidateDisposableLocalConnection(string connectionString)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        Assert.True(IsLocalSqlServer(connection.DataSource),
            "Outbound SQL regression test refuses a non-local SQL Server.");
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

    private sealed record OutboundSeed(
        int WarehouseId,
        long VoucherId,
        int ItemLocationId,
        long StockReservationId);

    private sealed record WaveAllocationSeed(
        int WarehouseId,
        long[] NonPartialVoucherIds,
        long[] PartialVoucherIds,
        int PartialItemId,
        int PartialItemLocationId);

    private sealed record PartialBackorderSeed(
        int WarehouseId,
        long VoucherId,
        int ItemLocationId,
        int BaseUomId);
}
