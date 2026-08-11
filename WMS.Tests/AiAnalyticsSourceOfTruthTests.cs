using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using WMS.Authorization;
using WMS.Common;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public sealed class AiAnalyticsSourceOfTruthTests
{
    [Fact]
    public async Task SlowMoving_ShouldUseLastOutboundAndKeepReceiptAsSeparateContext()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        AddItem(db, 10, "SLOW-10", unitCost: 12m);
        AddItem(db, 11, "ACTIVE-11", unitCost: 8m);
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 10, ItemId = 10, LocationId = 1, Quantity = 10m },
            new ItemLocation { ItemLocationId = 11, ItemId = 11, LocationId = 1, Quantity = 10m });

        var now = VietnamTime.Now;
        db.InventoryTransactions.AddRange(
            Ledger(10, 10, InventoryTransactionTypeEnum.Ship, -2m, now.AddDays(-120), 1),
            Ledger(11, 10, InventoryTransactionTypeEnum.Receive, 10m, now.AddDays(-1), 2),
            Ledger(12, 11, InventoryTransactionTypeEnum.Ship, -1m, now.AddDays(-2), 3));
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.SlowMovingReport(warehouseId: 1, days: 90);

        Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<SlowMovingItemRow>>((object)controller.ViewBag.Data);
        var row = Assert.Single(rows);
        Assert.Equal("SLOW-10", row.ItemCode);
        Assert.Equal(now.AddDays(-120).Date, row.LastOutboundDate?.Date);
        Assert.Equal(now.AddDays(-1).Date, row.LastReceiptDate?.Date);
        Assert.True(row.DaysSinceLastOutbound >= 119);
    }

    [Fact]
    public async Task Analytics_ShouldCalculateDaysOfSupplyPerSkuInBaseUomAndOwnerScope()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        AddItem(db, 20, "DOS-20", unitCost: 5m);
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 20, ItemId = 20, OwnerPartnerId = 101, LocationId = 1, Quantity = 100m, ReservedQty = 10m },
            new ItemLocation { ItemLocationId = 21, ItemId = 20, OwnerPartnerId = 202, LocationId = 1, Quantity = 900m });

        var now = VietnamTime.Now;
        db.InventoryTransactions.AddRange(
            Ledger(20, 20, InventoryTransactionTypeEnum.Ship, -30m, now.AddDays(-10), 20, ownerPartnerId: 101),
            Ledger(21, 20, InventoryTransactionTypeEnum.Ship, -300m, now.AddDays(-10), 21, ownerPartnerId: 202));

        for (var index = 0; index < 12; index++)
        {
            db.Vouchers.Add(new Voucher
            {
                VoucherId = 100 + index,
                VoucherCode = $"OUT-{index:00}",
                VoucherType = VoucherTypeEnum.XuatKho,
                VoucherDate = now.Date.AddDays(-index),
                WarehouseId = 1,
                OwnerPartnerId = 101,
                IsPosted = true,
                CreatedBy = "AUDIT_TEST_AI1"
            });
        }
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: WmsRoles.Manager, warehouseId: 1, ownerPartnerId: 101);
        var result = await controller.Analytics(warehouseId: null, days: 30);

        Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<DaysOfSupplyItemRow>>((object)controller.ViewBag.DaysOfSupplyRows);
        var row = Assert.Single(rows);
        Assert.Equal("DOS-20", row.ItemCode);
        Assert.Equal("CAI", row.UomCode);
        Assert.Equal(90m, row.AvailableBaseQty);
        Assert.Equal(30m, row.OutboundBaseQty);
        Assert.Equal(1m, row.AverageDailyOutboundBaseQty);
        Assert.Equal(90m, row.DaysOfSupply);
        Assert.Equal("DEMAND_SAMPLE_INSUFFICIENT", row.DataQualityCode);
        Assert.False(row.IsReplenishmentRisk);
        Assert.Equal(90m, (decimal?)controller.ViewBag.DaysOfSupply);
        Assert.Equal(1, (int)controller.ViewBag.DaysOfSupplySampleCount);
    }

    [Fact]
    public async Task Analytics_ShouldSeparateDaysOfSupplyByOwnerAndIncludeConsignedAvailability()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        db.Partners.AddRange(
            new Partner { PartnerId = 101, PartnerCode = "OWN-101", PartnerName = "Chủ hàng 101", IsActive = true },
            new Partner { PartnerId = 202, PartnerCode = "OWN-202", PartnerName = "Chủ hàng 202", IsActive = true });
        AddItem(db, 25, "DOS-MULTI-25", unitCost: 5m);
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 25,
                ItemId = 25,
                OwnerPartnerId = 101,
                LocationId = 1,
                Quantity = 90m,
                HoldStatus = InventoryHoldStatusEnum.Consigned
            },
            new ItemLocation
            {
                ItemLocationId = 26,
                ItemId = 25,
                OwnerPartnerId = 202,
                LocationId = 1,
                Quantity = 300m,
                HoldStatus = InventoryHoldStatusEnum.Available
            });
        var now = VietnamTime.Now;
        db.InventoryTransactions.AddRange(
            Ledger(25, 25, InventoryTransactionTypeEnum.Ship, -30m, now.AddDays(-10), 25, ownerPartnerId: 101),
            Ledger(26, 25, InventoryTransactionTypeEnum.Ship, -60m, now.AddDays(-10), 26, ownerPartnerId: 202));
        await db.SaveChangesAsync();

        var controller = CreateController(db, warehouseId: 1);
        var result = await controller.Analytics(warehouseId: null, days: 30);

        Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<DaysOfSupplyItemRow>>((object)controller.ViewBag.DaysOfSupplyRows);
        Assert.Equal(2, rows.Count);
        var owner101 = Assert.Single(rows, row => row.OwnerPartnerId == 101);
        Assert.Equal("Chủ hàng 101", owner101.OwnerPartnerName);
        Assert.Equal(90m, owner101.AvailableBaseQty);
        Assert.Equal(90m, owner101.DaysOfSupply);
        var owner202 = Assert.Single(rows, row => row.OwnerPartnerId == 202);
        Assert.Equal(300m, owner202.AvailableBaseQty);
        Assert.Equal(150m, owner202.DaysOfSupply);
        Assert.Equal(1, (int)controller.ViewBag.StockKeepingUnitCount);
    }

    [Fact]
    public async Task Analytics_ShouldOnlyFlagReplenishmentRiskWhenDemandAndLeadTimeAreReliable()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        db.Partners.AddRange(
            new Partner
            {
                PartnerId = 101,
                PartnerCode = "AUDIT_TEST_OWNER_101",
                PartnerName = "Chủ hàng kiểm thử",
                PartnerType = PartnerTypeEnum.Both,
                IsActive = true
            },
            new Partner
            {
                PartnerId = 900,
                PartnerCode = "AUDIT_TEST_SUPPLIER_900",
                PartnerName = "Nhà cung cấp lead time 10",
                PartnerType = PartnerTypeEnum.Supplier,
                LeadTimeDays = 10,
                IsActive = true
            },
            new Partner
            {
                PartnerId = 901,
                PartnerCode = "AUDIT_TEST_SUPPLIER_901",
                PartnerName = "Nhà cung cấp lead time 20",
                PartnerType = PartnerTypeEnum.Supplier,
                LeadTimeDays = 20,
                IsActive = true
            });
        AddItem(db, 28, "RISK-READY-28", unitCost: 5m);
        AddItem(db, 29, "RISK-CONFLICT-29", unitCost: 5m);
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 28, ItemId = 28, OwnerPartnerId = 101, LocationId = 1, Quantity = 20m },
            new ItemLocation { ItemLocationId = 29, ItemId = 29, OwnerPartnerId = 101, LocationId = 1, Quantity = 20m });

        var now = VietnamTime.Now;
        var transactionId = 280L;
        foreach (var offset in new[] { 1, 8, 15, 22 })
        {
            db.InventoryTransactions.Add(Ledger(transactionId, 28, InventoryTransactionTypeEnum.Ship, -30m, now.AddDays(-offset), (int)transactionId, ownerPartnerId: 101));
            transactionId++;
            db.InventoryTransactions.Add(Ledger(transactionId, 29, InventoryTransactionTypeEnum.Ship, -30m, now.AddDays(-offset), (int)transactionId, ownerPartnerId: 101));
            transactionId++;
        }

        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 280,
                VoucherCode = "AUDIT_TEST_INBOUND_READY",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = now.Date,
                WarehouseId = 1,
                OwnerPartnerId = 101,
                PartnerId = 900,
                IsPosted = true,
                TotalLines = 2,
                CreatedBy = "AUDIT_TEST_AI1"
            },
            new Voucher
            {
                VoucherId = 281,
                VoucherCode = "AUDIT_TEST_INBOUND_CONFLICT",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = now.Date,
                WarehouseId = 1,
                OwnerPartnerId = 101,
                PartnerId = 901,
                IsPosted = true,
                TotalLines = 1,
                CreatedBy = "AUDIT_TEST_AI1"
            });
        db.VoucherDetails.AddRange(
            new VoucherDetail
            {
                VoucherDetailId = 280,
                VoucherId = 280,
                ItemId = 28,
                OwnerPartnerId = 101,
                TransactionQty = 20m,
                TransactionUomId = 1,
                BaseQty = 20m,
                LineNumber = 1
            },
            new VoucherDetail
            {
                VoucherDetailId = 281,
                VoucherId = 280,
                ItemId = 29,
                OwnerPartnerId = 101,
                TransactionQty = 20m,
                TransactionUomId = 1,
                BaseQty = 20m,
                LineNumber = 2
            },
            new VoucherDetail
            {
                VoucherDetailId = 282,
                VoucherId = 281,
                ItemId = 29,
                OwnerPartnerId = 101,
                TransactionQty = 20m,
                TransactionUomId = 1,
                BaseQty = 20m,
                LineNumber = 1
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: WmsRoles.Manager, warehouseId: 1, ownerPartnerId: 101);
        var result = await controller.Analytics(warehouseId: null, days: 30);

        Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<DaysOfSupplyItemRow>>((object)controller.ViewBag.DaysOfSupplyRows);
        var ready = Assert.Single(rows, row => row.ItemId == 28);
        Assert.Equal(4, ready.DemandActiveDayCount90);
        Assert.Equal(10, ready.LeadTimeDays);
        Assert.Equal(4m, ready.Velocity30DayBaseQty);
        Assert.Equal(5m, ready.RiskDaysOfSupply);
        Assert.Equal("READY", ready.DataQualityCode);
        Assert.True(ready.IsRiskEligible);
        Assert.True(ready.IsReplenishmentRisk);

        var conflict = Assert.Single(rows, row => row.ItemId == 29);
        Assert.Equal(2, conflict.SupplierSampleCount);
        Assert.Null(conflict.LeadTimeDays);
        Assert.Equal("LEAD_TIME_CONFLICT", conflict.DataQualityCode);
        Assert.False(conflict.IsRiskEligible);
        Assert.False(conflict.IsReplenishmentRisk);
        Assert.Equal(1, (int)controller.ViewBag.ReplenishmentEligibleCount);
        Assert.Equal(1, (int)controller.ViewBag.ReplenishmentRiskCount);
    }

    [Fact]
    public async Task Analytics_ShouldScopeQualityKpisByWarehouseAndOwner()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        AddItem(db, 27, "QC-SCOPE-27", unitCost: 5m);
        var now = VietnamTime.Now;
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 270,
                VoucherCode = "QC-OWNER-101",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = now.Date,
                WarehouseId = 1,
                OwnerPartnerId = 101,
                CreatedBy = "AUDIT_TEST_AI1"
            },
            new Voucher
            {
                VoucherId = 271,
                VoucherCode = "QC-OWNER-202",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = now.Date,
                WarehouseId = 1,
                OwnerPartnerId = 202,
                CreatedBy = "AUDIT_TEST_AI1"
            });
        db.QualityInspections.AddRange(
            new QualityInspection
            {
                QualityInspectionId = 270,
                VoucherId = 270,
                ItemId = 27,
                WarehouseId = 1,
                OverallResult = QualityStatusEnum.Passed,
                CreatedAt = now
            },
            new QualityInspection
            {
                QualityInspectionId = 271,
                VoucherId = 271,
                ItemId = 27,
                WarehouseId = 1,
                OverallResult = QualityStatusEnum.Failed,
                CreatedAt = now
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: WmsRoles.Manager, warehouseId: 1, ownerPartnerId: 101);
        var result = await controller.Analytics(warehouseId: null, days: 30);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(1, (int)controller.ViewBag.QcTotal);
        Assert.Equal(1, (int)controller.ViewBag.QcPassed);
        Assert.Equal(0, (int)controller.ViewBag.QcFailed);
    }

    [Fact]
    public async Task SupplierInboundScorecard_ShouldUseOnlyCompleteSamplesAndRespectOwnerScope()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        db.Partners.AddRange(
            new Partner { PartnerId = 101, PartnerCode = "AUDIT_TEST_OWNER_101", PartnerName = "Chủ hàng kiểm thử 101", PartnerType = PartnerTypeEnum.Both },
            new Partner { PartnerId = 202, PartnerCode = "AUDIT_TEST_OWNER_202", PartnerName = "Chủ hàng kiểm thử 202", PartnerType = PartnerTypeEnum.Both },
            new Partner { PartnerId = 910, PartnerCode = "AUDIT_TEST_SUPPLIER_READY", PartnerName = "Nhà cung cấp đủ dữ liệu", PartnerType = PartnerTypeEnum.Supplier },
            new Partner { PartnerId = 911, PartnerCode = "AUDIT_TEST_SUPPLIER_MISSING", PartnerName = "Nhà cung cấp thiếu dữ liệu", PartnerType = PartnerTypeEnum.Supplier });
        AddItem(db, 60, "AUDIT_TEST_SCORECARD_ITEM", unitCost: 5m);
        var item = db.Items.Local.Single(entity => entity.ItemId == 60);
        item.TrackLot = true;
        item.TrackExpiry = true;

        var now = VietnamTime.Now;
        var readyVoucher = new Voucher
        {
            VoucherId = 600,
            VoucherCode = "AUDIT_TEST_INBOUND_SCORECARD_READY",
            VoucherType = VoucherTypeEnum.NhapKho,
            VoucherDate = now.Date,
            WarehouseId = 1,
            OwnerPartnerId = 101,
            PartnerId = 910,
            ReferenceNo = "AUDIT_TEST_REFERENCE_READY",
            InboundStatus = InboundStatusEnum.Completed,
            IsPosted = true,
            DockAppointmentEnd = now.AddHours(-5),
            GateInAt = now.AddHours(-6),
            CompletedAt = now.AddHours(-2),
            CreatedBy = "AUDIT_TEST_AI1"
        };
        var missingVoucher = new Voucher
        {
            VoucherId = 601,
            VoucherCode = "AUDIT_TEST_INBOUND_SCORECARD_MISSING",
            VoucherType = VoucherTypeEnum.NhapKho,
            VoucherDate = now.Date,
            WarehouseId = 1,
            OwnerPartnerId = 101,
            PartnerId = 911,
            InboundStatus = InboundStatusEnum.Receiving,
            CreatedBy = "AUDIT_TEST_AI1"
        };
        var hiddenOwnerVoucher = new Voucher
        {
            VoucherId = 602,
            VoucherCode = "AUDIT_TEST_INBOUND_SCORECARD_HIDDEN",
            VoucherType = VoucherTypeEnum.NhapKho,
            VoucherDate = now.Date,
            WarehouseId = 1,
            OwnerPartnerId = 202,
            PartnerId = 910,
            ReferenceNo = "AUDIT_TEST_REFERENCE_HIDDEN",
            InboundStatus = InboundStatusEnum.Completed,
            IsPosted = true,
            DockAppointmentEnd = now.AddHours(-4),
            GateInAt = now.AddHours(-5),
            CompletedAt = now.AddHours(-1),
            CreatedBy = "AUDIT_TEST_AI1"
        };
        db.Vouchers.AddRange(readyVoucher, missingVoucher, hiddenOwnerVoucher);

        var readyDetail = new VoucherDetail
        {
            VoucherDetailId = 600,
            VoucherId = 600,
            ItemId = 60,
            OwnerPartnerId = 101,
            TransactionQty = 10m,
            TransactionUomId = 1,
            ConversionRate = 1m,
            BaseQty = 10m,
            LotNumber = "AUDIT_TEST_LOT_READY",
            ExpiryDate = now.Date.AddYears(1),
            LineNumber = 1
        };
        db.VoucherDetails.AddRange(
            readyDetail,
            new VoucherDetail
            {
                VoucherDetailId = 601,
                VoucherId = 601,
                ItemId = 60,
                OwnerPartnerId = 101,
                TransactionQty = 5m,
                TransactionUomId = 1,
                ConversionRate = 1m,
                BaseQty = 5m,
                LineNumber = 1
            },
            new VoucherDetail
            {
                VoucherDetailId = 602,
                VoucherId = 602,
                ItemId = 60,
                OwnerPartnerId = 202,
                TransactionQty = 50m,
                TransactionUomId = 1,
                ConversionRate = 1m,
                BaseQty = 50m,
                LotNumber = "AUDIT_TEST_LOT_HIDDEN",
                ExpiryDate = now.Date.AddYears(1),
                LineNumber = 1
            });
        db.QualityInspections.Add(new QualityInspection
        {
            QualityInspectionId = 600,
            VoucherId = 600,
            VoucherDetailId = 600,
            ItemId = 60,
            WarehouseId = 1,
            TotalQty = 10m,
            SampleQty = 10m,
            PassedQty = 10m,
            OverallResult = QualityStatusEnum.Passed,
            CreatedAt = now
        });
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryTransactionId = 600,
            TransactionType = InventoryTransactionTypeEnum.Adjust,
            TransactionGroupKey = "AUDIT_TEST_AI1:SCORECARD:600",
            IdempotencyKey = "AUDIT_TEST_AI1:SCORECARD:600:ADJUST",
            VoucherId = 600,
            WarehouseId = 1,
            OwnerPartnerId = 101,
            ItemId = 60,
            LocationId = 1,
            QuantityDelta = -2m,
            AvailableDelta = -2m,
            Actor = "AUDIT_TEST_AI1",
            TransactionAt = now
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: WmsRoles.Manager, warehouseId: 1, ownerPartnerId: 101);
        var result = await controller.SupplierInboundScorecard(warehouseId: null, days: 90);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<SupplierInboundScorecardRow>>(view.Model);
        Assert.Equal(2, rows.Count);

        var ready = Assert.Single(rows, row => row.PartnerId == 910);
        Assert.Equal(1, ready.InboundVoucherCount);
        Assert.Equal(100m, ready.OnTimePercent);
        Assert.Equal(100m, ready.InFullPercent);
        Assert.Equal(100m, ready.QualityPassPercent);
        Assert.Equal(100m, ready.DocumentAccuracyPercent);
        Assert.Equal(10m, ready.ReceivedBaseQty);
        Assert.Equal(0m, ready.DefectOrShortBaseQty);
        Assert.Equal(4m, ready.MedianDockToStockHours);
        Assert.Equal(1, ready.AdjustmentTransactionCount);
        Assert.Equal(2m, ready.AdjustmentAbsoluteBaseQty);
        Assert.Equal(new[] { "DAMAGE_REASON_TAXONOMY_MISSING" }, ready.DataQualityCodes);

        var missing = Assert.Single(rows, row => row.PartnerId == 911);
        Assert.Null(missing.OnTimePercent);
        Assert.Null(missing.InFullPercent);
        Assert.Null(missing.QualityPassPercent);
        Assert.Null(missing.DocumentAccuracyPercent);
        Assert.Null(missing.MedianDockToStockHours);
        Assert.Contains("APPOINTMENT_TIMESTAMP_MISSING", missing.DataQualityCodes);
        Assert.Contains("QC_SAMPLE_MISSING", missing.DataQualityCodes);
        Assert.Contains("DOCK_TO_STOCK_MILESTONE_MISSING", missing.DataQualityCodes);

        Assert.Equal(2, (int)controller.ViewBag.SupplierCount);
        Assert.Equal(2, (int)controller.ViewBag.VoucherCount);
        Assert.Equal(1, (int)controller.ViewBag.OnTimeSampleCount);
        Assert.Equal(1, (int)controller.ViewBag.QcSampleCount);
    }

    [Fact]
    public async Task SpaceUtilization_ShouldNotInventCapacityAndShouldUseKgWhenMasterDataIsComplete()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        db.Locations.Local.Single(location => location.LocationId == 2).MaxWeightCapacityKg = 100m;
        AddItem(db, 30, "NO-CAPACITY-30", unitCost: 1m, weightKg: 2m);
        AddItem(db, 31, "WEIGHTED-31", unitCost: 1m, weightKg: 2m);
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 30, ItemId = 30, LocationId = 1, Quantity = 20m },
            new ItemLocation { ItemLocationId = 31, ItemId = 31, LocationId = 2, Quantity = 20m });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.SpaceUtilization(warehouseId: 1);

        Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<SpaceUtilizationRow>>((object)controller.ViewBag.Rows);
        var missing = Assert.Single(rows, row => row.LocationId == 1);
        Assert.Null(missing.CurrentLoad);
        Assert.Null(missing.MaxCapacity);
        Assert.Null(missing.UsedPercent);
        Assert.Equal("CAPACITY_DATA_MISSING", missing.DataQualityCode);

        var measured = Assert.Single(rows, row => row.LocationId == 2);
        Assert.Equal(40m, measured.CurrentLoad);
        Assert.Equal(100m, measured.MaxCapacity);
        Assert.Equal(40m, measured.UsedPercent);
        Assert.Equal("kg", measured.CapacityUnit);
        Assert.Equal("CAPACITY_OK", measured.DataQualityCode);
        Assert.Equal(1, (int)controller.ViewBag.CapacitySampleCount);
    }

    [Fact]
    public async Task DockToStock_ShouldExcludeMissingOrInvalidMilestonesFromPercentiles()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        var now = VietnamTime.Now;
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 40,
                VoucherCode = "IN-COMPLETE-MILESTONES",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = now.Date,
                WarehouseId = 1,
                IsPosted = true,
                ExpectedArrivalAt = now.AddHours(-12),
                CreatedAt = now.AddHours(-10),
                UpdatedAt = now.AddHours(-1),
                CreatedBy = "AUDIT_TEST_AI1"
            },
            new Voucher
            {
                VoucherId = 41,
                VoucherCode = "IN-COMPLETE-ORDER",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = now.Date,
                WarehouseId = 1,
                IsPosted = true,
                DockArrivalAt = now.AddHours(-6),
                ReceivedAt = now.AddHours(-5),
                CompletedAt = now.AddHours(-2),
                CreatedAt = now.AddHours(-7),
                CreatedBy = "AUDIT_TEST_AI1"
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.DockToStock(warehouseId: 1, days: 30);

        Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<DockToStockRow>>((object)controller.ViewBag.Rows);
        Assert.Equal(2, rows.Count);
        var missing = Assert.Single(rows, row => row.VoucherId == 40);
        Assert.Null(missing.DockArrival);
        Assert.Null(missing.ReceiveStart);
        Assert.Null(missing.Completed);
        Assert.Null(missing.TotalHours);
        Assert.Equal("missing", missing.Sla);
        Assert.Contains("Giờ đến thực tế", missing.MissingMilestones);

        var complete = Assert.Single(rows, row => row.VoucherId == 41);
        Assert.Equal(1m, complete.DockToReceiveHours);
        Assert.Equal(3m, complete.ReceiveToStockHours);
        Assert.Equal(4m, complete.TotalHours);
        Assert.Equal("good", complete.Sla);
        Assert.Equal(1, (int)controller.ViewBag.SampleCount);
        Assert.Equal(1, (int)controller.ViewBag.MissingMilestoneCount);
        Assert.Equal(4m, (decimal?)controller.ViewBag.MedianTotal);
        Assert.Equal(4m, (decimal?)controller.ViewBag.P90Total);
        Assert.Equal(4m, (decimal?)controller.ViewBag.P95Total);
    }

    [Fact]
    public async Task AbcInventoryValue_ShouldMarkMissingValuationWithoutFabricatingValue()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        AddItem(db, 50, "NO-VALUE-50", unitCost: 0m);
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 50,
            ItemId = 50,
            LocationId = 1,
            Quantity = 25m
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.AbcAnalysis(warehouseId: 1);

        Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<AbcInventoryValueRow>>((object)controller.ViewBag.Data);
        var row = Assert.Single(rows);
        Assert.Equal(0m, row.TotalStockValue);
        Assert.Null(row.CumulativePct);
        Assert.Equal("N", row.AbcClass);
        Assert.Equal(0m, (decimal)controller.ViewBag.TotalValue);
        Assert.False((bool)controller.ViewBag.HasValuationData);
        Assert.Equal(1, (int)controller.ViewBag.MissingValuationCount);
    }

    [Fact]
    public async Task Analytics_ShouldUseExactlyRequestedDaysAndHideFinancialValueWithoutPermission()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        var today = VietnamTime.Now.Date;
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 610,
                VoucherCode = "IN-DAY-0",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = today,
                WarehouseId = 1,
                IsPosted = true,
                TotalAmount = 100m,
                CreatedBy = "AUDIT_TEST_AI1"
            },
            new Voucher
            {
                VoucherId = 611,
                VoucherCode = "IN-DAY-6",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = today.AddDays(-6),
                WarehouseId = 1,
                IsPosted = true,
                TotalAmount = 200m,
                CreatedBy = "AUDIT_TEST_AI1"
            },
            new Voucher
            {
                VoucherId = 612,
                VoucherCode = "IN-DAY-7-EXCLUDED",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = today.AddDays(-7),
                WarehouseId = 1,
                IsPosted = true,
                TotalAmount = 300m,
                CreatedBy = "AUDIT_TEST_AI1"
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: WmsRoles.Manager, warehouseId: 1);
        var result = await controller.Analytics(warehouseId: null, days: 7);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(7, Assert.IsAssignableFrom<List<string>>((object)controller.ViewBag.ChartDates).Count);
        Assert.Equal(2, (int)controller.ViewBag.TotalInbound);
        Assert.False((bool)controller.ViewBag.CanSeeFinancial);
        Assert.Null((decimal?)controller.ViewBag.TotalValue);
    }

    [Fact]
    public async Task ExpiryReport_ShouldScopeOwnerAndSummarizeBeforeDisplayLimit()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        AddItem(db, 620, "EXPIRY-OWNER", unitCost: 1m);
        var expiry = VietnamTime.Now.Date.AddDays(10);
        for (var index = 0; index < 501; index++)
        {
            db.ItemLocations.Add(new ItemLocation
            {
                ItemLocationId = 62000 + index,
                ItemId = 620,
                OwnerPartnerId = 101,
                LocationId = 1,
                LotNumber = $"LOT-{index:000}",
                ExpiryDate = expiry,
                Quantity = 1m
            });
        }
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 62999,
            ItemId = 620,
            OwnerPartnerId = 202,
            LocationId = 1,
            LotNumber = "OTHER-OWNER",
            ExpiryDate = expiry,
            Quantity = 99m
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, role: WmsRoles.Manager, warehouseId: 1, ownerPartnerId: 101);
        var result = await controller.ExpiryReport(warehouseId: null);

        Assert.IsType<ViewResult>(result);
        var displayRows = Assert.IsAssignableFrom<System.Collections.ICollection>((object)controller.ViewBag.Data);
        Assert.Equal(500, displayRows.Count);
        var summary = (object)controller.ViewBag.Summary;
        Assert.Equal(501, (int)summary.GetType().GetProperty("Within30")!.GetValue(summary)!);
        Assert.Equal(501m, (decimal)summary.GetType().GetProperty("TotalQty")!.GetValue(summary)!);
    }

    [Fact]
    public async Task AbcInventoryValue_ShouldKeepThresholdCrossingItemInCurrentClassAndZeroValueUnclassified()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        AddItem(db, 630, "ABC-70", unitCost: 70m);
        AddItem(db, 631, "ABC-20", unitCost: 20m);
        AddItem(db, 632, "ABC-10", unitCost: 10m);
        AddItem(db, 633, "ABC-N", unitCost: 0m);
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 630, ItemId = 630, LocationId = 1, Quantity = 1m },
            new ItemLocation { ItemLocationId = 631, ItemId = 631, LocationId = 1, Quantity = 1m },
            new ItemLocation { ItemLocationId = 632, ItemId = 632, LocationId = 1, Quantity = 1m },
            new ItemLocation { ItemLocationId = 633, ItemId = 633, LocationId = 1, Quantity = 1m });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.AbcAnalysis(warehouseId: 1);

        Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<AbcInventoryValueRow>>((object)controller.ViewBag.Data);
        Assert.Equal("A", Assert.Single(rows, row => row.ItemCode == "ABC-70").AbcClass);
        Assert.Equal("A", Assert.Single(rows, row => row.ItemCode == "ABC-20").AbcClass);
        Assert.Equal("B", Assert.Single(rows, row => row.ItemCode == "ABC-10").AbcClass);
        Assert.Equal("N", Assert.Single(rows, row => row.ItemCode == "ABC-N").AbcClass);
    }

    [Fact]
    public async Task DockToStock_ShouldUseAllEligibleRowsForKpiAndLimitOnlyDisplayedRows()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        var now = VietnamTime.Now;
        for (var index = 0; index < 201; index++)
        {
            var arrival = now.AddDays(-1).AddMinutes(-index);
            db.Vouchers.Add(new Voucher
            {
                VoucherId = 6400 + index,
                VoucherCode = $"IN-DTS-{index:000}",
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = now.Date.AddDays(-1),
                WarehouseId = 1,
                IsPosted = true,
                DockArrivalAt = arrival,
                ReceivedAt = arrival.AddMinutes(15),
                CompletedAt = arrival.AddHours(1),
                CreatedAt = now.AddMinutes(-index),
                CreatedBy = "AUDIT_TEST_AI1"
            });
        }
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.DockToStock(warehouseId: 1, days: 30);

        Assert.IsType<ViewResult>(result);
        var displayRows = Assert.IsAssignableFrom<IReadOnlyList<DockToStockRow>>((object)controller.ViewBag.Rows);
        Assert.Equal(200, displayRows.Count);
        Assert.Equal(201, (int)controller.ViewBag.SampleCount);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AI1-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static ReportsController CreateController(
        AppDbContext db,
        string role = WmsRoles.Admin,
        int? warehouseId = null,
        int? ownerPartnerId = null)
    {
        var controller = new ReportsController(
            db,
            NullLogger<ReportsController>.Instance,
            new InventoryBalanceService(db),
            new EfUnitOfWork(db));
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "AUDIT_TEST_AI1"),
            new(ClaimTypes.Role, role)
        };
        if (warehouseId.HasValue)
            claims.Add(new Claim("WarehouseId", warehouseId.Value.ToString()));
        if (ownerPartnerId.HasValue)
            claims.Add(new Claim(TenantClaimTypes.OwnerPartnerId, ownerPartnerId.Value.ToString()));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "AI1Test"))
            }
        };
        return controller;
    }

    private static void SeedWarehouseGraph(AppDbContext db)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure
        {
            UomId = 1,
            UomCode = "CAI",
            UomName = "Cái",
            IsActive = true
        });
        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 1,
            WarehouseCode = "AUDIT_TEST_WH1",
            WarehouseName = "Kho kiểm thử AI-1",
            IsActive = true
        });
        db.Zones.Add(new Zone
        {
            ZoneId = 1,
            WarehouseId = 1,
            ZoneCode = "STO",
            ZoneName = "Lưu trữ",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        });
        db.Locations.AddRange(
            new Location
            {
                LocationId = 1,
                ZoneId = 1,
                LocationCode = "AUDIT_TEST_BIN_01",
                MaxCapacity = 999m,
                IsActive = true
            },
            new Location
            {
                LocationId = 2,
                ZoneId = 1,
                LocationCode = "AUDIT_TEST_BIN_02",
                MaxCapacity = 999m,
                IsActive = true
            });
    }

    private static void AddItem(
        AppDbContext db,
        int itemId,
        string itemCode,
        decimal unitCost,
        decimal? weightKg = null)
    {
        db.Items.Add(new Item
        {
            ItemId = itemId,
            ItemCode = itemCode,
            ItemName = itemCode,
            BaseUomId = 1,
            UnitCost = unitCost,
            Weight = weightKg,
            IsActive = true,
            CreatedBy = "AUDIT_TEST_AI1"
        });
    }

    private static InventoryTransaction Ledger(
        long transactionId,
        int itemId,
        InventoryTransactionTypeEnum type,
        decimal quantityDelta,
        DateTime transactionAt,
        int sequence,
        int? ownerPartnerId = null)
    {
        return new InventoryTransaction
        {
            InventoryTransactionId = transactionId,
            TransactionType = type,
            TransactionGroupKey = $"AUDIT_TEST_AI1:{sequence}",
            IdempotencyKey = $"AUDIT_TEST_AI1:{sequence}:ledger",
            WarehouseId = 1,
            OwnerPartnerId = ownerPartnerId,
            ItemId = itemId,
            LocationId = 1,
            QuantityDelta = quantityDelta,
            AvailableDelta = quantityDelta,
            Actor = "AUDIT_TEST_AI1",
            TransactionAt = transactionAt
        };
    }
}
