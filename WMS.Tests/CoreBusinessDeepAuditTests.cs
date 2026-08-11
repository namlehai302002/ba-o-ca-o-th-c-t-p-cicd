using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public class CoreBusinessDeepAuditTests
{
    [Fact]
    public async Task Inbound_ShouldRejectBadQuantitiesAndScopeThenPostLotExpiryStock()
    {
        await using var db = CreateDb(nameof(Inbound_ShouldRejectBadQuantitiesAndScopeThenPostLotExpiryStock));
        SeedBaseTopology(db);
        AddItem(db, 100, "IN-LOT", trackLot: true, trackExpiry: true);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 100,
            VoucherCode = "PN-DEEP-001",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "creator",
            InboundStatus = InboundStatusEnum.Receiving
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 1001,
            VoucherId = 100,
            ItemId = 100,
            LocationId = 1,
            TransactionQty = 7,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 7,
            UnitPrice = 11,
            LineAmount = 77,
            LotNumber = "LOT-IN",
            ExpiryDate = new DateTime(2027, 1, 31),
            LineNumber = 1
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 101,
            VoucherCode = "PN-DEEP-NEG",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "creator",
            InboundStatus = InboundStatusEnum.Receiving
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 1011,
            VoucherId = 101,
            ItemId = 100,
            LocationId = 1,
            TransactionQty = -1,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = -1,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var scopedResult = await services.Inbound.CompleteInboundAsync(100, scopedWarehouseId: 2, "receiver", null);
        Assert.True(scopedResult.Forbidden);

        var negativeInbound = await services.Inbound.CompleteInboundAsync(101, scopedWarehouseId: 1, "receiver", null);
        Assert.False(negativeInbound.Succeeded);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 101)).IsPosted);

        var result = await services.Inbound.CompleteInboundAsync(100, scopedWarehouseId: 1, "receiver", null);
        Assert.True(result.Succeeded, result.Message);

        var voucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 100);
        var stock = await db.ItemLocations.SingleAsync(il =>
            il.ItemId == 100 && il.LocationId == 1 && il.LotNumber == "LOT-IN" && il.ExpiryDate == new DateTime(2027, 1, 31));

        Assert.True(voucher.IsPosted);
        Assert.Equal(InboundStatusEnum.Completed, voucher.InboundStatus);
        Assert.Equal(7, stock.Quantity);
        Assert.Contains(await db.InventoryTransactions.ToListAsync(), tx =>
            tx.VoucherId == 100 && tx.TransactionType == InventoryTransactionTypeEnum.Receive && tx.QuantityDelta == 7);
    }

    [Fact]
    public async Task PostingServices_ShouldRecheckPeriodLockInsideTheirTransactions()
    {
        await using var db = CreateDb(nameof(PostingServices_ShouldRecheckPeriodLockInsideTheirTransactions));
        SeedBaseTopology(db);
        AddItem(db, 110, "IN-PERIOD-LOCK");
        AddItem(db, 210, "OUT-PERIOD-LOCK");
        AddStock(db, 210, 1, 8, "LOT-PERIOD-LOCK", VietnamTime.Now.Date.AddDays(90));
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 110,
            VoucherCode = "PN-PERIOD-LOCK",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "creator",
            InboundStatus = InboundStatusEnum.Receiving
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 1101,
            VoucherId = 110,
            ItemId = 110,
            LocationId = 3,
            TransactionQty = 3,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 3,
            LineNumber = 1
        });
        AddOutboundVoucher(db, 210, "PX-PERIOD-LOCK", 210, 4, lot: "LOT-PERIOD-LOCK", expiry: VietnamTime.Now.Date.AddDays(90));
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(210, scopedWarehouseId: 1, "picker");
        Assert.True(release.Succeeded, release.Message);
        await ConfirmAllPickTasksAsync(db, services.Outbound, 210, "picker");

        db.WarehousePeriodLocks.Add(new WarehousePeriodLock
        {
            WarehouseId = 1,
            LockDate = VietnamTime.Now.Date,
            IsActive = true,
            LockedBy = "period.manager"
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<WarehouseLockedException>(() =>
            services.Inbound.CompleteInboundAsync(110, scopedWarehouseId: 1, "receiver", null));
        await Assert.ThrowsAsync<WarehouseLockedException>(() =>
            services.Outbound.PostReservedOutboundAsync(210, cancelRemaining: false, scopedWarehouseId: 1, "shipper", null));

        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 110)).IsPosted);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 210)).IsPosted);
        Assert.False(await db.ItemLocations.AnyAsync(il => il.ItemId == 110));
        Assert.Equal(8m, (await db.ItemLocations.SingleAsync(il => il.ItemId == 210)).Quantity);
        Assert.DoesNotContain(await db.InventoryTransactions.ToListAsync(), tx =>
            tx.VoucherId is 110 or 210
            && tx.TransactionType is InventoryTransactionTypeEnum.Receive or InventoryTransactionTypeEnum.Ship);
    }

    [Fact]
    public void QuickAdjustFromSnapshot_ShouldRecheckPeriodLockInsideSerializableTransaction()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Controllers",
            "ReportsController.Inventory.cs"));

        const string methodSignature = "public async Task<IActionResult> QuickAdjustFromSnapshot";
        var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Không tìm thấy runtime path QuickAdjustFromSnapshot.");

        var methodEnd = source.IndexOf(
            "public async Task<IActionResult> ExportStockSnapshot",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart, "Không xác định được phạm vi QuickAdjustFromSnapshot.");

        var methodSource = source[methodStart..methodEnd];
        var transactionStart = methodSource.IndexOf(
            "BeginTransactionAsync(IsolationLevel.Serializable)",
            StringComparison.Ordinal);
        var lockRecheck = methodSource.IndexOf(
            "WarehousePeriodLocks.AsNoTracking()",
            transactionStart + 1,
            StringComparison.Ordinal);
        var ledgerScope = methodSource.IndexOf(
            "_inventoryTransactionService.BeginScope",
            StringComparison.Ordinal);
        var currentStockRead = methodSource.IndexOf(
            "var currentStocks = await _db.ItemLocations.AsNoTracking()",
            StringComparison.Ordinal);

        Assert.True(transactionStart >= 0, "Điều chỉnh snapshot phải chạy trong transaction Serializable.");
        Assert.True(lockRecheck > transactionStart,
            "Phải kiểm tra lại khóa kỳ sau khi transaction Serializable bắt đầu.");
        Assert.True(currentStockRead > lockRecheck,
            "Phải đọc tồn hiện tại và tính chênh lệch trong cùng transaction sau khi kiểm tra khóa kỳ.");
        Assert.True(ledgerScope > currentStockRead,
            "Phải hoàn tất snapshot tồn nhất quán trước khi mở ledger scope hoặc ghi tồn kho.");
    }

    [Fact]
    public async Task OutboundReleaseAndWave_ShouldBlockActivePeriodWithoutCreatingWork()
    {
        await using var db = CreateDb(nameof(OutboundReleaseAndWave_ShouldBlockActivePeriodWithoutCreatingWork));
        SeedBaseTopology(db);
        AddItem(db, 220, "OUT-RELEASE-LOCK");
        AddStock(db, 220, 1, 20, "LOT-RELEASE-LOCK", VietnamTime.Now.Date.AddDays(90));
        AddOutboundVoucher(db, 220, "PX-RELEASE-LOCK", 220, 4, lot: "LOT-RELEASE-LOCK", expiry: VietnamTime.Now.Date.AddDays(90));
        AddOutboundVoucher(db, 221, "PX-WAVE-LOCK", 220, 3, lot: "LOT-RELEASE-LOCK", expiry: VietnamTime.Now.Date.AddDays(90));
        db.WarehousePeriodLocks.Add(new WarehousePeriodLock
        {
            WarehouseId = 1,
            LockDate = VietnamTime.Now.Date,
            IsActive = true,
            LockedBy = "period.manager"
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);

        await Assert.ThrowsAsync<WarehouseLockedException>(() =>
            services.Outbound.ReleaseVoucherForPickingAsync(220, scopedWarehouseId: 1, "planner"));
        await Assert.ThrowsAsync<WarehouseLockedException>(() =>
            services.Outbound.CreateWaveAsync(
                "Standard",
                carrierCode: null,
                carrierName: null,
                routeCode: null,
                cutoffTime: null,
                WavePriorityEnum.Normal,
                new[] { 221L },
                notes: null,
                scopedWarehouseId: 1,
                actor: "planner"));

        Assert.Empty(await db.Waves.ToListAsync());
        Assert.Empty(await db.PickTasks.ToListAsync());
        Assert.Empty(await db.StockReservations.ToListAsync());
        Assert.Equal(0m, await db.ItemLocations.Where(il => il.ItemId == 220).Select(il => il.ReservedQty).SingleAsync());
        Assert.All(await db.Vouchers.Where(v => v.VoucherId == 220 || v.VoucherId == 221).ToListAsync(),
            voucher => Assert.Equal(FulfillmentStatusEnum.Draft, voucher.FulfillmentStatus));
    }

    [Fact]
    public async Task Outbound_ShouldReserveFefoPickPostAndBlockWhenStockChangedAfterReservation()
    {
        await using var db = CreateDb(nameof(Outbound_ShouldReserveFefoPickPostAndBlockWhenStockChangedAfterReservation));
        SeedBaseTopology(db);
        AddItem(db, 200, "OUT-FEFO");
        var early = new DateTime(2027, 2, 1);
        var late = new DateTime(2027, 6, 1);
        AddStock(db, 200, 1, 5, "LOT-EARLY", early);
        AddStock(db, 200, 2, 10, "LOT-LATE", late);
        AddOutboundVoucher(db, 200, "PX-DEEP-001", 200, 6);
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(200, scopedWarehouseId: 1, "picker");
        Assert.True(release.Succeeded, release.Message);

        var reservations = await db.StockReservations
            .Where(r => r.VoucherId == 200)
            .OrderBy(r => r.ExpiryDate)
            .ToListAsync();
        Assert.Collection(reservations,
            r =>
            {
                Assert.Equal("LOT-EARLY", r.LotNumber);
                Assert.Equal(5, r.ReservedQty);
            },
            r =>
            {
                Assert.Equal("LOT-LATE", r.LotNumber);
                Assert.Equal(1, r.ReservedQty);
            });

        await ConfirmAllPickTasksAsync(db, services.Outbound, 200, "picker");
        var post = await services.Outbound.PostReservedOutboundAsync(200, cancelRemaining: false, scopedWarehouseId: 1, "shipper", null);
        Assert.True(post.Succeeded, post.Message);

        Assert.Equal(0, (await FindStockAsync(db, 200, 1, "LOT-EARLY", early)).Quantity);
        Assert.Equal(9, (await FindStockAsync(db, 200, 2, "LOT-LATE", late)).Quantity);
        Assert.All(await db.StockReservations.Where(r => r.VoucherId == 200).ToListAsync(), r => Assert.Equal(ReservationStatusEnum.Consumed, r.Status));

        AddOutboundVoucher(db, 201, "PX-DEEP-STOCK-CHANGED", 200, 4);
        await db.SaveChangesAsync();
        var release2 = await services.Outbound.ReleaseVoucherForPickingAsync(201, scopedWarehouseId: 1, "picker");
        Assert.True(release2.Succeeded, release2.Message);
        await ConfirmAllPickTasksAsync(db, services.Outbound, 201, "picker");
        var lateStock = await FindStockAsync(db, 200, 2, "LOT-LATE", late);
        lateStock.Quantity = 1;
        await db.SaveChangesAsync();

        var stockChangedPost = await services.Outbound.PostReservedOutboundAsync(201, cancelRemaining: false, scopedWarehouseId: 1, "shipper", null);
        Assert.False(stockChangedPost.Succeeded);
        Assert.Equal(1, (await FindStockAsync(db, 200, 2, "LOT-LATE", late)).Quantity);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 201)).IsPosted);
    }

    [Fact]
    public async Task Outbound_Release_ShouldIgnorePreselectedNearExpiryLocationAndUseEligibleFefoStock()
    {
        await using var db = CreateDb(nameof(Outbound_Release_ShouldIgnorePreselectedNearExpiryLocationAndUseEligibleFefoStock));
        SeedBaseTopology(db);
        AddItem(db, 203, "OUT-PRESELECTED-EXPIRY");
        var nearExpiry = VietnamTime.Now.Date.AddDays(5);
        var eligibleExpiry = VietnamTime.Now.Date.AddDays(60);
        AddStock(db, 203, 1, 10, "LOT-NEAR", nearExpiry);
        AddStock(db, 203, 2, 10, "LOT-ELIGIBLE", eligibleExpiry);
        AddOutboundVoucher(db, 203, "PX-PRESELECTED-EXPIRY", 203, 6);
        db.VoucherDetails.Local.Single(d => d.VoucherId == 203).LocationId = 1;
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var result = await services.Outbound.ReleaseVoucherForPickingAsync(203, scopedWarehouseId: 1, "picker");

        Assert.True(result.Succeeded, result.Message);
        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 203);
        Assert.Equal(2, reservation.LocationId);
        Assert.Equal("LOT-ELIGIBLE", reservation.LotNumber);
        Assert.Equal(eligibleExpiry, reservation.ExpiryDate);
        Assert.DoesNotContain(await db.StockReservations.Where(r => r.VoucherId == 203).ToListAsync(),
            r => r.LocationId == 1 || r.ExpiryDate < VietnamTime.Now.Date.AddDays(30));
    }

    [Fact]
    public async Task Outbound_CreateWave_ShouldIgnorePreselectedNearExpiryLocationAndUseEligibleFefoStock()
    {
        await using var db = CreateDb(nameof(Outbound_CreateWave_ShouldIgnorePreselectedNearExpiryLocationAndUseEligibleFefoStock));
        SeedBaseTopology(db);
        AddItem(db, 204, "OUT-WAVE-PRESELECTED-EXPIRY");
        var nearExpiry = VietnamTime.Now.Date.AddDays(5);
        var eligibleExpiry = VietnamTime.Now.Date.AddDays(60);
        AddStock(db, 204, 1, 10, "LOT-WAVE-NEAR", nearExpiry);
        AddStock(db, 204, 2, 10, "LOT-WAVE-ELIGIBLE", eligibleExpiry);
        AddOutboundVoucher(db, 204, "PX-WAVE-PRESELECTED-EXPIRY", 204, 6);
        db.VoucherDetails.Local.Single(d => d.VoucherId == 204).LocationId = 1;
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var result = await services.Outbound.CreateWaveAsync(
            "Standard",
            carrierCode: null,
            carrierName: null,
            routeCode: null,
            cutoffTime: null,
            WavePriorityEnum.Normal,
            new[] { 204L },
            "AUDIT_TEST_NEAR_EXPIRY",
            scopedWarehouseId: 1,
            "picker");

        Assert.True(result.Succeeded, result.Message);
        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 204);
        Assert.Equal(2, reservation.LocationId);
        Assert.Equal("LOT-WAVE-ELIGIBLE", reservation.LotNumber);
        Assert.Equal(eligibleExpiry, reservation.ExpiryDate);
        Assert.DoesNotContain(await db.StockReservations.Where(r => r.VoucherId == 204).ToListAsync(),
            r => r.LocationId == 1 || r.ExpiryDate < VietnamTime.Now.Date.AddDays(30));
    }

    [Fact]
    public async Task Outbound_Release_ShouldRejectCrossWarehousePreselectedLocationWhenNoLocalStockExists()
    {
        await using var db = CreateDb(nameof(Outbound_Release_ShouldRejectCrossWarehousePreselectedLocationWhenNoLocalStockExists));
        SeedBaseTopology(db);
        AddItem(db, 205, "OUT-CROSS-WAREHOUSE");
        AddStock(db, 205, 20, 10, "LOT-WH2", VietnamTime.Now.Date.AddDays(60));
        AddOutboundVoucher(db, 205, "PX-CROSS-WAREHOUSE", 205, 6);
        db.VoucherDetails.Local.Single(d => d.VoucherId == 205).LocationId = 20;
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var result = await services.Outbound.ReleaseVoucherForPickingAsync(205, scopedWarehouseId: 1, "picker");

        Assert.False(result.Succeeded);
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 205).ToListAsync());
        Assert.Empty(await db.PickTasks.Where(t => t.VoucherId == 205).ToListAsync());
        Assert.Null((await db.Vouchers.SingleAsync(v => v.VoucherId == 205)).WaveId);
    }

    [Fact]
    public async Task Outbound_CreateWave_NonPartial_ShouldRollbackWhenEligibleStockIsShort()
    {
        await using var db = CreateDb(nameof(Outbound_CreateWave_NonPartial_ShouldRollbackWhenEligibleStockIsShort));
        SeedBaseTopology(db);
        AddItem(db, 206, "OUT-WAVE-SHORT");
        AddStock(db, 206, 1, 3, "LOT-SHORT", VietnamTime.Now.Date.AddDays(60));
        AddOutboundVoucher(db, 206, "PX-WAVE-SHORT", 206, 6);
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var result = await services.Outbound.CreateWaveAsync(
            "Standard", null, null, null, null, WavePriorityEnum.Normal,
            new[] { 206L }, "AUDIT_TEST_NON_PARTIAL_SHORT", 1, "picker");

        Assert.False(result.Succeeded);
        Assert.Empty(await db.Waves.ToListAsync());
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 206).ToListAsync());
        Assert.Empty(await db.PickTasks.Where(t => t.VoucherId == 206).ToListAsync());
        var voucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 206);
        Assert.Null(voucher.WaveId);
        Assert.NotEqual(FulfillmentStatusEnum.WaitingForPick, voucher.FulfillmentStatus);
    }

    [Fact]
    public async Task Outbound_CreateWave_Partial_ShouldFailWhenNothingCanBeAllocatedAndAllowRealPartialAllocation()
    {
        await using var db = CreateDb(nameof(Outbound_CreateWave_Partial_ShouldFailWhenNothingCanBeAllocatedAndAllowRealPartialAllocation));
        SeedBaseTopology(db);
        AddItem(db, 207, "OUT-WAVE-ZERO");
        AddItem(db, 208, "OUT-WAVE-PARTIAL");
        AddStock(db, 208, 1, 3, "LOT-PARTIAL", VietnamTime.Now.Date.AddDays(60));
        AddOutboundVoucher(db, 207, "PX-WAVE-ZERO", 207, 6);
        AddOutboundVoucher(db, 208, "PX-WAVE-PARTIAL", 208, 6);
        db.Vouchers.Local.Single(v => v.VoucherId == 207).PartialShipmentAllowed = true;
        db.Vouchers.Local.Single(v => v.VoucherId == 208).PartialShipmentAllowed = true;
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var zeroResult = await services.Outbound.CreateWaveAsync(
            "Standard", null, null, null, null, WavePriorityEnum.Normal,
            new[] { 207L }, "AUDIT_TEST_ZERO_ALLOCATION", 1, "picker");

        Assert.False(zeroResult.Succeeded);
        Assert.Empty(await db.Waves.ToListAsync());
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 207).ToListAsync());

        var partialResult = await services.Outbound.CreateWaveAsync(
            "Standard", null, null, null, null, WavePriorityEnum.Normal,
            new[] { 208L }, "AUDIT_TEST_PARTIAL_ALLOCATION", 1, "picker");

        Assert.True(partialResult.Succeeded, partialResult.Message);
        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 208);
        Assert.Equal(3, reservation.ReservedQty);
        Assert.Equal(1, reservation.LocationId);
        Assert.NotEmpty(await db.PickTasks.Where(t => t.VoucherId == 208).ToListAsync());
    }

    [Fact]
    public async Task Outbound_CreateWave_ShouldNotOverReserveSharedStockAcrossVouchers()
    {
        await using var db = CreateDb(nameof(Outbound_CreateWave_ShouldNotOverReserveSharedStockAcrossVouchers));
        SeedBaseTopology(db);
        AddItem(db, 209, "OUT-WAVE-SHARED-STOCK");
        AddStock(db, 209, 1, 10, "LOT-SHARED", VietnamTime.Now.Date.AddDays(60));
        AddOutboundVoucher(db, 209, "PX-WAVE-SHARED-1", 209, 6);
        AddOutboundVoucher(db, 210, "PX-WAVE-SHARED-2", 209, 6);
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var result = await services.Outbound.CreateWaveAsync(
            "Standard", null, null, null, null, WavePriorityEnum.Normal,
            new[] { 209L, 210L }, "AUDIT_TEST_SHARED_STOCK", 1, "picker");

        Assert.False(result.Succeeded);
        Assert.Empty(await db.Waves.ToListAsync());
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 209 || r.VoucherId == 210).ToListAsync());
        Assert.All(await db.Vouchers.Where(v => v.VoucherId == 209 || v.VoucherId == 210).ToListAsync(),
            voucher => Assert.Null(voucher.WaveId));
    }

    [Fact]
    public async Task Outbound_CreateWave_Partial_ShouldCapSharedReservationsAtPhysicalStock()
    {
        await using var db = CreateDb(nameof(Outbound_CreateWave_Partial_ShouldCapSharedReservationsAtPhysicalStock));
        SeedBaseTopology(db);
        AddItem(db, 211, "OUT-WAVE-PARTIAL-SHARED");
        AddStock(db, 211, 1, 10, "LOT-PARTIAL-SHARED", VietnamTime.Now.Date.AddDays(60));
        AddOutboundVoucher(db, 211, "PX-WAVE-PARTIAL-1", 211, 6);
        AddOutboundVoucher(db, 212, "PX-WAVE-PARTIAL-2", 211, 6);
        db.Vouchers.Local.Single(v => v.VoucherId == 211).PartialShipmentAllowed = true;
        db.Vouchers.Local.Single(v => v.VoucherId == 212).PartialShipmentAllowed = true;
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var result = await services.Outbound.CreateWaveAsync(
            "Standard", null, null, null, null, WavePriorityEnum.Normal,
            new[] { 211L, 212L }, "AUDIT_TEST_PARTIAL_SHARED_STOCK", 1, "picker");

        Assert.True(result.Succeeded, result.Message);
        var reservations = await db.StockReservations
            .Where(r => r.VoucherId == 211 || r.VoucherId == 212)
            .OrderBy(r => r.VoucherId)
            .ToListAsync();
        Assert.Equal(10, reservations.Sum(r => r.ReservedQty));
        Assert.Equal(6, reservations[0].ReservedQty);
        Assert.Equal(4, reservations[1].ReservedQty);
        var stock = await FindStockAsync(db, 211, 1, "LOT-PARTIAL-SHARED", VietnamTime.Now.Date.AddDays(60));
        Assert.Equal(10, stock.Quantity);
        Assert.Equal(10, stock.ReservedQty);
        Assert.Equal(0, stock.Quantity - stock.ReservedQty);
    }

    [Fact]
    public async Task Outbound_Release_ShouldNotOverReserveSharedStockAcrossVoucherLines()
    {
        await using var db = CreateDb(nameof(Outbound_Release_ShouldNotOverReserveSharedStockAcrossVoucherLines));
        SeedBaseTopology(db);
        AddItem(db, 213, "OUT-LINES-SHARED-STOCK");
        AddStock(db, 213, 1, 10, "LOT-LINES-SHARED", VietnamTime.Now.Date.AddDays(60));
        AddOutboundVoucher(db, 213, "PX-LINES-SHARED", 213, 6);
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2132,
            VoucherId = 213,
            ItemId = 213,
            TransactionQty = 6,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 6,
            UnitPrice = 5,
            LineAmount = 30,
            LineNumber = 2
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var result = await services.Outbound.ReleaseVoucherForPickingAsync(213, scopedWarehouseId: 1, "picker");

        Assert.False(result.Succeeded);
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 213).ToListAsync());
        Assert.Null((await db.Vouchers.SingleAsync(v => v.VoucherId == 213)).WaveId);
    }

    [Fact]
    public async Task Outbound_Post_ShouldRejectStockPlacedOnQualityHoldAfterPicking()
    {
        await using var db = CreateDb(nameof(Outbound_Post_ShouldRejectStockPlacedOnQualityHoldAfterPicking));
        SeedBaseTopology(db);
        AddItem(db, 214, "OUT-HOLD-AFTER-PICK");
        var expiry = VietnamTime.Now.Date.AddDays(90);
        AddStock(db, 214, 1, 6, "LOT-HOLD-AFTER-PICK", expiry);
        AddOutboundVoucher(db, 214, "PX-HOLD-AFTER-PICK", 214, 3, VoucherTypeEnum.XuatKho, "LOT-HOLD-AFTER-PICK", expiry);
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(214, scopedWarehouseId: 1, "picker");
        Assert.True(release.Succeeded, release.Message);
        await ConfirmAllPickTasksAsync(db, services.Outbound, 214, "picker");

        var stock = await db.ItemLocations.SingleAsync(il =>
            il.ItemId == 214 && il.LocationId == 1 && il.LotNumber == "LOT-HOLD-AFTER-PICK");
        stock.HoldStatus = InventoryHoldStatusEnum.QcHold;
        await db.SaveChangesAsync();

        var post = await services.Outbound.PostReservedOutboundAsync(
            214,
            cancelRemaining: false,
            scopedWarehouseId: 1,
            "shipper",
            null);

        Assert.False(post.Succeeded);
        Assert.Equal(6, (await FindStockAsync(db, 214, 1, "LOT-HOLD-AFTER-PICK", expiry)).Quantity);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 214)).IsPosted);
        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 214);
        Assert.Equal(ReservationStatusEnum.Active, reservation.Status);
        Assert.Equal(0, reservation.ConsumedQty);
    }

    [Fact]
    public async Task Outbound_Post_ShouldCloseCachedReservationBeforeConstraintGuardedSave()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{nameof(Outbound_Post_ShouldCloseCachedReservationBeforeConstraintGuardedSave)}-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new ItemLocationQuantityConstraintInterceptor())
            .Options;
        await using var db = new AppDbContext(options);

        SeedBaseTopology(db);
        AddItem(db, 202, "OUT-CONSTRAINT");
        AddStock(db, 202, 1, 130);
        AddOutboundVoucher(db, 202, "PX-CONSTRAINT-001", 202, 100);

        var stock = db.ItemLocations.Local.Single(x => x.ItemId == 202 && x.LocationId == 1);
        stock.ReservedQty = 100;
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 20201,
            VoucherId = 202,
            VoucherDetailId = 2021,
            ItemId = 202,
            LocationId = 1,
            ReservedQty = 100,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "AUDIT_TEST_PICKER"
        });
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 20201,
            TaskCode = "AUDIT_TEST_PICK_202",
            VoucherId = 202,
            VoucherDetailId = 2021,
            ItemId = 202,
            SourceLocationId = 1,
            TargetQty = 100,
            PickedQty = 100,
            Status = PickTaskStatusEnum.Completed,
            PickTaskMode = PickTaskModeEnum.Single,
            AssignedTo = "AUDIT_TEST_PICKER",
            CompletedAt = VietnamTime.Now
        });
        db.PickTaskAllocations.Add(new PickTaskAllocation
        {
            PickTaskAllocationId = 20201,
            PickTaskId = 20201,
            StockReservationId = 20201,
            VoucherId = 202,
            VoucherDetailId = 2021,
            AllocatedQty = 100,
            PickedQty = 100
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var post = await services.Outbound.PostReservedOutboundAsync(
            202,
            cancelRemaining: false,
            scopedWarehouseId: 1,
            "AUDIT_TEST_POSTER",
            null);

        Assert.True(post.Succeeded, post.Message);
        Assert.Equal(30, stock.Quantity);
        Assert.Equal(0, stock.ReservedQty);
        Assert.True((await db.Vouchers.SingleAsync(v => v.VoucherId == 202)).IsPosted);
        var reservation = await db.StockReservations.SingleAsync(r => r.StockReservationId == 20201);
        Assert.Equal(100, reservation.ConsumedQty);
        Assert.Equal(ReservationStatusEnum.Consumed, reservation.Status);
    }

    [Fact]
    public async Task Outbound_NonPartial_ShouldRejectShortReleaseAndShortPost()
    {
        await using var db = CreateDb(nameof(Outbound_NonPartial_ShouldRejectShortReleaseAndShortPost));
        SeedBaseTopology(db);
        AddItem(db, 210, "OUT-NONPARTIAL");
        AddStock(db, 210, 1, 6, "LOT-SHORT", new DateTime(2027, 3, 1));
        AddOutboundVoucher(db, 210, "PX-NONPARTIAL-SHORT", 210, 10, VoucherTypeEnum.XuatKho, "LOT-SHORT", new DateTime(2027, 3, 1));
        AddOutboundVoucher(db, 211, "PX-NONPARTIAL-POST", 210, 10, VoucherTypeEnum.XuatKho, "LOT-SHORT", new DateTime(2027, 3, 1));
        db.StockReservations.Add(new StockReservation
        {
            VoucherId = 211,
            VoucherDetailId = 2111,
            ItemId = 210,
            LocationId = 1,
            LotNumber = "LOT-SHORT",
            ExpiryDate = new DateTime(2027, 3, 1),
            ReservedQty = 6,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "picker",
            CreatedAt = VietnamTime.Now
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);

        var release = await services.Outbound.ReleaseVoucherForPickingAsync(210, scopedWarehouseId: 1, "picker");
        Assert.False(release.Succeeded);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 210)).IsPosted);

        var post = await services.Outbound.PostReservedOutboundAsync(211, cancelRemaining: false, scopedWarehouseId: 1, "shipper", null);
        Assert.False(post.Succeeded);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 211)).IsPosted);
    }

    [Fact]
    public async Task Outbound_PartialPost_ShouldCreateOneCanonicalBackorderForSubUomRemainder()
    {
        await using var db = CreateDb(nameof(Outbound_PartialPost_ShouldCreateOneCanonicalBackorderForSubUomRemainder));
        SeedBaseTopology(db);
        AddItem(db, 215, "OUT-PARTIAL-UOM");
        AddStock(db, 215, 1, 0.0100m);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 215,
            VoucherCode = "PX-PARTIAL-UOM",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            CreatedBy = "creator",
            PartialShipmentAllowed = true
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2151,
            VoucherId = 215,
            ItemId = 215,
            TransactionQty = 0.0001m,
            TransactionUomId = 2,
            ConversionRate = 100m,
            BaseQty = 0.0100m,
            UnitPrice = 5m,
            LineAmount = 0.0005m,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(215, scopedWarehouseId: 1, "picker");
        Assert.True(release.Succeeded, release.Message);

        var task = await db.PickTasks.Include(t => t.Allocations).SingleAsync(t => t.VoucherId == 215);
        task.PickedQty = 0.0098m;
        task.Status = PickTaskStatusEnum.Short;
        task.CompletedAt = VietnamTime.Now;
        var allocation = Assert.Single(task.Allocations);
        allocation.PickedQty = 0.0098m;
        await db.SaveChangesAsync();

        var post = await services.Outbound.PostReservedOutboundAsync(
            215,
            cancelRemaining: false,
            scopedWarehouseId: 1,
            "shipper",
            null);

        Assert.True(post.Succeeded, post.Message);
        Assert.True((await db.Vouchers.SingleAsync(v => v.VoucherId == 215)).IsPosted);
        Assert.Equal(0.0002m, (await FindStockAsync(db, 215, 1, lot: null, expiry: null)).Quantity);

        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 215);
        Assert.Equal(0.0098m, reservation.ConsumedQty);
        Assert.Equal(0.0002m, reservation.ReleasedQty);

        var backorder = await db.Vouchers
            .Include(v => v.Details).ThenInclude(d => d.Item)
            .SingleAsync(v => v.ParentVoucherId == 215);
        var backorderLine = Assert.Single(backorder.Details);
        Assert.Equal(0.0002m, backorderLine.TransactionQty);
        Assert.Equal(1, backorderLine.TransactionUomId);
        Assert.Equal(1m, backorderLine.ConversionRate);
        Assert.Equal(0.0002m, backorderLine.BaseQty);

        var retry = await services.Outbound.PostReservedOutboundAsync(
            215,
            cancelRemaining: false,
            scopedWarehouseId: 1,
            "shipper",
            null);
        Assert.False(retry.Succeeded);
        Assert.Equal(1, await db.Vouchers.CountAsync(v => v.ParentVoucherId == 215));
    }

    [Fact]
    public async Task Outbound_PartialPostWithCancelRemaining_ShouldPostPickedQtyAndReleaseRemainderExactlyOnce()
    {
        await using var db = CreateDb(nameof(Outbound_PartialPostWithCancelRemaining_ShouldPostPickedQtyAndReleaseRemainderExactlyOnce));
        SeedBaseTopology(db);
        AddItem(db, 216, "OUT-PARTIAL-CANCEL");
        AddStock(db, 216, 1, 10);
        AddOutboundVoucher(db, 216, "PX-PARTIAL-CANCEL", 216, 10);
        db.Vouchers.Local.Single(v => v.VoucherId == 216).PartialShipmentAllowed = true;
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(
            216,
            scopedWarehouseId: 1,
            "picker");
        Assert.True(release.Succeeded, release.Message);

        var task = await db.PickTasks
            .Include(t => t.Allocations)
            .SingleAsync(t => t.VoucherId == 216);
        task.PickedQty = 4;
        task.Status = PickTaskStatusEnum.Short;
        task.CompletedAt = VietnamTime.Now;
        var allocation = Assert.Single(task.Allocations);
        allocation.PickedQty = 4;
        await db.SaveChangesAsync();

        var post = await services.Outbound.PostReservedOutboundAsync(
            216,
            cancelRemaining: true,
            scopedWarehouseId: 1,
            "shipper",
            null);

        Assert.True(post.Succeeded, post.Message);

        var voucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 216);
        Assert.True(voucher.IsPosted);
        Assert.Equal(FulfillmentStatusEnum.Completed, voucher.FulfillmentStatus);

        var stock = await FindStockAsync(db, 216, 1, lot: null, expiry: null);
        Assert.Equal(6, stock.Quantity);
        Assert.Equal(0, stock.ReservedQty);
        Assert.Equal(6, await db.Items.Where(i => i.ItemId == 216).Select(i => i.CurrentStock).SingleAsync());

        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 216);
        Assert.Equal(10, reservation.ReservedQty);
        Assert.Equal(4, reservation.ConsumedQty);
        Assert.Equal(6, reservation.ReleasedQty);
        Assert.Equal(ReservationStatusEnum.Consumed, reservation.Status);
        Assert.Equal(reservation.ReservedQty, reservation.ConsumedQty + reservation.ReleasedQty);

        Assert.False(await db.Vouchers.AnyAsync(v => v.ParentVoucherId == 216));
        var shipLedger = await db.InventoryTransactions
            .Where(t => t.VoucherId == 216 && t.TransactionType == InventoryTransactionTypeEnum.Ship)
            .ToListAsync();
        var ledgerRow = Assert.Single(shipLedger);
        Assert.Equal(-4, ledgerRow.QuantityDelta);

        var retry = await services.Outbound.PostReservedOutboundAsync(
            216,
            cancelRemaining: true,
            scopedWarehouseId: 1,
            "shipper",
            null);
        Assert.False(retry.Succeeded);
        Assert.Equal(6, (await FindStockAsync(db, 216, 1, lot: null, expiry: null)).Quantity);
        Assert.Single(await db.InventoryTransactions
            .Where(t => t.VoucherId == 216 && t.TransactionType == InventoryTransactionTypeEnum.Ship)
            .ToListAsync());
    }

    [Fact]
    public async Task Inbound_ShouldRejectNegativeConversionRateInServicePath()
    {
        await using var db = CreateDb(nameof(Inbound_ShouldRejectNegativeConversionRateInServicePath));
        SeedBaseTopology(db);
        AddItem(db, 220, "IN-BAD-CONVERSION");
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 220,
            VoucherCode = "PN-BAD-CONVERSION",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "creator",
            InboundStatus = InboundStatusEnum.Receiving
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2201,
            VoucherId = 220,
            ItemId = 220,
            LocationId = 1,
            TransactionQty = 2,
            TransactionUomId = 1,
            ConversionRate = -1,
            BaseQty = 2,
            UnitPrice = 5,
            LineAmount = 10,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var result = await services.Inbound.CompleteInboundAsync(220, scopedWarehouseId: 1, "receiver", null);

        Assert.False(result.Succeeded);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 220)).IsPosted);
        Assert.Empty(await db.ItemLocations.Where(il => il.ItemId == 220).ToListAsync());
    }

    [Fact]
    public async Task Inbound_ShouldRejectEmptyZeroAndUomMismatchWithoutStockMutation()
    {
        await using var db = CreateDb(nameof(Inbound_ShouldRejectEmptyZeroAndUomMismatchWithoutStockMutation));
        SeedBaseTopology(db);
        AddItem(db, 221, "IN-QTY-CONTRACT");
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 221,
                VoucherCode = "PN-EMPTY",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                CreatedBy = "creator",
                InboundStatus = InboundStatusEnum.Receiving
            },
            new Voucher
            {
                VoucherId = 222,
                VoucherCode = "PN-ZERO",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                CreatedBy = "creator",
                InboundStatus = InboundStatusEnum.Receiving
            },
            new Voucher
            {
                VoucherId = 223,
                VoucherCode = "PN-UOM-MISMATCH",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                CreatedBy = "creator",
                InboundStatus = InboundStatusEnum.Receiving
            });
        db.VoucherDetails.AddRange(
            new VoucherDetail
            {
                VoucherDetailId = 2221,
                VoucherId = 222,
                ItemId = 221,
                LocationId = 1,
                TransactionQty = 0,
                TransactionUomId = 1,
                ConversionRate = 1,
                BaseQty = 0,
                LineNumber = 1
            },
            new VoucherDetail
            {
                VoucherDetailId = 2231,
                VoucherId = 223,
                ItemId = 221,
                LocationId = 1,
                TransactionQty = 2,
                TransactionUomId = 1,
                ConversionRate = 3,
                BaseQty = 5.99m,
                LineNumber = 1
            });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var empty = await services.Inbound.CompleteInboundAsync(221, 1, "receiver", null);
        var zero = await services.Inbound.CompleteInboundAsync(222, 1, "receiver", null);
        var mismatch = await services.Inbound.CompleteInboundAsync(223, 1, "receiver", null);

        Assert.False(empty.Succeeded);
        Assert.Contains("chưa có dòng hàng", empty.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(zero.Succeeded);
        Assert.Contains("phải lớn hơn 0", zero.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(mismatch.Succeeded);
        Assert.Contains("không khớp dữ liệu phiếu", mismatch.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(await db.Vouchers.Where(v => v.VoucherId >= 221 && v.VoucherId <= 223).ToListAsync(),
            voucher => Assert.False(voucher.IsPosted));
        Assert.Empty(await db.ItemLocations.Where(il => il.ItemId == 221).ToListAsync());
        Assert.Empty(await db.InventoryTransactions.Where(tx => tx.ItemId == 221).ToListAsync());
    }

    [Fact]
    public async Task CancelInbound_AfterCrossDock_ShouldOnlyReversePutawayQuantity()
    {
        await using var db = CreateDb(nameof(CancelInbound_AfterCrossDock_ShouldOnlyReversePutawayQuantity));
        SeedBaseTopology(db);
        AddItem(db, 230, "IN-CD-CANCEL");
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 230,
            VoucherCode = "PN-CD-CANCEL",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "creator",
            IsPosted = true,
            InboundStatus = InboundStatusEnum.Completed,
            CompletedAt = VietnamTime.Now
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2301,
            VoucherId = 230,
            ItemId = 230,
            LocationId = 1,
            TransactionQty = 10,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 10,
            UnitPrice = 5,
            LineAmount = 50,
            LineNumber = 1
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 23001,
            ItemId = 230,
            LocationId = 1,
            Quantity = 6,
            ReservedQty = 0,
            UpdatedAt = VietnamTime.Now
        });
        db.CrossDockTasks.Add(new CrossDockTask
        {
            CrossDockTaskId = 23001,
            TaskCode = "CD-CANCEL-001",
            InboundVoucherId = 230,
            InboundVoucherDetailId = 2301,
            OutboundVoucherId = 999,
            ItemId = 230,
            StageLocationId = 2,
            ScheduledQty = 4,
            ActualQty = 4,
            Status = CrossDockTaskStatusEnum.Completed,
            AssignedTo = "dock"
        });
        var originalTransaction = new InventoryTransaction
        {
            TransactionType = InventoryTransactionTypeEnum.Receive,
            TransactionGroupKey = "AUDIT_TEST_GATE1_INBOUND_ORIGINAL",
            IdempotencyKey = "AUDIT_TEST_GATE1_INBOUND_ORIGINAL",
            WarehouseId = 1,
            ItemId = 230,
            LocationId = 1,
            QuantityDelta = 6,
            AvailableDelta = 6,
            QuantityBefore = 0,
            QuantityAfter = 6,
            ReservedBefore = 0,
            ReservedAfter = 0,
            AvailableBefore = 0,
            AvailableAfter = 6,
            VoucherId = 230,
            VoucherDetailId = 2301,
            ReferenceType = "Voucher",
            ReferenceId = "230",
            ReferenceCode = "PN-CD-CANCEL",
            Actor = "receiver"
        };
        db.InventoryTransactions.Add(originalTransaction);
        db.Items.Local.First(i => i.ItemId == 230).CurrentStock = 6;
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        const string cancelReason = "Hủy kiểm thử cross-dock";
        var cancel = await services.Cancellation.CancelVoucherAsync(230, cancelReason, CancelReasonEnum.OperationalError, scopedWarehouseId: 1, "manager", null, null);

        Assert.True(cancel.Succeeded, cancel.Message);
        Assert.Equal(0, (await db.ItemLocations.SingleAsync(il => il.ItemId == 230 && il.LocationId == 1)).Quantity);
        Assert.Equal(0, (await db.Items.SingleAsync(i => i.ItemId == 230)).CurrentStock);
        var reversal = await db.InventoryTransactions.SingleAsync(t =>
            t.VoucherId == 230 && t.TransactionType == InventoryTransactionTypeEnum.Cancel);
        using var reversalMetadata = JsonDocument.Parse(reversal.MetadataJson);
        var originalIds = reversalMetadata.RootElement
            .GetProperty("originalInventoryTransactionIds")
            .EnumerateArray()
            .Select(value => value.GetInt64())
            .ToList();
        Assert.Contains(originalTransaction.InventoryTransactionId, originalIds);
        Assert.Equal(cancelReason, reversalMetadata.RootElement.GetProperty("cancelReason").GetString());
        Assert.Equal(nameof(CancelReasonEnum.OperationalError), reversalMetadata.RootElement.GetProperty("cancelReasonCode").GetString());
    }

    [Fact]
    public async Task Transfer_ShouldRespectWarehouseScopeAndMoveLotExpiryToDestination()
    {
        await using var db = CreateDb(nameof(Transfer_ShouldRespectWarehouseScopeAndMoveLotExpiryToDestination));
        SeedBaseTopology(db);
        AddItem(db, 300, "TRF-LOT");
        var expiry = new DateTime(2027, 7, 1);
        AddStock(db, 300, 1, 8, "LOT-TRF", expiry);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 300,
            VoucherCode = "CT-DEEP-001",
            VoucherType = VoucherTypeEnum.ChuyenKho,
            WarehouseId = 1,
            DestWarehouseId = 2,
            CreatedBy = "creator"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 3001,
            VoucherId = 300,
            ItemId = 300,
            LocationId = 1,
            DestLocationId = 20,
            TransactionQty = 3,
            TransactionUomId = 1,
            BaseQty = 3,
            LotNumber = "LOT-TRF",
            ExpiryDate = expiry,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var forbidden = await services.Outbound.ReleaseVoucherForPickingAsync(300, scopedWarehouseId: 2, "picker");
        Assert.True(forbidden.Forbidden);

        var release = await services.Outbound.ReleaseVoucherForPickingAsync(300, scopedWarehouseId: 1, "picker");
        Assert.True(release.Succeeded, release.Message);
        await ConfirmAllPickTasksAsync(db, services.Outbound, 300, "picker");
        var post = await services.Outbound.PostReservedOutboundAsync(300, cancelRemaining: false, scopedWarehouseId: 1, "shipper", null);
        Assert.True(post.Succeeded, post.Message);

        Assert.Equal(5, (await FindStockAsync(db, 300, 1, "LOT-TRF", expiry)).Quantity);
        Assert.Equal(3, (await FindStockAsync(db, 300, 20, "LOT-TRF", expiry)).Quantity);
    }

    [Fact]
    public async Task Transfer_ShouldRejectDestinationLocationOutsideDeclaredWarehouseBeforeStockMutation()
    {
        await using var db = CreateDb(nameof(Transfer_ShouldRejectDestinationLocationOutsideDeclaredWarehouseBeforeStockMutation));
        SeedBaseTopology(db);
        AddItem(db, 301, "TRF-WRONG-DEST");
        var expiry = VietnamTime.Now.Date.AddDays(90);
        AddStock(db, 301, 1, 8, "LOT-WRONG-DEST", expiry);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 301,
            VoucherCode = "CT-DEEP-WRONG-DEST",
            VoucherType = VoucherTypeEnum.ChuyenKho,
            WarehouseId = 1,
            DestWarehouseId = 2,
            CreatedBy = "creator"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 3011,
            VoucherId = 301,
            ItemId = 301,
            LocationId = 1,
            DestLocationId = 2,
            TransactionQty = 3,
            TransactionUomId = 1,
            BaseQty = 3,
            LotNumber = "LOT-WRONG-DEST",
            ExpiryDate = expiry,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(301, scopedWarehouseId: 1, "picker");
        Assert.False(release.Succeeded);
        Assert.Equal(8, (await FindStockAsync(db, 301, 1, "LOT-WRONG-DEST", expiry)).Quantity);
        Assert.Null(await db.ItemLocations.SingleOrDefaultAsync(il =>
            il.ItemId == 301 && il.LocationId == 2 && il.LotNumber == "LOT-WRONG-DEST"));
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 301).ToListAsync());
        Assert.Empty(await db.PickTasks.Where(t => t.VoucherId == 301).ToListAsync());
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 301)).IsPosted);
    }

    [Fact]
    public async Task Transfer_PostShouldRejectDestinationChangedOutsideDeclaredWarehouseAndRollback()
    {
        await using var db = CreateDb(nameof(Transfer_PostShouldRejectDestinationChangedOutsideDeclaredWarehouseAndRollback));
        SeedBaseTopology(db);
        AddItem(db, 302, "TRF-CHANGED-DEST");
        var expiry = VietnamTime.Now.Date.AddDays(90);
        AddStock(db, 302, 1, 8, "LOT-CHANGED-DEST", expiry);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 302,
            VoucherCode = "CT-DEEP-CHANGED-DEST",
            VoucherType = VoucherTypeEnum.ChuyenKho,
            WarehouseId = 1,
            DestWarehouseId = 2,
            CreatedBy = "creator"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 3021,
            VoucherId = 302,
            ItemId = 302,
            LocationId = 1,
            DestLocationId = 20,
            TransactionQty = 3,
            TransactionUomId = 1,
            BaseQty = 3,
            LotNumber = "LOT-CHANGED-DEST",
            ExpiryDate = expiry,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(302, scopedWarehouseId: 1, "picker");
        Assert.True(release.Succeeded, release.Message);
        await ConfirmAllPickTasksAsync(db, services.Outbound, 302, "picker");

        var detail = await db.VoucherDetails.SingleAsync(d => d.VoucherDetailId == 3021);
        detail.DestLocationId = 2;
        await db.SaveChangesAsync();

        var post = await services.Outbound.PostReservedOutboundAsync(
            302,
            cancelRemaining: false,
            scopedWarehouseId: 1,
            "shipper",
            null);

        Assert.False(post.Succeeded);
        Assert.Equal(8, (await FindStockAsync(db, 302, 1, "LOT-CHANGED-DEST", expiry)).Quantity);
        Assert.Null(await db.ItemLocations.SingleOrDefaultAsync(il =>
            il.ItemId == 302 && il.LocationId == 2 && il.LotNumber == "LOT-CHANGED-DEST"));
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 302)).IsPosted);
        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 302);
        Assert.Equal(ReservationStatusEnum.Active, reservation.Status);
        Assert.Equal(0, reservation.ConsumedQty);
    }

    [Fact]
    public async Task Transfer_PostShouldRejectConflictingStorageDestinationBeforeSourceMutation()
    {
        await using var db = CreateDb(nameof(Transfer_PostShouldRejectConflictingStorageDestinationBeforeSourceMutation));
        SeedBaseTopology(db);
        AddItem(db, 303, "TRF-DEST-OCCUPIED");
        AddItem(db, 304, "TRF-NEW-KEY");
        AddStock(db, 303, 20, 4, "LOT-OCCUPIED", VietnamTime.Now.Date.AddDays(120));
        var expiry = VietnamTime.Now.Date.AddDays(90);
        AddStock(db, 304, 1, 8, "LOT-NEW-KEY", expiry);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 304,
            VoucherCode = "CT-DEEP-DEST-CONFLICT",
            VoucherType = VoucherTypeEnum.ChuyenKho,
            WarehouseId = 1,
            DestWarehouseId = 2,
            CreatedBy = "creator"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 3041,
            VoucherId = 304,
            ItemId = 304,
            LocationId = 1,
            DestLocationId = 20,
            TransactionQty = 3,
            TransactionUomId = 1,
            BaseQty = 3,
            LotNumber = "LOT-NEW-KEY",
            ExpiryDate = expiry,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(304, scopedWarehouseId: 1, "picker");
        Assert.True(release.Succeeded, release.Message);
        await ConfirmAllPickTasksAsync(db, services.Outbound, 304, "picker");

        var post = await services.Outbound.PostReservedOutboundAsync(
            304,
            cancelRemaining: false,
            scopedWarehouseId: 1,
            "shipper",
            null);

        Assert.False(post.Succeeded);
        Assert.Contains("một vị trí một mã vật tư", post.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, (await FindStockAsync(db, 304, 1, "LOT-NEW-KEY", expiry)).Quantity);
        Assert.Equal(4, (await FindStockAsync(db, 303, 20, "LOT-OCCUPIED", VietnamTime.Now.Date.AddDays(120))).Quantity);
        Assert.Null(await db.ItemLocations.SingleOrDefaultAsync(row =>
            row.ItemId == 304 && row.LocationId == 20));
        Assert.False((await db.Vouchers.SingleAsync(row => row.VoucherId == 304)).IsPosted);
        Assert.Empty(await db.InventoryTransactions
            .Where(row => row.VoucherId == 304
                && row.TransactionGroupKey == "voucher:304:outbound-post")
            .ToListAsync());

        var reservation = await db.StockReservations.SingleAsync(row => row.VoucherId == 304);
        Assert.Equal(ReservationStatusEnum.Active, reservation.Status);
        Assert.Equal(0, reservation.ConsumedQty);
    }

    [Fact]
    public async Task ReturnFlows_ShouldRequireCustomerReturnQcAndDeductSupplierReturnStock()
    {
        await using var db = CreateDb(nameof(ReturnFlows_ShouldRequireCustomerReturnQcAndDeductSupplierReturnStock));
        SeedBaseTopology(db);
        AddItem(db, 400, "RET-SKU");
        AddStock(db, 400, 1, 10, "LOT-RET", new DateTime(2027, 8, 1));
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 400,
            VoucherCode = "PTKH-DEEP-001",
            VoucherType = VoucherTypeEnum.KhachTra,
            WarehouseId = 1,
            CreatedBy = "creator",
            InboundStatus = InboundStatusEnum.Receiving
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 4001,
            VoucherId = 400,
            ItemId = 400,
            LocationId = 1,
            TransactionQty = 2,
            TransactionUomId = 1,
            BaseQty = 2,
            UnitPrice = 5,
            QualityStatus = QualityStatusEnum.Pending,
            LotNumber = "LOT-RET",
            ExpiryDate = new DateTime(2027, 8, 1),
            LineNumber = 1
        });
        AddOutboundVoucher(db, 401, "TNCC-DEEP-001", 400, 4, VoucherTypeEnum.TraNCC, "LOT-RET", new DateTime(2027, 8, 1));
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var blockedCustomerReturn = await services.Inbound.CompleteInboundAsync(400, scopedWarehouseId: 1, "receiver", null);
        Assert.False(blockedCustomerReturn.Succeeded);
        Assert.Equal(10, (await FindStockAsync(db, 400, 1, "LOT-RET", new DateTime(2027, 8, 1))).Quantity);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 400)).IsPosted);

        var returnDetail = await db.VoucherDetails.SingleAsync(d => d.VoucherDetailId == 4001);
        returnDetail.QualityStatus = QualityStatusEnum.Passed;
        await db.SaveChangesAsync();

        var customerReturn = await services.Inbound.CompleteInboundAsync(400, scopedWarehouseId: 1, "receiver", null);
        Assert.True(customerReturn.Succeeded, customerReturn.Message);
        Assert.Equal(12, (await FindStockAsync(db, 400, 1, "LOT-RET", new DateTime(2027, 8, 1))).Quantity);
        var customerReturnVoucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 400);
        Assert.True(customerReturnVoucher.IsPosted);
        Assert.Equal(InboundStatusEnum.Completed, customerReturnVoucher.InboundStatus);
        var customerReturnLedger = Assert.Single(await db.InventoryTransactions
            .Where(t => t.VoucherId == 400 && t.TransactionType == InventoryTransactionTypeEnum.Receive)
            .ToListAsync());
        Assert.Equal(2, customerReturnLedger.QuantityDelta);

        var supplierRelease = await services.Outbound.ReleaseVoucherForPickingAsync(401, scopedWarehouseId: 1, "picker");
        Assert.True(supplierRelease.Succeeded, supplierRelease.Message);
        await ConfirmAllPickTasksAsync(db, services.Outbound, 401, "picker");
        var supplierReturn = await services.Outbound.PostReservedOutboundAsync(401, cancelRemaining: false, scopedWarehouseId: 1, "shipper", null);
        Assert.True(supplierReturn.Succeeded, supplierReturn.Message);
        Assert.Equal(8, (await FindStockAsync(db, 400, 1, "LOT-RET", new DateTime(2027, 8, 1))).Quantity);
        var supplierReturnVoucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 401);
        Assert.True(supplierReturnVoucher.IsPosted);
        Assert.Equal(FulfillmentStatusEnum.Completed, supplierReturnVoucher.FulfillmentStatus);
        var supplierReturnReservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 401);
        Assert.Equal(4, supplierReturnReservation.ConsumedQty);
        Assert.Equal(ReservationStatusEnum.Consumed, supplierReturnReservation.Status);
        var supplierReturnLedger = Assert.Single(await db.InventoryTransactions
            .Where(t => t.VoucherId == 401 && t.TransactionType == InventoryTransactionTypeEnum.Ship)
            .ToListAsync());
        Assert.Equal(-4, supplierReturnLedger.QuantityDelta);
    }

    [Fact]
    public async Task ImportantInventoryCommands_RepeatedFiveTimes_ShouldOnlyMutateOnce()
    {
        await using var db = CreateDb(nameof(ImportantInventoryCommands_RepeatedFiveTimes_ShouldOnlyMutateOnce));
        SeedBaseTopology(db);
        AddItem(db, 410, "AUDIT_TEST_GATE1_INBOUND");
        AddItem(db, 411, "AUDIT_TEST_GATE1_OUTBOUND");
        AddStock(db, 411, 1, 10);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 410,
            VoucherCode = "AUDIT_TEST_GATE1_IN",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "creator",
            InboundStatus = InboundStatusEnum.Receiving
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 4101,
            VoucherId = 410,
            ItemId = 410,
            LocationId = 2,
            TransactionQty = 5,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 5,
            UnitPrice = 1,
            LineAmount = 5,
            LineNumber = 1
        });
        AddOutboundVoucher(db, 411, "AUDIT_TEST_GATE1_OUT", 411, 3);
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var inboundResults = new List<WorkflowResult>();
        for (var attempt = 0; attempt < 5; attempt++)
            inboundResults.Add(await services.Inbound.CompleteInboundAsync(410, scopedWarehouseId: 1, "receiver", null));

        Assert.True(inboundResults[0].Succeeded, inboundResults[0].Message);
        Assert.All(inboundResults.Skip(1), result => Assert.False(result.Succeeded));
        Assert.Equal(5, (await FindStockAsync(db, 410, 2, lot: null, expiry: null)).Quantity);
        Assert.Single(await db.InventoryTransactions
            .Where(t => t.VoucherId == 410 && t.TransactionType == InventoryTransactionTypeEnum.Receive)
            .ToListAsync());

        var releaseResults = new List<WorkflowResult>();
        for (var attempt = 0; attempt < 5; attempt++)
            releaseResults.Add(await services.Outbound.ReleaseVoucherForPickingAsync(411, scopedWarehouseId: 1, "picker"));

        Assert.All(releaseResults, result => Assert.True(result.Succeeded, result.Message));
        Assert.Single(await db.StockReservations.Where(r => r.VoucherId == 411).ToListAsync());
        Assert.Equal(3, (await FindStockAsync(db, 411, 1, lot: null, expiry: null)).ReservedQty);

        await ConfirmAllPickTasksAsync(db, services.Outbound, 411, "picker");
        var outboundResults = new List<WorkflowResult>();
        for (var attempt = 0; attempt < 5; attempt++)
            outboundResults.Add(await services.Outbound.PostReservedOutboundAsync(411, cancelRemaining: false, scopedWarehouseId: 1, "shipper", null));

        Assert.True(outboundResults[0].Succeeded, outboundResults[0].Message);
        Assert.All(outboundResults.Skip(1), result => Assert.False(result.Succeeded));
        Assert.Equal(7, (await FindStockAsync(db, 411, 1, lot: null, expiry: null)).Quantity);
        Assert.Single(await db.InventoryTransactions
            .Where(t => t.VoucherId == 411 && t.TransactionType == InventoryTransactionTypeEnum.Ship)
            .ToListAsync());

        var cancellation = await services.Cancellation.CancelVoucherAsync(
            411,
            "AUDIT_TEST_GATE1_CANCEL",
            CancelReasonEnum.OperationalError,
            scopedWarehouseId: 1,
            "manager",
            null,
            null);
        Assert.True(cancellation.Succeeded, cancellation.Message);
        for (var attempt = 1; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => services.Cancellation.CancelVoucherAsync(
                411,
                "AUDIT_TEST_GATE1_CANCEL",
                CancelReasonEnum.OperationalError,
                scopedWarehouseId: 1,
                "manager",
                null,
                null));
        }

        var restoredStock = await FindStockAsync(db, 411, 1, lot: null, expiry: null);
        Assert.Equal(10, restoredStock.Quantity);
        Assert.Equal(0, restoredStock.ReservedQty);

        var cancelledVoucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 411);
        Assert.True(cancelledVoucher.IsCancelled);
        Assert.True(cancelledVoucher.IsPosted);
        Assert.Equal("manager", cancelledVoucher.CancelledBy);
        Assert.Equal("AUDIT_TEST_GATE1_CANCEL", cancelledVoucher.CancelReason);
        Assert.Equal(CancelReasonEnum.OperationalError, cancelledVoucher.CancelReasonCode);

        var releasedReservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 411);
        Assert.Equal(0, releasedReservation.ConsumedQty);
        Assert.Equal(3, releasedReservation.ReleasedQty);
        Assert.Equal(ReservationStatusEnum.Released, releasedReservation.Status);

        var cancelLedger = Assert.Single(await db.InventoryTransactions
            .Where(t => t.VoucherId == 411 && t.TransactionGroupKey == "voucher:411:cancel")
            .ToListAsync());
        Assert.Equal(InventoryTransactionTypeEnum.Cancel, cancelLedger.TransactionType);
        Assert.Equal(3, cancelLedger.QuantityDelta);
        using var metadata = JsonDocument.Parse(cancelLedger.MetadataJson);
        Assert.Equal("AUDIT_TEST_GATE1_CANCEL", metadata.RootElement.GetProperty("cancelReason").GetString());
        Assert.Equal(nameof(CancelReasonEnum.OperationalError), metadata.RootElement.GetProperty("cancelReasonCode").GetString());
        Assert.NotEmpty(metadata.RootElement.GetProperty("originalInventoryTransactionIds").EnumerateArray());
    }

    [Fact]
    public async Task SerialLotExpiryAndCancellation_ShouldPreserveScopeAndReleaseOpenReservations()
    {
        await using var db = CreateDb(nameof(SerialLotExpiryAndCancellation_ShouldPreserveScopeAndReleaseOpenReservations));
        SeedBaseTopology(db);
        AddItem(db, 500, "SER-LOT", trackSerial: true);
        AddStock(db, 500, 1, 2, "LOT-SER", new DateTime(2027, 9, 1));
        db.SerialNumbers.AddRange(
            new SerialNumber { SerialNumberId = 5001, SerialCode = "SN-001", WarehouseId = 1, ItemId = 500, LocationId = 1, VoucherId = 1, LotNumber = "LOT-SER", ExpiryDate = new DateTime(2027, 9, 1), Status = SerialNumberStatusEnum.Active },
            new SerialNumber { SerialNumberId = 5002, SerialCode = "SN-001", WarehouseId = 2, ItemId = 500, LocationId = 20, VoucherId = 1, LotNumber = "LOT-SER", ExpiryDate = new DateTime(2027, 9, 1), Status = SerialNumberStatusEnum.Active });
        AddOutboundVoucher(db, 500, "PX-SER-CANCEL", 500, 1, VoucherTypeEnum.XuatKho, "LOT-SER", new DateTime(2027, 9, 1));
        await db.SaveChangesAsync();

        var serialIndex = db.Model.FindEntityType(typeof(SerialNumber))!
            .GetIndexes()
            .Single(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(SerialNumber.WarehouseId),
                nameof(SerialNumber.ItemId),
                nameof(SerialNumber.SerialCode)
            }));
        Assert.NotNull(serialIndex);

        var services = CreateServices(db);
        var release = await services.Outbound.ReleaseVoucherForPickingAsync(500, scopedWarehouseId: 1, "picker");
        Assert.True(release.Succeeded, release.Message);
        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 500);
        Assert.Equal("LOT-SER", reservation.LotNumber);
        Assert.Equal(new DateTime(2027, 9, 1), reservation.ExpiryDate);
        Assert.Equal(1, (await FindStockAsync(db, 500, 1, "LOT-SER", new DateTime(2027, 9, 1))).ReservedQty);

        var cancel = await services.Cancellation.CancelVoucherAsync(500, "Hủy kiểm thử giữ chỗ", CancelReasonEnum.OperationalError, scopedWarehouseId: 1, "manager", null, null);
        Assert.True(cancel.Succeeded, cancel.Message);

        reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 500);
        Assert.Equal(ReservationStatusEnum.Released, reservation.Status);
        Assert.Equal(0, (await FindStockAsync(db, 500, 1, "LOT-SER", new DateTime(2027, 9, 1))).ReservedQty);
        Assert.All(await db.PickTasks.Where(t => t.VoucherId == 500).ToListAsync(), t => Assert.Equal(PickTaskStatusEnum.Cancelled, t.Status));
    }

    [Fact]
    public async Task CancelPackedOutbound_ShouldBeBlockedWithoutRestoringInventory()
    {
        await using var db = CreateDb(nameof(CancelPackedOutbound_ShouldBeBlockedWithoutRestoringInventory));
        SeedBaseTopology(db);
        AddItem(db, 501, "PACKED-CANCEL-SKU");
        AddStock(db, 501, 1, 7, lot: null, expiry: null);
        AddOutboundVoucher(db, 501, "PX-PACKED-CANCEL", 501, 3);
        await db.SaveChangesAsync();
        var voucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 501);
        voucher.IsPosted = true;
        voucher.PackedAt = VietnamTime.Now;
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 50101,
            PackageCode = "PK-PACKED-CANCEL",
            VoucherId = 501,
            WarehouseId = 1,
            TotalQuantity = 3,
            ItemCount = 1,
            PackedBy = "packer",
            PackedAt = VietnamTime.Now
        });
        await db.SaveChangesAsync();

        var services = CreateServices(db);
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            services.Cancellation.CancelVoucherAsync(
                501,
                "Không được phục hồi hàng đã bàn giao",
                CancelReasonEnum.OperationalError,
                scopedWarehouseId: 1,
                "manager",
                null,
                null));

        Assert.Equal("CANNOT_CANCEL_AFTER_PACKING_OR_SHIPPING", exception.Code);
        Assert.False((await db.Vouchers.SingleAsync(v => v.VoucherId == 501)).IsCancelled);
        Assert.Equal(7, (await FindStockAsync(db, 501, 1, lot: null, expiry: null)).Quantity);
    }

    [Fact]
    public async Task CatchWeight_ShouldRequirePositiveConsistentBaseAndActualWeight()
    {
        await using var db = CreateDb(nameof(CatchWeight_ShouldRequirePositiveConsistentBaseAndActualWeight));
        SeedBaseTopology(db);
        AddItem(db, 600, "CW-SKU", catchWeight: true);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 600,
            VoucherCode = "PN-CW-DEEP",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "creator",
            InboundStatus = InboundStatusEnum.Receiving
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 6001,
            VoucherId = 600,
            ItemId = 600,
            LocationId = 1,
            TransactionQty = 5,
            TransactionUomId = 1,
            BaseQty = 5,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var catchWeight = new CatchWeightService(db);
        await Assert.ThrowsAsync<BusinessRuleException>(() => catchWeight.CaptureAsync(new CatchWeightCaptureRequest
        {
            ItemId = 600,
            WarehouseId = 1,
            VoucherId = 600,
            VoucherDetailId = 6001,
            BaseQuantity = 0,
            ActualWeight = 10,
            WeightUomId = 3,
            CapturePoint = CatchWeightCapturePointEnum.Receive,
            IdempotencyKey = "cw-zero-base"
        }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => catchWeight.CaptureAsync(new CatchWeightCaptureRequest
        {
            ItemId = 600,
            WarehouseId = 1,
            VoucherId = 600,
            VoucherDetailId = 6001,
            BaseQuantity = 5,
            ActualWeight = 20,
            WeightUomId = 3,
            CapturePoint = CatchWeightCapturePointEnum.Receive,
            IdempotencyKey = "cw-outside-tolerance"
        }));

        var entry = await catchWeight.CaptureAsync(new CatchWeightCaptureRequest
        {
            ItemId = 600,
            WarehouseId = 1,
            VoucherId = 600,
            VoucherDetailId = 6001,
            BaseQuantity = 5,
            ActualWeight = 10,
            WeightUomId = 3,
            CapturePoint = CatchWeightCapturePointEnum.Receive,
            CapturedBy = "receiver",
            IdempotencyKey = "cw-ok"
        });
        await db.SaveChangesAsync();

        Assert.Equal(10, entry.ActualWeight);
        Assert.Equal(10, await catchWeight.GetVoucherActualWeightAsync(600));
    }

    [Fact]
    public async Task UomConversion_ShouldPreferItemSpecificAndRejectZeroReverseRate()
    {
        await using var db = CreateDb(nameof(UomConversion_ShouldPreferItemSpecificAndRejectZeroReverseRate));
        SeedBaseTopology(db);
        AddItem(db, 700, "UOM-SKU");
        db.UnitConversions.AddRange(
            new UnitConversion { ConversionId = 7001, FromUomId = 2, ToUomId = 1, ConversionRate = 10, IsActive = true },
            new UnitConversion { ConversionId = 7002, ItemId = 700, FromUomId = 2, ToUomId = 1, ConversionRate = 12, IsActive = true },
            new UnitConversion { ConversionId = 7003, FromUomId = 1, ToUomId = 3, ConversionRate = 0, IsActive = true });
        await db.SaveChangesAsync();

        var service = new VoucherSharedRuleService(db);
        var conversions = await db.UnitConversions.AsNoTracking().Where(c => c.IsActive).ToListAsync();

        Assert.Equal(1, service.ResolveConversionRate(conversions, 700, 1, 1));
        Assert.Equal(12, service.ResolveConversionRate(conversions, 700, 2, 1));
        Assert.Equal(1m / 12m, service.ResolveConversionRate(conversions, 700, 1, 2));
        Assert.Null(service.ResolveConversionRate(conversions, 700, 3, 1));
    }

    [Fact]
    public async Task AppsettingsHostingEvidence_ShouldPreservePolicyAndAvoidSecretValueLeaks()
    {
        var root = ResolveRepoRoot();
        var appsettingsPath = Path.Combine(root, "appsettings.json");
        var evidencePath = Path.Combine(root, "docs", "APPSETTINGS_HOSTING_PROTECTION_EVIDENCE_2026_06_01.md");
        var packageScriptPath = Path.Combine(root, "scripts", "Build-ProductionPackage.ps1");
        Assert.True(File.Exists(appsettingsPath), "appsettings.json must remain present and unchanged by this evidence scope.");
        Assert.True(File.Exists(evidencePath), "Hosting protection evidence document is required.");

        var doc = await File.ReadAllTextAsync(evidencePath);
        var packageScript = await File.ReadAllTextAsync(packageScriptPath);
        Assert.Contains("Khong xoa, khong sua, khong mask appsettings.json", doc, StringComparison.Ordinal);
        Assert.Contains("Config values are not printed by this script.", packageScript, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", packageScript, StringComparison.Ordinal);

        var sensitiveValues = ExtractSensitiveAppsettingsValues(await File.ReadAllTextAsync(appsettingsPath));
        var scannedEvidence = doc + Environment.NewLine + packageScript;
        var leaked = sensitiveValues.Any(value => scannedEvidence.Contains(value, StringComparison.Ordinal));
        Assert.False(leaked, "Evidence docs/scripts must not contain literal secret, API key, password or connection-string values from appsettings.json.");
    }

    private static AppDbContext CreateDb(string testName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{testName}-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedBaseTopology(AppDbContext db)
    {
        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true },
            new UnitOfMeasure { UomId = 2, UomCode = "BOX", UomName = "Box", IsActive = true },
            new UnitOfMeasure { UomId = 3, UomCode = "KG", UomName = "Kilogram", IsActive = true });
        db.Warehouses.AddRange(
            new Warehouse { WarehouseId = 1, WarehouseCode = "WH1", WarehouseName = "Kho chinh", IsActive = true },
            new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Kho phu", IsActive = true });
        db.Zones.AddRange(
            new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "Z1", ZoneName = "Zone 1", ZoneType = ZoneTypeEnum.Storage, IsActive = true },
            new Zone { ZoneId = 2, WarehouseId = 1, ZoneCode = "Z2", ZoneName = "Zone 2", ZoneType = ZoneTypeEnum.Storage, IsActive = true },
            new Zone { ZoneId = 20, WarehouseId = 2, ZoneCode = "Z20", ZoneName = "Zone 20", ZoneType = ZoneTypeEnum.Storage, IsActive = true });
        db.Locations.AddRange(
            new Location { LocationId = 1, ZoneId = 1, LocationCode = "L1", IsActive = true },
            new Location { LocationId = 2, ZoneId = 2, LocationCode = "L2", IsActive = true },
            new Location { LocationId = 3, ZoneId = 1, LocationCode = "L3", IsActive = true },
            new Location { LocationId = 20, ZoneId = 20, LocationCode = "WH2-L1", IsActive = true });
    }

    private static void AddItem(
        AppDbContext db,
        int itemId,
        string itemCode,
        bool trackLot = false,
        bool trackExpiry = false,
        bool trackSerial = false,
        bool catchWeight = false)
    {
        db.Items.Add(new Item
        {
            ItemId = itemId,
            ItemCode = itemCode,
            ItemName = itemCode,
            BaseUomId = 1,
            TrackLot = trackLot,
            TrackExpiry = trackExpiry,
            TrackSerial = trackSerial,
            TrackCatchWeight = catchWeight,
            RequireCatchWeightAtReceive = catchWeight,
            RequireCatchWeightAtPickPack = catchWeight,
            CatchWeightUomId = catchWeight ? 3 : null,
            NominalWeightPerBaseUnit = catchWeight ? 2m : null,
            CatchWeightTolerancePercent = catchWeight ? 10m : null,
            CurrentStock = 0,
            UnitCost = 5,
            IsActive = true
        });
    }

    private static void AddStock(AppDbContext db, int itemId, int locationId, decimal qty, string? lot = null, DateTime? expiry = null)
    {
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = itemId * 100 + locationId,
            ItemId = itemId,
            LocationId = locationId,
            Quantity = qty,
            ReservedQty = 0,
            LotNumber = lot,
            ExpiryDate = expiry,
            HoldStatus = InventoryHoldStatusEnum.Available,
            UpdatedAt = VietnamTime.Now
        });

        var item = db.Items.Local.First(i => i.ItemId == itemId);
        item.CurrentStock += qty;
    }

    private static void AddOutboundVoucher(
        AppDbContext db,
        long voucherId,
        string code,
        int itemId,
        decimal qty,
        VoucherTypeEnum voucherType = VoucherTypeEnum.XuatKho,
        string? lot = null,
        DateTime? expiry = null)
    {
        db.Vouchers.Add(new Voucher
        {
            VoucherId = voucherId,
            VoucherCode = code,
            VoucherType = voucherType,
            WarehouseId = 1,
            CreatedBy = "creator",
            PartialShipmentAllowed = false
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = voucherId * 10 + 1,
            VoucherId = voucherId,
            ItemId = itemId,
            TransactionQty = qty,
            TransactionUomId = 1,
            BaseQty = qty,
            UnitPrice = 5,
            LineAmount = qty * 5,
            LotNumber = lot,
            ExpiryDate = expiry,
            LineNumber = 1
        });
    }

    private static CoreServices CreateServices(AppDbContext db)
    {
        var unitOfWork = new EfUnitOfWork(db);
        var transactionService = new InventoryTransactionService(db);
        var reservationService = new InventoryReservationService(db, transactionService);
        var balanceService = new InventoryBalanceService(db);
        var serialService = new SerialInventoryService(db);
        var catchWeightService = new CatchWeightService(db);
        return new CoreServices(
            new InboundExecutionService(db, unitOfWork, balanceService, transactionService, catchWeightService),
            new OutboundExecutionService(db, unitOfWork, reservationService, balanceService, serialService, transactionService),
            new VoucherCancellationService(db, unitOfWork, reservationService, balanceService, serialService, transactionService));
    }

    private static async Task ConfirmAllPickTasksAsync(AppDbContext db, IOutboundExecutionService outbound, long voucherId, string actor)
    {
        var tasks = await db.PickTasks
            .Where(t => t.VoucherId == voucherId)
            .OrderBy(t => t.PickTaskId)
            .ToListAsync();
        Assert.NotEmpty(tasks);

        foreach (var task in tasks)
        {
            var scan = task.LotNumber ?? (await db.Items.Where(i => i.ItemId == task.ItemId).Select(i => i.ItemCode).SingleAsync());
            var result = await outbound.ConfirmPickTaskAsync(
                task.PickTaskId,
                task.TargetQty,
                scan,
                serialCodes: null,
                actor,
                canOverrideAssignee: true,
                sourceLocationCode: (await db.Locations.Where(l => l.LocationId == task.SourceLocationId).Select(l => l.LocationCode).SingleAsync()));
            Assert.True(result.Succeeded, result.Message);
        }
    }

    private static async Task<ItemLocation> FindStockAsync(AppDbContext db, int itemId, int locationId, string? lot, DateTime? expiry)
        => await db.ItemLocations.SingleAsync(il =>
            il.ItemId == itemId
            && il.LocationId == locationId
            && il.LotNumber == lot
            && il.ExpiryDate == expiry);

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Cannot locate repository root.");
    }

    private static IReadOnlyList<string> ExtractSensitiveAppsettingsValues(string appsettingsJson)
    {
        using var document = JsonDocument.Parse(appsettingsJson);
        var values = new List<string>();
        Collect(document.RootElement, "");
        return values
            .Where(value => value.Length >= 8
                && !value.StartsWith("${", StringComparison.Ordinal)
                && !string.Equals(value, "local" + "host", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        void Collect(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}");
                return;
            }

            if (element.ValueKind != JsonValueKind.String)
                return;

            if (!LooksSensitivePath(path))
                return;

            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value.Trim());
        }

        static bool LooksSensitivePath(string path)
            => path.Contains("ConnectionStrings", StringComparison.OrdinalIgnoreCase)
                || path.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Api:Key", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Secret", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CoreServices(
        IInboundExecutionService Inbound,
        IOutboundExecutionService Outbound,
        IVoucherCancellationService Cancellation);

    private sealed class ItemLocationQuantityConstraintInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            EnforceConstraint(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            EnforceConstraint(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void EnforceConstraint(DbContext? context)
        {
            if (context == null)
                return;

            var invalid = context.ChangeTracker.Entries<ItemLocation>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                .Select(entry => entry.Entity)
                .FirstOrDefault(row => row.Quantity < 0 || row.ReservedQty < 0 || row.Quantity < row.ReservedQty);
            if (invalid != null)
            {
                throw new DbUpdateException(
                    $"Simulated CK_ItemLocations_Qty_NonNegative violation for ItemLocationId={invalid.ItemLocationId}.");
            }
        }
    }
}
