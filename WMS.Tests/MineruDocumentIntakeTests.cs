using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WMS.Common;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public class MineruDocumentIntakeTests
{
    [Fact]
    public async Task MineruClient_ShouldParseFileParseResponse()
    {
        var handler = new StubHttpHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/health")
                return Task.FromResult(JsonResponse("""{"status":"healthy"}"""));

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/file_parse")
            {
                var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
                Assert.Contains(multipart, part => string.Equals(
                    part.Headers.ContentDisposition?.Name?.Trim('"'),
                    "files",
                    StringComparison.Ordinal));
                return Task.FromResult(JsonResponse("""
                {
                  "version": "test-1",
                  "task_id": "task-42",
                  "results": {
                    "receipt": {
                      "md_content": "| Mã vật tư | Tên vật tư | Số lượng |\n| --- | --- | --- |\n| SKU-1 | Bột giặt | 2 |",
                      "content_list": [
                        { "type": "table", "table_body": "<table><tr><th>Mã vật tư</th><th>Số lượng</th></tr><tr><td>SKU-1</td><td>2</td></tr></table>" }
                      ]
                    }
                  }
                }
                """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        var client = new MineruDocumentParserClient(
            new StubHttpClientFactory(handler),
            Options.Create(new MinerUOptions { Enabled = true, BaseUrl = "http://mineru.local", TimeoutSeconds = 30 }));

        var result = await client.ParseAsync(Upload("receipt.pdf", "fake-pdf"));

        Assert.True(result.Success);
        Assert.Equal("Success", result.ParseStatus);
        Assert.Equal("test-1", result.Version);
        Assert.Equal("task-42", result.TaskId);
        Assert.Contains("SKU-1", result.RawText);
        Assert.Contains("table_body", result.ContentListJson);
    }

    [Fact]
    public async Task DocumentIntake_ShouldMapExactCodeBarcodeAndName()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldMapExactCodeBarcodeAndName));
        SeedMasterData(db);
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "MAT-001", ItemName = "Vật tư theo mã", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 2, ItemCode = "MAT-002", ItemName = "Vật tư theo barcode", Barcode = "BAR-002", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 3, ItemCode = "MAT-003", ItemName = "Tên chính xác", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var service = CreateIntakeService(db, MarkdownResult("""
        | Mã vật tư | Tên vật tư | Số lượng | ĐVT | Đơn giá | Số lô | NSX | HSD |
        | --- | --- | --- | --- | --- | --- | --- | --- |
        | MAT-001 | Vật tư theo mã | 2 | Cái | 1000 | L001 | 01/05/2026 | 31/12/2026 |
        | BAR-002 | Vật tư theo barcode | 4 | Cái | 2000 | L002 | 02/05/2026 | 30/12/2026 |
        |  | Tên chính xác | 3 | Cái | 3000 | L003 | 03/05/2026 | 29/12/2026 |
        """));

        var result = await service.AnalyzeAsync(Upload("receipt.pdf", "payload"), "staff.user");

        Assert.Equal("Success", result.ParseStatus);
        Assert.Equal(1m, result.Confidence);
        Assert.Equal(new int?[] { 1, 2, 3 }, result.Lines.Select(line => line.ItemId).ToArray());
        Assert.All(result.Lines, line =>
        {
            Assert.True(line.IsMatched);
            Assert.False(line.RequiresReview);
        });
        Assert.Equal(3, await db.Items.CountAsync());
        CleanupStoredDocument(db);
    }

    [Fact]
    public async Task DocumentIntake_ShouldExtractAndMapHeaderAndLines()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldExtractAndMapHeaderAndLines));
        SeedMasterData(db);
        db.Warehouses.Add(new Warehouse { WarehouseId = 10, WarehouseCode = "KHO-CHINH", WarehouseName = "Kho Tổng Hợp Miền Nam", IsActive = true });
        db.Partners.Add(new Partner { PartnerId = 20, PartnerCode = "NCC-XM-001", PartnerName = "Công ty CP Xi Măng Vicem Hà Tiên", PartnerType = PartnerTypeEnum.Supplier, IsActive = true });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "CAP-CDV-CV25", ItemName = "Cuộn dây cáp điện Cadivi CV-2.5", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var service = CreateIntakeService(db, MarkdownResult("""
        Số chứng từ: AI-IN-20260601-001
        Ngày chứng từ: 01/06/2026
        Nhà cung cấp / nguồn giao: NCC-XM-001 - Công ty CP Xi Măng Vicem Hà Tiên
        Kho nhận: KHO-CHINH - Kho Tổng Hợp Miền Nam
        Xe / người giao: 51D-123.45 - Trần Minh Khôi
        Ghi chú: Nhận hàng theo lịch hẹn cửa A.

        | Mã vật tư | Tên vật tư | Số lượng | ĐVT | Đơn giá | Số lô | NSX | HSD |
        | --- | --- | --- | --- | --- | --- | --- | --- |
        | CAP-CDV-CV25 | Cuộn dây cáp điện Cadivi CV-2.5 | 25 | Cái | 18500 | CAP-260601 | 01/06/2026 | 31/12/2028 |
        """));

        var result = await service.AnalyzeAsync(Upload("receipt.pdf", "payload"), "staff.user");

        Assert.Equal("Success", result.ParseStatus);
        Assert.NotNull(result.Header);
        Assert.Equal("AI-IN-20260601-001", result.Header!.ReferenceNo);
        Assert.Equal(new DateTime(2026, 6, 1), result.Header.VoucherDate);
        Assert.Equal(20, result.Header.PartnerId);
        Assert.Equal(10, result.Header.WarehouseId);
        Assert.Equal("51D-123.45", result.Header.VehicleNumber);
        Assert.Equal("Trần Minh Khôi", result.Header.DriverName);
        var line = Assert.Single(result.Lines);
        Assert.Equal(1, line.ItemId);
        Assert.Equal("CAP-260601", line.LotNumber);
        var log = Assert.Single(await db.AiOcrLogs.ToListAsync());
        Assert.Equal("MinerU", log.OcrProvider);
        Assert.Equal("not-reported", log.ModelVersion);
        Assert.Equal(1, log.DetectedItems);
        Assert.True(log.ProcessingTimeMs >= 0);
        using (var metadata = JsonDocument.Parse(log.RawJsonResponse!))
        {
            Assert.False(string.IsNullOrWhiteSpace(metadata.RootElement.GetProperty("sourceDocumentId").GetString()));
            Assert.Equal(64, metadata.RootElement.GetProperty("fileHashSha256").GetString()!.Length);
            Assert.DoesNotContain("CAP-CDV-CV25", log.RawJsonResponse, StringComparison.Ordinal);
        }
        using (var trace = JsonDocument.Parse(log.ParsedData!))
        {
            Assert.Equal("WMS_OCR_TRACE_1", trace.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(1, trace.RootElement.GetProperty("lines")[0].GetProperty("sourceLine").GetInt32());
            Assert.Equal(1, trace.RootElement.GetProperty("lines")[0].GetProperty("itemId").GetInt32());
            Assert.DoesNotContain("CAP-CDV-CV25", log.ParsedData, StringComparison.Ordinal);
            Assert.DoesNotContain("Cuộn dây cáp điện", log.ParsedData, StringComparison.Ordinal);
        }
        CleanupStoredDocument(db);
    }

    [Fact]
    public void AnalyzeReceiptEndpoints_ShouldUseOcrRateLimitPolicy()
    {
        foreach (var methodName in new[] { nameof(VouchersController.AnalyzeReceipt), nameof(VouchersController.AnalyzeReceipts) })
        {
            var method = typeof(VouchersController).GetMethod(methodName);
            Assert.NotNull(method);
            var attribute = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal("ocr", attribute!.PolicyName);
        }

        var programSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Program.cs"));
        Assert.Contains("AddPolicy(\"ocr\"", programSource, StringComparison.Ordinal);
        Assert.Contains("PermitLimit = 5", programSource, StringComparison.Ordinal);
        Assert.Contains("QueueLimit = 0", programSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocumentIntake_ShouldWarnUnknownItemAndNeverCreateMasterData()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldWarnUnknownItemAndNeverCreateMasterData));
        SeedMasterData(db);
        db.Items.Add(new Item { ItemId = 1, ItemCode = "KNOWN", ItemName = "Hàng đã có", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var service = CreateIntakeService(db, MarkdownResult("""
        | Mã vật tư | Tên vật tư | Số lượng |
        | --- | --- | --- |
        | NEW-999 | Hàng chưa có master | 5 |
        """));

        var result = await service.AnalyzeAsync(Upload("unknown.docx", "payload"), "manager.user");

        var line = Assert.Single(result.Lines);
        Assert.Null(line.ItemId);
        Assert.True(line.RequiresReview);
        Assert.Equal("Unmatched", line.MatchKind);
        Assert.Contains(result.Warnings, warning => warning.Contains("chưa khớp vật tư", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, await db.Items.CountAsync());
        CleanupStoredDocument(db);
    }

    [Fact]
    public async Task DocumentIntake_ShouldFlagFuzzySuggestionForManualReview()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldFlagFuzzySuggestionForManualReview));
        SeedMasterData(db);
        db.Items.Add(new Item { ItemId = 10, ItemCode = "OMO-001", ItemName = "Bột giặt OMO", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var service = CreateIntakeService(db, MarkdownResult("""
        | Tên vật tư | Số lượng |
        | --- | --- |
        | Bột giặt OMO đậm đặc | 2 |
        """));

        var result = await service.AnalyzeAsync(Upload("fuzzy.png", "payload", "image/png"), "staff.user");

        var line = Assert.Single(result.Lines);
        Assert.Null(line.ItemId);
        Assert.Equal(10, line.SuggestedItemId);
        Assert.True(line.RequiresReview);
        Assert.Equal("FuzzySuggestion", line.MatchKind);
        Assert.Contains(line.Warnings, warning => warning.Contains("cần chọn thủ công", StringComparison.OrdinalIgnoreCase));
        CleanupStoredDocument(db);
    }

    [Fact]
    public async Task DocumentIntake_ShouldRejectUnsafeExtension()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldRejectUnsafeExtension));
        SeedMasterData(db);
        await db.SaveChangesAsync();
        var service = CreateIntakeService(db, MarkdownResult(""));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AnalyzeAsync(Upload("malware.exe", "payload"), "staff.user"));

        Assert.Equal("DOCUMENT_FILE_TYPE_INVALID", ex.Code);
        Assert.Empty(await db.AiOcrLogs.ToListAsync());
    }

    [Fact]
    public async Task DocumentIntake_ShouldRejectMismatchedMimeType()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldRejectMismatchedMimeType));
        SeedMasterData(db);
        await db.SaveChangesAsync();
        var service = CreateIntakeService(db, MarkdownResult(""));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AnalyzeAsync(Upload("receipt.png", "payload", "application/pdf"), "staff.user"));

        Assert.Equal("DOCUMENT_FILE_MIME_INVALID", ex.Code);
        Assert.Empty(await db.AiOcrLogs.ToListAsync());
    }

    [Fact]
    public async Task DocumentIntake_ShouldRejectForgedFileSignature()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldRejectForgedFileSignature));
        SeedMasterData(db);
        await db.SaveChangesAsync();
        var service = CreateIntakeService(db, MarkdownResult(""));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AnalyzeAsync(RawUpload("receipt.pdf", "<html>not a PDF</html>", "application/pdf"), "staff.user"));

        Assert.Equal("DOCUMENT_FILE_SIGNATURE_INVALID", ex.Code);
        Assert.Empty(await db.AiOcrLogs.ToListAsync());
    }

    [Fact]
    public async Task DocumentIntake_ShouldRejectFileLargerThanConfiguredLimit()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldRejectFileLargerThanConfiguredLimit));
        SeedMasterData(db);
        await db.SaveChangesAsync();
        var service = CreateIntakeService(db, MarkdownResult(""));
        var bytes = new byte[(20 * 1024 * 1024) + 1];
        "%PDF-"u8.CopyTo(bytes);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AnalyzeAsync(UploadBytes("oversized.pdf", bytes, "application/pdf"), "staff.user"));

        Assert.Equal("DOCUMENT_FILE_TOO_LARGE", ex.Code);
        Assert.Empty(await db.AiOcrLogs.ToListAsync());
    }

    [Fact]
    public async Task DocumentIntake_ShouldRejectPathTraversalFileNameWithoutPersistence()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldRejectPathTraversalFileNameWithoutPersistence));
        SeedMasterData(db);
        await db.SaveChangesAsync();
        var service = CreateIntakeService(db, MarkdownResult(""));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AnalyzeAsync(Upload("../../unsafe name.pdf", "payload"), "staff.user"));

        Assert.Equal("DOCUMENT_FILE_MIME_INVALID", ex.Code);
        Assert.Empty(await db.AiOcrLogs.ToListAsync());
    }

    [Fact]
    public async Task AnalyzeReceipt_ShouldUseDocumentIntakeServiceAndReturnCompatibleJson()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_ShouldUseDocumentIntakeServiceAndReturnCompatibleJson));
        var intake = new RecordingIntakeService(new VoucherDocumentIntakeResult
        {
            Provider = "MinerU",
            ParseStatus = "Success",
            RawText = "markdown",
            LogId = 99,
            Confidence = 1m,
            Lines =
            {
                new MappedDocumentLine { ItemId = 1, ItemCode = "MAT-1", ItemName = "Vật tư", Quantity = 2, IsMatched = true }
            }
        });
        var controller = CreateController(db, intake: intake);

        var action = await controller.AnalyzeReceipt(Upload("receipt.pdf", "payload"));

        Assert.True(intake.Called);
        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal("MinerU", GetAnonValue(ok.Value!, "provider"));
        Assert.Equal("Success", GetAnonValue(ok.Value!, "parseStatus"));
        Assert.Equal(99L, GetAnonValue(ok.Value!, "logId"));
        var data = Assert.IsType<string>(GetAnonValue(ok.Value!, "data"));
        Assert.Contains("MAT-1", data);
    }

    [Fact]
    public async Task AnalyzeReceipt_ShouldRemoveWarehouseOwnerAndItemMappingsOutsideUserScope()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_ShouldRemoveWarehouseOwnerAndItemMappingsOutsideUserScope));
        SeedMasterData(db);
        db.Warehouses.AddRange(
            new Warehouse { WarehouseId = 10, WarehouseCode = "WH-ALLOWED", WarehouseName = "Kho được phép", IsActive = true },
            new Warehouse { WarehouseId = 20, WarehouseCode = "WH-FOREIGN", WarehouseName = "Kho ngoài phạm vi", IsActive = true });
        db.Partners.AddRange(
            new Partner { PartnerId = 101, PartnerCode = "OWNER-A", PartnerName = "Chủ hàng A", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true },
            new Partner { PartnerId = 202, PartnerCode = "OWNER-B", PartnerName = "Chủ hàng B", PartnerType = PartnerTypeEnum.Customer, IsThreePlClient = true, IsActive = true });
        db.Items.Add(new Item
        {
            ItemId = 2020,
            ItemCode = "FOREIGN-ITEM",
            ItemName = "Vật tư ngoài phạm vi",
            BaseUomId = 1,
            OwnerPartnerId = 202,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var intake = new RecordingIntakeService(new VoucherDocumentIntakeResult
        {
            Provider = "MinerU",
            ParseStatus = "Success",
            Confidence = 1m,
            Header = new MappedDocumentHeader
            {
                WarehouseId = 20,
                WarehouseCode = "WH-FOREIGN",
                WarehouseName = "Kho ngoài phạm vi",
                InventoryOwnershipMode = "ThreePl",
                OwnerPartnerId = 202,
                OwnerPartnerCode = "OWNER-B",
                OwnerPartnerName = "Chủ hàng B",
                PartnerId = 202,
                PartnerCode = "OWNER-B",
                PartnerName = "Chủ hàng B"
            },
            Lines =
            {
                new MappedDocumentLine
                {
                    LineNumber = 7,
                    ItemId = 2020,
                    ItemCode = "FOREIGN-ITEM",
                    ItemName = "Vật tư ngoài phạm vi",
                    BaseUomId = 1,
                    Quantity = 2,
                    IsMatched = true,
                    MatchKind = "Exact"
                }
            }
        });
        var controller = CreateController(
            db,
            intake,
            roleName: "Manager",
            ownerPartnerIds: new[] { 101 },
            warehouseId: 10);

        var action = await controller.AnalyzeReceipt(Upload("scoped-receipt.pdf", "payload"));

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal("Failed", GetAnonValue(ok.Value!, "parseStatus"));
        var header = Assert.IsType<MappedDocumentHeader>(GetAnonValue(ok.Value!, "header"));
        Assert.Null(header.WarehouseId);
        Assert.Null(header.WarehouseCode);
        Assert.Null(header.OwnerPartnerId);
        Assert.Null(header.OwnerPartnerCode);
        Assert.Null(header.PartnerId);
        Assert.Null(header.PartnerCode);

        var data = Assert.IsType<string>(GetAnonValue(ok.Value!, "data"));
        using var dataDocument = JsonDocument.Parse(data);
        var line = Assert.Single(dataDocument.RootElement.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, line.GetProperty("ItemId").ValueKind);
        Assert.Equal(JsonValueKind.Null, line.GetProperty("ItemCode").ValueKind);
        Assert.Equal("OutOfScope", line.GetProperty("MatchKind").GetString());
        Assert.True(line.GetProperty("RequiresReview").GetBoolean());
        Assert.DoesNotContain("FOREIGN-ITEM", data, StringComparison.Ordinal);
        var warnings = Assert.IsAssignableFrom<IEnumerable<string>>(GetAnonValue(ok.Value!, "warnings"));
        Assert.Contains(warnings, warning => warning.Contains("ngoài phạm vi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DownloadSampleImport100_ShouldRespectWarehouseAndOwnerScope()
    {
        await using var db = CreateDb(nameof(DownloadSampleImport100_ShouldRespectWarehouseAndOwnerScope));
        SeedMasterData(db);
        var warehouseA = new Warehouse { WarehouseId = 10, WarehouseCode = "WH-A", WarehouseName = "Kho A", IsActive = true };
        var warehouseB = new Warehouse { WarehouseId = 20, WarehouseCode = "WH-B", WarehouseName = "Kho B", IsActive = true };
        var zoneA = new Zone { ZoneId = 101, WarehouseId = 10, Warehouse = warehouseA, ZoneCode = "ZA", ZoneName = "Khu A", IsActive = true };
        var zoneB = new Zone { ZoneId = 201, WarehouseId = 20, Warehouse = warehouseB, ZoneCode = "ZB", ZoneName = "Khu B", IsActive = true };
        db.Warehouses.AddRange(warehouseA, warehouseB);
        db.Zones.AddRange(zoneA, zoneB);
        db.Locations.AddRange(
            new Location { LocationId = 1001, ZoneId = 101, Zone = zoneA, LocationCode = "LOC-A", IsActive = true },
            new Location { LocationId = 2001, ZoneId = 201, Zone = zoneB, LocationCode = "LOC-B", IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "GLOBAL-ITEM", ItemName = "Vật tư dùng chung", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 2, ItemCode = "OWNER-A-ITEM", ItemName = "Vật tư chủ hàng A", BaseUomId = 1, OwnerPartnerId = 101, IsActive = true },
            new Item { ItemId = 3, ItemCode = "OWNER-B-ITEM", ItemName = "Vật tư chủ hàng B", BaseUomId = 1, OwnerPartnerId = 202, IsActive = true });
        await db.SaveChangesAsync();
        var controller = CreateController(
            db,
            new FailingVoucherDocumentIntakeService(),
            roleName: "Manager",
            ownerPartnerIds: new[] { 101 },
            warehouseId: 10);

        var result = Assert.IsType<FileContentResult>(await controller.DownloadSampleImport100());

        using var workbook = new XLWorkbook(new MemoryStream(result.FileContents));
        var sheet = workbook.Worksheet("ImportLines");
        var itemCodes = sheet.Column(1).CellsUsed().Skip(1).Select(cell => cell.GetString()).ToList();
        var locationCodes = sheet.Column(6).CellsUsed().Skip(1).Select(cell => cell.GetString()).ToList();
        Assert.NotEmpty(itemCodes);
        Assert.All(itemCodes, code => Assert.Contains(code, new[] { "GLOBAL-ITEM", "OWNER-A-ITEM" }));
        Assert.DoesNotContain("OWNER-B-ITEM", itemCodes);
        Assert.NotEmpty(locationCodes);
        Assert.All(locationCodes, code => Assert.Equal("LOC-A", code));
    }

    [Fact]
    public async Task AnalyzeReceipts_ShouldSkipDuplicateContentHashAndNotDoubleQuantity()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipts_ShouldSkipDuplicateContentHashAndNotDoubleQuantity));
        var intake = new FileAwareIntakeService(fileName => new VoucherDocumentIntakeResult
        {
            Provider = "MinerU",
            ParseStatus = "Success",
            Confidence = 1m,
            Header = new MappedDocumentHeader
            {
                ReferenceNo = "HD-ECOM-2026-071",
                VoucherDate = new DateTime(2026, 6, 9),
                PartnerName = "Nhà phân phối DigiHub Việt Nam"
            },
            Lines =
            {
                new MappedDocumentLine
                {
                    ItemId = 11,
                    ItemCode = "DEMO-ECOM-HEAD-BT-A9",
                    ItemName = "Tai nghe Bluetooth AirBeat A9",
                    Quantity = 80,
                    UnitName = "Cái",
                    LotNumber = "AB9-260609",
                    IsMatched = true
                }
            }
        });
        var controller = CreateController(db, intake: intake);

        var action = await controller.AnalyzeReceipts(new List<IFormFile>
        {
            Upload("ecommerce-inbound-bill-01.jpg", "same-binary-image", "image/jpeg"),
            Upload("ecommerce-inbound-bill-01-copy.jpg", "same-binary-image", "image/jpeg")
        });

        var ok = Assert.IsType<OkObjectResult>(action);
        var root = ToJson(ok.Value!);
        Assert.Equal(1, root.GetProperty("duplicateFileCount").GetInt32());
        Assert.Single(root.GetProperty("documents").EnumerateArray());
        var document = root.GetProperty("documents")[0];
        var line = Assert.Single(document.GetProperty("Lines").EnumerateArray());
        Assert.Equal(80, line.GetProperty("Quantity").GetDecimal());
        Assert.Contains("không nhân đôi số lượng", string.Join("\n", root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString())));
        Assert.Equal(1, intake.Calls);
    }

    [Fact]
    public async Task AnalyzeReceipts_ShouldKeepDifferentDocumentNumbersInSeparateGroups()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipts_ShouldKeepDifferentDocumentNumbersInSeparateGroups));
        var intake = new FileAwareIntakeService(fileName =>
        {
            var is071 = fileName.Contains("071", StringComparison.OrdinalIgnoreCase);
            return new VoucherDocumentIntakeResult
            {
                Provider = "Groq",
                ParseStatus = "Success",
                Confidence = 1m,
                Header = new MappedDocumentHeader
                {
                    ReferenceNo = is071 ? "HD-ECOM-2026-071" : "HD-ECOM-2026-072",
                    VoucherDate = new DateTime(2026, 6, 9),
                    PartnerName = is071 ? "Nhà phân phối DigiHub Việt Nam" : "GearZone Distribution"
                },
                Lines =
                {
                    new MappedDocumentLine
                    {
                        ItemId = 21,
                        ItemCode = "DEMO-ECOM-MOUSE-G102",
                        ItemName = "Chuột gaming Logitech G102",
                        Quantity = is071 ? 80 : 48,
                        UnitName = "Cái",
                        LotNumber = "G102-260609",
                        IsMatched = true
                    }
                }
            };
        });
        var controller = CreateController(db, intake: intake);

        var action = await controller.AnalyzeReceipts(new List<IFormFile>
        {
            Upload("ecommerce-inbound-bill-071.png", "image-071", "image/png"),
            Upload("ecommerce-inbound-bill-072.png", "image-072", "image/png")
        });

        var ok = Assert.IsType<OkObjectResult>(action);
        var root = ToJson(ok.Value!);
        Assert.True(root.GetProperty("requiresDocumentSelection").GetBoolean());
        var documents = root.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(2, documents.Count);
        Assert.Contains(documents, doc => doc.GetProperty("ReferenceNo").GetString() == "HD-ECOM-2026-071"
            && doc.GetProperty("Lines")[0].GetProperty("Quantity").GetDecimal() == 80);
        Assert.Contains(documents, doc => doc.GetProperty("ReferenceNo").GetString() == "HD-ECOM-2026-072"
            && doc.GetProperty("Lines")[0].GetProperty("Quantity").GetDecimal() == 48);
        Assert.Equal(2, intake.Calls);
    }

    [Fact]
    public void VoucherCreate_ShouldRejectOcrLinesFromMixedDocuments()
    {
        var vm = new VoucherCreateViewModel
        {
            Lines =
            {
                new VoucherDetailLine { ItemId = 10, TransactionUomId = 1, TransactionQty = 80, LotNumber = "AB9-260609", OcrDocumentNumber = "HD-ECOM-2026-071" },
                new VoucherDetailLine { ItemId = 11, TransactionUomId = 1, TransactionQty = 48, LotNumber = "G102-260609", OcrDocumentNumber = "HD-ECOM-2026-072" }
            }
        };

        var ex = InvokeOcrDocumentGuard(vm);

        Assert.Equal("OCR_DOCUMENT_MIXED_SOURCE", ex.Code);
    }

    [Fact]
    public void VoucherCreate_ShouldRejectDuplicateOcrLinesInSameDocument()
    {
        var vm = new VoucherCreateViewModel
        {
            Lines =
            {
                new VoucherDetailLine
                {
                    ItemId = 10,
                    TransactionUomId = 1,
                    TransactionQty = 80,
                    LotNumber = "AB9-260609",
                    ManufacturingDate = new DateTime(2026, 6, 9),
                    ExpiryDate = new DateTime(2028, 12, 31),
                    OcrDocumentNumber = "HD-ECOM-2026-071"
                },
                new VoucherDetailLine
                {
                    ItemId = 10,
                    TransactionUomId = 1,
                    TransactionQty = 80,
                    LotNumber = "AB9-260609",
                    ManufacturingDate = new DateTime(2026, 6, 9),
                    ExpiryDate = new DateTime(2028, 12, 31),
                    OcrDocumentNumber = "HD-ECOM-2026-071"
                }
            }
        };

        var ex = InvokeOcrDocumentGuard(vm);

        Assert.Equal("OCR_DOCUMENT_DUPLICATE_LINE", ex.Code);
    }

    [Fact]
    public async Task AnalyzeReceipt_ShouldNotUseLegacyFallbackUnlessEnabled()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_ShouldNotUseLegacyFallbackUnlessEnabled));
        var controller = CreateController(db, intake: null);

        var action = await controller.AnalyzeReceipt(Upload("receipt.jpg", "payload", "image/jpeg"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("MinerU", Assert.IsType<string>(GetAnonValue(badRequest.Value!, "error")));
        Assert.Contains("nhập bằng Excel", Assert.IsType<string>(GetAnonValue(badRequest.Value!, "guidance")));
        Assert.Contains("quét mã vạch", Assert.IsType<string>(GetAnonValue(badRequest.Value!, "guidance")));
        Assert.Contains("chọn vật tư thủ công", Assert.IsType<string>(GetAnonValue(badRequest.Value!, "guidance")));
    }

    [Fact]
    public async Task AnalyzeReceipt_ShouldReturnGuidedFallbackErrorWhenNoDocumentReaderProviderIsAvailable()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_ShouldReturnGuidedFallbackErrorWhenNoDocumentReaderProviderIsAvailable));
        var controller = CreateController(
            db,
            intake: null,
            new Dictionary<string, string?>
            {
                ["MinerU:AllowLegacyFallback"] = "true"
            });

        var action = await controller.AnalyzeReceipt(Upload("receipt.jpg", "payload", "image/jpeg"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Equal("Chưa có dịch vụ đọc chứng từ dự phòng khả dụng.", GetAnonValue(badRequest.Value!, "error"));
        Assert.Equal("DOCUMENT_READER_PROVIDER_UNAVAILABLE", GetAnonValue(badRequest.Value!, "code"));
        var guidance = Assert.IsType<string>(GetAnonValue(badRequest.Value!, "guidance"));
        Assert.Contains("nhập bằng Excel", guidance);
        Assert.Contains("quét mã vạch", guidance);
        Assert.Contains("chọn vật tư thủ công", guidance);
    }

    [Fact]
    public async Task AnalyzeReceipt_ShouldUseLegacyReaderBeforeMineruForImageBillWhenConfigured()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_ShouldUseLegacyReaderBeforeMineruForImageBillWhenConfigured));
        SeedMasterData(db);
        db.Items.Add(new Item { ItemId = 1, ItemCode = "MAT-1", ItemName = "Vật tư bill", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var intake = new RecordingIntakeService(new VoucherDocumentIntakeResult
        {
            Provider = "MinerU",
            ParseStatus = "Success"
        });
        var handler = new StubHttpHandler(_ => Task.FromResult(JsonResponse("""
        {
          "choices": [
            {
              "message": {
                "content": "[{\"ItemCode\":\"MAT-1\",\"ItemName\":\"Vật tư bill\",\"Quantity\":2,\"UnitPrice\":0,\"UnitName\":\"EA\"}]"
              }
            }
          ]
        }
        """)));
        var controller = CreateController(
            db,
            intake,
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler);

        var action = await controller.AnalyzeReceipt(Upload("bill.jpg", "fake-image", "image/jpeg"));

        Assert.False(intake.Called);
        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal("Groq", GetAnonValue(ok.Value!, "provider"));
        Assert.Equal("Success", GetAnonValue(ok.Value!, "parseStatus"));
        var data = Assert.IsType<string>(GetAnonValue(ok.Value!, "data"));
        Assert.Contains("MAT-1", data);
        Assert.Equal(1, await db.AiOcrLogs.CountAsync());
        CleanupStoredDocument(db);
    }

    [Fact]
    public async Task AnalyzeReceipt_LegacyGroq_ShouldUseCurrentConfigurableVisionModel()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_LegacyGroq_ShouldUseCurrentConfigurableVisionModel));
        SeedMasterData(db);
        db.Items.Add(new Item { ItemId = 1, ItemCode = "MAT-1", ItemName = "Vật tư bill", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        string? requestedModel = null;
        var handler = new StubHttpHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var requestJson = JsonDocument.Parse(body);
            requestedModel = requestJson.RootElement.GetProperty("model").GetString();

            return JsonResponse("""
            {
              "choices": [
                {
                  "message": {
                    "content": "[{\"ItemCode\":\"MAT-1\",\"ItemName\":\"Vật tư bill\",\"Quantity\":2,\"UnitPrice\":0,\"UnitName\":\"EA\"}]"
                  }
                }
              ]
            }
            """);
        });
        var controller = CreateController(
            db,
            new RecordingIntakeService(new VoucherDocumentIntakeResult { Provider = "MinerU", ParseStatus = "Success" }),
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler);

        var action = await controller.AnalyzeReceipt(Upload("bill.jpg", "fake-image", "image/jpeg"));

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal("qwen/qwen3.6-27b", requestedModel);

        var source = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Controllers", "VouchersController.Import.cs"));
        Assert.Contains("DefaultGroqVisionModel", source, StringComparison.Ordinal);
        Assert.Contains("_config[\"Groq:VisionModel\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("meta-llama/llama-4-scout-17b-16e-instruct", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeReceipt_LegacyGroqInvalidContent_ShouldFallbackToGeminiUsingHeaderApiKey()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_LegacyGroqInvalidContent_ShouldFallbackToGeminiUsingHeaderApiKey));
        SeedMasterData(db);
        db.Items.Add(new Item { ItemId = 1, ItemCode = "MAT-1", ItemName = "Vật tư bill", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var requestCount = 0;
        string? geminiQuery = null;
        string? geminiApiKeyHeader = null;
        var handler = new StubHttpHandler(request =>
        {
            requestCount++;
            if (request.RequestUri?.Host.Contains("groq", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromResult(JsonResponse("""
                {
                  "choices": [
                    { "message": { "content": "<html><body>upstream proxy page</body></html>" } }
                  ]
                }
                """));
            }

            geminiQuery = request.RequestUri?.Query;
            geminiApiKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.SingleOrDefault()
                : null;
            var text = """[{"ItemCode":"MAT-1","ItemName":"Vật tư bill","Quantity":2,"UnitPrice":0,"UnitName":"EA"}]""";
            return Task.FromResult(JsonResponse(JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text } } } }
                }
            })));
        });
        var controller = CreateController(
            db,
            new RecordingIntakeService(new VoucherDocumentIntakeResult { Provider = "MinerU", ParseStatus = "Success" }),
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["GeminiApiKey"] = "test-gemini-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler);

        try
        {
            var action = await controller.AnalyzeReceipt(Upload("bill.png", "fake-image", "image/png"));

            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.Equal("Gemini", GetAnonValue(ok.Value!, "provider"));
            Assert.Equal("Partial", GetAnonValue(ok.Value!, "parseStatus"));
            Assert.Equal(2, requestCount);
            Assert.True(string.IsNullOrEmpty(geminiQuery));
            Assert.Equal("test-gemini-key", geminiApiKeyHeader);
            var warnings = Assert.IsAssignableFrom<IEnumerable<string>>(GetAnonValue(ok.Value!, "warnings"));
            Assert.Contains(warnings, warning => warning.Contains("Gemini", StringComparison.OrdinalIgnoreCase));
            var log = Assert.Single(await db.AiOcrLogs.ToListAsync());
            Assert.Equal("Gemini", log.OcrProvider);
            Assert.Equal("gemini-2.5-flash", log.ModelVersion);
            Assert.Equal(1, log.DetectedItems);
            Assert.Equal(2, log.Status);
            Assert.True(log.ProcessingTimeMs >= 0);
            using var metadata = JsonDocument.Parse(log.RawJsonResponse!);
            Assert.Equal(64, metadata.RootElement.GetProperty("fileHashSha256").GetString()!.Length);
        }
        finally
        {
            CleanupStoredDocument(db);
        }
    }

    [Fact]
    public async Task AnalyzeReceipt_LegacyGroqRateLimitedOnce_ShouldRetryWithoutDuplicateLog()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_LegacyGroqRateLimitedOnce_ShouldRetryWithoutDuplicateLog));
        SeedMasterData(db);
        db.Items.Add(new Item { ItemId = 1, ItemCode = "MAT-1", ItemName = "Vật tư bill", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var requestCount = 0;
        var handler = new StubHttpHandler(_ =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited", Encoding.UTF8, "text/plain")
                };
                rateLimited.Headers.TryAddWithoutValidation("Retry-After", "0");
                return Task.FromResult(rateLimited);
            }

            return Task.FromResult(JsonResponse("""
            {
              "choices": [
                { "message": { "content": "[{\"ItemCode\":\"MAT-1\",\"Quantity\":2,\"UnitName\":\"EA\"}]" } }
              ]
            }
            """));
        });
        var controller = CreateController(
            db,
            new RecordingIntakeService(new VoucherDocumentIntakeResult { Provider = "MinerU", ParseStatus = "Success" }),
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler);

        try
        {
            var action = await controller.AnalyzeReceipt(Upload("bill.png", "fake-image", "image/png"));

            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.Equal("Groq", GetAnonValue(ok.Value!, "provider"));
            Assert.Equal("Success", GetAnonValue(ok.Value!, "parseStatus"));
            Assert.Equal(2, requestCount);
            var log = Assert.Single(await db.AiOcrLogs.ToListAsync());
            Assert.Equal("Groq", log.OcrProvider);
            Assert.Equal(1, log.DetectedItems);
        }
        finally
        {
            CleanupStoredDocument(db);
        }
    }

    [Fact]
    public async Task AnalyzeReceipt_LegacyGroqTransportFailure_ShouldRetryThenFallbackToGemini()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_LegacyGroqTransportFailure_ShouldRetryThenFallbackToGemini));
        SeedMasterData(db);
        db.Items.Add(new Item { ItemId = 1, ItemCode = "MAT-1", ItemName = "Vật tư bill", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var requestCount = 0;
        var handler = new StubHttpHandler(request =>
        {
            requestCount++;
            if (request.RequestUri?.Host.Contains("groq", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("Injected transport failure without credentials."));
            }

            var text = """[{"ItemCode":"MAT-1","ItemName":"Vật tư bill","Quantity":2,"UnitPrice":0,"UnitName":"EA"}]""";
            return Task.FromResult(JsonResponse(JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { parts = new[] { new { text } } } }
                }
            })));
        });
        var controller = CreateController(
            db,
            new RecordingIntakeService(new VoucherDocumentIntakeResult { Provider = "MinerU", ParseStatus = "Success" }),
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["GeminiApiKey"] = "test-gemini-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler);

        try
        {
            var action = await controller.AnalyzeReceipt(Upload("bill.png", "fake-image", "image/png"));

            var ok = Assert.IsType<OkObjectResult>(action);
            Assert.Equal("Gemini", GetAnonValue(ok.Value!, "provider"));
            Assert.Equal("Partial", GetAnonValue(ok.Value!, "parseStatus"));
            Assert.Equal(3, requestCount);
            var log = Assert.Single(await db.AiOcrLogs.ToListAsync());
            Assert.Equal("Gemini", log.OcrProvider);
            Assert.Equal(1, log.DetectedItems);
        }
        finally
        {
            CleanupStoredDocument(db);
        }
    }

    [Fact]
    public async Task AnalyzeReceipt_LegacyInvalidProviderContent_ShouldNotCreateEmptyDocumentLog()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_LegacyInvalidProviderContent_ShouldNotCreateEmptyDocumentLog));
        SeedMasterData(db);
        await db.SaveChangesAsync();

        var handler = new StubHttpHandler(_ => Task.FromResult(JsonResponse("""
        {
          "choices": [
            { "message": { "content": "<html><body>rate limit gateway</body></html>" } }
          ]
        }
        """)));
        var controller = CreateController(
            db,
            new RecordingIntakeService(new VoucherDocumentIntakeResult { Provider = "MinerU", ParseStatus = "Success" }),
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler);

        try
        {
            var action = await controller.AnalyzeReceipt(Upload("bill.png", "fake-image", "image/png"));

            Assert.IsType<BadRequestObjectResult>(action);
            Assert.Empty(await db.AiOcrLogs.ToListAsync());
        }
        finally
        {
            CleanupStoredDocument(db);
        }
    }

    [Fact]
    public async Task AnalyzeReceipt_LegacyForgedImage_ShouldRejectBeforeCallingProvider()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_LegacyForgedImage_ShouldRejectBeforeCallingProvider));
        SeedMasterData(db);
        await db.SaveChangesAsync();

        var requestCount = 0;
        var handler = new StubHttpHandler(_ =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse("""
            {
              "choices": [
                { "message": { "content": "[{\"ItemCode\":\"MAT-1\",\"Quantity\":1}]" } }
              ]
            }
            """));
        });
        var controller = CreateController(
            db,
            new RecordingIntakeService(new VoucherDocumentIntakeResult { Provider = "MinerU", ParseStatus = "Success" }),
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler);

        var action = await controller.AnalyzeReceipt(
            RawUpload("bill.png", "<html>not an image</html>", "image/png"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Equal("DOCUMENT_FILE_SIGNATURE_INVALID", GetAnonValue(badRequest.Value!, "code"));
        Assert.Equal(0, requestCount);
        Assert.Empty(await db.AiOcrLogs.ToListAsync());
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectMismatchedMimeTypeBeforeParsing()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectMismatchedMimeTypeBeforeParsing));
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService());
        var workbookBytes = CreateWorkbookBytes();

        var action = await controller.ImportLinesExcel(
            UploadBytes("lines.xlsx", workbookBytes, "image/png"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("MIME", Assert.IsType<string>(badRequest.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectForgedSignatureBeforeParsing()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectForgedSignatureBeforeParsing));
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService());

        var action = await controller.ImportLinesExcel(
            RawUpload(
                "lines.xlsx",
                "<html>not an OpenXML workbook</html>",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("nội dung", Assert.IsType<string>(badRequest.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectWrongWorksheetAndReturnStructuredRemediation()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectWrongWorksheetAndReturnStructuredRemediation));
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(sheetName: "WrongSheet");

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        var root = ToJson(badRequest.Value!);
        var error = Assert.Single(root.GetProperty("errors").EnumerateArray());
        Assert.Equal("WORKSHEET_MISSING", error.GetProperty("Code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("Remediation").GetString()));
        Assert.Equal("AllOrNothing", root.GetProperty("policy").GetString());
    }

    [Theory]
    [InlineData(true, false, "HEADER_MISSING")]
    [InlineData(false, true, "HEADER_EXTRA")]
    public async Task ImportLinesExcel_ShouldRejectMissingOrExtraColumns(
        bool removeRequiredHeader,
        bool addExtraColumn,
        string expectedCode)
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectMissingOrExtraColumns) + expectedCode);
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
        {
            if (removeRequiredHeader)
                sheet.Cell(1, 1).Clear();
            if (addExtraColumn)
                sheet.Cell(1, 11).Value = "UnexpectedColumn";
            WriteImportRow(sheet, 2, "ITEM-1", "Vật tư 1", "1", "0", "EA", "WH-A-01");
        });

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains(expectedCode, GetExcelImportErrorCodes(badRequest));
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectUnsupportedTemplateVersion()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectUnsupportedTemplateVersion));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
            WriteImportRow(sheet, 2, "ITEM-1", "Vật tư 1", "1", "0", "EA", "WH-A-01"));
        using (var editable = new XLWorkbook(new MemoryStream(workbook)))
        {
            editable.Worksheet("_WMS_META").Cell(2, 2).Value = "WMS-VOUCHER-LINES-0.1";
            using var output = new MemoryStream();
            editable.SaveAs(output);
            workbook = output.ToArray();
        }

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("TEMPLATE_VERSION_UNSUPPORTED", GetExcelImportErrorCodes(badRequest));
    }

    [Fact]
    public async Task ImportLinesExcel_UnknownItem_ShouldNotCreateMasterDataEvenForAdmin()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_UnknownItem_ShouldNotCreateMasterDataEvenForAdmin));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var beforeCount = await db.Items.CountAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
        {
            WriteImportRow(sheet, 2, "AUDIT_TEST_UNKNOWN", "Chưa có dữ liệu nền", "5", "0", "EA", "WH-A-01");
        });

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("ITEM_NOT_FOUND_OR_OUT_OF_SCOPE", GetExcelImportErrorCodes(badRequest));
        Assert.Equal(beforeCount, await db.Items.CountAsync());
        Assert.DoesNotContain(db.ChangeTracker.Entries<Item>(), entry => entry.State == EntityState.Added);
    }

    [Theory]
    [InlineData("0", "QUANTITY_OUT_OF_RANGE")]
    [InlineData("-2", "QUANTITY_OUT_OF_RANGE")]
    [InlineData("abc", "QUANTITY_INVALID")]
    [InlineData("1e3", "QUANTITY_INVALID")]
    [InlineData("1,000.25", "QUANTITY_INVALID")]
    [InlineData("1.12345", "QUANTITY_OUT_OF_RANGE")]
    [InlineData("100000000000000", "QUANTITY_OUT_OF_RANGE")]
    public async Task ImportLinesExcel_ShouldRejectInvalidQuantityWithoutSilentDefault(string quantity, string expectedCode)
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectInvalidQuantityWithoutSilentDefault) + quantity);
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
            WriteImportRow(sheet, 2, "ITEM-1", "Vật tư 1", quantity, "0", "EA", "WH-A-01"));

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains(expectedCode, GetExcelImportErrorCodes(badRequest));
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectLocationOutsideSelectedWarehouse()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectLocationOutsideSelectedWarehouse));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
            WriteImportRow(sheet, 2, "ITEM-1", "Vật tư 1", "2", "0", "EA", "WH-B-01"));

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("LOCATION_NOT_FOUND_OR_OUT_OF_SCOPE", GetExcelImportErrorCodes(badRequest));
    }

    [Fact]
    public async Task ImportLinesExcel_ValidAlternateUom_ShouldReturnPreviewWithoutDatabaseWrites()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ValidAlternateUom_ShouldReturnPreviewWithoutDatabaseWrites));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var itemCount = await db.Items.CountAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
            WriteImportRow(sheet, 2, "ITEM-1", "Vật tư 1", "2", "100", "BOX", "WH-A-01"));

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal("Preview", GetAnonValue(ok.Value!, "mode"));
        Assert.Equal("AllOrNothing", GetAnonValue(ok.Value!, "policy"));
        Assert.Equal("WMS-VOUCHER-LINES-1.0", GetAnonValue(ok.Value!, "templateVersion"));
        Assert.Equal(64, Assert.IsType<string>(GetAnonValue(ok.Value!, "fileHashSha256")).Length);
        using var data = JsonDocument.Parse(Assert.IsType<string>(GetAnonValue(ok.Value!, "data")));
        var row = Assert.Single(data.RootElement.EnumerateArray());
        Assert.Equal(2, row.GetProperty("TransactionUomId").GetInt32());
        Assert.Equal(10m, row.GetProperty("ConversionRate").GetDecimal());
        Assert.False(row.GetProperty("IsNew").GetBoolean());
        Assert.Equal(itemCount, await db.Items.CountAsync());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectExistingUomWithoutItemConversion()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectExistingUomWithoutItemConversion));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
            WriteImportRow(sheet, 2, "ITEM-2", "Vật tư 2", "2", "0", "BOX", "WH-A-01"));

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("UOM_CONVERSION_MISSING", GetExcelImportErrorCodes(badRequest));
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectTrackedItemMissingLotAndExpiry()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectTrackedItemMissingLotAndExpiry));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
            WriteImportRow(sheet, 2, "ITEM-TRACKED", "Vật tư theo lô", "1", "0", "EA", "WH-A-01"));

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        var codes = GetExcelImportErrorCodes(badRequest);
        Assert.Contains("LOT_REQUIRED", codes);
        Assert.Contains("EXPIRY_REQUIRED", codes);
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectDuplicateRowsUsingAllOrNothingPolicy()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectDuplicateRowsUsingAllOrNothingPolicy));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
        {
            WriteImportRow(sheet, 2, "ITEM-1", "Vật tư 1", "2", "0", "EA", "WH-A-01");
            WriteImportRow(sheet, 3, "ITEM-1", "Vật tư 1", "3", "0", "EA", "WH-A-01");
        });

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("DUPLICATE_ROW", GetExcelImportErrorCodes(badRequest));
        Assert.Equal("AllOrNothing", ToJson(badRequest.Value!).GetProperty("policy").GetString());
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldSkipBlankRowsButKeepFollowingValidRows()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldSkipBlankRowsButKeepFollowingValidRows));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
        {
            WriteImportRow(sheet, 2, "ITEM-1", "Vật tư 1", "2", "0", "EA", "WH-A-01");
            sheet.Cell(3, 1).Value = "   ";
            WriteImportRow(sheet, 4, "ITEM-2", "Vật tư 2", "3", "0", "EA", "WH-A-01");
        });

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal(2, GetAnonValue(ok.Value!, "rowCount"));
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectMoreThanOneThousandRows()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectMoreThanOneThousandRows));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
        {
            for (var row = 2; row <= 1002; row++)
                WriteImportRow(sheet, row, "ITEM-1", "Vật tư 1", "1", "0", "EA", "WH-A-01");
        });

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("ROW_LIMIT_EXCEEDED", GetExcelImportErrorCodes(badRequest));
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldAcceptExactlyOneThousandValidRowsAsPreview()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldAcceptExactlyOneThousandValidRowsAsPreview));
        SeedVoucherImportData(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var workbook = CreateVoucherImportWorkbookBytes(configure: sheet =>
        {
            for (var row = 2; row <= 1001; row++)
                WriteImportRow(sheet, row, "ITEM-1", "Vật tư 1", "1", "0", "EA", "WH-A-01", lotNumber: $"AUDIT_TEST_{row:0000}");
        });

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            warehouseId: 10,
            voucherType: VoucherTypeEnum.NhapKho);

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal(1000, GetAnonValue(ok.Value!, "rowCount"));
        Assert.Equal("Preview", GetAnonValue(ok.Value!, "mode"));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ImportLinesExcel_ShouldRejectFileOverFiveMegabytesBeforeParsing()
    {
        await using var db = CreateDb(nameof(ImportLinesExcel_ShouldRejectFileOverFiveMegabytesBeforeParsing));
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");
        var oversized = new byte[(5 * 1024 * 1024) + 1];

        var action = await controller.ImportLinesExcel(UploadBytes(
            "lines.xlsx",
            oversized,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Contains("5 MB", Assert.IsType<string>(badRequest.Value), StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadImportTemplate_ShouldContainVersionedVeryHiddenMetadata()
    {
        using var db = CreateDb(nameof(DownloadImportTemplate_ShouldContainVersionedVeryHiddenMetadata));
        var controller = CreateController(db, new FailingVoucherDocumentIntakeService(), roleName: "Admin");

        var result = Assert.IsType<FileContentResult>(controller.DownloadImportTemplate());
        using var stream = new MemoryStream(result.FileContents);
        using var workbook = new XLWorkbook(stream);
        var lines = workbook.Worksheet("ImportLines");
        Assert.Equal("ItemCode", lines.Cell(1, 1).GetString());
        Assert.Equal("Notes", lines.Cell(1, 10).GetString());
        var metadata = workbook.Worksheet("_WMS_META");
        Assert.Equal("VoucherLines", metadata.Cell(2, 1).GetString());
        Assert.Equal("WMS-VOUCHER-LINES-1.0", metadata.Cell(2, 2).GetString());
        Assert.Equal(XLWorksheetVisibility.VeryHidden, metadata.Visibility);
    }

    [Fact]
    public async Task CreateVoucherUi_ShouldPreviewExcelAndPreventDuplicateApplyAndSubmit()
    {
        var view = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("confirmExcelImportPreview", view, StringComparison.Ordinal);
        Assert.Contains("appliedExcelImportFingerprints", view, StringComparison.Ordinal);
        Assert.Contains("formData.append('warehouseId'", view, StringComparison.Ordinal);
        Assert.Contains("formData.append('ownerPartnerId'", view, StringComparison.Ordinal);
        Assert.Contains("formData.append('voucherType'", view, StringComparison.Ordinal);
        Assert.Contains("dataset.submitting === 'true'", view, StringComparison.Ordinal);
        Assert.Contains("data-submit-intent", view, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadReceiptDocument_ShouldEnforceOwnerScopeForLinkedVoucher()
    {
        await using var db = CreateDb(nameof(DownloadReceiptDocument_ShouldEnforceOwnerScopeForLinkedVoucher));
        db.Warehouses.Add(new Warehouse
        {
            WarehouseId = 1,
            WarehouseCode = "WH-1",
            WarehouseName = "Kho kiểm thử",
            IsActive = true
        });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 81001,
            VoucherCode = "AUDIT_TEST_OCR_OWNER",
            VoucherType = VoucherTypeEnum.NhapKho,
            VoucherDate = VietnamTime.Today,
            WarehouseId = 1,
            OwnerPartnerId = 202,
            CreatedBy = "owner-202.user"
        });
        db.AiOcrLogs.Add(new AiOcrLog
        {
            AiOcrLogId = 81001,
            VoucherId = 81001,
            ImageUrl = "App_Data/uploads/document-intake/missing.pdf",
            FileName = "owner-202.pdf",
            CreatedBy = "owner-202.user"
        });
        await db.SaveChangesAsync();
        var controller = CreateController(
            db,
            new FailingVoucherDocumentIntakeService(),
            userName: "owner-101.manager",
            roleName: "Manager",
            ownerPartnerIds: new[] { 101 });

        var action = await controller.DownloadReceiptDocument(81001);

        Assert.IsType<ForbidResult>(action);
    }

    [Fact]
    public async Task DownloadReceiptDocument_ShouldLimitUnlinkedLogToCreatorForOwnerScopedManager()
    {
        await using var db = CreateDb(nameof(DownloadReceiptDocument_ShouldLimitUnlinkedLogToCreatorForOwnerScopedManager));
        db.AiOcrLogs.Add(new AiOcrLog
        {
            AiOcrLogId = 81002,
            ImageUrl = "App_Data/uploads/document-intake/missing.pdf",
            FileName = "unlinked.pdf",
            CreatedBy = "another.user"
        });
        await db.SaveChangesAsync();
        var controller = CreateController(
            db,
            new FailingVoucherDocumentIntakeService(),
            userName: "owner-101.manager",
            roleName: "Manager",
            ownerPartnerIds: new[] { 101 });

        var action = await controller.DownloadReceiptDocument(81002);

        Assert.IsType<ForbidResult>(action);
    }

    [Fact]
    public async Task DocumentIntake_ShouldDeleteStoredFileWhenParserThrows()
    {
        await using var db = CreateDb(nameof(DocumentIntake_ShouldDeleteStoredFileWhenParserThrows));
        var service = new VoucherDocumentIntakeService(
            db,
            new EfUnitOfWork(db),
            new ThrowingMineruParserClient(),
            Options.Create(new MinerUOptions { Enabled = true, MaxFileSizeMb = 20 }));
        const string fileStem = "AUDIT_TEST_orphan_cleanup";
        const string storedMarker = "audit-test-orphan-cleanup";

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AnalyzeAsync(Upload($"{fileStem}.pdf", "payload"), "audit.user"));

            Assert.Empty(GetDocumentIntakeFiles(storedMarker));
            Assert.Empty(await db.AiOcrLogs.ToListAsync());
        }
        finally
        {
            foreach (var path in GetDocumentIntakeFiles(storedMarker))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task AnalyzeReceipt_Legacy_ShouldDeleteStoredFileWhenLogPersistenceFails()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_Legacy_ShouldDeleteStoredFileWhenLogPersistenceFails));
        SeedMasterData(db);
        await db.SaveChangesAsync();
        const string marker = "AUDIT_TEST_legacy_orphan";
        var handler = new StubHttpHandler(_ => Task.FromResult(JsonResponse("""
        {
          "choices": [
            { "message": { "content": "[{\"ItemCode\":\"MAT-1\",\"Quantity\":1}]" } }
          ]
        }
        """)));
        var controller = CreateController(
            db,
            new RecordingIntakeService(new VoucherDocumentIntakeResult { Provider = "MinerU", ParseStatus = "Success" }),
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler,
            unitOfWork: new AlwaysFailSaveUnitOfWork());

        try
        {
            var action = await controller.AnalyzeReceipt(Upload($"{marker}.png", "payload", "image/png"));

            Assert.IsType<BadRequestObjectResult>(action);
            Assert.Empty(GetLegacyDocumentIntakeFiles(marker));
        }
        finally
        {
            foreach (var path in GetLegacyDocumentIntakeFiles(marker))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task AnalyzeReceipt_LegacyGroq_ShouldReturnMappedHeaderAndLines()
    {
        await using var db = CreateDb(nameof(AnalyzeReceipt_LegacyGroq_ShouldReturnMappedHeaderAndLines));
        SeedMasterData(db);
        db.Warehouses.Add(new Warehouse { WarehouseId = 10, WarehouseCode = "KHO-CHINH", WarehouseName = "Kho Tổng Hợp Miền Nam", IsActive = true });
        db.Partners.Add(new Partner { PartnerId = 20, PartnerCode = "NCC-XM-001", PartnerName = "Công ty CP Xi Măng Vicem Hà Tiên", PartnerType = PartnerTypeEnum.Supplier, IsActive = true });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "CAP-CDV-CV25", ItemName = "Cuộn dây cáp điện Cadivi CV-2.5", BaseUomId = 1, IsActive = true });
        await db.SaveChangesAsync();

        var groqContent = """
        {
          "Header": {
            "ReferenceNo": "AI-IN-20260601-001",
            "VoucherDate": "2026-06-01",
            "PartnerCode": "NCC-XM-001",
            "PartnerName": "Công ty CP Xi Măng Vicem Hà Tiên",
            "WarehouseCode": "KHO-CHINH",
            "WarehouseName": "Kho Tổng Hợp Miền Nam",
            "InventoryOwnershipMode": "Internal",
            "VehicleNumber": "51D-123.45",
            "DriverName": "Trần Minh Khôi",
            "Description": "Chứng từ nhập kho mẫu"
          },
          "Lines": [
            {
              "ItemCode": "CAP-CDV-CV25",
              "ItemName": "Cuộn dây cáp điện Cadivi CV-2.5",
              "Quantity": 25,
              "UnitPrice": 18500,
              "UnitName": "Cái",
              "LotNumber": "CAP-260601",
              "ManufacturingDate": "2026-06-01",
              "ExpiryDate": "2028-12-31"
            }
          ]
        }
        """;
        var groqPayload = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = groqContent
                    }
                }
            }
        });
        var handler = new StubHttpHandler(_ => Task.FromResult(JsonResponse(groqPayload)));
        var controller = CreateController(
            db,
            new RecordingIntakeService(new VoucherDocumentIntakeResult { Provider = "MinerU", ParseStatus = "Success" }),
            new Dictionary<string, string?>
            {
                ["GroqApiKey"] = "test-groq-key",
                ["MinerU:AllowLegacyFallback"] = "false"
            },
            handler);

        var action = await controller.AnalyzeReceipt(Upload("bill.png", "fake-image", "image/png"));

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal("Groq", GetAnonValue(ok.Value!, "provider"));
        var header = Assert.IsType<MappedDocumentHeader>(GetAnonValue(ok.Value!, "header"));
        Assert.Equal("AI-IN-20260601-001", header.ReferenceNo);
        Assert.Equal(20, header.PartnerId);
        Assert.Equal(10, header.WarehouseId);
        Assert.Equal("Trần Minh Khôi", header.DriverName);
        var data = Assert.IsType<string>(GetAnonValue(ok.Value!, "data"));
        Assert.Contains("CAP-CDV-CV25", data);
        Assert.Contains("CAP-260601", data);
        Assert.Contains("ManufacturingDate", data);
        var log = Assert.Single(await db.AiOcrLogs.AsNoTracking().ToListAsync());
        using (var trace = JsonDocument.Parse(log.ParsedData!))
        {
            Assert.Equal("WMS_OCR_TRACE_1", trace.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(1, trace.RootElement.GetProperty("lines")[0].GetProperty("sourceLine").GetInt32());
            Assert.Equal(1, trace.RootElement.GetProperty("lines")[0].GetProperty("itemId").GetInt32());
            Assert.DoesNotContain("CAP-CDV-CV25", log.ParsedData, StringComparison.Ordinal);
            Assert.DoesNotContain("Cuộn dây cáp điện", log.ParsedData, StringComparison.Ordinal);
        }
        CleanupStoredDocument(db);
    }

    [Fact]
    public async Task CreateVoucherUi_ShouldUseVietnameseDocumentIntakeWording()
    {
        var view = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("Đọc chứng từ bằng AI", view);
        Assert.Contains("buildDocumentReadErrorMessage", view);
        Assert.Contains("kiểm tra trạng thái dịch vụ đọc chứng từ", view);
        Assert.Contains("chọn vật tư thủ công", view);
        Assert.Contains("replaceAppliedOcrRowsIfNeeded", view);
        Assert.Contains("chỉ thay các dòng AI cũ", view);
        Assert.Contains("Giữ phiếu hiện tại", view);
        Assert.Contains("classList.contains('ocr-document-number-input')", view);
        Assert.Contains("classList.contains('ocr-source-line-input')", view);
        Assert.Contains(".pdf,.jpg,.jpeg,.png,.webp,.docx,.pptx,.xlsx", view);
        Assert.Contains("Tải danh sách từ Excel", view);
        Assert.Contains("ImportLinesExcel", view);
        Assert.DoesNotContain("Đọc hóa đơn", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AI OCR", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bill", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateVoucherUi_ShouldPreviewAndApplyDocumentHeaderWithoutOverwritingManualFields()
    {
        var view = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("const analyzeReceiptsUrl", view, StringComparison.Ordinal);
        Assert.Contains("buildDocumentBatchPreviewHtml", view, StringComparison.Ordinal);
        Assert.Contains("wmsOcrDocumentChoice", view, StringComparison.Ordinal);
        Assert.Contains("confirmDocumentHeaderConflictMode", view, StringComparison.Ordinal);
        Assert.Contains("OcrDocumentNumber", view, StringComparison.Ordinal);
        Assert.Contains("AiOcrLogId", view, StringComparison.Ordinal);
        Assert.Contains("OcrSourceLineNumber", view, StringComparison.Ordinal);
        Assert.Contains("SourceLogId", view, StringComparison.Ordinal);
        Assert.DoesNotContain("existing.Quantity = (existing.Quantity || 0) + quantity", view, StringComparison.Ordinal);
        Assert.Contains("function applyDocumentHeaderToForm(header, options = {})", view, StringComparison.Ordinal);
        Assert.Contains("function analyzeHeaderApplyPlan(header)", view, StringComparison.Ordinal);
        Assert.Contains("setInputIfBlank('input[name=\"ReferenceNo\"]'", view, StringComparison.Ordinal);
        Assert.Contains("setSelectIfBlank('select[name=\"PartnerId\"]'", view, StringComparison.Ordinal);
        Assert.Contains("Header đã áp dụng", view, StringComparison.Ordinal);
        Assert.Contains("Hệ thống chỉ áp dụng header vào trường còn trống", view, StringComparison.Ordinal);
        Assert.Contains("function canApplyDocumentValue(element)", view, StringComparison.Ordinal);
        Assert.Contains("getInitialFieldValue(element)", view, StringComparison.Ordinal);
    }


    [Fact]
    public void SampleAiBills_ShouldBeUploadableAndUseParserFriendlyColumns()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var dir = Path.Combine(root, "docs", "sample-ai-bills");
        var requiredColumns = new[] { "Mã vật tư", "Tên vật tư", "Số lượng", "ĐVT", "Đơn giá", "Số lô", "NSX", "HSD" };

        var htmlFiles = new[]
        {
            "wms-ai-inbound-bill.html",
            "wms-ai-inbound-bill-02.html",
            "wms-ai-outbound-bill.html",
            "wms-ai-outbound-bill-02.html"
        };
        var htmlContents = new Dictionary<string, string>();
        foreach (var fileName in htmlFiles)
        {
            var path = Path.Combine(dir, fileName);
            Assert.True(File.Exists(path), $"Missing AI bill HTML source: {fileName}.");
            htmlContents[fileName] = File.ReadAllText(path);
        }

        foreach (var html in htmlContents.Values)
        {
            foreach (var column in requiredColumns)
                Assert.Contains(column, html, StringComparison.Ordinal);

            Assert.Contains("Số chứng từ", html, StringComparison.Ordinal);
            Assert.Contains("Ngày chứng từ", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Internal / unowned", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Fixed Bin", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Hàng 3PL", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Nguyễn Văn A", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Nguyễn Văn B", html, StringComparison.OrdinalIgnoreCase);
        }

        var inboundHtml = htmlContents["wms-ai-inbound-bill.html"];
        var inboundHtml2 = htmlContents["wms-ai-inbound-bill-02.html"];
        var outboundHtml = htmlContents["wms-ai-outbound-bill.html"];
        var outboundHtml2 = htmlContents["wms-ai-outbound-bill-02.html"];
        Assert.Contains("CAP-CDV-CV25", inboundHtml, StringComparison.Ordinal);
        Assert.Contains("BL-NEO-M2000", inboundHtml, StringComparison.Ordinal);
        Assert.Contains("GACH-MEN-000", inboundHtml, StringComparison.Ordinal);
        Assert.Contains("SON-DLX-E18L", inboundHtml2, StringComparison.Ordinal);
        Assert.Contains("ONG-PVC-BM21", inboundHtml2, StringComparison.Ordinal);
        Assert.Contains("KEO-APL-FOAM", inboundHtml2, StringComparison.Ordinal);
        Assert.Contains("SON-DLX-E18L", outboundHtml, StringComparison.Ordinal);
        Assert.Contains("GACH-MEN-000", outboundHtml, StringComparison.Ordinal);
        Assert.Contains("CAP-CDV-CV25", outboundHtml, StringComparison.Ordinal);
        Assert.Contains("BL-NEO-M2000", outboundHtml2, StringComparison.Ordinal);
        Assert.Contains("GACH-VGL-000", outboundHtml2, StringComparison.Ordinal);
        Assert.Contains("CAP-CDV-CV25", outboundHtml2, StringComparison.Ordinal);

        foreach (var fileName in new[]
        {
            "wms-ai-inbound-bill.pdf",
            "wms-ai-inbound-bill.png",
            "wms-ai-inbound-bill.jpg",
            "wms-ai-inbound-bill-02.png",
            "wms-ai-inbound-bill-02.jpg",
            "wms-ai-outbound-bill.pdf",
            "wms-ai-outbound-bill.png",
            "wms-ai-outbound-bill.jpg",
            "wms-ai-outbound-bill-02.png",
            "wms-ai-outbound-bill-02.jpg"
        })
        {
            var path = Path.Combine(dir, fileName);
            Assert.True(File.Exists(path), $"Missing uploadable AI bill sample: {fileName}.");
            Assert.True(new FileInfo(path).Length > 1024, $"AI bill sample is unexpectedly small: {fileName}.");
        }
    }

    private static VoucherDocumentIntakeService CreateIntakeService(AppDbContext db, MinerUDocumentParseResult parseResult)
        => new(
            db,
            new EfUnitOfWork(db),
            new StaticMineruParserClient(parseResult),
            Options.Create(new MinerUOptions { Enabled = true, MaxFileSizeMb = 20 }));

    private static MinerUDocumentParseResult MarkdownResult(string markdown)
        => new()
        {
            Success = true,
            ParseStatus = "Success",
            RawText = markdown,
            Provider = "MinerU"
        };

    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("mineru-" + name + "-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedMasterData(AppDbContext db)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true });
    }

    private static IFormFile Upload(string fileName, string content, string? contentType = null)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var payload = Encoding.UTF8.GetBytes(content);
        byte[] bytes = extension switch
        {
            ".pdf" => Encoding.UTF8.GetBytes($"%PDF-1.7\n{content}\n%%EOF"),
            ".jpg" or ".jpeg" => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.Concat(payload).Concat(new byte[] { 0xFF, 0xD9 }).ToArray(),
            ".png" => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(payload).ToArray(),
            ".webp" => Encoding.ASCII.GetBytes("RIFF0000WEBP").Concat(payload).ToArray(),
            ".docx" => CreateOpenXmlBytes("word/document.xml", content),
            ".pptx" => CreateOpenXmlBytes("ppt/presentation.xml", content),
            ".xlsx" => CreateOpenXmlBytes("xl/workbook.xml", content),
            _ => payload
        };
        contentType ??= extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/pdf"
        };
        return UploadBytes(fileName, bytes, contentType);
    }

    private static IFormFile RawUpload(string fileName, string content, string contentType)
        => UploadBytes(fileName, Encoding.UTF8.GetBytes(content), contentType);

    private static IFormFile UploadBytes(string fileName, byte[] bytes, string contentType)
    {
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] CreateOpenXmlBytes(string requiredEntry, string content)
    {
        using var output = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open(), Encoding.UTF8))
                writer.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
            using (var writer = new StreamWriter(archive.CreateEntry(requiredEntry).Open(), Encoding.UTF8))
                writer.Write(content);
        }
        return output.ToArray();
    }

    private static byte[] CreateWorkbookBytes()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.Worksheets.Add("Lines");
        sheet.Cell(1, 1).Value = "ItemCode";
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static void SeedVoucherImportData(AppDbContext db)
    {
        var warehouseA = new Warehouse
        {
            WarehouseId = 10,
            WarehouseCode = "WH-A",
            WarehouseName = "Kho A",
            IsActive = true
        };
        var warehouseB = new Warehouse
        {
            WarehouseId = 20,
            WarehouseCode = "WH-B",
            WarehouseName = "Kho B",
            IsActive = true
        };
        var zoneA = new Zone
        {
            ZoneId = 101,
            WarehouseId = warehouseA.WarehouseId,
            Warehouse = warehouseA,
            ZoneCode = "ZA",
            ZoneName = "Khu A",
            IsActive = true
        };
        var zoneB = new Zone
        {
            ZoneId = 201,
            WarehouseId = warehouseB.WarehouseId,
            Warehouse = warehouseB,
            ZoneCode = "ZB",
            ZoneName = "Khu B",
            IsActive = true
        };

        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Cái", IsActive = true },
            new UnitOfMeasure { UomId = 2, UomCode = "BOX", UomName = "Thùng", IsActive = true });
        db.Warehouses.AddRange(warehouseA, warehouseB);
        db.Zones.AddRange(zoneA, zoneB);
        db.Locations.AddRange(
            new Location { LocationId = 1001, ZoneId = zoneA.ZoneId, Zone = zoneA, LocationCode = "WH-A-01", IsActive = true },
            new Location { LocationId = 2001, ZoneId = zoneB.ZoneId, Zone = zoneB, LocationCode = "WH-B-01", IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "ITEM-1", ItemName = "Vật tư 1", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 2, ItemCode = "ITEM-2", ItemName = "Vật tư 2", BaseUomId = 1, IsActive = true },
            new Item
            {
                ItemId = 3,
                ItemCode = "ITEM-TRACKED",
                ItemName = "Vật tư theo lô",
                BaseUomId = 1,
                TrackLot = true,
                TrackExpiry = true,
                IsActive = true
            });
        db.UnitConversions.Add(new UnitConversion
        {
            ConversionId = 1,
            ItemId = 1,
            FromUomId = 2,
            ToUomId = 1,
            ConversionRate = 10m,
            IsActive = true
        });
    }

    private static byte[] CreateVoucherImportWorkbookBytes(
        string sheetName = "ImportLines",
        Action<IXLWorksheet>? configure = null,
        bool includeMetadata = true)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        var headers = new[]
        {
            "ItemCode",
            "ItemName",
            "Quantity",
            "UnitPrice",
            "UnitName",
            "LocationCode",
            "ExpiryDate (yyyy-MM-dd)",
            "LotNumber",
            "DefectQty",
            "Notes"
        };
        for (var column = 1; column <= headers.Length; column++)
            sheet.Cell(1, column).Value = headers[column - 1];

        configure?.Invoke(sheet);

        if (includeMetadata)
        {
            var metadata = workbook.Worksheets.Add("_WMS_META");
            metadata.Cell(1, 1).Value = "TemplateType";
            metadata.Cell(1, 2).Value = "TemplateVersion";
            metadata.Cell(2, 1).Value = "VoucherLines";
            metadata.Cell(2, 2).Value = "WMS-VOUCHER-LINES-1.0";
            metadata.Visibility = XLWorksheetVisibility.VeryHidden;
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static void WriteImportRow(
        IXLWorksheet sheet,
        int row,
        string itemCode,
        string itemName,
        string quantity,
        string unitPrice,
        string unitName,
        string locationCode,
        string expiryDate = "",
        string lotNumber = "",
        string defectQuantity = "0",
        string notes = "")
    {
        sheet.Cell(row, 1).Value = itemCode;
        sheet.Cell(row, 2).Value = itemName;
        sheet.Cell(row, 3).Value = quantity;
        sheet.Cell(row, 4).Value = unitPrice;
        sheet.Cell(row, 5).Value = unitName;
        sheet.Cell(row, 6).Value = locationCode;
        sheet.Cell(row, 7).Value = expiryDate;
        sheet.Cell(row, 8).Value = lotNumber;
        sheet.Cell(row, 9).Value = defectQuantity;
        sheet.Cell(row, 10).Value = notes;
    }

    private static IReadOnlyCollection<string> GetExcelImportErrorCodes(BadRequestObjectResult result)
    {
        var root = ToJson(result.Value!);
        return root.GetProperty("errors")
            .EnumerateArray()
            .Select(error => error.GetProperty("Code").GetString() ?? "")
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();
    }

    private static string[] GetDocumentIntakeFiles(string marker)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "uploads", "document-intake");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, $"*{marker}*")
            : Array.Empty<string>();
    }

    private static string[] GetLegacyDocumentIntakeFiles(string marker)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "uploads", "document-intake-legacy");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, $"*{marker}*")
            : Array.Empty<string>();
    }

    private static VouchersController CreateController(
        AppDbContext db,
        IVoucherDocumentIntakeService? intake,
        Dictionary<string, string?>? configurationOverrides = null,
        HttpMessageHandler? httpHandler = null,
        string userName = "staff.user",
        string roleName = "Staff",
        IReadOnlyCollection<int>? ownerPartnerIds = null,
        IUnitOfWork? unitOfWork = null,
        int? warehouseId = null)
    {
        var configurationValues = new Dictionary<string, string?>
        {
            ["MinerU:AllowLegacyFallback"] = "false"
        };
        if (configurationOverrides != null)
        {
            foreach (var pair in configurationOverrides)
                configurationValues[pair.Key] = pair.Value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        unitOfWork ??= new EfUnitOfWork(db);
        var reservationService = new InventoryReservationService(db);
        var balanceService = new InventoryBalanceService(db);
        var inboundService = new InboundExecutionService(db, unitOfWork, balanceService);
        var outboundService = new OutboundExecutionService(db, unitOfWork, reservationService, balanceService);
        var cancellationService = new VoucherCancellationService(db, unitOfWork, reservationService, balanceService);
        var orderStreamingService = new OrderStreamingService(db, unitOfWork, reservationService);
        var integrationService = new NullIntegrationService();
        var documentIntakeService = intake ?? new FailingVoucherDocumentIntakeService();
        var controller = new VouchersController(
            db,
            configuration,
            new StubHttpClientFactory(httpHandler ?? new StubHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))),
            integrationService,
            reservationService,
            unitOfWork,
            outboundService,
            inboundService,
            balanceService,
            cancellationService,
            orderStreamingService,
            new SerialInventoryService(db),
            new InventoryTransactionService(db),
            new CatchWeightService(db),
            new ShipmentLoadService(db, unitOfWork),
            new CarrierIntegrationService(db, integrationService, unitOfWork),
            documentIntakeService,
            new VoucherSharedRuleService(db),
            new VoucherImportQueryService(),
            new VoucherCreateWorkflowService(db),
            new VoucherDetailQueryService());

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Role, roleName)
        };
        foreach (var ownerPartnerId in ownerPartnerIds ?? Array.Empty<int>())
            claims.Add(new Claim(TenantClaimTypes.OwnerPartnerId, ownerPartnerId.ToString()));
        if (warehouseId.HasValue)
            claims.Add(new Claim("WarehouseId", warehouseId.Value.ToString()));
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private static object? GetAnonValue(object source, string propertyName)
        => source.GetType()
            .GetProperties()
            .First(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            .GetValue(source);

    private static JsonElement ToJson(object source)
    {
        var json = JsonSerializer.Serialize(source);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static BusinessRuleException InvokeOcrDocumentGuard(VoucherCreateViewModel vm)
    {
        var method = typeof(VouchersController).GetMethod(
            "ValidateOcrDocumentSourceConsistency",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        try
        {
            method!.Invoke(null, new object[] { vm });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is BusinessRuleException businessRule)
        {
            return businessRule;
        }

        throw new Xunit.Sdk.XunitException("Expected OCR document guard to reject the voucher lines.");
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static void CleanupStoredDocument(AppDbContext db)
    {
        foreach (var relativePath in db.AiOcrLogs.Select(log => log.ImageUrl).Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), relativePath!.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(physicalPath))
                File.Delete(physicalPath);
        }
    }

    private sealed class StaticMineruParserClient(MinerUDocumentParseResult result) : IMineruDocumentParserClient
    {
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<MinerUDocumentParseResult> ParseAsync(IFormFile file, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class ThrowingMineruParserClient : IMineruDocumentParserClient
    {
        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<MinerUDocumentParseResult> ParseAsync(IFormFile file, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Injected parser failure.");
    }

    private sealed class AlwaysFailSaveUnitOfWork : IUnitOfWork
    {
        public bool HasActiveTransaction => false;

        public Task BeginTransactionAsync(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Injected OCR log persistence failure.");

        public Task CommitAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FailingVoucherDocumentIntakeService : IVoucherDocumentIntakeService
    {
        public Task<VoucherDocumentIntakeResult> AnalyzeAsync(IFormFile file, string actor, CancellationToken cancellationToken = default)
            => throw new BusinessRuleException("Dịch vụ đọc chứng từ MinerU chưa sẵn sàng.", "MINERU_UNAVAILABLE", nameof(AiOcrLog));
    }

    private sealed class RecordingIntakeService(VoucherDocumentIntakeResult result) : IVoucherDocumentIntakeService
    {
        public bool Called { get; private set; }

        public Task<VoucherDocumentIntakeResult> AnalyzeAsync(IFormFile file, string actor, CancellationToken cancellationToken = default)
        {
            Called = true;
            Assert.Equal("staff.user", actor);
            return Task.FromResult(result);
        }
    }

    private sealed class FileAwareIntakeService(Func<string, VoucherDocumentIntakeResult> factory) : IVoucherDocumentIntakeService
    {
        public int Calls { get; private set; }

        public Task<VoucherDocumentIntakeResult> AnalyzeAsync(IFormFile file, string actor, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal("staff.user", actor);
            return Task.FromResult(factory(file.FileName));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }

    private sealed class NullIntegrationService : IIntegrationService
    {
        public Task EnqueueAsync(OutboxEventTypeEnum eventType, string targetEndpoint, object payload, string? idempotencyKey = null, string? targetSystem = null)
            => Task.CompletedTask;

        public Task<(bool IsDuplicate, string? CachedResponse, int StatusCode)> CheckIdempotencyAsync(string keyValue, string operationType)
            => Task.FromResult((false, (string?)null, 0));

        public Task SetIdempotencyAsync(string keyValue, string operationType, string response, int statusCode)
            => Task.CompletedTask;

        public Task ProcessOutboxBatchAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
