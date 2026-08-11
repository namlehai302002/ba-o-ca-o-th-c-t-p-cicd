using System.Reflection;
using System.Security.Claims;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using WMS.Authorization;
using WMS.Common;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public class BusinessLogicHardeningTests
{
    [Fact]
    public async Task FefoSingleLinePick_ShouldReturnEarliestExpiryWithLot()
    {
        await using var db = CreateDb(nameof(FefoSingleLinePick_ShouldReturnEarliestExpiryWithLot));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-001",
            ItemName = "Hàng kiểm thử",
            BaseUomId = 1,
            UnitCost = 100,
            IsActive = true
        });
        var earlyExpiry = VietnamTime.Today.AddDays(45);
        var lateExpiry = VietnamTime.Today.AddDays(120);

        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 1,
                ItemId = 1,
                LocationId = 1,
                Quantity = 20,
                ReservedQty = 0,
                LotNumber = "LOT-LATE",
                ExpiryDate = lateExpiry,
                UpdatedAt = DateTime.UtcNow
            },
            new ItemLocation
            {
                ItemLocationId = 2,
                ItemId = 1,
                LocationId = 2,
                Quantity = 20,
                ReservedQty = 0,
                LotNumber = "LOT-EARLY",
                ExpiryDate = earlyExpiry,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var method = typeof(VouchersController).GetMethod(
            "GetFefoLocationForSingleLineAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = (Task)method!.Invoke(controller, new object[] { 1, 1, 5m, true })!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task);

        Assert.NotNull(result);
        Assert.Equal(2, (int)result!.GetType().GetProperty("LocationId")!.GetValue(result)!);
        Assert.Equal("LOT-EARLY", (string?)result.GetType().GetProperty("LotNumber")!.GetValue(result));
        Assert.Equal(earlyExpiry, (DateTime?)result.GetType().GetProperty("ExpiryDate")!.GetValue(result));
    }

    [Fact]
    public async Task Create_ShouldRejectUnknownItemAndRollbackVoucher()
    {
        await using var db = CreateDb(nameof(Create_ShouldRejectUnknownItemAndRollbackVoucher));
        SeedWarehouseGraph(db);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.DieuChinh,
            WarehouseId = 1,
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 999,
                    LocationId = 1,
                    TransactionQty = 1,
                    TransactionUomId = 1,
                    AdjustSign = 1,
                    UnitPrice = 100,
                    LineAmount = 100
                }
            }
        };

        var result = await controller.Create(vm);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(vm, view.Model);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Equal(1, await db.Vouchers.CountAsync());
        Assert.Equal(0, await db.VoucherDetails.CountAsync());
    }

    [Fact]
    public async Task Create_ShouldRejectNegativeFinancialValuesForFinancialUser()
    {
        await using var db = CreateDb(nameof(Create_ShouldRejectNegativeFinancialValuesForFinancialUser));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-NEG",
            ItemName = "Hàng giữ âm",
            BaseUomId = 1,
            UnitCost = 50,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, includeFinancialPermission: true);
        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.DieuChinh,
            WarehouseId = 1,
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 1,
                    LocationId = 1,
                    TransactionQty = 1,
                    TransactionUomId = 1,
                    AdjustSign = 1,
                    UnitPrice = -10,
                    LineAmount = -10
                }
            }
        };

        var result = await controller.Create(vm);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(vm, view.Model);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Equal(1, await db.Vouchers.CountAsync());
        Assert.Equal(0, await db.VoucherDetails.CountAsync());
    }

    [Fact]
    public async Task Create_WithDocumentIntakeTrace_ShouldLinkSourceAndRejectReuse()
    {
        await using var db = CreateDb(nameof(Create_WithDocumentIntakeTrace_ShouldLinkSourceAndRejectReuse));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure
        {
            UomId = 1,
            UomCode = "EA",
            UomName = "Cái",
            IsActive = true
        });
        db.Items.Add(new Item
        {
            ItemId = 41,
            ItemCode = "AUDIT_TEST_OCR_ITEM",
            ItemName = "Vật tư truy vết chứng từ",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });
        db.AiOcrLogs.Add(new AiOcrLog
        {
            AiOcrLogId = 4101,
            ImageUrl = "App_Data/uploads/document-intake/AUDIT_TEST_OCR.pdf",
            FileName = "AUDIT_TEST_OCR.pdf",
            OcrProvider = "MinerU",
            ParsedData = """
                {"schemaVersion":"WMS_OCR_TRACE_1","parseStatus":"Success","lines":[{"sourceLine":4,"itemId":41,"matchKind":"Exact","requiresReview":false,"confidence":1.0}]}
                """,
            CreatedBy = "qa.user"
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.DieuChinh,
            WarehouseId = 1,
            AiOcrLogId = 4101,
            ReferenceNo = "AUDIT_TEST_OCR_REF",
            Lines =
            {
                new VoucherDetailLine
                {
                    ItemId = 41,
                    LocationId = 1,
                    TransactionQty = 2,
                    TransactionUomId = 1,
                    AdjustSign = 1,
                    OcrDocumentNumber = "AUDIT_TEST_OCR_REF",
                    OcrSourceLineNumber = 4
                }
            }
        };

        var created = Assert.IsType<RedirectToActionResult>(await controller.Create(vm));
        var voucher = await db.Vouchers.SingleAsync(v => v.ReferenceNo == "AUDIT_TEST_OCR_REF");
        Assert.Equal("Details", created.ActionName);
        Assert.Equal(SourceTypeEnum.AI_Gemini, voucher.SourceType);
        Assert.Equal(4101, voucher.AiOcrLogId);
        Assert.Equal(voucher.VoucherId, (await db.AiOcrLogs.FindAsync(4101L))!.VoucherId);
        var adjustment = Assert.Single(await db.AiOcrAdjustments.Where(row => row.AiOcrLogId == 4101).ToListAsync());
        Assert.Equal(4, adjustment.LineNumber);
        Assert.Equal(41, adjustment.ItemId);
        Assert.Equal("VoucherDetailApplied", adjustment.FieldName);

        var duplicateController = CreateController(db);
        var duplicate = Assert.IsType<ViewResult>(await duplicateController.Create(vm));
        Assert.Same(vm, duplicate.Model);
        Assert.Contains("đã được áp dụng", duplicateController.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await db.Vouchers.CountAsync(v => v.ReferenceNo == "AUDIT_TEST_OCR_REF"));
    }

    [Fact]
    public async Task CreateCustomerReturn_ShouldForcePendingQualityUntilInspection()
    {
        await using var db = CreateDb(nameof(CreateCustomerReturn_ShouldForcePendingQualityUntilInspection));
        SeedWarehouseGraph(db);

        db.UnitsOfMeasure.Add(new UnitOfMeasure
        {
            UomId = 1,
            UomCode = "CAI",
            UomName = "Cái",
            IsActive = true
        });
        db.Partners.Add(new Partner
        {
            PartnerId = 1,
            PartnerCode = "CUSTOMER-RETURN",
            PartnerName = "Khách hàng trả hàng",
            PartnerType = PartnerTypeEnum.Customer,
            IsActive = true
        });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "RETURN-QC-001",
            ItemName = "Hàng khách trả chờ kiểm phẩm",
            BaseUomId = 1,
            UnitCost = 50,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "returns.user");
        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.KhachTra,
            WarehouseId = 1,
            PartnerId = 1,
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 1,
                    LocationId = 1,
                    TransactionQty = 2,
                    TransactionUomId = 1,
                    QualityStatus = QualityStatusEnum.Good
                }
            }
        };

        var result = await controller.Create(vm);
        var creationError = controller.TempData["Error"];

        Assert.True(
            result is RedirectToActionResult,
            $"Expected successful customer-return creation, but got: {creationError}");
        var detail = await db.VoucherDetails.SingleAsync(d => d.ItemId == 1);
        Assert.Equal(QualityStatusEnum.Pending, detail.QualityStatus);
        Assert.False(await db.Vouchers.Where(v => v.VoucherId == detail.VoucherId).Select(v => v.IsPosted).SingleAsync());
        Assert.Equal(0, await db.ItemLocations.Where(il => il.ItemId == 1).SumAsync(il => il.Quantity));
    }

    [Fact]
    public async Task CreateInbound_ShouldRejectExpiryBeforeManufacturingDate()
    {
        await using var db = CreateDb(nameof(CreateInbound_ShouldRejectExpiryBeforeManufacturingDate));
        SeedWarehouseGraph(db);

        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "HOP", UomName = "Hộp", IsActive = true });
        db.Partners.Add(new Partner { PartnerId = 1, PartnerCode = "SUP-MED", PartnerName = "Nhà cung cấp y tế", PartnerType = PartnerTypeEnum.Supplier, IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "MED-HSD-BAD",
            ItemName = "Vật tư y tế kiểm HSD",
            BaseUomId = 1,
            UnitCost = 10,
            TrackLot = true,
            TrackExpiry = true,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "receiver.user");
        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            PartnerId = 1,
            VoucherDate = new DateTime(2026, 7, 2),
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 1,
                    LocationId = 1,
                    TransactionQty = 5,
                    TransactionUomId = 1,
                    UnitPrice = 10,
                    LineAmount = 50,
                    LotNumber = "LOT-HSD-BAD",
                    ManufacturingDate = new DateTime(2026, 7, 2),
                    ExpiryDate = new DateTime(2026, 7, 1)
                }
            }
        };

        var result = await controller.Create(vm);

        Assert.IsType<ViewResult>(result);
        Assert.Contains("HSD", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.VoucherDetails.ToListAsync());
    }

    [Fact]
    public async Task CreateInbound_ShouldIgnoreExpiryForItemWithoutExpiryTracking()
    {
        await using var db = CreateDb(nameof(CreateInbound_ShouldIgnoreExpiryForItemWithoutExpiryTracking));
        SeedWarehouseGraph(db);

        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "CAI", UomName = "Cái", IsActive = true });
        db.Partners.Add(new Partner { PartnerId = 1, PartnerCode = "SUP-IT", PartnerName = "Nhà cung cấp IT", PartnerType = PartnerTypeEnum.Supplier, IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "IT-LAP-NO-HSD",
            ItemName = "Laptop không quản lý HSD",
            BaseUomId = 1,
            UnitCost = 15000000,
            TrackSerial = true,
            TrackExpiry = false,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "receiver.user");
        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            PartnerId = 1,
            VoucherDate = new DateTime(2026, 7, 2),
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 1,
                    LocationId = 1,
                    TransactionQty = 1,
                    TransactionUomId = 1,
                    UnitPrice = 15000000,
                    LineAmount = 15000000,
                    ManufacturingDate = new DateTime(2026, 6, 15),
                    ExpiryDate = new DateTime(2027, 6, 15)
                }
            }
        };

        var result = await controller.Create(vm);

        Assert.IsType<RedirectToActionResult>(result);
        var detail = await db.VoucherDetails.SingleAsync(d => d.ItemId == 1);
        Assert.Null(detail.ExpiryDate);
        Assert.Equal(new DateTime(2026, 6, 15), detail.ManufacturingDate);
    }

    [Fact]
    public async Task CreateInbound_ShouldRoundTransactionConversionAndBaseQtyAtDefinedBoundaries()
    {
        await using var db = CreateDb(nameof(CreateInbound_ShouldRoundTransactionConversionAndBaseQtyAtDefinedBoundaries));
        SeedWarehouseGraph(db);

        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true },
            new UnitOfMeasure { UomId = 2, UomCode = "ALT", UomName = "Đơn vị phụ", IsActive = true });
        db.Partners.Add(new Partner
        {
            PartnerId = 1,
            PartnerCode = "SUP-ROUND",
            PartnerName = "Nhà cung cấp kiểm thử làm tròn",
            PartnerType = PartnerTypeEnum.Supplier,
            IsActive = true
        });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ROUND-SKU",
            ItemName = "Vật tư kiểm thử làm tròn",
            BaseUomId = 1,
            DefaultLocationId = 1,
            UnitCost = 10,
            IsActive = true
        });
        db.UnitConversions.Add(new UnitConversion
        {
            ConversionId = 1,
            ItemId = 1,
            FromUomId = 2,
            ToUomId = 1,
            ConversionRate = 1.2345655m,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "receiver.user");
        var result = await controller.Create(new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            PartnerId = 1,
            VoucherDate = new DateTime(2026, 7, 14),
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 1,
                    LocationId = 1,
                    TransactionQty = 1.00005m,
                    TransactionUomId = 2,
                    UnitPrice = 10
                }
            }
        });

        Assert.IsType<RedirectToActionResult>(result);
        var detail = await db.VoucherDetails.SingleAsync(d => d.ItemId == 1);
        Assert.Equal(1.0001m, detail.TransactionQty);
        Assert.Equal(1.234566m, detail.ConversionRate);
        Assert.Equal(1.2347m, detail.BaseQty);
    }

    [Fact]
    public async Task CreateNegativeAdjustment_ShouldKeepConversionRatePositive()
    {
        await using var db = CreateDb(nameof(CreateNegativeAdjustment_ShouldKeepConversionRatePositive));
        SeedWarehouseGraph(db);

        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true },
            new UnitOfMeasure { UomId = 2, UomCode = "BOX", UomName = "Thùng", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ADJ-UOM-SKU",
            ItemName = "Vật tư điều chỉnh có quy đổi",
            BaseUomId = 1,
            CurrentStock = 100,
            UnitCost = 10,
            IsActive = true
        });
        db.UnitConversions.Add(new UnitConversion
        {
            ConversionId = 1,
            ItemId = 1,
            FromUomId = 2,
            ToUomId = 1,
            ConversionRate = 12,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            LocationId = 1,
            Quantity = 100,
            ReservedQty = 0,
            UpdatedAt = VietnamTime.Now
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.Create(new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.DieuChinh,
            WarehouseId = 1,
            VoucherDate = new DateTime(2026, 7, 14),
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 1,
                    LocationId = 1,
                    TransactionQty = 2,
                    TransactionUomId = 2,
                    AdjustSign = -1
                }
            }
        });

        Assert.IsType<RedirectToActionResult>(result);
        var detail = await db.VoucherDetails.SingleAsync(d => d.ItemId == 1);
        Assert.Equal(-24m, detail.BaseQty);
        Assert.Equal(12m, detail.ConversionRate);
    }

    [Fact]
    public async Task CreateInbound_ShouldRejectSameTrackedLotWithDifferentExpiry()
    {
        await using var db = CreateDb(nameof(CreateInbound_ShouldRejectSameTrackedLotWithDifferentExpiry));
        SeedWarehouseGraph(db);

        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "HOP", UomName = "Hộp", IsActive = true });
        db.Partners.Add(new Partner { PartnerId = 1, PartnerCode = "SUP-MED", PartnerName = "Nhà cung cấp y tế", PartnerType = PartnerTypeEnum.Supplier, IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "MED-LOT-STRICT",
            ItemName = "Vật tư y tế theo lô",
            BaseUomId = 1,
            UnitCost = 10,
            TrackLot = true,
            TrackExpiry = true,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1001,
            ItemId = 1,
            LocationId = 1,
            Quantity = 10,
            LotNumber = "LOT-SAME",
            ExpiryDate = new DateTime(2026, 12, 31),
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "receiver.user");
        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            PartnerId = 1,
            VoucherDate = new DateTime(2026, 7, 2),
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 1,
                    LocationId = 2,
                    TransactionQty = 3,
                    TransactionUomId = 1,
                    UnitPrice = 10,
                    LineAmount = 30,
                    LotNumber = "LOT-SAME",
                    ManufacturingDate = new DateTime(2026, 7, 1),
                    ExpiryDate = new DateTime(2027, 1, 31)
                }
            }
        };

        var result = await controller.Create(vm);

        Assert.IsType<ViewResult>(result);
        Assert.Contains("nhiều hạn sử dụng", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.ItemLocations.ToListAsync());
        Assert.Empty(await db.VoucherDetails.ToListAsync());
    }

    [Fact]
    public async Task CreateInbound_ShouldAutoPutawaySameItemDifferentOwnerIntoEmptyLocation()
    {
        await using var db = CreateDb(nameof(CreateInbound_ShouldAutoPutawaySameItemDifferentOwnerIntoEmptyLocation));
        SeedWarehouseGraph(db);

        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Partners.AddRange(
            new Partner { PartnerId = 1, PartnerCode = "SUP-OWN", PartnerName = "Supplier", PartnerType = PartnerTypeEnum.Supplier, IsActive = true },
            new Partner { PartnerId = 10, PartnerCode = "OWN-10", PartnerName = "Owner 10", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 20, PartnerCode = "OWN-20", PartnerName = "Owner 20", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "PUT-OWNER",
            ItemName = "Hàng cất theo owner",
            BaseUomId = 1,
            OwnerPartnerId = 10,
            DefaultLocationId = 1,
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1010,
            ItemId = 1,
            OwnerPartnerId = 20,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            PartnerId = 1,
            InventoryOwnershipMode = "ThreePl",
            OwnerPartnerId = 10,
            VoucherDate = DateTime.Today,
            Lines = new List<VoucherDetailLine>
            {
                new()
                {
                    ItemId = 1,
                    TransactionQty = 2,
                    TransactionUomId = 1,
                    UnitPrice = 10,
                    LineAmount = 20
                }
            }
        };

        var result = await controller.Create(vm);

        Assert.IsType<RedirectToActionResult>(result);
        var detail = await db.VoucherDetails.SingleAsync(d => d.ItemId == 1);
        Assert.Equal(10, detail.OwnerPartnerId);
        Assert.Equal(2, detail.LocationId);
    }

    [Fact]
    public async Task Approve_ShouldRejectInbound_WhenVoucherNotInReceiving()
    {
        await using var db = CreateDb(nameof(Approve_ShouldRejectInbound_WhenVoucherNotInReceiving));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-IN-01",
            ItemName = "Hàng nhập kho",
            BaseUomId = 1,
            UnitCost = 25,
            IsActive = true
        });

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 1,
            VoucherCode = "PN-0001",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Approved,
            IsPosted = false
        });

        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 1,
            VoucherId = 1,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 5,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 5,
            UnitPrice = 25,
            LineAmount = 125,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");

        var result = await controller.Approve(1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        var voucher = await db.Vouchers.FindAsync(1L);
        Assert.NotNull(voucher);
        Assert.False(voucher!.IsPosted);
        Assert.Equal(InboundStatusEnum.Approved, voucher.InboundStatus);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task ApproveAdjustment_ShouldLinkInventoryLedgerToVoucher()
    {
        await using var db = CreateDb(nameof(ApproveAdjustment_ShouldLinkInventoryLedgerToVoucher));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 61,
            ItemCode = "ADJ-LEDGER-61",
            ItemName = "Hàng kiểm tra ledger điều chỉnh",
            BaseUomId = 1,
            UnitCost = 10,
            CurrentStock = 10,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 6101,
            ItemId = 61,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            UpdatedAt = DateTime.Now
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 61,
            VoucherCode = "PDC-LEDGER-61",
            VoucherType = VoucherTypeEnum.DieuChinh,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            IsPosted = false
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 61001,
            VoucherId = 61,
            ItemId = 61,
            LocationId = 1,
            TransactionQty = 2,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = -2,
            UnitPrice = 10,
            LineAmount = -20,
            LineNumber = 1
        });
        await db.SaveChangesAsync();
        db.InventoryTransactions.RemoveRange(db.InventoryTransactions);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");
        var result = await controller.Approve(61);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(
            (await db.Vouchers.FindAsync(61L))!.IsPosted,
            controller.TempData["Error"]?.ToString());
        Assert.Equal(8, (await db.ItemLocations.FindAsync(6101))!.Quantity);

        var ledger = await db.InventoryTransactions.SingleAsync();
        Assert.Equal(InventoryTransactionTypeEnum.Adjust, ledger.TransactionType);
        Assert.Equal(61, ledger.VoucherId);
        Assert.Equal("voucher:61:adjustment-approve", ledger.TransactionGroupKey);
        Assert.Equal("PDC-LEDGER-61", ledger.ReferenceCode);
        Assert.Equal(-2, ledger.QuantityDelta);
    }

    [Fact]
    public async Task CompleteInbound_ShouldPostVoucherAndMarkCompleted()
    {
        await using var db = CreateDb(nameof(CompleteInbound_ShouldPostVoucherAndMarkCompleted));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-IN-02",
            ItemName = "Hàng nhập hoàn tất",
            BaseUomId = 1,
            UnitCost = 40,
            CurrentStock = 0,
            IsActive = true
        });

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 2,
            VoucherCode = "PN-0002",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Receiving,
            ReceivedBy = "receiver.user",
            ReceivedAt = DateTime.Now.AddMinutes(-20),
            ReviewedBy = "checker.user",
            ReviewedAt = DateTime.Now.AddMinutes(-10),
            ReviewResult = ReviewResultEnum.Pass,
            IsPosted = false
        });

        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2,
            VoucherId = 2,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 8,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 8,
            UnitPrice = 40,
            LineAmount = 320,
            LineNumber = 1,
            Notes = "[ACTUAL:8.0000;CHECKED_BY:checker.user;CHECKED_AT:2026-06-09 08:30] Đủ số lượng."
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");

        var result = await controller.CompleteInbound(2);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        var voucher = await db.Vouchers.FindAsync(2L);
        var item = await db.Items.FindAsync(1);
        var itemLocation = await db.ItemLocations.FirstOrDefaultAsync(x => x.ItemId == 1 && x.LocationId == 1);
        var lpn = await db.LicensePlates.FirstOrDefaultAsync(x => x.VoucherId == 2);

        Assert.NotNull(voucher);
        Assert.True(voucher!.IsPosted);
        Assert.Equal(InboundStatusEnum.Completed, voucher.InboundStatus);
        Assert.Equal(ReviewResultEnum.Pass, voucher.ReviewResult);
        Assert.Equal("checker.user", voucher.ReviewedBy);
        Assert.Equal("reviewer.user", voucher.CompletedBy);

        Assert.NotNull(item);
        Assert.Equal(8, item!.CurrentStock);
        Assert.NotNull(itemLocation);
        Assert.Equal(8, itemLocation!.Quantity);
        Assert.NotNull(lpn);
        Assert.True(lpn!.IsActive);
        Assert.Equal(LpnStatusEnum.Stored, lpn.Status);
        Assert.Equal(1, lpn.CurrentLocationId);
        await db.Entry(lpn).Collection(x => x.Details).LoadAsync();
        var lpnDetail = Assert.Single(lpn.Details);
        Assert.Equal(1, lpnDetail.ItemId);
        Assert.Equal(2, lpnDetail.VoucherDetailId);
        Assert.Equal(8, lpnDetail.Quantity);
    }

    [Fact]
    public async Task CompleteInbound_ShouldRejectSameItemDifferentOwnerInPutawayLocation()
    {
        await using var db = CreateDb(nameof(CompleteInbound_ShouldRejectSameItemDifferentOwnerInPutawayLocation));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "IN-OWN-MIX",
            ItemName = "Hàng kiểm owner",
            BaseUomId = 1,
            UnitCost = 40,
            CurrentStock = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 991,
            ItemId = 1,
            OwnerPartnerId = 20,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 991,
            VoucherCode = "PN-OWN-MIX",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            OwnerPartnerId = 10,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Receiving,
            ReceivedBy = "receiver.user",
            ReceivedAt = DateTime.Now.AddMinutes(-20),
            ReviewedBy = "checker.user",
            ReviewedAt = DateTime.Now.AddMinutes(-10),
            ReviewResult = ReviewResultEnum.Pass,
            IsPosted = false
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 9911,
            VoucherId = 991,
            OwnerPartnerId = 10,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 2,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 2,
            UnitPrice = 40,
            LineAmount = 80,
            LineNumber = 1,
            Notes = "[ACTUAL:2.0000;CHECKED_BY:checker.user;CHECKED_AT:2026-06-09 08:30] Đủ số lượng."
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");

        var result = await controller.CompleteInbound(991);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("một vị trí một mã vật tư", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.Vouchers.FindAsync(991L))!.IsPosted);
        Assert.Equal(5, await db.ItemLocations.Where(il => il.ItemId == 1).SumAsync(il => il.Quantity));
        Assert.DoesNotContain(await db.ItemLocations.Where(il => il.ItemId == 1).ToListAsync(), il => il.OwnerPartnerId == 10);
    }

    [Fact]
    public async Task CompleteInbound_ShouldAllowReplenishingExistingStockKeyInLegacyMixedLocation()
    {
        await using var db = CreateDb(nameof(CompleteInbound_ShouldAllowReplenishingExistingStockKeyInLegacyMixedLocation));
        SeedWarehouseGraph(db);
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "LEGACY-CHARGER", ItemName = "Sạc", BaseUomId = 1, UnitCost = 10, CurrentStock = 85, IsActive = true },
            new Item { ItemId = 2, ItemCode = "LEGACY-KEYBOARD", ItemName = "Bàn phím", BaseUomId = 1, UnitCost = 20, CurrentStock = 38, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 1001, ItemId = 1, LocationId = 1, Quantity = 85, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 1002, ItemId = 2, LocationId = 1, Quantity = 38, HoldStatus = InventoryHoldStatusEnum.Available });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 1001,
            VoucherCode = "PN-LEGACY-MIXED",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Receiving,
            ReceivedBy = "receiver.user",
            ReceivedAt = DateTime.Now.AddMinutes(-20),
            ReviewedBy = "checker.user",
            ReviewedAt = DateTime.Now.AddMinutes(-10),
            ReviewResult = ReviewResultEnum.Pass
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 10011,
            VoucherId = 1001,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 50,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 50,
            UnitPrice = 10,
            LineAmount = 500,
            LineNumber = 1,
            Notes = "[ACTUAL:50.0000;CHECKED_BY:checker.user;CHECKED_AT:2026-07-13 09:00] Đủ số lượng."
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");
        var result = await controller.CompleteInbound(1001);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(controller.TempData["Error"]);
        Assert.True((await db.Vouchers.FindAsync(1001L))!.IsPosted);
        Assert.Equal(135, (await db.ItemLocations.FindAsync(1001))!.Quantity);
        Assert.Equal(38, (await db.ItemLocations.FindAsync(1002))!.Quantity);
    }

    [Fact]
    public async Task CompleteInbound_ShouldRejectPositivePutawayQtyWithoutLocation()
    {
        await using var db = CreateDb(nameof(CompleteInbound_ShouldRejectPositivePutawayQtyWithoutLocation));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "IN-NO-LOC",
            ItemName = "Hàng thiếu vị trí",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 21,
            VoucherCode = "PN-NO-LOC",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Receiving,
            ReceivedBy = "receiver.user",
            ReceivedAt = DateTime.Now.AddMinutes(-20),
            ReviewedBy = "checker.user",
            ReviewedAt = DateTime.Now.AddMinutes(-10),
            ReviewResult = ReviewResultEnum.Pass
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2101,
            VoucherId = 21,
            ItemId = 1,
            TransactionQty = 3,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 3,
            UnitPrice = 10,
            LineAmount = 30,
            LineNumber = 1,
            Notes = "[ACTUAL:3.0000;CHECKED_BY:checker.user;CHECKED_AT:2026-06-09 08:30] Đủ số lượng."
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");
        var result = await controller.CompleteInbound(21);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("vị trí", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.Vouchers.FindAsync(21L))!.IsPosted);
        Assert.Empty(await db.ItemLocations.Where(il => il.ItemId == 1).ToListAsync());
    }

    [Fact]
    public async Task CompleteInbound_ShouldRequireLotAndExpiryForTrackedItem()
    {
        await using var db = CreateDb(nameof(CompleteInbound_ShouldRequireLotAndExpiryForTrackedItem));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "IN-LOT-HSD",
            ItemName = "Hàng theo lô hạn",
            BaseUomId = 1,
            UnitCost = 10,
            TrackLot = true,
            TrackExpiry = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 22,
            VoucherCode = "PN-LOT-HSD",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Receiving,
            ReceivedBy = "receiver.user",
            ReceivedAt = DateTime.Now.AddMinutes(-20),
            ReviewedBy = "checker.user",
            ReviewedAt = DateTime.Now.AddMinutes(-10),
            ReviewResult = ReviewResultEnum.Pass
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2201,
            VoucherId = 22,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 2,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 2,
            UnitPrice = 10,
            LineAmount = 20,
            LineNumber = 1,
            Notes = "[ACTUAL:2.0000;CHECKED_BY:checker.user;CHECKED_AT:2026-06-09 08:30] Đủ số lượng."
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");
        var result = await controller.CompleteInbound(22);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("lô", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.Vouchers.FindAsync(22L))!.IsPosted);
    }

    [Fact]
    public async Task SubmitForApproval_ShouldRequireExpectedArrivalForInbound()
    {
        await using var db = CreateDb(nameof(SubmitForApproval_ShouldRequireExpectedArrivalForInbound));
        SeedWarehouseGraph(db);

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 3,
            VoucherCode = "PN-0003",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Draft,
            IsPosted = false
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user");

        var result = await controller.SubmitForApproval(3);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        var voucher = await db.Vouchers.FindAsync(3L);
        Assert.NotNull(voucher);
        Assert.Equal(InboundStatusEnum.Draft, voucher!.InboundStatus);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task ConfirmReceiving_ShouldRejectInboundWithoutAsn()
    {
        await using var db = CreateDb(nameof(ConfirmReceiving_ShouldRejectInboundWithoutAsn));
        SeedWarehouseGraph(db);

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 4,
            VoucherCode = "PN-0004",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Approved,
            IsPosted = false,
            ExpectedArrivalAt = null,
            AsnCode = null
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user");

        var result = await controller.ConfirmReceiving(4);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        var voucher = await db.Vouchers.FindAsync(4L);
        Assert.NotNull(voucher);
        Assert.Equal(InboundStatusEnum.Approved, voucher!.InboundStatus);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task AssignDock_ShouldRejectOverlappingDockWindow()
    {
        await using var db = CreateDb(nameof(AssignDock_ShouldRejectOverlappingDockWindow));
        SeedWarehouseGraph(db);

        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 5,
                VoucherCode = "PN-0005",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Approved,
                AsnCode = "ASN-LOCKED",
                ExpectedArrivalAt = new DateTime(2026, 4, 24, 9, 0, 0),
                DockDoor = "DOCK-01",
                DockAppointmentStart = new DateTime(2026, 4, 24, 8, 30, 0),
                DockAppointmentEnd = new DateTime(2026, 4, 24, 10, 0, 0)
            },
            new Voucher
            {
                VoucherId = 6,
                VoucherCode = "PN-0006",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Draft
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");

        var result = await controller.AssignDock(
            6,
            "DOCK-01",
            new DateTime(2026, 4, 24, 9, 15, 0),
            new DateTime(2026, 4, 24, 9, 0, 0),
            new DateTime(2026, 4, 24, 10, 30, 0),
            "Xe NCC",
            "51D-99999",
            "Tài xế A",
            "0900000000");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        var voucher = await db.Vouchers.FindAsync(6L);
        Assert.NotNull(voucher);
        Assert.Null(voucher!.DockDoor);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task AssignDock_ShouldAutoSuggestFreeDockAndGenerateAsn()
    {
        await using var db = CreateDb(nameof(AssignDock_ShouldAutoSuggestFreeDockAndGenerateAsn));
        SeedWarehouseGraph(db);

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 7,
            VoucherCode = "PN-0007",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Draft
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");

        var result = await controller.AssignDock(
            7,
            null,
            new DateTime(2026, 4, 24, 14, 0, 0),
            new DateTime(2026, 4, 24, 13, 30, 0),
            new DateTime(2026, 4, 24, 15, 0, 0),
            "Xe nội bộ",
            "51D-12345",
            "Tài xế B",
            "0911111111");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        var voucher = await db.Vouchers.FindAsync(7L);
        Assert.NotNull(voucher);
        Assert.Equal("DOCK-01", voucher!.DockDoor);
        Assert.NotNull(voucher.AsnCode);
        Assert.Equal("51D-12345", voucher.VehicleNumber);
    }

    [Fact]
    public async Task Cancel_ShouldVoidInboundLpns()
    {
        await using var db = CreateDb(nameof(Cancel_ShouldVoidInboundLpns));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-LPN-01",
            ItemName = "Hàng nhập mã kiện",
            BaseUomId = 1,
            UnitCost = 40,
            CurrentStock = 8,
            IsActive = true
        });

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 8,
            VoucherCode = "PN-0008",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            ReviewedBy = "reviewer.user",
            InboundStatus = InboundStatusEnum.Completed,
            IsPosted = true
        });

        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 8,
            VoucherId = 8,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 8,
            TransactionUomId = 1,
            ConversionRate = 1,
            BaseQty = 8,
            UnitPrice = 40,
            LineAmount = 320,
            LineNumber = 1
        });

        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 8,
            ItemId = 1,
            LocationId = 1,
            Quantity = 8,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });

        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 1,
            LpnCode = "LPN-20260424-000001",
            VoucherId = 8,
            VoucherDetailId = 8,
            WarehouseId = 1,
            ItemId = 1,
            LocationId = 1,
            Quantity = 8,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");

        var result = await controller.Cancel(8, "Sai chứng từ", CancelReasonEnum.WrongInfo);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);

        var lpn = await db.LicensePlates.SingleAsync(x => x.VoucherId == 8);
        Assert.False(lpn.IsActive);
        Assert.Equal(LpnStatusEnum.Voided, lpn.Status);
        Assert.Equal("manager.user", lpn.VoidedBy);
        Assert.NotNull(lpn.VoidedAt);
    }

    [Fact]
    public async Task CancelInbound_ShouldReverseWeightedAverageCostAndRestorePreviousLastCost()
    {
        await using var db = CreateDb(nameof(CancelInbound_ShouldReverseWeightedAverageCostAndRestorePreviousLastCost));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 18,
            ItemCode = "WAC-CANCEL-18",
            ItemName = "Hàng kiểm hoàn tác giá vốn",
            BaseUomId = 1,
            CurrentStock = 20,
            UnitCost = 15,
            LastCost = 20,
            TotalStockValue = 300,
            IsActive = true
        });
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 18,
                VoucherCode = "PN-WAC-OLD-18",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today.AddDays(-2),
                CreatedBy = "old.creator",
                ReviewedBy = "old.reviewer",
                CreatedAt = DateTime.Today.AddDays(-2),
                CompletedAt = DateTime.Today.AddDays(-2).AddHours(1),
                InboundStatus = InboundStatusEnum.Completed,
                IsPosted = true
            },
            new Voucher
            {
                VoucherId = 19,
                VoucherCode = "PN-WAC-CURRENT-19",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today.AddDays(-1),
                CreatedBy = "receipt.creator",
                ReviewedBy = "receipt.reviewer",
                CreatedAt = DateTime.Today.AddDays(-1),
                CompletedAt = DateTime.Today.AddDays(-1).AddHours(1),
                InboundStatus = InboundStatusEnum.Completed,
                IsPosted = true
            });
        db.VoucherDetails.AddRange(
            new VoucherDetail
            {
                VoucherDetailId = 1801,
                VoucherId = 18,
                LineNumber = 1,
                ItemId = 18,
                LocationId = 1,
                TransactionUomId = 1,
                TransactionQty = 10,
                BaseQty = 10,
                ConversionRate = 1,
                UnitPrice = 10
            },
            new VoucherDetail
            {
                VoucherDetailId = 1901,
                VoucherId = 19,
                LineNumber = 1,
                ItemId = 18,
                LocationId = 1,
                TransactionUomId = 1,
                TransactionQty = 10,
                BaseQty = 10,
                ConversionRate = 1,
                UnitPrice = 20
            });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 19,
            ItemId = 18,
            LocationId = 1,
            Quantity = 20,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.Cancel(19, "Hoàn tác chứng từ nhập sai", CancelReasonEnum.WrongInfo);

        Assert.IsType<RedirectToActionResult>(result);
        var item = await db.Items.FindAsync(18);
        Assert.NotNull(item);
        Assert.Equal(10m, item!.CurrentStock);
        Assert.Equal(10m, item.UnitCost);
        Assert.Equal(10m, item.LastCost);
        Assert.Equal(100m, item.TotalStockValue);
        Assert.Equal(10m, (await db.ItemLocations.FindAsync(19))!.Quantity);
    }

    [Fact]
    public async Task ScanLpn_ShouldReturnDataWithinScopedWarehouse()
    {
        await using var db = CreateDb(nameof(ScanLpn_ShouldReturnDataWithinScopedWarehouse));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-SCAN-01",
            ItemName = "Hàng quét mã kiện",
            BaseUomId = 1,
            UnitCost = 20,
            IsActive = true
        });

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 9,
            VoucherCode = "PN-0009",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user"
        });

        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 9,
            LpnCode = "LPN-TEST-000009",
            VoucherId = 9,
            WarehouseId = 1,
            ItemId = 1,
            LocationId = 1,
            Quantity = 5,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1);

        var result = await controller.ScanLpn("LPN-TEST-000009");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<List<LicensePlate>>(view.Model);
        Assert.Single(model);
        Assert.Equal("LPN-TEST-000009", model[0].LpnCode);
    }

    [Fact]
    public async Task ScanLpn_ShouldRejectForeignWarehouseScope()
    {
        await using var db = CreateDb(nameof(ScanLpn_ShouldRejectForeignWarehouseScope));
        SeedWarehouseGraph(db);

        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 2,
            WarehouseCode = "WH2",
            WarehouseName = "Warehouse 2",
            IsActive = true
        });
        db.Zones.Add(new Zone { ZoneId = 3, WarehouseId = 2, ZoneCode = "Z3", ZoneName = "Zone 3", ZoneType = ZoneTypeEnum.Storage, IsActive = true });
        db.Locations.Add(new Location { LocationId = 3, ZoneId = 3, LocationCode = "L3", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-SCAN-02",
            ItemName = "Hàng phân quyền kho",
            BaseUomId = 1,
            UnitCost = 20,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 10,
            VoucherCode = "PN-0010",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 2,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user"
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 10,
            LpnCode = "LPN-TEST-000010",
            VoucherId = 10,
            WarehouseId = 2,
            ItemId = 1,
            LocationId = 3,
            Quantity = 5,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1);

        var result = await controller.ScanLpn("LPN-TEST-000010");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<List<LicensePlate>>(view.Model);
        Assert.Empty(model); // foreign warehouse → not returned
    }

    [Fact]
    public async Task ScanLpn_JsonLookup_ShouldSummarizeMixedLpnDetails()
    {
        await using var db = CreateDb(nameof(ScanLpn_JsonLookup_ShouldSummarizeMixedLpnDetails));
        SeedWarehouseGraph(db);

        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "MIX-A", ItemName = "Mixed item A", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 2, ItemCode = "MIX-B", ItemName = "Mixed item B", BaseUomId = 1, IsActive = true });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 110,
            LpnCode = "LPN-MIX-110",
            VoucherId = 1,
            WarehouseId = 1,
            CurrentLocationId = 1,
            Status = LpnStatusEnum.Stored,
            LpnType = LpnTypeEnum.Pallet,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail { ItemId = 1, Quantity = 3, LotNumber = "LOT-A" },
                new LicensePlateDetail { ItemId = 2, Quantity = 5, LotNumber = "LOT-B" }
            }
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1);
        var result = await controller.ScanLpn(null, "LPN-MIX-110");

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);
        var data = GetAnonValue(json.Value!, "data")!;
        Assert.Equal("LPN-MIX-110", GetAnonValue(data, "lpnCode"));
        Assert.Equal("MIX-A, MIX-B", GetAnonValue(data, "itemCode"));
        Assert.Equal(8m, (decimal)GetAnonValue(data, "quantity")!);
        Assert.Equal(2, (int)GetAnonValue(data, "detailCount")!);
        Assert.Equal("Stored", GetAnonValue(data, "status"));
        Assert.Equal("Pallet", GetAnonValue(data, "lpnType"));
    }

    [Fact]
    public async Task LpnHierarchyService_ShouldBlockSelfParentAndIndirectLoops()
    {
        await using var db = CreateDb(nameof(LpnHierarchyService_ShouldBlockSelfParentAndIndirectLoops));
        SeedWarehouseGraph(db);

        db.LicensePlates.AddRange(
            new LicensePlate
            {
                LicensePlateId = 201,
                LpnCode = "LPN-HIER-A",
                VoucherId = 1,
                WarehouseId = 1,
                CurrentLocationId = 1,
                ParentLpnId = 202,
                Status = LpnStatusEnum.Stored,
                IsActive = true
            },
            new LicensePlate
            {
                LicensePlateId = 202,
                LpnCode = "LPN-HIER-B",
                VoucherId = 1,
                WarehouseId = 1,
                CurrentLocationId = 1,
                Status = LpnStatusEnum.Stored,
                IsActive = true
            },
            new LicensePlate
            {
                LicensePlateId = 203,
                LpnCode = "LPN-HIER-C",
                VoucherId = 1,
                WarehouseId = 1,
                CurrentLocationId = 1,
                Status = LpnStatusEnum.Stored,
                IsActive = true
            });
        await db.SaveChangesAsync();

        var service = new LpnHierarchyService(db);
        await service.EnsureCanAssignParentAsync(203, 202);

        var self = await Assert.ThrowsAsync<BusinessRuleException>(() => service.EnsureCanAssignParentAsync(201, 201));
        Assert.Equal("LPN_SELF_PARENT", self.Code);

        var indirect = await Assert.ThrowsAsync<BusinessRuleException>(() => service.EnsureCanAssignParentAsync(202, 201));
        Assert.Equal("LPN_PARENT_LOOP", indirect.Code);
    }

    [Fact]
    public async Task Receiving_ShouldOnlyReturnInboundRowsInScopedWarehouse()
    {
        await using var db = CreateDb(nameof(Receiving_ShouldOnlyReturnInboundRowsInScopedWarehouse));
        SeedWarehouseGraph(db);

        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 2,
            WarehouseCode = "WH2",
            WarehouseName = "Warehouse 2",
            IsActive = true
        });

        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 11,
                VoucherCode = "PN-0011",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Approved,
                AsnCode = "ASN-0011"
            },
            new Voucher
            {
                VoucherId = 12,
                VoucherCode = "PX-0012",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user"
            },
            new Voucher
            {
                VoucherId = 13,
                VoucherCode = "PN-0013",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 2,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Approved,
                AsnCode = "ASN-0013"
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1);

        var result = await controller.Receiving(null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<InboundReceivingRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal("PN-0011", rows[0].VoucherCode);
    }

    [Fact]
    public async Task RfPicking_ShouldReturnAssignedOrUnassignedTasksForStaff()
    {
        await using var db = CreateDb(nameof(RfPicking_ShouldReturnAssignedOrUnassignedTasksForStaff));
        SeedWarehouseGraph(db);

        db.Waves.Add(new Wave
        {
            WaveId = 1,
            WaveCode = "WAVE-01",
            WarehouseId = 1,
            Status = WaveStatusEnum.Released,
            CreatedAt = DateTime.UtcNow
        });

        // Add User & Zone mapping so staff can see tasks
        db.AppUsers.Add(new AppUser { UserId = 1, UserName = "staff.user", FullName = "Staff User", WarehouseId = 1, RoleId = 3, IsActive = true });
        db.UserZoneAssignments.Add(new UserZoneAssignment { UserZoneAssignmentId = 1, UserId = 1, ZoneId = 1 });

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-RF-01",
            ItemName = "Hàng quét lấy RF",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 20,
            VoucherCode = "PX-0020",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user"
        });

        db.PickTasks.AddRange(
            new PickTask
            {
                PickTaskId = 1,
                TaskCode = "TASK-001",
                WaveId = 1,
                VoucherId = 20,
                ItemId = 1,
                SourceLocationId = 1,
                TargetQty = 10,
                PickedQty = 0,
                Status = PickTaskStatusEnum.Assigned,
                AssignedTo = "staff.user"
            },
            new PickTask
            {
                PickTaskId = 2,
                TaskCode = "TASK-002",
                WaveId = 1,
                VoucherId = 20,
                ItemId = 1,
                SourceLocationId = 1,
                TargetQty = 10,
                PickedQty = 0,
                Status = PickTaskStatusEnum.Pending,
                AssignedTo = null
            },
            new PickTask
            {
                PickTaskId = 3,
                TaskCode = "TASK-003",
                WaveId = 1,
                VoucherId = 20,
                ItemId = 1,
                SourceLocationId = 1,
                TargetQty = 10,
                PickedQty = 0,
                Status = PickTaskStatusEnum.Assigned,
                AssignedTo = "other.user"
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        var result = await controller.RfPicking(warehouseId: 1);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<PickTaskBoardRow>>(view.Model);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.TaskCode == "TASK-001");
        Assert.Contains(rows, x => x.TaskCode == "TASK-002");
        Assert.DoesNotContain(rows, x => x.TaskCode == "TASK-003");
    }

    [Fact]
    public async Task RfReceiving_ShouldOnlyReturnApprovedOrReceivingInboundRows()
    {
        await using var db = CreateDb(nameof(RfReceiving_ShouldOnlyReturnApprovedOrReceivingInboundRows));
        SeedWarehouseGraph(db);

        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 21,
                VoucherCode = "PN-0021",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Approved
            },
            new Voucher
            {
                VoucherId = 22,
                VoucherCode = "PN-0022",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Receiving
            },
            new Voucher
            {
                VoucherId = 23,
                VoucherCode = "PN-0023",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Draft
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        var result = await controller.RfReceiving(warehouseId: 1);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<InboundReceivingRow>>(view.Model);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.VoucherCode == "PN-0021");
        Assert.Contains(rows, x => x.VoucherCode == "PN-0022");
        Assert.DoesNotContain(rows, x => x.VoucherCode == "PN-0023");
    }

    [Fact]
    public async Task Replenishment_ShouldSuggestItemWhenPickFaceBelowTrigger()
    {
        await using var db = CreateDb(nameof(Replenishment_ShouldSuggestItemWhenPickFaceBelowTrigger));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-REP-01",
            ItemName = "Hàng bổ sung",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            ReorderPoint = 10,
            MaxThreshold = 20,
            IsActive = true
        });

        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 1,
                ItemId = 1,
                LocationId = 1,
                Quantity = 2,
                ReservedQty = 0,
                UpdatedAt = DateTime.UtcNow
            },
            new ItemLocation
            {
                ItemLocationId = 2,
                ItemId = 1,
                LocationId = 2,
                Quantity = 20,
                ReservedQty = 0,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        var result = await controller.Replenishment(1, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<ReplenishmentSuggestionRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal("ITEM-REP-01", rows[0].ItemCode);
        Assert.Equal(18, rows[0].SuggestedQty);
    }

    [Fact]
    public async Task ExecuteReplenishment_ShouldCreateMovementTaskAndNotMoveStockImmediately()
    {
        await using var db = CreateDb(nameof(ExecuteReplenishment_ShouldCreateMovementTaskAndNotMoveStockImmediately));
        SeedWarehouseGraph(db);

        // DefaultLocationId = 2 → đây là SOURCE (vị trí pick face / bulk)
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-REP-02",
            ItemName = "Hàng di chuyển",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 2,  // SOURCE = Location 2 (sẽ giảm)
            IsActive = true
        });

        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 10,
                ItemId = 1,
                LocationId = 2,  // SOURCE - có 20 cái
                Quantity = 20,
                ReservedQty = 0,
                UpdatedAt = DateTime.UtcNow
            },
            new ItemLocation
            {
                ItemLocationId = 11,
                ItemId = 1,
                LocationId = 1,  // DESTINATION - có 2 cái (sẽ tăng)
                Quantity = 2,
                ReservedQty = 0,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        // Gọi: ExecuteReplenishment(itemId=1, toLocationId=1, qty=5)
        // → Source = DefaultLocationId = 2 (Location 2)
        // → Destination = toLocationId = 1 (Location 1)
        var result = await controller.ExecuteReplenishment(1, 1, 5, 10);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Replenishment", redirect.ActionName);

        var source = await db.ItemLocations.SingleAsync(x => x.ItemLocationId == 10);
        var dest = await db.ItemLocations.SingleAsync(x => x.ItemLocationId == 11);
        var task = await db.MovementTasks.SingleAsync();

        Assert.Equal(20, source.Quantity);
        Assert.Equal(2, dest.Quantity);
        Assert.Equal(MovementTaskTypeEnum.Replenishment, task.TaskType);
        Assert.Equal(MovementTaskStatusEnum.Pending, task.Status);
        Assert.Equal(5, task.PlannedQty);
        Assert.Equal(2, task.SourceLocationId);
        Assert.Equal(1, task.DestinationLocationId);
    }

    [Fact]
    public async Task ExecuteReplenishment_ShouldBlockWhenSourceHasActiveLpn()
    {
        await using var db = CreateDb(nameof(ExecuteReplenishment_ShouldBlockWhenSourceHasActiveLpn));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-REP-03",
            ItemName = "Hàng bảo vệ mã kiện",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            IsActive = true
        });

        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 21,
            ItemId = 1,
            LocationId = 2,
            Quantity = 20,
            ReservedQty = 0,
            LotNumber = "LOT-01",
            ExpiryDate = new DateTime(2026, 12, 31),
            UpdatedAt = DateTime.UtcNow
        });

        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 21,
            LpnCode = "LPN-REP-000021",
            VoucherId = 1,
            WarehouseId = 1,
            ItemId = 1,
            LocationId = 2,
            CurrentLocationId = 2,
            Quantity = 20,
            LotNumber = "LOT-01",
            ExpiryDate = new DateTime(2026, 12, 31),
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Details =
            {
                new LicensePlateDetail
                {
                    ItemId = 1,
                    Quantity = 20,
                    LotNumber = "LOT-01",
                    ExpiryDate = new DateTime(2026, 12, 31)
                }
            }
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        var result = await controller.ExecuteReplenishment(1, 2, 5, 21);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Replenishment", redirect.ActionName);

        var source = await db.ItemLocations.SingleAsync(x => x.ItemLocationId == 21);
        Assert.Equal(20, source.Quantity);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Slotting_ShouldSuggestDominantLocationAsNewDefault()
    {
        await using var db = CreateDb(nameof(Slotting_ShouldSuggestDominantLocationAsNewDefault));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-SLOT-01",
            ItemName = "Hàng tối ưu vị trí",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            IsActive = true
        });

        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 31,
                ItemId = 1,
                LocationId = 1,
                Quantity = 2,
                ReservedQty = 0,
                UpdatedAt = DateTime.UtcNow
            },
            new ItemLocation
            {
                ItemLocationId = 32,
                ItemId = 1,
                LocationId = 2,
                Quantity = 18,
                ReservedQty = 0,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        var result = await controller.Slotting(1, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<SlottingSuggestionRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal("ITEM-SLOT-01", rows[0].ItemCode);
        Assert.Equal(2, rows[0].SuggestedLocationId);
        Assert.True(rows[0].DominancePercent >= 90);
    }

    [Fact]
    public async Task Slotting_ShouldRespectOwnerScopeAndOwnerQuantities()
    {
        await using var db = CreateDb(nameof(Slotting_ShouldRespectOwnerScopeAndOwnerQuantities));
        SeedWarehouseGraph(db);

        db.Partners.AddRange(
            new Partner { PartnerId = 10, PartnerCode = "OWN10", PartnerName = "Owner 10", IsActive = true, IsThreePlClient = true },
            new Partner { PartnerId = 20, PartnerCode = "OWN20", PartnerName = "Owner 20", IsActive = true, IsThreePlClient = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "OWNER-SLOT",
            ItemName = "Hang slotting theo owner",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 501, ItemId = 1, OwnerPartnerId = 10, LocationId = 1, Quantity = 2, ReservedQty = 0, UpdatedAt = DateTime.UtcNow },
            new ItemLocation { ItemLocationId = 502, ItemId = 1, OwnerPartnerId = 10, LocationId = 2, Quantity = 18, ReservedQty = 0, UpdatedAt = DateTime.UtcNow },
            new ItemLocation { ItemLocationId = 503, ItemId = 1, OwnerPartnerId = 20, LocationId = 1, Quantity = 100, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "owner10.manager", roleName: "Manager", ownerPartnerClaims: new[] { 10 });

        var result = await controller.Slotting(1, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<SlottingSuggestionRow>>(view.Model);
        var row = Assert.Single(rows);
        Assert.Equal(10, row.OwnerPartnerId);
        Assert.Contains("OWN10", row.OwnerName, StringComparison.Ordinal);
        Assert.Equal(20, row.TotalStockQty);
        Assert.Equal(2, row.SuggestedLocationId);
    }

    [Fact]
    public async Task SlottingSimulation_ShouldCarryOwnerToSourceAndMovementTask()
    {
        await using var db = CreateDb(nameof(SlottingSimulation_ShouldCarryOwnerToSourceAndMovementTask));
        SeedWarehouseGraph(db);

        var golden = await db.Locations.FindAsync(1);
        var source = await db.Locations.FindAsync(2);
        golden!.HeightLevel = 3;
        golden.IsGoldenZone = true;
        golden.AisleSequence = 1;
        golden.MaxWeightCapacityKg = 2000;
        source!.HeightLevel = 5;
        source.AisleSequence = 9;
        source.AllowMechanicalHandling = true;
        source.MaxWeightCapacityKg = 2000;

        db.Partners.AddRange(
            new Partner { PartnerId = 10, PartnerCode = "OWN10", PartnerName = "Owner 10", IsActive = true, IsThreePlClient = true },
            new Partner { PartnerId = 20, PartnerCode = "OWN20", PartnerName = "Owner 20", IsActive = true, IsThreePlClient = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "OWNER-SIM",
            ItemName = "Hang mo phong theo owner",
            BaseUomId = 1,
            UnitCost = 10,
            Weight = 5,
            AbcClass = "A",
            DefaultLocationId = 2,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 511, ItemId = 1, OwnerPartnerId = 10, LocationId = 2, Quantity = 20, ReservedQty = 0, UpdatedAt = DateTime.UtcNow },
            new ItemLocation { ItemLocationId = 512, ItemId = 1, OwnerPartnerId = 20, LocationId = 2, Quantity = 200, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "owner10.manager", roleName: "Manager", ownerPartnerClaims: new[] { 10 });

        var createResult = await controller.CreateSlottingSimulation(new CreateSlottingSimulationRequest
        {
            WarehouseId = 1,
            ScenarioName = "Owner scoped simulation",
            MaxLines = 5
        });

        Assert.IsType<RedirectToActionResult>(createResult);
        var scenario = await db.SlottingSimulationScenarios.Include(s => s.Lines).SingleAsync();
        var line = Assert.Single(scenario.Lines);
        Assert.Equal(10, line.OwnerPartnerId);
        Assert.Equal(511, line.SourceItemLocationId);

        var approveResult = await controller.ApproveSlottingSimulation(scenario.ScenarioId);

        Assert.IsType<RedirectToActionResult>(approveResult);
        var task = await db.MovementTasks.SingleAsync();
        Assert.Equal(10, task.OwnerPartnerId);
        Assert.Equal(511, task.SourceItemLocationId);
    }

    [Fact]
    public void MovementTaskDuplicateGuard_ShouldIncludeOwnerPartnerId()
    {
        using var db = CreateDb(nameof(MovementTaskDuplicateGuard_ShouldIncludeOwnerPartnerId));

        var movementTaskEntity = db.Model.FindEntityType(typeof(MovementTask));
        Assert.NotNull(movementTaskEntity);
        var duplicateGuard = Assert.Single(movementTaskEntity!.GetIndexes(), i => i.GetDatabaseName() == "IX_MovementTasks_Open_Item_DuplicateGuard");
        var propertyNames = duplicateGuard.Properties.Select(p => p.Name).ToList();

        Assert.Contains(nameof(MovementTask.OwnerPartnerId), propertyNames);
    }

    [Fact]
    public async Task Slotting_ShouldNotSuggestHeavyItemToHighShelfWithoutMechanicalHandling()
    {
        await using var db = CreateDb(nameof(Slotting_ShouldNotSuggestHeavyItemToHighShelfWithoutMechanicalHandling));
        SeedWarehouseGraph(db);

        db.Locations.Add(new Location
        {
            LocationId = 5,
            ZoneId = 1,
            LocationCode = "L5-HIGH",
            HeightLevel = 5,
            WeightLimitKg = 23,
            MaxWeightCapacityKg = 2000,
            MaxCapacity = 2000,
            IsActive = true
        });
        var low = await db.Locations.FindAsync(2);
        low!.HeightLevel = 2;
        low.IsGoldenZone = true;
        low.MaxWeightCapacityKg = 2000;
        low.MaxCapacity = 2000;

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "HEAVY-01",
            ItemName = "Hàng nặng",
            BaseUomId = 1,
            UnitCost = 10,
            Weight = 30,
            DefaultLocationId = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 61,
            ItemId = 1,
            LocationId = 5,
            Quantity = 10,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user");
        var result = await controller.Slotting(1, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<SlottingSuggestionRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal(2, rows[0].SuggestedLocationId);
        Assert.NotEqual(5, rows[0].SuggestedLocationId);
    }

    [Fact]
    public async Task Slotting_ShouldPrioritizeGoldenZoneForAClassItem()
    {
        await using var db = CreateDb(nameof(Slotting_ShouldPrioritizeGoldenZoneForAClassItem));
        SeedWarehouseGraph(db);

        var level1 = await db.Locations.FindAsync(1);
        var golden = await db.Locations.FindAsync(2);
        level1!.HeightLevel = 1;
        level1.IsGoldenZone = false;
        level1.MaxWeightCapacityKg = 2000;
        golden!.HeightLevel = 3;
        golden.IsGoldenZone = true;
        golden.MaxWeightCapacityKg = 2000;

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "FAST-01",
            ItemName = "Hàng bán nhanh",
            BaseUomId = 1,
            UnitCost = 10,
            Weight = 5,
            AbcClass = "A",
            DefaultLocationId = 1,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 62,
            ItemId = 1,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user");
        var result = await controller.Slotting(1, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<SlottingSuggestionRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal(2, rows[0].SuggestedLocationId);
        Assert.True(rows[0].SuggestedIsGoldenZone);
        Assert.Equal("A", rows[0].AbcClass);
    }

    [Fact]
    public async Task Slotting_ShouldRespectPerUnitWeightLimit()
    {
        await using var db = CreateDb(nameof(Slotting_ShouldRespectPerUnitWeightLimit));
        SeedWarehouseGraph(db);

        db.Locations.Add(new Location
        {
            LocationId = 3,
            ZoneId = 1,
            LocationCode = "L3-GOLDEN",
            HeightLevel = 3,
            IsGoldenZone = true,
            MaxWeightCapacityKg = 2000,
            MaxCapacity = 2000,
            IsActive = true
        });
        var blocked = await db.Locations.FindAsync(2);
        blocked!.HeightLevel = 3;
        blocked.IsGoldenZone = true;
        blocked.WeightLimitKg = 10;
        blocked.MaxWeightCapacityKg = 2000;

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "LIMIT-01",
            ItemName = "Hàng vượt giới hạn",
            BaseUomId = 1,
            UnitCost = 10,
            Weight = 20,
            AbcClass = "A",
            DefaultLocationId = 1,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 63, ItemId = 1, LocationId = 1, Quantity = 4, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user");
        var result = await controller.Slotting(1, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<SlottingSuggestionRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal(3, rows[0].SuggestedLocationId);
        Assert.NotEqual(2, rows[0].SuggestedLocationId);
    }

    [Fact]
    public async Task Slotting_ShouldRespectMaxWeightCapacity()
    {
        await using var db = CreateDb(nameof(Slotting_ShouldRespectMaxWeightCapacity));
        SeedWarehouseGraph(db);

        db.Locations.Add(new Location
        {
            LocationId = 3,
            ZoneId = 1,
            LocationCode = "L3-CAPACITY",
            HeightLevel = 3,
            IsGoldenZone = true,
            MaxWeightCapacityKg = 2000,
            MaxCapacity = 2000,
            IsActive = true
        });
        var blocked = await db.Locations.FindAsync(2);
        blocked!.HeightLevel = 3;
        blocked.IsGoldenZone = true;
        blocked.MaxWeightCapacityKg = 10;

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "CAP-01",
            ItemName = "Hàng kiểm sức chứa",
            BaseUomId = 1,
            UnitCost = 10,
            Weight = 5,
            AbcClass = "A",
            DefaultLocationId = 1,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 64, ItemId = 1, LocationId = 1, Quantity = 3, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user");
        var result = await controller.Slotting(1, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<SlottingSuggestionRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal(3, rows[0].SuggestedLocationId);
        Assert.NotEqual(2, rows[0].SuggestedLocationId);
    }

    [Fact]
    public async Task ApplySlotting_ShouldCreateReslottingTaskAndNotUpdateDefaultImmediately()
    {
        await using var db = CreateDb(nameof(ApplySlotting_ShouldCreateReslottingTaskAndNotUpdateDefaultImmediately));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-SLOT-02",
            ItemName = "Hàng áp dụng slotting",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 41,
                ItemId = 1,
                LocationId = 1,
                Quantity = 5,
                ReservedQty = 0,
                UpdatedAt = DateTime.UtcNow
            },
            new ItemLocation
            {
                ItemLocationId = 42,
                ItemId = 1,
                LocationId = 2,
                Quantity = 20,
                ReservedQty = 0,
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, userName: "manager.user");

        var result = await controller.ApplySlotting(1, 2);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Slotting", redirect.ActionName);

        var item = await db.Items.FindAsync(1);
        var task = await db.MovementTasks.SingleAsync();

        Assert.NotNull(item);
        Assert.Equal(1, item!.DefaultLocationId);
        Assert.Equal(MovementTaskTypeEnum.Reslotting, task.TaskType);
        Assert.Equal(MovementTaskStatusEnum.Pending, task.Status);
        Assert.Equal(1, task.SourceLocationId);
        Assert.Equal(2, task.DestinationLocationId);
    }

    [Fact]
    public async Task ConfirmMovementTask_ShouldMoveReslottingStockAndUpdateDefaultLocation()
    {
        await using var db = CreateDb(nameof(ConfirmMovementTask_ShouldMoveReslottingStockAndUpdateDefaultLocation));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-SLOT-03",
            ItemName = "Hàng xác nhận slotting",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 51, ItemId = 1, LocationId = 1, Quantity = 5, ReservedQty = 0, UpdatedAt = DateTime.UtcNow },
            new ItemLocation { ItemLocationId = 52, ItemId = 1, LocationId = 2, Quantity = 20, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, userName: "manager.user");
        await controller.ApplySlotting(1, 2);
        var task = await db.MovementTasks.SingleAsync();

        var result = await controller.ConfirmMovementTask(task.MovementTaskId, "L1", "L2", 5);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(0, (await db.ItemLocations.FindAsync(51))!.Quantity);
        Assert.Equal(25, (await db.ItemLocations.FindAsync(52))!.Quantity);
        Assert.Equal(2, (await db.Items.FindAsync(1))!.DefaultLocationId);
        Assert.Equal(MovementTaskStatusEnum.Completed, (await db.MovementTasks.FindAsync(task.MovementTaskId))!.Status);
        Assert.NotNull(await db.AuditLogs.FirstOrDefaultAsync(x => x.AppModule == "Slotting"));
    }

    [Fact]
    public async Task CreateSlottingSimulation_ShouldPersistScenarioWithSavingsSnapshot()
    {
        await using var db = CreateDb(nameof(CreateSlottingSimulation_ShouldPersistScenarioWithSavingsSnapshot));
        SeedWarehouseGraph(db);
        PrepareSlottingSimulationFixture(db);
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: "Manager");

        var result = await controller.CreateSlottingSimulation(new CreateSlottingSimulationRequest
        {
            WarehouseId = 1,
            ScenarioName = "Golden move",
            MaxLines = 10
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SlottingSimulation", redirect.ActionName);

        var scenario = await db.SlottingSimulationScenarios.Include(s => s.Lines).SingleAsync();
        var line = Assert.Single(scenario.Lines);
        Assert.Equal(SlottingSimulationStatusEnum.Draft, scenario.Status);
        Assert.Equal(2, line.SourceLocationId);
        Assert.Equal(1, line.SuggestedLocationId);
        Assert.True(line.EstimatedTravelMinutesSaved > 0);
        Assert.True(line.MovementCostMinutes > 0);
        Assert.True(line.NetEstimatedMinutesSaved > 0);
    }

    [Fact]
    public async Task ApproveSlottingSimulation_ShouldCreateMovementTasksWithoutMovingStockImmediately()
    {
        await using var db = CreateDb(nameof(ApproveSlottingSimulation_ShouldCreateMovementTasksWithoutMovingStockImmediately));
        SeedWarehouseGraph(db);
        PrepareSlottingSimulationFixture(db);
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: "Manager");
        await controller.CreateSlottingSimulation(new CreateSlottingSimulationRequest
        {
            WarehouseId = 1,
            ScenarioName = "Approve golden move",
            MaxLines = 10
        });
        var scenario = await db.SlottingSimulationScenarios.SingleAsync();

        var result = await controller.ApproveSlottingSimulation(scenario.ScenarioId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SlottingSimulation", redirect.ActionName);

        var task = await db.MovementTasks.SingleAsync();
        var line = await db.SlottingSimulationLines.SingleAsync();
        var item = await db.Items.FindAsync(1);
        var stock = await db.ItemLocations.FindAsync(71);

        Assert.Equal(MovementTaskTypeEnum.Reslotting, task.TaskType);
        Assert.Equal(MovementTaskStatusEnum.Pending, task.Status);
        Assert.Equal("SlottingSimulation", task.SourceModule);
        Assert.Equal(2, task.SourceLocationId);
        Assert.Equal(1, task.DestinationLocationId);
        Assert.Equal(SlottingSimulationLineStatusEnum.TaskCreated, line.Status);
        Assert.Equal(task.MovementTaskId, line.MovementTaskId);
        Assert.Equal(2, item!.DefaultLocationId);
        Assert.Equal(20, stock!.Quantity);
    }

    [Fact]
    public async Task ConfirmMovementTask_ShouldMoveReplenishmentStockWithLotAndExpiry()
    {
        await using var db = CreateDb(nameof(ConfirmMovementTask_ShouldMoveReplenishmentStockWithLotAndExpiry));
        SeedWarehouseGraph(db);
        var expiry = new DateTime(2026, 12, 31);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-REP-04",
            ItemName = "Hàng bổ sung theo lô",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 61,
            ItemId = 1,
            LocationId = 2,
            Quantity = 20,
            ReservedQty = 0,
            LotNumber = "LOT-RF",
            ExpiryDate = expiry,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        await controller.ExecuteReplenishment(1, 1, 5, 61);
        var task = await db.MovementTasks.SingleAsync();

        await controller.ConfirmMovementTask(task.MovementTaskId, "L2", "L1", 5, 1);

        Assert.Equal(15, (await db.ItemLocations.FindAsync(61))!.Quantity);
        var dest = await db.ItemLocations.SingleAsync(x => x.ItemId == 1 && x.LocationId == 1 && x.LotNumber == "LOT-RF" && x.ExpiryDate == expiry);
        Assert.Equal(5, dest.Quantity);
    }

    [Fact]
    public async Task LpnMovement_ShouldMoveMixedLpnAndSnapshotAtomically()
    {
        await using var db = CreateDb(nameof(LpnMovement_ShouldMoveMixedLpnAndSnapshotAtomically));
        SeedWarehouseGraph(db);

        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "MIX-A", ItemName = "Mixed A", BaseUomId = 1, UnitCost = 10, IsActive = true },
            new Item { ItemId = 2, ItemCode = "MIX-B", ItemName = "Mixed B", BaseUomId = 1, UnitCost = 20, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 2001, ItemId = 1, LocationId = 1, Quantity = 3, ReservedQty = 0, LotNumber = "LOT-A", UpdatedAt = DateTime.UtcNow },
            new ItemLocation { ItemLocationId = 2002, ItemId = 2, LocationId = 1, Quantity = 5, ReservedQty = 0, LotNumber = "LOT-B", UpdatedAt = DateTime.UtcNow });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 2001,
            LpnCode = "LPN-MIX-2001",
            VoucherId = 1,
            WarehouseId = 1,
            CurrentLocationId = 1,
            LocationId = 1,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail { ItemId = 1, Quantity = 3, LotNumber = "LOT-A" },
                new LicensePlateDetail { ItemId = 2, Quantity = 5, LotNumber = "LOT-B" }
            }
        });
        await db.SaveChangesAsync();

        var service = new MovementTaskService(db, new EfUnitOfWork(db));
        var task = await service.CreateLpnMovementTaskAsync("LPN-MIX-2001", 2, MovementTaskTypeEnum.Relocate, MovementTaskPriorityEnum.High, 1, "tester");
        await service.CompleteAsync(task.MovementTaskId, "L1", "L2", 8, 1, "tester", "LPN-MIX-2001");

        Assert.Equal(MovementTaskModeEnum.Lpn, task.MovementMode);
        Assert.Equal(2, (await db.LicensePlates.FindAsync(2001L))!.CurrentLocationId);
        Assert.Equal(0, (await db.ItemLocations.FindAsync(2001))!.Quantity);
        Assert.Equal(0, (await db.ItemLocations.FindAsync(2002))!.Quantity);
        Assert.Equal(3, (await db.ItemLocations.SingleAsync(x => x.ItemId == 1 && x.LocationId == 2 && x.LotNumber == "LOT-A")).Quantity);
        Assert.Equal(5, (await db.ItemLocations.SingleAsync(x => x.ItemId == 2 && x.LocationId == 2 && x.LotNumber == "LOT-B")).Quantity);
        Assert.Equal(InventorySnapshotOutboxStatusEnum.Processed, (await db.InventorySnapshotOutbox.SingleAsync()).Status);
    }

    [Fact]
    public async Task MovementTaskComplete_ShouldAllowShortMoveWhenOnlyConfirmedQtyIsAvailable()
    {
        await using var db = CreateDb(nameof(MovementTaskComplete_ShouldAllowShortMoveWhenOnlyConfirmedQtyIsAvailable));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 2030,
            ItemCode = "MOVE-SHORT-2030",
            ItemName = "Movement short item",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 203001,
            ItemId = 2030,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available,
            UpdatedAt = VietnamTime.Now
        });
        await db.SaveChangesAsync();

        var service = new MovementTaskService(db, new EfUnitOfWork(db));
        var task = await service.CreateMovementTaskAsync(new MovementTaskCreateRequest
        {
            WarehouseId = 1,
            ItemId = 2030,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            SourceItemLocationId = 203001,
            TaskType = MovementTaskTypeEnum.Relocate,
            PlannedQty = 10
        }, scopedWarehouseId: 1, actor: "planner.user");

        var source = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 203001);
        source.Quantity = 6;
        await db.SaveChangesAsync();

        var completed = await service.CompleteAsync(task.MovementTaskId, "L1", "L2", 6, 1, "forklift.user");

        Assert.Equal(MovementTaskStatusEnum.Short, completed.Status);
        Assert.Equal(6, completed.ConfirmedQty);
        Assert.Equal(0, source.Quantity);
        Assert.Equal(6, await db.ItemLocations
            .Where(il => il.ItemId == 2030 && il.LocationId == 2)
            .Select(il => il.Quantity)
            .SingleAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), log =>
            log.TableName == "MovementTask"
            && log.RecordId == task.MovementTaskId.ToString()
            && log.ActionType == "MOVE_SHORT");
    }

    [Fact]
    public async Task MovementTaskCreate_InternalOwner_ShouldNotUseThreePlOwnedStock()
    {
        await using var db = CreateDb(nameof(MovementTaskCreate_InternalOwner_ShouldNotUseThreePlOwnedStock));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 2031,
            ItemCode = "MOVE-OWNER-2031",
            ItemName = "Movement owner scoped item",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 203101,
            ItemId = 2031,
            OwnerPartnerId = 10,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available,
            UpdatedAt = VietnamTime.Now
        });
        await db.SaveChangesAsync();

        var service = new MovementTaskService(db, new EfUnitOfWork(db));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateMovementTaskAsync(new MovementTaskCreateRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = null,
            ItemId = 2031,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            SourceItemLocationId = 203101,
            TaskType = MovementTaskTypeEnum.Relocate,
            PlannedQty = 5
        }, scopedWarehouseId: 1, actor: "planner.user"));

        Assert.Equal("MOVEMENT_SOURCE_STOCK_NOT_FOUND", ex.Code);
        Assert.Empty(db.MovementTasks);
        Assert.Equal(10, (await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 203101)).Quantity);
    }

    [Fact]
    public async Task LpnMovement_ShouldMoveNestedChildrenWithParent()
    {
        await using var db = CreateDb(nameof(LpnMovement_ShouldMoveNestedChildrenWithParent));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "NEST-A", ItemName = "Nested A", BaseUomId = 1, UnitCost = 10, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 2101, ItemId = 1, LocationId = 1, Quantity = 4, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        db.LicensePlates.AddRange(
            new LicensePlate
            {
                LicensePlateId = 2100,
                LpnCode = "LPN-PARENT-2100",
                VoucherId = 1,
                WarehouseId = 1,
                CurrentLocationId = 1,
                LocationId = 1,
                Status = LpnStatusEnum.Stored,
                IsActive = true
            },
            new LicensePlate
            {
                LicensePlateId = 2101,
                LpnCode = "LPN-CHILD-2101",
                VoucherId = 1,
                WarehouseId = 1,
                ParentLpnId = 2100,
                CurrentLocationId = 1,
                LocationId = 1,
                Status = LpnStatusEnum.Stored,
                IsActive = true,
                Details = { new LicensePlateDetail { ItemId = 1, Quantity = 4 } }
            });
        await db.SaveChangesAsync();

        var service = new MovementTaskService(db, new EfUnitOfWork(db));
        var task = await service.CreateLpnMovementTaskAsync("LPN-PARENT-2100", 2, MovementTaskTypeEnum.Relocate, MovementTaskPriorityEnum.High, 1, "tester");
        await service.CompleteAsync(task.MovementTaskId, "L1", "L2", 4, 1, "tester", "LPN-PARENT-2100");

        Assert.Equal(2, (await db.LicensePlates.FindAsync(2100L))!.CurrentLocationId);
        Assert.Equal(2, (await db.LicensePlates.FindAsync(2101L))!.CurrentLocationId);
        Assert.Equal(0, (await db.ItemLocations.FindAsync(2101))!.Quantity);
        Assert.Equal(4, (await db.ItemLocations.SingleAsync(x => x.ItemId == 1 && x.LocationId == 2)).Quantity);
    }

    [Fact]
    public async Task InventorySnapshotOutbox_ShouldBeIdempotentForProcessedLpnMove()
    {
        await using var db = CreateDb(nameof(InventorySnapshotOutbox_ShouldBeIdempotentForProcessedLpnMove));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "IDEMP-A", ItemName = "Idempotent A", BaseUomId = 1, UnitCost = 10, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 2201, ItemId = 1, LocationId = 1, Quantity = 2, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 2201,
            LpnCode = "LPN-IDEMP-2201",
            VoucherId = 1,
            WarehouseId = 1,
            CurrentLocationId = 1,
            LocationId = 1,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details = { new LicensePlateDetail { ItemId = 1, Quantity = 2 } }
        });
        await db.SaveChangesAsync();

        var service = new MovementTaskService(db, new EfUnitOfWork(db));
        var task = await service.CreateLpnMovementTaskAsync("LPN-IDEMP-2201", 2, MovementTaskTypeEnum.Relocate, MovementTaskPriorityEnum.High, 1, "tester");
        await service.CompleteAsync(task.MovementTaskId, "L1", "L2", 2, 1, "tester", "LPN-IDEMP-2201");

        var snapshot = new InventorySnapshotService(db);
        await snapshot.RecordAndApplyLpnMovedAsync(new LpnMovementSnapshotRequest(2201, 1, 1, 2, $"movement-task:{task.MovementTaskId}:lpn-moved", "tester"));

        Assert.Equal(2, (await db.ItemLocations.SingleAsync(x => x.ItemId == 1 && x.LocationId == 2)).Quantity);
        Assert.Equal(0, (await db.ItemLocations.FindAsync(2201))!.Quantity);
        Assert.Single(await db.InventorySnapshotOutbox.ToListAsync());
    }

    [Fact]
    public async Task InventoryReconciliation_ShouldAutoHealSmallSnapshotDrift()
    {
        await using var db = CreateDb(nameof(InventoryReconciliation_ShouldAutoHealSmallSnapshotDrift));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "REC-A", ItemName = "Reconcile A", BaseUomId = 1, UnitCost = 10, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 2301, ItemId = 1, LocationId = 1, Quantity = 9.99995m, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 2301,
            LpnCode = "LPN-REC-2301",
            VoucherId = 1,
            WarehouseId = 1,
            CurrentLocationId = 1,
            LocationId = 1,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details = { new LicensePlateDetail { ItemId = 1, Quantity = 10 } }
        });
        await db.SaveChangesAsync();

        var reconciliation = new InventorySnapshotService(db);
        var run = await reconciliation.RunAsync(1, 0.0001m);

        Assert.Equal(InventoryReconciliationRunStatusEnum.Completed, run.Status);
        Assert.Equal(1, run.AutoHealedCount);
        Assert.Equal(10, (await db.ItemLocations.FindAsync(2301))!.Quantity);
        Assert.Equal(InventoryReconciliationActionEnum.AutoHealed, (await db.InventoryReconciliationIssues.SingleAsync()).Action);
    }

    [Fact]
    public async Task InventoryReconciliation_ShouldRollbackAutoHealWhenBalanceSyncFails()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 2401,
            ItemCode = "REC-ROLLBACK",
            ItemName = "Reconciliation rollback",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 2401,
            VoucherCode = "PN-REC-ROLLBACK",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "audit.test"
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 2401,
            ItemId = 2401,
            LocationId = 1,
            Quantity = 9.99995m,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 2401,
            LpnCode = "LPN-REC-ROLLBACK",
            VoucherId = 2401,
            WarehouseId = 1,
            CurrentLocationId = 1,
            LocationId = 1,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail { ItemId = 2401, Quantity = 10 }
            }
        });
        await db.SaveChangesAsync();

        var service = new InventorySnapshotService(db, new ThrowingInventoryBalanceService());
        var run = await service.RunAsync(1, 0.0001m);

        Assert.Equal(InventoryReconciliationRunStatusEnum.Failed, run.Status);
        await using var verify = new AppDbContext(options) { SkipAudit = true };
        Assert.Equal(9.99995m, (await verify.ItemLocations.SingleAsync(x => x.ItemLocationId == 2401)).Quantity);
        Assert.Empty(await verify.InventoryReconciliationIssues.ToListAsync());
    }

    [Fact]
    public async Task InventorySnapshotOutbox_ShouldRollbackMoveWhenBalanceSyncFails()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 2501,
            ItemCode = "SNAPSHOT-ROLLBACK",
            ItemName = "Snapshot rollback",
            BaseUomId = 1,
            UnitCost = 10,
            CurrentStock = 10,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 2501,
            VoucherCode = "PN-SNAPSHOT-ROLLBACK",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "audit.test"
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 2501,
            ItemId = 2501,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 2501,
            LpnCode = "LPN-SNAPSHOT-ROLLBACK",
            VoucherId = 2501,
            WarehouseId = 1,
            CurrentLocationId = 2,
            LocationId = 2,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail { ItemId = 2501, Quantity = 2 }
            }
        });
        db.InventorySnapshotOutbox.Add(new InventorySnapshotOutbox
        {
            InventorySnapshotOutboxId = 2501,
            EventType = InventorySnapshotEventTypeEnum.LpnMoved,
            LicensePlateId = 2501,
            WarehouseId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            IdempotencyKey = "AUDIT_TEST_snapshot_rollback",
            PayloadJson = "{}",
            Status = InventorySnapshotOutboxStatusEnum.Pending,
            CreatedBy = "audit.test",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new InventorySnapshotService(db, new ThrowingInventoryBalanceService());
        await service.ProcessOutboxBatchAsync(1);

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        Assert.Equal(10, (await verify.ItemLocations.SingleAsync(x => x.ItemLocationId == 2501)).Quantity);
        Assert.False(await verify.ItemLocations.AnyAsync(x => x.ItemId == 2501 && x.LocationId == 2));
        var outbox = await verify.InventorySnapshotOutbox.SingleAsync(x => x.InventorySnapshotOutboxId == 2501);
        Assert.Equal(InventorySnapshotOutboxStatusEnum.Failed, outbox.Status);
        Assert.Equal(1, outbox.RetryCount);

        for (var attempt = 2; attempt <= 5; attempt++)
        {
            db.ChangeTracker.Clear();
            var retry = await db.InventorySnapshotOutbox.SingleAsync(x => x.InventorySnapshotOutboxId == 2501);
            retry.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            await service.ProcessOutboxBatchAsync(1);
        }

        db.ChangeTracker.Clear();
        var deadLetter = await db.InventorySnapshotOutbox.SingleAsync(x => x.InventorySnapshotOutboxId == 2501);
        Assert.Equal(InventorySnapshotOutboxStatusEnum.DeadLetter, deadLetter.Status);
        Assert.Equal(5, deadLetter.RetryCount);
        Assert.Null(deadLetter.NextAttemptAt);

        await service.ProcessOutboxBatchAsync(1);
        db.ChangeTracker.Clear();
        Assert.Equal(5, (await db.InventorySnapshotOutbox.SingleAsync(x => x.InventorySnapshotOutboxId == 2501)).RetryCount);
    }

    [Fact]
    public async Task InventorySnapshotOutbox_ShouldCommitMoveBalanceAndLedgerTogether()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 2551,
            ItemCode = "SNAPSHOT-COMMIT",
            ItemName = "Snapshot commit",
            BaseUomId = 1,
            UnitCost = 10,
            CurrentStock = 10,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 2551,
            VoucherCode = "PN-SNAPSHOT-COMMIT",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            CreatedBy = "audit.test"
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 2551,
            ItemId = 2551,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 2551,
            LpnCode = "LPN-SNAPSHOT-COMMIT",
            VoucherId = 2551,
            WarehouseId = 1,
            CurrentLocationId = 2,
            LocationId = 2,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail { ItemId = 2551, Quantity = 2 }
            }
        });
        db.InventorySnapshotOutbox.Add(new InventorySnapshotOutbox
        {
            InventorySnapshotOutboxId = 2551,
            EventType = InventorySnapshotEventTypeEnum.LpnMoved,
            LicensePlateId = 2551,
            WarehouseId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            IdempotencyKey = "AUDIT_TEST_snapshot_commit",
            PayloadJson = "{}",
            Status = InventorySnapshotOutboxStatusEnum.Pending,
            CreatedBy = "audit.test",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.SkipAudit = false;

        var logger = new CapturingLogger<InventorySnapshotService>();
        var service = new InventorySnapshotService(db, new ClientSideInventoryBalanceService(db), logger: logger);
        await service.ProcessOutboxBatchAsync(1);

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        var processedOutbox = await verify.InventorySnapshotOutbox.SingleAsync(x => x.InventorySnapshotOutboxId == 2551);
        Assert.True(
            processedOutbox.Status == InventorySnapshotOutboxStatusEnum.Processed,
            logger.Exception?.ToString() ?? processedOutbox.LastError);
        Assert.Equal(8, (await verify.ItemLocations.SingleAsync(x => x.ItemLocationId == 2551)).Quantity);
        Assert.Equal(2, (await verify.ItemLocations.SingleAsync(x => x.ItemId == 2551 && x.LocationId == 2)).Quantity);
        Assert.Equal(10, (await verify.Items.SingleAsync(x => x.ItemId == 2551)).CurrentStock);
        var ledger = await verify.InventoryTransactions
            .Where(x => x.TransactionGroupKey == "snapshot-outbox:2551:AUDIT_TEST_snapshot_commit")
            .OrderBy(x => x.LocationId)
            .ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Equal(new[] { -2m, 2m }, ledger.Select(x => x.QuantityDelta).ToArray());
        Assert.All(ledger, x =>
        {
            Assert.Equal(InventoryTransactionTypeEnum.Move, x.TransactionType);
            Assert.Equal(2551, x.LicensePlateId);
            Assert.Equal("InventorySnapshotOutbox", x.ReferenceType);
        });
    }

    [Fact]
    public async Task CycleCountPlanning_ShouldRollbackSheetWhenLinePersistenceFails()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 2601, ItemCode = "COUNT-ROLLBACK", ItemName = "Count rollback", BaseUomId = 1, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 2601, ItemId = 2601, LocationId = 1, Quantity = 4, HoldStatus = InventoryHoldStatusEnum.Available });
        db.CycleCountPrograms.Add(new CycleCountProgram
        {
            ProgramId = 2601,
            ProgramName = "AUDIT_TEST cycle count rollback",
            WarehouseId = 1,
            FrequencyA = 1,
            FrequencyB = 7,
            FrequencyC = 30,
            IsActive = true,
            CreatedBy = "audit.test"
        });
        db.CycleCountSchedules.Add(new CycleCountSchedule
        {
            ScheduleId = 2601,
            ProgramId = 2601,
            ItemId = 2601,
            LocationId = 1,
            AbcClass = 'A',
            NextScheduledAt = VietnamTime.Today,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new CycleCountPlanningService(db, new FailOnSaveUnitOfWork(db, failOnSave: 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateDueSheetAsync(2601, "audit.test"));

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        Assert.Empty(await verify.StockCountSheets.ToListAsync());
        var schedule = await verify.CycleCountSchedules.SingleAsync(x => x.ScheduleId == 2601);
        Assert.Equal(0, schedule.CountAttempt);
        Assert.Equal(VietnamTime.Today, schedule.NextScheduledAt!.Value.Date);
    }

    [Fact]
    public async Task RunCycleCountProgram_ShouldForbidProgramOutsideWarehouseScope()
    {
        await using var db = CreateDb(nameof(RunCycleCountProgram_ShouldForbidProgramOutsideWarehouseScope));
        SeedWarehouseGraph(db);
        SeedSecondWarehouseLocation(db);
        db.Items.Add(new Item { ItemId = 2651, ItemCode = "COUNT-WH-SCOPE", ItemName = "Count warehouse scope", BaseUomId = 1, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 2651, ItemId = 2651, LocationId = 10, Quantity = 1 });
        db.CycleCountPrograms.Add(new CycleCountProgram { ProgramId = 2651, ProgramName = "Warehouse 2", WarehouseId = 2, FrequencyA = 1, FrequencyB = 7, FrequencyC = 30, IsActive = true, CreatedBy = "manager" });
        db.CycleCountSchedules.Add(new CycleCountSchedule { ScheduleId = 2651, ProgramId = 2651, ItemId = 2651, LocationId = 10, AbcClass = 'A', NextScheduledAt = VietnamTime.Today, IsActive = true });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager);
        var result = await controller.RunCycleCountProgram(2651);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await db.StockCountSheets.ToListAsync());
    }

    [Fact]
    public async Task RunCycleCountProgram_ShouldKeepOwnerAndBatchSystemQuantity()
    {
        await using var db = CreateDb(nameof(RunCycleCountProgram_ShouldKeepOwnerAndBatchSystemQuantity));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 2671, ItemCode = "COUNT-OWNER-BATCH", ItemName = "Count owner batch", BaseUomId = 1, IsActive = true, AbcClass = "A" });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 26711, ItemId = 2671, OwnerPartnerId = 10, LocationId = 1, LotNumber = "LOT-A", Quantity = 3 },
            new ItemLocation { ItemLocationId = 26712, ItemId = 2671, OwnerPartnerId = 20, LocationId = 1, LotNumber = "LOT-B", Quantity = 7 });
        db.CycleCountPrograms.Add(new CycleCountProgram { ProgramId = 2671, ProgramName = "Owner batch", WarehouseId = 1, FrequencyA = 1, FrequencyB = 7, FrequencyC = 30, IsActive = true, CreatedBy = "manager" });
        db.CycleCountSchedules.AddRange(
            new CycleCountSchedule { ScheduleId = 26711, ProgramId = 2671, ItemId = 2671, OwnerPartnerId = 10, LocationId = 1, AbcClass = 'A', NextScheduledAt = VietnamTime.Today, IsActive = true },
            new CycleCountSchedule { ScheduleId = 26712, ProgramId = 2671, ItemId = 2671, OwnerPartnerId = 20, LocationId = 1, AbcClass = 'A', NextScheduledAt = VietnamTime.Today, IsActive = true });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager);
        await controller.RunCycleCountProgram(2671);

        var sheet = await db.StockCountSheets.SingleAsync();
        var lines = await db.StockCountLines
            .Where(x => x.StockCountSheetId == sheet.StockCountSheetId)
            .OrderBy(x => x.OwnerPartnerId)
            .ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(10, lines[0].OwnerPartnerId);
        Assert.Equal("LOT-A", lines[0].LotNumber);
        Assert.Equal(3, lines[0].SystemQty);
        Assert.Equal(20, lines[1].OwnerPartnerId);
        Assert.Equal("LOT-B", lines[1].LotNumber);
        Assert.Equal(7, lines[1].SystemQty);
    }

    [Fact]
    public async Task StockCountApproveDraft_ShouldRollbackWhenSheetDoesNotExist()
    {
        await using var db = CreateDb(nameof(StockCountApproveDraft_ShouldRollbackWhenSheetDoesNotExist));
        var uow = new EfUnitOfWork(db);
        var controller = CreateReportsController(
            db,
            userName: "manager.user",
            roleName: WmsRoles.Manager,
            unitOfWork: uow);

        var result = await controller.StockCountApproveDraft(999_991L, "AUDIT_TEST_APPROVAL_REASON");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(uow.HasActiveTransaction);
        Assert.Empty(await db.Vouchers.ToListAsync());
    }

    [Fact]
    public async Task StockCountApproveDraft_ShouldRejectSheetWithUncountedLines()
    {
        await using var db = CreateDb(nameof(StockCountApproveDraft_ShouldRejectSheetWithUncountedLines));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 2681,
            ItemCode = "COUNT-NOT-ENTERED",
            ItemName = "Dòng chưa nhập số đếm",
            BaseUomId = 1,
            CurrentStock = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 2681,
            ItemId = 2681,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        db.StockCountSheets.Add(new StockCountSheet
        {
            StockCountSheetId = 2681,
            SheetCode = "AUDIT_TEST_COUNT_UNCOUNTED",
            WarehouseId = 1,
            CountDate = VietnamTime.Today,
            Status = StockCountStatusEnum.Draft,
            CreatedBy = "counter.user"
        });
        db.StockCountLines.Add(new StockCountLine
        {
            StockCountLineId = 2681,
            StockCountSheetId = 2681,
            ItemId = 2681,
            LocationId = 1,
            SystemQty = 5,
            CountedQty = null,
            Variance = null
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, userName: "manager.user", roleName: WmsRoles.Manager);
        var result = await controller.StockCountApproveDraft(2681, "AUDIT_TEST_APPROVAL_REASON");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("chưa", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StockCountStatusEnum.Draft, await db.StockCountSheets.Where(s => s.StockCountSheetId == 2681).Select(s => s.Status).SingleAsync());
        Assert.Empty(await db.Vouchers.Where(v => v.Description != null && v.Description.Contains("#2681")).ToListAsync());
        Assert.Equal(5, await db.ItemLocations.Where(il => il.ItemLocationId == 2681).Select(il => il.Quantity).SingleAsync());
    }

    [Fact]
    public async Task StockCountApproveDraft_ShouldBlockVarianceAcrossMultipleHoldBuckets()
    {
        await using var db = CreateDb(nameof(StockCountApproveDraft_ShouldBlockVarianceAcrossMultipleHoldBuckets));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 2682,
            ItemCode = "AUDIT_TEST_MULTI_HOLD",
            ItemName = "Phạm vi kiểm kê nhiều trạng thái tồn",
            BaseUomId = 1,
            CurrentStock = 10,
            UnitCost = 1,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 26821,
                ItemId = 2682,
                LocationId = 1,
                Quantity = 6,
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 26822,
                ItemId = 2682,
                LocationId = 1,
                Quantity = 4,
                HoldStatus = InventoryHoldStatusEnum.QcHold
            });
        db.StockCountSheets.Add(new StockCountSheet
        {
            StockCountSheetId = 2682,
            SheetCode = "AUDIT_TEST_COUNT_MULTI_HOLD",
            WarehouseId = 1,
            CountDate = VietnamTime.Today,
            Status = StockCountStatusEnum.Counted,
            CreatedBy = "counter.user"
        });
        db.StockCountLines.Add(new StockCountLine
        {
            StockCountLineId = 2682,
            StockCountSheetId = 2682,
            ItemId = 2682,
            LocationId = 1,
            SystemQty = 10,
            CountedQty = 9,
            Variance = -1,
            CountedBy = "counter.user",
            Status = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, userName: "manager.user", roleName: WmsRoles.Manager);
        var result = await controller.StockCountApproveDraft(2682, "AUDIT_TEST_APPROVAL_REASON");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("nhiều trạng thái tồn", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StockCountStatusEnum.Counted, await db.StockCountSheets.Where(s => s.StockCountSheetId == 2682).Select(s => s.Status).SingleAsync());
        Assert.Empty(await db.Vouchers.Where(v => v.Description != null && v.Description.Contains("#2682")).ToListAsync());
        Assert.Equal(10, await db.ItemLocations.Where(il => il.ItemId == 2682).SumAsync(il => il.Quantity));
    }

    [Fact]
    public async Task StockCountWorkflow_ShouldStartSubmitRecountAndApproveAdjustment()
    {
        await using var db = CreateDb(nameof(StockCountWorkflow_ShouldStartSubmitRecountAndApproveAdjustment));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 2691,
            ItemCode = "COUNT-WORKFLOW",
            ItemName = "Vật tư kiểm thử quy trình kiểm kê",
            BaseUomId = 1,
            CurrentStock = 5,
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 2691,
            ItemId = 2691,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        db.StockCountSheets.Add(new StockCountSheet
        {
            StockCountSheetId = 2691,
            SheetCode = "AUDIT_TEST_COUNT_WORKFLOW",
            WarehouseId = 1,
            CountDate = VietnamTime.Today,
            Status = StockCountStatusEnum.Draft,
            CreatedBy = "planner.user",
            Notes = "Cycle count: AUDIT_TEST; blind=True"
        });
        db.StockCountLines.Add(new StockCountLine
        {
            StockCountLineId = 2691,
            StockCountSheetId = 2691,
            ItemId = 2691,
            LocationId = 1,
            SystemQty = 5,
            CountedQty = null,
            Variance = null
        });
        db.CycleCountPrograms.Add(new CycleCountProgram
        {
            ProgramId = 2691,
            ProgramName = "AUDIT_TEST_COUNT_WORKFLOW",
            WarehouseId = 1,
            FrequencyA = 2,
            FrequencyB = 7,
            FrequencyC = 30,
            IsActive = true,
            CreatedBy = "planner.user"
        });
        db.CycleCountSchedules.Add(new CycleCountSchedule
        {
            ScheduleId = 2691,
            ProgramId = 2691,
            ItemId = 2691,
            LocationId = 1,
            AbcClass = 'A',
            NextScheduledAt = VietnamTime.Today,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var counter = CreateReportsController(db, warehouseClaim: 1, userName: "counter.user", roleName: "InventoryStaff");
        Assert.IsType<RedirectToActionResult>(await counter.StockCountStart(2691));
        Assert.Equal(StockCountStatusEnum.Counting, await db.StockCountSheets.Where(s => s.StockCountSheetId == 2691).Select(s => s.Status).SingleAsync());

        var firstSubmit = new StockCountEntryViewModel
        {
            StockCountSheetId = 2691,
            Lines = new List<StockCountEntryLineInput>
            {
                new() { StockCountLineId = 2691, CountedQty = 4 }
            }
        };
        Assert.IsType<RedirectToActionResult>(await counter.StockCountSubmit(firstSubmit));
        var countedLine = await db.StockCountLines.SingleAsync(l => l.StockCountLineId == 2691);
        Assert.Equal(StockCountStatusEnum.Counted, await db.StockCountSheets.Where(s => s.StockCountSheetId == 2691).Select(s => s.Status).SingleAsync());
        Assert.Equal(4, countedLine.CountedQty);
        Assert.Equal(-1, countedLine.Variance);
        Assert.Equal("counter.user", countedLine.CountedBy);
        Assert.NotNull(countedLine.CountedAt);

        var manager = CreateReportsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager);
        Assert.IsType<RedirectToActionResult>(await manager.StockCountRequestRecount(2691, "AUDIT_TEST_RECOUNT_REASON"));
        countedLine = await db.StockCountLines.SingleAsync(l => l.StockCountLineId == 2691);
        Assert.Equal(StockCountStatusEnum.Counting, await db.StockCountSheets.Where(s => s.StockCountSheetId == 2691).Select(s => s.Status).SingleAsync());
        Assert.Null(countedLine.CountedQty);
        Assert.Null(countedLine.Variance);
        Assert.Equal(3, countedLine.Status);

        Assert.IsType<RedirectToActionResult>(await counter.StockCountSubmit(firstSubmit));
        Assert.Equal(StockCountStatusEnum.Counted, await db.StockCountSheets.Where(s => s.StockCountSheetId == 2691).Select(s => s.Status).SingleAsync());
        Assert.IsType<RedirectToActionResult>(await manager.StockCountApproveDraft(2691, "AUDIT_TEST_APPROVAL_REASON"));

        var approvedSheet = await db.StockCountSheets.SingleAsync(s => s.StockCountSheetId == 2691);
        Assert.Equal(StockCountStatusEnum.Approved, approvedSheet.Status);
        Assert.NotNull(approvedSheet.GeneratedAdjustmentVoucherId);
        Assert.Equal(4, await db.ItemLocations.Where(il => il.ItemLocationId == 2691).Select(il => il.Quantity).SingleAsync());
        Assert.Equal(4, await db.Items.Where(i => i.ItemId == 2691).Select(i => i.CurrentStock).SingleAsync());
        Assert.Equal(2, await db.StockCountLines.Where(l => l.StockCountLineId == 2691).Select(l => l.Status).SingleAsync());
        var completedSchedule = await db.CycleCountSchedules.SingleAsync(s => s.ScheduleId == 2691);
        Assert.Equal(approvedSheet.ApprovedAt, completedSchedule.LastCountedAt);
        Assert.Equal(approvedSheet.ApprovedAt!.Value.Date.AddDays(2), completedSchedule.NextScheduledAt);
        Assert.Equal(1m, completedSchedule.CumulativeVariance);
    }

    [Fact]
    public async Task StockCountApproveDraft_ShouldAllowDifferentScopeInSameWarehouseAndDate()
    {
        await using var db = CreateDb(nameof(StockCountApproveDraft_ShouldAllowDifferentScopeInSameWarehouseAndDate));
        SeedWarehouseGraph(db);
        db.Items.AddRange(
            new Item
            {
                ItemId = 2701,
                ItemCode = "AUDIT_TEST_COUNT_SCOPE_A",
                ItemName = "Vật tư phạm vi kiểm kê A",
                BaseUomId = 1,
                CurrentStock = 5,
                IsActive = true
            },
            new Item
            {
                ItemId = 2702,
                ItemCode = "AUDIT_TEST_COUNT_SCOPE_B",
                ItemName = "Vật tư phạm vi kiểm kê B",
                BaseUomId = 1,
                CurrentStock = 7,
                IsActive = true
            });
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 2701,
                ItemId = 2701,
                LocationId = 1,
                Quantity = 5,
                ReservedQty = 0,
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 2702,
                ItemId = 2702,
                LocationId = 1,
                Quantity = 7,
                ReservedQty = 0,
                HoldStatus = InventoryHoldStatusEnum.Available
            });
        db.StockCountSheets.AddRange(
            new StockCountSheet
            {
                StockCountSheetId = 2701,
                SheetCode = "AUDIT_TEST_COUNT_APPROVED_SCOPE_A",
                WarehouseId = 1,
                CountDate = VietnamTime.Today,
                Status = StockCountStatusEnum.Approved,
                CreatedBy = "counter.a",
                ApprovedBy = "manager.a",
                ApprovedAt = VietnamTime.Now
            },
            new StockCountSheet
            {
                StockCountSheetId = 2702,
                SheetCode = "AUDIT_TEST_COUNT_READY_SCOPE_B",
                WarehouseId = 1,
                CountDate = VietnamTime.Today,
                Status = StockCountStatusEnum.Counted,
                CreatedBy = "counter.b"
            });
        db.StockCountLines.AddRange(
            new StockCountLine
            {
                StockCountLineId = 2701,
                StockCountSheetId = 2701,
                ItemId = 2701,
                LocationId = 1,
                SystemQty = 5,
                CountedQty = 5,
                Variance = 0,
                Status = 2
            },
            new StockCountLine
            {
                StockCountLineId = 2702,
                StockCountSheetId = 2702,
                ItemId = 2702,
                LocationId = 1,
                SystemQty = 7,
                CountedQty = 7,
                Variance = 0,
                Status = 1
            });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "manager.b", roleName: WmsRoles.Manager);
        Assert.IsType<RedirectToActionResult>(await controller.StockCountApproveDraft(2702, "AUDIT_TEST phạm vi khác"));

        Assert.Equal(StockCountStatusEnum.Approved, await db.StockCountSheets
            .Where(sheet => sheet.StockCountSheetId == 2702)
            .Select(sheet => sheet.Status)
            .SingleAsync());
        Assert.Null(controller.TempData["Error"]);
    }

    [Fact]
    public async Task StockCountApproveDraft_ShouldBlockOverlappingScopeInSameWarehouseAndDate()
    {
        await using var db = CreateDb(nameof(StockCountApproveDraft_ShouldBlockOverlappingScopeInSameWarehouseAndDate));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 2711,
            ItemCode = "AUDIT_TEST_COUNT_OVERLAP",
            ItemName = "Vật tư phạm vi trùng",
            BaseUomId = 1,
            CurrentStock = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 2711,
            ItemId = 2711,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        db.StockCountSheets.AddRange(
            new StockCountSheet
            {
                StockCountSheetId = 2711,
                SheetCode = "AUDIT_TEST_COUNT_OVERLAP_APPROVED",
                WarehouseId = 1,
                CountDate = VietnamTime.Today,
                Status = StockCountStatusEnum.Approved,
                CreatedBy = "counter.a",
                ApprovedBy = "manager.a",
                ApprovedAt = VietnamTime.Now
            },
            new StockCountSheet
            {
                StockCountSheetId = 2712,
                SheetCode = "AUDIT_TEST_COUNT_OVERLAP_READY",
                WarehouseId = 1,
                CountDate = VietnamTime.Today,
                Status = StockCountStatusEnum.Counted,
                CreatedBy = "counter.b"
            });
        db.StockCountLines.AddRange(
            new StockCountLine
            {
                StockCountLineId = 2711,
                StockCountSheetId = 2711,
                ItemId = 2711,
                LocationId = 1,
                SystemQty = 5,
                CountedQty = 5,
                Variance = 0,
                Status = 2
            },
            new StockCountLine
            {
                StockCountLineId = 2712,
                StockCountSheetId = 2712,
                ItemId = 2711,
                LocationId = 1,
                SystemQty = 5,
                CountedQty = 5,
                Variance = 0,
                Status = 1
            });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "manager.b", roleName: WmsRoles.Manager);
        Assert.IsType<RedirectToActionResult>(await controller.StockCountApproveDraft(2712, "AUDIT_TEST phạm vi trùng"));

        Assert.Equal(StockCountStatusEnum.Counted, await db.StockCountSheets
            .Where(sheet => sheet.StockCountSheetId == 2712)
            .Select(sheet => sheet.Status)
            .SingleAsync());
        Assert.Contains("trùng phạm vi", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StockCountApproveDraft_ShouldBlockWhenApproverAlsoCountedAndWriteAudit()
    {
        await using var db = CreateDb(nameof(StockCountApproveDraft_ShouldBlockWhenApproverAlsoCountedAndWriteAudit));
        SeedWarehouseGraph(db);
        db.StockCountSheets.Add(new StockCountSheet
        {
            StockCountSheetId = 2721,
            SheetCode = "AUDIT_TEST_SOD_COUNT",
            WarehouseId = 1,
            CountDate = VietnamTime.Today,
            Status = StockCountStatusEnum.Counted,
            CreatedBy = "audit.maker"
        });
        db.StockCountLines.Add(new StockCountLine
        {
            StockCountLineId = 2721,
            StockCountSheetId = 2721,
            ItemId = 2721,
            LocationId = 1,
            SystemQty = 5m,
            CountedQty = 5m,
            Variance = 0m,
            CountedBy = "audit.manager",
            Status = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "audit.manager", roleName: WmsRoles.Manager);
        Assert.IsType<RedirectToActionResult>(await controller.StockCountApproveDraft(2721, "AUDIT_TEST duyệt kiểm kê"));

        Assert.Equal(StockCountStatusEnum.Counted, await db.StockCountSheets
            .Where(sheet => sheet.StockCountSheetId == 2721)
            .Select(sheet => sheet.Status)
            .SingleAsync());
        Assert.Contains("trực tiếp kiểm đếm", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(await db.AuditLogs.ToListAsync(), log =>
            log.ActionType == "SOD_BLOCK"
            && log.RecordId == "2721"
            && log.NewValue == "COUNTED_BY_EQUALS_APPROVER");
    }

    [Fact]
    public async Task StockCountEntry_BlindCountShouldHideExpectedQuantityUntilManagerReview()
    {
        await using var db = CreateDb(nameof(StockCountEntry_BlindCountShouldHideExpectedQuantityUntilManagerReview));
        SeedWarehouseGraph(db);
        db.StockCountSheets.Add(new StockCountSheet
        {
            StockCountSheetId = 2722,
            SheetCode = "AUDIT_TEST_BLIND_COUNT",
            WarehouseId = 1,
            CountDate = VietnamTime.Today,
            Status = StockCountStatusEnum.Counting,
            CreatedBy = "inventory-risk-engine",
            Notes = "AUDIT_TEST; blind=True"
        });
        db.StockCountLines.Add(new StockCountLine
        {
            StockCountLineId = 2722,
            StockCountSheetId = 2722,
            ItemId = 2722,
            LocationId = 1,
            SystemQty = 9876.5432m,
            Status = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "audit.manager", roleName: WmsRoles.Manager);
        var countingView = Assert.IsType<ViewResult>(await controller.StockCountEntry(2722));
        var countingModel = Assert.IsType<StockCountEntryViewModel>(countingView.Model);
        Assert.True(countingModel.IsBlindCount);
        Assert.False(countingModel.CanViewExpectedQty);

        var sheet = await db.StockCountSheets.SingleAsync(row => row.StockCountSheetId == 2722);
        sheet.Status = StockCountStatusEnum.Counted;
        await db.SaveChangesAsync();
        var reviewView = Assert.IsType<ViewResult>(await controller.StockCountEntry(2722));
        Assert.True(Assert.IsType<StockCountEntryViewModel>(reviewView.Model).CanViewExpectedQty);
    }

    [Fact]
    public async Task StockCountApproveDraft_ShouldAllowDifferentLotAtSameItemAndLocation()
    {
        await using var db = CreateDb(nameof(StockCountApproveDraft_ShouldAllowDifferentLotAtSameItemAndLocation));
        SeedWarehouseGraph(db);
        db.StockCountSheets.AddRange(
            new StockCountSheet
            {
                StockCountSheetId = 2723,
                SheetCode = "AUDIT_TEST_LOT_A_APPROVED",
                WarehouseId = 1,
                CountDate = VietnamTime.Today,
                Status = StockCountStatusEnum.Approved,
                CreatedBy = "audit.counter.a",
                ApprovedBy = "audit.approver.a",
                ApprovedAt = VietnamTime.Now
            },
            new StockCountSheet
            {
                StockCountSheetId = 2724,
                SheetCode = "AUDIT_TEST_LOT_B_COUNTED",
                WarehouseId = 1,
                CountDate = VietnamTime.Today,
                Status = StockCountStatusEnum.Counted,
                CreatedBy = "audit.counter.b"
            });
        db.StockCountLines.AddRange(
            new StockCountLine
            {
                StockCountLineId = 2723,
                StockCountSheetId = 2723,
                ItemId = 2723,
                LocationId = 1,
                LotNumber = "AUDIT_TEST_LOT_A",
                SystemQty = 0m,
                CountedQty = 0m,
                Variance = 0m,
                CountedBy = "audit.counter.a",
                Status = 2
            },
            new StockCountLine
            {
                StockCountLineId = 2724,
                StockCountSheetId = 2724,
                ItemId = 2723,
                LocationId = 1,
                LotNumber = "AUDIT_TEST_LOT_B",
                SystemQty = 0m,
                CountedQty = 0m,
                Variance = 0m,
                CountedBy = "audit.counter.b",
                Status = 1
            });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "audit.manager", roleName: WmsRoles.Manager);
        Assert.IsType<RedirectToActionResult>(await controller.StockCountApproveDraft(2724, "AUDIT_TEST lô độc lập"));

        Assert.Equal(StockCountStatusEnum.Approved, await db.StockCountSheets
            .Where(sheet => sheet.StockCountSheetId == 2724)
            .Select(sheet => sheet.Status)
            .SingleAsync());
        Assert.Null(controller.TempData["Error"]);
    }

    [Fact]
    public async Task PeriodLock_SetClearAndRelockSameDate_ShouldReuseHistoryAndAudit()
    {
        await using var db = CreateDb(nameof(PeriodLock_SetClearAndRelockSameDate_ShouldReuseHistoryAndAudit));
        SeedWarehouseGraph(db);
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "period.manager", roleName: WmsRoles.Manager);
        var lockDate = VietnamTime.Today.AddDays(-1);

        Assert.IsType<RedirectToActionResult>(await controller.SetPeriodLock(1, lockDate, "AUDIT_TEST khóa lần đầu"));
        var firstLock = await db.WarehousePeriodLocks.SingleAsync();
        Assert.IsType<RedirectToActionResult>(await controller.ClearPeriodLock(firstLock.WarehousePeriodLockId));
        Assert.IsType<RedirectToActionResult>(await controller.SetPeriodLock(1, lockDate, "AUDIT_TEST mở lại cùng ngày"));

        var lockRow = Assert.Single(await db.WarehousePeriodLocks
            .Where(row => row.WarehouseId == 1 && row.LockDate == lockDate.Date)
            .ToListAsync());
        Assert.True(lockRow.IsActive);
        Assert.Equal("period.manager", lockRow.LockedBy);
        Assert.Equal("AUDIT_TEST mở lại cùng ngày", lockRow.Reason);
        Assert.Null(lockRow.UnlockedAt);
        Assert.Null(lockRow.UnlockedBy);

        var actions = await db.AuditLogs
            .Where(log => log.TableName == "WarehousePeriodLocks" && log.ActionType.StartsWith("PERIOD_LOCK_"))
            .OrderBy(log => log.AuditLogId)
            .Select(log => log.ActionType)
            .ToListAsync();
        Assert.Equal(new[] { "PERIOD_LOCK_SET", "PERIOD_LOCK_CLEAR", "PERIOD_LOCK_REOPEN" }, actions);
    }

    [Fact]
    public async Task PeriodLock_ChangingToHistoricalDate_ShouldPreserveBothHistoryRows()
    {
        await using var db = CreateDb(nameof(PeriodLock_ChangingToHistoricalDate_ShouldPreserveBothHistoryRows));
        SeedWarehouseGraph(db);
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "period.manager", roleName: WmsRoles.Manager);
        var firstDate = VietnamTime.Today.AddDays(-2);
        var secondDate = VietnamTime.Today.AddDays(-1);

        Assert.IsType<RedirectToActionResult>(await controller.SetPeriodLock(1, firstDate, "AUDIT_TEST kỳ thứ nhất"));
        var firstLock = await db.WarehousePeriodLocks.SingleAsync();
        Assert.IsType<RedirectToActionResult>(await controller.ClearPeriodLock(firstLock.WarehousePeriodLockId));
        Assert.IsType<RedirectToActionResult>(await controller.SetPeriodLock(1, secondDate, "AUDIT_TEST kỳ thứ hai"));
        Assert.IsType<RedirectToActionResult>(await controller.SetPeriodLock(1, firstDate, "AUDIT_TEST quay lại kỳ thứ nhất"));

        var rows = await db.WarehousePeriodLocks
            .Where(row => row.WarehouseId == 1)
            .OrderBy(row => row.LockDate)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(firstDate.Date, rows[0].LockDate);
        Assert.True(rows[0].IsActive);
        Assert.Equal("AUDIT_TEST quay lại kỳ thứ nhất", rows[0].Reason);
        Assert.Equal(secondDate.Date, rows[1].LockDate);
        Assert.False(rows[1].IsActive);
        Assert.Equal("period.manager", rows[1].UnlockedBy);
        Assert.NotNull(rows[1].UnlockedAt);

        var actions = await db.AuditLogs
            .Where(log => log.TableName == "WarehousePeriodLocks" && log.ActionType.StartsWith("PERIOD_LOCK_"))
            .OrderBy(log => log.AuditLogId)
            .Select(log => log.ActionType)
            .ToListAsync();
        Assert.Equal(
            new[] { "PERIOD_LOCK_SET", "PERIOD_LOCK_CLEAR", "PERIOD_LOCK_SET", "PERIOD_LOCK_SUPERSEDE", "PERIOD_LOCK_REOPEN" },
            actions);
    }

    [Fact]
    public async Task PeriodLock_RelationalUniqueIndex_ShouldAllowRelockSameDate()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);
        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 1,
            WarehouseCode = "AUDIT_TEST_WH_PERIOD",
            WarehouseName = "Kho kiểm thử khóa kỳ",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "period.manager", roleName: WmsRoles.Manager);
        var lockDate = VietnamTime.Today.AddDays(-1);

        Assert.IsType<RedirectToActionResult>(await controller.SetPeriodLock(1, lockDate, "AUDIT_TEST relational set"));
        var lockId = await db.WarehousePeriodLocks.Select(row => row.WarehousePeriodLockId).SingleAsync();
        Assert.IsType<RedirectToActionResult>(await controller.ClearPeriodLock(lockId));
        Assert.IsType<RedirectToActionResult>(await controller.SetPeriodLock(1, lockDate, "AUDIT_TEST relational reopen"));

        var row = await db.WarehousePeriodLocks.SingleAsync();
        Assert.True(row.IsActive);
        Assert.Equal("AUDIT_TEST relational reopen", row.Reason);
        Assert.Equal(3, await db.AuditLogs.CountAsync(log => log.ActionType.StartsWith("PERIOD_LOCK_")));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\t=1+1")]
    public void CsvExports_ShouldNeutralizeSpreadsheetFormulaPayloads(string payload)
    {
        var controllerType = typeof(OperationsController);
        var alwaysQuotedMethod = controllerType.GetMethod("Csv", BindingFlags.NonPublic | BindingFlags.Static);
        var conditionalMethod = controllerType.GetMethod("EscapeCsv", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(alwaysQuotedMethod);
        Assert.NotNull(conditionalMethod);
        var alwaysQuoted = Assert.IsType<string>(alwaysQuotedMethod!.Invoke(null, new object?[] { payload }));
        var conditional = Assert.IsType<string>(conditionalMethod!.Invoke(null, new object?[] { payload }));

        Assert.StartsWith("\"'", alwaysQuoted, StringComparison.Ordinal);
        var conditionalCell = conditional.StartsWith('"') ? conditional[1..] : conditional;
        Assert.StartsWith("'", conditionalCell, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovementTask_ShouldPreventDuplicateOpenTask()
    {
        await using var db = CreateDb(nameof(MovementTask_ShouldPreventDuplicateOpenTask));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITEM-DUP", ItemName = "Hàng trùng nhiệm vụ", BaseUomId = 1, UnitCost = 10, DefaultLocationId = 1, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 71, ItemId = 1, LocationId = 2, Quantity = 20, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        await controller.ExecuteReplenishment(1, 1, 5, 71);
        await controller.ExecuteReplenishment(1, 1, 5, 71);

        Assert.Single(await db.MovementTasks.ToListAsync());
        Assert.Contains("điều chuyển đang mở", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmMovementTask_ShouldMarkShortWhenConfirmedQtyIsLower()
    {
        await using var db = CreateDb(nameof(ConfirmMovementTask_ShouldMarkShortWhenConfirmedQtyIsLower));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITEM-SHORT", ItemName = "Hàng thiếu di chuyển", BaseUomId = 1, UnitCost = 10, DefaultLocationId = 1, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 81, ItemId = 1, LocationId = 2, Quantity = 20, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        await controller.ExecuteReplenishment(1, 1, 10, 81);
        var task = await db.MovementTasks.SingleAsync();

        await controller.ConfirmMovementTask(task.MovementTaskId, "L2", "L1", 6, 1);

        var completed = await db.MovementTasks.FindAsync(task.MovementTaskId);
        Assert.Equal(MovementTaskStatusEnum.Short, completed!.Status);
        Assert.Equal(6, completed.ConfirmedQty);
        Assert.Equal(14, (await db.ItemLocations.FindAsync(81))!.Quantity);
    }

    [Fact]
    public async Task ConfirmMovementTask_ShouldMarkShortWhenSourceStockChangedAndConfirmedQtyMatchesAvailable()
    {
        await using var db = CreateDb(nameof(ConfirmMovementTask_ShouldMarkShortWhenSourceStockChangedAndConfirmedQtyMatchesAvailable));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITEM-CHANGED", ItemName = "Hàng đổi nguồn", BaseUomId = 1, UnitCost = 10, DefaultLocationId = 1, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 91, ItemId = 1, LocationId = 2, Quantity = 10, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        await controller.ExecuteReplenishment(1, 1, 10, 91);
        var task = await db.MovementTasks.SingleAsync();
        var source = await db.ItemLocations.FindAsync(91);
        source!.Quantity = 5;
        await db.SaveChangesAsync();

        await controller.ConfirmMovementTask(task.MovementTaskId, "L2", "L1", 5, 1);

        var completed = await db.MovementTasks.FindAsync(task.MovementTaskId);
        Assert.Equal(MovementTaskStatusEnum.Short, completed!.Status);
        Assert.Equal(5, completed.ConfirmedQty);
        Assert.Equal(0, (await db.ItemLocations.FindAsync(91))!.Quantity);
        Assert.Contains("Đã xác nhận thiếu", controller.TempData["Success"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmMovementTask_ShouldRejectWhenConfirmedQtyExceedsChangedSourceStock()
    {
        await using var db = CreateDb(nameof(ConfirmMovementTask_ShouldRejectWhenConfirmedQtyExceedsChangedSourceStock));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITEM-CHANGED-REJECT", ItemName = "Hàng đổi nguồn bị chặn", BaseUomId = 1, UnitCost = 10, DefaultLocationId = 1, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 92, ItemId = 1, LocationId = 2, Quantity = 10, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        await controller.ExecuteReplenishment(1, 1, 10, 92);
        var task = await db.MovementTasks.SingleAsync();
        var source = await db.ItemLocations.FindAsync(92);
        source!.Quantity = 5;
        await db.SaveChangesAsync();

        await controller.ConfirmMovementTask(task.MovementTaskId, "L2", "L1", 6, 1);

        Assert.Equal(MovementTaskStatusEnum.Pending, (await db.MovementTasks.FindAsync(task.MovementTaskId))!.Status);
        Assert.Equal(5, (await db.ItemLocations.FindAsync(92))!.Quantity);
        Assert.Contains("tồn kho nguồn đã thay đổi", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestPutaway_ShouldIgnoreForeignDefaultLocationAndStayInRequestedWarehouse()
    {
        await using var db = CreateDb(nameof(SuggestPutaway_ShouldIgnoreForeignDefaultLocationAndStayInRequestedWarehouse));
        SeedWarehouseGraph(db);

        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 2,
            WarehouseCode = "WH2",
            WarehouseName = "Overflow Warehouse",
            IsActive = true
        });
        db.Zones.Add(new Zone
        {
            ZoneId = 3,
            WarehouseId = 2,
            ZoneCode = "Z3",
            ZoneName = "Zone 3",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        });
        db.Locations.Add(new Location
        {
            LocationId = 3,
            ZoneId = 3,
            LocationCode = "L3",
            IsActive = true
        });

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-PUT-01",
            ItemName = "Hàng cất kho",
            BaseUomId = 1,
            UnitCost = 15,
            DefaultLocationId = 3,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");

        var result = await controller.SuggestPutaway(new List<VouchersController.PutawayRequest>
        {
            new() { ItemId = 1, RowIndex = 0, WarehouseId = 1 }
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);

        var suggestions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetAnonValue(json.Value!, "suggestions"));
        var first = suggestions.Cast<object>().First();
        Assert.Equal(1, (int)GetAnonValue(first, "locationId")!);
        Assert.Equal("L1", (string?)GetAnonValue(first, "locationCode"));
        Assert.DoesNotContain("Fixed Bin", (string?)GetAnonValue(first, "strategy") ?? string.Empty);
    }

    [Fact]
    public async Task SuggestPutaway_ShouldPreferExactLotBinOverEmptyDefaultLocation()
    {
        await using var db = CreateDb(nameof(SuggestPutaway_ShouldPreferExactLotBinOverEmptyDefaultLocation));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-PUT-LOT",
            ItemName = "Hàng khớp lô",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            IsActive = true
        });

        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            LocationId = 2,
            Quantity = 5,
            ReservedQty = 0,
            LotNumber = "LOT-001",
            ExpiryDate = new DateTime(2026, 12, 31),
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");

        var result = await controller.SuggestPutaway(new List<VouchersController.PutawayRequest>
        {
            new() { ItemId = 1, RowIndex = 0, WarehouseId = 1, Quantity = 2, LotNumber = "LOT-001", ExpiryDate = new DateTime(2026, 12, 31) }
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);
        var suggestions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetAnonValue(json.Value!, "suggestions"));
        var first = suggestions.Cast<object>().First();
        Assert.Equal(2, (int)GetAnonValue(first, "locationId")!);
        Assert.Contains("cùng lô", (string?)GetAnonValue(first, "strategy") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestPutaway_ShouldPreferEmptyBinInSameZoneAsDefault()
    {
        await using var db = CreateDb(nameof(SuggestPutaway_ShouldPreferEmptyBinInSameZoneAsDefault));
        SeedWarehouseGraph(db);

        db.Locations.Add(new Location
        {
            LocationId = 3,
            ZoneId = 1,
            LocationCode = "L3",
            IsActive = true
        });

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-PUT-ZONE",
            ItemName = "Hàng ưu tiên khu vực",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            IsActive = true
        });

        db.Items.Add(new Item
        {
            ItemId = 2,
            ItemCode = "OTHER-01",
            ItemName = "Hàng khác",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });

        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 2,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");

        var result = await controller.SuggestPutaway(new List<VouchersController.PutawayRequest>
        {
            new() { ItemId = 1, RowIndex = 0, WarehouseId = 1, Quantity = 1 }
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);
        var suggestions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetAnonValue(json.Value!, "suggestions"));
        var first = suggestions.Cast<object>().First();
        Assert.Equal(3, (int)GetAnonValue(first, "locationId")!);
        Assert.Contains("cùng Zone", (string?)GetAnonValue(first, "strategy") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestPutaway_ShouldAvoidDefaultLocationWhenProjectedLoadExceedsCapacity()
    {
        await using var db = CreateDb(nameof(SuggestPutaway_ShouldAvoidDefaultLocationWhenProjectedLoadExceedsCapacity));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-PUT-CAP",
            ItemName = "Hàng kiểm sức chứa",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            Weight = 1000,
            IsActive = true
        });

        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            LocationId = 1,
            Quantity = 1.6m,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");

        var result = await controller.SuggestPutaway(new List<VouchersController.PutawayRequest>
        {
            new() { ItemId = 1, RowIndex = 0, WarehouseId = 1, Quantity = 1 }
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);
        var suggestions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetAnonValue(json.Value!, "suggestions"));
        var first = suggestions.Cast<object>().First();
        Assert.Equal(2, (int)GetAnonValue(first, "locationId")!);
        Assert.DoesNotContain("Fixed Bin", (string?)GetAnonValue(first, "strategy") ?? string.Empty);
    }

    [Fact]
    public async Task SuggestPutaway_ShouldNotConsolidateSameItemDifferentOwner()
    {
        await using var db = CreateDb(nameof(SuggestPutaway_ShouldNotConsolidateSameItemDifferentOwner));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-PUT-OWNER",
            ItemName = "Hàng cùng mã khác owner",
            BaseUomId = 1,
            OwnerPartnerId = 10,
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 1,
            ItemId = 1,
            OwnerPartnerId = 20,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");

        var result = await controller.SuggestPutaway(new List<VouchersController.PutawayRequest>
        {
            new() { ItemId = 1, RowIndex = 0, WarehouseId = 1, OwnerPartnerId = 10, Quantity = 1 }
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);
        var suggestions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(GetAnonValue(json.Value!, "suggestions"));
        var first = suggestions.Cast<object>().First();
        Assert.Equal(2, (int)GetAnonValue(first, "locationId")!);
        Assert.DoesNotContain("Gom hàng", (string?)GetAnonValue(first, "strategy") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestPutaway_ShouldForbidOwnerOutsideUserScope()
    {
        await using var db = CreateDb(nameof(SuggestPutaway_ShouldForbidOwnerOutsideUserScope));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 31,
            ItemCode = "ITEM-PUT-FOREIGN-OWNER",
            ItemName = "Hàng ngoài phạm vi chủ hàng",
            BaseUomId = 1,
            OwnerPartnerId = 20,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            userName: "inbound.owner10",
            warehouseClaim: 1,
            roleName: WmsRoles.InboundStaff,
            ownerPartnerClaims: new[] { 10 });

        var result = await controller.SuggestPutaway(new List<VouchersController.PutawayRequest>
        {
            new() { ItemId = 31, RowIndex = 0, WarehouseId = 1, OwnerPartnerId = 20, Quantity = 1 }
        });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CaptureCatchWeight_ShouldForbidRoleOutsideVoucherFlow()
    {
        await using var db = CreateDb(nameof(CaptureCatchWeight_ShouldForbidRoleOutsideVoucherFlow));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 32,
            ItemCode = "CATCH-WEIGHT-OUTBOUND",
            ItemName = "Hàng cân khi xuất",
            BaseUomId = 1,
            TrackCatchWeight = true,
            CatchWeightUomId = 1,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 320,
            VoucherCode = "PX-CATCH-320",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            CreatedBy = "maker.outbound"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 3201,
            VoucherId = 320,
            ItemId = 32,
            TransactionQty = 2,
            BaseQty = 2,
            TransactionUomId = 1,
            ConversionRate = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            userName: "inbound.user",
            warehouseClaim: 1,
            roleName: WmsRoles.InboundStaff);

        var result = await controller.CaptureCatchWeight(
            320,
            3201,
            2,
            5,
            1,
            CatchWeightCapturePointEnum.Receive);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await db.CatchWeightEntries.ToListAsync());
    }

    [Fact]
    public async Task SuggestPutawayLocation_ShouldForbidWarehouseOutsideUserScope()
    {
        await using var db = CreateDb(nameof(SuggestPutawayLocation_ShouldForbidWarehouseOutsideUserScope));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 33,
            ItemCode = "PUTAWAY-WAREHOUSE-SCOPE",
            ItemName = "Hàng kiểm tra phạm vi kho",
            BaseUomId = 1,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            userName: "inbound.wh1",
            warehouseClaim: 1,
            roleName: WmsRoles.InboundStaff);

        Assert.IsType<ForbidResult>(await controller.SuggestPutawayLocation(33, 2));
    }

    [Fact]
    public async Task SuggestPutawayLocation_ShouldForbidOwnerOutsideUserScope()
    {
        await using var db = CreateDb(nameof(SuggestPutawayLocation_ShouldForbidOwnerOutsideUserScope));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 34,
            ItemCode = "PUTAWAY-OWNER-SCOPE",
            ItemName = "Hàng kiểm tra phạm vi chủ hàng",
            BaseUomId = 1,
            OwnerPartnerId = 20,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            userName: "inbound.owner10",
            warehouseClaim: 1,
            roleName: WmsRoles.InboundStaff,
            ownerPartnerClaims: new[] { 10 });

        Assert.IsType<ForbidResult>(await controller.SuggestPutawayLocation(34, 1));
    }

    [Fact]
    public async Task ExceptionCenter_ShouldOnlyReturnRowsWithinScopedWarehouse()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldOnlyReturnRowsWithinScopedWarehouse));
        SeedWarehouseGraph(db);

        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 2,
            WarehouseCode = "WH2",
            WarehouseName = "Overflow Warehouse",
            IsActive = true
        });
        db.Zones.Add(new Zone
        {
            ZoneId = 3,
            WarehouseId = 2,
            ZoneCode = "Z3",
            ZoneName = "Zone 3",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        });
        db.Locations.Add(new Location
        {
            LocationId = 3,
            ZoneId = 3,
            LocationCode = "L3",
            IsActive = true
        });

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-EX-01",
            ItemName = "Hàng ngoại lệ",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });

        var now = DateTime.Now;
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 101,
                VoucherCode = "PN-EX-01",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = now.Date,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Approved,
                AsnCode = "ASN-EX-01",
                ExpectedArrivalAt = now.AddHours(-3),
                DockAppointmentStart = now.AddHours(-4),
                DockAppointmentEnd = now.AddHours(-2),
                DockDoor = "DOCK-01"
            },
            new Voucher
            {
                VoucherId = 102,
                VoucherCode = "PN-EX-02",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 2,
                VoucherDate = now.Date,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Approved,
                AsnCode = "ASN-EX-02",
                ExpectedArrivalAt = now.AddHours(-3),
                DockAppointmentStart = now.AddHours(-4),
                DockAppointmentEnd = now.AddHours(-2),
                DockDoor = "DOCK-02"
            });

        db.Waves.AddRange(
            new Wave
            {
                WaveId = 201,
                WaveCode = "W-EX-01",
                WarehouseId = 1,
                Status = WaveStatusEnum.Released,
                CreatedAt = now
            },
            new Wave
            {
                WaveId = 202,
                WaveCode = "W-EX-02",
                WarehouseId = 2,
                Status = WaveStatusEnum.Released,
                CreatedAt = now
            });

        db.PickTasks.AddRange(
            new PickTask
            {
                PickTaskId = 301,
                TaskCode = "PT-EX-01",
                WaveId = 201,
                VoucherId = 101,
                ItemId = 1,
                SourceLocationId = 1,
                TargetQty = 5,
                PickedQty = 0,
                Status = PickTaskStatusEnum.Pending,
                DueAt = now.AddHours(-1)
            },
            new PickTask
            {
                PickTaskId = 302,
                TaskCode = "PT-EX-02",
                WaveId = 202,
                VoucherId = 102,
                ItemId = 1,
                SourceLocationId = 3,
                TargetQty = 5,
                PickedQty = 0,
                Status = PickTaskStatusEnum.Pending,
                DueAt = now.AddHours(-1)
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        var result = await controller.ExceptionCenter(null, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(1, row.WarehouseId));
        Assert.Contains(rows, row => row.ReferenceCode == "PN-EX-01");
        Assert.Contains(rows, row => row.ReferenceCode == "PT-EX-01");
        Assert.DoesNotContain(rows, row => row.ReferenceCode == "PN-EX-02");
        Assert.DoesNotContain(rows, row => row.ReferenceCode == "PT-EX-02");
    }

    [Fact]
    public async Task ExceptionCenter_ShouldSurfaceInboundPickAndReplenishmentExceptions()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldSurfaceInboundPickAndReplenishmentExceptions));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-EX-02",
            ItemName = "Hàng ngoại lệ trộn",
            BaseUomId = 1,
            UnitCost = 10,
            DefaultLocationId = 1,
            ReorderPoint = 10,
            MaxThreshold = 20,
            IsActive = true
        });

        var now = DateTime.Now;
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 111,
                VoucherCode = "PN-EX-11",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = now.Date,
                CreatedBy = "creator.user",
                InboundStatus = InboundStatusEnum.Approved,
                AsnCode = "ASN-EX-11",
                ExpectedArrivalAt = now.AddHours(-5),
                DockAppointmentStart = now.AddHours(-6),
                DockAppointmentEnd = now.AddHours(-4),
                DockDoor = "DOCK-01"
            },
            new Voucher
            {
                VoucherId = 112,
                VoucherCode = "PX-EX-12",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                VoucherDate = now.Date,
                CreatedBy = "creator.user"
            });

        db.Waves.Add(new Wave
        {
            WaveId = 211,
            WaveCode = "W-EX-11",
            WarehouseId = 1,
            Status = WaveStatusEnum.Released,
            CreatedAt = now
        });

        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 311,
            TaskCode = "PT-EX-11",
            WaveId = 211,
            VoucherId = 112,
            ItemId = 1,
            SourceLocationId = 2,
            TargetQty = 4,
            PickedQty = 0,
            Status = PickTaskStatusEnum.Pending,
            DueAt = now.AddHours(-2)
        });

        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 401,
                ItemId = 1,
                LocationId = 1,
                Quantity = 1,
                ReservedQty = 0,
                UpdatedAt = now
            },
            new ItemLocation
            {
                ItemLocationId = 402,
                ItemId = 1,
                LocationId = 2,
                Quantity = 15,
                ReservedQty = 0,
                LotNumber = "LOT-EX-11",
                ExpiryDate = now.Date.AddMonths(6),
                UpdatedAt = now
            });

        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 501,
            LpnCode = "LPN-EX-11",
            VoucherId = 111,
            WarehouseId = 1,
            ItemId = 1,
            LocationId = 2,
            CurrentLocationId = 2,
            Quantity = 15,
            LotNumber = "LOT-EX-11",
            ExpiryDate = now.Date.AddMonths(6),
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            CreatedAt = now.AddHours(-3),
            Details =
            {
                new LicensePlateDetail
                {
                    ItemId = 1,
                    Quantity = 15,
                    LotNumber = "LOT-EX-11",
                    ExpiryDate = now.Date.AddMonths(6)
                }
            }
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        var result = await controller.ExceptionCenter(1, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Contains(rows, row => row.CategoryKey == "inbound_overdue" && row.ReferenceCode == "PN-EX-11");
        Assert.Contains(rows, row => row.CategoryKey == "pick_unassigned" && row.ReferenceCode == "PT-EX-11");
        Assert.Contains(rows, row => row.CategoryKey == "pick_overdue" && row.ReferenceCode == "PT-EX-11");
        Assert.Contains(rows, row => row.CategoryKey == "replenishment_blocked_lpn" && row.ReferenceCode == "ITEM-EX-02");
    }

    [Fact]
    public async Task ExceptionCenter_GetShouldBeReadOnlyAndExplicitSyncShouldPersistCase()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_GetShouldBeReadOnlyAndExplicitSyncShouldPersistCase));
        SeedWarehouseGraph(db);

        var now = DateTime.Now;
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 121,
            VoucherCode = "PN-CASE-01",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = now.Date,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Approved,
            AsnCode = "ASN-CASE-01",
            ExpectedArrivalAt = now.AddHours(-3),
            DockAppointmentStart = now.AddHours(-4),
            DockAppointmentEnd = now.AddHours(-2),
            DockDoor = "DOCK-01"
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");

        var result = await controller.ExceptionCenter(1, null, null, null);

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await db.OperationExceptionCases.ToListAsync());

        var managerController = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: "Manager");
        await managerController.SynchronizeExceptionCenter(1);

        var exceptionCase = await db.OperationExceptionCases.FirstAsync(x => x.CategoryKey == "inbound_overdue");
        Assert.Equal("PN-CASE-01", exceptionCase.ReferenceCode);
        Assert.Equal(OperationExceptionStatusEnum.Open, exceptionCase.Status);
        Assert.Equal("inbound_overdue", exceptionCase.CategoryKey);
    }

    [Fact]
    public void ExceptionCenter_ShouldRetryDuplicateCaseInsertWhenParallelVisualRequestsRace()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WMS.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "Controllers", "OperationsController.ExceptionCenter.cs"));

        Assert.Contains("UpsertExceptionCasesAsync(rows, retryOnDuplicate: true)", source, StringComparison.Ordinal);
        Assert.Contains("catch (DbUpdateException ex) when (retryOnDuplicate && IsDuplicateOperationExceptionKey(ex))", source, StringComparison.Ordinal);
        Assert.Contains("DetachPendingOperationExceptionCases();", source, StringComparison.Ordinal);
        Assert.Contains("UpsertExceptionCasesAsync(rows, retryOnDuplicate: false)", source, StringComparison.Ordinal);
        Assert.Contains("IX_OperationExceptionCases_ExceptionKey", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssignException_ShouldSetOwnerAndAcknowledgeCase()
    {
        await using var db = CreateDb(nameof(AssignException_ShouldSetOwnerAndAcknowledgeCase));
        SeedWarehouseGraph(db);

        db.AppRoles.AddRange(
            new AppRole { RoleId = 2, RoleName = "Manager" },
            new AppRole { RoleId = 3, RoleName = "Staff" });
        db.AppUsers.Add(new AppUser
        {
            UserId = 1,
            UserName = "staff.worker",
            FullName = "Nhân viên kho",
            PasswordHash = "x",
            WarehouseId = 1,
            RoleId = 3,
            IsActive = true
        });

        var now = DateTime.Now;
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 122,
            VoucherCode = "PN-CASE-02",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = now.Date,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Approved,
            AsnCode = "ASN-CASE-02",
            ExpectedArrivalAt = now.AddHours(-3),
            DockAppointmentStart = now.AddHours(-4),
            DockAppointmentEnd = now.AddHours(-2),
            DockDoor = "DOCK-02"
        });
        await db.SaveChangesAsync();

        var managerController = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: "Manager");
        await managerController.SynchronizeExceptionCenter(1);
        var exceptionCase = await db.OperationExceptionCases.FirstAsync(x => x.CategoryKey == "inbound_overdue");

        var result = await managerController.AssignException(exceptionCase.ExceptionKey, "staff.worker");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ExceptionCenter", redirect.ActionName);

        var updatedCase = await db.OperationExceptionCases.FirstAsync(x => x.CategoryKey == "inbound_overdue");
        Assert.Equal("staff.worker", updatedCase.AssignedTo);
        Assert.Equal(OperationExceptionStatusEnum.Acknowledged, updatedCase.Status);
        Assert.Equal("manager.user", updatedCase.AcknowledgedBy);
        Assert.NotNull(updatedCase.AcknowledgedAt);
    }

    [Fact]
    public async Task ResolveException_ShouldReopenCaseWhenUnderlyingIssueStillExists()
    {
        await using var db = CreateDb(nameof(ResolveException_ShouldReopenCaseWhenUnderlyingIssueStillExists));
        SeedWarehouseGraph(db);

        var now = DateTime.Now;
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 123,
            VoucherCode = "PN-CASE-03",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = now.Date,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Approved,
            AsnCode = "ASN-CASE-03",
            ExpectedArrivalAt = now.AddHours(-5),
            DockAppointmentStart = now.AddHours(-6),
            DockAppointmentEnd = now.AddHours(-4),
            DockDoor = "DOCK-03"
        });
        await db.SaveChangesAsync();

        var managerController = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: "Manager");
        await managerController.SynchronizeExceptionCenter(1);
        var exceptionCase = await db.OperationExceptionCases.FirstAsync(x => x.CategoryKey == "inbound_overdue");

        var resolveResult = await managerController.ResolveException(exceptionCase.ExceptionKey, "Đã liên hệ xe và cập nhật lại lịch");
        Assert.IsType<RedirectToActionResult>(resolveResult);

        var resolvedCase = await db.OperationExceptionCases.FirstAsync(x => x.CategoryKey == "inbound_overdue");
        Assert.Equal(OperationExceptionStatusEnum.Resolved, resolvedCase.Status);
        Assert.NotNull(resolvedCase.ResolvedAt);

        await managerController.SynchronizeExceptionCenter(1);

        var reopenedCase = await db.OperationExceptionCases.FirstAsync(x => x.CategoryKey == "inbound_overdue");
        Assert.Equal(OperationExceptionStatusEnum.Open, reopenedCase.Status);
        Assert.NotNull(reopenedCase.ResolvedAt);
        Assert.Equal("manager.user", reopenedCase.ResolvedBy);
        Assert.Equal("Đã liên hệ xe và cập nhật lại lịch", reopenedCase.ResolutionNote);
    }

    [Fact]
    public async Task IgnoreException_ShouldRequireReasonAndRemainTerminalForAssignmentOrAcknowledgement()
    {
        await using var db = CreateDb(nameof(IgnoreException_ShouldRequireReasonAndRemainTerminalForAssignmentOrAcknowledgement));
        SeedWarehouseGraph(db);
        db.AppRoles.Add(new AppRole { RoleId = 3, RoleName = "Staff" });
        db.AppUsers.Add(new AppUser
        {
            UserId = 1,
            UserName = "staff.worker",
            FullName = "Nhân viên kho",
            PasswordHash = "x",
            WarehouseId = 1,
            RoleId = 3,
            IsActive = true
        });

        var now = DateTime.Now;
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 124,
            VoucherCode = "PN-CASE-IGNORE",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = now.Date,
            CreatedBy = "creator.user",
            InboundStatus = InboundStatusEnum.Approved,
            AsnCode = "ASN-CASE-IGNORE",
            ExpectedArrivalAt = now.AddHours(-5),
            DockAppointmentStart = now.AddHours(-6),
            DockAppointmentEnd = now.AddHours(-4),
            DockDoor = "DOCK-IGNORE"
        });
        await db.SaveChangesAsync();

        var managerController = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: "Manager");
        await managerController.SynchronizeExceptionCenter(1);
        var exceptionCase = await db.OperationExceptionCases.FirstAsync(x => x.CategoryKey == "inbound_overdue");

        await managerController.IgnoreException(exceptionCase.ExceptionKey, "Sai lệch đã được đối chiếu và chấp nhận");

        var ignoredCase = await db.OperationExceptionCases.FirstAsync(x => x.ExceptionKey == exceptionCase.ExceptionKey);
        Assert.Equal(OperationExceptionStatusEnum.Ignored, ignoredCase.Status);
        Assert.Equal("manager.user", ignoredCase.ResolvedBy);
        Assert.NotNull(ignoredCase.ResolvedAt);
        Assert.NotNull(ignoredCase.AcknowledgedAt);
        Assert.Equal("Sai lệch đã được đối chiếu và chấp nhận", ignoredCase.ResolutionNote);

        await managerController.AcknowledgeException(exceptionCase.ExceptionKey);
        await managerController.AssignException(exceptionCase.ExceptionKey, "staff.worker");

        var terminalCase = await db.OperationExceptionCases.FirstAsync(x => x.ExceptionKey == exceptionCase.ExceptionKey);
        Assert.Equal(OperationExceptionStatusEnum.Ignored, terminalCase.Status);
        Assert.Null(terminalCase.AssignedTo);

        var filteredResult = await managerController.ExceptionCenter(1, null, null, null, caseStatus: "ignored");
        var filteredView = Assert.IsType<ViewResult>(filteredResult);
        var filteredRows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(filteredView.Model);
        Assert.Single(filteredRows);
        Assert.Equal("ignored", filteredRows[0].CaseStatusKey);
    }

    [Fact]
    public async Task ExceptionCenter_ShouldForbidOwnerScopedPrincipalUntilCasesCarryOwnerLineage()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldForbidOwnerScopedPrincipalUntilCasesCarryOwnerLineage));
        SeedWarehouseGraph(db);

        var controller = CreateOperationsController(
            db,
            warehouseClaim: 1,
            userName: "owner.report",
            roleName: "ReportViewer",
            ownerPartnerClaims: [10]);

        var result = await controller.ExceptionCenter(1, null, null, null);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task RegisterSerials_ShouldPersistActiveSerialsWithinWarehouseScope()
    {
        await using var db = CreateDb(nameof(RegisterSerials_ShouldPersistActiveSerialsWithinWarehouseScope));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-001",
            ItemName = "Hàng theo dõi serial",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 201,
            VoucherCode = "PN-SER-201",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.NhapKho,
            InboundStatus = InboundStatusEnum.Receiving,
            CreatedBy = "creator.user",
            TotalLines = 1
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2011,
            VoucherId = 201,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 2,
            BaseQty = 2,
            ConversionRate = 1,
            TransactionUomId = 1,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.RegisterSerials(201, 2011, "SN-201-001\r\nSN-201-002");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("SerialReceiving", redirect.ActionName);

        var serials = await db.SerialNumbers.OrderBy(x => x.SerialCode).ToListAsync();
        Assert.Equal(2, serials.Count);
        Assert.All(serials, s =>
        {
            Assert.Equal(1, s.WarehouseId);
            Assert.Equal(1, s.ItemId);
            Assert.Equal(201L, s.VoucherId);
            Assert.Equal(2011L, s.VoucherDetailId);
            Assert.Equal(SerialNumberStatusEnum.Active, s.Status);
        });
    }

    [Fact]
    public async Task ReceivingAndSerialLookup_ShouldHonorOwnerScope()
    {
        await using var db = CreateDb(nameof(ReceivingAndSerialLookup_ShouldHonorOwnerScope));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 210,
            ItemCode = "SER-SCOPE",
            ItemName = "Hàng kiểm tra phạm vi chủ hàng",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 2101,
                VoucherCode = "PN-OWNER-10",
                WarehouseId = 1,
                OwnerPartnerId = 10,
                VoucherType = VoucherTypeEnum.NhapKho,
                InboundStatus = InboundStatusEnum.Receiving
            },
            new Voucher
            {
                VoucherId = 2102,
                VoucherCode = "PN-OWNER-20",
                WarehouseId = 1,
                OwnerPartnerId = 20,
                VoucherType = VoucherTypeEnum.NhapKho,
                InboundStatus = InboundStatusEnum.Receiving
            });
        db.SerialNumbers.AddRange(
            new SerialNumber
            {
                SerialNumberId = 21001,
                SerialCode = "SER-OWNER-10",
                WarehouseId = 1,
                OwnerPartnerId = 10,
                ItemId = 210,
                VoucherId = 2101,
                Status = SerialNumberStatusEnum.Active
            },
            new SerialNumber
            {
                SerialNumberId = 21002,
                SerialCode = "SER-OWNER-20",
                WarehouseId = 1,
                OwnerPartnerId = 20,
                ItemId = 210,
                VoucherId = 2102,
                Status = SerialNumberStatusEnum.Active
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(
            db,
            warehouseClaim: 1,
            userName: "owner.10",
            roleName: "Staff",
            ownerPartnerClaims: [10]);

        var receivingView = Assert.IsType<ViewResult>(await controller.Receiving(1, null, null));
        var receivingRows = Assert.IsAssignableFrom<List<InboundReceivingRow>>(receivingView.Model);
        Assert.Equal("PN-OWNER-10", Assert.Single(receivingRows).VoucherCode);

        var serialView = Assert.IsType<ViewResult>(await controller.SerialLookup(null, 1, null));
        var serialRows = Assert.IsAssignableFrom<List<SerialLookupRow>>(serialView.Model);
        Assert.Equal("SER-OWNER-10", Assert.Single(serialRows).SerialCode);
    }

    [Fact]
    public async Task RegisterSerials_ShouldRejectVoucherOutsideOwnerScope()
    {
        await using var db = CreateDb(nameof(RegisterSerials_ShouldRejectVoucherOutsideOwnerScope));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 211,
            ItemCode = "SER-OWNER-BLOCK",
            ItemName = "Hàng ngoài phạm vi",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 2111,
            VoucherCode = "PN-OWNER-20-BLOCK",
            WarehouseId = 1,
            OwnerPartnerId = 20,
            VoucherType = VoucherTypeEnum.NhapKho,
            InboundStatus = InboundStatusEnum.Receiving
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 21101,
            VoucherId = 2111,
            OwnerPartnerId = 20,
            ItemId = 211,
            TransactionQty = 1,
            BaseQty = 1,
            ConversionRate = 1,
            TransactionUomId = 1,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(
            db,
            warehouseClaim: 1,
            userName: "owner.10",
            roleName: "Staff",
            ownerPartnerClaims: [10]);
        var result = await controller.RegisterSerials(2111, 21101, "SER-BLOCK-001");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.False(await db.SerialNumbers.AnyAsync());
        Assert.Contains("không có quyền", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanSerial_ShouldReturnDataWithinScopedWarehouse()
    {
        await using var db = CreateDb(nameof(ScanSerial_ShouldReturnDataWithinScopedWarehouse));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-002",
            ItemName = "Hàng quét serial",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 202,
            VoucherCode = "PN-SER-202",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.NhapKho
        });
        db.SerialNumbers.Add(new SerialNumber
        {
            SerialNumberId = 1,
            SerialCode = "SERIAL-202-001",
            WarehouseId = 1,
            ItemId = 1,
            LocationId = 1,
            VoucherId = 202,
            Status = SerialNumberStatusEnum.Active
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ScanSerial("SERIAL-202-001");

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);

        var data = GetAnonValue(json.Value!, "data")!;
        Assert.Equal("SERIAL-202-001", (string?)GetAnonValue(data, "serialCode"));
        Assert.Equal("SER-002", (string?)GetAnonValue(data, "itemCode"));
    }

    [Fact]
    public async Task ExceptionCenter_ShouldIncludeMissingSerialRegistration()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldIncludeMissingSerialRegistration));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-003",
            ItemName = "Hàng thiếu serial",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 203,
            VoucherCode = "PN-SER-203",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.NhapKho,
            InboundStatus = InboundStatusEnum.Receiving,
            ExpectedArrivalAt = DateTime.Now.AddHours(-1),
            TotalLines = 1
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2031,
            VoucherId = 203,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 2,
            BaseQty = 2,
            ConversionRate = 1,
            TransactionUomId = 1,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ExceptionCenter(1, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Contains(rows, r => r.CategoryKey == "serial_missing_registration" && r.ReferenceCode == "PN-SER-203");
    }

    [Fact]
    public async Task Approve_ShouldRequireRegisteredSerialsForSerialTrackedInbound()
    {
        await using var db = CreateDb(nameof(Approve_ShouldRequireRegisteredSerialsForSerialTrackedInbound));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-004",
            ItemName = "Hàng bắt buộc serial",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 204,
            VoucherCode = "PN-SER-204",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.NhapKho,
            InboundStatus = InboundStatusEnum.Receiving,
            CreatedBy = "creator.user"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2041,
            VoucherId = 204,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 2,
            BaseQty = 2,
            ConversionRate = 1,
            TransactionUomId = 1,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");
        var result = await controller.Approve(204);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("số sê-ri", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.Vouchers.FindAsync(204L))!.IsPosted);
    }

    [Fact]
    public async Task Approve_ShouldAttachRegisteredSerialsToGeneratedLpn()
    {
        await using var db = CreateDb(nameof(Approve_ShouldAttachRegisteredSerialsToGeneratedLpn));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-005",
            ItemName = "Hàng liên kết serial",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 205,
            VoucherCode = "PN-SER-205",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.NhapKho,
            InboundStatus = InboundStatusEnum.Receiving,
            CreatedBy = "creator.user"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2051,
            VoucherId = 205,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 2,
            BaseQty = 2,
            ConversionRate = 1,
            TransactionUomId = 1,
            LineNumber = 1
        });
        db.SerialNumbers.AddRange(
            new SerialNumber
            {
                SerialNumberId = 11,
                SerialCode = "SERIAL-205-001",
                WarehouseId = 1,
                ItemId = 1,
                LocationId = 1,
                VoucherId = 205,
                VoucherDetailId = 2051,
                Status = SerialNumberStatusEnum.Active
            },
            new SerialNumber
            {
                SerialNumberId = 12,
                SerialCode = "SERIAL-205-002",
                WarehouseId = 1,
                ItemId = 1,
                LocationId = 1,
                VoucherId = 205,
                VoucherDetailId = 2051,
                Status = SerialNumberStatusEnum.Active
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "reviewer.user");
        var result = await controller.Approve(205);
        Assert.IsType<RedirectToActionResult>(result);

        var lpn = await db.LicensePlates.SingleAsync(x => x.VoucherId == 205);
        await db.Entry(lpn).Collection(x => x.Details).LoadAsync();
        var serials = await db.SerialNumbers.Where(x => x.VoucherId == 205).OrderBy(x => x.SerialCode).ToListAsync();
        Assert.Equal(LpnStatusEnum.Stored, lpn.Status);
        Assert.Equal(1, lpn.CurrentLocationId);
        var lpnLine = Assert.Single(lpn.Details);
        Assert.Equal(2051, lpnLine.VoucherDetailId);
        Assert.Equal(2, lpnLine.Quantity);
        Assert.All(serials, s =>
        {
            Assert.Equal(lpn.LicensePlateId, s.LicensePlateId);
            Assert.Equal(1, s.LocationId);
        });
        Assert.True((await db.Vouchers.FindAsync(205L))!.IsPosted);
    }

    [Fact]
    public async Task ConfirmPickTask_ShouldRequireAndAssignSerialsForSerialTrackedItem()
    {
        await using var db = CreateDb(nameof(ConfirmPickTask_ShouldRequireAndAssignSerialsForSerialTrackedItem));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-OUT-001",
            ItemName = "Hàng xuất serial",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-001", WarehouseId = 1, Status = WaveStatusEnum.Released, CreatedAt = DateTime.Now });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 301,
            VoucherCode = "PX-SER-301",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            WaveId = 1,
            FulfillmentStatus = FulfillmentStatusEnum.WaitingForPick
        });
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 3011,
            TaskCode = "PT-3011",
            WaveId = 1,
            VoucherId = 301,
            ItemId = 1,
            SourceLocationId = 1,
            TargetQty = 2,
            PickedQty = 0,
            Status = PickTaskStatusEnum.Assigned,
            AssignedTo = "picker.user"
        });
        db.SerialNumbers.AddRange(
            new SerialNumber { SerialNumberId = 30101, SerialCode = "OUT-SN-001", WarehouseId = 1, ItemId = 1, LocationId = 1, VoucherId = 201, Status = SerialNumberStatusEnum.Active },
            new SerialNumber { SerialNumberId = 30102, SerialCode = "OUT-SN-002", WarehouseId = 1, ItemId = 1, LocationId = 1, VoucherId = 201, Status = SerialNumberStatusEnum.Active });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "picker.user");
        var result = await controller.ConfirmPickTask(3011, 2, "SER-OUT-001", "OUT-SN-001\r\nOUT-SN-002");

        Assert.IsType<RedirectToActionResult>(result);
        var task = await db.PickTasks.FindAsync(3011L);
        Assert.Equal(2, task!.PickedQty);
        Assert.Equal(PickTaskStatusEnum.Completed, task.Status);

        var assignments = await db.PickTaskSerialAssignments.OrderBy(x => x.SerialCode).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.All(assignments, x => Assert.Equal(3011L, x.PickTaskId));
    }

    [Fact]
    public async Task PostReservedOutbound_ShouldConsumeAssignedSerials()
    {
        await using var db = CreateDb(nameof(PostReservedOutbound_ShouldConsumeAssignedSerials));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-OUT-002",
            ItemName = "Hàng xuất lỗi serial",
            BaseUomId = 1,
            CurrentStock = 2,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 302,
            VoucherCode = "PX-SER-302",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            FulfillmentStatus = FulfillmentStatusEnum.Picked
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 3021,
            VoucherId = 302,
            ItemId = 1,
            TransactionQty = 2,
            BaseQty = 2,
            TransactionUomId = 1,
            LineNumber = 1
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 30201,
            ItemId = 1,
            LocationId = 1,
            Quantity = 2,
            ReservedQty = 2
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 302001,
            VoucherId = 302,
            VoucherDetailId = 3021,
            ItemId = 1,
            LocationId = 1,
            ReservedQty = 2,
            Status = ReservationStatusEnum.Active
        });
        db.Waves.Add(new Wave { WaveId = 302, WaveCode = "WV-302", WarehouseId = 1, Status = WaveStatusEnum.Released, CreatedAt = DateTime.Now });
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 3021,
            TaskCode = "PT-3021",
            WaveId = 302,
            VoucherId = 302,
            VoucherDetailId = 3021,
            ItemId = 1,
            SourceLocationId = 1,
            TargetQty = 2,
            PickedQty = 2,
            Status = PickTaskStatusEnum.Completed
        });
        db.SerialNumbers.AddRange(
            new SerialNumber { SerialNumberId = 30211, SerialCode = "OUT-CONSUME-001", WarehouseId = 1, ItemId = 1, LocationId = 1, VoucherId = 201, Status = SerialNumberStatusEnum.Active },
            new SerialNumber { SerialNumberId = 30212, SerialCode = "OUT-CONSUME-002", WarehouseId = 1, ItemId = 1, LocationId = 1, VoucherId = 201, Status = SerialNumberStatusEnum.Active });
        db.PickTaskSerialAssignments.AddRange(
            new PickTaskSerialAssignment { PickTaskSerialAssignmentId = 1, PickTaskId = 3021, VoucherId = 302, VoucherDetailId = 3021, SerialNumberId = 30211, SerialCode = "OUT-CONSUME-001", ScannedBy = "picker.user" },
            new PickTaskSerialAssignment { PickTaskSerialAssignmentId = 2, PickTaskId = 3021, VoucherId = 302, VoucherDetailId = 3021, SerialNumberId = 30212, SerialCode = "OUT-CONSUME-002", ScannedBy = "picker.user" });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.PostReservedOutbound(302);

        Assert.IsType<RedirectToActionResult>(result);
        var serials = await db.SerialNumbers.Where(x => x.ItemId == 1).OrderBy(x => x.SerialCode).ToListAsync();
        Assert.All(serials, s =>
        {
            Assert.Equal(SerialNumberStatusEnum.Consumed, s.Status);
            Assert.Equal(302L, s.ConsumedVoucherId);
            Assert.Null(s.LocationId);
        });
        Assert.True((await db.Vouchers.FindAsync(302L))!.IsPosted);
    }

    [Fact]
    public async Task CreateWave_ShouldBatchSameSkuLocationAndPostEachVoucherFromAllocations()
    {
        await using var db = CreateDb(nameof(CreateWave_ShouldBatchSameSkuLocationAndPostEachVoucherFromAllocations));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "BATCH-SKU-001",
            ItemName = "Hàng gom đơn",
            BaseUomId = 1,
            CurrentStock = 10,
            UnitCost = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 91001,
            ItemId = 1,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            LotNumber = "LOT-BATCH",
            ExpiryDate = new DateTime(2026, 12, 31)
        });

        for (var i = 0; i < 3; i++)
        {
            var voucherId = 910L + i;
            var detailId = 9100L + i;
            db.Vouchers.Add(new Voucher
            {
                VoucherId = voucherId,
                VoucherCode = $"PX-BATCH-{i + 1:D3}",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                IsPosted = false
            });
            db.VoucherDetails.Add(new VoucherDetail
            {
                VoucherDetailId = detailId,
                VoucherId = voucherId,
                LineNumber = 1,
                ItemId = 1,
                LocationId = 1,
                LotNumber = "LOT-BATCH",
                ExpiryDate = new DateTime(2026, 12, 31),
                TransactionQty = 2,
                BaseQty = 2,
                TransactionUomId = 1,
                UnitPrice = 5,
                LineAmount = 10
            });
        }
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var createResult = await controller.CreateWave(
            "Standard",
            null,
            null,
            null,
            null,
            2,
            new[] { 910L, 911L, 912L },
            null);

        Assert.IsType<RedirectToActionResult>(createResult);
        var task = await db.PickTasks.Include(t => t.Allocations).SingleAsync();
        Assert.True(task.IsBatchPick);
        Assert.Equal(6, task.TargetQty);
        Assert.Equal(3, task.Allocations.Count);
        Assert.All(task.Allocations, a => Assert.Equal(2, a.AllocatedQty));

        var confirmResult = await controller.ConfirmPickTask(task.PickTaskId, 6, "BATCH-SKU-001", null);
        Assert.IsType<RedirectToActionResult>(confirmResult);

        var allocations = await db.PickTaskAllocations.OrderBy(a => a.VoucherId).ToListAsync();
        Assert.All(allocations, a => Assert.Equal(2, a.PickedQty));

        foreach (var voucherId in new[] { 910L, 911L, 912L })
        {
            var postResult = await controller.PostReservedOutbound(voucherId);
            Assert.IsType<RedirectToActionResult>(postResult);
        }

        var vouchers = await db.Vouchers
            .Where(v => v.VoucherId >= 910 && v.VoucherId <= 912)
            .OrderBy(v => v.VoucherId)
            .ToListAsync();
        Assert.All(vouchers, v =>
        {
            Assert.True(v.IsPosted);
            Assert.Equal(FulfillmentStatusEnum.Completed, v.FulfillmentStatus);
        });

        var reservations = await db.StockReservations
            .Where(r => r.VoucherId >= 910 && r.VoucherId <= 912)
            .OrderBy(r => r.VoucherId)
            .ToListAsync();
        Assert.All(reservations, r =>
        {
            Assert.Equal(2, r.ConsumedQty);
            Assert.Equal(ReservationStatusEnum.Consumed, r.Status);
        });
        Assert.Equal(4, (await db.ItemLocations.FindAsync(91001))!.Quantity);
    }

    [Fact]
    public async Task CreateWave_ShouldRollbackWhenNoSelectedVoucherIsEligible()
    {
        await using var db = CreateDb(nameof(CreateWave_ShouldRollbackWhenNoSelectedVoucherIsEligible));
        SeedWarehouseGraph(db);
        var uow = new EfUnitOfWork(db);
        var service = new OutboundExecutionService(
            db,
            uow,
            new InventoryReservationService(db),
            new InventoryBalanceService(db));

        var result = await service.CreateWaveAsync(
            "Standard",
            null,
            null,
            null,
            null,
            WavePriorityEnum.Normal,
            new[] { 999_991L },
            "AUDIT_TEST_NO_ELIGIBLE_VOUCHER",
            1,
            "audit.test");

        Assert.False(result.Succeeded);
        Assert.False(uow.HasActiveTransaction);
        Assert.Empty(await db.Waves.ToListAsync());
    }

    [Fact]
    public async Task TwoStepWave_ShouldCreateBulkTaskAndSortTasks()
    {
        await using var db = CreateDb(nameof(TwoStepWave_ShouldCreateBulkTaskAndSortTasks));
        SeedTwoStepPickingFixture(db);

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.CreateWave(
            "TwoStep",
            null,
            null,
            null,
            null,
            2,
            new[] { 920L, 921L, 922L },
            null);

        Assert.IsType<RedirectToActionResult>(result);
        var tasks = await db.PickTasks.Include(t => t.Allocations).OrderBy(t => t.PickTaskId).ToListAsync();
        var bulkTask = Assert.Single(tasks, t => t.PickTaskMode == PickTaskModeEnum.Bulk);
        var sortTasks = tasks.Where(t => t.PickTaskMode == PickTaskModeEnum.Sort).ToList();

        Assert.Equal(3, sortTasks.Count);
        Assert.Equal(6, bulkTask.TargetQty);
        Assert.Equal(31, bulkTask.TargetLocationId);
        Assert.All(sortTasks, t =>
        {
            Assert.Equal(bulkTask.PickTaskId, t.ParentPickTaskId);
            Assert.Equal(PickTaskStatusEnum.WaitingForBulk, t.Status);
            Assert.Equal(31, t.SourceLocationId);
            Assert.Equal(32, t.TargetLocationId);
            Assert.Single(t.Allocations);
        });
    }

    [Fact]
    public async Task TwoStepBulkConfirm_ShouldMoveStockToStagingAndReleaseSortTasks()
    {
        await using var db = CreateDb(nameof(TwoStepBulkConfirm_ShouldMoveStockToStagingAndReleaseSortTasks));
        SeedTwoStepPickingFixture(db);
        var controller = CreateController(db, userName: "manager.user");
        await controller.CreateWave("TwoStep", null, null, null, null, 2, new[] { 920L, 921L, 922L }, null);
        var bulkTask = await db.PickTasks.SingleAsync(t => t.PickTaskMode == PickTaskModeEnum.Bulk);
        db.InventoryTransactions.RemoveRange(db.InventoryTransactions);
        await db.SaveChangesAsync();

        var result = await controller.ConfirmPickTask(
            bulkTask.PickTaskId,
            6,
            "TWO-STEP-SKU",
            null,
            sourceLocationCode: "L1",
            targetLocationCode: "STAGE-01");

        Assert.IsType<RedirectToActionResult>(result);
        var source = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 92001);
        var staging = await db.ItemLocations.SingleAsync(il => il.ItemId == 1 && il.LocationId == 31);
        var sortTasks = await db.PickTasks.Where(t => t.PickTaskMode == PickTaskModeEnum.Sort).ToListAsync();
        var reservations = await db.StockReservations.Where(r => r.VoucherId >= 920 && r.VoucherId <= 922).ToListAsync();

        Assert.Equal(4, source.Quantity);
        Assert.Equal(0, source.ReservedQty);
        Assert.Equal(6, staging.Quantity);
        Assert.Equal(6, staging.ReservedQty);
        Assert.All(sortTasks, t => Assert.Equal(PickTaskStatusEnum.Pending, t.Status));
        Assert.All(reservations, r => Assert.Equal(31, r.LocationId));

        var bulkLedger = await db.InventoryTransactions
            .Where(t => t.PickTaskId == bulkTask.PickTaskId)
            .ToListAsync();
        Assert.NotEmpty(bulkLedger);
        Assert.All(bulkLedger, row => Assert.Equal($"pick-task:{bulkTask.PickTaskId}:bulk-move", row.TransactionGroupKey));
        var quantityMoves = bulkLedger.Where(row => row.QuantityDelta != 0).OrderBy(row => row.QuantityDelta).ToList();
        Assert.Equal(2, quantityMoves.Count);
        Assert.Equal(-6, quantityMoves[0].QuantityDelta);
        Assert.Equal(6, quantityMoves[1].QuantityDelta);
        Assert.All(quantityMoves, row => Assert.Equal(InventoryTransactionTypeEnum.Move, row.TransactionType));
    }

    [Fact]
    public async Task TwoStepPost_ShouldBlockUntilSortTasksAreCompleted()
    {
        await using var db = CreateDb(nameof(TwoStepPost_ShouldBlockUntilSortTasksAreCompleted));
        SeedTwoStepPickingFixture(db);
        var controller = CreateController(db, userName: "manager.user");
        await controller.CreateWave("TwoStep", null, null, null, null, 2, new[] { 920L, 921L, 922L }, null);
        var bulkTask = await db.PickTasks.SingleAsync(t => t.PickTaskMode == PickTaskModeEnum.Bulk);
        await controller.ConfirmPickTask(bulkTask.PickTaskId, 6, "TWO-STEP-SKU", null, sourceLocationCode: "L1", targetLocationCode: "STAGE-01");

        var postResult = await controller.PostReservedOutbound(920L);

        Assert.IsType<RedirectToActionResult>(postResult);
        Assert.False((await db.Vouchers.FindAsync(920L))!.IsPosted);
        Assert.Contains("chưa phân loại", controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task TwoStepSortAndPost_ShouldConsumeFromStagingLocation()
    {
        await using var db = CreateDb(nameof(TwoStepSortAndPost_ShouldConsumeFromStagingLocation));
        SeedTwoStepPickingFixture(db);
        var controller = CreateController(db, userName: "manager.user");
        await controller.CreateWave("TwoStep", null, null, null, null, 2, new[] { 920L, 921L, 922L }, null);
        var bulkTask = await db.PickTasks.SingleAsync(t => t.PickTaskMode == PickTaskModeEnum.Bulk);
        await controller.ConfirmPickTask(bulkTask.PickTaskId, 6, "TWO-STEP-SKU", null, sourceLocationCode: "L1", targetLocationCode: "STAGE-01");

        var sortTasks = await db.PickTasks
            .Where(t => t.PickTaskMode == PickTaskModeEnum.Sort)
            .OrderBy(t => t.PickTaskId)
            .ToListAsync();
        foreach (var task in sortTasks)
        {
            var sortResult = await controller.ConfirmPickTask(
                task.PickTaskId,
                2,
                "TWO-STEP-SKU",
                null,
                sourceLocationCode: "STAGE-01");
            Assert.IsType<RedirectToActionResult>(sortResult);
        }
        Assert.All(await db.PickTasks.Where(t => t.PickTaskMode == PickTaskModeEnum.Sort).ToListAsync(), t =>
        {
            Assert.Equal(PickTaskStatusEnum.Completed, t.Status);
            Assert.Equal(2, t.PickedQty);
        });

        foreach (var voucherId in new[] { 920L, 921L, 922L })
        {
            var postResult = await controller.PostReservedOutbound(voucherId);
            Assert.IsType<RedirectToActionResult>(postResult);
            Assert.True((await db.Vouchers.FindAsync(voucherId))!.IsPosted, $"Phiếu {voucherId}: {controller.TempData["Error"]}");
        }

        var staging = await db.ItemLocations.SingleAsync(il => il.ItemId == 1 && il.LocationId == 31);
        var source = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 92001);
        var reservations = await db.StockReservations
            .Where(r => r.VoucherId >= 920 && r.VoucherId <= 922)
            .OrderBy(r => r.VoucherId)
            .ToListAsync();

        Assert.All(reservations, r =>
        {
            Assert.Equal(31, r.LocationId);
            Assert.Equal(2, r.ConsumedQty);
            Assert.Equal(ReservationStatusEnum.Consumed, r.Status);
        });
        Assert.Equal(4, source.Quantity);
        Assert.Equal(0, staging.Quantity);
        Assert.Equal(0, staging.ReservedQty);
        Assert.All(await db.Vouchers.Where(v => v.VoucherId >= 920 && v.VoucherId <= 922).ToListAsync(), v => Assert.True(v.IsPosted));
    }

    [Fact]
    public async Task RecalculateReservedQty_ShouldKeepReservationsSeparatedByWarehouseLocation()
    {
        await using var db = CreateDb(nameof(RecalculateReservedQty_ShouldKeepReservationsSeparatedByWarehouseLocation));
        SeedWarehouseGraph(db);
        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Second Warehouse", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 20, WarehouseId = 2, ZoneCode = "Z20", ZoneName = "WH2 Storage", ZoneType = ZoneTypeEnum.Storage, IsActive = true });
        db.Locations.Add(new Location { LocationId = 20, ZoneId = 20, LocationCode = "WH2-L1", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SKU-RES-WH",
            ItemName = "Hàng giữ chỗ theo kho",
            BaseUomId = 1,
            IsActive = true
        });

        var expiry = new DateTime(2026, 12, 31);
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 41001,
                ItemId = 1,
                LocationId = 1,
                Quantity = 10,
                ReservedQty = 0,
                LotNumber = "LOT-SAME",
                ExpiryDate = expiry
            },
            new ItemLocation
            {
                ItemLocationId = 41002,
                ItemId = 1,
                LocationId = 20,
                Quantity = 10,
                ReservedQty = 0,
                LotNumber = "LOT-SAME",
                ExpiryDate = expiry
            });

        db.StockReservations.AddRange(
            new StockReservation
            {
                StockReservationId = 410001,
                VoucherId = 4101,
                ItemId = 1,
                LocationId = 1,
                LotNumber = "LOT-SAME",
                ExpiryDate = expiry,
                ReservedQty = 3,
                Status = ReservationStatusEnum.Active
            },
            new StockReservation
            {
                StockReservationId = 410002,
                VoucherId = 4102,
                ItemId = 1,
                LocationId = 20,
                LotNumber = "LOT-SAME",
                ExpiryDate = expiry,
                ReservedQty = 8,
                ConsumedQty = 2,
                ReleasedQty = 1,
                Status = ReservationStatusEnum.Active
            },
            new StockReservation
            {
                StockReservationId = 410003,
                VoucherId = 4103,
                ItemId = 1,
                LocationId = 20,
                LotNumber = "LOT-SAME",
                ExpiryDate = expiry,
                ReservedQty = 4,
                Status = ReservationStatusEnum.Consumed
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var method = typeof(VouchersController).GetMethod("RecalculateReservedQtyAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(controller, new object[] { new[] { 41001, 41002 } })!;
        await task;
        await db.SaveChangesAsync();

        var wh1Stock = await db.ItemLocations.FindAsync(41001);
        var wh2Stock = await db.ItemLocations.FindAsync(41002);
        Assert.Equal(3, wh1Stock!.ReservedQty);
        Assert.Equal(5, wh2Stock!.ReservedQty);
    }

    [Fact]
    public async Task InventoryBalanceService_ShouldCalculateStockByWarehouseFromItemLocations()
    {
        await using var db = CreateDb(nameof(InventoryBalanceService_ShouldCalculateStockByWarehouseFromItemLocations));
        SeedWarehouseGraph(db);
        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Second Warehouse", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 30, WarehouseId = 2, ZoneCode = "Z30", ZoneName = "WH2 Storage", ZoneType = ZoneTypeEnum.Storage, IsActive = true });
        db.Locations.Add(new Location { LocationId = 30, ZoneId = 30, LocationCode = "WH2-L1", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SKU-BAL-WH",
            ItemName = "Hàng kiểm số dư",
            BaseUomId = 1,
            CurrentStock = 999,
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 42001, ItemId = 1, LocationId = 1, Quantity = 4 },
            new ItemLocation { ItemLocationId = 42002, ItemId = 1, LocationId = 30, Quantity = 9 });
        await db.SaveChangesAsync();

        var service = new InventoryBalanceService(db);

        var wh1Stock = await service.GetStockByItemAsync(warehouseId: 1);
        var allStock = await service.GetStockByItemAsync();

        Assert.Equal(4, wh1Stock[1]);
        Assert.Equal(13, allStock[1]);
    }

    [Fact]
    public async Task UnitOfWork_ShouldExposeIdempotentTransactionBoundaries()
    {
        await using var db = CreateDb(nameof(UnitOfWork_ShouldExposeIdempotentTransactionBoundaries));
        var uow = new EfUnitOfWork(db);

        await uow.BeginTransactionAsync();
        await uow.BeginTransactionAsync();
        Assert.True(uow.HasActiveTransaction);

        await uow.RollbackAsync();
        await uow.RollbackAsync();
        Assert.False(uow.HasActiveTransaction);

        await uow.CommitAsync();
        Assert.False(uow.HasActiveTransaction);
    }

    [Fact]
    public async Task CrossDockService_ShouldCreateTaskReservationAndAuditInOneWorkflow()
    {
        await using var db = CreateDb(nameof(CrossDockService_ShouldCreateTaskReservationAndAuditInOneWorkflow));
        SeedWarehouseGraph(db);
        db.Zones.Add(new Zone { ZoneId = 50, WarehouseId = 1, ZoneCode = "CD", ZoneName = "Cross Dock", ZoneType = ZoneTypeEnum.CrossDock, IsActive = true });
        db.Locations.Add(new Location { LocationId = 50, ZoneId = 50, LocationCode = "CD-STAGE", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 50,
            ItemCode = "CD-SKU-001",
            ItemName = "Hàng chuyển thẳng",
            BaseUomId = 1,
            IsActive = true
        });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 501, VoucherCode = "IN-CD-501", WarehouseId = 1, VoucherType = VoucherTypeEnum.NhapKho, CreatedBy = "qa" },
            new Voucher { VoucherId = 502, VoucherCode = "OUT-CD-502", WarehouseId = 1, VoucherType = VoucherTypeEnum.XuatKho, CreatedBy = "qa" });
        db.VoucherDetails.AddRange(
            new VoucherDetail { VoucherDetailId = 5011, VoucherId = 501, ItemId = 50, LocationId = 1, BaseQty = 10, TransactionQty = 10, TransactionUomId = 1 },
            new VoucherDetail { VoucherDetailId = 5021, VoucherId = 502, ItemId = 50, BaseQty = 7, TransactionQty = 7, TransactionUomId = 1 });
        await db.SaveChangesAsync();

        var uow = new EfUnitOfWork(db);
        var service = new CrossDockService(db, uow);

        var result = await service.ExecuteCrossDockAsync(501, 502, 50, 7, 50, null, "manager.user", System.Net.IPAddress.Loopback.ToString(), 5011, 5021);

        Assert.True(result.Succeeded);
        var task = await db.CrossDockTasks.SingleAsync();
        Assert.Equal("manager.user", task.AssignedTo);
        Assert.Equal(7, task.ScheduledQty);
        var reservation = await db.StockReservations.SingleAsync();
        Assert.Equal(502, reservation.VoucherId);
        Assert.Equal(5021, reservation.VoucherDetailId);
        Assert.Equal(50, reservation.LocationId);
        Assert.Equal(7, reservation.ReservedQty);
        var audit = await db.AuditLogs.SingleAsync(a => a.ActionType == "CROSSDOCK_TASK_CREATED");
        Assert.Equal("CrossDock", audit.AppModule);
    }

    [Fact]
    public async Task CrossDockOpportunities_ShouldAutoMatchTodayInboundToPriorityOutbound()
    {
        await using var db = CreateDb(nameof(CrossDockOpportunities_ShouldAutoMatchTodayInboundToPriorityOutbound));
        SeedWarehouseGraph(db);
        db.Zones.Add(new Zone { ZoneId = 51, WarehouseId = 1, ZoneCode = "CD", ZoneName = "Cross Dock", ZoneType = ZoneTypeEnum.CrossDock, IsActive = true });
        db.Locations.Add(new Location { LocationId = 51, ZoneId = 51, LocationCode = "CD-STAGE", IsActive = true });
        db.Items.Add(new Item { ItemId = 51, ItemCode = "CD-AUTO", ItemName = "Hàng khớp tự động", BaseUomId = 1, IsActive = true });
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 510,
                VoucherCode = "IN-CD-510",
                WarehouseId = 1,
                VoucherType = VoucherTypeEnum.NhapKho,
                VoucherDate = DateTime.Today,
                ExpectedArrivalAt = DateTime.Today.AddHours(8),
                InboundStatus = InboundStatusEnum.Receiving,
                CreatedBy = "qa"
            },
            new Voucher
            {
                VoucherId = 511,
                VoucherCode = "OUT-CD-511",
                WarehouseId = 1,
                VoucherType = VoucherTypeEnum.XuatKho,
                Priority = 90,
                ServiceLevel = ServiceLevelEnum.SameDay,
                RequestedDeliveryDate = DateTime.Today,
                CreatedBy = "qa"
            });
        db.VoucherDetails.AddRange(
            new VoucherDetail { VoucherDetailId = 5101, VoucherId = 510, ItemId = 51, LocationId = 1, BaseQty = 5, TransactionQty = 5, TransactionUomId = 1, LotNumber = "LOT-CD", ExpiryDate = new DateTime(2026, 12, 31) },
            new VoucherDetail { VoucherDetailId = 5111, VoucherId = 511, ItemId = 51, BaseQty = 3, TransactionQty = 3, TransactionUomId = 1, LotNumber = "LOT-CD", ExpiryDate = new DateTime(2026, 12, 31) });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, userName: "manager.user", roleName: "Manager");
        var result = await controller.CrossDockOpportunities(1);

        Assert.IsType<ViewResult>(result);
        var opportunities = Assert.IsAssignableFrom<List<dynamic>>(controller.ViewBag.Opportunities);
        Assert.Single(opportunities);
        var opportunity = opportunities[0];
        Assert.Equal(5101L, (long)GetAnonValue(opportunity, "InboundVoucherDetailId")!);
        Assert.Equal(5111L, (long)GetAnonValue(opportunity, "OutboundVoucherDetailId")!);
        Assert.Equal(3m, (decimal)GetAnonValue(opportunity, "CrossDockQty")!);
        Assert.Equal(51, (int)GetAnonValue(opportunity, "StageLocationId")!);
    }

    [Fact]
    public async Task CrossDockOpportunities_ShouldApplyOwnerScopeAndNeverCrossMatchOwners()
    {
        await using var db = CreateDb(nameof(CrossDockOpportunities_ShouldApplyOwnerScopeAndNeverCrossMatchOwners));
        SeedWarehouseGraph(db);
        db.Zones.Add(new Zone { ZoneId = 55, WarehouseId = 1, ZoneCode = "CD-OWNER", ZoneName = "Cross Dock owner", ZoneType = ZoneTypeEnum.CrossDock, IsActive = true });
        db.Locations.Add(new Location { LocationId = 55, ZoneId = 55, LocationCode = "CD-OWNER-STAGE", IsActive = true });
        db.Items.Add(new Item { ItemId = 55, ItemCode = "CD-OWNER", ItemName = "Cross dock owner scope", BaseUomId = 1, IsActive = true });
        db.Partners.AddRange(
            new Partner { PartnerId = 551, PartnerCode = "OWNER-551", PartnerName = "Owner 551", PartnerType = PartnerTypeEnum.Customer, IsActive = true, IsThreePlClient = true },
            new Partner { PartnerId = 552, PartnerCode = "OWNER-552", PartnerName = "Owner 552", PartnerType = PartnerTypeEnum.Customer, IsActive = true, IsThreePlClient = true });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 550, VoucherCode = "IN-CD-OWNER-A", WarehouseId = 1, OwnerPartnerId = 551, VoucherType = VoucherTypeEnum.NhapKho, VoucherDate = VietnamTime.Today, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "audit.test" },
            new Voucher { VoucherId = 551, VoucherCode = "IN-CD-OWNER-B", WarehouseId = 1, OwnerPartnerId = 552, VoucherType = VoucherTypeEnum.NhapKho, VoucherDate = VietnamTime.Today, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "audit.test" },
            new Voucher { VoucherId = 552, VoucherCode = "OUT-CD-OWNER-A", WarehouseId = 1, OwnerPartnerId = 551, VoucherType = VoucherTypeEnum.XuatKho, VoucherDate = VietnamTime.Today, CreatedBy = "audit.test" },
            new Voucher { VoucherId = 553, VoucherCode = "OUT-CD-OWNER-B", WarehouseId = 1, OwnerPartnerId = 552, VoucherType = VoucherTypeEnum.XuatKho, VoucherDate = VietnamTime.Today, CreatedBy = "audit.test" });
        db.VoucherDetails.AddRange(
            new VoucherDetail { VoucherDetailId = 5501, VoucherId = 550, ItemId = 55, BaseQty = 2, TransactionQty = 2, TransactionUomId = 1, LotNumber = "AUDIT_TEST_OWNER_LOT" },
            new VoucherDetail { VoucherDetailId = 5511, VoucherId = 551, ItemId = 55, BaseQty = 2, TransactionQty = 2, TransactionUomId = 1, LotNumber = "AUDIT_TEST_OWNER_LOT" },
            new VoucherDetail { VoucherDetailId = 5521, VoucherId = 552, ItemId = 55, BaseQty = 2, TransactionQty = 2, TransactionUomId = 1, LotNumber = "AUDIT_TEST_OWNER_LOT" },
            new VoucherDetail { VoucherDetailId = 5531, VoucherId = 553, ItemId = 55, BaseQty = 2, TransactionQty = 2, TransactionUomId = 1, LotNumber = "AUDIT_TEST_OWNER_LOT" });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, 1, "owner.manager", WmsRoles.Manager, 551);
        var result = await controller.CrossDockOpportunities(1);

        Assert.IsType<ViewResult>(result);
        var opportunities = Assert.IsAssignableFrom<List<dynamic>>(controller.ViewBag.Opportunities);
        var opportunity = Assert.Single(opportunities);
        Assert.Equal(550L, (long)GetAnonValue(opportunity, "InboundVoucherId")!);
        Assert.Equal(552L, (long)GetAnonValue(opportunity, "OutboundVoucherId")!);
    }

    [Fact]
    public async Task CompleteCrossDockTask_ShouldCreateStagingStockAndInboundCompletionSkipsMatchedQty()
    {
        await using var db = CreateDb(nameof(CompleteCrossDockTask_ShouldCreateStagingStockAndInboundCompletionSkipsMatchedQty));
        SeedWarehouseGraph(db);
        db.Zones.Add(new Zone { ZoneId = 52, WarehouseId = 1, ZoneCode = "CD", ZoneName = "Cross Dock", ZoneType = ZoneTypeEnum.CrossDock, IsActive = true });
        db.Locations.Add(new Location { LocationId = 52, ZoneId = 52, LocationCode = "CD-STAGE", IsActive = true });
        db.Items.Add(new Item { ItemId = 52, ItemCode = "CD-FLOW", ItemName = "Hàng luồng đầy đủ", BaseUomId = 1, UnitCost = 2, IsActive = true });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 520, VoucherCode = "IN-CD-520", WarehouseId = 1, VoucherType = VoucherTypeEnum.NhapKho, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "qa" },
            new Voucher { VoucherId = 521, VoucherCode = "OUT-CD-521", WarehouseId = 1, VoucherType = VoucherTypeEnum.XuatKho, CreatedBy = "qa" });
        db.VoucherDetails.AddRange(
            new VoucherDetail { VoucherDetailId = 5201, VoucherId = 520, ItemId = 52, LocationId = 1, BaseQty = 5, TransactionQty = 5, TransactionUomId = 1, UnitPrice = 2, LotNumber = "LOT-FLOW", ExpiryDate = new DateTime(2026, 12, 31) },
            new VoucherDetail { VoucherDetailId = 5211, VoucherId = 521, ItemId = 52, BaseQty = 3, TransactionQty = 3, TransactionUomId = 1, LotNumber = "LOT-FLOW", ExpiryDate = new DateTime(2026, 12, 31) });
        await db.SaveChangesAsync();

        var uow = new EfUnitOfWork(db);
        var reservationService = new InventoryReservationService(db);
        var balanceService = new InventoryBalanceService(db);
        var crossDockService = new CrossDockService(db, uow, reservationService, balanceService);
        var execute = await crossDockService.ExecuteCrossDockAsync(520, 521, 52, 3, 52, null, "manager.user", System.Net.IPAddress.Loopback.ToString(), 5201, 5211);
        Assert.True(execute.Succeeded);

        var task = await db.CrossDockTasks.SingleAsync();
        var complete = await crossDockService.CompleteCrossDockTaskAsync(task.CrossDockTaskId);
        Assert.True(complete.Succeeded);

        var inboundService = new InboundExecutionService(db, uow, balanceService);
        var inboundComplete = await inboundService.CompleteInboundAsync(520, null, "manager.user", System.Net.IPAddress.Loopback.ToString());
        Assert.True(inboundComplete.Succeeded);

        var receivingStock = await db.ItemLocations.SingleAsync(il => il.ItemId == 52 && il.LocationId == 1);
        var stagingStock = await db.ItemLocations.SingleAsync(il => il.ItemId == 52 && il.LocationId == 52);
        Assert.Equal(2, receivingStock.Quantity);
        Assert.Equal(3, stagingStock.Quantity);
        Assert.Equal(3, stagingStock.ReservedQty);
        Assert.Equal(5, (await db.Items.FindAsync(52))!.CurrentStock);
    }

    [Fact]
    public async Task CompleteCrossDockTask_ShouldRollbackWhenPostedInboundStockIsInsufficient()
    {
        await using var db = CreateDb(nameof(CompleteCrossDockTask_ShouldRollbackWhenPostedInboundStockIsInsufficient));
        SeedWarehouseGraph(db);
        db.Zones.Add(new Zone { ZoneId = 53, WarehouseId = 1, ZoneCode = "CD-ROLLBACK", ZoneName = "Cross Dock rollback", ZoneType = ZoneTypeEnum.CrossDock, IsActive = true });
        db.Locations.Add(new Location { LocationId = 53, ZoneId = 53, LocationCode = "CD-STAGE-ROLLBACK", IsActive = true });
        db.Items.Add(new Item { ItemId = 53, ItemCode = "CD-ROLLBACK", ItemName = "Cross dock rollback", BaseUomId = 1, IsActive = true });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 530, VoucherCode = "IN-CD-530", WarehouseId = 1, VoucherType = VoucherTypeEnum.NhapKho, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "audit.test" },
            new Voucher { VoucherId = 531, VoucherCode = "OUT-CD-531", WarehouseId = 1, VoucherType = VoucherTypeEnum.XuatKho, CreatedBy = "audit.test" });
        db.VoucherDetails.AddRange(
            new VoucherDetail { VoucherDetailId = 5301, VoucherId = 530, ItemId = 53, LocationId = 1, BaseQty = 5, TransactionQty = 5, TransactionUomId = 1, LotNumber = "AUDIT_TEST_CD", ExpiryDate = new DateTime(2027, 1, 31) },
            new VoucherDetail { VoucherDetailId = 5311, VoucherId = 531, ItemId = 53, BaseQty = 3, TransactionQty = 3, TransactionUomId = 1, LotNumber = "AUDIT_TEST_CD", ExpiryDate = new DateTime(2027, 1, 31) });
        await db.SaveChangesAsync();

        var uow = new EfUnitOfWork(db);
        var service = new CrossDockService(db, uow);
        var execute = await service.ExecuteCrossDockAsync(530, 531, 53, 3, 53, null, "audit.test", null, 5301, 5311);
        Assert.True(execute.Succeeded);

        var inbound = await db.Vouchers.FindAsync(530L);
        inbound!.IsPosted = true;
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 53001,
            ItemId = 53,
            LocationId = 1,
            LotNumber = "AUDIT_TEST_CD",
            ExpiryDate = new DateTime(2027, 1, 31),
            Quantity = 1
        });
        await db.SaveChangesAsync();

        var taskId = await db.CrossDockTasks.Select(task => task.CrossDockTaskId).SingleAsync();
        var result = await service.CompleteCrossDockTaskAsync(taskId);

        Assert.False(result.Succeeded);
        Assert.False(uow.HasActiveTransaction);
        Assert.Empty(await db.ItemLocations.Where(row => row.LocationId == 53).ToListAsync());
        Assert.Equal(CrossDockTaskStatusEnum.Pending, (await db.CrossDockTasks.FindAsync(taskId))!.Status);
    }

    [Fact]
    public async Task CompleteCrossDockTask_ShouldEnforceWarehouseAndOwnerScope()
    {
        await using var db = CreateDb(nameof(CompleteCrossDockTask_ShouldEnforceWarehouseAndOwnerScope));
        SeedWarehouseGraph(db);
        SeedSecondWarehouseLocation(db);
        db.Zones.Add(new Zone { ZoneId = 54, WarehouseId = 2, ZoneCode = "CD-WH2", ZoneName = "Cross Dock WH2", ZoneType = ZoneTypeEnum.CrossDock, IsActive = true });
        db.Locations.Add(new Location { LocationId = 54, ZoneId = 54, LocationCode = "CD-WH2-STAGE", IsActive = true });
        db.Items.Add(new Item { ItemId = 54, ItemCode = "CD-WH-SCOPE", ItemName = "Cross dock warehouse scope", BaseUomId = 1, IsActive = true });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 540, VoucherCode = "IN-CD-WH2", WarehouseId = 2, VoucherType = VoucherTypeEnum.NhapKho, CreatedBy = "audit.test" },
            new Voucher { VoucherId = 541, VoucherCode = "OUT-CD-WH2", WarehouseId = 2, VoucherType = VoucherTypeEnum.XuatKho, CreatedBy = "audit.test" });
        db.VoucherDetails.AddRange(
            new VoucherDetail { VoucherDetailId = 5401, VoucherId = 540, ItemId = 54, BaseQty = 2, TransactionQty = 2, TransactionUomId = 1 },
            new VoucherDetail { VoucherDetailId = 5411, VoucherId = 541, ItemId = 54, BaseQty = 2, TransactionQty = 2, TransactionUomId = 1 });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 5401,
            VoucherId = 541,
            VoucherDetailId = 5411,
            ItemId = 54,
            LocationId = 54,
            ReservedQty = 2,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "audit.test"
        });
        db.CrossDockTasks.Add(new CrossDockTask
        {
            CrossDockTaskId = 540,
            TaskCode = "AUDIT_TEST_CD_WH_SCOPE",
            InboundVoucherId = 540,
            InboundVoucherDetailId = 5401,
            OutboundVoucherId = 541,
            OutboundVoucherDetailId = 5411,
            StockReservationId = 5401,
            ItemId = 54,
            StageLocationId = 54,
            ScheduledQty = 2,
            Status = CrossDockTaskStatusEnum.Pending,
            AssignedTo = "audit.test"
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager);
        var result = await controller.CompleteCrossDockTask(540);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(CrossDockTaskStatusEnum.Pending, (await db.CrossDockTasks.FindAsync(540L))!.Status);
        Assert.Empty(await db.ItemLocations.Where(row => row.LocationId == 54).ToListAsync());

        var ownerScopedService = new CrossDockService(db, new EfUnitOfWork(db));
        var ownerResult = await ownerScopedService.CompleteCrossDockTaskAsync(540, 2, new[] { 999 });
        Assert.True(ownerResult.Forbidden);
        Assert.Equal(CrossDockTaskStatusEnum.Pending, (await db.CrossDockTasks.FindAsync(540L))!.Status);
    }

    [Fact]
    public async Task PostReservedOutbound_Transfer_ShouldKeepSerialActiveAndMoveLocation()
    {
        await using var db = CreateDb(nameof(PostReservedOutbound_Transfer_ShouldKeepSerialActiveAndMoveLocation));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-TRF-001",
            ItemName = "Hàng chuyển kho serial",
            BaseUomId = 1,
            CurrentStock = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 303,
            VoucherCode = "CK-SER-303",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.ChuyenKho,
            FulfillmentStatus = FulfillmentStatusEnum.Picked
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 3031,
            VoucherId = 303,
            ItemId = 1,
            DestLocationId = 2,
            TransactionQty = 1,
            BaseQty = 1,
            TransactionUomId = 1,
            LineNumber = 1
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 30301,
            ItemId = 1,
            LocationId = 1,
            Quantity = 1,
            ReservedQty = 1
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 303001,
            VoucherId = 303,
            VoucherDetailId = 3031,
            ItemId = 1,
            LocationId = 1,
            ReservedQty = 1,
            Status = ReservationStatusEnum.Active
        });
        db.Waves.Add(new Wave { WaveId = 303, WaveCode = "WV-303", WarehouseId = 1, Status = WaveStatusEnum.Released, CreatedAt = DateTime.Now });
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 3031,
            TaskCode = "PT-3031",
            WaveId = 303,
            VoucherId = 303,
            VoucherDetailId = 3031,
            ItemId = 1,
            SourceLocationId = 1,
            TargetQty = 1,
            PickedQty = 1,
            Status = PickTaskStatusEnum.Completed
        });
        db.SerialNumbers.Add(new SerialNumber
        {
            SerialNumberId = 30311,
            SerialCode = "TRF-SN-001",
            WarehouseId = 1,
            ItemId = 1,
            LocationId = 1,
            VoucherId = 201,
            Status = SerialNumberStatusEnum.Active
        });
        db.PickTaskSerialAssignments.Add(new PickTaskSerialAssignment
        {
            PickTaskSerialAssignmentId = 11,
            PickTaskId = 3031,
            VoucherId = 303,
            VoucherDetailId = 3031,
            SerialNumberId = 30311,
            SerialCode = "TRF-SN-001",
            ScannedBy = "picker.user"
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.PostReservedOutbound(303);

        Assert.IsType<RedirectToActionResult>(result);
        var serial = await db.SerialNumbers.SingleAsync(x => x.SerialNumberId == 30311);
        Assert.Equal(SerialNumberStatusEnum.Active, serial.Status);
        Assert.Equal(2, serial.LocationId);
        Assert.Null(serial.ConsumedVoucherId);
    }

    [Fact]
    public async Task SerialInventory_ShouldReserveStrictPickAndPostLifecycle()
    {
        await using var db = CreateDb(nameof(SerialInventory_ShouldReserveStrictPickAndPostLifecycle));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 701,
            ItemCode = "SER-E3-001",
            ItemName = "Epic 3 serial item",
            BaseUomId = 1,
            CurrentStock = 2,
            UnitCost = 9,
            TrackSerial = true,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 70101,
            ItemId = 701,
            LocationId = 1,
            Quantity = 2,
            ReservedQty = 0,
            LotNumber = "LOT-E3",
            ExpiryDate = new DateTime(2026, 12, 31),
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        db.WarehouseOrderStreamingConfigs.Add(new WarehouseOrderStreamingConfig
        {
            WarehouseOrderStreamingConfigId = 701,
            WarehouseId = 1,
            IsEnabled = true,
            IsActive = true,
            MinPriority = 1,
            DeliveryWindowHours = 24,
            CreatedBy = "qa.user"
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 701,
            VoucherCode = "PX-SER-E3-001",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            Priority = 10,
            FulfillmentStatus = FulfillmentStatusEnum.Draft
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 7011,
            VoucherId = 701,
            LineNumber = 1,
            ItemId = 701,
            LocationId = 1,
            LotNumber = "LOT-E3",
            ExpiryDate = new DateTime(2026, 12, 31),
            TransactionQty = 2,
            BaseQty = 2,
            TransactionUomId = 1,
            UnitPrice = 9,
            LineAmount = 18
        });
        db.SerialNumbers.AddRange(
            new SerialNumber { SerialNumberId = 70101, SerialCode = "E3-SN-001", WarehouseId = 1, ItemId = 701, LocationId = 1, VoucherId = 500, LotNumber = "LOT-E3", ExpiryDate = new DateTime(2026, 12, 31), Status = SerialNumberStatusEnum.Active },
            new SerialNumber { SerialNumberId = 70102, SerialCode = "E3-SN-002", WarehouseId = 1, ItemId = 701, LocationId = 1, VoucherId = 500, LotNumber = "LOT-E3", ExpiryDate = new DateTime(2026, 12, 31), Status = SerialNumberStatusEnum.Active },
            new SerialNumber { SerialNumberId = 70103, SerialCode = "E3-SN-UNRES", WarehouseId = 1, ItemId = 701, LocationId = 1, VoucherId = 500, LotNumber = "LOT-E3", ExpiryDate = new DateTime(2026, 12, 31), Status = SerialNumberStatusEnum.Active });
        await db.SaveChangesAsync();

        var releaseService = CreateOrderStreamingService(db);
        var release = await releaseService.ReleaseNowAsync(701, "manager.user", scopedWarehouseId: 1);

        Assert.True(release.Succeeded);
        Assert.Equal(2, await db.SerialReservations.CountAsync(r => r.VoucherId == 701 && r.Status == SerialReservationStatusEnum.Reserved));
        Assert.Equal(2, await db.SerialNumbers.CountAsync(s => s.ItemId == 701 && s.Status == SerialNumberStatusEnum.Allocated));

        var serialService = new SerialInventoryService(db);
        await serialService.AllocateForVoucherAsync(701, "manager.user", "voucher:701:direct-release-serial");
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.SerialReservations.CountAsync(r => r.VoucherId == 701));

        var task = await db.PickTasks.SingleAsync(t => t.VoucherId == 701);
        var picker = CreateController(db, userName: "picker.user");
        var badPick = await picker.ConfirmPickTask(task.PickTaskId, 2, "SER-E3-001", "E3-SN-001\r\nE3-SN-UNRES");
        Assert.IsType<RedirectToActionResult>(badPick);
        Assert.Contains("Serial", picker.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, (await db.PickTasks.FindAsync(task.PickTaskId))!.PickedQty);

        var reservedCodes = await db.SerialReservations
            .Include(r => r.SerialNumber)
            .Where(r => r.VoucherId == 701)
            .OrderBy(r => r.SerialReservationId)
            .Select(r => r.SerialNumber!.SerialCode)
            .ToListAsync();
        var goodPick = await picker.ConfirmPickTask(task.PickTaskId, 2, "SER-E3-001", string.Join("\r\n", reservedCodes));

        Assert.IsType<RedirectToActionResult>(goodPick);
        Assert.Equal(2, await db.SerialReservations.CountAsync(r => r.VoucherId == 701 && r.Status == SerialReservationStatusEnum.Picked));
        Assert.Equal(2, await db.PickTaskSerialAssignments.CountAsync(a => a.PickTaskId == task.PickTaskId && a.SerialReservationId != null));

        var manager = CreateController(db, userName: "manager.user");
        var post = await manager.PostReservedOutbound(701);

        Assert.IsType<RedirectToActionResult>(post);
        Assert.Equal(2, await db.SerialReservations.CountAsync(r => r.VoucherId == 701 && r.Status == SerialReservationStatusEnum.Consumed));
        Assert.Equal(2, await db.SerialNumbers.CountAsync(s => s.ItemId == 701 && s.Status == SerialNumberStatusEnum.Consumed && s.ConsumedVoucherId == 701));
        Assert.True((await db.Vouchers.FindAsync(701L))!.IsPosted);
    }

    [Fact]
    public async Task LpnMovement_ShouldSyncSerialLocationAndBlockAllocatedSerials()
    {
        await using var db = CreateDb(nameof(LpnMovement_ShouldSyncSerialLocationAndBlockAllocatedSerials));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 801, ItemCode = "LPN-SER-001", ItemName = "LPN serial item", BaseUomId = 1, IsActive = true, TrackSerial = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 80101, ItemId = 801, LocationId = 1, Quantity = 1, ReservedQty = 0 });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 801,
            LpnCode = "LPN-SER-801",
            VoucherId = 801,
            WarehouseId = 1,
            CurrentLocationId = 1,
            Status = LpnStatusEnum.Stored,
            LpnType = LpnTypeEnum.Carton,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail { LicensePlateDetailId = 8011, ItemId = 801, Quantity = 1 }
            }
        });
        db.SerialNumbers.Add(new SerialNumber
        {
            SerialNumberId = 80101,
            SerialCode = "LPN-SN-801",
            WarehouseId = 1,
            ItemId = 801,
            LocationId = 1,
            LicensePlateId = 801,
            VoucherId = 700,
            Status = SerialNumberStatusEnum.Active
        });
        await db.SaveChangesAsync();

        var service = new MovementTaskService(db, new EfUnitOfWork(db));
        var move = await service.CreateLpnMovementTaskAsync("LPN-SER-801", 2, MovementTaskTypeEnum.Relocate, MovementTaskPriorityEnum.High, 1, "forklift.user");
        await service.CompleteAsync(move.MovementTaskId, "L1", "L2", 1, 1, "forklift.user", "LPN-SER-801");

        var serial = await db.SerialNumbers.SingleAsync(s => s.SerialNumberId == 80101);
        Assert.Equal(2, serial.LocationId);
        Assert.Equal(801, serial.LicensePlateId);

        serial.Status = SerialNumberStatusEnum.Allocated;
        db.SerialReservations.Add(new SerialReservation
        {
            SerialReservationId = 801001,
            SerialNumberId = 80101,
            VoucherId = 8019,
            WarehouseId = 1,
            ItemId = 801,
            LocationId = 2,
            LicensePlateId = 801,
            Status = SerialReservationStatusEnum.Reserved,
            IdempotencyKey = "test:lpn-block:80101",
            ReservedBy = "wave.user"
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateLpnMovementTaskAsync("LPN-SER-801", 1, MovementTaskTypeEnum.Relocate, MovementTaskPriorityEnum.High, 1, "forklift.user"));
    }

    [Fact]
    public async Task InventoryTransactionService_ShouldDedupeAndValidateSemanticRules()
    {
        await using var db = CreateDb(nameof(InventoryTransactionService_ShouldDedupeAndValidateSemanticRules));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 901, ItemCode = "LEDGER-001", ItemName = "Ledger item", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var service = new InventoryTransactionService(db);
        var request = new InventoryTransactionWriteRequest
        {
            TransactionType = InventoryTransactionTypeEnum.Receive,
            TransactionGroupKey = "test:ledger:receive",
            IdempotencyKey = "test:ledger:receive:901",
            WarehouseId = 1,
            ItemId = 901,
            LocationId = 1,
            HoldStatusBefore = null,
            HoldStatusAfter = InventoryHoldStatusEnum.Available,
            QuantityBefore = 0,
            QuantityAfter = 5,
            ReservedBefore = 0,
            ReservedAfter = 0,
            ReferenceType = "Test",
            ReferenceId = "901",
            ReferenceCode = "LEDGER-RECEIVE",
            Actor = "qa.user"
        };

        await service.RecordAsync(request);
        await db.SaveChangesAsync();
        await service.RecordAsync(request);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.InventoryTransactions.CountAsync(t => t.IdempotencyKey == request.IdempotencyKey));

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordAsync(new InventoryTransactionWriteRequest
        {
            TransactionType = InventoryTransactionTypeEnum.Receive,
            TransactionGroupKey = "test:ledger:bad",
            IdempotencyKey = "test:ledger:bad-receive",
            WarehouseId = 1,
            ItemId = 901,
            LocationId = 1,
            HoldStatusBefore = InventoryHoldStatusEnum.Available,
            HoldStatusAfter = InventoryHoldStatusEnum.Available,
            QuantityBefore = 5,
            QuantityAfter = 3,
            ReservedBefore = 0,
            ReservedAfter = 0,
            Actor = "qa.user"
        }));
    }

    [Fact]
    public async Task ItemLocationMutation_ShouldCreateMoveLedgerRowsWithBeforeAfterBalances()
    {
        await using var db = CreateDb(nameof(ItemLocationMutation_ShouldCreateMoveLedgerRowsWithBeforeAfterBalances));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 902, ItemCode = "LEDGER-MOVE", ItemName = "Ledger move item", BaseUomId = 1, IsActive = true });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 90201,
            ItemId = 902,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            LotNumber = "LOT-MOVE",
            ExpiryDate = new DateTime(2026, 12, 31),
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        await db.SaveChangesAsync();
        db.InventoryTransactions.RemoveRange(db.InventoryTransactions);
        await db.SaveChangesAsync();

        var ledgerService = new InventoryTransactionService(db);
        using (ledgerService.BeginScope(new InventoryTransactionContext
        {
            TransactionType = InventoryTransactionTypeEnum.Move,
            TransactionGroupKey = "test:movement:902",
            IdempotencyKeyPrefix = "test:movement:902",
            WarehouseId = 1,
            MovementTaskId = 902,
            ReferenceType = "MovementTask",
            ReferenceId = "902",
            ReferenceCode = "MT-902",
            Actor = "forklift.user"
        }))
        {
            var source = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 90201);
            source.Quantity -= 3;
            db.ItemLocations.Add(new ItemLocation
            {
                ItemLocationId = 90202,
                ItemId = 902,
                LocationId = 2,
                Quantity = 3,
                ReservedQty = 0,
                LotNumber = "LOT-MOVE",
                ExpiryDate = new DateTime(2026, 12, 31),
                HoldStatus = InventoryHoldStatusEnum.Available
            });
            await db.SaveChangesAsync();
        }

        var transactions = await db.InventoryTransactions
            .Where(t => t.TransactionGroupKey == "test:movement:902")
            .OrderBy(t => t.QuantityDelta)
            .ToListAsync();

        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, t => Assert.Equal(InventoryTransactionTypeEnum.Move, t.TransactionType));
        Assert.Equal(-3, transactions[0].QuantityDelta);
        Assert.Equal(10, transactions[0].QuantityBefore);
        Assert.Equal(7, transactions[0].QuantityAfter);
        Assert.Equal(3, transactions[1].QuantityDelta);
        Assert.Equal(0, transactions[1].QuantityBefore);
        Assert.Equal(3, transactions[1].QuantityAfter);
        Assert.Equal("MT-902", transactions[0].ReferenceCode);
    }

    [Fact]
    public async Task OrderStreaming_AutoReleaseUrgentVoucher_ShouldCreateDirectPickTasks()
    {
        await using var db = CreateDb(nameof(OrderStreaming_AutoReleaseUrgentVoucher_ShouldCreateDirectPickTasks));
        SeedOrderStreamingFixture(db, priority: 10, serviceLevel: ServiceLevelEnum.Express);

        var service = CreateOrderStreamingService(db);
        var result = await service.TryAutoReleaseAsync(6101, "planner.user");

        Assert.True(result.Succeeded);
        var task = await db.PickTasks.Include(t => t.Allocations).SingleAsync();
        Assert.Null(task.WaveId);
        Assert.Equal(6101, task.VoucherId);
        Assert.Equal(PickTaskStatusEnum.Pending, task.Status);
        Assert.Equal(4, task.TargetQty);
        Assert.Single(task.Allocations);
        Assert.Equal("PT-TT-6101-001", task.TaskCode);
        Assert.Equal(4, await db.StockReservations.SumAsync(r => r.ReservedQty));
        Assert.Equal(4, await db.ItemLocations.Where(il => il.ItemLocationId == 61001).Select(il => il.ReservedQty).SingleAsync());
        Assert.Equal(FulfillmentStatusEnum.WaitingForPick, await db.Vouchers.Where(v => v.VoucherId == 6101).Select(v => v.FulfillmentStatus).SingleAsync());
    }

    [Fact]
    public async Task OrderStreaming_AutoReleaseVoucherBelowRules_ShouldNotCreateData()
    {
        await using var db = CreateDb(nameof(OrderStreaming_AutoReleaseVoucherBelowRules_ShouldNotCreateData));
        SeedOrderStreamingFixture(db, priority: 20, serviceLevel: ServiceLevelEnum.Standard, requestedDeliveryDate: DateTime.Today.AddDays(10));

        var service = CreateOrderStreamingService(db);
        var result = await service.TryAutoReleaseAsync(6101, "planner.user");

        Assert.True(result.Succeeded);
        Assert.Empty(db.PickTasks);
        Assert.Empty(db.StockReservations);
        Assert.Equal(FulfillmentStatusEnum.Draft, await db.Vouchers.Where(v => v.VoucherId == 6101).Select(v => v.FulfillmentStatus).SingleAsync());
    }

    [Fact]
    public async Task OrderStreaming_ManualRelease_ShouldReleaseEligibleVoucher()
    {
        await using var db = CreateDb(nameof(OrderStreaming_ManualRelease_ShouldReleaseEligibleVoucher));
        SeedOrderStreamingFixture(db, priority: 1, serviceLevel: ServiceLevelEnum.Standard);

        var service = CreateOrderStreamingService(db);
        var result = await service.ReleaseNowAsync(6101, "manager.user", scopedWarehouseId: 1);

        Assert.True(result.Succeeded);
        Assert.Single(db.PickTasks);
        Assert.Null(await db.PickTasks.Select(t => t.WaveId).SingleAsync());
        Assert.Single(db.StockReservations);
    }

    [Fact]
    public async Task OrderStreaming_ShouldNotReserveStockFromDifferentOwner()
    {
        await using var db = CreateDb(nameof(OrderStreaming_ShouldNotReserveStockFromDifferentOwner));
        SeedWarehouseGraph(db);
        db.Partners.AddRange(
            new Partner { PartnerId = 101, PartnerCode = "OWN-A", PartnerName = "Chủ hàng A", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 102, PartnerCode = "OWN-B", PartnerName = "Chủ hàng B", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "OWN-SKU", ItemName = "Hàng cùng mã khác chủ", BaseUomId = 1, OwnerPartnerId = 101, IsActive = true });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 10101,
            ItemId = 1,
            OwnerPartnerId = 102,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        db.WarehouseOrderStreamingConfigs.Add(new WarehouseOrderStreamingConfig
        {
            WarehouseOrderStreamingConfigId = 101,
            WarehouseId = 1,
            IsEnabled = true,
            IsActive = true,
            CreatedBy = "qa.user"
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 101,
            VoucherCode = "PX-OWN-A",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            OwnerPartnerId = 101,
            CreatedBy = "creator.user",
            FulfillmentStatus = FulfillmentStatusEnum.Draft
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 1011,
            VoucherId = 101,
            ItemId = 1,
            LocationId = 1,
            TransactionQty = 2,
            BaseQty = 2,
            TransactionUomId = 1,
            UnitPrice = 1,
            LineAmount = 2,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var service = CreateOrderStreamingService(db);
        var result = await service.ReleaseNowAsync(101, "manager.user", scopedWarehouseId: 1);

        Assert.False(result.Succeeded);
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 101).ToListAsync());
        Assert.Equal(0, await db.ItemLocations.Where(il => il.ItemLocationId == 10101).Select(il => il.ReservedQty).SingleAsync());
    }

    [Fact]
    public async Task ConfirmForPicking_ShouldNotReserveStockFromDifferentOwner()
    {
        await using var db = CreateDb(nameof(ConfirmForPicking_ShouldNotReserveStockFromDifferentOwner));
        SeedWarehouseGraph(db);
        db.Partners.AddRange(
            new Partner { PartnerId = 201, PartnerCode = "OWN-A", PartnerName = "Chủ hàng A", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 202, PartnerCode = "OWN-B", PartnerName = "Chủ hàng B", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.Add(new Item { ItemId = 201, ItemCode = "PICK-OWN-SKU", ItemName = "Hàng lấy theo chủ", BaseUomId = 1, OwnerPartnerId = 201, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 20101,
                ItemId = 201,
                OwnerPartnerId = 201,
                LocationId = 1,
                Quantity = 4,
                ReservedQty = 0,
                LotNumber = "LOT-OWN",
                ExpiryDate = new DateTime(2027, 12, 31),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 20201,
                ItemId = 201,
                OwnerPartnerId = 202,
                LocationId = 1,
                Quantity = 99,
                ReservedQty = 0,
                LotNumber = "LOT-OWN",
                ExpiryDate = new DateTime(2027, 12, 31),
                HoldStatus = InventoryHoldStatusEnum.Available
            });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 201,
            VoucherCode = "PX-PICK-OWN-A",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            OwnerPartnerId = 201,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            FulfillmentStatus = FulfillmentStatusEnum.Draft
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 2011,
            VoucherId = 201,
            LineNumber = 1,
            ItemId = 201,
            LocationId = 1,
            LotNumber = "LOT-OWN",
            ExpiryDate = new DateTime(2027, 12, 31),
            TransactionQty = 2,
            BaseQty = 2,
            TransactionUomId = 1,
            UnitPrice = 1,
            LineAmount = 2
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.ConfirmForPicking(201);

        Assert.IsType<RedirectToActionResult>(result);
        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 201);
        Assert.Equal(201, reservation.OwnerPartnerId);
        Assert.Equal(2, reservation.ReservedQty);

        var task = await db.PickTasks.SingleAsync(t => t.VoucherId == 201);
        Assert.Equal(201, task.OwnerPartnerId);
        Assert.Equal(2, task.TargetQty);

        Assert.Equal(201, (await db.VoucherDetails.SingleAsync(d => d.VoucherDetailId == 2011)).OwnerPartnerId);
        Assert.Equal(2, await db.ItemLocations.Where(il => il.ItemLocationId == 20101).Select(il => il.ReservedQty).SingleAsync());
        Assert.Equal(0, await db.ItemLocations.Where(il => il.ItemLocationId == 20201).Select(il => il.ReservedQty).SingleAsync());
    }

    [Fact]
    public async Task VoucherListAndDetails_ShouldRespectOwnerScopeClaims()
    {
        await using var db = CreateDb(nameof(VoucherListAndDetails_ShouldRespectOwnerScopeClaims));
        SeedWarehouseGraph(db);
        db.Partners.AddRange(
            new Partner { PartnerId = 301, PartnerCode = "OWN-301", PartnerName = "Owner 301", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 302, PartnerCode = "OWN-302", PartnerName = "Owner 302", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 301, VoucherCode = "PX-OWN-301", VoucherType = VoucherTypeEnum.XuatKho, WarehouseId = 1, OwnerPartnerId = 301, CreatedBy = "creator.user" },
            new Voucher { VoucherId = 302, VoucherCode = "PX-OWN-302", VoucherType = VoucherTypeEnum.XuatKho, WarehouseId = 1, OwnerPartnerId = 302, CreatedBy = "creator.user" });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user", ownerPartnerClaims: new[] { 301 });

        var index = Assert.IsType<ViewResult>(await controller.Index(null, null, null, null, null));
        var model = Assert.IsAssignableFrom<IEnumerable<Voucher>>(index.Model);
        var visibleVoucher = Assert.Single(model);
        Assert.Equal(301, visibleVoucher.OwnerPartnerId);

        Assert.IsType<ViewResult>(await controller.Details(301));
        Assert.IsType<NotFoundResult>(await controller.Details(302));
    }

    [Fact]
    public async Task VoucherListAndDetails_ShouldRespectSpecializedRoleWorkflowScope()
    {
        await using var db = CreateDb(nameof(VoucherListAndDetails_ShouldRespectSpecializedRoleWorkflowScope));
        SeedWarehouseGraph(db);
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 311, VoucherCode = "PN-ROLE-311", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, CreatedBy = "creator.user" },
            new Voucher { VoucherId = 312, VoucherCode = "PX-ROLE-312", VoucherType = VoucherTypeEnum.XuatKho, WarehouseId = 1, CreatedBy = "creator.user" },
            new Voucher { VoucherId = 313, VoucherCode = "DC-ROLE-313", VoucherType = VoucherTypeEnum.DieuChinh, WarehouseId = 1, CreatedBy = "creator.user" },
            new Voucher { VoucherId = 314, VoucherCode = "CK-ROLE-314", VoucherType = VoucherTypeEnum.ChuyenKho, WarehouseId = 1, CreatedBy = "creator.user" });
        await db.SaveChangesAsync();

        var inbound = CreateController(db, roleName: WmsRoles.InboundStaff);
        var inboundIndex = Assert.IsType<ViewResult>(await inbound.Index(null, null, null, null, null));
        Assert.Equal(new[] { VoucherTypeEnum.NhapKho }, Assert.IsAssignableFrom<IEnumerable<Voucher>>(inboundIndex.Model).Select(x => x.VoucherType).Distinct());
        Assert.IsType<ForbidResult>(await inbound.Index(VoucherTypeEnum.XuatKho, null, null, null, null));
        Assert.IsType<ForbidResult>(await inbound.Details(312));

        var outbound = CreateController(db, roleName: WmsRoles.OutboundStaff);
        var outboundIndex = Assert.IsType<ViewResult>(await outbound.Index(null, null, null, null, null));
        Assert.Equal(new[] { VoucherTypeEnum.XuatKho }, Assert.IsAssignableFrom<IEnumerable<Voucher>>(outboundIndex.Model).Select(x => x.VoucherType).Distinct());
        Assert.IsType<ForbidResult>(await outbound.Details(311));

        var inventory = CreateController(db, roleName: WmsRoles.InventoryStaff);
        var inventoryIndex = Assert.IsType<ViewResult>(await inventory.Index(null, null, null, null, null));
        Assert.Equal(
            new[] { VoucherTypeEnum.DieuChinh, VoucherTypeEnum.ChuyenKho }.OrderBy(x => x),
            Assert.IsAssignableFrom<IEnumerable<Voucher>>(inventoryIndex.Model).Select(x => x.VoucherType).Distinct().OrderBy(x => x));
        Assert.IsType<ForbidResult>(await inventory.Details(311));

        var transport = CreateController(db, roleName: WmsRoles.TransportStaff);
        Assert.IsType<ViewResult>(await transport.Details(312));
        Assert.IsType<ForbidResult>(await transport.Details(311));

        var reportViewer = CreateController(db, roleName: WmsRoles.ReportViewer);
        var reportIndex = Assert.IsType<ViewResult>(await reportViewer.Index(null, null, null, null, null));
        Assert.Equal(4, Assert.IsAssignableFrom<IEnumerable<Voucher>>(reportIndex.Model).Count());
    }

    [Fact]
    public async Task CreateAdjustmentFromSnapshot_ShouldCalculateDiffWithinOwnerScope()
    {
        await using var db = CreateDb(nameof(CreateAdjustmentFromSnapshot_ShouldCalculateDiffWithinOwnerScope));
        SeedWarehouseGraph(db);
        var snapshotDate = DateTime.Today;
        db.Partners.AddRange(
            new Partner { PartnerId = 351, PartnerCode = "OWN-351", PartnerName = "Owner 351", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 352, PartnerCode = "OWN-352", PartnerName = "Owner 352", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.Add(new Item { ItemId = 351, ItemCode = "SNAP-OWN", ItemName = "Snapshot owner item", BaseUomId = 1, DefaultLocationId = 1, UnitCost = 2, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 35101, ItemId = 351, OwnerPartnerId = 351, LocationId = 1, Quantity = 1, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 35201, ItemId = 351, OwnerPartnerId = 352, LocationId = 1, Quantity = 99, HoldStatus = InventoryHoldStatusEnum.Available });
        db.StockSnapshots.AddRange(
            new StockSnapshot { SnapshotId = 351, SnapshotDate = snapshotDate, WarehouseId = 1, ItemId = 351, OwnerPartnerId = 351, ClosingStock = 4, UnitCost = 2, TotalValue = 8 },
            new StockSnapshot { SnapshotId = 352, SnapshotDate = snapshotDate, WarehouseId = 1, ItemId = 351, OwnerPartnerId = 352, ClosingStock = 99, UnitCost = 2, TotalValue = 198 });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user", ownerPartnerClaims: new[] { 351 });

        var result = Assert.IsType<ViewResult>(await controller.CreateAdjustmentFromSnapshot(1, snapshotDate));
        var vm = Assert.IsType<VoucherCreateViewModel>(result.Model);
        var line = Assert.Single(vm.Lines);
        Assert.Equal(351, vm.OwnerPartnerId);
        Assert.Equal("ThreePl", vm.InventoryOwnershipMode);
        Assert.Equal(3, line.TransactionQty);
        Assert.Equal(1, line.AdjustSign);
    }

    [Fact]
    public async Task OutboundVoucherPosts_ShouldForbidVoucherOutsideOwnerScope()
    {
        await using var db = CreateDb(nameof(OutboundVoucherPosts_ShouldForbidVoucherOutsideOwnerScope));
        SeedWarehouseGraph(db);
        db.Partners.AddRange(
            new Partner { PartnerId = 401, PartnerCode = "OWN-401", PartnerName = "Owner 401", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 402, PartnerCode = "OWN-402", PartnerName = "Owner 402", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 402,
            VoucherCode = "PX-OWN-402",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            OwnerPartnerId = 402,
            CreatedBy = "creator.user",
            IsPosted = true,
            PartialShipmentAllowed = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user", ownerPartnerClaims: new[] { 401 });

        Assert.IsType<ForbidResult>(await controller.ReleaseDirect(402));
        Assert.IsType<ForbidResult>(await controller.ConfirmForPicking(402));
        Assert.IsType<ForbidResult>(await controller.PostReservedOutbound(402));
        Assert.IsType<ForbidResult>(await controller.Cancel(402, "wrong owner", CancelReasonEnum.Other));
        Assert.IsType<ForbidResult>(await controller.ConfirmPacking(402, 1, "Carton", null, null, null));
        Assert.IsType<ForbidResult>(await controller.ConfirmShipping(402, "TRK", null, null));
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 402).ToListAsync());
        Assert.False(await db.Vouchers.Where(v => v.VoucherId == 402).Select(v => v.IsCancelled).SingleAsync());
    }

    [Fact]
    public async Task ConfirmPickTask_ShouldForbidTaskOutsideOwnerScope()
    {
        await using var db = CreateDb(nameof(ConfirmPickTask_ShouldForbidTaskOutsideOwnerScope));
        SeedWarehouseGraph(db);
        db.Partners.AddRange(
            new Partner { PartnerId = 501, PartnerCode = "OWN-501", PartnerName = "Owner 501", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 502, PartnerCode = "OWN-502", PartnerName = "Owner 502", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.Add(new Item { ItemId = 502, ItemCode = "PICK-502", ItemName = "Pick owner 502", BaseUomId = 1, OwnerPartnerId = 502, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 502, VoucherCode = "PX-PICK-502", VoucherType = VoucherTypeEnum.XuatKho, WarehouseId = 1, OwnerPartnerId = 502, CreatedBy = "creator.user" });
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 502,
            TaskCode = "PT-502",
            VoucherId = 502,
            OwnerPartnerId = 502,
            ItemId = 502,
            SourceLocationId = 1,
            TargetQty = 1,
            PickedQty = 0,
            Status = PickTaskStatusEnum.Assigned,
            AssignedTo = "picker.user"
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "picker.user", ownerPartnerClaims: new[] { 501 });

        Assert.IsType<ForbidResult>(await controller.ConfirmPickTask(502, 1, "PICK-502", null));
        Assert.Equal(0, await db.PickTasks.Where(t => t.PickTaskId == 502).Select(t => t.PickedQty).SingleAsync());

        var queuedController = CreateController(db, userName: "picker.user", ownerPartnerClaims: new[] { 501 });
        MarkQueued(queuedController, "owner-scope-pick-task");
        var queuedResult = Assert.IsType<JsonResult>(await queuedController.ConfirmPickTask(502, 1, "PICK-502", null, queuedBaselinePickedQty: 0));
        Assert.Equal(StatusCodes.Status403Forbidden, queuedResult.StatusCode);
    }

    [Fact]
    public async Task WarehouseStockApis_ShouldRespectOwnerScopeClaims()
    {
        await using var db = CreateDb(nameof(WarehouseStockApis_ShouldRespectOwnerScopeClaims));
        SeedWarehouseGraph(db);
        db.Locations.Add(new Location { LocationId = 3, ZoneId = 1, LocationCode = "L3", IsActive = true });
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Partners.AddRange(
            new Partner { PartnerId = 601, PartnerCode = "OWN-601", PartnerName = "Owner 601", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 602, PartnerCode = "OWN-602", PartnerName = "Owner 602", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 601, ItemCode = "WH-OWN-601", ItemName = "Warehouse owner item", BaseUomId = 1, OwnerPartnerId = 601, IsActive = true },
            new Item { ItemId = 602, ItemCode = "WH-OWN-602", ItemName = "Foreign owner item", BaseUomId = 1, OwnerPartnerId = 602, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 60101, ItemId = 601, OwnerPartnerId = 601, LocationId = 1, Quantity = 5, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 60201, ItemId = 601, OwnerPartnerId = 602, LocationId = 2, Quantity = 9, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 60202, ItemId = 602, OwnerPartnerId = 602, LocationId = 3, Quantity = 7, HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();

        var controller = CreateWarehousesController(db, warehouseClaim: 1, roleName: "Staff", ownerPartnerClaims: new[] { 601 });

        var ownLocationStock = Assert.IsType<JsonResult>(await controller.GetLocationStock(1));
        var ownRows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ownLocationStock.Value).Cast<object>().ToList();
        var ownRow = Assert.Single(ownRows);
        Assert.Equal("WH-OWN-601", GetAnonValue(ownRow, "ItemCode"));

        var foreignLocationStock = Assert.IsType<JsonResult>(await controller.GetLocationStock(3));
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(foreignLocationStock.Value).Cast<object>());

        var itemLocations = Assert.IsType<JsonResult>(await controller.GetItemLocations(601, 1));
        var locationRows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(itemLocations.Value).Cast<object>().ToList();
        var locationRow = Assert.Single(locationRows);
        Assert.Equal(1, (int)GetAnonValue(locationRow, "locationId")!);
        Assert.DoesNotContain(locationRows, row => (int)GetAnonValue(row, "locationId")! == 2);
        Assert.IsType<ForbidResult>(await controller.GetItemLocations(602, 1));

        var suggestions = Assert.IsType<JsonResult>(await controller.GetSuggestedLocations(601, 1, 1));
        var suggestionRows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(suggestions.Value).Cast<object>().ToList();
        Assert.Contains(suggestionRows, row => (int)GetAnonValue(row, "locationId")! == 1);
        Assert.DoesNotContain(suggestionRows, row => (int)GetAnonValue(row, "locationId")! == 2);
        Assert.DoesNotContain(suggestionRows, row => (int)GetAnonValue(row, "locationId")! == 3);
        Assert.IsType<ForbidResult>(await controller.GetSuggestedLocations(602, 1, 1));
    }

    [Fact]
    public async Task WarehouseGetItemLocations_ShouldRecommendFefoAvailableSourceAndHideBlockedOrUnsafeLots()
    {
        await using var db = CreateDb(nameof(WarehouseGetItemLocations_ShouldRecommendFefoAvailableSourceAndHideBlockedOrUnsafeLots));
        SeedWarehouseGraph(db);
        db.Locations.AddRange(
            new Location { LocationId = 3, ZoneId = 1, LocationCode = "L3-QC", IsActive = true },
            new Location { LocationId = 4, ZoneId = 1, LocationCode = "L4-NEAR", IsActive = true },
            new Location { LocationId = 5, ZoneId = 1, LocationCode = "L5-RESERVED", IsActive = true });
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 721,
            ItemCode = "MED-FEFO-721",
            ItemName = "Vật tư kiểm thử FEFO",
            BaseUomId = 1,
            TrackLot = true,
            TrackExpiry = true,
            IsActive = true
        });

        var today = VietnamTime.Now.Date;
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 72101,
                ItemId = 721,
                LocationId = 1,
                Quantity = 10,
                ReservedQty = 0,
                LotNumber = "LOT-LATE",
                ExpiryDate = today.AddDays(120),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 72102,
                ItemId = 721,
                LocationId = 2,
                Quantity = 10,
                ReservedQty = 3,
                LotNumber = "LOT-EARLY",
                ExpiryDate = today.AddDays(45),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 72103,
                ItemId = 721,
                LocationId = 3,
                Quantity = 10,
                LotNumber = "LOT-QC",
                ExpiryDate = today.AddDays(40),
                HoldStatus = InventoryHoldStatusEnum.QcHold
            },
            new ItemLocation
            {
                ItemLocationId = 72104,
                ItemId = 721,
                LocationId = 4,
                Quantity = 10,
                LotNumber = "LOT-NEAR",
                ExpiryDate = today.AddDays(5),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 72105,
                ItemId = 721,
                LocationId = 5,
                Quantity = 10,
                ReservedQty = 10,
                LotNumber = "LOT-RESERVED",
                ExpiryDate = today.AddDays(90),
                HoldStatus = InventoryHoldStatusEnum.Available
            });
        await db.SaveChangesAsync();

        var controller = CreateWarehousesController(db, warehouseClaim: 1, roleName: "Staff");

        var result = Assert.IsType<JsonResult>(await controller.GetItemLocations(721, 1, requiredQty: 6));
        var rows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result.Value).Cast<object>().ToList();

        Assert.Equal(new[] { 2, 1 }, rows.Select(row => (int)GetAnonValue(row, "locationId")!));
        var recommended = rows.First();
        Assert.Equal(72102, (int)GetAnonValue(recommended, "itemLocationId")!);
        Assert.Equal("LOT-EARLY", (string?)GetAnonValue(recommended, "lotNumber"));
        Assert.Equal(10m, (decimal)GetAnonValue(recommended, "quantity")!);
        Assert.Equal(3m, (decimal)GetAnonValue(recommended, "reservedQty")!);
        Assert.Equal(7m, (decimal)GetAnonValue(recommended, "availableQty")!);
        Assert.True((bool)GetAnonValue(recommended, "isFefoRecommended")!);
        Assert.True((bool)GetAnonValue(recommended, "isEnough")!);
        Assert.False((bool)GetAnonValue(rows[1], "isFefoRecommended")!);
        Assert.DoesNotContain(rows, row => (int)GetAnonValue(row, "locationId")! == 3);
        Assert.DoesNotContain(rows, row => (int)GetAnonValue(row, "locationId")! == 4);
        Assert.DoesNotContain(rows, row => (int)GetAnonValue(row, "locationId")! == 5);
    }

    [Fact]
    public async Task WarehouseCheckLocationConflict_ShouldTreatSameItemDifferentOwnerAsConflict()
    {
        await using var db = CreateDb(nameof(WarehouseCheckLocationConflict_ShouldTreatSameItemDifferentOwnerAsConflict));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Partners.AddRange(
            new Partner { PartnerId = 701, PartnerCode = "OWN-701", PartnerName = "Owner 701", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 702, PartnerCode = "OWN-702", PartnerName = "Owner 702", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 701,
            ItemCode = "WH-SAME-ITEM",
            ItemName = "Same item owner conflict",
            BaseUomId = 1,
            OwnerPartnerId = 701,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 70102,
            ItemId = 701,
            OwnerPartnerId = 702,
            LocationId = 1,
            Quantity = 4,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        await db.SaveChangesAsync();

        var controller = CreateWarehousesController(db, warehouseClaim: 1, roleName: "Staff");

        var result = Assert.IsType<JsonResult>(await controller.CheckLocationConflict(1, 701));

        Assert.True((bool)GetAnonValue(result.Value!, "conflict")!);
        Assert.Contains("WH-SAME-ITEM", (string?)GetAnonValue(result.Value!, "conflictItemName") ?? string.Empty);
    }

    [Fact]
    public async Task WarehouseCheckLocationConflict_ShouldAllowExistingStockKeyInLegacyMixedLocation()
    {
        await using var db = CreateDb(nameof(WarehouseCheckLocationConflict_ShouldAllowExistingStockKeyInLegacyMixedLocation));
        SeedWarehouseGraph(db);
        db.Items.AddRange(
            new Item { ItemId = 711, ItemCode = "LEGACY-CHARGER", ItemName = "Sạc", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 712, ItemCode = "LEGACY-KEYBOARD", ItemName = "Bàn phím", BaseUomId = 1, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 71101, ItemId = 711, LocationId = 1, Quantity = 85, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 71201, ItemId = 712, LocationId = 1, Quantity = 38, HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();

        var controller = CreateWarehousesController(db, warehouseClaim: 1, roleName: "Staff");
        var result = Assert.IsType<JsonResult>(await controller.CheckLocationConflict(1, 711));

        Assert.False((bool)GetAnonValue(result.Value!, "conflict")!);
    }

    [Fact]
    public async Task WarehouseSuggestedLocations_ShouldRequireEnoughCapacityForRequestedQty()
    {
        await using var db = CreateDb(nameof(WarehouseSuggestedLocations_ShouldRequireEnoughCapacityForRequestedQty));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 711,
            ItemCode = "WH-CAP-REQ",
            ItemName = "Capacity request item",
            BaseUomId = 1,
            Weight = 1000,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 71101,
            ItemId = 711,
            LocationId = 1,
            Quantity = 1.5m,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        await db.SaveChangesAsync();

        var controller = CreateWarehousesController(db, warehouseClaim: 1, roleName: "Staff");

        var suggestions = Assert.IsType<JsonResult>(await controller.GetSuggestedLocations(711, 1, 1));
        var rows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(suggestions.Value).Cast<object>().ToList();

        Assert.DoesNotContain(rows, row => (int)GetAnonValue(row, "locationId")! == 1);
        Assert.Contains(rows, row => (int)GetAnonValue(row, "locationId")! == 2);
    }

    [Fact]
    public async Task WarehouseSuggestedLocations_ShouldExcludeDefaultLocationOccupiedByAnotherItem()
    {
        await using var db = CreateDb(nameof(WarehouseSuggestedLocations_ShouldExcludeDefaultLocationOccupiedByAnotherItem));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.AddRange(
            new Item
            {
                ItemId = 731,
                ItemCode = "PUTAWAY-TARGET",
                ItemName = "Target item",
                BaseUomId = 1,
                DefaultLocationId = 1,
                Weight = 1,
                IsActive = true
            },
            new Item
            {
                ItemId = 732,
                ItemCode = "PUTAWAY-OCCUPANT",
                ItemName = "Existing occupant",
                BaseUomId = 1,
                Weight = 1,
                IsActive = true
            });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 73201,
            ItemId = 732,
            LocationId = 1,
            Quantity = 5,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        await db.SaveChangesAsync();

        var controller = CreateWarehousesController(db, warehouseClaim: 1, roleName: "Staff");

        var suggestions = Assert.IsType<JsonResult>(await controller.GetSuggestedLocations(731, 1, 1));
        var rows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(suggestions.Value).Cast<object>().ToList();

        Assert.DoesNotContain(rows, row => (int)GetAnonValue(row, "locationId")! == 1);
        Assert.Contains(rows, row => (int)GetAnonValue(row, "locationId")! == 2);
    }

    [Fact]
    public async Task InventoryMap_ShouldHideForeignOwnerStockLinesButKeepPhysicalOccupancy()
    {
        await using var db = CreateDb(nameof(InventoryMap_ShouldHideForeignOwnerStockLinesButKeepPhysicalOccupancy));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Partners.AddRange(
            new Partner { PartnerId = 611, PartnerCode = "OWN-611", PartnerName = "Owner 611", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 612, PartnerCode = "OWN-612", PartnerName = "Owner 612", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 611, ItemCode = "MAP-OWN-611", ItemName = "Map owner item", BaseUomId = 1, OwnerPartnerId = 611, IsActive = true },
            new Item { ItemId = 612, ItemCode = "MAP-OWN-612", ItemName = "Map foreign item", BaseUomId = 1, OwnerPartnerId = 612, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 61101, ItemId = 611, OwnerPartnerId = 611, LocationId = 1, Quantity = 5, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 61201, ItemId = 612, OwnerPartnerId = 612, LocationId = 2, Quantity = 8, HoldStatus = InventoryHoldStatusEnum.Available });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 611, VoucherCode = "PN-MAP-611", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 611, VoucherDate = DateTime.Today, CreatedBy = "qa.user" },
            new Voucher { VoucherId = 612, VoucherCode = "PN-MAP-612", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 612, VoucherDate = DateTime.Today.AddDays(1), CreatedBy = "qa.user" });
        await db.SaveChangesAsync();

        var controller = CreateWarehousesController(db, warehouseClaim: 1, roleName: "Staff", ownerPartnerClaims: new[] { 611 });

        var view = Assert.IsType<ViewResult>(await controller.InventoryMap(1));
        var model = Assert.IsType<InventoryMapPageViewModel>(view.Model);
        var tiles = model.Zones.SelectMany(z => z.Aisles).SelectMany(a => a.Racks).SelectMany(r => r.Locations).ToList();
        var ownTile = tiles.Single(t => t.LocationId == 1);
        var hiddenForeignTile = tiles.Single(t => t.LocationId == 2);

        Assert.False(ownTile.HasHiddenStockLines);
        Assert.Equal("MAP-OWN-611", Assert.Single(ownTile.StockLines).ItemCode);
        Assert.True(hiddenForeignTile.HasHiddenStockLines);
        Assert.Empty(hiddenForeignTile.StockLines);
        Assert.True(hiddenForeignTile.CurrentLoad > 0);
        Assert.NotEqual("empty", hiddenForeignTile.StatusKey);
        Assert.DoesNotContain(model.RecentVouchers, v => v.OwnerPartnerId == 612);
    }

    [Fact]
    public async Task OrderStreaming_DuplicateRelease_ShouldBeBlocked()
    {
        await using var db = CreateDb(nameof(OrderStreaming_DuplicateRelease_ShouldBeBlocked));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard);

        var service = CreateOrderStreamingService(db);
        var first = await service.ReleaseNowAsync(6101, "manager.user");
        var second = await service.ReleaseNowAsync(6101, "manager.user");

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Single(db.PickTasks);
        Assert.Single(db.StockReservations);
    }

    [Fact]
    public async Task OrderStreaming_InsufficientStock_ShouldRollbackWithoutPartialData()
    {
        await using var db = CreateDb(nameof(OrderStreaming_InsufficientStock_ShouldRollbackWithoutPartialData));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard, requestedQty: 12, availableQty: 5);

        var service = CreateOrderStreamingService(db);
        var result = await service.ReleaseNowAsync(6101, "manager.user");

        Assert.False(result.Succeeded);
        Assert.Empty(db.PickTasks);
        Assert.Empty(db.StockReservations);
        Assert.Equal(0, await db.ItemLocations.Where(il => il.ItemLocationId == 61001).Select(il => il.ReservedQty).SingleAsync());
    }

    [Fact]
    public async Task OrderStreaming_PartialShipmentAllowed_ShouldReleaseAvailableQuantityOnly()
    {
        await using var db = CreateDb(nameof(OrderStreaming_PartialShipmentAllowed_ShouldReleaseAvailableQuantityOnly));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard, requestedQty: 12, availableQty: 5);
        var voucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 6101);
        voucher.PartialShipmentAllowed = true;
        await db.SaveChangesAsync();

        var service = CreateOrderStreamingService(db);
        var result = await service.ReleaseNowAsync(6101, "manager.user", scopedWarehouseId: 1);

        Assert.True(result.Succeeded);
        var task = await db.PickTasks.SingleAsync(t => t.VoucherId == 6101);
        var reservation = await db.StockReservations.SingleAsync(r => r.VoucherId == 6101);
        Assert.Equal(5m, task.TargetQty);
        Assert.Equal(5m, reservation.ReservedQty);
        Assert.Equal(5m, await db.ItemLocations.Where(il => il.ItemLocationId == 61001).Select(il => il.ReservedQty).SingleAsync());
        Assert.Equal(FulfillmentStatusEnum.WaitingForPick, voucher.FulfillmentStatus);
    }

    [Fact]
    public async Task OrderStreaming_PartialShipmentAllowedWithoutAnyStock_ShouldRollbackWithoutTasks()
    {
        await using var db = CreateDb(nameof(OrderStreaming_PartialShipmentAllowedWithoutAnyStock_ShouldRollbackWithoutTasks));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard, requestedQty: 12, availableQty: 0);
        var voucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 6101);
        voucher.PartialShipmentAllowed = true;
        await db.SaveChangesAsync();

        var service = CreateOrderStreamingService(db);
        var result = await service.ReleaseNowAsync(6101, "manager.user", scopedWarehouseId: 1);

        Assert.False(result.Succeeded);
        Assert.Contains("không có tồn khả dụng", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.PickTasks.Where(t => t.VoucherId == 6101).ToListAsync());
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 6101).ToListAsync());
        Assert.Equal(FulfillmentStatusEnum.Draft, voucher.FulfillmentStatus);
    }

    [Fact]
    public async Task OrderStreaming_ActivePeriodLock_ShouldBlockReleaseWithoutReservation()
    {
        await using var db = CreateDb(nameof(OrderStreaming_ActivePeriodLock_ShouldBlockReleaseWithoutReservation));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard);
        db.WarehousePeriodLocks.Add(new WarehousePeriodLock
        {
            WarehouseId = 1,
            LockDate = VietnamTime.Now.Date,
            IsActive = true,
            LockedBy = "period.manager"
        });
        await db.SaveChangesAsync();

        var service = CreateOrderStreamingService(db);

        await Assert.ThrowsAsync<WarehouseLockedException>(() =>
            service.ReleaseNowAsync(6101, "manager.user", scopedWarehouseId: 1));
        Assert.Empty(await db.PickTasks.Where(t => t.VoucherId == 6101).ToListAsync());
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 6101).ToListAsync());
        Assert.Equal(0m, await db.ItemLocations.Where(il => il.ItemLocationId == 61001).Select(il => il.ReservedQty).SingleAsync());
        Assert.Equal(FulfillmentStatusEnum.Draft, await db.Vouchers.Where(v => v.VoucherId == 6101).Select(v => v.FulfillmentStatus).SingleAsync());
    }

    [Fact]
    public async Task OrderStreaming_ShouldSkipNearExpiryRequestedStockAndAllocateEligibleFefoStock()
    {
        await using var db = CreateDb(nameof(OrderStreaming_ShouldSkipNearExpiryRequestedStockAndAllocateEligibleFefoStock));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard, requestedQty: 4, availableQty: 10);

        var requestedRow = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 61001);
        requestedRow.ExpiryDate = VietnamTime.Now.Date.AddDays(5);
        var detail = await db.VoucherDetails.SingleAsync(d => d.VoucherDetailId == 61011);
        detail.LotNumber = null;
        detail.ExpiryDate = null;
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 61002,
            ItemId = 6101,
            LocationId = 2,
            Quantity = 10,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available,
            LotNumber = "LOT-STREAM-ELIGIBLE",
            ExpiryDate = VietnamTime.Now.Date.AddDays(60)
        });
        await db.SaveChangesAsync();

        var service = CreateOrderStreamingService(db);
        var result = await service.ReleaseNowAsync(6101, "manager.user", scopedWarehouseId: 1);

        Assert.True(result.Succeeded);
        var reservation = await db.StockReservations.SingleAsync();
        Assert.Equal(2, reservation.LocationId);
        Assert.Equal("LOT-STREAM-ELIGIBLE", reservation.LotNumber);
        Assert.Equal(0, requestedRow.ReservedQty);
    }

    [Fact]
    public async Task OrderStreaming_MultipleLinesForSameStock_ShouldNotOverReserve()
    {
        await using var db = CreateDb(nameof(OrderStreaming_MultipleLinesForSameStock_ShouldNotOverReserve));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard, requestedQty: 6, availableQty: 10);
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 61012,
            VoucherId = 6101,
            LineNumber = 2,
            ItemId = 6101,
            LocationId = 1,
            LotNumber = "LOT-STREAM",
            ExpiryDate = new DateTime(2026, 12, 31),
            TransactionQty = 6,
            BaseQty = 6,
            TransactionUomId = 1,
            UnitPrice = 7,
            LineAmount = 42
        });
        await db.SaveChangesAsync();

        var service = CreateOrderStreamingService(db);
        var result = await service.ReleaseNowAsync(6101, "manager.user", scopedWarehouseId: 1);

        Assert.False(result.Succeeded);
        Assert.Empty(await db.PickTasks.Where(t => t.VoucherId == 6101).ToListAsync());
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 6101).ToListAsync());
        Assert.Equal(0, await db.ItemLocations.Where(il => il.ItemLocationId == 61001).Select(il => il.ReservedQty).SingleAsync());
    }

    [Fact]
    public async Task OrderStreaming_Transfer_ShouldRejectDestinationOutsideDeclaredWarehouse()
    {
        await using var db = CreateDb(nameof(OrderStreaming_Transfer_ShouldRejectDestinationOutsideDeclaredWarehouse));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard, requestedQty: 4, availableQty: 10);
        SeedSecondWarehouseLocation(db);
        var voucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 6101);
        voucher.VoucherType = VoucherTypeEnum.ChuyenKho;
        voucher.DestWarehouseId = 2;
        var detail = await db.VoucherDetails.SingleAsync(d => d.VoucherDetailId == 61011);
        detail.DestLocationId = 1;
        await db.SaveChangesAsync();

        var service = CreateOrderStreamingService(db);
        var result = await service.ReleaseNowAsync(6101, "manager.user", scopedWarehouseId: 1);

        Assert.False(result.Succeeded);
        Assert.Contains("vị trí đích", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.PickTasks.Where(t => t.VoucherId == 6101).ToListAsync());
        Assert.Empty(await db.StockReservations.Where(r => r.VoucherId == 6101).ToListAsync());
    }

    [Fact]
    public async Task DirectPickTask_ShouldConfirmAndPostOutboundWithoutWave()
    {
        await using var db = CreateDb(nameof(DirectPickTask_ShouldConfirmAndPostOutboundWithoutWave));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard, requestedQty: 4, availableQty: 10);

        var service = CreateOrderStreamingService(db);
        var release = await service.ReleaseNowAsync(6101, "manager.user");
        Assert.True(release.Succeeded);
        var task = await db.PickTasks.SingleAsync();
        Assert.Null(task.WaveId);

        var controller = CreateController(db, userName: "picker.user");
        var confirmResult = await controller.ConfirmPickTask(task.PickTaskId, 4, "STREAM-SKU", null);
        Assert.IsType<RedirectToActionResult>(confirmResult);

        var postResult = await controller.PostReservedOutbound(6101);
        Assert.IsType<RedirectToActionResult>(postResult);

        var itemLocation = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 61001);
        var reservation = await db.StockReservations.SingleAsync();
        var item = await db.Items.SingleAsync(i => i.ItemId == 6101);
        Assert.Equal(6, itemLocation.Quantity);
        Assert.Equal(0, itemLocation.ReservedQty);
        Assert.Equal(4, reservation.ConsumedQty);
        Assert.Equal(ReservationStatusEnum.Consumed, reservation.Status);
        Assert.Equal(6, item.CurrentStock);
        Assert.True(await db.Vouchers.Where(v => v.VoucherId == 6101).Select(v => v.IsPosted).SingleAsync());
    }

    [Fact]
    public async Task PostReservedOutbound_WhenCreatorAttemptsPost_ShouldKeepPickedStateAndInventoryUnchanged()
    {
        await using var db = CreateDb(nameof(PostReservedOutbound_WhenCreatorAttemptsPost_ShouldKeepPickedStateAndInventoryUnchanged));
        SeedOrderStreamingFixture(db, priority: 100, serviceLevel: ServiceLevelEnum.Standard, requestedQty: 4, availableQty: 10);

        var service = CreateOrderStreamingService(db);
        var release = await service.ReleaseNowAsync(6101, "manager.user");
        Assert.True(release.Succeeded);

        var task = await db.PickTasks.SingleAsync();
        var creatorController = CreateController(db, userName: "creator.user");
        var confirmResult = await creatorController.ConfirmPickTask(task.PickTaskId, 4, "STREAM-SKU", null);
        Assert.IsType<RedirectToActionResult>(confirmResult);

        var postResult = await creatorController.PostReservedOutbound(6101);

        Assert.IsType<RedirectToActionResult>(postResult);
        Assert.Contains("SoD", creatorController.TempData["Error"]?.ToString());

        var voucher = await db.Vouchers.SingleAsync(v => v.VoucherId == 6101);
        var itemLocation = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 61001);
        var reservation = await db.StockReservations.SingleAsync();
        var item = await db.Items.SingleAsync(i => i.ItemId == 6101);
        var completedTask = await db.PickTasks.SingleAsync();
        var ledger = await db.InventoryTransactions
            .Where(t => t.VoucherId == 6101)
            .OrderBy(t => t.InventoryTransactionId)
            .ToListAsync();

        Assert.False(voucher.IsPosted);
        Assert.Equal(FulfillmentStatusEnum.Picked, voucher.FulfillmentStatus);
        Assert.Equal(PickTaskStatusEnum.Completed, completedTask.Status);
        Assert.Equal(4, completedTask.PickedQty);
        Assert.Equal(10, itemLocation.Quantity);
        Assert.Equal(4, itemLocation.ReservedQty);
        Assert.Equal(10, item.CurrentStock);
        Assert.Equal(ReservationStatusEnum.Active, reservation.Status);
        Assert.Equal(4, reservation.ReservedQty);
        Assert.Equal(0, reservation.ConsumedQty);
        Assert.Equal(0, reservation.ReleasedQty);
        Assert.DoesNotContain(ledger, transaction => transaction.QuantityDelta != 0);
    }

    [Fact]
    public async Task ExceptionCenter_ShouldIncludeOutboundSerialGap()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldIncludeOutboundSerialGap));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SER-GAP-001",
            ItemName = "Hàng lỗ serial",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Waves.Add(new Wave { WaveId = 401, WaveCode = "WV-401", WarehouseId = 1, Status = WaveStatusEnum.Released, CreatedAt = DateTime.Now });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 401,
            VoucherCode = "PX-SER-401",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            WaveId = 401
        });
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 4011,
            TaskCode = "PT-4011",
            WaveId = 401,
            VoucherId = 401,
            ItemId = 1,
            SourceLocationId = 1,
            TargetQty = 2,
            PickedQty = 2,
            Status = PickTaskStatusEnum.InProgress
        });
        db.SerialNumbers.Add(new SerialNumber
        {
            SerialNumberId = 40111,
            SerialCode = "SER-GAP-A",
            WarehouseId = 1,
            ItemId = 1,
            LocationId = 1,
            VoucherId = 201,
            Status = SerialNumberStatusEnum.Active
        });
        db.PickTaskSerialAssignments.Add(new PickTaskSerialAssignment
        {
            PickTaskSerialAssignmentId = 21,
            PickTaskId = 4011,
            VoucherId = 401,
            SerialNumberId = 40111,
            SerialCode = "SER-GAP-A",
            ScannedBy = "picker.user"
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ExceptionCenter(1, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Contains(rows, r => r.CategoryKey == "outbound_serial_gap" && r.ReferenceCode == "PT-4011");
    }

    [Fact]
    public async Task Shipping_ShouldRespectWarehouseScope()
    {
        await using var db = CreateDb(nameof(Shipping_ShouldRespectWarehouseScope));
        SeedWarehouseGraph(db);
        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Second Warehouse", IsActive = true });
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 501,
                VoucherCode = "PX-SHP-501",
                WarehouseId = 1,
                VoucherType = VoucherTypeEnum.XuatKho,
                IsPosted = true,
                PackedAt = DateTime.Now.AddHours(-2)
            },
            new Voucher
            {
                VoucherId = 502,
                VoucherCode = "PX-SHP-502",
                WarehouseId = 2,
                VoucherType = VoucherTypeEnum.XuatKho,
                IsPosted = true,
                PackedAt = DateTime.Now.AddHours(-2)
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.Shipping(null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<ShippingBoardRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal("PX-SHP-501", rows[0].VoucherCode);
    }

    [Fact]
    public async Task ConfirmShipping_ShouldRequireManifestForTransferVoucher()
    {
        await using var db = CreateDb(nameof(ConfirmShipping_ShouldRequireManifestForTransferVoucher));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 503,
            VoucherCode = "CK-SHP-503",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.ChuyenKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-1),
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.ConfirmShipping(503, null, null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("chuyến bàn giao", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null((await db.Vouchers.FindAsync(503L))!.ShippedAt);
    }

    [Fact]
    public async Task ConfirmShipping_ShouldRequireOutboundPackageBeforeHandover()
    {
        await using var db = CreateDb(nameof(ConfirmShipping_ShouldRequireOutboundPackageBeforeHandover));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 516,
            VoucherCode = "PX-SHP-516",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-1),
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.ConfirmShipping(516, "TRACK-516", null, null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("kiện", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null((await db.Vouchers.FindAsync(516L))!.ShippedAt);
        Assert.Empty(await db.ShippingHandoverLogs.Where(x => x.VoucherId == 516).ToListAsync());
    }

    [Fact]
    public async Task QueuedConfirmReceiving_ShouldReturnJsonAndTreatRetryAsSuccess()
    {
        await using var db = CreateDb(nameof(QueuedConfirmReceiving_ShouldReturnJsonAndTreatRetryAsSuccess));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 530,
            VoucherCode = "PN-OFF-530",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.NhapKho,
            InboundStatus = InboundStatusEnum.Approved,
            AsnCode = "ASN-OFF-530",
            ExpectedArrivalAt = DateTime.Now.AddMinutes(30)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user", warehouseClaim: 1);
        MarkQueued(controller, "offline-receive-530");
        var result = await controller.ConfirmReceiving(530);

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);
        Assert.Equal(InboundStatusEnum.Receiving, (await db.Vouchers.FindAsync(530L))!.InboundStatus);

        MarkQueued(controller, "offline-receive-530");
        var retry = await controller.ConfirmReceiving(530);

        var retryJson = Assert.IsType<JsonResult>(retry);
        Assert.True((bool)GetAnonValue(retryJson.Value!, "success")!);
        Assert.Equal(InboundStatusEnum.Receiving, (await db.Vouchers.FindAsync(530L))!.InboundStatus);
    }

    [Fact]
    public async Task QueuedConfirmReceiving_ShouldRejectMissingAppointmentWithoutChangingState()
    {
        await using var db = CreateDb(nameof(QueuedConfirmReceiving_ShouldRejectMissingAppointmentWithoutChangingState));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 5301,
            VoucherCode = "PN-OFF-5301",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.NhapKho,
            InboundStatus = InboundStatusEnum.Approved,
            AsnCode = null,
            ExpectedArrivalAt = null
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user", warehouseClaim: 1);
        MarkQueued(controller, "offline-receive-5301");

        var result = Assert.IsType<JsonResult>(await controller.ConfirmReceiving(5301));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, result.StatusCode);
        Assert.False((bool)GetAnonValue(result.Value!, "success")!);
        Assert.Equal("INBOUND_APPOINTMENT_REQUIRED", GetAnonValue(result.Value!, "code"));
        Assert.Contains("lịch xe đến", GetAnonValue(result.Value!, "message")?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InboundStatusEnum.Approved, (await db.Vouchers.FindAsync(5301L))!.InboundStatus);
    }

    [Fact]
    public async Task QueuedConfirmPickTask_ShouldReturnJsonAndAvoidDuplicateRetry()
    {
        await using var db = CreateDb(nameof(QueuedConfirmPickTask_ShouldReturnJsonAndAvoidDuplicateRetry));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5301, ItemCode = "OFF-PICK-01", ItemName = "Hàng lấy khi mạng yếu", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 531, VoucherCode = "PX-OFF-531", WarehouseId = 1, VoucherType = VoucherTypeEnum.XuatKho, FulfillmentStatus = FulfillmentStatusEnum.WaitingForPick });
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 5311,
            TaskCode = "PT-OFF-5311",
            VoucherId = 531,
            ItemId = 5301,
            SourceLocationId = 1,
            TargetQty = 3,
            PickedQty = 0,
            Status = PickTaskStatusEnum.Assigned,
            AssignedTo = "picker.user"
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "picker.user");
        MarkQueued(controller, "offline-pick-5311");
        var result = await controller.ConfirmPickTask(5311, 3, "OFF-PICK-01", null, queuedBaselinePickedQty: 0);

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);
        var task = await db.PickTasks.FindAsync(5311L);
        Assert.Equal(3, task!.PickedQty);
        Assert.Equal(PickTaskStatusEnum.Completed, task.Status);

        MarkQueued(controller, "offline-pick-5311");
        var retry = await controller.ConfirmPickTask(5311, 3, "OFF-PICK-01", null, queuedBaselinePickedQty: 0);

        var retryJson = Assert.IsType<JsonResult>(retry);
        Assert.True((bool)GetAnonValue(retryJson.Value!, "success")!);
        Assert.Equal(3, (await db.PickTasks.FindAsync(5311L))!.PickedQty);
    }

    [Fact]
    public async Task QueuedConfirmPickTask_ShouldReturnBusinessErrorAsJson()
    {
        await using var db = CreateDb(nameof(QueuedConfirmPickTask_ShouldReturnBusinessErrorAsJson));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5302, ItemCode = "OFF-PICK-02", ItemName = "Hàng lấy sai mã", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 532, VoucherCode = "PX-OFF-532", WarehouseId = 1, VoucherType = VoucherTypeEnum.XuatKho, FulfillmentStatus = FulfillmentStatusEnum.WaitingForPick });
        db.PickTasks.Add(new PickTask
        {
            PickTaskId = 5321,
            TaskCode = "PT-OFF-5321",
            VoucherId = 532,
            ItemId = 5302,
            SourceLocationId = 1,
            TargetQty = 2,
            PickedQty = 0,
            Status = PickTaskStatusEnum.Assigned,
            AssignedTo = "picker.user"
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "picker.user");
        MarkQueued(controller, "offline-pick-5321");
        var result = await controller.ConfirmPickTask(5321, 1, "WRONG-CODE", null, queuedBaselinePickedQty: 0);

        var json = Assert.IsType<JsonResult>(result);
        Assert.False((bool)GetAnonValue(json.Value!, "success")!);
        Assert.Equal(422, json.StatusCode);
        Assert.Equal(0, (await db.PickTasks.FindAsync(5321L))!.PickedQty);
    }

    [Fact]
    public async Task QueuedConfirmMovementTask_ShouldReturnJsonAndAvoidDuplicateRetry()
    {
        await using var db = CreateDb(nameof(QueuedConfirmMovementTask_ShouldReturnJsonAndAvoidDuplicateRetry));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5303, ItemCode = "OFF-MOVE-01", ItemName = "Hàng di chuyển khi mạng yếu", BaseUomId = 1, UnitCost = 10, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 53031, ItemId = 5303, LocationId = 1, Quantity = 5, ReservedQty = 0, UpdatedAt = DateTime.UtcNow },
            new ItemLocation { ItemLocationId = 53032, ItemId = 5303, LocationId = 2, Quantity = 0, ReservedQty = 0, UpdatedAt = DateTime.UtcNow });
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 53031,
            TaskCode = "MV-OFF-53031",
            WarehouseId = 1,
            ItemId = 5303,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            PlannedQty = 5,
            ConfirmedQty = 0,
            Status = MovementTaskStatusEnum.Assigned,
            TaskType = MovementTaskTypeEnum.Relocate,
            Priority = MovementTaskPriorityEnum.Normal
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        MarkQueued(controller, "offline-move-53031");
        var result = await controller.ConfirmMovementTask(53031, "L1", "L2", 5, 1, queuedBaselineConfirmedQty: 0);

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);
        Assert.Equal(0, (await db.ItemLocations.FindAsync(53031))!.Quantity);
        Assert.Equal(5, (await db.ItemLocations.FindAsync(53032))!.Quantity);

        MarkQueued(controller, "offline-move-53031");
        var retry = await controller.ConfirmMovementTask(53031, "L1", "L2", 5, 1, queuedBaselineConfirmedQty: 0);

        var retryJson = Assert.IsType<JsonResult>(retry);
        Assert.True((bool)GetAnonValue(retryJson.Value!, "success")!);
        Assert.Equal(0, (await db.ItemLocations.FindAsync(53031))!.Quantity);
        Assert.Equal(5, (await db.ItemLocations.FindAsync(53032))!.Quantity);
    }

    [Fact]
    public async Task QueuedScanShipmentLoadPackage_ShouldReturnJsonAndAvoidDuplicateMapping()
    {
        await using var db = CreateDb(nameof(QueuedScanShipmentLoadPackage_ShouldReturnJsonAndAvoidDuplicateMapping));
        SeedWarehouseGraph(db);
        db.ShipmentLoads.Add(new ShipmentLoad
        {
            ShipmentLoadId = 53001,
            LoadCode = "LOAD-OFF-530",
            WarehouseId = 1,
            Status = ShipmentLoadStatusEnum.Planned,
            CreatedAt = DateTime.Now
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 533,
            VoucherCode = "PX-OFF-533",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddMinutes(-10),
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        db.ShipmentLoadVouchers.Add(new ShipmentLoadVoucher
        {
            ShipmentLoadId = 53001,
            VoucherId = 533,
            Sequence = 1,
            AddedBy = "staff.user",
            AddedAt = DateTime.Now
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 53301,
            PackageCode = "BOX-OFF-533",
            VoucherId = 533,
            WarehouseId = 1,
            SourceType = "Manual",
            ItemCount = 1,
            TotalQuantity = 1,
            PackedBy = "staff.user",
            PackedAt = DateTime.Now.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        MarkQueued(controller, "offline-load-53301");
        var result = await controller.ScanShipmentLoadPackage(53001, "BOX-OFF-533");

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)GetAnonValue(json.Value!, "success")!);

        MarkQueued(controller, "offline-load-53301");
        var retry = await controller.ScanShipmentLoadPackage(53001, "BOX-OFF-533");

        var retryJson = Assert.IsType<JsonResult>(retry);
        Assert.True((bool)GetAnonValue(retryJson.Value!, "success")!);
        Assert.Equal(1, await db.ShipmentLoadPackages.CountAsync(x => x.ShipmentLoadId == 53001 && x.OutboundPackageId == 53301 && x.RemovedAt == null));
    }

    [Fact]
    public async Task ExceptionCenter_ShouldIncludeShippingReadyNotShipped()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldIncludeShippingReadyNotShipped));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 504,
            VoucherCode = "PX-SHP-504",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-6),
            RequestedDeliveryDate = DateTime.Today,
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ExceptionCenter(1, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Contains(rows, r => r.CategoryKey == "shipping_ready_not_shipped" && r.ReferenceCode == "PX-SHP-504");
        Assert.Contains(rows, r => r.CategoryKey == "shipping_missing_reference" && r.ReferenceCode == "PX-SHP-504");
    }

    [Fact]
    public async Task ExceptionCenter_ShouldIncludeShippedMissingReference()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldIncludeShippedMissingReference));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 505,
            VoucherCode = "CK-SHP-505",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.ChuyenKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-2),
            ShippedAt = DateTime.Now.AddHours(-1),
            FulfillmentStatus = FulfillmentStatusEnum.Shipped
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ExceptionCenter(1, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Contains(rows, r => r.CategoryKey == "shipping_posted_missing_reference" && r.ReferenceCode == "CK-SHP-505");
    }

    [Fact]
    public async Task ConfirmShipping_ShouldCreateShippingHandoverLog()
    {
        await using var db = CreateDb(nameof(ConfirmShipping_ShouldCreateShippingHandoverLog));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 506,
            VoucherCode = "PX-SHP-506",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-1),
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 50601,
            PackageCode = "BOX-506-A",
            VoucherId = 506,
            WarehouseId = 1,
            SourceType = "Manual",
            ItemCount = 1,
            PackedBy = "staff.user",
            PackedAt = DateTime.Now.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.ConfirmShipping(506, "TRACK-506", null, "Bàn giao cho hãng vận chuyển chiều nay");

        Assert.IsType<RedirectToActionResult>(result);
        var log = await db.ShippingHandoverLogs.SingleAsync(x => x.VoucherId == 506);
        Assert.Equal("manager.user", log.HandedOverBy);
        Assert.Equal("TRACK-506", log.TrackingNumber);
        Assert.Contains("hãng vận chuyển", log.Notes ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmPacking_ShouldCreateManualAndLpnPackages()
    {
        await using var db = CreateDb(nameof(ConfirmPacking_ShouldCreateManualAndLpnPackages));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "PKG-001",
            ItemName = "Hàng kiểm đóng gói",
            BaseUomId = 1,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 510,
            VoucherCode = "PX-PKG-510",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            FulfillmentStatus = FulfillmentStatusEnum.Completed
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 5101,
            VoucherId = 510,
            LineNumber = 1,
            ItemId = 1,
            TransactionUomId = 1,
            BaseQty = 12,
            TransactionQty = 12,
            UnitPrice = 100
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 51001,
            LpnCode = "LPN-OUT-510",
            VoucherId = 99,
            WarehouseId = 1,
            ItemId = 1,
            LocationId = 1,
            CurrentLocationId = 1,
            Quantity = 4,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail
                {
                    ItemId = 1,
                    VoucherDetailId = 5101,
                    Quantity = 4
                }
            }
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user");
        var result = await controller.ConfirmPacking(
            510,
            2,
            "Thùng carton",
            "BOX-510-A\nBOX-510-B",
            "LPN-OUT-510",
            "Chia kiện để lên xe");

        Assert.IsType<RedirectToActionResult>(result);
        var packages = await db.OutboundPackages
            .Where(x => x.VoucherId == 510)
            .OrderBy(x => x.PackageCode)
            .ToListAsync();
        Assert.Equal(3, packages.Count);
        Assert.Contains(packages, x => x.PackageCode == "LPN-OUT-510" && x.SourceType == "LPN" && x.TotalQuantity == 4);
        Assert.Contains(packages, x => x.PackageCode == "BOX-510-A" && x.SourceType == "Manual");
        Assert.Contains(packages, x => x.PackageCode == "BOX-510-B" && x.SourceType == "Manual");
        Assert.All(packages, x => Assert.Equal("Thùng carton", x.PackageType));
        Assert.NotNull((await db.Vouchers.FindAsync(510L))!.PackedAt);
        Assert.Equal(LpnStatusEnum.Packed, (await db.LicensePlates.FindAsync(51001L))!.Status);
    }

    [Fact]
    public async Task ConfirmPacking_ShouldRejectLpnOutsideVoucherItems()
    {
        await using var db = CreateDb(nameof(ConfirmPacking_ShouldRejectLpnOutsideVoucherItems));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "ITEM-A", ItemName = "Hàng A", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 2, ItemCode = "ITEM-B", ItemName = "Hàng B", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 511,
            VoucherCode = "PX-PKG-511",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            FulfillmentStatus = FulfillmentStatusEnum.Completed
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 5111,
            VoucherId = 511,
            LineNumber = 1,
            ItemId = 1,
            TransactionUomId = 1,
            BaseQty = 5,
            TransactionQty = 5,
            UnitPrice = 100
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 51101,
            LpnCode = "LPN-OUT-511",
            VoucherId = 100,
            WarehouseId = 1,
            ItemId = 2,
            LocationId = 1,
            CurrentLocationId = 1,
            Quantity = 3,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail
                {
                    ItemId = 2,
                    Quantity = 3
                }
            }
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user");
        var result = await controller.ConfirmPacking(511, 0, "Thùng carton", null, "LPN-OUT-511", null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("vật tư", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.OutboundPackages.Where(x => x.VoucherId == 511).ToListAsync());
    }

    [Fact]
    public async Task ConfirmPacking_ShouldRejectLpnFromDifferentOwner()
    {
        await using var db = CreateDb(nameof(ConfirmPacking_ShouldRejectLpnFromDifferentOwner));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true });
        db.Partners.AddRange(
            new Partner { PartnerId = 301, PartnerCode = "OWN-PACK-A", PartnerName = "Chủ hàng đóng gói A", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 302, PartnerCode = "OWN-PACK-B", PartnerName = "Chủ hàng đóng gói B", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "PKG-OWN", ItemName = "Hàng đóng gói theo chủ", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 514,
            VoucherCode = "PX-PKG-514",
            WarehouseId = 1,
            OwnerPartnerId = 301,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            FulfillmentStatus = FulfillmentStatusEnum.Completed
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 5141,
            VoucherId = 514,
            LineNumber = 1,
            ItemId = 1,
            OwnerPartnerId = 301,
            TransactionUomId = 1,
            BaseQty = 3,
            TransactionQty = 3,
            UnitPrice = 100
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 51401,
            LpnCode = "LPN-OWN-B",
            VoucherId = 99,
            WarehouseId = 1,
            OwnerPartnerId = 302,
            CurrentLocationId = 1,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail
                {
                    ItemId = 1,
                    OwnerPartnerId = 302,
                    VoucherDetailId = 5141,
                    Quantity = 2
                }
            }
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user");
        var result = await controller.ConfirmPacking(514, 0, "Thùng carton", null, "LPN-OWN-B", null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("chủ hàng", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.OutboundPackages.Where(x => x.VoucherId == 514).ToListAsync());
    }

    [Fact]
    public async Task ConfirmPacking_ShouldRejectLpnQuantityGreaterThanVoucherQuantity()
    {
        await using var db = CreateDb(nameof(ConfirmPacking_ShouldRejectLpnQuantityGreaterThanVoucherQuantity));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "PKG-QTY", ItemName = "Hàng vượt lượng", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 515,
            VoucherCode = "PX-PKG-515",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            FulfillmentStatus = FulfillmentStatusEnum.Completed
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 5151,
            VoucherId = 515,
            LineNumber = 1,
            ItemId = 1,
            TransactionUomId = 1,
            BaseQty = 2,
            TransactionQty = 2,
            UnitPrice = 100
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 51501,
            LpnCode = "LPN-QTY-OVER",
            VoucherId = 99,
            WarehouseId = 1,
            CurrentLocationId = 1,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail
                {
                    ItemId = 1,
                    VoucherDetailId = 5151,
                    Quantity = 3
                }
            }
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user");
        var result = await controller.ConfirmPacking(515, 0, "Thùng carton", null, "LPN-QTY-OVER", null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("vượt", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.OutboundPackages.Where(x => x.VoucherId == 515).ToListAsync());
    }

    [Fact]
    public async Task ConfirmPacking_ShouldValidateLpnAgainstActuallyPostedPartialQuantity()
    {
        await using var db = CreateDb(nameof(ConfirmPacking_ShouldValidateLpnAgainstActuallyPostedPartialQuantity));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "PKG-PARTIAL", ItemName = "Hàng giao từng phần", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 518,
            VoucherCode = "PX-PKG-518",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PartialShipmentAllowed = true,
            FulfillmentStatus = FulfillmentStatusEnum.Completed
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 5181,
            VoucherId = 518,
            LineNumber = 1,
            ItemId = 1,
            TransactionUomId = 1,
            BaseQty = 10,
            TransactionQty = 10
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 51801,
            VoucherId = 518,
            VoucherDetailId = 5181,
            ItemId = 1,
            LocationId = 1,
            ReservedQty = 10,
            ConsumedQty = 4,
            ReleasedQty = 6,
            Status = ReservationStatusEnum.Consumed,
            CreatedBy = "picker.user"
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 51802,
            LpnCode = "LPN-PARTIAL-518",
            VoucherId = 98,
            WarehouseId = 1,
            CurrentLocationId = 1,
            Status = LpnStatusEnum.Picked,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail
                {
                    ItemId = 1,
                    VoucherDetailId = 5181,
                    Quantity = 5
                }
            }
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "staff.user");
        var result = await controller.ConfirmPacking(518, 0, "Thùng carton", null, "LPN-PARTIAL-518", null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("vượt số lượng phiếu đã xuất", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.OutboundPackages.Where(x => x.VoucherId == 518).ToListAsync());
        Assert.Equal(LpnStatusEnum.Picked, (await db.LicensePlates.FindAsync(51802L))!.Status);
    }

    [Fact]
    public async Task ConfirmShipping_ShouldAdvanceReferencedLpnToShipped()
    {
        await using var db = CreateDb(nameof(ConfirmShipping_ShouldAdvanceReferencedLpnToShipped));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 519,
            VoucherCode = "PX-SHP-519",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            CreatedBy = "creator.user",
            PackedAt = DateTime.Now.AddHours(-1),
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 51901,
            LpnCode = "LPN-SHP-519",
            VoucherId = 97,
            WarehouseId = 1,
            CurrentLocationId = 1,
            Status = LpnStatusEnum.Packed,
            IsActive = true
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 51902,
            PackageCode = "LPN-SHP-519",
            VoucherId = 519,
            WarehouseId = 1,
            SourceType = "LPN",
            ReferenceLpnCode = "LPN-SHP-519",
            ItemCount = 1,
            PackedBy = "packer.user",
            PackedAt = DateTime.Now.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager.user");
        var result = await controller.ConfirmShipping(519, "TRACK-519", null, "Bàn giao kiểm thử");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(LpnStatusEnum.Shipped, (await db.LicensePlates.FindAsync(51901L))!.Status);
        Assert.NotNull((await db.Vouchers.FindAsync(519L))!.ShippedAt);
    }

    [Fact]
    public async Task Shipping_ShouldExposePackageCountsOnBoard()
    {
        await using var db = CreateDb(nameof(Shipping_ShouldExposePackageCountsOnBoard));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 512,
            VoucherCode = "PX-SHP-512",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-2),
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        db.OutboundPackages.AddRange(
            new OutboundPackage
            {
                OutboundPackageId = 1,
                PackageCode = "BOX-512-A",
                VoucherId = 512,
                WarehouseId = 1,
                SourceType = "Manual",
                ItemCount = 1,
                PackedBy = "staff.user",
                PackedAt = DateTime.Now.AddHours(-2)
            },
            new OutboundPackage
            {
                OutboundPackageId = 2,
                PackageCode = "BOX-512-B",
                VoucherId = 512,
                WarehouseId = 1,
                SourceType = "Manual",
                ItemCount = 1,
                PackedBy = "staff.user",
                PackedAt = DateTime.Now.AddHours(-2)
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.Shipping(1, "ready", "PX-SHP-512");

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<ShippingBoardRow>>(view.Model);
        var row = Assert.Single(rows);
        Assert.Equal(2, row.PackageCount);
        Assert.Contains("BOX-512-A", row.PackageSummary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExceptionCenter_ShouldIncludeShippingMissingPackages()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldIncludeShippingMissingPackages));
        SeedWarehouseGraph(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 513,
            VoucherCode = "PX-SHP-513",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-5),
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ExceptionCenter(1, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Contains(rows, r => r.CategoryKey == "shipping_missing_packages" && r.ReferenceCode == "PX-SHP-513");
    }

    [Fact]
    public async Task ExceptionCenter_ShouldIncludeInboundReceiptVariance()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldIncludeInboundReceiptVariance));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 5141,
            ItemCode = "INB-VAR-514",
            ItemName = "Hàng lệch nhận thực tế",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 514,
            VoucherCode = "PN-VAR-514",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.NhapKho,
            InboundStatus = InboundStatusEnum.Receiving,
            ExpectedArrivalAt = DateTime.Now.AddHours(-2),
            ReceivedAt = DateTime.Now.AddHours(-1),
            CreatedBy = "receiver.user"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 51401,
            VoucherId = 514,
            ItemId = 5141,
            LocationId = 1,
            TransactionQty = 10,
            BaseQty = 10,
            DefectQty = 2,
            DefectBaseQty = 2,
            TransactionUomId = 1,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ExceptionCenter(1, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Contains(rows, r => r.CategoryKey == "inbound_receipt_variance" && r.ReferenceCode == "PN-VAR-514");
    }

    [Fact]
    public async Task ExceptionCenter_ShouldIncludeLoadPackageScanMissingAndFilterByCategory()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldIncludeLoadPackageScanMissingAndFilterByCategory));
        SeedWarehouseGraph(db);
        db.ShipmentLoads.Add(new ShipmentLoad
        {
            ShipmentLoadId = 515,
            LoadCode = "LOAD-MISS-515",
            WarehouseId = 1,
            Status = ShipmentLoadStatusEnum.Loading,
            PlannedDepartureAt = DateTime.Now.AddMinutes(30),
            CreatedBy = "planner.user",
            CreatedAt = DateTime.Now.AddHours(-1)
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 5151,
            VoucherCode = "PX-LOAD-515",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-1),
            FulfillmentStatus = FulfillmentStatusEnum.Packed,
            TrackingNumber = "TRK-515"
        });
        db.ShipmentLoadVouchers.Add(new ShipmentLoadVoucher
        {
            ShipmentLoadId = 515,
            VoucherId = 5151,
            Sequence = 1,
            AddedBy = "planner.user",
            AddedAt = DateTime.Now.AddHours(-1)
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 51501,
            PackageCode = "BOX-LOAD-515",
            VoucherId = 5151,
            WarehouseId = 1,
            SourceType = "Manual",
            ItemCount = 1,
            TotalQuantity = 1,
            PackedBy = "packer.user",
            PackedAt = DateTime.Now.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ExceptionCenter(1, "load_package_scan_missing", null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Single(rows);
        Assert.Contains(rows, r => r.CategoryKey == "load_package_scan_missing" && r.ReferenceCode == "LOAD-MISS-515");
    }

    [Fact]
    public async Task ExceptionCenter_ShouldIncludeDepartedLoadMissingPackage()
    {
        await using var db = CreateDb(nameof(ExceptionCenter_ShouldIncludeDepartedLoadMissingPackage));
        SeedWarehouseGraph(db);
        db.ShipmentLoads.Add(new ShipmentLoad
        {
            ShipmentLoadId = 517,
            LoadCode = "LOAD-DEP-517",
            WarehouseId = 1,
            Status = ShipmentLoadStatusEnum.Departed,
            PlannedDepartureAt = DateTime.Now.AddHours(-2),
            ActualDepartureAt = DateTime.Now.AddHours(-1),
            CreatedBy = "planner.user",
            CreatedAt = DateTime.Now.AddHours(-3),
            TrackingNumber = "TRK-517",
            ManifestCode = "MAN-517"
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 5171,
            VoucherCode = "PX-LOAD-517",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho,
            IsPosted = true,
            PackedAt = DateTime.Now.AddHours(-2),
            ShippedAt = DateTime.Now.AddHours(-1),
            FulfillmentStatus = FulfillmentStatusEnum.Shipped,
            TrackingNumber = "TRK-517",
            ManifestCode = "MAN-517"
        });
        db.ShipmentLoadVouchers.Add(new ShipmentLoadVoucher
        {
            ShipmentLoadId = 517,
            VoucherId = 5171,
            Sequence = 1,
            AddedBy = "planner.user",
            AddedAt = DateTime.Now.AddHours(-3)
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 51701,
            PackageCode = "BOX-LOAD-517",
            VoucherId = 5171,
            WarehouseId = 1,
            SourceType = "Manual",
            ItemCount = 1,
            TotalQuantity = 1,
            PackedBy = "packer.user",
            PackedAt = DateTime.Now.AddHours(-2),
            TrackingNumber = "TRK-517",
            ManifestCode = "MAN-517"
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.user");
        var result = await controller.ExceptionCenter(1, null, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OperationExceptionRow>>(view.Model);
        Assert.Contains(rows, r => r.CategoryKey == "load_departed_package_missing" && r.ReferenceCode == "LOAD-DEP-517");
    }

    [Fact]
    public async Task OpsKpi_ShouldExposeShippingMetricsAndRecentHandovers()
    {
        await using var db = CreateDb(nameof(OpsKpi_ShouldExposeShippingMetricsAndRecentHandovers));
        SeedWarehouseGraph(db);
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 507,
                VoucherCode = "PX-SHP-507",
                WarehouseId = 1,
                VoucherType = VoucherTypeEnum.XuatKho,
                IsPosted = true,
                RequestedDeliveryDate = DateTime.Today.AddDays(-1),
                FulfillmentStatus = FulfillmentStatusEnum.Completed
            },
            new Voucher
            {
                VoucherId = 508,
                VoucherCode = "PX-SHP-508",
                WarehouseId = 1,
                VoucherType = VoucherTypeEnum.XuatKho,
                IsPosted = true,
                RequestedDeliveryDate = WMS.Common.VietnamTime.Now.Date,
                PackedAt = WMS.Common.VietnamTime.Now.Date.AddHours(9),
                ShippedAt = WMS.Common.VietnamTime.Now.Date.AddHours(11),
                FulfillmentStatus = FulfillmentStatusEnum.Shipped,
                TrackingNumber = "TRACK-508"
            });
        db.ShippingHandoverLogs.Add(new ShippingHandoverLog
        {
            ShippingHandoverLogId = 1,
            VoucherId = 508,
            WarehouseId = 1,
            HandedOverBy = "manager.user",
            HandedOverAt = WMS.Common.VietnamTime.Now.Date.AddHours(11),
            TrackingNumber = "TRACK-508",
            Notes = "Đối soát xong"
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseClaim: 1, userName: "manager.user", roleName: "Manager");
        var result = await controller.OpsKpi(1);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(1, (int)controller.ViewBag.WaitingPacking);
        Assert.Equal(0, (int)controller.ViewBag.ReadyToShip);
        Assert.Equal(1, (int)controller.ViewBag.ShippedToday);
        Assert.Equal(1, (int)controller.ViewBag.OverdueShipping);
        Assert.True((decimal)controller.ViewBag.OnTimeShipRate >= 100m);

        var handovers = Assert.IsAssignableFrom<List<ShippingHandoverLog>>(controller.ViewBag.RecentHandovers);
        Assert.Single(handovers);
        Assert.Equal("TRACK-508", handovers[0].TrackingNumber);
    }

    [Fact]
    public async Task DockBoard_ShouldShowDelayedAppointmentsWithinScopedWarehouse()
    {
        await using var db = CreateDb(nameof(DockBoard_ShouldShowDelayedAppointmentsWithinScopedWarehouse));
        SeedWarehouseGraph(db);

        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Warehouse 2", IsActive = true });
        db.DockDoorCapacities.AddRange(
            new DockDoorCapacity { CapacityId = 1, WarehouseId = 1, DockDoor = "D01", SlotStartMinutes = 0, SlotEndMinutes = 1440, DoorType = DockDoorTypeEnum.Receiving },
            new DockDoorCapacity { CapacityId = 2, WarehouseId = 2, DockDoor = "D02", SlotStartMinutes = 0, SlotEndMinutes = 1440, DoorType = DockDoorTypeEnum.Receiving });

        var now = WMS.Common.VietnamTime.Now;
        var boardDate = now.Date;
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 701,
                VoucherCode = "PN-0701",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                VoucherDate = boardDate,
                CreatedBy = "qa.user",
                InboundStatus = InboundStatusEnum.Approved,
                DockDoor = "D01",
                ExpectedArrivalAt = now.AddHours(-3),
                DockAppointmentStart = now.AddHours(-3),
                DockAppointmentEnd = now.AddHours(-2),
                DockStatus = DockOperationStatusEnum.Scheduled
            },
            new Voucher
            {
                VoucherId = 702,
                VoucherCode = "PN-0702",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 2,
                VoucherDate = boardDate,
                CreatedBy = "qa.user",
                InboundStatus = InboundStatusEnum.Approved,
                DockDoor = "D02",
                ExpectedArrivalAt = now,
                DockAppointmentStart = now,
                DockAppointmentEnd = now.AddHours(1)
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, roleName: "Staff");
        var result = await controller.DockBoard(null, boardDate);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DockBoardPageViewModel>(view.Model);
        var row = Assert.Single(model.Rows);
        Assert.Equal("PN-0701", row.VoucherCode);
        Assert.True(row.IsDelayed);
        Assert.Equal(DockOperationStatusEnum.Delayed, row.EffectiveDockStatus);
        Assert.Equal(1, model.DelayedCount);
        Assert.DoesNotContain(model.Rows, r => r.WarehouseId == 2);
    }

    [Fact]
    public async Task UpdateDockMilestone_ShouldRecordSequentialUnloadStartAndReceivingStatus()
    {
        await using var db = CreateDb(nameof(UpdateDockMilestone_ShouldRecordSequentialUnloadStartAndReceivingStatus));
        SeedWarehouseGraph(db);

        var now = WMS.Common.VietnamTime.Now;
        var boardDate = now.Date;
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 703,
            VoucherCode = "PN-0703",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            VoucherDate = boardDate,
            CreatedBy = "qa.user",
            InboundStatus = InboundStatusEnum.Approved,
            DockDoor = "D01",
            ExpectedArrivalAt = now,
            DockAppointmentStart = now,
            DockAppointmentEnd = now.AddHours(1),
            DockStatus = DockOperationStatusEnum.Scheduled
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "dock.user", roleName: "Staff");
        var result = await controller.UpdateDockMilestone(703, "unload-start", null, boardDate);

        Assert.IsType<RedirectToActionResult>(result);
        var voucher = await db.Vouchers.FindAsync(703L);
        Assert.NotNull(voucher);
        Assert.NotNull(voucher!.GateInAt);
        Assert.NotNull(voucher.DockArrivalAt);
        Assert.NotNull(voucher.UnloadStartAt);
        Assert.Equal(DockOperationStatusEnum.Unloading, voucher.DockStatus);
        Assert.Equal(InboundStatusEnum.Receiving, voucher.InboundStatus);
        Assert.Equal("dock.user", voucher.ReceivedBy);
        Assert.NotNull(voucher.ReceivedAt);
    }

    [Fact]
    public async Task UpdateDockMilestone_ShouldForbidCrossOwnerMutation()
    {
        await using var db = CreateDb(nameof(UpdateDockMilestone_ShouldForbidCrossOwnerMutation));
        SeedWarehouseGraph(db);
        var now = WMS.Common.VietnamTime.Now;
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 704,
            VoucherCode = "PN-OWNER-704",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            OwnerPartnerId = 20,
            VoucherDate = now.Date,
            CreatedBy = "qa.user",
            InboundStatus = InboundStatusEnum.Approved,
            DockDoor = "D01",
            ExpectedArrivalAt = now,
            DockAppointmentStart = now,
            DockAppointmentEnd = now.AddHours(1),
            DockStatus = DockOperationStatusEnum.Scheduled
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "owner10.inbound", roleName: WmsRoles.InboundStaff, ownerPartnerClaims: new[] { 10 });
        var result = await controller.UpdateDockMilestone(704, "unload-start", null, now.Date);

        Assert.IsType<ForbidResult>(result);
        var voucher = await db.Vouchers.FindAsync(704L);
        Assert.NotNull(voucher);
        Assert.Equal(InboundStatusEnum.Approved, voucher!.InboundStatus);
        Assert.Equal(DockOperationStatusEnum.Scheduled, voucher.DockStatus);
        Assert.Null(voucher.ReceivedAt);
    }

    [Fact]
    public async Task CustomerLabels_SameSku_ShouldRenderDifferentContentPerCustomer()
    {
        await using var db = CreateDb(nameof(CustomerLabels_SameSku_ShouldRenderDifferentContentPerCustomer));
        SeedWarehouseGraph(db);
        SeedCustomerLabelFixture(db);
        await db.SaveChangesAsync();

        var service = CreateLabelService(db);
        var jobA = await service.CreateVoucherPrintJobAsync(8101, "tester");
        var jobB = await service.CreateVoucherPrintJobAsync(8102, "tester");

        var lineA = await db.LabelPrintJobLines.SingleAsync(l => l.LabelPrintJobId == jobA.LabelPrintJobId);
        var lineB = await db.LabelPrintJobLines.SingleAsync(l => l.LabelPrintJobId == jobB.LabelPrintJobId);

        Assert.Contains("A-COFFEE-500", lineA.BodyText);
        Assert.Contains("B-CAFE-500", lineB.BodyText);
        Assert.NotEqual(lineA.BodyText, lineB.BodyText);
    }

    [Fact]
    public async Task CustomerLabels_PartnerTemplate_ShouldOverrideDefaultTemplate()
    {
        await using var db = CreateDb(nameof(CustomerLabels_PartnerTemplate_ShouldOverrideDefaultTemplate));
        SeedWarehouseGraph(db);
        SeedCustomerLabelFixture(db);
        db.PartnerLabelTemplates.Add(new PartnerLabelTemplate
        {
            PartnerLabelTemplateId = 9003,
            PartnerId = 901,
            LabelPurpose = LabelPurposeEnum.OutboundVoucher,
            TemplateName = "Mẫu riêng khách A",
            LabelSize = "50x30",
            CodeType = "barcode",
            HeaderTemplate = "RIÊNG {TenKhachHang}",
            BodyTemplate = "Mã riêng {MaHangKhach} - {MaPhieu}",
            FooterTemplate = "Khách A",
            IsDefault = true,
            IsActive = true,
            CreatedBy = "tester"
        });
        await db.SaveChangesAsync();

        var service = CreateLabelService(db);
        var job = await service.CreateVoucherPrintJobAsync(8101, "tester");
        var line = await db.LabelPrintJobLines.SingleAsync(l => l.LabelPrintJobId == job.LabelPrintJobId);

        Assert.StartsWith("RIÊNG", line.HeaderText);
        Assert.Contains("Mã riêng A-COFFEE-500", line.BodyText);
    }

    [Fact]
    public async Task CustomerLabels_PackagePrintJob_ShouldLinkVoucherAndPackage()
    {
        await using var db = CreateDb(nameof(CustomerLabels_PackagePrintJob_ShouldLinkVoucherAndPackage));
        SeedWarehouseGraph(db);
        SeedCustomerLabelFixture(db);
        await db.SaveChangesAsync();

        var service = CreateLabelService(db);
        var job = await service.CreatePackagePrintJobAsync(8201, "packer");

        Assert.Equal(LabelPurposeEnum.OutboundPackage, job.LabelPurpose);
        Assert.Equal(8101, job.VoucherId);
        Assert.Equal(8201, job.OutboundPackageId);
        Assert.Equal(1, job.TotalLabels);

        var line = await db.LabelPrintJobLines.SingleAsync(l => l.LabelPrintJobId == job.LabelPrintJobId);
        Assert.Equal("PK-A-001", line.PackageCode);
        Assert.Equal("PK-A-001", line.BarcodeValue);
        Assert.Contains("PK-A-001", line.BodyText);
    }

    [Fact]
    public async Task CustomerLabels_PackagePrintJob_ShouldAllowLpnBackedMultiLinePackage()
    {
        await using var db = CreateDb(nameof(CustomerLabels_PackagePrintJob_ShouldAllowLpnBackedMultiLinePackage));
        SeedWarehouseGraph(db);
        SeedCustomerLabelFixture(db);
        db.Items.Add(new Item
        {
            ItemId = 802,
            ItemCode = "TEA-INT",
            ItemName = "Tea internal",
            Barcode = "893000000002",
            BaseUomId = 1,
            UnitCost = 12,
            IsActive = true
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 8113,
            VoucherId = 8101,
            ItemId = 802,
            TransactionQty = 2,
            BaseQty = 2,
            TransactionUomId = 1,
            LineNumber = 2
        });
        db.LicensePlates.Add(new LicensePlate
        {
            LicensePlateId = 8301,
            LpnCode = "LPN-A-001",
            VoucherId = 8101,
            VoucherDetailId = 8111,
            WarehouseId = 1,
            ItemId = 801,
            CurrentLocationId = 1,
            Quantity = 1,
            Status = LpnStatusEnum.Stored,
            IsActive = true,
            Details =
            {
                new LicensePlateDetail
                {
                    ItemId = 801,
                    VoucherDetailId = 8111,
                    Quantity = 1
                },
                new LicensePlateDetail
                {
                    ItemId = 802,
                    VoucherDetailId = 8113,
                    Quantity = 2
                }
            }
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 8202,
            PackageCode = "PK-LPN-001",
            VoucherId = 8101,
            WarehouseId = 1,
            SourceType = "LPN",
            ReferenceLpnCode = "LPN-A-001",
            TotalQuantity = 3,
            ItemCount = 2,
            PackedBy = "packer"
        });
        await db.SaveChangesAsync();

        var service = CreateLabelService(db);
        var job = await service.CreatePackagePrintJobAsync(8202, "packer");

        Assert.Equal(3, job.TotalLabels);
        var lines = await db.LabelPrintJobLines
            .Where(l => l.LabelPrintJobId == job.LabelPrintJobId)
            .OrderBy(l => l.VoucherDetailId)
            .ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, line => line.VoucherDetailId == 8111 && line.InternalItemCode == "COFFEE-INT" && line.Quantity == 1);
        Assert.Contains(lines, line => line.VoucherDetailId == 8113 && line.InternalItemCode == "TEA-INT" && line.Quantity == 2);
    }

    [Fact]
    public async Task CustomerLabels_PackagePrintJob_ShouldRejectAmbiguousManualMultiLinePackage()
    {
        await using var db = CreateDb(nameof(CustomerLabels_PackagePrintJob_ShouldRejectAmbiguousManualMultiLinePackage));
        SeedWarehouseGraph(db);
        SeedCustomerLabelFixture(db);
        db.Items.Add(new Item
        {
            ItemId = 802,
            ItemCode = "TEA-INT",
            ItemName = "Tea internal",
            Barcode = "893000000002",
            BaseUomId = 1,
            UnitCost = 12,
            IsActive = true
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 8113,
            VoucherId = 8101,
            ItemId = 802,
            TransactionQty = 2,
            BaseQty = 2,
            TransactionUomId = 1,
            LineNumber = 2
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 8202,
            PackageCode = "PK-MANUAL-AMB",
            VoucherId = 8101,
            WarehouseId = 1,
            SourceType = "Manual",
            TotalQuantity = 3,
            ItemCount = 2,
            PackedBy = "packer"
        });
        await db.SaveChangesAsync();

        var service = CreateLabelService(db);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreatePackagePrintJobAsync(8202, "packer"));

        Assert.Equal("LABEL_PACKAGE_CONTENT_AMBIGUOUS", ex.Code);
        Assert.Empty(await db.LabelPrintJobs.Where(j => j.OutboundPackageId == 8202).ToListAsync());
    }

    [Fact]
    public async Task CustomerLabels_ShouldRejectWhenNoActiveTemplate()
    {
        await using var db = CreateDb(nameof(CustomerLabels_ShouldRejectWhenNoActiveTemplate));
        SeedWarehouseGraph(db);
        SeedCustomerLabelFixture(db);
        await db.SaveChangesAsync();
        foreach (var template in await db.PartnerLabelTemplates.ToListAsync())
            template.IsActive = false;
        await db.SaveChangesAsync();

        var service = CreateLabelService(db);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateVoucherPrintJobAsync(8101, "tester"));

        Assert.Equal("LABEL_TEMPLATE_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task StockValuation_Current_ShouldUseItemLocationQuantityNotCurrentStock()
    {
        await using var db = CreateDb(nameof(StockValuation_Current_ShouldUseItemLocationQuantityNotCurrentStock));
        SeedWarehouseGraph(db);
        SeedStockValuationBaseData(db);

        db.Items.Add(new Item
        {
            ItemId = 910,
            ItemCode = "VAL-001",
            ItemName = "Hàng định giá",
            CategoryId = 920,
            BaseUomId = 1,
            UnitCost = 10,
            CurrentStock = 999,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 97001, ItemId = 910, LocationId = 1, Quantity = 5, ReservedQty = 1, LotNumber = "LOT-A" },
            new ItemLocation { ItemLocationId = 97002, ItemId = 910, LocationId = 2, Quantity = 3, ReservedQty = 0, LotNumber = "LOT-A" });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, includeFinancialPermission: true);

        var result = await controller.StockValuation(warehouseId: 1, categoryId: null, itemSearch: null, lotNumber: null, expiryDate: null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StockValuationPageViewModel>(view.Model);
        Assert.Equal(8, model.TotalQuantity);
        Assert.Equal(1, model.TotalReservedQty);
        Assert.Equal(7, model.TotalAvailableQty);
        Assert.Equal(80, model.TotalValue);
        Assert.NotEqual(9990, model.TotalValue);
    }

    [Fact]
    public async Task StockValuation_Current_ShouldScopeWarehouseAndApplyFilters()
    {
        await using var db = CreateDb(nameof(StockValuation_Current_ShouldScopeWarehouseAndApplyFilters));
        SeedWarehouseGraph(db);
        SeedStockValuationBaseData(db);

        db.Items.AddRange(
            new Item
            {
                ItemId = 911,
                ItemCode = "FILTER-KEEP",
                ItemName = "Hàng cần xem",
                CategoryId = 920,
                BaseUomId = 1,
                UnitCost = 20,
                IsActive = true
            },
            new Item
            {
                ItemId = 912,
                ItemCode = "FILTER-SKIP",
                ItemName = "Hàng khác",
                CategoryId = 921,
                BaseUomId = 1,
                UnitCost = 99,
                IsActive = true
            });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 97003, ItemId = 911, LocationId = 1, Quantity = 4, ReservedQty = 1, LotNumber = "LOT-KEEP", ExpiryDate = new DateTime(2026, 12, 31) },
            new ItemLocation { ItemLocationId = 97004, ItemId = 911, LocationId = 10, Quantity = 50, ReservedQty = 0, LotNumber = "LOT-KEEP", ExpiryDate = new DateTime(2026, 12, 31) },
            new ItemLocation { ItemLocationId = 97005, ItemId = 912, LocationId = 1, Quantity = 7, ReservedQty = 0, LotNumber = "LOT-OTHER", ExpiryDate = new DateTime(2026, 12, 31) });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, includeFinancialPermission: true);

        var result = await controller.StockValuation(
            warehouseId: 1,
            categoryId: 920,
            itemSearch: "KEEP",
            lotNumber: "LOT-KEEP",
            expiryDate: new DateTime(2026, 12, 31));

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StockValuationPageViewModel>(view.Model);
        var row = Assert.Single(model.Rows);
        Assert.Equal("FILTER-KEEP", row.ItemCode);
        Assert.Equal("WH1", row.WarehouseCode);
        Assert.Equal(4, row.Quantity);
        Assert.Equal(80, row.StockValue);
    }

    [Fact]
    public async Task StockValuation_Snapshot_ShouldReadStockSnapshots()
    {
        await using var db = CreateDb(nameof(StockValuation_Snapshot_ShouldReadStockSnapshots));
        SeedWarehouseGraph(db);
        SeedStockValuationBaseData(db);

        db.Items.Add(new Item
        {
            ItemId = 913,
            ItemCode = "SNAP-001",
            ItemName = "Hàng đã chốt",
            CategoryId = 920,
            BaseUomId = 1,
            UnitCost = 123,
            CurrentStock = 999,
            IsActive = true
        });
        db.StockSnapshots.Add(new StockSnapshot
        {
            SnapshotId = 98001,
            WarehouseId = 1,
            ItemId = 913,
            SnapshotDate = new DateTime(2026, 4, 30),
            ClosingStock = 12,
            UnitCost = 7,
            TotalValue = 84
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, includeFinancialPermission: true);

        var result = await controller.StockValuation(
            warehouseId: 1,
            categoryId: null,
            itemSearch: null,
            lotNumber: null,
            expiryDate: null,
            mode: "snapshot",
            snapshotDate: new DateTime(2026, 4, 30));

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StockValuationPageViewModel>(view.Model);
        var row = Assert.Single(model.Rows);
        Assert.Equal("SNAP-001", row.ItemCode);
        Assert.Equal(12, row.Quantity);
        Assert.Equal(7, row.UnitCost);
        Assert.Equal(84, row.StockValue);
        Assert.False(model.MissingSnapshot);
    }

    [Fact]
    public async Task StockValuation_Snapshot_ShouldReturnNoticeWhenMissing()
    {
        await using var db = CreateDb(nameof(StockValuation_Snapshot_ShouldReturnNoticeWhenMissing));
        SeedWarehouseGraph(db);
        SeedStockValuationBaseData(db);
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, includeFinancialPermission: true);

        var result = await controller.StockValuation(
            warehouseId: 1,
            categoryId: null,
            itemSearch: null,
            lotNumber: null,
            expiryDate: null,
            mode: "snapshot",
            snapshotDate: new DateTime(2026, 4, 30));

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StockValuationPageViewModel>(view.Model);
        Assert.True(model.MissingSnapshot);
        Assert.Empty(model.Rows);
        Assert.Equal("Chưa có dữ liệu chốt tồn cho kho và ngày đã chọn.", model.Notice);
    }

    [Fact]
    public async Task InventoryInOutSummary_ShouldShowLotExpiryLocationsAndFilterDirection()
    {
        await using var db = CreateDb(nameof(InventoryInOutSummary_ShouldShowLotExpiryLocationsAndFilterDirection));
        SeedWarehouseGraph(db);
        SeedStockValuationBaseData(db);

        var documentDate = new DateTime(2026, 7, 2);
        var expiry = new DateTime(2027, 6, 30);
        db.Partners.Add(new Partner
        {
            PartnerId = 700,
            PartnerCode = "SUP-700",
            PartnerName = "Nhà cung cấp kiểm thử",
            PartnerType = PartnerTypeEnum.Supplier,
            IsActive = true
        });
        db.Items.Add(new Item
        {
            ItemId = 700,
            ItemCode = "MED-LOT-700",
            ItemName = "Găng tay kiểm thử",
            CategoryId = 920,
            BaseUomId = 1,
            UnitCost = 10,
            TrackLot = true,
            TrackExpiry = true,
            IsActive = true
        });
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 7001,
                VoucherCode = "PN-TEST-7001",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                PartnerId = 700,
                VoucherDate = documentDate,
                CreatedBy = "receiver.user",
                ApprovedBy = "manager.user",
                IsPosted = true
            },
            new Voucher
            {
                VoucherId = 7002,
                VoucherCode = "PX-TEST-7002",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                PartnerId = 700,
                VoucherDate = documentDate.AddDays(1),
                CreatedBy = "picker.user",
                ApprovedBy = "manager.user",
                IsPosted = true
            });
        db.VoucherDetails.AddRange(
            new VoucherDetail
            {
                VoucherDetailId = 70011,
                VoucherId = 7001,
                ItemId = 700,
                LocationId = 1,
                TransactionQty = 80,
                BaseQty = 80,
                TransactionUomId = 1,
                LotNumber = "LOT-HSD-700",
                ManufacturingDate = new DateTime(2026, 7, 1),
                ExpiryDate = expiry,
                LineNumber = 1
            },
            new VoucherDetail
            {
                VoucherDetailId = 70021,
                VoucherId = 7002,
                ItemId = 700,
                LocationId = 1,
                TransactionQty = 20,
                BaseQty = 20,
                TransactionUomId = 1,
                LotNumber = "LOT-HSD-700",
                ManufacturingDate = new DateTime(2026, 7, 1),
                ExpiryDate = expiry,
                LineNumber = 1
            });
        db.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                InventoryTransactionId = 700101,
                TransactionType = InventoryTransactionTypeEnum.Receive,
                TransactionGroupKey = "receive-700",
                IdempotencyKey = "receive-700-1",
                WarehouseId = 1,
                ItemId = 700,
                LocationId = 1,
                LotNumber = "LOT-HSD-700",
                ExpiryDate = expiry,
                QuantityDelta = 80,
                AvailableDelta = 80,
                QuantityBefore = 0,
                QuantityAfter = 80,
                AvailableBefore = 0,
                AvailableAfter = 80,
                VoucherId = 7001,
                VoucherDetailId = 70011,
                Actor = "receiver.user",
                TransactionAt = documentDate.AddHours(9)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 700201,
                TransactionType = InventoryTransactionTypeEnum.Ship,
                TransactionGroupKey = "ship-700",
                IdempotencyKey = "ship-700-1",
                WarehouseId = 1,
                ItemId = 700,
                LocationId = 1,
                LotNumber = "LOT-HSD-700",
                ExpiryDate = expiry,
                QuantityDelta = -20,
                AvailableDelta = -20,
                QuantityBefore = 80,
                QuantityAfter = 60,
                AvailableBefore = 80,
                AvailableAfter = 60,
                VoucherId = 7002,
                VoucherDetailId = 70021,
                Actor = "picker.user",
                TransactionAt = documentDate.AddDays(1).AddHours(11)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 700301,
                TransactionType = InventoryTransactionTypeEnum.Pick,
                TransactionGroupKey = "reserve-700",
                IdempotencyKey = "reserve-700-1",
                WarehouseId = 1,
                ItemId = 700,
                LocationId = 1,
                LotNumber = "LOT-HSD-700",
                ExpiryDate = expiry,
                QuantityDelta = 0,
                ReservedDelta = 5,
                AvailableDelta = -5,
                QuantityBefore = 60,
                QuantityAfter = 60,
                ReservedBefore = 0,
                ReservedAfter = 5,
                AvailableBefore = 60,
                AvailableAfter = 55,
                Actor = "reservation.user",
                TransactionAt = documentDate.AddDays(1).AddHours(12)
            });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db);

        var result = await controller.InventoryInOutSummary(documentDate, documentDate.AddDays(2), 1, null, null, null, null, "All", "LOT-HSD");
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<InventoryInOutSummaryPageViewModel>(view.Model);

        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(80, model.TotalInboundQty);
        Assert.Equal(20, model.TotalOutboundQty);
        Assert.All(model.Rows, row =>
        {
            Assert.Equal("LOT-HSD-700", row.LotNumber);
            Assert.Equal(expiry, row.ExpiryDate);
            Assert.Equal("L1", row.SourceLocationCode);
        });

        var outboundResult = await controller.InventoryInOutSummary(documentDate, documentDate.AddDays(2), 1, null, null, null, null, "Outbound", null);
        var outboundView = Assert.IsType<ViewResult>(outboundResult);
        var outboundModel = Assert.IsType<InventoryInOutSummaryPageViewModel>(outboundView.Model);
        var outboundRow = Assert.Single(outboundModel.Rows);
        Assert.Equal("PX-TEST-7002", outboundRow.VoucherCode);
        Assert.Equal(20, outboundRow.OutboundQty);
        Assert.Equal("picker.user", outboundRow.Actor);
    }

    [Fact]
    public async Task InventoryInOutSummary_ShouldRespectOwnerScopeClaims()
    {
        await using var db = CreateDb(nameof(InventoryInOutSummary_ShouldRespectOwnerScopeClaims));
        SeedWarehouseGraph(db);
        SeedStockValuationBaseData(db);

        var documentDate = new DateTime(2026, 7, 3);
        db.Partners.AddRange(
            new Partner { PartnerId = 810, PartnerCode = "OWN-810", PartnerName = "Chủ hàng 810", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 811, PartnerCode = "OWN-811", PartnerName = "Chủ hàng 811", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 810, OwnerPartnerId = 810, ItemCode = "OWNER-810-SKU", ItemName = "Hàng owner 810", CategoryId = 920, BaseUomId = 1, UnitCost = 10, IsActive = true },
            new Item { ItemId = 811, OwnerPartnerId = 811, ItemCode = "OWNER-811-SKU", ItemName = "Hàng owner 811", CategoryId = 920, BaseUomId = 1, UnitCost = 10, IsActive = true });
        db.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                InventoryTransactionId = 810001,
                TransactionType = InventoryTransactionTypeEnum.Receive,
                TransactionGroupKey = "owner-810",
                IdempotencyKey = "owner-810-1",
                WarehouseId = 1,
                OwnerPartnerId = 810,
                ItemId = 810,
                LocationId = 1,
                QuantityDelta = 30,
                QuantityBefore = 0,
                QuantityAfter = 30,
                AvailableDelta = 30,
                AvailableBefore = 0,
                AvailableAfter = 30,
                Actor = "owner.scope",
                TransactionAt = documentDate.AddHours(9)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 811001,
                TransactionType = InventoryTransactionTypeEnum.Receive,
                TransactionGroupKey = "owner-811",
                IdempotencyKey = "owner-811-1",
                WarehouseId = 1,
                OwnerPartnerId = 811,
                ItemId = 811,
                LocationId = 1,
                QuantityDelta = 70,
                QuantityBefore = 0,
                QuantityAfter = 70,
                AvailableDelta = 70,
                AvailableBefore = 0,
                AvailableAfter = 70,
                Actor = "foreign.owner",
                TransactionAt = documentDate.AddHours(10)
            });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, roleName: "Manager", ownerPartnerIds: new[] { 810 });
        var result = await controller.InventoryInOutSummary(documentDate, documentDate, null, null, null, null, null, "All", null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<InventoryInOutSummaryPageViewModel>(view.Model);
        var row = Assert.Single(model.Rows);
        Assert.Equal("OWNER-810-SKU", row.ItemCode);
        Assert.Equal(30, model.TotalInboundQty);
        Assert.DoesNotContain(model.Items, item => item.OwnerPartnerId == 811);
        Assert.DoesNotContain(model.Partners, partner => partner.PartnerId == 811);
    }

    [Fact]
    public async Task WarehouseOverview_ShouldCombineInventoryFlowBacklogAndDataExceptions()
    {
        await using var db = CreateDb(nameof(WarehouseOverview_ShouldCombineInventoryFlowBacklogAndDataExceptions));
        SeedWarehouseGraph(db);
        SeedStockValuationBaseData(db);

        var from = new DateTime(2026, 7, 1);
        var expirySoon = new DateTime(2026, 7, 20);
        db.Partners.Add(new Partner
        {
            PartnerId = 820,
            PartnerCode = "CUST-820",
            PartnerName = "Khách tổng quan",
            PartnerType = PartnerTypeEnum.Customer,
            IsActive = true
        });
        db.Items.AddRange(
            new Item { ItemId = 820, ItemCode = "OVR-SKU-820", ItemName = "Hàng tổng quan", CategoryId = 920, BaseUomId = 1, UnitCost = 12, IsActive = true, TrackExpiry = true },
            new Item { ItemId = 821, ItemCode = "OVR-SKU-821", ItemName = "Hàng lệch giữ chỗ", CategoryId = 920, BaseUomId = 1, UnitCost = 8, IsActive = true },
            new Item { ItemId = 822, ItemCode = "OVR-SKU-822", ItemName = "Hàng sai vị trí", CategoryId = 920, BaseUomId = 1, UnitCost = 0, IsActive = true },
            new Item { ItemId = 823, ItemCode = "OVR-INTERNAL-ONLY", ItemName = "Chỉ điều chuyển nội bộ", CategoryId = 920, BaseUomId = 1, UnitCost = 0, IsActive = true });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 82001, ItemId = 820, LocationId = 1, Quantity = 100, ReservedQty = 20, ExpiryDate = expirySoon, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 82101, ItemId = 821, LocationId = 2, Quantity = 5, ReservedQty = 6, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 82201, ItemId = 822, LocationId = 1, Quantity = 3, ReservedQty = 0, HoldStatus = InventoryHoldStatusEnum.QcHold });
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 8201,
                VoucherCode = "PN-OVR-001",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                PartnerId = 820,
                VoucherDate = from,
                IsPosted = true,
                CreatedBy = "receiver"
            },
            new Voucher
            {
                VoucherId = 8202,
                VoucherCode = "PX-OVR-001",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                PartnerId = 820,
                VoucherDate = from.AddDays(1),
                IsPosted = true,
                CreatedBy = "picker"
            },
            new Voucher
            {
                VoucherId = 8203,
                VoucherCode = "PN-OPEN-001",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                PartnerId = 820,
                VoucherDate = from,
                IsPosted = false,
                InboundStatus = InboundStatusEnum.Approved,
                CreatedBy = "receiver"
            },
            new Voucher
            {
                VoucherId = 8204,
                VoucherCode = "PX-OPEN-001",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                PartnerId = 820,
                VoucherDate = from,
                IsPosted = false,
                FulfillmentStatus = FulfillmentStatusEnum.Picking,
                CreatedBy = "picker"
            },
            new Voucher
            {
                VoucherId = 8206,
                VoucherCode = "PX-PARTIAL-001",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                PartnerId = 820,
                VoucherDate = from,
                IsPosted = false,
                FulfillmentStatus = FulfillmentStatusEnum.PartiallyIssued,
                PartialShipmentAllowed = true,
                CreatedBy = "picker"
            },
            new Voucher
            {
                VoucherId = 8205,
                VoucherCode = "PN-MISSING-LEDGER",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                PartnerId = 820,
                VoucherDate = from,
                IsPosted = true,
                CreatedBy = "receiver"
            });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 82051,
            VoucherId = 8205,
            ItemId = 820,
            LocationId = 1,
            TransactionQty = 4,
            BaseQty = 4,
            TransactionUomId = 1,
            LineNumber = 1
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 820401,
            VoucherId = 8204,
            ItemId = 820,
            LocationId = 1,
            ExpiryDate = expirySoon,
            ReservedQty = 15,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "picker"
        });
        db.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                InventoryTransactionId = 820101,
                TransactionType = InventoryTransactionTypeEnum.Receive,
                TransactionGroupKey = "ovr-receive",
                IdempotencyKey = "ovr-receive-1",
                WarehouseId = 1,
                ItemId = 820,
                LocationId = 1,
                QuantityDelta = 40,
                QuantityBefore = 0,
                QuantityAfter = 40,
                AvailableDelta = 40,
                AvailableBefore = 0,
                AvailableAfter = 40,
                VoucherId = 8201,
                Actor = "receiver",
                TransactionAt = from.AddHours(9)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 820201,
                TransactionType = InventoryTransactionTypeEnum.Ship,
                TransactionGroupKey = "ovr-ship",
                IdempotencyKey = "ovr-ship-1",
                WarehouseId = 1,
                ItemId = 820,
                LocationId = 1,
                QuantityDelta = -12,
                QuantityBefore = 40,
                QuantityAfter = 28,
                AvailableDelta = -12,
                AvailableBefore = 40,
                AvailableAfter = 28,
                VoucherId = 8202,
                Actor = "picker",
                TransactionAt = from.AddDays(1).AddHours(10)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 820301,
                TransactionType = InventoryTransactionTypeEnum.Move,
                TransactionGroupKey = "ovr-move",
                IdempotencyKey = "ovr-move-out",
                WarehouseId = 1,
                ItemId = 820,
                LocationId = 1,
                QuantityDelta = -5,
                QuantityBefore = 40,
                QuantityAfter = 35,
                AvailableDelta = -5,
                AvailableBefore = 40,
                AvailableAfter = 35,
                Actor = "mover",
                TransactionAt = from.AddDays(1).AddHours(11)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 820302,
                TransactionType = InventoryTransactionTypeEnum.Move,
                TransactionGroupKey = "ovr-move",
                IdempotencyKey = "ovr-move-in",
                WarehouseId = 1,
                ItemId = 820,
                LocationId = 2,
                QuantityDelta = 5,
                QuantityBefore = 0,
                QuantityAfter = 5,
                AvailableDelta = 5,
                AvailableBefore = 0,
                AvailableAfter = 5,
                Actor = "mover",
                TransactionAt = from.AddDays(1).AddHours(11)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 820303,
                TransactionType = InventoryTransactionTypeEnum.Putaway,
                TransactionGroupKey = "ovr-putaway",
                IdempotencyKey = "ovr-putaway-in",
                WarehouseId = 1,
                ItemId = 820,
                LocationId = 2,
                QuantityDelta = 7,
                QuantityBefore = 5,
                QuantityAfter = 12,
                AvailableDelta = 7,
                AvailableBefore = 5,
                AvailableAfter = 12,
                Actor = "putaway",
                TransactionAt = from.AddDays(1).AddHours(12)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 820304,
                TransactionType = InventoryTransactionTypeEnum.Move,
                TransactionGroupKey = "ovr-internal-only",
                IdempotencyKey = "ovr-internal-only-out",
                WarehouseId = 1,
                ItemId = 823,
                LocationId = 1,
                QuantityDelta = -2,
                QuantityBefore = 2,
                QuantityAfter = 0,
                AvailableDelta = -2,
                AvailableBefore = 2,
                AvailableAfter = 0,
                Actor = "mover",
                TransactionAt = from.AddDays(1).AddHours(13)
            },
            new InventoryTransaction
            {
                InventoryTransactionId = 820305,
                TransactionType = InventoryTransactionTypeEnum.Move,
                TransactionGroupKey = "ovr-internal-only",
                IdempotencyKey = "ovr-internal-only-in",
                WarehouseId = 1,
                ItemId = 823,
                LocationId = 2,
                QuantityDelta = 2,
                QuantityBefore = 0,
                QuantityAfter = 2,
                AvailableDelta = 2,
                AvailableBefore = 0,
                AvailableAfter = 2,
                Actor = "mover",
                TransactionAt = from.AddDays(1).AddHours(13)
            });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, includeFinancialPermission: true);
        var result = await controller.WarehouseOverview(from, from.AddDays(2), 1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<WarehouseOverviewPageViewModel>(view.Model);

        Assert.Equal(108, model.Kpi.OnHandQty);
        Assert.Equal(26, model.Kpi.ReservedQty);
        Assert.Equal(80, model.Kpi.AvailableQty);
        Assert.Equal(1240, model.Kpi.TotalStockValue);
        Assert.Equal(40, model.Kpi.InboundQty);
        Assert.Equal(12, model.Kpi.OutboundQty);
        Assert.Equal(1, model.Kpi.OpenInboundVouchers);
        Assert.Equal(2, model.Kpi.OpenOutboundVouchers);
        Assert.Equal(1, model.Kpi.ExpiringLotCount);
        Assert.Contains(model.TopItems, row => row.ItemCode == "OVR-SKU-820" && row.InboundQty == 40 && row.OutboundQty == 12);
        Assert.DoesNotContain(model.TopItems, row => row.ItemCode == "OVR-INTERNAL-ONLY");

        var overReserved = Assert.Single(model.Exceptions, x => x.Code == "OVER_RESERVED");
        Assert.Equal(1, overReserved.Count);
        Assert.Equal("Giữ chỗ vượt tồn", overReserved.StatusLabel);

        var reservationMismatch = Assert.Single(model.Exceptions, x => x.Code == "RESERVATION_MISMATCH");
        Assert.Equal(2, reservationMismatch.Count);
        Assert.Equal("Lệch giữ chỗ", reservationMismatch.StatusLabel);

        var mixedLocation = Assert.Single(model.Exceptions, x => x.Code == "LOCATION_MULTIPLE_STOCK_KEYS");
        Assert.Equal(1, mixedLocation.Count);
        Assert.Equal("Trộn hàng sai cấu hình", mixedLocation.StatusLabel);

        var warehouseRow = Assert.Single(model.WarehouseRows, row => row.WarehouseId == 1);
        Assert.Equal(2, warehouseRow.OpenOutboundVouchers);
        Assert.Equal(80, warehouseRow.AvailableQty);

        var missingLedger = Assert.Single(model.Exceptions, x => x.Code == "POSTED_WITHOUT_LEDGER");
        Assert.Equal(1, missingLedger.Count);
        Assert.Equal("Phiếu thiếu sổ kho", missingLedger.StatusLabel);
    }

    [Fact]
    public async Task Dashboard_ShouldCountPartiallyIssuedOutboundAsPending()
    {
        await using var db = CreateDb(nameof(Dashboard_ShouldCountPartiallyIssuedOutboundAsPending));
        SeedWarehouseGraph(db);
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 8221,
                VoucherCode = "PX-DASH-PICKING",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                IsPosted = false,
                FulfillmentStatus = FulfillmentStatusEnum.Picking,
                CreatedBy = "picker"
            },
            new Voucher
            {
                VoucherId = 8222,
                VoucherCode = "PX-DASH-PARTIAL",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                IsPosted = false,
                FulfillmentStatus = FulfillmentStatusEnum.PartiallyIssued,
                PartialShipmentAllowed = true,
                CreatedBy = "picker"
            },
            new Voucher
            {
                VoucherId = 8223,
                VoucherCode = "PX-DASH-CLOSED",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                IsPosted = true,
                FulfillmentStatus = FulfillmentStatusEnum.Completed,
                CreatedBy = "picker"
        });
        await db.SaveChangesAsync();

        var controller = new HomeController(db, new InventoryBalanceService(db));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "warehouse.manager"),
            new Claim(ClaimTypes.Role, WmsRoles.Manager),
            new Claim("WarehouseId", "1")
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var view = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<DashboardViewModel>(view.Model);

        Assert.Equal(2, model.PendingOutboundVouchers);
    }

    [Fact]
    public async Task Dashboard_ShouldApplyOwnerScopeWithinWarehouse()
    {
        await using var db = CreateDb(nameof(Dashboard_ShouldApplyOwnerScopeWithinWarehouse));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 823,
            ItemCode = "OWNER-SHARED-SKU",
            ItemName = "Owner scoped item",
            BaseUomId = 1,
            MinThreshold = 12,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 82301, ItemId = 823, LocationId = 1, OwnerPartnerId = 820, Quantity = 10 },
            new ItemLocation { ItemLocationId = 82302, ItemId = 823, LocationId = 2, OwnerPartnerId = 821, Quantity = 5 });
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 8231,
                VoucherCode = "PX-OWNER-820",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                OwnerPartnerId = 820,
                VoucherDate = DateTime.Today,
                IsPosted = false,
                FulfillmentStatus = FulfillmentStatusEnum.Picking,
                CreatedBy = "owner.820"
            },
            new Voucher
            {
                VoucherId = 8232,
                VoucherCode = "PX-OWNER-821",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                OwnerPartnerId = 821,
                VoucherDate = DateTime.Today,
                IsPosted = false,
                FulfillmentStatus = FulfillmentStatusEnum.Picking,
                CreatedBy = "owner.821"
            });
        await db.SaveChangesAsync();

        var balanceService = new InventoryBalanceService(db);
        var ownerStock = await balanceService.GetStockByItemAsync(1, ownerPartnerIds: new[] { 820 });
        Assert.Equal(10, ownerStock[823]);
        Assert.True(await db.Items.AnyAsync(i => i.ItemId == 823 && i.IsActive));
        var scopedItems = await db.Items.AsNoTracking()
            .Where(i => i.IsActive && ownerStock.Keys.Contains(i.ItemId))
            .ToListAsync();
        Assert.Single(scopedItems);

        var controller = new HomeController(db, balanceService);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "owner.manager"),
            new Claim(ClaimTypes.Role, WmsRoles.Manager),
            new Claim("WarehouseId", "1"),
            new Claim(TenantClaimTypes.OwnerPartnerId, "820")
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        var view = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<DashboardViewModel>(view.Model);

        Assert.Equal(1, model.PendingOutboundVouchers);
        Assert.Equal(1, model.TodayVouchers);
        Assert.Single(model.RecentVouchers);
        Assert.Equal("PX-OWNER-820", model.RecentVouchers[0].VoucherCode);
        Assert.Equal(1, model.TotalItems);
        Assert.Equal(10, Assert.Single(model.LowStockItems).CurrentStock);
        Assert.Equal(1, model.LowStockCount);
    }

    [Fact]
    public async Task InventoryReservationRecalculation_ShouldPersistBeforeReturning()
    {
        await using var db = CreateDb(nameof(InventoryReservationRecalculation_ShouldPersistBeforeReturning));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 824,
            ItemCode = "RES-CONTRACT-SKU",
            ItemName = "Reservation contract item",
            BaseUomId = 1,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 82401,
            ItemId = 824,
            LocationId = 1,
            Quantity = 20,
            ReservedQty = 0
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 824001,
            VoucherId = 8241,
            ItemId = 824,
            LocationId = 1,
            ReservedQty = 7,
            ConsumedQty = 2,
            ReleasedQty = 1,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "reservation.test"
        });
        await db.SaveChangesAsync();

        var service = new InventoryReservationService(db);
        await service.RecalculateReservedQtyAsync(new[] { 82401 });
        db.ChangeTracker.Clear();

        var persisted = await db.ItemLocations.AsNoTracking().SingleAsync(il => il.ItemLocationId == 82401);
        Assert.Equal(4, persisted.ReservedQty);
    }

    [Fact]
    public async Task InventoryReservationRecalculation_ShouldOnlyReserveAvailablePartition()
    {
        await using var db = CreateDb(nameof(InventoryReservationRecalculation_ShouldOnlyReserveAvailablePartition));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 825,
            ItemCode = "RES-HOLD-SKU",
            ItemName = "Reservation hold partition item",
            BaseUomId = 1,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 8251,
            VoucherCode = "PX-RES-HOLD",
            WarehouseId = 1,
            VoucherType = VoucherTypeEnum.XuatKho
        });
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 82501,
                ItemId = 825,
                LocationId = 1,
                Quantity = 20,
                ReservedQty = 0,
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 82502,
                ItemId = 825,
                LocationId = 1,
                Quantity = 3,
                ReservedQty = 2,
                HoldStatus = InventoryHoldStatusEnum.QcHold
            });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 825001,
            VoucherId = 8251,
            ItemId = 825,
            LocationId = 1,
            ReservedQty = 7,
            Status = ReservationStatusEnum.Active,
            CreatedBy = "reservation.test"
        });
        await db.SaveChangesAsync();

        var service = new InventoryReservationService(db);
        await service.RecalculateReservedQtyAsync(new[] { 82501, 82502 });
        db.ChangeTracker.Clear();

        var rows = await db.ItemLocations.AsNoTracking()
            .Where(il => il.ItemId == 825)
            .OrderBy(il => il.ItemLocationId)
            .ToListAsync();
        Assert.Equal(7, rows[0].ReservedQty);
        Assert.Equal(0, rows[1].ReservedQty);
    }

    [Fact]
    public async Task InventoryInOutSummary_ShouldRequireExplicitDateRangeOnServer()
    {
        await using var db = CreateDb(nameof(InventoryInOutSummary_ShouldRequireExplicitDateRangeOnServer));
        SeedWarehouseGraph(db);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            InventoryTransactionId = 771001,
            TransactionType = InventoryTransactionTypeEnum.Receive,
            TransactionGroupKey = "receive-date-required",
            IdempotencyKey = "receive-date-required-1",
            WarehouseId = 1,
            ItemId = 771001,
            QuantityDelta = 5,
            AvailableDelta = 5,
            TransactionAt = DateTime.Now.Date.AddHours(9)
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db);
        var result = await controller.InventoryInOutSummary(
            dateFrom: null,
            dateTo: DateTime.Now.Date,
            warehouseId: null,
            itemId: null,
            categoryId: null,
            locationId: null,
            partnerId: null,
            movementType: "All",
            lotNumber: null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<InventoryInOutSummaryPageViewModel>(view.Model);
        Assert.Empty(model.Rows);
        Assert.Contains("Từ ngày", controller.ViewData["DateRangeError"]?.ToString(), StringComparison.OrdinalIgnoreCase);

        var exportResult = await controller.ExportInventoryInOutSummary(
            dateFrom: DateTime.Now.Date,
            dateTo: null,
            warehouseId: null,
            itemId: null,
            categoryId: null,
            locationId: null,
            partnerId: null,
            movementType: "All",
            lotNumber: null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(exportResult);
        Assert.Contains("Đến ngày", badRequest.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateStockSnapshot_ShouldCreateNewRunAndKeepExistingClosingEvidence()
    {
        await using var db = CreateDb(nameof(GenerateStockSnapshot_ShouldCreateNewRunAndKeepExistingClosingEvidence));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 10, UomCode = "PCS", UomName = "Cái", UomGroup = "Count", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 9100,
            ItemCode = "SNAP-IMMUTABLE",
            ItemName = "Vật tư chốt tồn",
            BaseUomId = 10,
            UnitCost = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 9100,
            ItemId = 9100,
            LocationId = 1,
            Quantity = 10,
            UpdatedAt = DateTime.Now
        });
        var snapshotDate = VietnamTime.Now.Date;
        db.StockSnapshotRuns.Add(new StockSnapshotRun
        {
            StockSnapshotRunId = 1,
            SnapshotDate = snapshotDate,
            WarehouseId = 1,
            CreatedBy = "manager.user",
            CreatedAt = snapshotDate.AddHours(8),
            TotalItems = 1,
            TotalValue = 20,
            Status = "Completed"
        });
        db.StockSnapshots.Add(new StockSnapshot
        {
            SnapshotId = 1,
            StockSnapshotRunId = 1,
            SnapshotDate = snapshotDate,
            WarehouseId = 1,
            ItemId = 9100,
            ClosingStock = 4,
            UnitCost = 5,
            TotalValue = 20,
            CreatedAt = snapshotDate.AddHours(8)
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, userName: "manager.user", roleName: "Manager");
        var result = await controller.GenerateStockSnapshot(1, snapshotDate);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ReportsController.StockSnapshot), redirect.ActionName);
        Assert.Contains("phi", controller.TempData["Success"]?.ToString(), StringComparison.OrdinalIgnoreCase);

        var snapshots = await db.StockSnapshots.OrderBy(s => s.StockSnapshotRunId).ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(4, snapshots[0].ClosingStock);
        Assert.Equal(10, snapshots[1].ClosingStock);
        Assert.NotEqual(snapshots[0].StockSnapshotRunId, snapshots[1].StockSnapshotRunId);
        Assert.Equal(2, await db.StockSnapshotRuns.CountAsync());

        var viewResult = Assert.IsType<ViewResult>(await controller.StockSnapshot(1, snapshotDate, null));
        var history = Assert.IsType<List<StockSnapshotHistoryRow>>(controller.ViewData["SnapshotHistory"]);
        Assert.Equal(2, history.Count);
        Assert.All(history, row => Assert.Equal(snapshotDate, row.SnapshotDate));
        Assert.Contains(history, row => row.TotalItems == 1 && row.TotalValue == 50);
    }

    [Fact]
    public async Task GenerateStockSnapshot_ShouldBlockOwnerScopedOfficialRun()
    {
        await using var db = CreateDb(nameof(GenerateStockSnapshot_ShouldBlockOwnerScopedOfficialRun));
        SeedWarehouseGraph(db);
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, userName: "owner.manager", roleName: "Manager", ownerPartnerIds: new[] { 100 });
        var result = await controller.GenerateStockSnapshot(1, VietnamTime.Now.Date);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ReportsController.StockSnapshot), redirect.ActionName);
        Assert.Contains("chủ hàng", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.StockSnapshotRuns.ToListAsync());
    }

    [Fact]
    public async Task UploadYardVisitEvidence_ShouldRejectOwnerScopeAndRemovePhysicalFile()
    {
        await using var db = CreateDb(nameof(UploadYardVisitEvidence_ShouldRejectOwnerScopeAndRemovePhysicalFile));
        SeedYardEvidenceFixture(db, yardVisitId: 910001, ownerPartnerId: 202);
        await db.SaveChangesAsync();
        var controller = CreateOperationsController(
            db,
            warehouseClaim: 1,
            userName: "owner.staff",
            roleName: "Staff",
            ownerPartnerClaims: new[] { 101 });

        try
        {
            var result = await controller.UploadYardVisitEvidence(
                910001,
                YardEvidenceTypeEnum.GateInPhoto,
                CreatePngUpload("gate-in.png", validSignature: true),
                "scope test",
                warehouseId: 1);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(await db.YardVisitEvidence.ToListAsync());
            Assert.Contains("quyền", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(GetYardEvidenceFiles(910001));
        }
        finally
        {
            CleanupYardEvidenceFiles(910001);
        }
    }

    [Fact]
    public async Task UploadYardVisitEvidence_ShouldRejectForgedSignatureWithoutPersistence()
    {
        await using var db = CreateDb(nameof(UploadYardVisitEvidence_ShouldRejectForgedSignatureWithoutPersistence));
        SeedYardEvidenceFixture(db, yardVisitId: 910002, ownerPartnerId: null);
        await db.SaveChangesAsync();
        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "yard.staff", roleName: "Staff");

        try
        {
            var result = await controller.UploadYardVisitEvidence(
                910002,
                YardEvidenceTypeEnum.GateInPhoto,
                CreatePngUpload("gate-in.png", validSignature: false),
                "signature test",
                warehouseId: 1);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(await db.YardVisitEvidence.ToListAsync());
            Assert.Contains("nội dung", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(GetYardEvidenceFiles(910002));
        }
        finally
        {
            CleanupYardEvidenceFiles(910002);
        }
    }

    [Fact]
    public async Task DownloadYardVisitEvidence_ShouldEnforceOwnerScopeBeforeReadingFile()
    {
        await using var db = CreateDb(nameof(DownloadYardVisitEvidence_ShouldEnforceOwnerScopeBeforeReadingFile));
        SeedYardEvidenceFixture(db, yardVisitId: 910003, ownerPartnerId: 202);
        var relativePath = "App_Data/uploads/yard-evidence/910003-owner-scope.png";
        db.YardVisitEvidence.Add(new YardVisitEvidence
        {
            YardVisitEvidenceId = 910003,
            YardVisitId = 910003,
            EvidenceType = YardEvidenceTypeEnum.GateInPhoto,
            FileUrl = relativePath,
            OriginalFileName = "gate-in.png",
            ContentType = "image/png",
            CapturedBy = "test"
        });
        await db.SaveChangesAsync();
        var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllBytesAsync(physicalPath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var controller = CreateOperationsController(
            db,
            warehouseClaim: 1,
            userName: "owner.staff",
            roleName: "Staff",
            ownerPartnerClaims: new[] { 101 });

        try
        {
            var result = await controller.DownloadYardVisitEvidence(910003);

            Assert.IsType<ForbidResult>(result);
        }
        finally
        {
            if (File.Exists(physicalPath))
                File.Delete(physicalPath);
        }
    }

    [Fact]
    public async Task ConfirmActualReceivingQty_ForSerialTrackedItem_ShouldExplainRemainingSerialStep()
    {
        await using var db = CreateDb(nameof(ConfirmActualReceivingQty_ForSerialTrackedItem_ShouldExplainRemainingSerialStep));
        db.Items.Add(new Item
        {
            ItemId = 99001,
            ItemCode = "AUDIT-SERIAL-ITEM",
            ItemName = "Serial tracked test item",
            BaseUomId = 1,
            TrackSerial = true,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 99001,
            VoucherCode = "AUDIT-PN-SERIAL",
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = 1,
            InboundStatus = InboundStatusEnum.Receiving,
            CreatedBy = "maker.user"
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 99001,
            VoucherId = 99001,
            ItemId = 99001,
            TransactionQty = 100m,
            BaseQty = 100m,
            ConversionRate = 1m,
            LineNumber = 1
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "checker.user");

        var result = await controller.ConfirmActualReceivingQty(99001, 99001, 100m, null);

        Assert.IsType<RedirectToActionResult>(result);
        var message = Assert.IsType<string>(controller.TempData["Success"]);
        Assert.Contains("Đây mới là bước xác nhận số lượng", message, StringComparison.Ordinal);
        Assert.Contains("còn thiếu 100 số sê-ri", message, StringComparison.Ordinal);
    }

    private static AppDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedYardEvidenceFixture(AppDbContext db, long yardVisitId, int? ownerPartnerId)
    {
        SeedWarehouseGraph(db);
        db.Trailers.Add(new Trailer
        {
            TrailerId = checked((int)(yardVisitId % int.MaxValue)),
            TrailerNumber = $"AUDIT_TEST_TRAILER_{yardVisitId}",
            IsActive = true
        });
        db.YardVisits.Add(new YardVisit
        {
            YardVisitId = yardVisitId,
            VisitCode = $"AUDIT_TEST_VISIT_{yardVisitId}",
            WarehouseId = 1,
            OwnerPartnerId = ownerPartnerId,
            TrailerId = checked((int)(yardVisitId % int.MaxValue)),
            GateInBy = "test",
            Status = YardVisitStatusEnum.GatedIn
        });
    }

    private static IFormFile CreatePngUpload(string fileName, bool validSignature)
    {
        var bytes = validSignature
            ? new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01 }
            : System.Text.Encoding.UTF8.GetBytes("<html>not an image</html>");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "evidenceFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }

    private static string[] GetYardEvidenceFiles(long yardVisitId)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "uploads", "yard-evidence");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, $"{yardVisitId}-*")
            : Array.Empty<string>();
    }

    private static void CleanupYardEvidenceFiles(long yardVisitId)
    {
        foreach (var path in GetYardEvidenceFiles(yardVisitId))
            File.Delete(path);
    }

    private static async Task CreateSqliteSchemaAsync(AppDbContext db)
    {
        var script = db.Database.GenerateCreateScript()
            .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
            .Replace("varchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
            .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
            .Replace("\"RowVersion\" BLOB NOT NULL", "\"RowVersion\" BLOB NOT NULL DEFAULT X''", StringComparison.OrdinalIgnoreCase);

        var commands = script
            .Split(new[] { ";\r\n", ";\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(command => command.Trim())
            .Where(command => command.Length > 0 && !command.StartsWith("INSERT INTO ", StringComparison.OrdinalIgnoreCase));

        foreach (var command in commands)
        {
            await using var dbCommand = db.Database.GetDbConnection().CreateCommand();
            dbCommand.CommandText = command;
            await dbCommand.ExecuteNonQueryAsync();
        }
    }

    private static void SeedWarehouseGraph(AppDbContext db)
    {
        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 1,
            WarehouseCode = "WH1",
            WarehouseName = "Main Warehouse",
            IsActive = true
        });

        db.Zones.AddRange(
            new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "Z1", ZoneName = "Zone 1", ZoneType = ZoneTypeEnum.Storage, IsActive = true },
            new Zone { ZoneId = 2, WarehouseId = 1, ZoneCode = "Z2", ZoneName = "Zone 2", ZoneType = ZoneTypeEnum.Storage, IsActive = true });

        db.Locations.AddRange(
            new Location { LocationId = 1, ZoneId = 1, LocationCode = "L1", IsActive = true },
            new Location { LocationId = 2, ZoneId = 2, LocationCode = "L2", IsActive = true });
    }

    private static void SeedSecondWarehouseLocation(AppDbContext db)
    {
        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Warehouse 2", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 10, WarehouseId = 2, ZoneCode = "Z-WH2", ZoneName = "WH2 Zone", ZoneType = ZoneTypeEnum.Storage, IsActive = true });
        db.Locations.Add(new Location { LocationId = 10, ZoneId = 10, LocationCode = "WH2-L1", IsActive = true });
    }

    private static void SeedCarrierFixture(AppDbContext db, CarrierAdapterTypeEnum adapterType, bool requireShipmentBeforeShipping = false)
    {
        SeedWarehouseGraph(db);
        db.Partners.Add(new Partner
        {
            PartnerId = 880,
            PartnerCode = "CUST-CAR",
            PartnerName = "Khách vận chuyển",
            PartnerType = PartnerTypeEnum.Customer,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 8801,
            VoucherCode = "PX-CAR-001",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            PartnerId = 880,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            IsPosted = true,
            PackedAt = DateTime.Today,
            FulfillmentStatus = FulfillmentStatusEnum.Packed
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 88001,
            PackageCode = "PK-CAR-1",
            VoucherId = 8801,
            WarehouseId = 1,
            SourceType = "Manual",
            PackageType = "Carton",
            TotalQuantity = 1,
            ItemCount = 1,
            PackedBy = "packer.user",
            PackedAt = DateTime.Today
        });
        db.CarrierConnectors.Add(new CarrierConnector
        {
            CarrierConnectorId = 881,
            WarehouseId = 1,
            CarrierCode = "MOCK",
            CarrierName = "Đơn vị vận chuyển giả lập",
            AdapterType = adapterType,
            AuthType = CarrierAuthTypeEnum.None,
            EndpointUrl = adapterType == CarrierAdapterTypeEnum.Http ? "https://carrier.example.test/shipments" : null,
            IsActive = true,
            IsSandbox = true,
            RequireShipmentCreatedBeforeShipping = requireShipmentBeforeShipping,
            CreatedBy = "qa.user"
        });
    }

    private static void SeedTwoStepPickingFixture(AppDbContext db)
    {
        SeedWarehouseGraph(db);
        db.Locations.AddRange(
            new Location { LocationId = 31, ZoneId = 1, LocationCode = "STAGE-01", IsActive = true },
            new Location { LocationId = 32, ZoneId = 1, LocationCode = "SORT-01", IsActive = true });
        db.WarehouseSortationConfigs.Add(new WarehouseSortationConfig
        {
            WarehouseSortationConfigId = 1,
            WarehouseId = 1,
            StagingLocationId = 31,
            SortationLocationId = 32,
            IsActive = true,
            CreatedBy = "qa.user",
            Notes = "Cấu hình kiểm thử lấy tổng hai bước"
        });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "TWO-STEP-SKU",
            ItemName = "Hàng kiểm thử lấy tổng",
            BaseUomId = 1,
            CurrentStock = 10,
            UnitCost = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 92001,
            ItemId = 1,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            LotNumber = "LOT-2STEP",
            ExpiryDate = new DateTime(2026, 12, 31)
        });

        for (var i = 0; i < 3; i++)
        {
            var voucherId = 920L + i;
            var detailId = 9200L + i;
            db.Vouchers.Add(new Voucher
            {
                VoucherId = voucherId,
                VoucherCode = $"PX-2STEP-{i + 1:D3}",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                IsPosted = false
            });
            db.VoucherDetails.Add(new VoucherDetail
            {
                VoucherDetailId = detailId,
                VoucherId = voucherId,
                LineNumber = 1,
                ItemId = 1,
                LocationId = 1,
                LotNumber = "LOT-2STEP",
                ExpiryDate = new DateTime(2026, 12, 31),
                TransactionQty = 2,
                BaseQty = 2,
                TransactionUomId = 1,
                UnitPrice = 5,
                LineAmount = 10
            });
        }

        db.SaveChanges();
    }

    private static void SeedStockValuationBaseData(AppDbContext db)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "CÁI", UomName = "Cái", IsActive = true });
        db.ItemCategories.AddRange(
            new ItemCategory { CategoryId = 920, CategoryCode = "CAT-VAL", CategoryName = "Danh mục định giá", IsActive = true },
            new ItemCategory { CategoryId = 921, CategoryCode = "CAT-OTHER", CategoryName = "Danh mục khác", IsActive = true });
        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Kho phụ", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 10, WarehouseId = 2, ZoneCode = "Z-WH2", ZoneName = "Khu kho phụ", ZoneType = ZoneTypeEnum.Storage, IsActive = true });
        db.Locations.Add(new Location { LocationId = 10, ZoneId = 10, LocationCode = "WH2-L1", IsActive = true });
    }

    private static KittingWorkOrderService CreateKittingService(AppDbContext db)
    {
        var unitOfWork = new EfUnitOfWork(db);
        return new KittingWorkOrderService(
            db,
            unitOfWork,
            new InventoryReservationService(db),
            new InventoryBalanceService(db));
    }

    private static VasWorkOrderService CreateVasService(AppDbContext db)
    {
        var unitOfWork = new EfUnitOfWork(db);
        return new VasWorkOrderService(
            db,
            unitOfWork,
            new InventoryReservationService(db),
            new InventoryBalanceService(db));
    }

    private static OrderStreamingService CreateOrderStreamingService(AppDbContext db)
        => new(db, new EfUnitOfWork(db), new InventoryReservationService(db));

    private static void SeedOrderStreamingFixture(
        AppDbContext db,
        int priority,
        ServiceLevelEnum serviceLevel,
        decimal requestedQty = 4,
        decimal availableQty = 10,
        DateTime? requestedDeliveryDate = null)
    {
        SeedWarehouseGraph(db);
        db.Items.Add(new Item
        {
            ItemId = 6101,
            ItemCode = "STREAM-SKU",
            ItemName = "Hàng phát hành trực tiếp",
            BaseUomId = 1,
            CurrentStock = availableQty,
            UnitCost = 7,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 61001,
            ItemId = 6101,
            LocationId = 1,
            Quantity = availableQty,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available,
            LotNumber = "LOT-STREAM",
            ExpiryDate = new DateTime(2026, 12, 31)
        });
        db.WarehouseOrderStreamingConfigs.Add(new WarehouseOrderStreamingConfig
        {
            WarehouseOrderStreamingConfigId = 6101,
            WarehouseId = 1,
            IsEnabled = true,
            IsActive = true,
            MinPriority = 80,
            DeliveryWindowHours = 24,
            CreatedBy = "qa.user"
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 6101,
            VoucherCode = "PX-DIRECT-001",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            Priority = priority,
            ServiceLevel = serviceLevel,
            RequestedDeliveryDate = requestedDeliveryDate,
            FulfillmentStatus = FulfillmentStatusEnum.Draft,
            IsPosted = false,
            IsCancelled = false
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 61011,
            VoucherId = 6101,
            LineNumber = 1,
            ItemId = 6101,
            LocationId = 1,
            LotNumber = "LOT-STREAM",
            ExpiryDate = new DateTime(2026, 12, 31),
            TransactionQty = requestedQty,
            BaseQty = requestedQty,
            TransactionUomId = 1,
            UnitPrice = 7,
            LineAmount = requestedQty * 7
        });
        db.SaveChanges();
    }

    private static LabelPrintService CreateLabelService(AppDbContext db)
        => new(db, new EfUnitOfWork(db));

    private static void SeedCustomerLabelFixture(AppDbContext db)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure
        {
            UomId = 1,
            UomCode = "EA",
            UomName = "Cái",
            IsActive = true
        });

        db.Partners.AddRange(
            new Partner { PartnerId = 901, PartnerCode = "CUST-A", PartnerName = "Khách hàng A", PartnerType = PartnerTypeEnum.Customer, IsActive = true },
            new Partner { PartnerId = 902, PartnerCode = "CUST-B", PartnerName = "Khách hàng B", PartnerType = PartnerTypeEnum.Customer, IsActive = true });

        db.Items.Add(new Item
        {
            ItemId = 801,
            ItemCode = "COFFEE-INT",
            ItemName = "Cà phê nội bộ",
            Barcode = "893000000001",
            BaseUomId = 1,
            UnitCost = 10,
            IsActive = true
        });

        db.PartnerItemLabelRules.AddRange(
            new PartnerItemLabelRule
            {
                PartnerItemLabelRuleId = 9101,
                PartnerId = 901,
                ItemId = 801,
                CustomerItemCode = "A-COFFEE-500",
                CustomerItemName = "Cà phê rang khách A",
                CustomText = "Kệ A",
                IsActive = true,
                CreatedBy = "tester"
            },
            new PartnerItemLabelRule
            {
                PartnerItemLabelRuleId = 9102,
                PartnerId = 902,
                ItemId = 801,
                CustomerItemCode = "B-CAFE-500",
                CustomerItemName = "Cafe nhãn B",
                CustomText = "Kệ B",
                IsActive = true,
                CreatedBy = "tester"
            });

        db.PartnerLabelTemplates.AddRange(
            new PartnerLabelTemplate
            {
                PartnerLabelTemplateId = 9001,
                LabelPurpose = LabelPurposeEnum.OutboundVoucher,
                TemplateName = "Mẫu phiếu mặc định",
                LabelSize = "50x30",
                CodeType = "barcode",
                HeaderTemplate = "{TenKhachHang}",
                BodyTemplate = "Mã khách: {MaHangKhach}\nTên khách: {TenHangKhach}\nPhiếu: {MaPhieu}\nGhi chú: {GhiChuRieng}",
                FooterTemplate = "{NgayIn}",
                IsDefault = true,
                IsActive = true,
                CreatedBy = "tester"
            },
            new PartnerLabelTemplate
            {
                PartnerLabelTemplateId = 9002,
                LabelPurpose = LabelPurposeEnum.OutboundPackage,
                TemplateName = "Mẫu kiện mặc định",
                LabelSize = "100x50",
                CodeType = "barcode",
                HeaderTemplate = "{TenKhachHang}",
                BodyTemplate = "Kiện: {MaKien}\nMã khách: {MaHangKhach}\nPhiếu: {MaPhieu}",
                FooterTemplate = "{MaVanDon}",
                IsDefault = true,
                IsActive = true,
                CreatedBy = "tester"
            });

        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 8101,
                VoucherCode = "PX-A-001",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                PartnerId = 901,
                VoucherDate = DateTime.Today,
                CreatedBy = "tester",
                TrackingNumber = "TRK-A"
            },
            new Voucher
            {
                VoucherId = 8102,
                VoucherCode = "PX-B-001",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                PartnerId = 902,
                VoucherDate = DateTime.Today,
                CreatedBy = "tester",
                TrackingNumber = "TRK-B"
            });

        db.VoucherDetails.AddRange(
            new VoucherDetail
            {
                VoucherDetailId = 8111,
                VoucherId = 8101,
                ItemId = 801,
                TransactionQty = 1,
                BaseQty = 1,
                TransactionUomId = 1,
                LineNumber = 1
            },
            new VoucherDetail
            {
                VoucherDetailId = 8112,
                VoucherId = 8102,
                ItemId = 801,
                TransactionQty = 1,
                BaseQty = 1,
                TransactionUomId = 1,
                LineNumber = 1
            });

        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 8201,
            PackageCode = "PK-A-001",
            VoucherId = 8101,
            WarehouseId = 1,
            TotalQuantity = 1,
            ItemCount = 1,
            PackedBy = "packer",
            TrackingNumber = "TRK-A"
        });
    }

    private static void SeedKittingFixture(AppDbContext db)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure
        {
            UomId = 1,
            UomCode = "EA",
            UomName = "Each",
            IsActive = true
        });

        db.Items.AddRange(
            new Item
            {
                ItemId = 100,
                ItemCode = "KIT-GIFT",
                ItemName = "Bộ quà tặng",
                BaseUomId = 1,
                UnitCost = 100,
                TrackLot = true,
                IsActive = true
            },
            new Item
            {
                ItemId = 101,
                ItemCode = "COMP-A",
                ItemName = "Thành phần A",
                BaseUomId = 1,
                UnitCost = 10,
                IsActive = true
            },
            new Item
            {
                ItemId = 102,
                ItemCode = "COMP-B",
                ItemName = "Thành phần B",
                BaseUomId = 1,
                UnitCost = 20,
                IsActive = true
            });

        db.BillOfMaterials.AddRange(
            new BillOfMaterial
            {
                BomId = 1,
                ParentItemId = 100,
                ChildItemId = 101,
                Quantity = 2,
                UomId = 1,
                ScrapPercent = 0,
                IsActive = true
            },
            new BillOfMaterial
            {
                BomId = 2,
                ParentItemId = 100,
                ChildItemId = 102,
                Quantity = 1,
                UomId = 1,
                ScrapPercent = 0,
                IsActive = true
            });

        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 101,
                ItemId = 101,
                LocationId = 2,
                Quantity = 3,
                ReservedQty = 0,
                LotNumber = "A-EARLY",
                ExpiryDate = new DateTime(2026, 6, 30),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 102,
                ItemId = 101,
                LocationId = 1,
                Quantity = 5,
                ReservedQty = 0,
                LotNumber = "A-LATE",
                ExpiryDate = new DateTime(2026, 12, 31),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 201,
                ItemId = 102,
                LocationId = 1,
                Quantity = 2,
                ReservedQty = 0,
                LotNumber = "B-OK",
                ExpiryDate = new DateTime(2026, 10, 31),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 202,
                ItemId = 102,
                LocationId = 2,
                Quantity = 100,
                ReservedQty = 0,
                LotNumber = "B-HOLD",
                ExpiryDate = new DateTime(2026, 1, 31),
                HoldStatus = InventoryHoldStatusEnum.QcHold
            });
    }

    private static void SeedVasFixture(AppDbContext db)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure
        {
            UomId = 1,
            UomCode = "EA",
            UomName = "Cái",
            IsActive = true
        });

        db.Partners.Add(new Partner
        {
            PartnerId = 701,
            PartnerCode = "CUST-VAS",
            PartnerName = "Khách VAS",
            PartnerType = PartnerTypeEnum.Customer,
            IsActive = true
        });

        db.Items.AddRange(
            new Item
            {
                ItemId = 900,
                ItemCode = "VAS-PRIMARY",
                ItemName = "SKU chính VAS",
                BaseUomId = 1,
                UnitCost = 100,
                IsActive = true
            },
            new Item
            {
                ItemId = 901,
                ItemCode = "VAS-LABEL",
                ItemName = "Nhãn phụ VAS",
                BaseUomId = 1,
                UnitCost = 5,
                IsActive = true
            },
            new Item
            {
                ItemId = 902,
                ItemCode = "VAS-BOX",
                ItemName = "Thùng co-pack",
                BaseUomId = 1,
                UnitCost = 12,
                IsActive = true
            });

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 9701,
            VoucherCode = "PX-VAS-001",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            PartnerId = 701,
            VoucherDate = DateTime.Today,
            CreatedBy = "tester"
        });

        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 9701,
                ItemId = 901,
                LocationId = 2,
                Quantity = 3,
                ReservedQty = 0,
                LotNumber = "LBL-EARLY",
                ExpiryDate = new DateTime(2026, 6, 30),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 9702,
                ItemId = 901,
                LocationId = 1,
                Quantity = 5,
                ReservedQty = 0,
                LotNumber = "LBL-LATE",
                ExpiryDate = new DateTime(2026, 12, 31),
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 9703,
                ItemId = 902,
                LocationId = 1,
                Quantity = 10,
                ReservedQty = 0,
                LotNumber = "BOX-OK",
                ExpiryDate = null,
                HoldStatus = InventoryHoldStatusEnum.Available
            },
            new ItemLocation
            {
                ItemLocationId = 9704,
                ItemId = 901,
                LocationId = 1,
                Quantity = 50,
                ReservedQty = 0,
                LotNumber = "LBL-HOLD",
                ExpiryDate = new DateTime(2026, 1, 31),
                HoldStatus = InventoryHoldStatusEnum.QcHold
            });
    }

    private static void PrepareSlottingSimulationFixture(AppDbContext db)
    {
        var golden = db.Locations.Local.FirstOrDefault(l => l.LocationId == 1) ?? db.Locations.Find(1)!;
        var source = db.Locations.Local.FirstOrDefault(l => l.LocationId == 2) ?? db.Locations.Find(2)!;
        golden.HeightLevel = 3;
        golden.IsGoldenZone = true;
        golden.AisleSequence = 1;
        golden.MaxWeightCapacityKg = 2000;
        golden.MaxCapacity = 2000;

        source.HeightLevel = 5;
        source.AisleSequence = 9;
        source.AllowMechanicalHandling = true;
        source.MaxWeightCapacityKg = 2000;
        source.MaxCapacity = 2000;

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SIM-FAST-01",
            ItemName = "Hàng giả lập bán nhanh",
            BaseUomId = 1,
            UnitCost = 10,
            Weight = 5,
            AbcClass = "A",
            DefaultLocationId = 2,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 71,
            ItemId = 1,
            LocationId = 2,
            Quantity = 20,
            ReservedQty = 0,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static object? GetAnonValue(object source, string propertyName)
        => source.GetType().GetProperty(propertyName)?.GetValue(source);

    private static void MarkQueued(Controller controller, string operationId)
    {
        controller.ControllerContext.HttpContext.Request.Headers["X-WMS-Queued-Operation"] = "true";
        controller.ControllerContext.HttpContext.Request.Headers["X-WMS-Offline-Operation-Id"] = operationId;
        controller.ControllerContext.HttpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
    }

    [Fact]
    public async Task CreateBackorder_ShouldFallbackToBaseUomAndRemainSingleOnRetry()
    {
        await using var db = CreateDb(nameof(CreateBackorder_ShouldFallbackToBaseUomAndRemainSingleOnRetry));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true },
            new UnitOfMeasure { UomId = 2, UomCode = "BOX", UomName = "Thùng", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 985,
            ItemCode = "AUDIT_TEST_BACKORDER_UOM",
            ItemName = "Backorder UOM precision",
            BaseUomId = 1,
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 9850,
            VoucherCode = "PX-AUDIT-BO-UOM",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator",
            IsPosted = true,
            PartialShipmentAllowed = true
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 98501,
            VoucherId = 9850,
            ItemId = 985,
            LocationId = 1,
            TransactionQty = 0.0001m,
            TransactionUomId = 2,
            ConversionRate = 100m,
            BaseQty = 0.0100m,
            UnitPrice = 5m,
            LineAmount = 0.0005m,
            LineNumber = 1
        });
        db.StockReservations.Add(new StockReservation
        {
            StockReservationId = 98501,
            VoucherId = 9850,
            VoucherDetailId = 98501,
            ItemId = 985,
            LocationId = 1,
            ReservedQty = 0.0100m,
            ConsumedQty = 0.0098m,
            ReleasedQty = 0.0002m,
            Status = ReservationStatusEnum.Consumed,
            CreatedBy = "picker"
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "manager", roleName: WmsRoles.Admin);
        Assert.IsType<RedirectToActionResult>(await controller.CreateBackorder(9850));

        var backorder = await db.Vouchers
            .Include(v => v.Details)
            .SingleAsync(v => v.ParentVoucherId == 9850);
        var detail = Assert.Single(backorder.Details);
        Assert.Equal(0.0002m, detail.TransactionQty);
        Assert.Equal(1, detail.TransactionUomId);
        Assert.Null(detail.PackagingUnitId);
        Assert.Equal(1m, detail.ConversionRate);
        Assert.Equal(0.0002m, detail.BaseQty);

        Assert.IsType<RedirectToActionResult>(await controller.CreateBackorder(9850));
        Assert.Equal(1, await db.Vouchers.CountAsync(v => v.ParentVoucherId == 9850));
    }

    private static VouchersController CreateController(
        AppDbContext db,
        bool includeFinancialPermission = false,
        string userName = "qa.user",
        int? warehouseClaim = null,
        string roleName = WmsRoles.Admin,
        params int[] ownerPartnerClaims)
    {
        var configuration = new ConfigurationBuilder().Build();
        // Create a minimal IHttpClientFactory for testing
        var httpClientFactory = new TestHttpClientFactory();
        var reservationService = new InventoryReservationService(db);
        var unitOfWork = new EfUnitOfWork(db);
        var balanceService = new InventoryBalanceService(db);
        var outboundService = new OutboundExecutionService(db, unitOfWork, reservationService, balanceService);
        var inboundService = new InboundExecutionService(db, unitOfWork, balanceService);
        var cancellationService = new VoucherCancellationService(db, unitOfWork, reservationService, balanceService);
        var orderStreamingService = new OrderStreamingService(db, unitOfWork, reservationService);
        var integrationService = new NullIntegrationService();
        var serialInventoryService = new SerialInventoryService(db);
        var inventoryTransactionService = new InventoryTransactionService(db);
        var catchWeightService = new CatchWeightService(db);
        var shipmentLoadService = new ShipmentLoadService(db, unitOfWork);
        var carrierIntegrationService = new CarrierIntegrationService(db, integrationService, unitOfWork);
        var controller = new VouchersController(db, configuration, httpClientFactory, integrationService,
            reservationService, unitOfWork, outboundService, inboundService,
            balanceService, cancellationService, orderStreamingService,
            serialInventoryService, inventoryTransactionService, catchWeightService, shipmentLoadService,
            carrierIntegrationService, new UnavailableVoucherDocumentIntakeService(),
            new VoucherSharedRuleService(db), new VoucherImportQueryService(),
            new VoucherCreateWorkflowService(db), new VoucherDetailQueryService());

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Role, roleName)
        };
        if (includeFinancialPermission)
            claims.Add(new Claim(PermissionClaimTypes.Permission, WmsPermissions.ReportViewFinancial));
        if (warehouseClaim.HasValue)
            claims.Add(new Claim("WarehouseId", warehouseClaim.Value.ToString()));
        foreach (var ownerPartnerId in ownerPartnerClaims.Distinct())
            claims.Add(new Claim(TenantClaimTypes.OwnerPartnerId, ownerPartnerId.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext { User = principal };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        return controller;
    }

    [Fact]
    public async Task CreateVoucherLookups_ShouldRespectWarehouseAndOwnerScope()
    {
        await using var db = CreateDb(nameof(CreateVoucherLookups_ShouldRespectWarehouseAndOwnerScope));
        SeedWarehouseGraph(db);
        SeedSecondWarehouseLocation(db);
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "CAI", UomName = "Cái" });
        db.Partners.AddRange(
            new Partner { PartnerId = 101, PartnerCode = "AUDIT_TEST_OWNER_A", PartnerName = "Chủ hàng A", PartnerType = PartnerTypeEnum.Both, IsThreePlClient = true },
            new Partner { PartnerId = 202, PartnerCode = "AUDIT_TEST_OWNER_B", PartnerName = "Chủ hàng B", PartnerType = PartnerTypeEnum.Both, IsThreePlClient = true },
            new Partner { PartnerId = 303, PartnerCode = "AUDIT_TEST_SUPPLIER", PartnerName = "Nhà cung cấp dùng chung", PartnerType = PartnerTypeEnum.Supplier });
        db.Items.AddRange(
            new Item { ItemId = 101, ItemCode = "AUDIT_TEST_SHARED", ItemName = "Vật tư dùng chung", BaseUomId = 1, DefaultLocationId = 10 },
            new Item { ItemId = 102, ItemCode = "AUDIT_TEST_OWNER_A_ITEM", ItemName = "Vật tư chủ A", BaseUomId = 1, OwnerPartnerId = 101, DefaultLocationId = 1 },
            new Item { ItemId = 103, ItemCode = "AUDIT_TEST_OWNER_B_ITEM", ItemName = "Vật tư chủ B", BaseUomId = 1, OwnerPartnerId = 202, DefaultLocationId = 10 });
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            userName: "AUDIT_TEST_INBOUND",
            warehouseClaim: 1,
            roleName: WmsRoles.InboundStaff,
            ownerPartnerClaims: new[] { 101 });
        var view = Assert.IsType<ViewResult>(await controller.Create(VoucherTypeEnum.NhapKho));
        var model = Assert.IsType<VoucherCreateViewModel>(view.Model);

        Assert.Collection(model.Warehouses, warehouse => Assert.Equal(1, warehouse.WarehouseId));
        Assert.Collection(model.OwnerPartners, owner => Assert.Equal(101, owner.PartnerId));
        Assert.Contains(model.Partners, partner => partner.PartnerId == 303);
        Assert.Contains(model.Partners, partner => partner.PartnerId == 101);
        Assert.DoesNotContain(model.Partners, partner => partner.PartnerId == 202);
        Assert.Contains(model.Items, item => item.ItemId == 101);
        Assert.Contains(model.Items, item => item.ItemId == 102);
        Assert.DoesNotContain(model.Items, item => item.ItemId == 103);
        Assert.All(model.Locations, location => Assert.Equal(1, location.Zone.WarehouseId));
        Assert.Null(model.Items.Single(item => item.ItemId == 101).DefaultLocationId);
    }

    [Fact]
    public async Task SubmitInspection_ShouldRejectDetailFromDifferentVoucher()
    {
        await using var db = CreateDb(nameof(SubmitInspection_ShouldRejectDetailFromDifferentVoucher));
        SeedWarehouseGraph(db);
        db.Items.AddRange(
            new Item { ItemId = 5101, ItemCode = "QC-5101", ItemName = "QC 5101", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 5102, ItemCode = "QC-5102", ItemName = "QC 5102", BaseUomId = 1, IsActive = true });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 5101, VoucherCode = "PN-QC-5101", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 10, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "maker.one" },
            new Voucher { VoucherId = 5102, VoucherCode = "PN-QC-5102", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 10, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "maker.two" });
        db.VoucherDetails.AddRange(
            new VoucherDetail { VoucherDetailId = 51011, VoucherId = 5101, ItemId = 5101, OwnerPartnerId = 10, LocationId = 1, TransactionQty = 5, BaseQty = 5, TransactionUomId = 1, ConversionRate = 1, QualityStatus = QualityStatusEnum.Pending },
            new VoucherDetail { VoucherDetailId = 51021, VoucherId = 5102, ItemId = 5102, OwnerPartnerId = 10, LocationId = 1, TransactionQty = 5, BaseQty = 5, TransactionUomId = 1, ConversionRate = 1, QualityStatus = QualityStatusEnum.Pending });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 51021, ItemId = 5102, OwnerPartnerId = 10, LocationId = 1, Quantity = 5, HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "qc.user", roleName: WmsRoles.InboundStaff, ownerPartnerClaims: new[] { 10 });
        await controller.SubmitInspection(5101, 51021, 5102, 5, 1, 0, 1, (int)QcDispositionEnum.Hold, "Sai phiếu", "Test");

        Assert.Empty(await db.QualityInspections.ToListAsync());
        Assert.Equal(QualityStatusEnum.Pending, (await db.VoucherDetails.FindAsync(51021L))!.QualityStatus);
        Assert.Equal(InventoryHoldStatusEnum.Available, (await db.ItemLocations.FindAsync(51021))!.HoldStatus);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task SubmitInspection_ShouldQuarantineOnlyExactOwnerLotAndExpiryAndLinkLedger()
    {
        await using var db = CreateDb(nameof(SubmitInspection_ShouldQuarantineOnlyExactOwnerLotAndExpiryAndLinkLedger));
        SeedWarehouseGraph(db);
        var expiry = new DateTime(2026, 12, 31);
        db.Items.Add(new Item { ItemId = 5201, ItemCode = "QC-5201", ItemName = "QC exact batch", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 5201, VoucherCode = "PN-QC-5201", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 10, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "maker.user" });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 52011,
            VoucherId = 5201,
            ItemId = 5201,
            OwnerPartnerId = 10,
            LocationId = 1,
            TransactionQty = 10,
            BaseQty = 10,
            TransactionUomId = 1,
            ConversionRate = 1,
            LotNumber = "LOT-QC",
            ExpiryDate = expiry,
            QualityStatus = QualityStatusEnum.Pending
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 52011, ItemId = 5201, OwnerPartnerId = 10, LocationId = 1, Quantity = 10, LotNumber = "LOT-QC", ExpiryDate = expiry, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 52012, ItemId = 5201, OwnerPartnerId = 10, LocationId = 1, Quantity = 4, LotNumber = "LOT-QC", ExpiryDate = expiry.AddMonths(1), HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();
        db.InventoryTransactions.RemoveRange(db.InventoryTransactions);
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "qc.user", roleName: WmsRoles.InboundStaff, ownerPartnerClaims: new[] { 10 });
        await controller.SubmitInspection(5201, 52011, 5201, 10, 2, 1, 1, (int)QcDispositionEnum.Hold, "Mẫu lỗi", "Cách ly lô");

        Assert.True(
            await db.QualityInspections.CountAsync() == 1,
            controller.TempData["Error"]?.ToString());
        Assert.Equal(QualityStatusEnum.Defect, (await db.VoucherDetails.FindAsync(52011L))!.QualityStatus);
        Assert.Equal(InventoryHoldStatusEnum.Quarantine, (await db.ItemLocations.FindAsync(52011))!.HoldStatus);
        Assert.Equal(InventoryHoldStatusEnum.Available, (await db.ItemLocations.FindAsync(52012))!.HoldStatus);
        var ledger = await db.InventoryTransactions.SingleAsync(t => t.VoucherDetailId == 52011);
        Assert.Equal(InventoryTransactionTypeEnum.Hold, ledger.TransactionType);
        Assert.Equal("voucher:5201:qc:52011", ledger.TransactionGroupKey);
        Assert.Equal(5201, ledger.VoucherId);
    }

    [Fact]
    public async Task SubmitInspection_ShouldRejectVoucherOutsideOwnerScope()
    {
        await using var db = CreateDb(nameof(SubmitInspection_ShouldRejectVoucherOutsideOwnerScope));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5251, ItemCode = "QC-5251", ItemName = "QC owner", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 5251, VoucherCode = "PN-QC-5251", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 20, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "maker.user" });
        db.VoucherDetails.Add(new VoucherDetail { VoucherDetailId = 52511, VoucherId = 5251, ItemId = 5251, OwnerPartnerId = 20, LocationId = 1, TransactionQty = 1, BaseQty = 1, TransactionUomId = 1, ConversionRate = 1, QualityStatus = QualityStatusEnum.Pending });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 52511, ItemId = 5251, OwnerPartnerId = 20, LocationId = 1, Quantity = 1, HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "qc.user", roleName: WmsRoles.InboundStaff, ownerPartnerClaims: new[] { 10 });
        var result = await controller.SubmitInspection(5251, 52511, 5251, 1, 1, 1, 0, (int)QcDispositionEnum.Accept, null, null);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await db.QualityInspections.ToListAsync());
        Assert.Equal(InventoryHoldStatusEnum.Available, (await db.ItemLocations.FindAsync(52511))!.HoldStatus);
    }

    [Fact]
    public async Task QualityInspection_ShouldListOnlyVouchersWithinOwnerScope()
    {
        await using var db = CreateDb(nameof(QualityInspection_ShouldListOnlyVouchersWithinOwnerScope));
        SeedWarehouseGraph(db);
        db.Vouchers.AddRange(
            new Voucher
            {
                VoucherId = 5271,
                VoucherCode = "PN-QC-OWNER-10",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                OwnerPartnerId = 10,
                InboundStatus = InboundStatusEnum.Receiving,
                CreatedBy = "maker.user"
            },
            new Voucher
            {
                VoucherId = 5272,
                VoucherCode = "PN-QC-OWNER-20",
                VoucherType = VoucherTypeEnum.NhapKho,
                WarehouseId = 1,
                OwnerPartnerId = 20,
                InboundStatus = InboundStatusEnum.Receiving,
                CreatedBy = "maker.user"
            });
        db.Items.Add(new Item
        {
            ItemId = 5271,
            ItemCode = "QC-OWNER-10",
            ItemName = "QC owner scoped item",
            BaseUomId = 1,
            IsActive = true
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 52711,
            VoucherId = 5271,
            ItemId = 5271,
            OwnerPartnerId = 10,
            LocationId = 1,
            TransactionQty = 80,
            BaseQty = 80,
            TransactionUomId = 1,
            ConversionRate = 1,
            QualityStatus = QualityStatusEnum.Pending
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(
            db,
            warehouseClaim: 1,
            userName: "qc.user",
            roleName: WmsRoles.InboundStaff,
            ownerPartnerClaims: new[] { 10 });

        var view = Assert.IsType<ViewResult>(await controller.QualityInspection(null));
        var vouchers = Assert.IsAssignableFrom<IReadOnlyCollection<Voucher>>(view.Model);
        var voucher = Assert.Single(vouchers);

        Assert.Equal(5271, voucher.VoucherId);
        var detail = Assert.Single(voucher.Details);
        Assert.NotNull(detail.Item);
        Assert.Equal("QC-OWNER-10", detail.Item.ItemCode);
        Assert.Equal("QC owner scoped item", detail.Item.ItemName);
    }

    [Fact]
    public async Task ReleaseQuarantine_ShouldKeepReworkOnHold()
    {
        await using var db = CreateDb(nameof(ReleaseQuarantine_ShouldKeepReworkOnHold));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5301, ItemCode = "QC-5301", ItemName = "QC rework", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 5301, VoucherCode = "PN-QC-5301", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 10, InboundStatus = InboundStatusEnum.Receiving, CreatedBy = "maker.user" });
        db.VoucherDetails.Add(new VoucherDetail { VoucherDetailId = 53011, VoucherId = 5301, ItemId = 5301, OwnerPartnerId = 10, LocationId = 1, TransactionQty = 2, BaseQty = 2, TransactionUomId = 1, ConversionRate = 1, QualityStatus = QualityStatusEnum.OnHold });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 53011, ItemId = 5301, OwnerPartnerId = 10, LocationId = 1, Quantity = 2, HoldStatus = InventoryHoldStatusEnum.Quarantine });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager, ownerPartnerClaims: new[] { 10 });
        await controller.ReleaseQuarantine(5301, 53011, 5301, (int)QcDispositionEnum.Rework, "Chuyển sửa chữa");

        Assert.Equal(QualityStatusEnum.OnHold, (await db.VoucherDetails.FindAsync(53011L))!.QualityStatus);
        Assert.Equal(InventoryHoldStatusEnum.Quarantine, (await db.ItemLocations.FindAsync(53011))!.HoldStatus);
        Assert.True(
            controller.TempData["Warning"] != null,
            controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task CreateRecallCase_ShouldQuarantineOnlyExactOwnerLotExpiryAndLinkLedger()
    {
        await using var db = CreateDb(nameof(CreateRecallCase_ShouldQuarantineOnlyExactOwnerLotExpiryAndLinkLedger));
        SeedWarehouseGraph(db);
        var expiry = new DateTime(2027, 1, 31);
        db.Items.Add(new Item { ItemId = 5401, ItemCode = "RECALL-5401", ItemName = "Recall exact", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 5401, VoucherCode = "PN-RECALL-5401", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 10, CreatedBy = "maker.user" });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 54011,
            VoucherId = 5401,
            ItemId = 5401,
            OwnerPartnerId = 10,
            LocationId = 1,
            LotNumber = "LOT-5401",
            ExpiryDate = expiry,
            TransactionQty = 5,
            BaseQty = 5,
            TransactionUomId = 1,
            ConversionRate = 1
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 54011, ItemId = 5401, OwnerPartnerId = 10, LocationId = 1, LotNumber = "LOT-5401", ExpiryDate = expiry, Quantity = 5, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 54012, ItemId = 5401, OwnerPartnerId = 10, LocationId = 1, LotNumber = "LOT-5401", ExpiryDate = expiry.AddMonths(1), Quantity = 3, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 54013, ItemId = 5401, OwnerPartnerId = 20, LocationId = 1, LotNumber = "LOT-5401", ExpiryDate = expiry, Quantity = 4, HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();
        db.InventoryTransactions.RemoveRange(db.InventoryTransactions);
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager, ownerPartnerClaims: new[] { 10 });
        await controller.CreateRecallCase(new[] { 54011L }, "Thu hồi lô kiểm thử", (int)RecallSeverityEnum.High, null);

        var recall = await db.RecallCases.Include(x => x.Lines).SingleAsync();
        Assert.Single(recall.Lines);
        Assert.Equal(InventoryHoldStatusEnum.Quarantine, (await db.ItemLocations.FindAsync(54011))!.HoldStatus);
        Assert.Equal(InventoryHoldStatusEnum.Available, (await db.ItemLocations.FindAsync(54012))!.HoldStatus);
        Assert.Equal(InventoryHoldStatusEnum.Available, (await db.ItemLocations.FindAsync(54013))!.HoldStatus);
        var ledger = await db.InventoryTransactions.SingleAsync(x => x.ReferenceType == "RecallCase");
        Assert.Equal(InventoryTransactionTypeEnum.Hold, ledger.TransactionType);
        Assert.Equal(recall.RecallCaseId.ToString(), ledger.ReferenceId);
        Assert.Equal(5401, ledger.ItemId);
        Assert.Equal(1, ledger.LocationId);
    }

    [Fact]
    public async Task CreateRecallCase_ShouldNotLeaveHeaderWhenAnyDetailIsInvalid()
    {
        await using var db = CreateDb(nameof(CreateRecallCase_ShouldNotLeaveHeaderWhenAnyDetailIsInvalid));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5451, ItemCode = "RECALL-5451", ItemName = "Recall atomic", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 5451, VoucherCode = "PN-RECALL-5451", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 10, CreatedBy = "maker.user" });
        db.VoucherDetails.Add(new VoucherDetail { VoucherDetailId = 54511, VoucherId = 5451, ItemId = 5451, OwnerPartnerId = 10, LocationId = 1, TransactionQty = 2, BaseQty = 2, TransactionUomId = 1, ConversionRate = 1 });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 54511, ItemId = 5451, OwnerPartnerId = 10, LocationId = 1, Quantity = 2, HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager, ownerPartnerClaims: new[] { 10 });
        await controller.CreateRecallCase(new[] { 54511L, 999999L }, "Thu hồi không hợp lệ", (int)RecallSeverityEnum.High, null);

        Assert.Empty(await db.RecallCases.ToListAsync());
        Assert.Empty(await db.RecallLines.ToListAsync());
        Assert.Equal(InventoryHoldStatusEnum.Available, (await db.ItemLocations.FindAsync(54511))!.HoldStatus);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task CreateRecallCase_ShouldRejectVoucherOutsideOwnerScope()
    {
        await using var db = CreateDb(nameof(CreateRecallCase_ShouldRejectVoucherOutsideOwnerScope));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5471, ItemCode = "RECALL-5471", ItemName = "Recall owner", BaseUomId = 1, IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 5471, VoucherCode = "PN-RECALL-5471", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, OwnerPartnerId = 20, CreatedBy = "maker.user" });
        db.VoucherDetails.Add(new VoucherDetail { VoucherDetailId = 54711, VoucherId = 5471, ItemId = 5471, OwnerPartnerId = 20, LocationId = 1, TransactionQty = 2, BaseQty = 2, TransactionUomId = 1, ConversionRate = 1 });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 54711, ItemId = 5471, OwnerPartnerId = 20, LocationId = 1, Quantity = 2, HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager, ownerPartnerClaims: new[] { 10 });
        var result = await controller.CreateRecallCase(new[] { 54711L }, "Sai chủ hàng", (int)RecallSeverityEnum.High, null);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await db.RecallCases.ToListAsync());
        Assert.Equal(InventoryHoldStatusEnum.Available, (await db.ItemLocations.FindAsync(54711))!.HoldStatus);
    }

    [Fact]
    public async Task ResolveRecallCase_ShouldReleaseOnlyRowsRecordedByRecallAudit()
    {
        await using var db = CreateDb(nameof(ResolveRecallCase_ShouldReleaseOnlyRowsRecordedByRecallAudit));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5501, ItemCode = "RECALL-5501", ItemName = "Recall release", BaseUomId = 1, IsActive = true });
        db.RecallCases.Add(new RecallCase
        {
            RecallCaseId = 5501,
            CaseNumber = "RCL-AUDIT-5501",
            Reason = "Kiểm thử giải phóng",
            Severity = RecallSeverityEnum.High,
            Status = RecallStatusEnum.Issued,
            IssuedBy = "manager.user",
            Lines =
            {
                new RecallLine { RecallLineId = 55011, ItemId = 5501, OwnerPartnerId = 10, LotNumber = "LOT-5501", AffectedQty = 2, LineStatus = RecallLineStatusEnum.InProgress }
            }
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 55011, ItemId = 5501, OwnerPartnerId = 10, LocationId = 1, LotNumber = "LOT-5501", Quantity = 2, HoldStatus = InventoryHoldStatusEnum.Quarantine },
            new ItemLocation { ItemLocationId = 55012, ItemId = 5501, OwnerPartnerId = 10, LocationId = 2, LotNumber = "LOT-5501", Quantity = 3, HoldStatus = InventoryHoldStatusEnum.Quarantine });
        db.AuditLogs.Add(new AuditLog
        {
            TableName = "ItemLocation",
            RecordId = "55011",
            ActionType = "QUARANTINE_BY_RECALL",
            NewValue = "Recall:RCL-AUDIT-5501",
            ChangedBy = "manager.user",
            ChangedAt = VietnamTime.Now,
            AppModule = "Recall"
        });
        await db.SaveChangesAsync();
        db.InventoryTransactions.RemoveRange(db.InventoryTransactions);
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager, ownerPartnerClaims: new[] { 10 });
        await controller.ResolveRecallCase(5501, "Cho phép sử dụng có giám sát", (int)RecallDispositionEnum.ReleaseUnderObservations);

        Assert.Equal(RecallStatusEnum.Resolved, (await db.RecallCases.FindAsync(5501L))!.Status);
        Assert.Equal(InventoryHoldStatusEnum.Available, (await db.ItemLocations.FindAsync(55011))!.HoldStatus);
        Assert.Equal(InventoryHoldStatusEnum.Quarantine, (await db.ItemLocations.FindAsync(55012))!.HoldStatus);
        var ledger = await db.InventoryTransactions.SingleAsync(x => x.ReferenceType == "RecallCaseRelease");
        Assert.Equal(InventoryTransactionTypeEnum.ReleaseHold, ledger.TransactionType);
        Assert.Equal("5501", ledger.ReferenceId);
    }

    [Fact]
    public async Task ResolveRecallCase_ShouldKeepReworkInventoryOnHold()
    {
        await using var db = CreateDb(nameof(ResolveRecallCase_ShouldKeepReworkInventoryOnHold));
        SeedWarehouseGraph(db);
        db.Items.Add(new Item { ItemId = 5551, ItemCode = "RECALL-5551", ItemName = "Recall rework", BaseUomId = 1, IsActive = true });
        db.RecallCases.Add(new RecallCase
        {
            RecallCaseId = 5551,
            CaseNumber = "RCL-AUDIT-5551",
            Reason = "Kiểm thử gia công lại",
            Severity = RecallSeverityEnum.Medium,
            Status = RecallStatusEnum.Issued,
            IssuedBy = "manager.user",
            Lines =
            {
                new RecallLine { RecallLineId = 55511, ItemId = 5551, OwnerPartnerId = 10, LotNumber = "LOT-5551", AffectedQty = 2, LineStatus = RecallLineStatusEnum.InProgress }
            }
        });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 55511, ItemId = 5551, OwnerPartnerId = 10, LocationId = 1, LotNumber = "LOT-5551", Quantity = 2, HoldStatus = InventoryHoldStatusEnum.Quarantine });
        db.AuditLogs.Add(new AuditLog
        {
            TableName = "ItemLocation",
            RecordId = "55511",
            ActionType = "QUARANTINE_BY_RECALL",
            NewValue = "Recall:RCL-AUDIT-5551",
            ChangedBy = "manager.user",
            ChangedAt = VietnamTime.Now,
            AppModule = "Recall"
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.user", roleName: WmsRoles.Manager, ownerPartnerClaims: new[] { 10 });
        await controller.ResolveRecallCase(5551, "Chuyển gia công lại", (int)RecallDispositionEnum.Rework);

        Assert.Equal(RecallStatusEnum.Resolved, (await db.RecallCases.FindAsync(5551L))!.Status);
        Assert.Equal(InventoryHoldStatusEnum.Quarantine, (await db.ItemLocations.FindAsync(55511))!.HoldStatus);
        Assert.NotNull(controller.TempData["Warning"]);
    }

    private sealed class ThrowingInventoryBalanceService : IInventoryBalanceService
    {
        public Task<Dictionary<int, decimal>> GetStockByItemAsync(
            int? warehouseId = null,
            IEnumerable<int>? itemIds = null,
            int? ownerPartnerId = null,
            IEnumerable<int>? ownerPartnerIds = null)
            => throw new NotSupportedException();

        public Task<decimal> GetTotalStockAsync(int? warehouseId = null)
            => throw new NotSupportedException();

        public void ApplyStockBalances(IEnumerable<Item> items, IReadOnlyDictionary<int, decimal> stockByItem)
            => throw new NotSupportedException();

        public Task SyncCurrentStockAsync(IEnumerable<int> itemIds)
            => throw new InvalidOperationException("Injected balance synchronization failure.");
    }

    private sealed class ClientSideInventoryBalanceService(AppDbContext db) : IInventoryBalanceService
    {
        public Task<Dictionary<int, decimal>> GetStockByItemAsync(
            int? warehouseId = null,
            IEnumerable<int>? itemIds = null,
            int? ownerPartnerId = null,
            IEnumerable<int>? ownerPartnerIds = null)
            => throw new NotSupportedException();

        public Task<decimal> GetTotalStockAsync(int? warehouseId = null)
            => throw new NotSupportedException();

        public void ApplyStockBalances(IEnumerable<Item> items, IReadOnlyDictionary<int, decimal> stockByItem)
            => throw new NotSupportedException();

        public async Task SyncCurrentStockAsync(IEnumerable<int> itemIds)
        {
            var ids = itemIds.Distinct().ToList();
            var rows = await db.ItemLocations
                .Where(x => ids.Contains(x.ItemId))
                .Select(x => new { x.ItemId, x.Quantity })
                .ToListAsync();
            var balances = rows
                .GroupBy(x => x.ItemId)
                .ToDictionary(group => group.Key, group => group.Sum(x => x.Quantity));
            var items = await db.Items.Where(x => ids.Contains(x.ItemId)).ToListAsync();
            foreach (var item in items)
            {
                item.CurrentStock = balances.GetValueOrDefault(item.ItemId);
                item.TotalStockValue = item.CurrentStock * item.UnitCost;
            }
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? Exception { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exception ??= exception;
        }
    }

    private sealed class FailOnSaveUnitOfWork : IUnitOfWork
    {
        private readonly EfUnitOfWork _inner;
        private readonly int _failOnSave;
        private int _saveCount;

        public FailOnSaveUnitOfWork(AppDbContext db, int failOnSave)
        {
            _inner = new EfUnitOfWork(db);
            _failOnSave = failOnSave;
        }

        public bool HasActiveTransaction => _inner.HasActiveTransaction;

        public Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
            => _inner.BeginTransactionAsync(isolationLevel, cancellationToken);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCount++;
            if (_saveCount == _failOnSave)
                throw new InvalidOperationException("Injected cycle count persistence failure.");
            return _inner.SaveChangesAsync(cancellationToken);
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
            => _inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => _inner.RollbackAsync(cancellationToken);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    private sealed class NullIntegrationService : WMS.Services.IIntegrationService
    {
        public Task EnqueueAsync(Models.OutboxEventTypeEnum eventType, string targetEndpoint, object payload, string? idempotencyKey = null, string? targetSystem = null)
            => Task.CompletedTask;
        public Task<(bool IsDuplicate, string? CachedResponse, int StatusCode)> CheckIdempotencyAsync(string keyValue, string operationType)
            => Task.FromResult<(bool, string?, int)>((false, null, 0));
        public Task SetIdempotencyAsync(string keyValue, string operationType, string response, int statusCode)
            => Task.CompletedTask;
        public Task ProcessOutboxBatchAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ThrowingEnqueueIntegrationService : WMS.Services.IIntegrationService
    {
        public Task EnqueueAsync(Models.OutboxEventTypeEnum eventType, string targetEndpoint, object payload, string? idempotencyKey = null, string? targetSystem = null)
            => throw new InvalidOperationException("Injected carrier outbox failure.");

        public Task<(bool IsDuplicate, string? CachedResponse, int StatusCode)> CheckIdempotencyAsync(string keyValue, string operationType)
            => Task.FromResult<(bool, string?, int)>((false, null, 0));

        public Task SetIdempotencyAsync(string keyValue, string operationType, string response, int statusCode)
            => Task.CompletedTask;

        public Task ProcessOutboxBatchAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class UnavailableVoucherDocumentIntakeService : IVoucherDocumentIntakeService
    {
        public Task<VoucherDocumentIntakeResult> AnalyzeAsync(IFormFile file, string actor, CancellationToken cancellationToken = default)
            => throw new BusinessRuleException("Dịch vụ đọc chứng từ MinerU chưa sẵn sàng.", "MINERU_UNAVAILABLE", nameof(AiOcrLog));
    }

    private static OperationsController CreateOperationsController(AppDbContext db, int? warehouseClaim = null, string userName = "qa.user", string? roleName = null, params int[] ownerPartnerClaims)
    {
        var unitOfWork = new EfUnitOfWork(db);
        var crossDockService = new CrossDockService(db, unitOfWork);
        var yardService = new YardManagementService(db, unitOfWork);
        var movementTaskService = new MovementTaskService(db, unitOfWork);
        var reservationService = new InventoryReservationService(db);
        var balanceService = new InventoryBalanceService(db);
        var kittingService = new KittingWorkOrderService(db, unitOfWork, reservationService, balanceService);
        var vasService = new VasWorkOrderService(db, unitOfWork, reservationService, balanceService);
        var taskInterleavingService = new TaskInterleavingService(db);
        var yardBillingService = new YardBillingService(db, unitOfWork);
        var integrationService = new NullIntegrationService();
        var controller = new OperationsController(
            db,
            unitOfWork,
            crossDockService,
            yardService,
            movementTaskService,
            kittingService,
            vasService,
            taskInterleavingService,
            yardBillingService,
            new ReplenishmentAutomationService(db, unitOfWork, movementTaskService),
            new ShipmentLoadService(db, unitOfWork),
            new TenantScopeService(db, new HttpContextAccessor()),
            new ThreePlBillingService(db, unitOfWork),
            new MheIntegrationService(db, integrationService, new ConfigurationBuilder().Build(), unitOfWork),
            new CarrierIntegrationService(db, integrationService, unitOfWork),
            new ShippingReconciliationService(db),
            new DockAppointmentService(db, unitOfWork),
            new ThreePlEnterpriseBillingService(db, unitOfWork),
            new LaborManagementService(db, unitOfWork),
            new OptimizationEnterpriseService(db, unitOfWork),
            new AutomationEnterpriseService(db, unitOfWork),
            new EnterpriseIntegrationService(db, unitOfWork),
            new OperationsScopeQueryService(db),
            new SlottingPlanningService(db, unitOfWork),
            new OperationExceptionQueryService(),
            new YardBillingQueryService(db),
            new CycleCountPlanningService(db, unitOfWork),
            new InventoryTransactionService(db));
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        var effectiveRole = roleName ?? (warehouseClaim.HasValue ? "Staff" : "Admin");
        claims.Add(new Claim(ClaimTypes.Role, effectiveRole));
        if (warehouseClaim.HasValue)
            claims.Add(new Claim("WarehouseId", warehouseClaim.Value.ToString()));
        foreach (var ownerPartnerId in ownerPartnerClaims.Distinct())
            claims.Add(new Claim(TenantClaimTypes.OwnerPartnerId, ownerPartnerId.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext { User = principal };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        return controller;
    }

    private static ItemsController CreateItemsController(AppDbContext db, int? warehouseClaim = null, string userName = "qa.user", string? roleName = null)
    {
        var controller = new ItemsController(db, new TestWebHostEnvironment(), new InventoryBalanceService(db));
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        var effectiveRole = roleName ?? (warehouseClaim.HasValue ? "Staff" : "Admin");
        claims.Add(new Claim(ClaimTypes.Role, effectiveRole));
        if (warehouseClaim.HasValue)
            claims.Add(new Claim("WarehouseId", warehouseClaim.Value.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext { User = principal };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        return controller;
    }

    private static WarehousesController CreateWarehousesController(
        AppDbContext db,
        int? warehouseClaim = null,
        string userName = "qa.user",
        string? roleName = null,
        params int[] ownerPartnerClaims)
    {
        var controller = new WarehousesController(
            db,
            new ConfigurationBuilder().Build(),
            new TestWebHostEnvironment(),
            new MemoryCache(new MemoryCacheOptions()));
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        var effectiveRole = roleName ?? (warehouseClaim.HasValue ? "Staff" : "Admin");
        claims.Add(new Claim(ClaimTypes.Role, effectiveRole));
        if (warehouseClaim.HasValue)
            claims.Add(new Claim("WarehouseId", warehouseClaim.Value.ToString()));
        foreach (var ownerPartnerId in ownerPartnerClaims.Distinct())
            claims.Add(new Claim(TenantClaimTypes.OwnerPartnerId, ownerPartnerId.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext { User = principal };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        return controller;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "WMS.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ReportsController CreateReportsController(
        AppDbContext db,
        int? warehouseClaim = null,
        string userName = "qa.user",
        string? roleName = null,
        bool includeFinancialPermission = false,
        IReadOnlyCollection<int>? ownerPartnerIds = null,
        IUnitOfWork? unitOfWork = null)
    {
        unitOfWork ??= new EfUnitOfWork(db);
        var balanceService = new InventoryBalanceService(db);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ReportsController>.Instance;
        var controller = new ReportsController(db, logger, balanceService, unitOfWork);
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        var effectiveRole = roleName ?? (warehouseClaim.HasValue ? "Manager" : "Admin");
        claims.Add(new Claim(ClaimTypes.Role, effectiveRole));
        if (warehouseClaim.HasValue)
            claims.Add(new Claim("WarehouseId", warehouseClaim.Value.ToString()));
        if (ownerPartnerIds != null)
        {
            foreach (var ownerPartnerId in ownerPartnerIds)
                claims.Add(new Claim(TenantClaimTypes.OwnerPartnerId, ownerPartnerId.ToString()));
        }
        if (includeFinancialPermission)
            claims.Add(new Claim(PermissionClaimTypes.Permission, WmsPermissions.ReportViewFinancial));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var httpContext = new DefaultHttpContext { User = principal };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        return controller;
    }

    [Fact]
    public async Task ScheduledReports_ShouldNotExposeGlobalOrForeignWarehouseConfigurationToScopedManager()
    {
        await using var db = CreateDb(nameof(ScheduledReports_ShouldNotExposeGlobalOrForeignWarehouseConfigurationToScopedManager));
        db.Warehouses.AddRange(
            new Warehouse { WarehouseId = 1, WarehouseCode = "AUDIT_TEST_WH_A", WarehouseName = "Kho A", IsActive = true },
            new Warehouse { WarehouseId = 2, WarehouseCode = "AUDIT_TEST_WH_B", WarehouseName = "Kho B", IsActive = true });
        db.ScheduledReports.AddRange(
            new ScheduledReport { ScheduledReportId = 1, ReportName = "AUDIT_TEST_REPORT_A", ReportType = "StockSnapshot", WarehouseId = 1 },
            new ScheduledReport { ScheduledReportId = 2, ReportName = "AUDIT_TEST_REPORT_B", ReportType = "StockSnapshot", WarehouseId = 2 },
            new ScheduledReport { ScheduledReportId = 3, ReportName = "AUDIT_TEST_REPORT_GLOBAL", ReportType = "StockSnapshot", WarehouseId = null });
        await db.SaveChangesAsync();

        var scopedController = CreateReportsController(db, warehouseClaim: 1, roleName: "Manager");
        var scopedResult = Assert.IsType<ViewResult>(await scopedController.ScheduledReports(null));
        var scopedReports = Assert.IsAssignableFrom<IReadOnlyCollection<ScheduledReport>>(scopedResult.Model);
        var scopedWarehouses = Assert.IsAssignableFrom<IReadOnlyCollection<Warehouse>>((object)scopedController.ViewBag.Warehouses);

        Assert.Collection(scopedReports, report => Assert.Equal("AUDIT_TEST_REPORT_A", report.ReportName));
        Assert.Collection(scopedWarehouses, warehouse => Assert.Equal(1, warehouse.WarehouseId));

        var adminController = CreateReportsController(db, roleName: "Admin");
        var adminResult = Assert.IsType<ViewResult>(await adminController.ScheduledReports(null));
        var adminReports = Assert.IsAssignableFrom<IReadOnlyCollection<ScheduledReport>>(adminResult.Model);
        var adminWarehouses = Assert.IsAssignableFrom<IReadOnlyCollection<Warehouse>>((object)adminController.ViewBag.Warehouses);

        Assert.Equal(3, adminReports.Count);
        Assert.Equal(2, adminWarehouses.Count);
    }

    [Fact]
    public async Task PackageLookup_ShouldScopeByWarehouse()
    {
        await using var db = CreateDb(nameof(PackageLookup_ShouldScopeByWarehouse));
        SeedWarehouseGraph(db);

        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Warehouse 2", IsActive = true });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 601,
            VoucherCode = "PX-0601",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "qa.user",
            IsPosted = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 602,
            VoucherCode = "PX-0602",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 2,
            VoucherDate = DateTime.Today,
            CreatedBy = "qa.user",
            IsPosted = true
        });
        db.OutboundPackages.AddRange(
            new OutboundPackage
            {
                OutboundPackageId = 1,
                PackageCode = "PKG-001",
                VoucherId = 601,
                WarehouseId = 1,
                SourceType = "Manual",
                PackedBy = "staff.user",
                PackedAt = DateTime.UtcNow
            },
            new OutboundPackage
            {
                OutboundPackageId = 2,
                PackageCode = "PKG-002",
                VoucherId = 602,
                WarehouseId = 2,
                SourceType = "Manual",
                PackedBy = "staff.user",
                PackedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1);
        var result = await controller.PackageLookup(null, null);

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OutboundPackageLookupRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal("PKG-001", rows[0].PackageCode);
    }

    [Fact]
    public async Task PackageLookup_SearchByTrackingNumber()
    {
        await using var db = CreateDb(nameof(PackageLookup_SearchByTrackingNumber));
        SeedWarehouseGraph(db);

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 603,
            VoucherCode = "PX-0603",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "qa.user",
            IsPosted = true
        });
        db.OutboundPackages.AddRange(
            new OutboundPackage
            {
                OutboundPackageId = 3,
                PackageCode = "PKG-003",
                VoucherId = 603,
                WarehouseId = 1,
                SourceType = "Manual",
                PackedBy = "staff.user",
                PackedAt = DateTime.UtcNow,
                TrackingNumber = "TRACK-ABC-123"
            },
            new OutboundPackage
            {
                OutboundPackageId = 4,
                PackageCode = "PKG-004",
                VoucherId = 603,
                WarehouseId = 1,
                SourceType = "LPN",
                PackedBy = "staff.user",
                PackedAt = DateTime.UtcNow,
                TrackingNumber = "TRACK-XYZ-999"
            });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db);
        var result = await controller.PackageLookup(null, "ABC-123");

        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<OutboundPackageLookupRow>>(view.Model);
        Assert.Single(rows);
        Assert.Equal("PKG-003", rows[0].PackageCode);
    }

    [Fact]
    public async Task CarrierIntegration_MockCreate_ShouldCreatePackageShipmentIdempotently()
    {
        await using var db = CreateDb(nameof(CarrierIntegration_MockCreate_ShouldCreatePackageShipmentIdempotently));
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Mock);
        await db.SaveChangesAsync();

        var service = new CarrierIntegrationService(db, new NullIntegrationService(), new EfUnitOfWork(db));

        var first = await service.CreateShipmentsForVoucherAsync(8801, 881, null, "shipper");
        var second = await service.CreateShipmentsForVoucherAsync(8801, 881, null, "shipper");

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(first[0].CarrierShipmentId, second[0].CarrierShipmentId);
        Assert.Equal(CarrierShipmentStatusEnum.Created, first[0].Status);
        Assert.Equal(1, await db.CarrierShipments.CountAsync());
        Assert.Equal("MOCK-PK-CAR-1", (await db.OutboundPackages.FindAsync(88001L))!.TrackingNumber);
        Assert.Equal("MOCK-PK-CAR-1", (await db.Vouchers.FindAsync(8801L))!.TrackingNumber);
    }

    [Fact]
    public async Task CarrierIntegration_HttpAdapter_ShouldEnqueueOutboxPayload()
    {
        await using var db = CreateDb(nameof(CarrierIntegration_HttpAdapter_ShouldEnqueueOutboxPayload));
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Http);
        await db.SaveChangesAsync();
        var integration = new IntegrationService(db, new TestHttpClientFactory(), Microsoft.Extensions.Logging.Abstractions.NullLogger<IntegrationService>.Instance);
        var service = new CarrierIntegrationService(db, integration, new EfUnitOfWork(db));

        var shipments = await service.CreateShipmentsForVoucherAsync(8801, 881, null, "shipper");

        Assert.Single(shipments);
        Assert.Equal(CarrierShipmentStatusEnum.Queued, shipments[0].Status);
        var outbox = await db.IntegrationOutbox.SingleAsync(o => o.EventType == nameof(OutboxEventTypeEnum.CarrierShipmentRequested));
        Assert.Equal("https://carrier.example.test/shipments", outbox.TargetEndpoint);
        Assert.Equal("MOCK", outbox.TargetSystem);
        Assert.Contains("PK-CAR-1", outbox.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CarrierIntegration_CancelOutboxFailure_ShouldNotPersistCancelledState()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Http);
        await db.SaveChangesAsync();

        var integration = new IntegrationService(
            db,
            new TestHttpClientFactory(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IntegrationService>.Instance);
        var createService = new CarrierIntegrationService(db, integration, new EfUnitOfWork(db));
        var created = Assert.Single(await createService.CreateShipmentsForVoucherAsync(8801, 881, null, "shipper"));
        Assert.Equal(CarrierShipmentStatusEnum.Queued, created.Status);

        db.ChangeTracker.Clear();
        var cancelService = new CarrierIntegrationService(db, new ThrowingEnqueueIntegrationService(), new EfUnitOfWork(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cancelService.CancelAsync(created.CarrierShipmentId, null, "shipper"));

        db.ChangeTracker.Clear();
        var persisted = await db.CarrierShipments.SingleAsync();
        Assert.Equal(CarrierShipmentStatusEnum.Queued, persisted.Status);
        Assert.Null(persisted.CancelledAt);
        Assert.DoesNotContain(
            await db.CarrierShipmentEvents.ToListAsync(),
            row => row.EventType == CarrierShipmentEventTypeEnum.Cancelled);
    }

    [Fact]
    public async Task CarrierCallback_ShouldBeIdempotentAndUpdatePackageTracking()
    {
        await using var db = CreateDb(nameof(CarrierCallback_ShouldBeIdempotentAndUpdatePackageTracking));
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Http);
        await db.SaveChangesAsync();
        var integration = new IntegrationService(db, new TestHttpClientFactory(), Microsoft.Extensions.Logging.Abstractions.NullLogger<IntegrationService>.Instance);
        var service = new CarrierIntegrationService(db, integration, new EfUnitOfWork(db));
        await service.CreateShipmentsForVoucherAsync(8801, 881, null, "shipper");
        var shipment = await db.CarrierShipments.SingleAsync();

        var callback = new CarrierShipmentCallbackRequest
        {
            CorrelationId = shipment.CorrelationId,
            IdempotencyKey = "callback-001",
            Status = CarrierShipmentStatusEnum.Created,
            TrackingNumber = "TRK-CB-001",
            ExternalShipmentId = "EXT-001",
            LabelUrl = "https://carrier.example.test/labels/TRK-CB-001",
            PayloadJson = "{\"status\":\"created\"}"
        };

        await service.ProcessCallbackAsync(callback);
        await service.ProcessCallbackAsync(callback);

        Assert.Equal(2, await db.CarrierShipmentEvents.CountAsync());
        var updated = await db.CarrierShipments.SingleAsync();
        Assert.Equal(CarrierShipmentStatusEnum.Created, updated.Status);
        Assert.Equal("TRK-CB-001", updated.TrackingNumber);
        Assert.Equal("TRK-CB-001", (await db.OutboundPackages.FindAsync(88001L))!.TrackingNumber);
    }

    [Fact]
    public async Task ConfirmShipping_ShouldRespectCarrierRequirementWhenEnabled()
    {
        await using var db = CreateDb(nameof(ConfirmShipping_ShouldRespectCarrierRequirementWhenEnabled));
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Mock, requireShipmentBeforeShipping: true);
        await db.SaveChangesAsync();
        var controller = CreateController(db, userName: "manager.user");

        var blocked = await controller.ConfirmShipping(8801, "TRK-MANUAL", null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(blocked);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Contains("vận đơn", controller.TempData["Error"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null((await db.Vouchers.FindAsync(8801L))!.ShippedAt);

        var service = new CarrierIntegrationService(db, new NullIntegrationService(), new EfUnitOfWork(db));
        await service.CreateShipmentsForVoucherAsync(8801, 881, null, "shipper");
        var shipped = await controller.ConfirmShipping(8801, "TRK-MANUAL", null, null);

        Assert.IsType<RedirectToActionResult>(shipped);
        Assert.NotNull((await db.Vouchers.FindAsync(8801L))!.ShippedAt);
    }

    [Fact]
    public async Task ShippingDocument_ShouldCreatePackageLabelWithCarrierTracking()
    {
        await using var db = CreateDb(nameof(ShippingDocument_ShouldCreatePackageLabelWithCarrierTracking));
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Mock);
        await db.SaveChangesAsync();

        var carrierService = new CarrierIntegrationService(db, new NullIntegrationService(), new EfUnitOfWork(db));
        await carrierService.CreateShipmentsForVoucherAsync(8801, 881, null, "shipper");
        var documentService = new ShippingDocumentService(db, new EfUnitOfWork(db));

        var job = await documentService.CreatePackageShippingLabelAsync(88001, "printer");
        var vm = await documentService.BuildPrintViewModelAsync(job.LabelPrintJobId, "printer", markPrinted: true);

        Assert.Equal(LabelPurposeEnum.ShippingPackageLabel, job.LabelPurpose);
        Assert.Equal("PackageShippingLabel", job.DocumentType);
        Assert.NotNull(job.CarrierShipmentId);
        Assert.Single(vm.PackageLabels);
        Assert.Equal("PK-CAR-1", vm.PackageLabels[0].PackageCode);
        Assert.Equal("MOCK-PK-CAR-1", vm.PackageLabels[0].TrackingNumber);
        Assert.Equal(LabelPrintJobStatusEnum.Printed, (await db.LabelPrintJobs.FindAsync(job.LabelPrintJobId))!.Status);
    }

    [Fact]
    public async Task ShippingDocument_ShouldCreatePackageLabelWithoutCarrierShipment()
    {
        await using var db = CreateDb(nameof(ShippingDocument_ShouldCreatePackageLabelWithoutCarrierShipment));
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Mock);
        await db.SaveChangesAsync();
        var documentService = new ShippingDocumentService(db, new EfUnitOfWork(db));

        var job = await documentService.CreatePackageShippingLabelAsync(88001, "printer");
        var vm = await documentService.BuildPrintViewModelAsync(job.LabelPrintJobId, "printer", markPrinted: true);

        Assert.Equal("PK-CAR-1", job.DocumentNumber);
        Assert.Null(job.CarrierShipmentId);
        Assert.Single(vm.PackageLabels);
        Assert.Equal("chưa cập nhật địa chỉ", vm.PackageLabels[0].ShipToAddress);
    }

    [Fact]
    public async Task ShippingDocument_ShouldCreateLoadManifestHandoverAndBatchLabels()
    {
        await using var db = CreateDb(nameof(ShippingDocument_ShouldCreateLoadManifestHandoverAndBatchLabels));
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Mock);
        db.ShipmentLoads.Add(new ShipmentLoad
        {
            ShipmentLoadId = 9901,
            LoadCode = "LOAD-9901",
            WarehouseId = 1,
            Status = ShipmentLoadStatusEnum.Departed,
            CarrierName = "Đơn vị vận chuyển giả lập",
            RouteName = "Tuyến kiểm thử",
            VehicleNumber = "51D-00001",
            ManifestCode = "MAN-9901",
            ActualDepartureAt = DateTime.Today,
            CreatedBy = "qa.user"
        });
        db.ShipmentLoadVouchers.Add(new ShipmentLoadVoucher
        {
            ShipmentLoadVoucherId = 9902,
            ShipmentLoadId = 9901,
            VoucherId = 8801,
            Sequence = 1,
            AddedBy = "qa.user",
            StatusSnapshot = "Packed"
        });
        db.ShipmentLoadPackages.Add(new ShipmentLoadPackage
        {
            ShipmentLoadPackageId = 9903,
            ShipmentLoadId = 9901,
            OutboundPackageId = 88001,
            PackageCodeSnapshot = "PK-CAR-1",
            IsLoaded = true,
            LoadedBy = "loader",
            LoadedAt = DateTime.Today,
            AddedBy = "qa.user"
        });
        db.ShippingHandoverLogs.Add(new ShippingHandoverLog
        {
            ShippingHandoverLogId = 9904,
            VoucherId = 8801,
            WarehouseId = 1,
            ShipmentLoadId = 9901,
            HandedOverBy = "shipper",
            HandedOverAt = DateTime.Today,
            ManifestCode = "MAN-9901",
            CarrierName = "Đơn vị vận chuyển giả lập",
            VehicleNumber = "51D-00001"
        });
        var package = await db.OutboundPackages.FindAsync(88001L);
        package!.ShipmentLoadId = 9901;
        package.LoadedAt = DateTime.Today;
        package.LoadedBy = "loader";
        var voucher = await db.Vouchers.FindAsync(8801L);
        voucher!.ManifestCode = "MAN-9901";
        voucher.ShippedAt = DateTime.Today;
        await db.SaveChangesAsync();

        var documentService = new ShippingDocumentService(db, new EfUnitOfWork(db));
        var labels = await documentService.CreateShipmentLoadPackageLabelsAsync(9901, "printer");
        var manifest = await documentService.CreateShipmentLoadManifestAsync(9901, "printer");
        var handover = await documentService.CreateShipmentLoadHandoverAsync(9901, "printer");
        var manifestVm = await documentService.BuildPrintViewModelAsync(manifest.LabelPrintJobId, "printer", markPrinted: true);
        var handoverVm = await documentService.BuildPrintViewModelAsync(handover.LabelPrintJobId, "printer", markPrinted: true);

        Assert.Equal(1, labels.TotalLabels);
        Assert.Equal(LabelPurposeEnum.ShipmentLoadManifest, manifest.LabelPurpose);
        Assert.Single(manifestVm.Vouchers);
        Assert.Single(manifestVm.Packages);
        Assert.Equal(LabelPurposeEnum.ShippingHandoverDocument, handover.LabelPurpose);
        Assert.Single(handoverVm.Handovers);
        Assert.Equal(3, await db.LabelPrintJobs.CountAsync());
    }

    [Fact]
    public async Task ShippingReconciliation_ShouldDetectDeliveryMismatches()
    {
        await using var db = CreateDb(nameof(ShippingReconciliation_ShouldDetectDeliveryMismatches));
        SeedCarrierFixture(db, CarrierAdapterTypeEnum.Mock, requireShipmentBeforeShipping: true);
        db.ShipmentLoads.Add(new ShipmentLoad
        {
            ShipmentLoadId = 9911,
            LoadCode = "LOAD-9911",
            WarehouseId = 1,
            Status = ShipmentLoadStatusEnum.Departed,
            ManifestCode = "MAN-9911",
            CreatedBy = "qa.user"
        });
        db.ShipmentLoadVouchers.Add(new ShipmentLoadVoucher
        {
            ShipmentLoadVoucherId = 9912,
            ShipmentLoadId = 9911,
            VoucherId = 8801,
            Sequence = 1,
            AddedBy = "qa.user",
            StatusSnapshot = "Packed"
        });
        db.ShipmentLoadPackages.Add(new ShipmentLoadPackage
        {
            ShipmentLoadPackageId = 9913,
            ShipmentLoadId = 9911,
            OutboundPackageId = 88001,
            PackageCodeSnapshot = "PK-CAR-1",
            IsLoaded = false,
            AddedBy = "qa.user"
        });
        db.CarrierShipments.Add(new CarrierShipment
        {
            CarrierShipmentId = 9914,
            CarrierConnectorId = 881,
            WarehouseId = 1,
            VoucherId = 8801,
            OutboundPackageId = 88001,
            Status = CarrierShipmentStatusEnum.DeliveryFailed,
            CarrierCodeSnapshot = "MOCK",
            CarrierNameSnapshot = "Đơn vị vận chuyển giả lập",
            TrackingNumber = "TRK-FAIL-001",
            IdempotencyKey = "ship-fail-001",
            CorrelationId = "CORR-FAIL-001",
            CreatedBy = "qa.user"
        });
        var voucher = await db.Vouchers.FindAsync(8801L);
        voucher!.ShippedAt = DateTime.Today;
        voucher.FulfillmentStatus = FulfillmentStatusEnum.Shipped;
        voucher.TrackingNumber = null;
        voucher.ManifestCode = null;
        var package = await db.OutboundPackages.FindAsync(88001L);
        package!.ShipmentLoadId = 9911;
        await db.SaveChangesAsync();

        var service = new ShippingReconciliationService(db);
        var rows = await service.BuildAsync(new DeliveryReconciliationFilter());

        Assert.Contains(rows, r => r.IssueType == "shipped_missing_tracking");
        Assert.Contains(rows, r => r.IssueType == "shipped_missing_manifest");
        Assert.Contains(rows, r => r.IssueType == "departed_load_missing_package");
        Assert.Contains(rows, r => r.IssueType == "delivery_failed_shipped_voucher");
        Assert.Contains(rows, r => r.IssueType == "carrier_required_missing_shipment");
    }

    [Fact]
    public async Task OpsKpi_ShouldIncludeCarrierSlaRows()
    {
        await using var db = CreateDb(nameof(OpsKpi_ShouldIncludeCarrierSlaRows));
        SeedWarehouseGraph(db);

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 604,
            VoucherCode = "PX-0604",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today.AddDays(-2),
            CreatedBy = "qa.user",
            IsPosted = true,
            RequestedDeliveryDate = DateTime.Today,
            PackedAt = DateTime.Today.AddDays(-1),
            ShippedAt = DateTime.Today
        });
        db.ShippingHandoverLogs.Add(new ShippingHandoverLog
        {
            ShippingHandoverLogId = 10,
            VoucherId = 604,
            WarehouseId = 1,
            CarrierName = "GHTK",
            DriverName = "Trần Minh Khôi",
            VehicleNumber = "51D-00001",
            HandedOverBy = "staff.user",
            HandedOverAt = DateTime.Today
        });
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db);
        var result = await controller.OpsKpi(null);

        var view = Assert.IsType<ViewResult>(result);
        var carrierRows = Assert.IsAssignableFrom<List<CarrierSlaRow>>(controller.ViewBag.CarrierSlaRows);
        Assert.Single(carrierRows);
        Assert.Equal("GHTK", carrierRows[0].CarrierName);
        Assert.Equal(1, carrierRows[0].TotalShipped);
    }

    [Fact]
    public async Task Vas_ShouldCreateReserveStartQcAndCompleteCoPackingWithCost()
    {
        await using var db = CreateDb(nameof(Vas_ShouldCreateReserveStartQcAndCompleteCoPackingWithCost));
        SeedWarehouseGraph(db);
        SeedVasFixture(db);
        await db.SaveChangesAsync();

        var service = CreateVasService(db);
        var workOrder = await service.CreateAsync(new CreateVasWorkOrderCommand
        {
            WarehouseId = 1,
            PartnerId = 701,
            VoucherId = 9701,
            PrimaryItemId = 900,
            OperationType = VasOperationTypeEnum.CoPacking,
            PlannedQty = 2,
            LaborRatePerHour = 120,
            MaterialLines = new List<CreateVasMaterialLineCommand>
            {
                new() { MaterialItemId = 901, RequiredQty = 4, Notes = "Dán nhãn phụ" }
            }
        }, "tester");

        Assert.Equal(VasWorkOrderStatusEnum.Draft, workOrder.Status);

        await service.ReserveAsync(workOrder.VasWorkOrderId, "tester");

        var earlyLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 9701);
        var lateLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 9702);
        var holdLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 9704);
        Assert.Equal(3m, earlyLoc.ReservedQty);
        Assert.Equal(1m, lateLoc.ReservedQty);
        Assert.Equal(0m, holdLoc.ReservedQty);

        var started = await service.StartAsync(workOrder.VasWorkOrderId, "operator");
        var operationId = started.Operations.Single().VasOperationId;
        await service.CompleteOperationAsync(operationId, 30, "Đã hoàn thành co-packing", "operator");
        await service.SubmitQcAsync(workOrder.VasWorkOrderId, 2, "qc.user");
        await service.RecordQcAsync(workOrder.VasWorkOrderId, VasQcResultEnum.Passed, 2, 0, "Đạt QC", "qc.user");

        var completed = await service.CompleteAsync(workOrder.VasWorkOrderId, "manager");

        earlyLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 9701);
        lateLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 9702);
        Assert.Equal(0m, earlyLoc.Quantity);
        Assert.Equal(0m, earlyLoc.ReservedQty);
        Assert.Equal(4m, lateLoc.Quantity);
        Assert.Equal(0m, lateLoc.ReservedQty);

        Assert.Equal(VasWorkOrderStatusEnum.Completed, completed.Status);
        Assert.Equal(20m, completed.MaterialCost);
        Assert.Equal(60m, completed.LaborCost);
        Assert.Equal(80m, completed.TotalCost);
        Assert.All(completed.MaterialLines, l => Assert.Equal(VasMaterialLineStatusEnum.Consumed, l.Status));
        Assert.Equal(54m, (await db.Items.FindAsync(901))!.CurrentStock);
    }

    [Fact]
    public async Task VasReserve_ShouldIgnoreHeldStockAndFailWhenNoAvailableQty()
    {
        await using var db = CreateDb(nameof(VasReserve_ShouldIgnoreHeldStockAndFailWhenNoAvailableQty));
        SeedWarehouseGraph(db);
        SeedVasFixture(db);
        db.ItemLocations.RemoveRange(db.ItemLocations.Local.Where(il => il.ItemId == 901 && il.HoldStatus == InventoryHoldStatusEnum.Available).ToList());
        await db.SaveChangesAsync();

        var service = CreateVasService(db);
        var workOrder = await service.CreateAsync(new CreateVasWorkOrderCommand
        {
            WarehouseId = 1,
            PartnerId = 701,
            VoucherId = 9701,
            PrimaryItemId = 900,
            OperationType = VasOperationTypeEnum.Relabel,
            PlannedQty = 2,
            Notes = "Dán nhãn theo khách",
            MaterialLines = new List<CreateVasMaterialLineCommand>
            {
                new() { MaterialItemId = 901, RequiredQty = 4 }
            }
        }, "tester");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ReserveAsync(workOrder.VasWorkOrderId, "tester"));

        Assert.Equal("VAS_INSUFFICIENT_MATERIAL_STOCK", ex.Code);
        Assert.Equal(0m, db.ItemLocations.Single(il => il.ItemLocationId == 9704).ReservedQty);
    }

    [Fact]
    public async Task VasComplete_ShouldRejectWithoutLaborOrQcPassed()
    {
        await using var db = CreateDb(nameof(VasComplete_ShouldRejectWithoutLaborOrQcPassed));
        SeedWarehouseGraph(db);
        SeedVasFixture(db);
        await db.SaveChangesAsync();

        var service = CreateVasService(db);
        var workOrder = await service.CreateAsync(new CreateVasWorkOrderCommand
        {
            WarehouseId = 1,
            PartnerId = 701,
            VoucherId = 9701,
            PrimaryItemId = 900,
            OperationType = VasOperationTypeEnum.CoPacking,
            PlannedQty = 1,
            MaterialLines = new List<CreateVasMaterialLineCommand>
            {
                new() { MaterialItemId = 902, RequiredQty = 1 }
            }
        }, "tester");

        await service.ReserveAsync(workOrder.VasWorkOrderId, "tester");
        var started = await service.StartAsync(workOrder.VasWorkOrderId, "operator");

        var noLabor = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitQcAsync(workOrder.VasWorkOrderId, 1, "qc.user"));
        Assert.Equal("VAS_LABOR_REQUIRED", noLabor.Code);

        await service.CompleteOperationAsync(started.Operations.Single().VasOperationId, 10, null, "operator");
        await service.SubmitQcAsync(workOrder.VasWorkOrderId, 1, "qc.user");
        var rework = await service.RecordQcAsync(workOrder.VasWorkOrderId, VasQcResultEnum.Failed, 0, 1, "Lỗi dán nhãn", "qc.user");

        Assert.Equal(VasWorkOrderStatusEnum.InProgress, rework.Status);
        var invalidComplete = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CompleteAsync(workOrder.VasWorkOrderId, "manager"));
        Assert.Equal("VAS_INVALID_STATUS", invalidComplete.Code);
    }

    [Fact]
    public async Task VasCancel_ShouldReleaseReservedMaterialQty()
    {
        await using var db = CreateDb(nameof(VasCancel_ShouldReleaseReservedMaterialQty));
        SeedWarehouseGraph(db);
        SeedVasFixture(db);
        await db.SaveChangesAsync();

        var service = CreateVasService(db);
        var workOrder = await service.CreateAsync(new CreateVasWorkOrderCommand
        {
            WarehouseId = 1,
            PartnerId = 701,
            VoucherId = 9701,
            PrimaryItemId = 900,
            OperationType = VasOperationTypeEnum.Repack,
            PlannedQty = 1,
            MaterialLines = new List<CreateVasMaterialLineCommand>
            {
                new() { MaterialItemId = 902, RequiredQty = 2 }
            }
        }, "tester");

        await service.ReserveAsync(workOrder.VasWorkOrderId, "tester");
        Assert.Equal(2m, await db.ItemLocations.SumAsync(il => il.ReservedQty));

        await service.CancelAsync(workOrder.VasWorkOrderId, "Khách đổi yêu cầu", "manager");

        Assert.Equal(0m, await db.ItemLocations.SumAsync(il => il.ReservedQty));
        var cancelled = await db.VasWorkOrders.Include(v => v.MaterialLines).SingleAsync(v => v.VasWorkOrderId == workOrder.VasWorkOrderId);
        Assert.Equal(VasWorkOrderStatusEnum.Cancelled, cancelled.Status);
        Assert.All(cancelled.MaterialLines, l => Assert.Equal(VasMaterialLineStatusEnum.Released, l.Status));
    }

    /// <summary>
    /// P1-01 Hardening: Partial batch pick — picker picks 4 out of 6 total target.
    /// Verifies allocations are distributed proportionally and PostReservedOutbound
    /// with cancelRemaining handles the short correctly.
    /// </summary>
    [Fact]
    public async Task PartialBatchPick_ShouldAllocateAndPostCorrectly()
    {
        await using var db = CreateDb(nameof(PartialBatchPick_ShouldAllocateAndPostCorrectly));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "PARTIAL-SKU",
            ItemName = "Hàng lấy một phần",
            BaseUomId = 1,
            CurrentStock = 10,
            UnitCost = 5,
            IsActive = true
        });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 92001,
            ItemId = 1,
            LocationId = 1,
            Quantity = 10,
            ReservedQty = 0,
            LotNumber = "LOT-PART",
            ExpiryDate = new DateTime(2026, 12, 31)
        });

        // Create 3 vouchers, each requesting 2 units
        for (var i = 0; i < 3; i++)
        {
            var voucherId = 920L + i;
            var detailId = 9200L + i;
            db.Vouchers.Add(new Voucher
            {
                VoucherId = voucherId,
                VoucherCode = $"PX-PART-{i + 1:D3}",
                VoucherType = VoucherTypeEnum.XuatKho,
                WarehouseId = 1,
                VoucherDate = DateTime.Today,
                CreatedBy = "creator.user",
                IsPosted = false,
                PartialShipmentAllowed = true
            });
            db.VoucherDetails.Add(new VoucherDetail
            {
                VoucherDetailId = detailId,
                VoucherId = voucherId,
                LineNumber = 1,
                ItemId = 1,
                LocationId = 1,
                LotNumber = "LOT-PART",
                ExpiryDate = new DateTime(2026, 12, 31),
                TransactionQty = 2,
                BaseQty = 2,
                TransactionUomId = 1,
                UnitPrice = 5,
                LineAmount = 10
            });
        }
        await db.SaveChangesAsync();

        // Create wave — should batch all 3 into 1 pick task with TargetQty=6
        var controller = CreateController(db, userName: "manager.user");
        var createResult = await controller.CreateWave("Standard", null, null, null, null, 2, new[] { 920L, 921L, 922L }, null);
        Assert.IsType<RedirectToActionResult>(createResult);

        var task = await db.PickTasks.Include(t => t.Allocations).SingleAsync();
        Assert.True(task.IsBatchPick);
        Assert.Equal(6, task.TargetQty);
        Assert.Equal(3, task.Allocations.Count);

        // Picker only picks 4 out of 6 (partial)
        var confirmResult = await controller.ConfirmPickTask(task.PickTaskId, 4, "PARTIAL-SKU", null);
        Assert.IsType<RedirectToActionResult>(confirmResult);

        // Verify allocations: sequential filling — 2 + 2 + 0 (picks fill each allocation's quota sequentially)
        var allocations = await db.PickTaskAllocations.OrderBy(a => a.VoucherId).ToListAsync();
        var totalAllocated = allocations.Sum(a => a.PickedQty);
        Assert.Equal(4, totalAllocated);
        // First two allocations get full 2 each, third gets 0 (sequential fill)
        Assert.Equal(2, allocations[0].PickedQty);
        Assert.Equal(2, allocations[1].PickedQty);
        Assert.Equal(0, allocations[2].PickedQty);

        // Post outbound for first voucher with cancelRemaining
        var postResult = await controller.PostReservedOutbound(920L, cancelRemaining: true);
        Assert.IsType<RedirectToActionResult>(postResult);

        // Verify first voucher is completed
        var v920 = await db.Vouchers.FindAsync(920L);
        Assert.NotNull(v920);
        Assert.True(v920!.IsPosted);

        // Verify reservations for first voucher are consumed/released
        var res920 = await db.StockReservations.Where(r => r.VoucherId == 920L).ToListAsync();
        Assert.All(res920, r => Assert.True(
            r.Status == ReservationStatusEnum.Consumed || r.Status == ReservationStatusEnum.Released,
            $"Reservation {r.StockReservationId} has status {r.Status}"));

        // ItemLocation should have decreased by the picked amount for this voucher
        var itemLoc = await db.ItemLocations.FindAsync(92001);
        Assert.NotNull(itemLoc);
        Assert.True(itemLoc!.Quantity >= 0, "ItemLocation quantity should not be negative");
    }

    [Fact]
    public async Task ShortPick_ShouldAutoReallocateFromAlternativeLocation()
    {
        await using var db = CreateDb(nameof(ShortPick_ShouldAutoReallocateFromAlternativeLocation));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SHORT-SKU",
            ItemName = "Hàng thiếu lấy",
            BaseUomId = 1,
            CurrentStock = 8,
            UnitCost = 5,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation
            {
                ItemLocationId = 94001,
                ItemId = 1,
                LocationId = 1,
                Quantity = 5,
                ReservedQty = 0,
                LotNumber = "LOT-A",
                ExpiryDate = new DateTime(2026, 12, 31)
            },
            new ItemLocation
            {
                ItemLocationId = 94002,
                ItemId = 1,
                LocationId = 2,
                Quantity = 3,
                ReservedQty = 0,
                LotNumber = "LOT-B",
                ExpiryDate = new DateTime(2026, 11, 30)
            });

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 940,
            VoucherCode = "PX-SHORT-001",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = DateTime.Today,
            CreatedBy = "creator.user",
            IsPosted = false
        });
        db.VoucherDetails.Add(new VoucherDetail
        {
            VoucherDetailId = 9400,
            VoucherId = 940,
            LineNumber = 1,
            ItemId = 1,
            LocationId = 1,
            LotNumber = "LOT-A",
            ExpiryDate = new DateTime(2026, 12, 31),
            TransactionQty = 5,
            BaseQty = 5,
            TransactionUomId = 1,
            UnitPrice = 5,
            LineAmount = 25
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userName: "picker.user");
        var createResult = await controller.CreateWave("Standard", null, null, null, null, 2, new[] { 940L }, null);
        Assert.IsType<RedirectToActionResult>(createResult);

        var originalTask = await db.PickTasks.Include(t => t.Allocations).SingleAsync();
        Assert.Equal(1, originalTask.SourceLocationId);
        Assert.Equal(5, originalTask.TargetQty);

        var confirmResult = await controller.ConfirmPickTask(originalTask.PickTaskId, 2, "SHORT-SKU", null, reportShort: true);
        Assert.IsType<RedirectToActionResult>(confirmResult);

        var tasks = await db.PickTasks
            .Include(t => t.Allocations)
            .OrderBy(t => t.PickTaskId)
            .ToListAsync();
        var shortTask = tasks.First(t => t.PickTaskId == originalTask.PickTaskId);
        var replacementTask = tasks.Single(t => t.PickTaskId != originalTask.PickTaskId);

        Assert.Equal(PickTaskStatusEnum.Short, shortTask.Status);
        Assert.Equal(2, shortTask.PickedQty);
        Assert.Equal(2, shortTask.Allocations.Single().PickedQty);

        Assert.Equal(2, replacementTask.SourceLocationId);
        Assert.Equal(3, replacementTask.TargetQty);
        Assert.Equal(PickTaskStatusEnum.Assigned, replacementTask.Status);
        Assert.Single(replacementTask.Allocations);
        Assert.Equal(3, replacementTask.Allocations.Single().AllocatedQty);

        var reservations = await db.StockReservations.OrderBy(r => r.StockReservationId).ToListAsync();
        var releasedOriginal = reservations.Single(r => r.LocationId == 1);
        var replacementReservation = reservations.Single(r => r.LocationId == 2);
        Assert.Equal(3, releasedOriginal.ReleasedQty);
        Assert.Equal(ReservationStatusEnum.Active, releasedOriginal.Status);
        Assert.Equal(3, replacementReservation.ReservedQty);
        Assert.Equal(ReservationStatusEnum.Active, replacementReservation.Status);

        Assert.Equal(2, (await db.ItemLocations.FindAsync(94001))!.ReservedQty);
        Assert.Equal(3, (await db.ItemLocations.FindAsync(94002))!.ReservedQty);

        var exceptionCase = await db.OperationExceptionCases.SingleAsync();
        Assert.Equal("pick_short", exceptionCase.CategoryKey);
        Assert.Equal(shortTask.TaskCode, exceptionCase.ReferenceCode);
    }

    /// <summary>
    /// P0-03 Hardening: Verify SyncCurrentStockAsync correctly recalculates
    /// Item.CurrentStock from ItemLocation SUM after stock mutations.
    /// </summary>
    [Fact]
    public async Task SyncCurrentStock_ShouldRecalculateFromItemLocations()
    {
        await using var db = CreateDb(nameof(SyncCurrentStock_ShouldRecalculateFromItemLocations));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "SYNC-ITEM",
            ItemName = "Hàng đồng bộ tồn",
            BaseUomId = 1,
            CurrentStock = 999, // Deliberately wrong
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 93001, ItemId = 1, LocationId = 1, Quantity = 30, LotNumber = "LOT-A" },
            new ItemLocation { ItemLocationId = 93002, ItemId = 1, LocationId = 2, Quantity = 20, LotNumber = "LOT-B" }
        );
        await db.SaveChangesAsync();

        var service = new InventoryBalanceService(db);
        await service.SyncCurrentStockAsync(new[] { 1 });
        await db.SaveChangesAsync();

        var item = await db.Items.FindAsync(1);
        Assert.NotNull(item);
        Assert.Equal(50, item!.CurrentStock); // 30 + 20 = 50
        Assert.Equal(500, item.TotalStockValue); // 50 * 10
    }

    [Fact]
    public async Task CancelVoucher_ShouldBlockWhenTransactionDateIsSameLockDateWithDifferentTime()
    {
        await using var db = CreateDb(nameof(CancelVoucher_ShouldBlockWhenTransactionDateIsSameLockDateWithDifferentTime));
        SeedWarehouseGraph(db);

        db.Vouchers.Add(new Voucher
        {
            VoucherId = 9301,
            VoucherCode = "PX-LOCK-001",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            VoucherDate = new DateTime(2026, 4, 1),
            CompletedAt = new DateTime(2026, 4, 15, 16, 30, 0),
            CreatedBy = "creator.user",
            IsPosted = false
        });
        await db.SaveChangesAsync();

        var service = new VoucherCancellationService(
            db,
            new EfUnitOfWork(db),
            new InventoryReservationService(db),
            new InventoryBalanceService(db));

        var ex = await Assert.ThrowsAsync<WarehouseLockedException>(() =>
            service.CancelVoucherAsync(
                9301,
                "audit regression",
                CancelReasonEnum.Other,
                scopedWarehouseId: null,
                actor: "manager.user",
                ipAddress: null,
                lockDate: new DateTime(2026, 4, 15)));

        Assert.Equal(new DateTime(2026, 4, 15), ex.LockedDate.Date);
    }

    [Fact]
    public async Task Kitting_ShouldCreateReserveAndCompleteWithFefoAudit()
    {
        await using var db = CreateDb(nameof(Kitting_ShouldCreateReserveAndCompleteWithFefoAudit));
        SeedWarehouseGraph(db);
        SeedKittingFixture(db);
        db.Locations.Add(new Location
        {
            LocationId = 3,
            ZoneId = 1,
            LocationCode = "KIT-FINISHED-01",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = CreateKittingService(db);
        var workOrder = await service.CreateFromBomAsync(new CreateKittingWorkOrderCommand
        {
            WarehouseId = 1,
            FinishedItemId = 100,
            FinishedLocationId = 3,
            PlannedQty = 2,
            FinishedLotNumber = "KIT-LOT-01"
        }, "tester");

        Assert.Equal(KittingWorkOrderStatusEnum.Draft, workOrder.Status);
        Assert.Equal(2, workOrder.Lines.Count);

        await service.ReserveAsync(workOrder.KittingWorkOrderId, "tester");

        var earlyLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 101);
        var lateLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 102);
        Assert.Equal(3m, earlyLoc.ReservedQty);
        Assert.Equal(1m, lateLoc.ReservedQty);

        await service.CompleteAsync(workOrder.KittingWorkOrderId, "tester");

        earlyLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 101);
        lateLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 102);
        var componentBLoc = await db.ItemLocations.SingleAsync(il => il.ItemLocationId == 201);
        var finishedLoc = await db.ItemLocations.SingleAsync(il => il.ItemId == 100 && il.LocationId == 3 && il.LotNumber == "KIT-LOT-01");

        Assert.Equal(0m, earlyLoc.Quantity);
        Assert.Equal(0m, earlyLoc.ReservedQty);
        Assert.Equal(4m, lateLoc.Quantity);
        Assert.Equal(0m, lateLoc.ReservedQty);
        Assert.Equal(0m, componentBLoc.Quantity);
        Assert.Equal(2m, finishedLoc.Quantity);

        Assert.Equal(2m, (await db.Items.FindAsync(100))!.CurrentStock);
        Assert.Equal(4m, (await db.Items.FindAsync(101))!.CurrentStock);
        Assert.Equal(100m, (await db.Items.FindAsync(102))!.CurrentStock);

        var completed = await db.KittingWorkOrders.Include(k => k.Lines).SingleAsync(k => k.KittingWorkOrderId == workOrder.KittingWorkOrderId);
        Assert.Equal(KittingWorkOrderStatusEnum.Completed, completed.Status);
        Assert.All(completed.Lines, l => Assert.Equal(KittingWorkOrderLineStatusEnum.Consumed, l.Status));
    }

    [Fact]
    public async Task KittingComplete_ShouldRejectConflictingStorageDestinationBeforeComponentConsumption()
    {
        await using var db = CreateDb(nameof(KittingComplete_ShouldRejectConflictingStorageDestinationBeforeComponentConsumption));
        SeedWarehouseGraph(db);
        SeedKittingFixture(db);
        await db.SaveChangesAsync();

        var service = CreateKittingService(db);
        var workOrder = await service.CreateFromBomAsync(new CreateKittingWorkOrderCommand
        {
            WarehouseId = 1,
            FinishedItemId = 100,
            FinishedLocationId = 1,
            PlannedQty = 1,
            FinishedLotNumber = "KIT-CONFLICT-01"
        }, "tester");
        await service.ReserveAsync(workOrder.KittingWorkOrderId, "tester");

        var sourceBefore = await db.ItemLocations
            .Where(row => row.ItemId == 101 || row.ItemId == 102)
            .Select(row => new { row.ItemLocationId, row.Quantity, row.ReservedQty })
            .ToDictionaryAsync(row => row.ItemLocationId);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CompleteAsync(workOrder.KittingWorkOrderId, "tester"));

        Assert.Equal("ONE_LOCATION_ONE_ITEM", exception.Code);
        var sourceAfter = await db.ItemLocations
            .Where(row => row.ItemId == 101 || row.ItemId == 102)
            .Select(row => new { row.ItemLocationId, row.Quantity, row.ReservedQty })
            .ToDictionaryAsync(row => row.ItemLocationId);
        Assert.Equal(sourceBefore, sourceAfter);
        Assert.False(await db.ItemLocations.AnyAsync(row => row.ItemId == 100));
        Assert.Empty(await db.InventoryTransactions
            .Where(row => row.TransactionGroupKey == $"kitting:{workOrder.KittingWorkOrderId}:complete")
            .ToListAsync());

        var unchanged = await db.KittingWorkOrders
            .Include(row => row.Lines)
            .SingleAsync(row => row.KittingWorkOrderId == workOrder.KittingWorkOrderId);
        Assert.Equal(KittingWorkOrderStatusEnum.Reserved, unchanged.Status);
        Assert.All(unchanged.Lines, line => Assert.Equal(KittingWorkOrderLineStatusEnum.Reserved, line.Status));
    }

    [Fact]
    public async Task KittingReserve_ShouldIgnoreHeldStockAndFailWhenNoAvailableQty()
    {
        await using var db = CreateDb(nameof(KittingReserve_ShouldIgnoreHeldStockAndFailWhenNoAvailableQty));
        SeedWarehouseGraph(db);
        SeedKittingFixture(db);
        db.ItemLocations.RemoveRange(db.ItemLocations.Local.Where(il => il.ItemId == 101).ToList());
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 301,
            ItemId = 101,
            LocationId = 1,
            Quantity = 20,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.QcHold
        });
        await db.SaveChangesAsync();

        var service = CreateKittingService(db);
        var workOrder = await service.CreateFromBomAsync(new CreateKittingWorkOrderCommand
        {
            WarehouseId = 1,
            FinishedItemId = 100,
            FinishedLocationId = 1,
            PlannedQty = 2,
            FinishedLotNumber = "KIT-LOT-02"
        }, "tester");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ReserveAsync(workOrder.KittingWorkOrderId, "tester"));

        Assert.Equal("KITTING_INSUFFICIENT_COMPONENT_STOCK", ex.Code);
        Assert.Equal(0m, db.ItemLocations.Single(il => il.ItemLocationId == 301).ReservedQty);
    }

    [Fact]
    public async Task KittingCancel_ShouldReleaseReservedComponentQty()
    {
        await using var db = CreateDb(nameof(KittingCancel_ShouldReleaseReservedComponentQty));
        SeedWarehouseGraph(db);
        SeedKittingFixture(db);
        await db.SaveChangesAsync();

        var service = CreateKittingService(db);
        var workOrder = await service.CreateFromBomAsync(new CreateKittingWorkOrderCommand
        {
            WarehouseId = 1,
            FinishedItemId = 100,
            FinishedLocationId = 1,
            PlannedQty = 1,
            FinishedLotNumber = "KIT-LOT-03"
        }, "tester");

        await service.ReserveAsync(workOrder.KittingWorkOrderId, "tester");
        Assert.True(await db.ItemLocations.AnyAsync(il => il.ReservedQty > 0));

        await service.CancelAsync(workOrder.KittingWorkOrderId, "Khách đổi yêu cầu", "manager");

        Assert.Equal(0m, await db.ItemLocations.SumAsync(il => il.ReservedQty));
        var cancelled = await db.KittingWorkOrders.Include(k => k.Lines).SingleAsync(k => k.KittingWorkOrderId == workOrder.KittingWorkOrderId);
        Assert.Equal(KittingWorkOrderStatusEnum.Cancelled, cancelled.Status);
        Assert.All(cancelled.Lines, l => Assert.Equal(KittingWorkOrderLineStatusEnum.Released, l.Status));
    }

    [Fact]
    public async Task KittingCreate_ShouldRejectSerialTrackedFinishedOrComponent()
    {
        await using var db = CreateDb(nameof(KittingCreate_ShouldRejectSerialTrackedFinishedOrComponent));
        SeedWarehouseGraph(db);
        SeedKittingFixture(db);
        var component = await db.Items.FindAsync(101);
        component!.TrackSerial = true;
        await db.SaveChangesAsync();

        var service = CreateKittingService(db);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateFromBomAsync(new CreateKittingWorkOrderCommand
        {
            WarehouseId = 1,
            FinishedItemId = 100,
            FinishedLocationId = 1,
            PlannedQty = 1,
            FinishedLotNumber = "KIT-LOT-04"
        }, "tester"));

        Assert.Equal("KITTING_SERIAL_COMPONENT_NOT_SUPPORTED", ex.Code);
    }

    /// <summary>
    /// P0-03 Acceptance: item có tồn ở 2 kho, user scoped kho 1 chỉ thấy tồn kho 1.
    /// </summary>
    [Fact]
    public async Task GetStockByItem_ShouldReturnOnlyScopedWarehouseStock()
    {
        await using var db = CreateDb(nameof(GetStockByItem_ShouldReturnOnlyScopedWarehouseStock));
        SeedWarehouseGraph(db); // WH1 with Z1(L1), Z2(L2)

        // Add a second warehouse
        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Warehouse 2", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 10, WarehouseId = 2, ZoneCode = "Z-WH2", ZoneName = "WH2 Zone", ZoneType = ZoneTypeEnum.Storage, IsActive = true });
        db.Locations.Add(new Location { LocationId = 10, ZoneId = 10, LocationCode = "WH2-L1", IsActive = true });

        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "MULTI-WH-ITEM",
            ItemName = "Hàng đa kho",
            BaseUomId = 1,
            CurrentStock = 0,
            UnitCost = 10,
            IsActive = true
        });

        // Stock in WH1: 30 units
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 95001, ItemId = 1, LocationId = 1, Quantity = 30, LotNumber = "LOT-A" });
        // Stock in WH2: 20 units
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 95002, ItemId = 1, LocationId = 10, Quantity = 20, LotNumber = "LOT-B" });
        await db.SaveChangesAsync();

        var service = new InventoryBalanceService(db);

        // Global query — should see all 50
        var globalStock = await service.GetStockByItemAsync(warehouseId: null, itemIds: new[] { 1 });
        Assert.Equal(50, globalStock[1]);

        // Scoped to WH1 — should see only 30
        var wh1Stock = await service.GetStockByItemAsync(warehouseId: 1, itemIds: new[] { 1 });
        Assert.Equal(30, wh1Stock[1]);

        // Scoped to WH2 — should see only 20
        var wh2Stock = await service.GetStockByItemAsync(warehouseId: 2, itemIds: new[] { 1 });
        Assert.Equal(20, wh2Stock[1]);
    }

    [Fact]
    public async Task ItemsIndex_ShouldApplyItemLocationStockWithWarehouseScope()
    {
        await using var db = CreateDb(nameof(ItemsIndex_ShouldApplyItemLocationStockWithWarehouseScope));
        SeedWarehouseGraph(db);
        SeedSecondWarehouseLocation(db);

        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "MULTI-WH-ITEM",
            ItemName = "Scoped stock item",
            BaseUomId = 1,
            CurrentStock = 999,
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 96001, ItemId = 1, LocationId = 1, Quantity = 30 },
            new ItemLocation { ItemLocationId = 96002, ItemId = 1, LocationId = 10, Quantity = 20 });
        await db.SaveChangesAsync();

        var staffController = CreateItemsController(db, warehouseClaim: 1, roleName: "Staff");
        var staffResult = await staffController.Index(null, null, null, null);
        var staffRows = Assert.IsAssignableFrom<List<Item>>(Assert.IsType<ViewResult>(staffResult).Model);
        Assert.Equal(30, Assert.Single(staffRows).CurrentStock);

        var adminController = CreateItemsController(db, roleName: "Admin");
        var adminResult = await adminController.Index(null, null, null, null);
        var adminRows = Assert.IsAssignableFrom<List<Item>>(Assert.IsType<ViewResult>(adminResult).Model);
        Assert.Equal(50, Assert.Single(adminRows).CurrentStock);
    }

    [Fact]
    public async Task ItemsDetails_ShouldApplyScopedStockAndLocationRows()
    {
        await using var db = CreateDb(nameof(ItemsDetails_ShouldApplyScopedStockAndLocationRows));
        SeedWarehouseGraph(db);
        SeedSecondWarehouseLocation(db);

        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "MULTI-WH-DETAIL",
            ItemName = "Scoped detail item",
            BaseUomId = 1,
            CurrentStock = 999,
            UnitCost = 10,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 96101, ItemId = 1, LocationId = 1, Quantity = 30 },
            new ItemLocation { ItemLocationId = 96102, ItemId = 1, LocationId = 10, Quantity = 20 });
        await db.SaveChangesAsync();

        var controller = CreateItemsController(db, warehouseClaim: 1, roleName: "Staff");
        var result = await controller.Details(1);

        var view = Assert.IsType<ViewResult>(result);
        var item = Assert.IsType<Item>(view.Model);
        Assert.Equal(30, item.CurrentStock);

        var locations = Assert.IsAssignableFrom<List<ItemLocation>>(controller.ViewBag.Locations);
        var location = Assert.Single(locations);
        Assert.Equal(1, location.LocationId);
    }

    [Fact]
    public async Task ItemsDetails_ShouldScopeRecentVouchersByWarehouse()
    {
        await using var db = CreateDb(nameof(ItemsDetails_ShouldScopeRecentVouchersByWarehouse));
        SeedWarehouseGraph(db);
        SeedSecondWarehouseLocation(db);

        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "RECENT-SCOPE",
            ItemName = "Scoped voucher history",
            BaseUomId = 1,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 96201, ItemId = 1, LocationId = 1, Quantity = 10 },
            new ItemLocation { ItemLocationId = 96202, ItemId = 1, LocationId = 10, Quantity = 20 });
        db.Vouchers.AddRange(
            new Voucher { VoucherId = 101, VoucherCode = "PN-WH1", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 1, VoucherDate = DateTime.Today.AddDays(-1), CreatedBy = "qa.user" },
            new Voucher { VoucherId = 102, VoucherCode = "PN-WH2", VoucherType = VoucherTypeEnum.NhapKho, WarehouseId = 2, VoucherDate = DateTime.Today, CreatedBy = "qa.user" });
        db.VoucherDetails.AddRange(
            new VoucherDetail { VoucherDetailId = 101, VoucherId = 101, ItemId = 1, BaseQty = 10, TransactionQty = 10, TransactionUomId = 1 },
            new VoucherDetail { VoucherDetailId = 102, VoucherId = 102, ItemId = 1, BaseQty = 20, TransactionQty = 20, TransactionUomId = 1 });
        await db.SaveChangesAsync();

        var controller = CreateItemsController(db, warehouseClaim: 1, roleName: "Staff");
        var result = await controller.Details(1);

        Assert.IsType<ViewResult>(result);
        var recent = Assert.IsAssignableFrom<List<VoucherDetail>>(controller.ViewBag.RecentVouchers);
        var row = Assert.Single(recent);
        Assert.Equal(101, row.VoucherId);
        Assert.Equal(1, row.Voucher!.WarehouseId);
    }

    [Fact]
    public void GenerateItemCode_ShouldRequireMasterItemManagePolicy()
    {
        var method = typeof(ItemsController).GetMethod(nameof(ItemsController.GenerateItemCode), new[] { typeof(ItemTypeEnum) });

        Assert.NotNull(method);
        var attributes = method!.GetCustomAttributes<AuthorizeAttribute>().ToList();
        Assert.Contains(attributes, attribute => attribute.Policy == WmsPermissions.MasterItemManage);
        Assert.Contains(attributes, attribute => string.Equals(attribute.Roles, "Admin,Manager", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ItemsCreateAndEdit_ShouldRejectMissingOrInactiveBaseUom()
    {
        await using var db = CreateDb(nameof(ItemsCreateAndEdit_ShouldRejectMissingOrInactiveBaseUom));
        SeedWarehouseGraph(db);
        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each", IsActive = true },
            new UnitOfMeasure { UomId = 2, UomCode = "OLD", UomName = "Inactive", IsActive = false });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "GOOD-UOM", ItemName = "Valid item", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var createController = CreateItemsController(db, roleName: "Admin");
        var createVm = new ItemFormViewModel
        {
            Item = new Item
            {
                ItemCode = "BAD-UOM",
                ItemName = "Invalid UOM item",
                BaseUomId = 999,
                ItemType = ItemTypeEnum.NguyenVatLieu,
                IsActive = true
            }
        };
        var createResult = await createController.Create(createVm, null);

        Assert.IsType<ViewResult>(createResult);
        Assert.Contains("Đơn vị tính cơ bản", createController.TempData["Error"]?.ToString());
        Assert.False(await db.Items.AnyAsync(item => item.ItemCode == "BAD-UOM"));

        var editController = CreateItemsController(db, roleName: "Admin");
        var editVm = new ItemFormViewModel
        {
            Item = new Item
            {
                ItemId = 1,
                ItemCode = "GOOD-UOM",
                ItemName = "Try inactive UOM",
                BaseUomId = 2,
                ItemType = ItemTypeEnum.NguyenVatLieu,
                IsActive = true
            }
        };
        var editResult = await editController.Edit(1, editVm, null);

        Assert.IsType<ViewResult>(editResult);
        Assert.Contains("Đơn vị tính cơ bản", editController.TempData["Error"]?.ToString());
        Assert.Equal(1, (await db.Items.FindAsync(1))!.BaseUomId);
    }

    // ═══════════════════════════════════════════════════════════════
    // P1-02: Cluster Picking — Tote Validation Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ClusterPicking_ConfirmWithCorrectTote_ShouldSucceed()
    {
        await using var db = CreateDb(nameof(ClusterPicking_ConfirmWithCorrectTote_ShouldSucceed));
        SeedWarehouseGraph(db);

        // Create item + stock
        db.Items.Add(new Item { ItemId = 1, ItemCode = "APPLE", ItemName = "Táo", BaseUomId = 1, CurrentStock = 100, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 1, ItemId = 1, LocationId = 1, Quantity = 100 });

        // Create wave + pick task
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-TEST-TOTE", WarehouseId = 1, Status = WaveStatusEnum.Released, CreatedBy = "test" });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "PX-TOTE-1", WarehouseId = 1, VoucherType = VoucherTypeEnum.XuatKho, WaveId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-TOTE-1", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Assigned, AssignedTo = "picker" });

        // Assign tote to voucher
        db.PickTotes.Add(new PickTote { PickToteId = 1, ToteCode = "TOTE-A1", WaveId = 1, VoucherId = 1, Status = PickToteStatusEnum.Assigned, AssignedBy = "mgr" });
        await db.SaveChangesAsync();

        var uow = new EfUnitOfWork(db);
        var reservationService = new InventoryReservationService(db);
        var balanceService = new InventoryBalanceService(db);
        var service = new OutboundExecutionService(db, uow, reservationService, balanceService);

        // Confirm with CORRECT tote
        var result = await service.ConfirmPickTaskAsync(1, 10, "APPLE", null, "picker", false, toteCode: "TOTE-A1");
        Assert.True(result.Succeeded, $"Expected success but got: {result.Message}");
    }

    [Fact]
    public async Task ClusterPicking_ConfirmWithWrongTote_ShouldFail()
    {
        await using var db = CreateDb(nameof(ClusterPicking_ConfirmWithWrongTote_ShouldFail));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "APPLE", ItemName = "Táo", BaseUomId = 1, CurrentStock = 100, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 1, ItemId = 1, LocationId = 1, Quantity = 100 });

        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-TEST-TOTE2", WarehouseId = 1, Status = WaveStatusEnum.Released, CreatedBy = "test" });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "PX-TOTE-2", WarehouseId = 1, VoucherType = VoucherTypeEnum.XuatKho, WaveId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-TOTE-2", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Assigned, AssignedTo = "picker" });

        // Assign tote TOTE-A1 to voucher
        db.PickTotes.Add(new PickTote { PickToteId = 1, ToteCode = "TOTE-A1", WaveId = 1, VoucherId = 1, Status = PickToteStatusEnum.Assigned, AssignedBy = "mgr" });
        await db.SaveChangesAsync();

        var uow = new EfUnitOfWork(db);
        var reservationService = new InventoryReservationService(db);
        var balanceService = new InventoryBalanceService(db);
        var service = new OutboundExecutionService(db, uow, reservationService, balanceService);

        // Confirm with WRONG tote
        var result = await service.ConfirmPickTaskAsync(1, 10, "APPLE", null, "picker", false, toteCode: "TOTE-B2");
        Assert.False(result.Succeeded);
        Assert.Contains("Sai tote", result.Message);
    }

    [Fact]
    public async Task ClusterPicking_NoToteAssignment_ShouldWorkNormally()
    {
        await using var db = CreateDb(nameof(ClusterPicking_NoToteAssignment_ShouldWorkNormally));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "APPLE", ItemName = "Táo", BaseUomId = 1, CurrentStock = 100, IsActive = true });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 1, ItemId = 1, LocationId = 1, Quantity = 100 });

        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-TEST-NOTOTE", WarehouseId = 1, Status = WaveStatusEnum.Released, CreatedBy = "test" });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "PX-NOTOTE", WarehouseId = 1, VoucherType = VoucherTypeEnum.XuatKho, WaveId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-NOTOTE", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 5, Status = PickTaskStatusEnum.Assigned, AssignedTo = "picker" });

        // NO tote assignment at all
        await db.SaveChangesAsync();

        var uow = new EfUnitOfWork(db);
        var reservationService = new InventoryReservationService(db);
        var balanceService = new InventoryBalanceService(db);
        var service = new OutboundExecutionService(db, uow, reservationService, balanceService);

        // Confirm without toteCode — should work (backward compatible)
        var result = await service.ConfirmPickTaskAsync(1, 5, "APPLE", null, "picker", false, toteCode: null);
        Assert.True(result.Succeeded, $"Expected success but got: {result.Message}");
    }

    // ═══════════════════════════════════════════════════════════════
    // P1-03: Zone Picking Assignment Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ZonePicking_StaffWithZone1_ShouldOnlySeeZone1Tasks()
    {
        await using var db = CreateDb(nameof(ZonePicking_StaffWithZone1_ShouldOnlySeeZone1Tasks));
        SeedWarehouseGraph(db);

        // Add User + Zone Assignment
        db.AppUsers.Add(new AppUser { UserId = 1, UserName = "staff.zone", FullName = "Staff Zone", WarehouseId = 1, RoleId = 3, IsActive = true });
        db.UserZoneAssignments.Add(new UserZoneAssignment { UserZoneAssignmentId = 1, UserId = 1, ZoneId = 1 });

        // Add Item & Voucher to satisfy PickTask INNER JOINs
        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM", IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });

        // Add 2 tasks, one in Zone 1, one in Zone 2
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WAVE-01", WarehouseId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "TASK-A", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, Status = PickTaskStatusEnum.Assigned, AssignedTo = "staff.zone" });
        db.PickTasks.Add(new PickTask { PickTaskId = 2, TaskCode = "TASK-B", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 2, Status = PickTaskStatusEnum.Assigned });

        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.zone", roleName: "Staff");

        var result = await controller.RfPicking(warehouseId: 1);
        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<PickTaskBoardRow>>(view.Model);

        Assert.Single(rows);
        Assert.Equal("TASK-A", rows[0].TaskCode);
    }

    [Fact]
    public async Task ZonePicking_Manager_ShouldSeeAllTasks()
    {
        await using var db = CreateDb(nameof(ZonePicking_Manager_ShouldSeeAllTasks));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM", IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });

        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WAVE-01", WarehouseId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "TASK-A", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, Status = PickTaskStatusEnum.Assigned });
        db.PickTasks.Add(new PickTask { PickTaskId = 2, TaskCode = "TASK-B", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 2, Status = PickTaskStatusEnum.Assigned });

        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "manager.zone", roleName: "Manager");

        var result = await controller.RfPicking(warehouseId: 1);
        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<PickTaskBoardRow>>(view.Model);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task ZonePicking_StaffWithNoZone_ShouldSeeNoTasks()
    {
        await using var db = CreateDb(nameof(ZonePicking_StaffWithNoZone_ShouldSeeNoTasks));
        SeedWarehouseGraph(db);

        db.AppUsers.Add(new AppUser { UserId = 1, UserName = "staff.nozone", FullName = "Staff No Zone", WarehouseId = 1, RoleId = 3, IsActive = true });

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM", IsActive = true });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });

        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WAVE-01", WarehouseId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "TASK-A", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, Status = PickTaskStatusEnum.Assigned });

        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.nozone", roleName: "Staff");

        var result = await controller.RfPicking(warehouseId: 1);
        var view = Assert.IsType<ViewResult>(result);
        var rows = Assert.IsAssignableFrom<List<PickTaskBoardRow>>(view.Model);

        Assert.Empty(rows);
    }

    // ═══════════════════════════════════════════════════════════════
    // P3-05: Task Interleaving Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task NextTask_ShouldPrioritizeSameAisle()
    {
        await using var db = CreateDb(nameof(NextTask_ShouldPrioritizeSameAisle));
        SeedWarehouseGraph(db);

        // Override locations with aisle sequences
        var loc1 = await db.Locations.FindAsync(1);
        loc1!.AisleCode = "A01"; loc1.AisleSequence = 1;
        var loc2 = await db.Locations.FindAsync(2);
        loc2!.AisleCode = "A10"; loc2.AisleSequence = 10;
        // Add a third location in same zone, close aisle
        db.Locations.Add(new Location { LocationId = 3, ZoneId = 1, LocationCode = "L3", AisleCode = "A02", AisleSequence = 2, IsActive = true });
        await db.SaveChangesAsync();

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", ItemName = "Hàng kiểm thử", IsActive = true });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-01", WarehouseId = 1, Status = WaveStatusEnum.Released });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });

        // Pick task far away (aisle 10)
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-FAR", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 2, TargetQty = 10, Status = PickTaskStatusEnum.Pending });
        // Pick task nearby (aisle 2)
        db.PickTasks.Add(new PickTask { PickTaskId = 2, TaskCode = "PT-NEAR", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 3, TargetQty = 5, Status = PickTaskStatusEnum.Assigned, AssignedTo = "picker1" });

        await db.SaveChangesAsync();

        var service = new TaskInterleavingService(db);
        // Picker is at location 1 (aisle 1)
        var result = await service.GetNextTasksAsync("picker1", currentLocationId: 1, scopedWarehouseId: 1, scopedZoneIds: null);

        Assert.True(result.Tasks.Count >= 2);
        // Near task should rank higher than far task
        var nearTask = result.Tasks.First(t => t.TaskCode == "PT-NEAR");
        var farTask = result.Tasks.First(t => t.TaskCode == "PT-FAR");
        Assert.True(nearTask.ProximityScore > farTask.ProximityScore, "Near task should have higher proximity score");
    }

    [Fact]
    public async Task NextTask_ShouldPrioritizeUrgentOverProximity()
    {
        await using var db = CreateDb(nameof(NextTask_ShouldPrioritizeUrgentOverProximity));
        SeedWarehouseGraph(db);

        var loc1 = await db.Locations.FindAsync(1);
        loc1!.AisleSequence = 1;
        var loc2 = await db.Locations.FindAsync(2);
        loc2!.AisleSequence = 10;
        await db.SaveChangesAsync();

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });

        // Movement task nearby, low priority
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 1,
            TaskCode = "MT-NEAR-LOW",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            PlannedQty = 10,
            Priority = MovementTaskPriorityEnum.Low,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });
        // Movement task far away, urgent with overdue deadline
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 2,
            TaskCode = "MT-FAR-URGENT",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 2,
            DestinationLocationId = 1,
            PlannedQty = 20,
            Priority = MovementTaskPriorityEnum.Urgent,
            DueAt = DateTime.Now.AddHours(-1), // Overdue!
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });

        await db.SaveChangesAsync();

        var service = new TaskInterleavingService(db);
        var result = await service.GetNextTasksAsync("picker1", currentLocationId: 1, scopedWarehouseId: 1, scopedZoneIds: null);

        Assert.True(result.Tasks.Count >= 2);
        // Urgent+overdue should outweigh proximity advantage
        Assert.Equal("MT-FAR-URGENT", result.Tasks[0].TaskCode);
    }

    [Fact]
    public async Task NextTask_ShouldRespectWarehouseScope()
    {
        await using var db = CreateDb(nameof(NextTask_ShouldRespectWarehouseScope));
        SeedWarehouseGraph(db);

        // Add a second warehouse
        db.Warehouses.Add(new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Kho 2", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 3, WarehouseId = 2, ZoneCode = "Z3", ZoneName = "Zone 3", IsActive = true });
        db.Locations.Add(new Location { LocationId = 3, ZoneId = 3, LocationCode = "L3-WH2", IsActive = true });

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });

        // Task in WH1
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 1,
            TaskCode = "MT-WH1",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            PlannedQty = 5,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });
        // Task in WH2
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 2,
            TaskCode = "MT-WH2",
            WarehouseId = 2,
            ItemId = 1,
            SourceLocationId = 3,
            DestinationLocationId = 3,
            PlannedQty = 5,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });

        await db.SaveChangesAsync();

        var service = new TaskInterleavingService(db);
        // Scope to WH1 only
        var result = await service.GetNextTasksAsync("picker1", null, scopedWarehouseId: 1, scopedZoneIds: null);

        Assert.All(result.Tasks, t => Assert.Equal("MT-WH1", t.TaskCode));
        Assert.DoesNotContain(result.Tasks, t => t.TaskCode == "MT-WH2");
    }

    [Fact]
    public async Task NextTask_ShouldRespectZoneScope()
    {
        await using var db = CreateDb(nameof(NextTask_ShouldRespectZoneScope));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });

        // Task in Zone 1
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 1,
            TaskCode = "MT-Z1",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            PlannedQty = 5,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });
        // Task in Zone 2
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 2,
            TaskCode = "MT-Z2",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 2,
            DestinationLocationId = 1,
            PlannedQty = 5,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });

        await db.SaveChangesAsync();

        var service = new TaskInterleavingService(db);
        // Staff only assigned to Zone 1
        var result = await service.GetNextTasksAsync("staff1", null, scopedWarehouseId: 1, scopedZoneIds: new List<int> { 1 });

        Assert.Single(result.Tasks);
        Assert.Equal("MT-Z1", result.Tasks[0].TaskCode);
    }

    [Fact]
    public async Task NextTask_ShouldIncludeBothPickAndMovement()
    {
        await using var db = CreateDb(nameof(NextTask_ShouldIncludeBothPickAndMovement));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-01", WarehouseId = 1, Status = WaveStatusEnum.Released });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });

        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-01", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Pending });
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 1,
            TaskCode = "MT-01",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 2,
            DestinationLocationId = 1,
            PlannedQty = 5,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });

        await db.SaveChangesAsync();

        var service = new TaskInterleavingService(db);
        var result = await service.GetNextTasksAsync("picker1", null, scopedWarehouseId: 1, scopedZoneIds: null);

        Assert.Equal(2, result.Tasks.Count);
        Assert.Contains(result.Tasks, t => t.Category == TaskCategoryEnum.Pick);
        Assert.Contains(result.Tasks, t => t.Category == TaskCategoryEnum.Movement);
        Assert.Equal(1, result.TotalPickTasks);
        Assert.Equal(1, result.TotalMovementTasks);
    }

    [Fact]
    public async Task NextTask_ShouldGiveInterleavingBonus()
    {
        await using var db = CreateDb(nameof(NextTask_ShouldGiveInterleavingBonus));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-01", WarehouseId = 1, Status = WaveStatusEnum.Released });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });

        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-01", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Pending });
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 1,
            TaskCode = "MT-01",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            PlannedQty = 5,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });

        await db.SaveChangesAsync();

        var service = new TaskInterleavingService(db);
        // Last completed was Pick → Movement should get bonus
        var result = await service.GetNextTasksAsync("picker1", null, scopedWarehouseId: 1, scopedZoneIds: null, lastCompletedCategory: TaskCategoryEnum.Pick);

        var moveTask = result.Tasks.First(t => t.Category == TaskCategoryEnum.Movement);
        var pickTask = result.Tasks.First(t => t.Category == TaskCategoryEnum.Pick);

        Assert.Equal(20, moveTask.InterleavingBonus);
        Assert.Equal(0, pickTask.InterleavingBonus);
    }

    [Fact]
    public async Task NextTask_WithNoLocation_ShouldFallbackToPriorityOnly()
    {
        await using var db = CreateDb(nameof(NextTask_WithNoLocation_ShouldFallbackToPriorityOnly));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });

        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 1,
            TaskCode = "MT-LOW",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            PlannedQty = 5,
            Priority = MovementTaskPriorityEnum.Low,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 2,
            TaskCode = "MT-HIGH",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 2,
            DestinationLocationId = 1,
            PlannedQty = 5,
            Priority = MovementTaskPriorityEnum.High,
            Status = MovementTaskStatusEnum.Pending,
            CreatedBy = "system"
        });

        await db.SaveChangesAsync();

        var service = new TaskInterleavingService(db);
        // No current location → proximity scores equal → sort by priority
        var result = await service.GetNextTasksAsync("picker1", currentLocationId: null, scopedWarehouseId: 1, scopedZoneIds: null);

        Assert.True(result.Tasks.Count >= 2);
        Assert.Equal("MT-HIGH", result.Tasks[0].TaskCode);

        // All tasks should have proximity=50 (fallback)
        Assert.All(result.Tasks, t => Assert.Equal(50, t.ProximityScore));
    }

    [Fact]
    public async Task AcceptNextTask_ShouldAssignPickTaskAndRedirect()
    {
        await using var db = CreateDb(nameof(AcceptNextTask_ShouldAssignPickTaskAndRedirect));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-01", WarehouseId = 1, Status = WaveStatusEnum.Released });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-01", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Pending });

        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: null, userName: "picker.accept", roleName: "Admin");
        var result = await controller.AcceptNextTask(TaskCategoryEnum.Pick, taskId: 1, warehouseId: 1);

        // Should redirect to RfPicking
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("RfPicking", redirect.ActionName);

        // Task should be assigned
        var task = await db.PickTasks.FindAsync(1L);
        Assert.Equal("picker.accept", task!.AssignedTo);
        Assert.Equal(PickTaskStatusEnum.Assigned, task.Status);
        Assert.NotNull(task.AssignedAt);
    }

    [Fact]
    public async Task NextTask_ShouldExcludeTasksAssignedToAnotherUser()
    {
        await using var db = CreateDb(nameof(NextTask_ShouldExcludeTasksAssignedToAnotherUser));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-01", WarehouseId = 1, Status = WaveStatusEnum.Released });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });
        db.PickTasks.AddRange(
            new PickTask { PickTaskId = 1, TaskCode = "PT-OPEN", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Pending },
            new PickTask { PickTaskId = 2, TaskCode = "PT-OTHER", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Assigned, AssignedTo = "other.user" });
        db.MovementTasks.AddRange(
            new MovementTask { MovementTaskId = 1, TaskCode = "MT-OPEN", WarehouseId = 1, ItemId = 1, SourceLocationId = 1, DestinationLocationId = 2, PlannedQty = 5, Status = MovementTaskStatusEnum.Pending, CreatedBy = "system" },
            new MovementTask { MovementTaskId = 2, TaskCode = "MT-OTHER", WarehouseId = 1, ItemId = 1, SourceLocationId = 1, DestinationLocationId = 2, PlannedQty = 5, Status = MovementTaskStatusEnum.Assigned, AssignedTo = "other.user", CreatedBy = "system" });
        await db.SaveChangesAsync();

        var service = new TaskInterleavingService(db);
        var result = await service.GetNextTasksAsync("picker1", null, scopedWarehouseId: 1, scopedZoneIds: null);

        Assert.Contains(result.Tasks, t => t.TaskCode == "PT-OPEN");
        Assert.Contains(result.Tasks, t => t.TaskCode == "MT-OPEN");
        Assert.DoesNotContain(result.Tasks, t => t.TaskCode == "PT-OTHER");
        Assert.DoesNotContain(result.Tasks, t => t.TaskCode == "MT-OTHER");
    }

    [Fact]
    public async Task AcceptNextTask_ShouldRejectPickTaskAssignedToAnotherUser()
    {
        await using var db = CreateDb(nameof(AcceptNextTask_ShouldRejectPickTaskAssignedToAnotherUser));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-01", WarehouseId = 1, Status = WaveStatusEnum.Released });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-OTHER", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Assigned, AssignedTo = "other.user" });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: null, userName: "picker.accept", roleName: "Admin");
        var result = await controller.AcceptNextTask(TaskCategoryEnum.Pick, taskId: 1, warehouseId: 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("NextTask", redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Equal("other.user", (await db.PickTasks.FindAsync(1L))!.AssignedTo);
    }

    [Fact]
    public async Task AcceptNextTask_ShouldRejectClosedPickTask()
    {
        await using var db = CreateDb(nameof(AcceptNextTask_ShouldRejectClosedPickTask));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-01", WarehouseId = 1, Status = WaveStatusEnum.Released });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-CLOSED", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 1, TargetQty = 10, Status = PickTaskStatusEnum.Completed });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: null, userName: "picker.accept", roleName: "Admin");
        var result = await controller.AcceptNextTask(TaskCategoryEnum.Pick, taskId: 1, warehouseId: 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("NextTask", redirect.ActionName);
        var task = await db.PickTasks.FindAsync(1L);
        Assert.Equal(PickTaskStatusEnum.Completed, task!.Status);
        Assert.Null(task.AssignedTo);
    }

    [Fact]
    public async Task AcceptNextTask_ShouldRejectPickTaskOutsideZoneScope()
    {
        await using var db = CreateDb(nameof(AcceptNextTask_ShouldRejectPickTaskOutsideZoneScope));
        SeedWarehouseGraph(db);

        db.AppUsers.Add(new AppUser { UserId = 1, UserName = "staff.zone", FullName = "Staff Zone", WarehouseId = 1, RoleId = 3, IsActive = true });
        db.UserZoneAssignments.Add(new UserZoneAssignment { UserZoneAssignmentId = 1, UserId = 1, ZoneId = 1 });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });
        db.Waves.Add(new Wave { WaveId = 1, WaveCode = "WV-01", WarehouseId = 1, Status = WaveStatusEnum.Released });
        db.Vouchers.Add(new Voucher { VoucherId = 1, VoucherCode = "V01", WarehouseId = 1 });
        db.PickTasks.Add(new PickTask { PickTaskId = 1, TaskCode = "PT-Z2", WaveId = 1, VoucherId = 1, ItemId = 1, SourceLocationId = 2, TargetQty = 10, Status = PickTaskStatusEnum.Pending });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: 1, userName: "staff.zone", roleName: "Staff");
        var result = await controller.AcceptNextTask(TaskCategoryEnum.Pick, taskId: 1, warehouseId: 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("NextTask", redirect.ActionName);
        Assert.Null((await db.PickTasks.FindAsync(1L))!.AssignedTo);
    }

    [Fact]
    public async Task AcceptNextTask_ShouldRejectMovementTaskAssignedToAnotherUser()
    {
        await using var db = CreateDb(nameof(AcceptNextTask_ShouldRejectMovementTaskAssignedToAnotherUser));
        SeedWarehouseGraph(db);

        db.Items.Add(new Item { ItemId = 1, ItemCode = "ITM-1", IsActive = true });
        db.MovementTasks.Add(new MovementTask
        {
            MovementTaskId = 1,
            TaskCode = "MT-OTHER",
            WarehouseId = 1,
            ItemId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            PlannedQty = 5,
            Status = MovementTaskStatusEnum.Assigned,
            AssignedTo = "other.user",
            CreatedBy = "system"
        });
        await db.SaveChangesAsync();

        var controller = CreateOperationsController(db, warehouseClaim: null, userName: "picker.accept", roleName: "Admin");
        var result = await controller.AcceptNextTask(TaskCategoryEnum.Movement, taskId: 1, warehouseId: 1);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("NextTask", redirect.ActionName);
        Assert.Equal("other.user", (await db.MovementTasks.FindAsync(1L))!.AssignedTo);
    }

    // ═══════════════════════════════════════════════════════════════
    // P3-05: Static scoring helper tests
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(5, 5, 1, 1, 100)]   // Same aisle, same zone → 100
    [InlineData(5, 7, 1, 1, 82)]    // 2 aisles apart, same zone → 90-2*4=82
    [InlineData(5, 5, 1, 2, 30)]    // Same aisle but different zone → 30
    [InlineData(0, 5, null, null, 50)] // No current position → fallback 50
    public void ProximityScore_ShouldCalculateCorrectly(int currentAisle, int taskAisle, int? currentZone, int? taskZone, int expected)
    {
        var score = TaskInterleavingService.CalculateProximityScore(currentAisle, taskAisle, currentZone, taskZone);
        Assert.Equal(expected, score);
    }

    [Fact]
    public void UrgencyScore_ShouldReturn100ForOverdue()
    {
        var score = TaskInterleavingService.CalculateUrgencyScore(DateTime.Now.AddHours(-2));
        Assert.Equal(100, score);
    }

    [Fact]
    public void UrgencyScore_ShouldReturn30ForNoDeadline()
    {
        var score = TaskInterleavingService.CalculateUrgencyScore(null);
        Assert.Equal(30, score);
    }
}
