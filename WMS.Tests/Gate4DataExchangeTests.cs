using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
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
using Xunit.Abstractions;

namespace WMS.Tests;

public sealed class Gate4DataExchangeTests
{
    private readonly ITestOutputHelper _output;

    public Gate4DataExchangeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CsvSecurity_ShouldEmitUtf8BomAndNeutralizeSpreadsheetFormula()
    {
        var csv = string.Join(',', new[]
        {
            SpreadsheetExportSecurity.EscapeCsv("=2+2"),
            SpreadsheetExportSecurity.EscapeCsv(" +SUM(A1:A2)"),
            SpreadsheetExportSecurity.EscapeCsv("-12.5"),
            SpreadsheetExportSecurity.EscapeCsv("@IMPORTDATA(\"https://example.invalid\")")
        });

        var bytes = SpreadsheetExportSecurity.EncodeUtf8Csv(csv);
        var preamble = Encoding.UTF8.GetPreamble();

        Assert.True(bytes.AsSpan(0, preamble.Length).SequenceEqual(preamble));
        var decoded = Encoding.UTF8.GetString(bytes[preamble.Length..]);
        Assert.Contains("'=2+2", decoded, StringComparison.Ordinal);
        Assert.Contains("' +SUM(A1:A2)", decoded, StringComparison.Ordinal);
        Assert.Contains("-12.5", decoded, StringComparison.Ordinal);
        Assert.Contains("'@IMPORTDATA", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportTopItems_ShouldEnforceWarehouseOwnerAndFinancialScopes()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db, includeSecondWarehouse: true);
        SeedOwnersAndCatalog(db);

        var now = VietnamTime.Today;
        db.Vouchers.AddRange(
            PostedInbound(101, "AUDIT_TEST_ALLOWED_LOW", 1, 101, now),
            PostedInbound(102, "AUDIT_TEST_ALLOWED_HIGH", 1, 101, now),
            PostedInbound(103, "AUDIT_TEST_FOREIGN_OWNER", 1, 202, now),
            PostedInbound(104, "AUDIT_TEST_FOREIGN_WAREHOUSE", 2, 101, now));
        db.VoucherDetails.AddRange(
            Detail(1001, 101, 1, 101, 2m, 2_000m),
            Detail(1002, 102, 2, 101, 9m, 9m),
            Detail(1003, 103, 3, 202, 999m, 999_000m),
            Detail(1004, 104, 4, 101, 888m, 888_000m));
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, warehouseId: 1, ownerPartnerIds: new[] { 101 });
        var result = Assert.IsType<FileContentResult>(await controller.ExportTopItems(
            now.AddDays(-1), now.AddDays(1), "in", 50, "value"));

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
        Assert.Contains(now.AddDays(-1).ToString("yyyyMMdd"), result.FileDownloadName, StringComparison.Ordinal);
        Assert.Contains(now.AddDays(1).ToString("yyyyMMdd"), result.FileDownloadName, StringComparison.Ordinal);
        using var workbook = OpenWorkbook(result);
        var sheet = workbook.Worksheet(1);

