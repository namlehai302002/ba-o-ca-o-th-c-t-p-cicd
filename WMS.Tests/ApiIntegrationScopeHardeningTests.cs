using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public sealed class ApiIntegrationScopeHardeningTests
{
    [Fact]
    public async Task EveryApiAction_ShouldRejectMissingApiKeyBeforeRuntimeWork()
    {
        await using var db = CreateDb();
        var controller = CreateController(
            db,
            scopedWarehouseId: 1,
            scopedOwnerPartnerId: 10,
            includeApiKey: false);

        var results = new List<IActionResult>
        {
            await controller.GetItems(null, null, null),
            await controller.GetStock(null, null),
            await controller.GetVouchers(null, null, null, null, null),
            await controller.GetVoucherDetail(1),
            await controller.GetKpi(null),
            controller.GetDocs(),
            await controller.CreateItem(new ApiCreateItemRequest()),
            await controller.UpdateItem(1, new ApiUpdateItemRequest()),
            await controller.CreateVoucher(new ApiCreateVoucherRequest()),
            await controller.MheCallback(new MheCallbackRequest()),
            await controller.CarrierCallback(new CarrierShipmentCallbackRequest()),
            controller.OpenApiJson(),
            await controller.ImportEdi(new ApiEdiImportRequest()),
            await controller.ReplayEdi(1),
            await controller.ExportEdi(1),
            await controller.ConfirmShipment(1),
            await controller.IssueThreePlInvoice(1),
            await controller.ReplayWebhook(1)
        };

        var testedActions = new[]
        {
            nameof(ApiIntegrationController.GetItems),
            nameof(ApiIntegrationController.GetStock),
            nameof(ApiIntegrationController.GetVouchers),
            nameof(ApiIntegrationController.GetVoucherDetail),
            nameof(ApiIntegrationController.GetKpi),
            nameof(ApiIntegrationController.GetDocs),
            nameof(ApiIntegrationController.CreateItem),
            nameof(ApiIntegrationController.UpdateItem),
            nameof(ApiIntegrationController.CreateVoucher),
            nameof(ApiIntegrationController.MheCallback),
            nameof(ApiIntegrationController.CarrierCallback),
            nameof(ApiIntegrationController.OpenApiJson),
            nameof(ApiIntegrationController.ImportEdi),
            nameof(ApiIntegrationController.ReplayEdi),
            nameof(ApiIntegrationController.ExportEdi),
            nameof(ApiIntegrationController.ConfirmShipment),
            nameof(ApiIntegrationController.IssueThreePlInvoice),
            nameof(ApiIntegrationController.ReplayWebhook)
        };
        var exposedApiActions = typeof(ApiIntegrationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttributes<HttpGetAttribute>(true).Any()
                || method.GetCustomAttributes<HttpPostAttribute>(true).Any()
                || method.GetCustomAttributes<HttpPutAttribute>(true).Any()
                || method.GetCustomAttributes<HttpPatchAttribute>(true).Any()
                || method.GetCustomAttributes<HttpDeleteAttribute>(true).Any())
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(testedActions.OrderBy(name => name, StringComparer.Ordinal), exposedApiActions);
        Assert.Equal(testedActions.Length, results.Count);
        Assert.All(results, result =>
        {
            var response = Assert.IsAssignableFrom<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        });
    }

    [Fact]
    public async Task CreateVoucher_ShouldRejectUndefinedVoucherTypeWithoutPersisting()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        var result = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            VoucherType = 255,
            Lines = { new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 1 } }
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.Vouchers);
    }

    [Fact]
    public async Task CreateItemAndVoucher_ShouldRejectInactiveUomAndSubPrecisionQuantity()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        var uom = await db.UnitsOfMeasure.SingleAsync(u => u.UomId == 1);
        uom.IsActive = false;
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await controller.CreateItem(new ApiCreateItemRequest
        {
            ItemCode = "AUDIT_TEST_INACTIVE_UOM",
            ItemName = "Inactive UOM boundary",
            BaseUomId = 1,
            OwnerPartnerId = 10
        }));
        Assert.IsType<BadRequestObjectResult>(await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines = { new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 1 } }
        }));
        Assert.Empty(db.Vouchers);

        uom.IsActive = true;
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines = { new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 0.00004m } }
        }));
        Assert.Empty(db.Vouchers);

        var accepted = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines = { new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 0.00005m } }
        });

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(accepted).StatusCode);
        var detail = await db.VoucherDetails.SingleAsync();
        Assert.Equal(0.0001m, detail.TransactionQty);
        Assert.Equal(0.0001m, detail.BaseQty);
        Assert.Equal(1m, detail.ConversionRate);
    }

    [Fact]
    public async Task GetItemsAndStock_ShouldUseScopedWarehouseAndOwnerBalances()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();

        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        var itemsResult = await controller.GetItems(search: null, categoryId: null, active: null);
        var itemRows = GetDataRows(Assert.IsType<OkObjectResult>(itemsResult).Value!);
        var item = Assert.Single(itemRows);
        Assert.Equal("ITEM-SCOPE", GetString(item, "ItemCode"));
        Assert.Equal(5m, GetDecimal(item, "CurrentStock"));

        var stockResult = await controller.GetStock(warehouseId: null, itemId: 1);
        var stockRows = GetDataRows(Assert.IsType<OkObjectResult>(stockResult).Value!);
        var stock = Assert.Single(stockRows);
        Assert.Equal(5m, GetDecimal(stock, "Quantity"));
        Assert.Equal(1, GetInt(stock, "WarehouseId"));

        var kpiResult = await controller.GetKpi(warehouseId: null);
        var kpiData = GetAnonValue(Assert.IsType<OkObjectResult>(kpiResult).Value, "data")!;
        Assert.Equal(1, GetInt(kpiData, "totalActiveItems"));
        Assert.Equal(5m, GetDecimal(kpiData, "totalStock"));
        Assert.Equal(0, GetInt(kpiData, "openExceptions"));
        Assert.False(Assert.IsType<bool>(GetAnonValue(kpiData, "exceptionMetricAvailable")));
    }

    [Fact]
    public async Task GetKpi_ShouldExcludeResolvedAndIgnoredCasesFromOpenExceptionCount()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        db.OperationExceptionCases.AddRange(
            NewExceptionCase(1, "OPEN", OperationExceptionStatusEnum.Open),
            NewExceptionCase(2, "ACK", OperationExceptionStatusEnum.Acknowledged),
            NewExceptionCase(3, "RESOLVED", OperationExceptionStatusEnum.Resolved),
            NewExceptionCase(4, "IGNORED", OperationExceptionStatusEnum.Ignored));
        await db.SaveChangesAsync();

        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: null);
        var result = await controller.GetKpi(warehouseId: null);
        var data = GetAnonValue(Assert.IsType<OkObjectResult>(result).Value, "data")!;

        Assert.Equal(2, GetInt(data, "openExceptions"));
        Assert.True(Assert.IsType<bool>(GetAnonValue(data, "exceptionMetricAvailable")));
    }

    [Fact]
    public async Task DirectIdReadExportAndMutation_ShouldReturnSafeEnvelopeOutsideApiScope()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 9001,
            VoucherCode = "PX-OUT-OF-SCOPE",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 2,
            OwnerPartnerId = 20,
            VoucherDate = DateTime.Today,
            CreatedBy = "test"
        });
        db.EdiMessages.Add(new EdiMessage
        {
            EdiMessageId = 9002,
            MessageType = EdiMessageTypeEnum.Order940,
            Direction = EdiDirectionEnum.Outbound,
            Status = EdiMessageStatusEnum.Validated,
            WarehouseId = 2,
            PartnerId = 20,
            ControlNumber = "EDI-OUT-OF-SCOPE",
            Payload = "ISA*00*~ST*940*0001~SE*2*0001~"
        });
        db.CarrierShipments.Add(new CarrierShipment
        {
            CarrierShipmentId = 9003,
            CarrierConnectorId = 1,
            WarehouseId = 2,
            OwnerPartnerId = 20,
            VoucherId = 9001,
            OutboundPackageId = 1,
            Status = CarrierShipmentStatusEnum.Pending,
            CarrierCodeSnapshot = "MOCK",
            CarrierNameSnapshot = "Mock Carrier",
            IdempotencyKey = "scope-carrier-9003",
            CorrelationId = "scope-carrier-9003"
        });
        db.MheCommands.Add(new MheCommand
        {
            MheCommandId = 9007,
            CommandCode = "MHE-OUT-OF-SCOPE",
            WarehouseId = 2,
            OwnerPartnerId = 20,
            CommandType = MheCommandTypeEnum.MoveInventory,
            Status = MheCommandStatusEnum.Pending,
            IdempotencyKey = "mhe-out-of-scope",
            CorrelationId = "mhe-out-of-scope",
            PayloadJson = "{}",
            CreatedBy = "test"
        });
        db.ThreePlInvoices.Add(new ThreePlInvoice
        {
            ThreePlInvoiceId = 9004,
            InvoiceCode = "3PL-OUT-OF-SCOPE",
            WarehouseId = 2,
            OwnerPartnerId = 20,
            PeriodFrom = DateTime.Today.AddDays(-1),
            PeriodTo = DateTime.Today,
            ApiPublicId = "api-9004",
            CreatedBy = "test"
        });
        db.WebhookDeliveries.AddRange(
            new WebhookDelivery
            {
                WebhookDeliveryId = 9005,
                WebhookSubscriptionId = 1,
                EventType = "InventoryChanged",
                IdempotencyKey = "webhook-out-of-scope",
                PayloadJson = "{\"warehouseId\":2,\"ownerPartnerId\":20,\"itemId\":1}",
                Signature = "signature",
                Status = WebhookDeliveryStatusEnum.Failed
            },
            new WebhookDelivery
            {
                WebhookDeliveryId = 9006,
                WebhookSubscriptionId = 1,
                EventType = "InventoryChanged",
                IdempotencyKey = "webhook-missing-scope",
                PayloadJson = "{\"itemId\":1}",
                Signature = "signature",
                Status = WebhookDeliveryStatusEnum.Failed
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        AssertForbiddenScope(await controller.GetVoucherDetail(9001));
        AssertForbiddenScope(await controller.ExportEdi(9002));
        AssertForbiddenScope(await controller.ConfirmShipment(9003));
        AssertForbiddenScope(await controller.IssueThreePlInvoice(9004));
        AssertForbiddenScope(await controller.ReplayWebhook(9005));
        AssertForbiddenScope(await controller.ReplayWebhook(9006));
        AssertForbiddenScope(await controller.MheCallback(new MheCallbackRequest
        {
            CorrelationId = "  mhe-out-of-scope  ",
            IdempotencyKey = "mhe-callback-out-of-scope",
            Status = MheCommandStatusEnum.Completed,
            PayloadJson = "{}"
        }));
        AssertForbiddenScope(await controller.CarrierCallback(new CarrierShipmentCallbackRequest
        {
            CorrelationId = "  scope-carrier-9003  ",
            IdempotencyKey = "carrier-callback-out-of-scope",
            Status = CarrierShipmentStatusEnum.Delivered,
            PayloadJson = "{}"
        }));

        Assert.Equal(MheCommandStatusEnum.Pending, (await db.MheCommands.FindAsync(9007L))!.Status);
        Assert.Equal(CarrierShipmentStatusEnum.Pending, (await db.CarrierShipments.FindAsync(9003L))!.Status);
        Assert.Empty(db.MheMissionEvents);
        Assert.Empty(db.CarrierShipmentEvents);
    }

    [Fact]
    public async Task ImportEdi_ShouldDefaultToApiScopeAndRejectExplicitForeignScope()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        var accepted = await controller.ImportEdi(new ApiEdiImportRequest
        {
            MessageType = EdiMessageTypeEnum.Order940,
            Payload = "ISA*00*~ST*940*0001~SE*2*0001~",
            FileName = "order-940.edi"
        });

        Assert.IsType<ObjectResult>(accepted);
        var message = Assert.Single(await db.EdiMessages.ToListAsync());
        Assert.Equal(1, message.WarehouseId);
        Assert.Equal(10, message.PartnerId);

        var rejected = await controller.ImportEdi(new ApiEdiImportRequest
        {
            MessageType = EdiMessageTypeEnum.Order940,
            Payload = "ISA*00*~ST*940*0002~SE*2*0002~",
            WarehouseId = 2,
            PartnerId = 10
        });

        AssertForbiddenScope(rejected);
    }

    [Fact]
    public async Task CreateVoucher_ShouldRejectInternalItemWhenApiOwnerScoped()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();
        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        var rejected = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines =
            {
                new ApiVoucherLine { ItemId = 2, LocationId = 1, Quantity = 1 }
            }
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(rejected);
        var errors = Assert.IsAssignableFrom<IEnumerable>(GetAnonValue(badRequest.Value, "errors"));
        Assert.Contains(errors.Cast<object>().Select(e => e.ToString()), e => e != null && e.Contains("outside the API owner scope", StringComparison.Ordinal));
        Assert.Empty(db.Vouchers);

        var accepted = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines =
            {
                new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 2 }
            }
        });

        var created = Assert.IsType<ObjectResult>(accepted);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var voucher = await db.Vouchers.Include(v => v.Details).SingleAsync();
        Assert.Equal(10, voucher.OwnerPartnerId);
        var detail = Assert.Single(voucher.Details);
        Assert.Equal(10, detail.OwnerPartnerId);
        Assert.Equal(1, detail.ItemId);
    }

    [Fact]
    public async Task CreateVoucher_ShouldEnforceInboundTrackingPolicyForApiLines()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();
        var trackedItem = await db.Items.SingleAsync(i => i.ItemId == 1);
        trackedItem.TrackLot = true;
        trackedItem.TrackExpiry = true;
        await db.SaveChangesAsync();

        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        var missingExpiry = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines =
            {
                new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 1, LotNumber = "lot-api-01" }
            }
        });

        Assert.IsType<BadRequestObjectResult>(missingExpiry);
        Assert.Empty(db.Vouchers);

        var expiryBeforeManufacturing = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines =
            {
                new ApiVoucherLine
                {
                    ItemId = 1,
                    LocationId = 1,
                    Quantity = 1,
                    LotNumber = "lot-api-01",
                    ManufacturingDate = new DateTime(2026, 7, 2),
                    ExpiryDate = new DateTime(2026, 7, 1)
                }
            }
        });

        Assert.IsType<BadRequestObjectResult>(expiryBeforeManufacturing);
        Assert.Empty(db.Vouchers);

        var accepted = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines =
            {
                new ApiVoucherLine
                {
                    ItemId = 1,
                    LocationId = 1,
                    Quantity = 2,
                    LotNumber = "lot-api-02",
                    ManufacturingDate = new DateTime(2026, 7, 1),
                    ExpiryDate = new DateTime(2027, 7, 1)
                }
            }
        });

        var created = Assert.IsType<ObjectResult>(accepted);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var detail = await db.VoucherDetails.SingleAsync();
        Assert.Equal("LOT-API-02", detail.LotNumber);
        Assert.Equal(new DateTime(2026, 7, 1), detail.ManufacturingDate);
        Assert.Equal(new DateTime(2027, 7, 1), detail.ExpiryDate);
    }

    [Fact]
    public async Task CreateCustomerReturn_ShouldRequireQualityInspectionForApiLines()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();

        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);
        var accepted = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            VoucherType = (int)VoucherTypeEnum.KhachTra,
            Lines =
            {
                new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 2 }
            }
        });

        var created = Assert.IsType<ObjectResult>(accepted);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var detail = await db.VoucherDetails.SingleAsync();
        Assert.Equal(QualityStatusEnum.Pending, detail.QualityStatus);
    }

    [Fact]
    public async Task CreateVoucher_ShouldIgnoreApiExpiryForItemWithoutExpiryTracking()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();
        var item = await db.Items.SingleAsync(i => i.ItemId == 1);
        item.TrackLot = false;
        item.TrackExpiry = false;
        await db.SaveChangesAsync();

        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        var accepted = await controller.CreateVoucher(new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            Lines =
            {
                new ApiVoucherLine
                {
                    ItemId = 1,
                    LocationId = 1,
                    Quantity = 2,
                    LotNumber = "api-no-expiry",
                    ManufacturingDate = new DateTime(2026, 7, 1),
                    ExpiryDate = new DateTime(2027, 7, 1)
                }
            }
        });

        var created = Assert.IsType<ObjectResult>(accepted);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var detail = await db.VoucherDetails.SingleAsync();
        Assert.Equal("API-NO-EXPIRY", detail.LotNumber);
        Assert.Equal(new DateTime(2026, 7, 1), detail.ManufacturingDate);
        Assert.Null(detail.ExpiryDate);
    }

    [Fact]
    public async Task CreateVoucher_ShouldUseIdempotencyHeaderToAvoidDuplicateRetry()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        await db.SaveChangesAsync();
        var integrationService = new IntegrationService(db, new NullHttpClientFactory(), NullLogger<IntegrationService>.Instance);
        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10, integrationService);
        controller.ControllerContext.HttpContext.Request.Headers["X-Idempotency-Key"] = "voucher-retry-001";
        var request = new ApiCreateVoucherRequest
        {
            WarehouseId = 1,
            VoucherType = (int)VoucherTypeEnum.NhapKho,
            ReferenceNo = "ERP-ASN-001",
            Lines =
            {
                new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 2 }
            }
        };

        var first = await controller.CreateVoucher(request);
        var firstCreated = Assert.IsType<ObjectResult>(first);
        Assert.Equal(StatusCodes.Status201Created, firstCreated.StatusCode);
        var firstVoucherId = GetCreatedVoucherId(firstCreated);

        var second = await controller.CreateVoucher(request);
        var secondCreated = Assert.IsType<ObjectResult>(second);
        Assert.Equal(StatusCodes.Status201Created, secondCreated.StatusCode);

        Assert.Equal(firstVoucherId, GetCreatedVoucherId(secondCreated));
        var voucher = await db.Vouchers.Include(v => v.Details).SingleAsync();
        Assert.Equal("ERP-ASN-001", voucher.ReferenceNo);
        Assert.Single(voucher.Details);
        Assert.Single(await db.IntegrationIdempotencyKeys.ToListAsync());
    }

    [Fact]
    public async Task CreateVoucher_ShouldReserveIdempotencyKeyBeforeMutatingUnderConcurrentRetry()
    {
        var connectionString = $"Data Source=api-idempotency-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using (var setup = new AppDbContext(options) { SkipAudit = true })
        {
            await CreateApiVoucherSqliteSchemaAsync(setup);
            SeedWarehouseOwnerFixture(setup);
            await setup.SaveChangesAsync();
        }

        var checkGate = new IdempotencyCheckGate(participants: 2);

        async Task<IActionResult> SendAsync()
        {
            await using var db = new AppDbContext(options) { SkipAudit = true };
            var integrationService = new GatedDbIntegrationService(db, checkGate);
            var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10, integrationService);
            controller.ControllerContext.HttpContext.Request.Headers["X-Idempotency-Key"] = "voucher-race-001";

            return await controller.CreateVoucher(new ApiCreateVoucherRequest
            {
                WarehouseId = 1,
                VoucherType = (int)VoucherTypeEnum.NhapKho,
                ReferenceNo = "ERP-ASN-RACE-001",
                Lines =
                {
                    new ApiVoucherLine { ItemId = 1, LocationId = 1, Quantity = 2 }
                }
            });
        }

        var results = await Task.WhenAll(SendAsync(), SendAsync());

        await using var verify = new AppDbContext(options) { SkipAudit = true };
        Assert.Equal(1, await verify.Vouchers.CountAsync());
        Assert.Equal(1, await verify.VoucherDetails.CountAsync());
        Assert.Equal(1, await verify.IntegrationIdempotencyKeys.CountAsync());
        Assert.Contains(results, r => GetStatusCode(r) == StatusCodes.Status201Created);
        Assert.All(results, r => Assert.Contains(GetStatusCode(r), new[] { StatusCodes.Status201Created, StatusCodes.Status409Conflict }));
    }

    [Fact]
    public async Task ReplayWebhook_ShouldAllowInScopePayloadWhenApiScoped()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        db.WebhookDeliveries.Add(new WebhookDelivery
        {
            WebhookDeliveryId = 9010,
            WebhookSubscriptionId = 1,
            EventType = "InventoryChanged",
            IdempotencyKey = "webhook-in-scope",
            PayloadJson = "{\"warehouseId\":1,\"ownerPartnerId\":10,\"itemId\":1}",
            Signature = "signature",
            Status = WebhookDeliveryStatusEnum.Failed
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: 10);

        var result = await controller.ReplayWebhook(9010);

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = GetAnonValue(ok.Value, "data")!;
        Assert.Equal("InventoryChanged", GetString(data, "EventType"));
        Assert.Equal("Sent", GetString(data, "status"));
        Assert.Equal(2, await db.WebhookDeliveries.CountAsync());
    }

    [Fact]
    public async Task ReplayWebhook_ShouldRequireOnlyConfiguredScopeFields()
    {
        await using var db = CreateDb();
        SeedWarehouseOwnerFixture(db);
        db.WebhookDeliveries.AddRange(
            new WebhookDelivery
            {
                WebhookDeliveryId = 9020,
                WebhookSubscriptionId = 1,
                EventType = "InventoryChanged",
                IdempotencyKey = "webhook-warehouse-only-in-scope",
                PayloadJson = "{\"warehouseId\":1,\"itemId\":1}",
                Signature = "signature",
                Status = WebhookDeliveryStatusEnum.Failed
            },
            new WebhookDelivery
            {
                WebhookDeliveryId = 9021,
                WebhookSubscriptionId = 1,
                EventType = "InventoryChanged",
                IdempotencyKey = "webhook-warehouse-only-out-of-scope",
                PayloadJson = "{\"warehouseId\":2,\"itemId\":1}",
                Signature = "signature",
                Status = WebhookDeliveryStatusEnum.Failed
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, scopedWarehouseId: 1, scopedOwnerPartnerId: null);

        Assert.IsType<OkObjectResult>(await controller.ReplayWebhook(9020));
        AssertForbiddenScope(await controller.ReplayWebhook(9021));
    }

    private static ApiIntegrationController CreateController(
        AppDbContext db,
        int? scopedWarehouseId,
        int? scopedOwnerPartnerId,
        IIntegrationService? integrationService = null,
        bool includeApiKey = true)
    {
        const string apiKey = "unit-test-api-key";
        var values = new Dictionary<string, string?>
        {
            ["Api:Key"] = apiKey
        };
        if (scopedWarehouseId.HasValue)
            values["Api:ScopedWarehouseId"] = scopedWarehouseId.Value.ToString();
        if (scopedOwnerPartnerId.HasValue)
            values["Api:ScopedOwnerPartnerId"] = scopedOwnerPartnerId.Value.ToString();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var unitOfWork = new EfUnitOfWork(db);
        integrationService ??= new NullIntegrationService();
        var controller = new ApiIntegrationController(
            db,
            configuration,
            new InventoryBalanceService(db),
            new MheIntegrationService(db, integrationService, configuration, unitOfWork),
            new CarrierIntegrationService(db, integrationService, unitOfWork),
            integrationService,
            new EnterpriseIntegrationService(db, unitOfWork));

        var httpContext = new DefaultHttpContext();
        if (includeApiKey)
            httpContext.Request.Headers["X-API-Key"] = apiKey;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static async Task CreateApiVoucherSqliteSchemaAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE UnitsOfMeasure (
                UomId INTEGER NOT NULL CONSTRAINT PK_UnitsOfMeasure PRIMARY KEY,
                UomCode TEXT NOT NULL,
                UomName TEXT NOT NULL,
                UomGroup TEXT NULL,
                IsActive INTEGER NOT NULL
            );

            CREATE TABLE Warehouses (
                WarehouseId INTEGER NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
                WarehouseCode TEXT NOT NULL,
                WarehouseName TEXT NOT NULL,
                Address TEXT NULL,
                ManagerName TEXT NULL,
                ManagerUserId INTEGER NULL,
                Phone TEXT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE Partners (
                PartnerId INTEGER NOT NULL CONSTRAINT PK_Partners PRIMARY KEY,
                PartnerCode TEXT NOT NULL,
                PartnerName TEXT NOT NULL,
                PartnerType INTEGER NOT NULL,
                IsThreePlClient INTEGER NOT NULL,
                BillingAccountCode TEXT NULL,
                BillingCurrency TEXT NOT NULL,
                RequireOwnerScopeIsolation INTEGER NOT NULL,
                TaxCode TEXT NULL,
                Phone TEXT NULL,
                Email TEXT NULL,
                Address TEXT NULL,
                ContactPerson TEXT NULL,
                VendorRating INTEGER NOT NULL,
                LeadTimeDays INTEGER NULL,
                QcSamplePercent TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE Zones (
                ZoneId INTEGER NOT NULL CONSTRAINT PK_Zones PRIMARY KEY,
                WarehouseId INTEGER NOT NULL,
                ZoneCode TEXT NOT NULL,
                ZoneName TEXT NOT NULL,
                ZoneType INTEGER NOT NULL,
                IsActive INTEGER NOT NULL
            );

            CREATE TABLE Locations (
                LocationId INTEGER NOT NULL CONSTRAINT PK_Locations PRIMARY KEY,
                ZoneId INTEGER NOT NULL,
                LocationCode TEXT NOT NULL,
                AisleCode TEXT NULL,
                AisleSequence INTEGER NOT NULL,
                RackCode TEXT NULL,
                ShelfCode TEXT NULL,
                BinCode TEXT NULL,
                CurrentLoad TEXT NOT NULL DEFAULT '0',
                MaxCapacity TEXT NOT NULL DEFAULT '999999',
                MaxWeightCapacityKg TEXT NULL,
                HeightLevel INTEGER NOT NULL DEFAULT 1,
                IsGoldenZone INTEGER NOT NULL DEFAULT 0,
                AllowMechanicalHandling INTEGER NOT NULL DEFAULT 0,
                AllowMixedSku INTEGER NOT NULL DEFAULT 0,
                WeightLimitKg TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Barcode TEXT NULL
            );

            CREATE TABLE Items (
                ItemId INTEGER NOT NULL CONSTRAINT PK_Items PRIMARY KEY,
                ItemCode TEXT NOT NULL,
                ItemName TEXT NOT NULL,
                Barcode TEXT NULL,
                SkuCode TEXT NULL,
                CategoryId INTEGER NULL,
                OwnerPartnerId INTEGER NULL,
                ItemType INTEGER NOT NULL,
                BaseUomId INTEGER NOT NULL,
                CurrentStock TEXT NOT NULL,
                MinThreshold TEXT NOT NULL,
                MaxThreshold TEXT NULL,
                ReorderPoint TEXT NULL,
                Weight TEXT NULL,
                Length TEXT NULL,
                Width TEXT NULL,
                Height TEXT NULL,
                TrackExpiry INTEGER NOT NULL,
                TrackLot INTEGER NOT NULL,
                TrackSerial INTEGER NOT NULL,
                TrackCatchWeight INTEGER NOT NULL,
                CatchWeightUomId INTEGER NULL,
                NominalWeightPerBaseUnit TEXT NULL,
                CatchWeightTolerancePercent TEXT NULL,
                RequireCatchWeightAtReceive INTEGER NOT NULL,
                RequireCatchWeightAtPickPack INTEGER NOT NULL,
                UnitCost TEXT NOT NULL,
                LastCost TEXT NULL,
                TotalStockValue TEXT NOT NULL,
                RowVersion BLOB NOT NULL DEFAULT X'',
                ImageUrl TEXT NULL,
                Description TEXT NULL,
                Specifications TEXT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NULL,
                CreatedBy TEXT NULL,
                PutawayStrategy INTEGER NOT NULL,
                AllowedZoneTypes TEXT NULL,
                AbcClass TEXT NULL,
                DefaultLocationId INTEGER NULL
            );

            CREATE TABLE ItemLocations (
                ItemLocationId INTEGER NOT NULL CONSTRAINT PK_ItemLocations PRIMARY KEY,
                ItemId INTEGER NOT NULL,
                OwnerPartnerId INTEGER NULL,
                LocationId INTEGER NOT NULL,
                Quantity TEXT NOT NULL,
                ReservedQty TEXT NOT NULL,
                ExpiryDate TEXT NULL,
                MaxCapacity TEXT NULL,
                LotNumber TEXT NULL,
                TotalCapacity TEXT NULL,
                HoldStatus INTEGER NOT NULL,
                UpdatedAt TEXT NOT NULL,
                RowVersion BLOB NOT NULL DEFAULT X''
            );

            CREATE TABLE IntegrationIdempotencyKeys (
                KeyId INTEGER NOT NULL CONSTRAINT PK_IntegrationIdempotencyKeys PRIMARY KEY AUTOINCREMENT,
                KeyValue TEXT NOT NULL,
                OperationType TEXT NOT NULL,
                CachedResponse TEXT NULL,
                ResponseStatusCode INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IX_IntegrationIdempotencyKeys_KeyValue ON IntegrationIdempotencyKeys (KeyValue);

            CREATE TABLE WebhookSubscriptions (
                WebhookSubscriptionId INTEGER NOT NULL CONSTRAINT PK_WebhookSubscriptions PRIMARY KEY,
                SubscriptionCode TEXT NOT NULL,
                EventType TEXT NOT NULL,
                TargetUrl TEXT NOT NULL,
                SigningSecret TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                MaxRetries INTEGER NOT NULL,
                CreatedBy TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE Vouchers (
                VoucherId INTEGER NOT NULL CONSTRAINT PK_Vouchers PRIMARY KEY AUTOINCREMENT,
                VoucherCode TEXT NOT NULL,
                VoucherType INTEGER NOT NULL DEFAULT 0,
                VoucherDate TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                WarehouseId INTEGER NOT NULL DEFAULT 0,
                DestWarehouseId INTEGER NULL,
                PartnerId INTEGER NULL,
                OwnerPartnerId INTEGER NULL,
                SourceType INTEGER NOT NULL DEFAULT 1,
                ReferenceNo TEXT NULL,
                Description TEXT NULL,
                TotalAmount TEXT NOT NULL DEFAULT '0',
                CurrencyCode TEXT NOT NULL DEFAULT 'VND',
                TotalLines INTEGER NOT NULL DEFAULT 0,
                IsPosted INTEGER NOT NULL DEFAULT 0,
                IsCancelled INTEGER NOT NULL DEFAULT 0,
                CancelledBy TEXT NULL,
                CancelledAt TEXT NULL,
                CancelReason TEXT NULL,
                CancelReasonCode INTEGER NULL,
                CreatedBy TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt TEXT NULL,
                IpAddress TEXT NULL,
                AiOcrLogId INTEGER NULL,
                ParentVoucherId INTEGER NULL,
                WaveId INTEGER NULL,
                FulfillmentStatus INTEGER NOT NULL DEFAULT 0,
                ServiceLevel INTEGER NOT NULL DEFAULT 0,
                Priority INTEGER NOT NULL DEFAULT 50,
                PartialShipmentAllowed INTEGER NOT NULL DEFAULT 0,
                SlaCode TEXT NULL,
                SlaHours INTEGER NULL,
                RequestedDeliveryDate TEXT NULL,
                ReviewedBy TEXT NULL,
                ReviewedAt TEXT NULL,
                ReviewNote TEXT NULL,
                ReviewResult INTEGER NOT NULL DEFAULT 0,
                ResponsibilityScore TEXT NOT NULL DEFAULT '0',
                InboundStatus INTEGER NOT NULL DEFAULT 0,
                SubmittedBy TEXT NULL,
                SubmittedAt TEXT NULL,
                ApprovedBy TEXT NULL,
                ApprovedAt TEXT NULL,
                ReceivedBy TEXT NULL,
                ReceivedAt TEXT NULL,
                CompletedBy TEXT NULL,
                CompletedAt TEXT NULL,
                RejectionReason TEXT NULL,
                PackedBy TEXT NULL,
                PackedAt TEXT NULL,
                ShippedBy TEXT NULL,
                ShippedAt TEXT NULL,
                TrackingNumber TEXT NULL,
                ManifestCode TEXT NULL,
                AsnCode TEXT NULL,
                ExpectedArrivalAt TEXT NULL,
                CarrierName TEXT NULL,
                VehicleNumber TEXT NULL,
                DriverName TEXT NULL,
                DriverPhone TEXT NULL,
                DockAppointmentStart TEXT NULL,
                DockAppointmentEnd TEXT NULL,
                DockDoor TEXT NULL,
                DockStatus INTEGER NOT NULL DEFAULT 0,
                GateInAt TEXT NULL,
                DockArrivalAt TEXT NULL,
                UnloadStartAt TEXT NULL,
                UnloadEndAt TEXT NULL,
                DockCompletedAt TEXT NULL
            );

            CREATE TABLE VoucherDetails (
                VoucherDetailId INTEGER NOT NULL CONSTRAINT PK_VoucherDetails PRIMARY KEY AUTOINCREMENT,
                VoucherId INTEGER NOT NULL,
                ItemId INTEGER NOT NULL,
                OwnerPartnerId INTEGER NULL,
                LocationId INTEGER NULL,
                DestLocationId INTEGER NULL,
                TransactionQty TEXT NOT NULL DEFAULT '0',
                TransactionUomId INTEGER NOT NULL DEFAULT 0,
                PackagingUnitId INTEGER NULL,
                DefectQty TEXT NOT NULL DEFAULT '0',
                DefectBaseQty TEXT NOT NULL DEFAULT '0',
                ConversionRate TEXT NOT NULL DEFAULT '1',
                BaseQty TEXT NOT NULL DEFAULT '0',
                UnitPrice TEXT NOT NULL DEFAULT '0',
                LineAmount TEXT NOT NULL DEFAULT '0',
                QualityStatus INTEGER NOT NULL DEFAULT 0,
                ExpiryDate TEXT NULL,
                ManufacturingDate TEXT NULL,
                LotNumber TEXT NULL,
                Notes TEXT NULL,
                LineNumber INTEGER NOT NULL DEFAULT 0
            );
            """);
    }

    private static void SeedWarehouseOwnerFixture(AppDbContext db)
    {
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "EA", UomName = "Each" });
        db.Warehouses.AddRange(
            new Warehouse { WarehouseId = 1, WarehouseCode = "WH1", WarehouseName = "Warehouse 1" },
            new Warehouse { WarehouseId = 2, WarehouseCode = "WH2", WarehouseName = "Warehouse 2" });
        db.Zones.AddRange(
            new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "Z1", ZoneName = "Zone 1" },
            new Zone { ZoneId = 2, WarehouseId = 2, ZoneCode = "Z2", ZoneName = "Zone 2" });
        db.Locations.AddRange(
            new Location { LocationId = 1, ZoneId = 1, LocationCode = "WH1-A1" },
            new Location { LocationId = 2, ZoneId = 2, LocationCode = "WH2-A1" });
        db.Partners.AddRange(
            new Partner { PartnerId = 10, PartnerCode = "OWN-A", PartnerName = "Owner A", PartnerType = PartnerTypeEnum.Customer, IsActive = true },
            new Partner { PartnerId = 20, PartnerCode = "OWN-B", PartnerName = "Owner B", PartnerType = PartnerTypeEnum.Customer, IsActive = true });
        db.WebhookSubscriptions.Add(new WebhookSubscription
        {
            WebhookSubscriptionId = 1,
            SubscriptionCode = "INV",
            EventType = "InventoryChanged",
            TargetUrl = "mock://inventory",
            SigningSecret = "secret",
            IsActive = true
        });
        db.Items.Add(new Item
        {
            ItemId = 1,
            ItemCode = "ITEM-SCOPE",
            ItemName = "Scoped Item",
            BaseUomId = 1,
            OwnerPartnerId = 10,
            IsActive = true
        });
        db.Items.Add(new Item
        {
            ItemId = 2,
            ItemCode = "ITEM-INTERNAL",
            ItemName = "Internal Item",
            BaseUomId = 1,
            OwnerPartnerId = null,
            IsActive = true
        });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 1, ItemId = 1, LocationId = 1, OwnerPartnerId = 10, Quantity = 5, LotNumber = "LOT-A" },
            new ItemLocation { ItemLocationId = 2, ItemId = 1, LocationId = 1, OwnerPartnerId = 20, Quantity = 7, LotNumber = "LOT-A" },
            new ItemLocation { ItemLocationId = 3, ItemId = 1, LocationId = 1, OwnerPartnerId = null, Quantity = 11, LotNumber = "LOT-A" },
            new ItemLocation { ItemLocationId = 4, ItemId = 1, LocationId = 2, OwnerPartnerId = 10, Quantity = 13, LotNumber = "LOT-A" },
            new ItemLocation { ItemLocationId = 5, ItemId = 2, LocationId = 1, OwnerPartnerId = null, Quantity = 99, LotNumber = "LOT-INT" });
    }

    private static IReadOnlyList<object> GetDataRows(object envelope)
    {
        var data = GetAnonValue(envelope, "data");
        var rows = Assert.IsAssignableFrom<IEnumerable>(data);
        return rows.Cast<object>().ToList();
    }

    private static OperationExceptionCase NewExceptionCase(
        long id,
        string suffix,
        OperationExceptionStatusEnum status)
        => new()
        {
            OperationExceptionCaseId = id,
            ExceptionKey = "AUDIT_TEST_KPI_" + suffix,
            CategoryKey = "audit_test",
            CategoryLabel = "Kiểm thử KPI ngoại lệ",
            WarehouseId = 1,
            ReferenceCode = "AUDIT_TEST_" + suffix,
            Status = status,
            FirstDetectedAt = DateTime.Now.AddHours(-1),
            LastDetectedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

    private static object? GetAnonValue(object? source, string propertyName)
        => source?.GetType().GetProperty(propertyName)?.GetValue(source);

    private static decimal GetDecimal(object source, string propertyName)
        => Assert.IsType<decimal>(GetAnonValue(source, propertyName));

    private static int GetInt(object source, string propertyName)
        => Assert.IsType<int>(GetAnonValue(source, propertyName));

    private static string GetString(object source, string propertyName)
        => Assert.IsType<string>(GetAnonValue(source, propertyName));

    private static int GetStatusCode(IActionResult result)
        => Assert.IsType<ObjectResult>(result).StatusCode ?? StatusCodes.Status200OK;

    private static long GetCreatedVoucherId(ObjectResult result)
    {
        if (result.Value is JsonElement root)
        {
            return root.GetProperty("data").GetProperty("VoucherId").GetInt64();
        }

        var data = GetAnonValue(result.Value, "data")!;
        return Convert.ToInt64(GetAnonValue(data, "VoucherId"));
    }

    private static void AssertForbiddenScope(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.Equal("API_SCOPE_FORBIDDEN", GetAnonValue(objectResult.Value, "code"));
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

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class IdempotencyCheckGate
    {
        private readonly int _participants;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public IdempotencyCheckGate(int participants) => _participants = participants;

        public async Task SignalAndWaitAsync()
        {
            if (Interlocked.Increment(ref _arrivals) >= _participants)
            {
                _release.TrySetResult();
            }

            await _release.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class GatedDbIntegrationService : IIntegrationService
    {
        private readonly AppDbContext _db;
        private readonly IdempotencyCheckGate _gate;

        public GatedDbIntegrationService(AppDbContext db, IdempotencyCheckGate gate)
        {
            _db = db;
            _gate = gate;
        }

        public Task EnqueueAsync(OutboxEventTypeEnum eventType, string targetEndpoint, object payload, string? idempotencyKey = null, string? targetSystem = null)
            => Task.CompletedTask;

        public async Task<(bool IsDuplicate, string? CachedResponse, int StatusCode)> CheckIdempotencyAsync(string keyValue, string operationType)
        {
            await _gate.SignalAndWaitAsync();
            var key = await _db.IntegrationIdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.KeyValue == keyValue
                    && k.OperationType == operationType
                    && k.ExpiresAt > DateTime.Now);

            return key == null
                ? (false, null, 0)
                : (true, key.CachedResponse, key.ResponseStatusCode);
        }

        public async Task SetIdempotencyAsync(string keyValue, string operationType, string response, int statusCode)
        {
            var existing = await _db.IntegrationIdempotencyKeys
                .FirstOrDefaultAsync(k => k.KeyValue == keyValue && k.OperationType == operationType);
            if (existing == null)
            {
                _db.IntegrationIdempotencyKeys.Add(new IntegrationIdempotencyKey
                {
                    KeyValue = keyValue,
                    OperationType = operationType,
                    CachedResponse = response,
                    ResponseStatusCode = statusCode,
                    CreatedAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddHours(24)
                });
            }
            else
            {
                existing.CachedResponse = response;
                existing.ResponseStatusCode = statusCode;
                existing.CreatedAt = DateTime.Now;
                existing.ExpiresAt = DateTime.Now.AddHours(24);
            }

            await _db.SaveChangesAsync();
        }

        public Task ProcessOutboxBatchAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