        Assert.Equal("Mã VT", sheet.Cell(4, 2).GetString());
        Assert.Equal(7, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.DoesNotContain("Tổng Tiền", sheet.Row(4).CellsUsed().Select(c => c.GetString()));
        Assert.Equal("AUDIT_TEST_FORMULA_SAFE", sheet.Cell(5, 2).GetString());
        Assert.Equal(9m, sheet.Cell(5, 6).GetValue<decimal>());
        Assert.Equal("=HYPERLINK(\"https://example.invalid\",\"x\")", sheet.Cell(5, 3).GetString());
        Assert.Empty(sheet.Cell(5, 3).FormulaA1);

        var exportedText = string.Join('|', sheet.CellsUsed().Select(c => c.GetFormattedString()));
        Assert.Contains("AUDIT_TEST_ALLOWED_LOW", exportedText, StringComparison.Ordinal);
        Assert.DoesNotContain("AUDIT_TEST_FOREIGN_OWNER", exportedText, StringComparison.Ordinal);
        Assert.DoesNotContain("AUDIT_TEST_FOREIGN_WAREHOUSE", exportedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportInventory_ShouldAggregateOnlyAllowedOwnerStock()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        SeedOwnersAndCatalog(db);
        db.ItemLocations.AddRange(
            Stock(1, 1, 1, 101, 5m),
            Stock(2, 1, 1, 202, 95m),
            Stock(3, 2, 2, 202, 77m));
        await db.SaveChangesAsync();

        var controller = CreateReportsController(
            db,
            warehouseId: 1,
            ownerPartnerIds: new[] { 101 },
            includeFinancialPermission: true);
        var result = Assert.IsType<FileContentResult>(await controller.ExportInventory(1, null));

        using var workbook = OpenWorkbook(result);
        var sheet = workbook.Worksheet(1);
        Assert.Equal("AUDIT_TEST_ALLOWED_LOW", sheet.Cell(2, 1).GetString());
        Assert.Equal(5m, sheet.Cell(2, 6).GetValue<decimal>());
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
        Assert.DoesNotContain("AUDIT_TEST_FORMULA_SAFE", string.Join('|', sheet.CellsUsed().Select(c => c.GetString())), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportStockSnapshot_ShouldEnforceOwnerAndHideFinancialColumnsWithoutPermission()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        SeedOwnersAndCatalog(db);
        var snapshotDate = VietnamTime.Today.AddDays(-1);
        db.StockSnapshotRuns.Add(new StockSnapshotRun
        {
            StockSnapshotRunId = 501,
            WarehouseId = 1,
            SnapshotDate = snapshotDate,
            CreatedBy = "AUDIT_TEST_GATE4",
            TotalItems = 2,
            TotalValue = 10_010m
        });
        db.StockSnapshots.AddRange(
            Snapshot(5011, 501, snapshotDate, 1, 101, 5m, 2m),
            Snapshot(5012, 501, snapshotDate, 2, 202, 999m, 10m));
        await db.SaveChangesAsync();

        var nonFinancial = CreateReportsController(db, 1, new[] { 101 });
        var nonFinancialFile = Assert.IsType<FileContentResult>(
            await nonFinancial.ExportStockSnapshot(1, snapshotDate, 501));
        using (var workbook = OpenWorkbook(nonFinancialFile))
        {
            var sheet = workbook.Worksheet(1);
            var headers = sheet.Row(4).CellsUsed().Select(c => c.GetString()).ToList();
            Assert.DoesNotContain("Giá vốn", headers);
            Assert.DoesNotContain("Thành tiền", headers);
            Assert.Contains("OWNER-A - Chủ hàng A", sheet.Cell(5, 2).GetString(), StringComparison.Ordinal);
            Assert.Equal(5m, sheet.Cell(5, 5).GetValue<decimal>());
            Assert.Equal(5, sheet.LastColumnUsed()!.ColumnNumber());
            Assert.DoesNotContain("OWNER-B", string.Join('|', sheet.CellsUsed().Select(c => c.GetString())), StringComparison.Ordinal);
        }

        var financial = CreateReportsController(db, 1, new[] { 101 }, includeFinancialPermission: true);
        var financialFile = Assert.IsType<FileContentResult>(
            await financial.ExportStockSnapshot(1, snapshotDate, 501));
        using var financialWorkbook = OpenWorkbook(financialFile);
        var financialSheet = financialWorkbook.Worksheet(1);
        Assert.Equal("Giá vốn", financialSheet.Cell(4, 6).GetString());
        Assert.Equal("Thành tiền", financialSheet.Cell(4, 7).GetString());
        Assert.Equal(2m, financialSheet.Cell(5, 6).GetValue<decimal>());
        Assert.Equal(10m, financialSheet.Cell(5, 7).GetValue<decimal>());
    }

    [Fact]
    public async Task ExportStockSnapshot_EmptyScope_ShouldStillProduceReadableWorkbook()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        SeedOwnersAndCatalog(db);
        await db.SaveChangesAsync();

        var controller = CreateReportsController(db, 1, new[] { 101 });
        var result = Assert.IsType<FileContentResult>(
            await controller.ExportStockSnapshot(1, VietnamTime.Today.AddYears(-5)));

        using var workbook = OpenWorkbook(result);
        var sheet = workbook.Worksheet(1);
        Assert.Contains("Không có dữ liệu", sheet.Cell(5, 1).GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShippingReconciliation_ShouldNotExposeForeignOwnerRows()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        db.Vouchers.AddRange(
            ShippedVoucher(701, "AUDIT_TEST_ALLOWED_SHIPMENT", 101),
            ShippedVoucher(702, "AUDIT_TEST_FOREIGN_SHIPMENT", 202));
        await db.SaveChangesAsync();

        var service = new ShippingReconciliationService(db);
        var rows = await service.BuildAsync(new DeliveryReconciliationFilter
        {
            WarehouseId = 1,
            OwnerPartnerIds = new[] { 101 }
        });

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("AUDIT_TEST_ALLOWED_SHIPMENT", row.VoucherCode));
        Assert.DoesNotContain(rows, row => row.VoucherCode == "AUDIT_TEST_FOREIGN_SHIPMENT");
    }

    [Fact]
    public async Task ShippingReconciliation_ShouldExcludeForeignOwnerPackageLinkedToAllowedLoad()
    {
        await using var db = CreateDb();
        SeedWarehouseGraph(db);
        db.Vouchers.Add(ShippedVoucher(711, "AUDIT_TEST_FOREIGN_LOAD_VOUCHER", 202));
        db.ShipmentLoads.Add(new ShipmentLoad
        {
            ShipmentLoadId = 711,
            LoadCode = "AUDIT_TEST_ALLOWED_LOAD",
            WarehouseId = 1,
            OwnerPartnerId = 101,
            Status = ShipmentLoadStatusEnum.Departed,
            CreatedBy = "AUDIT_TEST_GATE4"
        });
        db.OutboundPackages.Add(new OutboundPackage
        {
            OutboundPackageId = 711,
            PackageCode = "AUDIT_TEST_FOREIGN_PACKAGE",
            VoucherId = 711,
            WarehouseId = 1,
            OwnerPartnerId = 202,
            SourceType = "Manual",
            PackageType = "Carton",
            TotalQuantity = 1,
            ItemCount = 1,
            PackedBy = "AUDIT_TEST_GATE4",
            PackedAt = VietnamTime.Now
        });
        db.ShipmentLoadVouchers.Add(new ShipmentLoadVoucher
        {
            ShipmentLoadVoucherId = 711,
            ShipmentLoadId = 711,
            VoucherId = 711,
            Sequence = 1,
            AddedBy = "AUDIT_TEST_GATE4",
            StatusSnapshot = "Packed"
        });
        await db.SaveChangesAsync();

        var rows = await new ShippingReconciliationService(db).BuildAsync(new DeliveryReconciliationFilter
        {
            WarehouseId = 1,
            OwnerPartnerIds = new[] { 101 }
        });

        Assert.DoesNotContain(rows, row => row.PackageCode == "AUDIT_TEST_FOREIGN_PACKAGE");
        Assert.DoesNotContain(rows, row => row.VoucherCode == "AUDIT_TEST_FOREIGN_LOAD_VOUCHER");
    }

    [Fact]
    public void YardBillingExcelExport_ShouldRequireFinancialPermission()
    {
        var method = typeof(OperationsController).GetMethod(
            "ExportYardBillingChargesExcel",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        var policies = method!.GetCustomAttributes<AuthorizeAttribute>()
            .Select(attribute => attribute.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .ToList();
        Assert.Contains(WmsPermissions.ReportViewFinancial, policies);
    }

    [Fact]
    public void SynchronousSpreadsheetBudget_ShouldRemainReadableAtConfiguredLimit()
    {
        var stopwatch = Stopwatch.StartNew();
        byte[] content;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Gate4Benchmark");
            sheet.Cell(1, 1).Value = "Mã";
            sheet.Cell(1, 2).Value = "Tên";
            sheet.Cell(1, 3).Value = "Số lượng";
            for (var index = 1; index <= SpreadsheetExportSecurity.MaxSynchronousRows; index++)
            {
                var row = index + 1;
                sheet.Cell(row, 1).Value = $"AUDIT_TEST_{index:00000}";
                sheet.Cell(row, 2).Value = $"Dòng kiểm thử {index}";
                sheet.Cell(row, 3).Value = index / 10m;
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            content = stream.ToArray();
        }

        using var reopened = new XLWorkbook(new MemoryStream(content));
        stopwatch.Stop();
        _output.WriteLine(
            "EXPORT_MAX_ROWS={0}; elapsed_ms={1}; file_size_bytes={2}",
            SpreadsheetExportSecurity.MaxSynchronousRows,
            stopwatch.ElapsedMilliseconds,
            content.LongLength);
        Assert.Equal(SpreadsheetExportSecurity.MaxSynchronousRows + 1, reopened.Worksheet(1).LastRowUsed()!.RowNumber());
        Assert.True(content.Length > 0);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Xuất {SpreadsheetExportSecurity.MaxSynchronousRows} dòng mất {stopwatch.Elapsed}.");
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"Gate4-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static ReportsController CreateReportsController(
        AppDbContext db,
        int warehouseId,
        IReadOnlyCollection<int> ownerPartnerIds,
        bool includeFinancialPermission = false)
    {
        var controller = new ReportsController(
            db,
            NullLogger<ReportsController>.Instance,
            new InventoryBalanceService(db),
            new EfUnitOfWork(db));
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "AUDIT_TEST_GATE4"),
            new(ClaimTypes.Role, WmsRoles.Manager),
            new("WarehouseId", warehouseId.ToString())
        };
        foreach (var ownerPartnerId in ownerPartnerIds)
            claims.Add(new Claim(TenantClaimTypes.OwnerPartnerId, ownerPartnerId.ToString()));
        if (includeFinancialPermission)
            claims.Add(new Claim(PermissionClaimTypes.Permission, WmsPermissions.ReportViewFinancial));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Gate4Test"))
            }
        };
        return controller;
    }

    private static XLWorkbook OpenWorkbook(FileContentResult result)
        => new(new MemoryStream(result.FileContents));

    private static void SeedWarehouseGraph(AppDbContext db, bool includeSecondWarehouse = false)
    {
        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 1,
            WarehouseCode = "AUDIT_TEST_WH1",
            WarehouseName = "Kho kiểm thử 1",
            IsActive = true
        });
        db.Zones.Add(new Zone
        {
            ZoneId = 1,
            WarehouseId = 1,
            ZoneCode = "AUDIT_TEST_ZONE1",
            ZoneName = "Khu kiểm thử 1",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        });
        db.Locations.AddRange(
            new Location { LocationId = 1, ZoneId = 1, LocationCode = "AUDIT_TEST_LOC1", IsActive = true },
            new Location { LocationId = 2, ZoneId = 1, LocationCode = "AUDIT_TEST_LOC2", IsActive = true });

        if (!includeSecondWarehouse)
            return;

        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 2,
            WarehouseCode = "AUDIT_TEST_WH2",
            WarehouseName = "Kho kiểm thử 2",
            IsActive = true
        });
        db.Zones.Add(new Zone
        {
            ZoneId = 2,
            WarehouseId = 2,
            ZoneCode = "AUDIT_TEST_ZONE2",
            ZoneName = "Khu kiểm thử 2",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        });
        db.Locations.Add(new Location { LocationId = 3, ZoneId = 2, LocationCode = "AUDIT_TEST_LOC3", IsActive = true });
    }

    private static void SeedOwnersAndCatalog(AppDbContext db)
    {
        db.Partners.AddRange(
            new Partner { PartnerId = 101, PartnerCode = "OWNER-A", PartnerName = "Chủ hàng A", PartnerType = PartnerTypeEnum.Both },
            new Partner { PartnerId = 202, PartnerCode = "OWNER-B", PartnerName = "Chủ hàng B", PartnerType = PartnerTypeEnum.Both });
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "CAI", UomName = "Cái" });
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "AUDIT_TEST_ALLOWED_LOW", ItemName = "Hàng được phép 1", BaseUomId = 1, UnitCost = 2m },
            new Item { ItemId = 2, ItemCode = "AUDIT_TEST_FORMULA_SAFE", ItemName = "=HYPERLINK(\"https://example.invalid\",\"x\")", BaseUomId = 1, UnitCost = 3m },
            new Item { ItemId = 3, ItemCode = "AUDIT_TEST_FOREIGN_OWNER", ItemName = "Hàng chủ khác", BaseUomId = 1, UnitCost = 4m },
            new Item { ItemId = 4, ItemCode = "AUDIT_TEST_FOREIGN_WAREHOUSE", ItemName = "Hàng kho khác", BaseUomId = 1, UnitCost = 5m });
    }

    private static Voucher PostedInbound(long id, string code, int warehouseId, int ownerPartnerId, DateTime date)
        => new()
        {
            VoucherId = id,
            VoucherCode = code,
            VoucherType = VoucherTypeEnum.NhapKho,
            VoucherDate = date,
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerPartnerId,
            IsPosted = true,
            CreatedBy = "AUDIT_TEST_GATE4"
        };

    private static VoucherDetail Detail(long id, long voucherId, int itemId, int ownerPartnerId, decimal qty, decimal amount)
        => new()
        {
            VoucherDetailId = id,
            VoucherId = voucherId,
            ItemId = itemId,
            OwnerPartnerId = ownerPartnerId,
            TransactionQty = qty,
            BaseQty = qty,
            TransactionUomId = 1,
            ConversionRate = 1m,
            UnitPrice = qty == 0 ? 0 : amount / qty,
            LineAmount = amount,
            LineNumber = 1
        };

    private static ItemLocation Stock(int id, int itemId, int locationId, int ownerPartnerId, decimal qty)
        => new()
        {
            ItemLocationId = id,
            ItemId = itemId,
            LocationId = locationId,
            OwnerPartnerId = ownerPartnerId,
            Quantity = qty,
            ReservedQty = 0m
        };

    private static StockSnapshot Snapshot(long id, long runId, DateTime date, int itemId, int ownerPartnerId, decimal qty, decimal unitCost)
        => new()
        {
            SnapshotId = id,
            StockSnapshotRunId = runId,
            SnapshotDate = date,
            ItemId = itemId,
            OwnerPartnerId = ownerPartnerId,
            WarehouseId = 1,
            ClosingStock = qty,
            UnitCost = unitCost,
            TotalValue = qty * unitCost
        };

    private static Voucher ShippedVoucher(long id, string code, int ownerPartnerId)
        => new()
        {
            VoucherId = id,
            VoucherCode = code,
            VoucherType = VoucherTypeEnum.XuatKho,
            VoucherDate = VietnamTime.Today,
            WarehouseId = 1,
            OwnerPartnerId = ownerPartnerId,
            IsPosted = true,
            ShippedAt = VietnamTime.Now,
            FulfillmentStatus = FulfillmentStatusEnum.Shipped,
            CreatedBy = "AUDIT_TEST_GATE4"
        };
}
