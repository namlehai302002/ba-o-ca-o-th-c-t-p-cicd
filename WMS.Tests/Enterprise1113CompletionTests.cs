using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using WMS.Authorization;
using WMS.Controllers;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using Xunit;

namespace WMS.Tests;

public sealed class Enterprise1113CompletionTests
{
    [Fact]
    public async Task BiPredictiveAuditAndAssistant_ShouldRespectScopeCitationAndMutationBlock()
    {
        await using var db = CreateDb();
        SeedEnterpriseAnalyticsData(db);
        var service = new EnterpriseAnalyticsService(db);
        var manager = Principal("manager", "Manager", warehouseId: 1, canSeeFinancial: true);

        var semantic = await service.BuildSemanticDashboardAsync(1, 30, canSeeFinancial: true);
        Assert.Contains(semantic.Definitions, x => x.MetricCode == "inventory.total_stock");
        Assert.Contains(semantic.Snapshots, x => x.MetricDefinition.MetricCode == "billing.total_cost" && x.MetricValue == 150000m);

        var financial = await service.BuildFinancialCostDashboardAsync(1, 30, canSeeFinancial: true);
        Assert.Equal(150000m, financial.TotalCost);
        Assert.Equal(1, financial.UnpricedLaborActivityCount);
        Assert.Contains(financial.Rows, x => x.SourceType == "Hóa đơn kho nhiều chủ hàng" && x.SourceCode == "INV-001");
        Assert.DoesNotContain(financial.Rows, x => x.SourceType == "Tác vụ lao động");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.BuildFinancialCostDashboardAsync(1, 30, canSeeFinancial: false));

        var alerts = await service.BuildPredictiveAlertsAsync(1);
        Assert.Contains(alerts.Alerts, x => x.AlertType == PredictiveAlertTypeEnum.StockoutRisk);
        Assert.Contains(alerts.Alerts, x => x.AlertType == PredictiveAlertTypeEnum.SlaDelay);
        Assert.Contains(alerts.Alerts, x => x.AlertType == PredictiveAlertTypeEnum.ExpiryRisk);

        var audit = await service.BuildAuditAnalyticsAsync();
        Assert.True(audit.SensitiveExportCount >= 1);
        Assert.True(audit.ScopeDeniedCount >= 1);
        Assert.True(audit.OutOfHoursCount >= 1);

        var answer = await service.AskAssistantAsync(manager, "Tóm tắt tồn kho và SLA", null);
        Assert.False(answer.IsMutationBlocked);
        Assert.NotEmpty(answer.Citations);
        var inventoryAnswer = await service.AskAssistantAsync(manager, "Rà soát giúp tôi xem tồn kho còn bao nhiêu", null);
        Assert.False(inventoryAnswer.IsMutationBlocked);
        Assert.Contains("Tổng tồn kho hiện tại", inventoryAnswer.Response);
        Assert.Contains("đơn vị tồn kho", inventoryAnswer.Response);
        Assert.Contains("Nguồn đối chiếu: Tồn kho theo vị trí", inventoryAnswer.Response);
        Assert.DoesNotContain("Tổng chi phí 3PL", inventoryAnswer.Response);
        Assert.DoesNotContain("Năng suất lao động", inventoryAnswer.Response);

        var alertAnswer = await service.AskAssistantAsync(manager, "Có bao nhiêu cảnh báo?", null);
        Assert.Contains("Tổng cảnh báo", alertAnswer.Response);
        Assert.Contains("Nguồn đối chiếu: Cảnh báo theo quy tắc", alertAnswer.Response);
        Assert.DoesNotContain("Tổng chi phí 3PL", alertAnswer.Response);
        Assert.DoesNotContain("Năng suất lao động", alertAnswer.Response);
        Assert.DoesNotContain("Tổng tồn kho hiện tại", alertAnswer.Response);

        var costAnswer = await service.AskAssistantAsync(manager, "chi phí của các bên/chủ hàng", null);
        Assert.Contains("Tổng chi phí trong kỳ", costAnswer.Response);
        Assert.Contains("Theo chủ hàng", costAnswer.Response);
        Assert.Contains("VND", costAnswer.Response);
        Assert.Contains("Nguồn đối chiếu: Dòng phí kho nhiều chủ hàng", costAnswer.Response);
        Assert.DoesNotContain("Tổng tồn kho hiện tại", costAnswer.Response);
        Assert.DoesNotContain("Năng suất lao động", costAnswer.Response);
        Assert.DoesNotContain("Phiếu trễ SLA", costAnswer.Response);

        var viewer = Principal("viewer", "Viewer", warehouseId: 1, canSeeFinancial: false);
        var deniedCostAnswer = await service.AskAssistantAsync(viewer, "chi phí của các bên", null);
        Assert.Contains("chưa có quyền xem số liệu chi phí", deniedCostAnswer.Response);
        Assert.DoesNotContain("VND", deniedCostAnswer.Response);
        Assert.Empty(deniedCostAnswer.Citations);

        var itemAnswer = await service.AskAssistantAsync(manager, "SKU-LOW còn bao nhiêu hàng trong kho?", null);
        Assert.Contains("Vật tư [SKU-LOW]", itemAnswer.Response);
        Assert.Contains("Pcs", itemAnswer.Response);
        Assert.Contains("Khả dụng", itemAnswer.Response);
        Assert.Contains("Tóm tắt", answer.Response);

        var blocked = await service.AskAssistantAsync(manager, "Xóa phiếu trễ SLA giúp tôi", answer.Session.AiAssistantSessionId);
        Assert.True(blocked.IsMutationBlocked);
        Assert.Contains("bị chặn", blocked.Response);
    }

    [Fact]
    public async Task VoucherCreateWorkflow_ShouldAlwaysExposeBaseUomForSelectableItems()
    {
        await using var db = CreateDb();
        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 10, UomCode = "Pcs", UomName = "Cái", UomGroup = "Count", IsActive = true },
            new UnitOfMeasure { UomId = 11, UomCode = "Box", UomName = "Thùng", UomGroup = "Count", IsActive = true });
        db.Items.Add(new Item { ItemId = 10, ItemCode = "SKU-UOM", ItemName = "Vật tư kiểm đơn vị", BaseUomId = 10, IsActive = true });
        db.UnitConversions.Add(new UnitConversion { ConversionId = 10, ItemId = 10, FromUomId = 11, ToUomId = 10, ConversionRate = 12, IsActive = true });
        await db.SaveChangesAsync();

        var service = new VoucherCreateWorkflowService(db);
        var json = await service.BuildItemAllowedSourceUomsJsonAsync(await db.Items.AsNoTracking().ToListAsync());
        var map = JsonSerializer.Deserialize<Dictionary<int, List<int>>>(json) ?? new();

        Assert.True(map.TryGetValue(10, out var allowed));
        Assert.Contains(10, allowed);
        Assert.Contains(11, allowed);
        Assert.DoesNotContain(0, allowed);
    }

    [Fact]
    public async Task RoleWorkflowAndSre_ShouldBuildRoleWorkspaceSaveWorkflowAndCaptureTelemetry()
    {
        await using var db = CreateDb();
        SeedEnterpriseAnalyticsData(db);

        var workspaceService = new RoleWorkspaceService();
        var adminWorkspace = workspaceService.Build(Principal("admin", "Admin", null, true));
        Assert.Equal("Admin", adminWorkspace.RoleKey);
        Assert.Equal("Quản trị viên", adminWorkspace.RoleLabel);
        Assert.Contains(adminWorkspace.QuickActions, x => x.Url == "/System/SreDashboard");
        Assert.Contains(adminWorkspace.QuickActions, x => x.Label == "Báo cáo dữ liệu");
        Assert.Contains(adminWorkspace.QuickActions, x => x.Label == "Giám sát hệ thống");
        Assert.Contains(adminWorkspace.QuickActions, x => x.Label == "Quy tắc vận hành");
        Assert.DoesNotContain(adminWorkspace.QuickActions, x => x.Label.Contains("BI semantic", StringComparison.OrdinalIgnoreCase) || x.Label.Contains("Workflow", StringComparison.OrdinalIgnoreCase) || x.Label.Equals("SRE", StringComparison.OrdinalIgnoreCase));

        var staffWorkspace = workspaceService.Build(Principal("staff", "Staff", 1, false));
        Assert.Equal("Staff", staffWorkspace.RoleKey);
        Assert.Equal("Nhân viên kho tổng hợp", staffWorkspace.RoleLabel);
        Assert.DoesNotContain(staffWorkspace.QuickActions, x => x.Url == "/Users");

        db.WarehouseWorkflowProfiles.Add(new WarehouseWorkflowProfile
        {
            WarehouseId = 1,
            ModuleKey = "picking",
            ProfileName = "Lấy hàng chuẩn",
            RequireLocationScan = true,
            RequireItemScan = true,
            RequireToteScan = true,
            RequirePacking = true,
            UpdatedBy = "manager"
        });
        await db.SaveChangesAsync();
        Assert.True(await db.WarehouseWorkflowProfiles.AnyAsync(x => x.ModuleKey == "picking" && x.RequireToteScan));

        db.RequestTelemetryLogs.Add(new RequestTelemetryLog
        {
            CorrelationId = "corr-expired",
            Method = "GET",
            Path = "/Reports/Old",
            StatusCode = 200,
            DurationMs = 80,
            CreatedAt = VietnamTime.Now.AddDays(-40)
        });
        await db.SaveChangesAsync();

        var sreConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductionSre:TelemetryRetentionDays"] = "14"
            })
            .Build();
        var sre = new ProductionSreService(db, sreConfig);
        await sre.RecordRequestAsync(new RequestTelemetryLog
        {
            CorrelationId = "corr-static",
            Method = "GET",
            Path = "/css/site.css",
            StatusCode = 200,
            DurationMs = 15,
            UserName = "manager",
            WarehouseId = 1
        });
        await sre.RecordRequestAsync(new RequestTelemetryLog
        {
            CorrelationId = "corr-health",
            Method = "GET",
            Path = "/health",
            StatusCode = 200,
            DurationMs = 20,
            UserName = "manager",
            WarehouseId = 1
        });
        await sre.RecordRequestAsync(new RequestTelemetryLog
        {
            CorrelationId = "corr-1113",
            Method = "GET",
            Path = "/Reports/SemanticBi",
            StatusCode = 200,
            DurationMs = 120,
            UserName = "manager",
            WarehouseId = 1
        });
        await sre.RecordRequestAsync(new RequestTelemetryLog
        {
            CorrelationId = "corr-1113-error",
            Method = "GET",
            Path = "/Reports/PredictiveAlerts",
            StatusCode = 500,
            DurationMs = 1900,
            IsError = true,
            UserName = "manager",
            WarehouseId = 1
        });
        db.IntegrationOutbox.Add(new IntegrationOutbox { EventType = "WebhookDelivery", TargetEndpoint = "mock://sre", TargetSystem = "Webhook", Status = OutboxStatusEnum.Pending, Payload = "{}" });
        await db.SaveChangesAsync();

        var dashboard = await sre.BuildDashboardAsync(60);
        Assert.Equal(2, dashboard.Snapshot.RequestCount);
        Assert.Equal(1, dashboard.Snapshot.ErrorCount);
        Assert.True(dashboard.Snapshot.QueueDepth >= 1);
        Assert.Contains(dashboard.RecentRequests, x => x.CorrelationId == "corr-1113");
        Assert.DoesNotContain(await db.RequestTelemetryLogs.AsNoTracking().ToListAsync(), x => x.CorrelationId is "corr-static" or "corr-health" or "corr-expired");
    }

    [Fact]
    public void CorrelationTelemetry_ShouldAlwaysKeepErrorsAndSlowRequestsBeforeSampling()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductionSre:TelemetrySamplingPercent"] = "0",
                ["ProductionSre:LatencyWarningMs"] = "1500"
            })
            .Build();

        Assert.False(CorrelationIdMiddlewareExtensions.ShouldRecordForTelemetry(200, 50, "/css/site.css", config));
        Assert.False(CorrelationIdMiddlewareExtensions.ShouldRecordForTelemetry(200, 50, "/Reports/InventoryInOutSummary", config));
        Assert.True(CorrelationIdMiddlewareExtensions.ShouldRecordForTelemetry(404, 50, "/Reports/Missing", config));
        Assert.True(CorrelationIdMiddlewareExtensions.ShouldRecordForTelemetry(500, 50, "/Reports/StockSnapshot", config));
        Assert.True(CorrelationIdMiddlewareExtensions.ShouldRecordForTelemetry(200, 2000, "/Reports/StockSnapshot", config));
    }

    [Fact]
    public void CorrelationTelemetry_ShouldSupportReadOnlySmokeWithoutPersistence()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductionSre:TelemetryPersistenceEnabled"] = "false",
                ["ProductionSre:TelemetrySamplingPercent"] = "100"
            })
            .Build();

        Assert.False(CorrelationIdMiddlewareExtensions.ShouldRecordForTelemetry(200, 10, "/Account/Login", config));
        Assert.False(CorrelationIdMiddlewareExtensions.ShouldRecordForTelemetry(500, 2000, "/Reports/StockSnapshot", config));
    }

    [Fact]
    public async Task PredictiveStockout_ShouldUseWarehouseScopedItemLocationsInsteadOfCurrentStock()
    {
        await using var db = CreateDb();
        db.Warehouses.AddRange(
            new Warehouse { WarehouseId = 1, WarehouseCode = "WH-A", WarehouseName = "Kho A", IsActive = true },
            new Warehouse { WarehouseId = 2, WarehouseCode = "WH-B", WarehouseName = "Kho B", IsActive = true });
        db.Zones.AddRange(
            new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "A", ZoneName = "A", IsActive = true },
            new Zone { ZoneId = 2, WarehouseId = 2, ZoneCode = "B", ZoneName = "B", IsActive = true });
        db.Locations.AddRange(
            new Location { LocationId = 1, ZoneId = 1, LocationCode = "A-01", IsActive = true, MaxCapacity = 100 },
            new Location { LocationId = 2, ZoneId = 2, LocationCode = "B-01", IsActive = true, MaxCapacity = 100 });
        db.Items.Add(new Item { ItemId = 10, ItemCode = "SKU-SCOPE", ItemName = "Scoped low stock", IsActive = true, CurrentStock = 999, MinThreshold = 5, UnitCost = 1 });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 10, ItemId = 10, LocationId = 1, Quantity = 2, ReservedQty = 0, HoldStatus = InventoryHoldStatusEnum.Available },
            new ItemLocation { ItemLocationId = 11, ItemId = 10, LocationId = 2, Quantity = 20, ReservedQty = 0, HoldStatus = InventoryHoldStatusEnum.Available });
        await db.SaveChangesAsync();

        var service = new EnterpriseAnalyticsService(db);

        var warehouseA = await service.BuildPredictiveAlertsAsync(1);
        var alert = Assert.Single(warehouseA.Alerts, x => x.AlertType == PredictiveAlertTypeEnum.StockoutRisk && x.Title.Contains("SKU-SCOPE", StringComparison.Ordinal));
        Assert.Equal(1, alert.WarehouseId);

        using var citation = JsonDocument.Parse(alert.CitationJson);
        Assert.Equal(2m, citation.RootElement.GetProperty("availableQty").GetDecimal());
        Assert.Equal(5m, citation.RootElement.GetProperty("minThreshold").GetDecimal());

        var warehouseB = await service.BuildPredictiveAlertsAsync(2);
        Assert.DoesNotContain(warehouseB.Alerts, x => x.AlertType == PredictiveAlertTypeEnum.StockoutRisk && x.Title.Contains("SKU-SCOPE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PredictiveStockout_ShouldSeparateOwnersAndIgnoreUnavailableHoldStock()
    {
        await using var db = CreateDb();
        db.Warehouses.Add(new Warehouse { WarehouseId = 1, WarehouseCode = "WH1", WarehouseName = "Kho 1", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "A", ZoneName = "A", IsActive = true });
        db.Locations.Add(new Location { LocationId = 1, ZoneId = 1, LocationCode = "A-01", IsActive = true, MaxCapacity = 100 });
        db.Partners.AddRange(
            new Partner { PartnerId = 10, PartnerCode = "OWN10", PartnerName = "Owner 10", IsActive = true, IsThreePlClient = true },
            new Partner { PartnerId = 20, PartnerCode = "OWN20", PartnerName = "Owner 20", IsActive = true, IsThreePlClient = true });
        db.Items.Add(new Item { ItemId = 20, ItemCode = "SKU-HOLD", ItemName = "Hold stock", IsActive = true, CurrentStock = 999, MinThreshold = 5, UnitCost = 1 });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 20, ItemId = 20, OwnerPartnerId = 10, LocationId = 1, Quantity = 6, ReservedQty = 0, HoldStatus = InventoryHoldStatusEnum.Consigned },
            new ItemLocation { ItemLocationId = 21, ItemId = 20, OwnerPartnerId = 20, LocationId = 1, Quantity = 100, ReservedQty = 0, HoldStatus = InventoryHoldStatusEnum.Blocked });
        await db.SaveChangesAsync();

        var alerts = await new EnterpriseAnalyticsService(db).BuildPredictiveAlertsAsync(1);
        Assert.DoesNotContain(alerts.Alerts, x => x.AlertType == PredictiveAlertTypeEnum.StockoutRisk && x.OwnerPartnerId == 10);

        var owner20 = Assert.Single(alerts.Alerts, x => x.AlertType == PredictiveAlertTypeEnum.StockoutRisk && x.OwnerPartnerId == 20);
        using var citation = JsonDocument.Parse(owner20.CitationJson);
        Assert.Equal(0m, citation.RootElement.GetProperty("availableQty").GetDecimal());
    }

    [Fact]
    public async Task PredictiveAlerts_ShouldEnforceOwnerScopeAndUseExpiryRowScope()
    {
        await using var db = CreateDb();
        db.Warehouses.Add(new Warehouse { WarehouseId = 1, WarehouseCode = "WH1", WarehouseName = "Kho 1", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "A", ZoneName = "A", IsActive = true });
        db.Locations.Add(new Location { LocationId = 1, ZoneId = 1, LocationCode = "A-01", IsActive = true, MaxCapacity = 1000 });
        db.Partners.AddRange(
            new Partner { PartnerId = 10, PartnerCode = "OWN10", PartnerName = "Owner 10", IsActive = true, IsThreePlClient = true },
            new Partner { PartnerId = 20, PartnerCode = "OWN20", PartnerName = "Owner 20", IsActive = true, IsThreePlClient = true });
        db.Items.AddRange(
            new Item { ItemId = 30, ItemCode = "EXP-10", ItemName = "Expiry owner 10", IsActive = true, MinThreshold = 1, UnitCost = 1 },
            new Item { ItemId = 31, ItemCode = "EXP-20", ItemName = "Expiry owner 20", IsActive = true, MinThreshold = 1, UnitCost = 1 });
        db.ItemLocations.AddRange(
            new ItemLocation { ItemLocationId = 30, ItemId = 30, OwnerPartnerId = 10, LocationId = 1, Quantity = 5, ExpiryDate = VietnamTime.Now.Date.AddDays(5) },
            new ItemLocation { ItemLocationId = 31, ItemId = 31, OwnerPartnerId = 20, LocationId = 1, Quantity = 5, ExpiryDate = VietnamTime.Now.Date.AddDays(6) });
        await db.SaveChangesAsync();

        var alerts = await new EnterpriseAnalyticsService(db).BuildPredictiveAlertsAsync(1, new[] { 10 });

        var expiry = Assert.Single(alerts.Alerts, alert => alert.AlertType == PredictiveAlertTypeEnum.ExpiryRisk);
        Assert.Equal(1, expiry.WarehouseId);
        Assert.Equal(10, expiry.OwnerPartnerId);
        Assert.Contains("EXP-10", expiry.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(alerts.Alerts, alert => alert.OwnerPartnerId == 20);
    }

    [Fact]
    public async Task PredictiveAlerts_ShouldRefreshResolveAndPreserveForeignOwnerScope()
    {
        await using var db = CreateDb();
        db.Warehouses.Add(new Warehouse { WarehouseId = 1, WarehouseCode = "WH1", WarehouseName = "Kho 1", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "A", ZoneName = "A", IsActive = true });
        db.Locations.Add(new Location { LocationId = 1, ZoneId = 1, LocationCode = "A-01", IsActive = true, MaxCapacity = 1000 });
        db.Items.Add(new Item { ItemId = 40, ItemCode = "LIFECYCLE-10", ItemName = "Lifecycle owner 10", IsActive = true, MinThreshold = 5, UnitCost = 1 });
        db.ItemLocations.Add(new ItemLocation
        {
            ItemLocationId = 40,
            ItemId = 40,
            OwnerPartnerId = 10,
            LocationId = 1,
            Quantity = 2,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        db.EnterprisePredictiveAlerts.AddRange(
            new EnterprisePredictiveAlert
            {
                EnterprisePredictiveAlertId = 40,
                AlertType = PredictiveAlertTypeEnum.StockoutRisk,
                Severity = EnterpriseSeverityEnum.Warning,
                WarehouseId = 1,
                OwnerPartnerId = 10,
                ReferenceType = "ItemStock",
                ReferenceId = "40:1:10",
                Title = "Stale title",
                Message = "Stale message",
                RiskScore = 1,
                ForecastFor = VietnamTime.Now.Date,
                CitationJson = "{}",
                Status = EnterpriseFindingStatusEnum.Open
            },
            new EnterprisePredictiveAlert
            {
                EnterprisePredictiveAlertId = 41,
                AlertType = PredictiveAlertTypeEnum.StockoutRisk,
                Severity = EnterpriseSeverityEnum.Critical,
                WarehouseId = 1,
                OwnerPartnerId = 20,
                ReferenceType = "ItemStock",
                ReferenceId = "999:1:20",
                Title = "Foreign owner alert",
                Message = "Must remain untouched",
                RiskScore = 99,
                ForecastFor = VietnamTime.Now.Date,
                CitationJson = "{}",
                Status = EnterpriseFindingStatusEnum.Open
            });
        await db.SaveChangesAsync();

        var service = new EnterpriseAnalyticsService(db);
        var active = await service.BuildPredictiveAlertsAsync(1, new[] { 10 });

        var refreshed = Assert.Single(active.Alerts, alert => alert.EnterprisePredictiveAlertId == 40);
        Assert.Equal(EnterpriseSeverityEnum.Critical, refreshed.Severity);
        Assert.Equal(95, refreshed.RiskScore);
        Assert.Contains("LIFECYCLE-10", refreshed.Title, StringComparison.Ordinal);
        Assert.Equal(2, await db.EnterprisePredictiveAlerts.CountAsync());

        var inventory = await db.ItemLocations.SingleAsync(x => x.ItemLocationId == 40);
        inventory.Quantity = 10;
        await db.SaveChangesAsync();

        var recovered = await service.BuildPredictiveAlertsAsync(1, new[] { 10 });

        Assert.DoesNotContain(recovered.Alerts, alert => alert.EnterprisePredictiveAlertId == 40);
        var resolved = await db.EnterprisePredictiveAlerts.SingleAsync(x => x.EnterprisePredictiveAlertId == 40);
        Assert.Equal(EnterpriseFindingStatusEnum.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
        var foreign = await db.EnterprisePredictiveAlerts.SingleAsync(x => x.EnterprisePredictiveAlertId == 41);
        Assert.Equal(EnterpriseFindingStatusEnum.Open, foreign.Status);
        Assert.Null(foreign.ResolvedAt);
    }

    [Fact]
    public async Task AuditAnalytics_ShouldLabelIqrMadOutlierAndResolveWhenSignalExpires()
    {
        await using var db = CreateDb();
        var now = VietnamTime.Now;
        long auditId = 1;
        foreach (var user in new[] { "normal-1", "normal-2", "normal-3", "normal-4" })
        {
            for (var index = 0; index < 5; index++)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    AuditLogId = auditId++,
                    TableName = "Inventory",
                    RecordId = $"{user}-{index}",
                    ActionType = "UPDATE",
                    ChangedBy = user,
                    ChangedAt = now.AddMinutes(-index)
                });
            }
        }
        for (var index = 0; index < 100; index++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogId = auditId++,
                TableName = "Inventory",
                RecordId = $"spike-{index}",
                ActionType = "UPDATE",
                ChangedBy = "spike-user",
                ChangedAt = now.AddSeconds(-index)
            });
        }
        await db.SaveChangesAsync();

        var service = new EnterpriseAnalyticsService(db);
        var active = await service.BuildAuditAnalyticsAsync();

        var finding = Assert.Single(active.Findings, row => row.FindingType == AuditFindingTypeEnum.AbnormalMutation);
        Assert.Equal("spike-user", finding.UserName);
        using (var evidence = JsonDocument.Parse(finding.EvidenceJson))
        {
            Assert.Equal("IQR+MAD", evidence.RootElement.GetProperty("method").GetString());
            Assert.Equal(5, evidence.RootElement.GetProperty("sampleSize").GetInt32());
            Assert.Equal(100, evidence.RootElement.GetProperty("count").GetInt32());
        }

        foreach (var row in db.AuditLogs.Where(row => row.ChangedBy == "spike-user"))
            row.ChangedAt = now.AddDays(-20);
        await db.SaveChangesAsync();

        var recovered = await service.BuildAuditAnalyticsAsync();

        Assert.Equal(0, recovered.AbnormalMutationCount);
        var resolved = await db.AuditAnalyticsFindings.SingleAsync(row => row.FindingType == AuditFindingTypeEnum.AbnormalMutation);
        Assert.Equal(EnterpriseFindingStatusEnum.Resolved, resolved.Status);
    }

    [Fact]
    public void Enterprise1113StaticArtifacts_ShouldExposeRoutesConfigDocsScaffoldsAndChecklist()
    {
        var root = FindRepositoryRoot();
        var reports = Read(Path.Combine(root, "Controllers", "ReportsController.Enterprise1113.cs"));
        var operations = Read(Path.Combine(root, "Controllers", "OperationsController.WorkflowProfiles.cs"));
        var system = Read(Path.Combine(root, "Controllers", "SystemController.cs"));
        var program = Read(Path.Combine(root, "Program.cs"));
        var appsettings = Read(Path.Combine(root, "appsettings.json"));
        var tasks = Read(Path.Combine(root, "FINAL_WMS_ENTERPRISE_QA_REPORT.md"));
        var visual = Read(Path.Combine(root, "tests", "visual", "wms-visual-regression.spec.ts"));
        var load = Read(Path.Combine(root, "tests", "load", "k6-wms-dod.js"));
        var migrationDoc = Read(Path.Combine(root, "PRODUCTION_MIGRATION_VALIDATION.md"));

        foreach (var token in new[] { "SemanticBi", "FinancialCostDashboard", "PredictiveAlerts", "AuditAnalytics", "AiAssistant", "AskAiAssistant" })
            Assert.Contains(token, reports, StringComparison.Ordinal);
        Assert.Contains("WorkflowProfiles", operations, StringComparison.Ordinal);
        Assert.Contains("SaveWorkflowProfile", operations, StringComparison.Ordinal);
        Assert.Contains("SreDashboard", system, StringComparison.Ordinal);
        Assert.Contains("ExportSreSnapshot", system, StringComparison.Ordinal);
        Assert.Contains("UseWmsCorrelationTelemetry", program, StringComparison.Ordinal);
        Assert.Contains("X-Correlation-ID", Read(Path.Combine(root, "Services", "Enterprise1113Services.cs")), StringComparison.Ordinal);

        foreach (var token in new[] { "AnalyticsGovernance", "RoleWorkspace", "ProductionSre", "TelemetrySamplingPercent" })
            Assert.Contains(token, appsettings, StringComparison.Ordinal);

        foreach (var code in new[] { "BI-01", "BI-02", "BI-03", "BI-04", "BI-05", "UX-01", "UX-02", "UX-03", "UX-04", "UX-05", "UX-06", "PROD-01", "PROD-02", "PROD-03", "PROD-04", "PROD-05", "PROD-06", "PROD-07" })
            Assert.Contains($"- [x] `{code}`", tasks, StringComparison.Ordinal);

        foreach (var token in new[] { "semantic-bi", "predictive-alerts", "ai-assistant", "workflow-profiles", "sre-dashboard" })
            Assert.Contains(token, visual, StringComparison.Ordinal);
        foreach (var token in new[] { "WMS_LOAD_PROFILE", "1000", "bi_sre_dashboards", "biSreDashboards" })
            Assert.Contains(token, load, StringComparison.Ordinal);
        foreach (var token in new[] { "Dry Run", "Rollback Plan", "Seed And Drift Validation", "dotnet ef migrations script --idempotent" })
            Assert.Contains(token, migrationDoc, StringComparison.Ordinal);

        foreach (var view in new[]
        {
            "SemanticBi.cshtml",
            "FinancialCostDashboard.cshtml",
            "PredictiveAlerts.cshtml",
            "AuditAnalytics.cshtml",
            "AiAssistant.cshtml"
        })
        {
            var content = Read(Path.Combine(root, "Views", "Reports", view));
            Assert.Contains("enterprise-section", content, StringComparison.Ordinal);
            Assert.Contains("empty-state", content, StringComparison.Ordinal);
        }

        var analyticsView = Read(Path.Combine(root, "Views", "Reports", "Analytics.cshtml"));
        var scheduledView = Read(Path.Combine(root, "Views", "Reports", "ScheduledReports.cshtml"));
        var siteJs = Read(Path.Combine(root, "wwwroot", "js", "site.js"));
        Assert.Contains("enterprise-section", analyticsView, StringComparison.Ordinal);
        Assert.Contains("data-wms-analytics-chart", analyticsView, StringComparison.Ordinal);
        Assert.DoesNotContain("new Chart(", analyticsView, StringComparison.Ordinal);
        Assert.Contains("window.openScheduledReportModal", siteJs, StringComparison.Ordinal);
        Assert.DoesNotContain("function openScheduledReportModal", scheduledView, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportRoutes_ShouldUseExplicitReportPoliciesBeyondRoleChecks()
    {
        foreach (var action in new[]
        {
            "WarehouseOverview",
            "OpsKpi",
            "ExpiryReport",
            "SlowMovingReport",
            "AbcAnalysis",
            "Analytics",
            "SpaceUtilization",
            "DockToStock",
            "StockMovement",
            "ExportStockMovement",
            "InventoryInOutSummary",
            "ExportInventoryInOutSummary",
            "InventoryTransactions",
            "ExportInventoryTransactions",
            "Inventory",
            "StockSnapshot",
            "GenerateStockSnapshot",
            "QuickAdjustFromSnapshot",
            "ExportStockSnapshot",
            "StockCount",
            "StockCountSaveDraft",
            "PeriodLocks",
            "SetPeriodLock",
            "ClearPeriodLock",
            "ScheduledReports",
            "AiAssistant",
            "AskAiAssistant",
            "SemanticBi",
            "PredictiveAlerts"
        })
        {
            AssertActionPolicy(action, WmsPermissions.ReportView);
        }

        foreach (var action in new[] { "StockValuation", "ExportStockValuation", "FinancialCostDashboard" })
            AssertActionPolicy(action, WmsPermissions.ReportViewFinancial);

        foreach (var action in new[] { "AuditTrail", "AuditAnalytics" })
            AssertActionPolicy(action, WmsPermissions.AuditTrailView);

        AssertActionPolicy("StockCountApproveDraft", WmsPermissions.StockCountApprove);
        AssertActionPolicy("StockCountUnlockApproved", WmsPermissions.StockCountUnlock);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("enterprise1113-" + Guid.NewGuid())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedEnterpriseAnalyticsData(AppDbContext db)
    {
        var now = VietnamTime.Now;
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UomId = 1, UomCode = "Pcs", UomName = "Cái", UomGroup = "Count", IsActive = true });
        db.Warehouses.Add(new Warehouse { WarehouseId = 1, WarehouseCode = "WH1", WarehouseName = "Kho 1", IsActive = true });
        db.Zones.Add(new Zone { ZoneId = 1, WarehouseId = 1, ZoneCode = "A", ZoneName = "Zone A", IsActive = true });
        db.Locations.Add(new Location { LocationId = 1, ZoneId = 1, LocationCode = "A-01", MaxCapacity = 100, IsActive = true });
        db.Partners.Add(new Partner { PartnerId = 1, PartnerCode = "OWN1", PartnerName = "Owner 1", IsActive = true, IsThreePlClient = true });
        db.Items.Add(new Item { ItemId = 1, ItemCode = "SKU-LOW", ItemName = "Low stock", BaseUomId = 1, IsActive = true, CurrentStock = 999, MinThreshold = 5, UnitCost = 1000 });
        db.ItemLocations.Add(new ItemLocation { ItemLocationId = 1, ItemId = 1, LocationId = 1, Quantity = 2, ReservedQty = 1, LotNumber = "L1", ExpiryDate = now.Date.AddDays(5) });
        db.Vouchers.Add(new Voucher
        {
            VoucherId = 1,
            VoucherCode = "PX-OVERDUE",
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = 1,
            OwnerPartnerId = 1,
            RequestedDeliveryDate = now.Date.AddDays(-1),
            VoucherDate = now.Date,
            CreatedBy = "manager",
            IsPosted = false,
            IsCancelled = false
        });
        db.ThreePlInvoices.Add(new ThreePlInvoice
        {
            ThreePlInvoiceId = 1,
            InvoiceCode = "INV-001",
            WarehouseId = 1,
            OwnerPartnerId = 1,
            PeriodFrom = now.Date.AddDays(-30),
            PeriodTo = now.Date,
            ApiPublicId = "api-inv-001",
            CreatedAt = now,
            TotalAmount = 150000
        });
        db.ThreePlInvoiceLines.Add(new ThreePlInvoiceLine
        {
            ThreePlInvoiceLineId = 1,
            ThreePlInvoiceId = 1,
            ChargeType = ThreePlChargeTypeEnum.Storage,
            Description = "Storage",
            Quantity = 3,
            UnitRate = 50000,
            TotalAmount = 150000
        });
        db.LaborActivities.Add(new LaborActivity
        {
            LaborActivityId = 1,
            ActivityCode = "LAB-001",
            WarehouseId = 1,
            UserName = "staff",
            TaskType = "Picking",
            TaskSourceType = "PickTask",
            StartedAt = now.AddHours(-1),
            ActualMinutes = 3,
            WorkQuantity = 1,
            ProductivityPercent = 120
        });
        db.AuditLogs.AddRange(
            new AuditLog { AuditLogId = 1, TableName = "Security", RecordId = "Export", ActionType = "EXPORT", ChangedBy = "manager", ChangedAt = now },
            new AuditLog { AuditLogId = 2, TableName = "Security", RecordId = "Scope", ActionType = "DENIED", ChangedBy = "staff", ChangedAt = now });
        db.LoginAuditLogs.Add(new LoginAuditLog { LoginAuditLogId = 1, UserName = "night.user", IsSuccess = true, Outcome = "LOGIN_OK", CreatedAt = now.Date.AddHours(23) });
        db.SaveChanges();
    }

    private static ClaimsPrincipal Principal(string name, string role, int? warehouseId, bool canSeeFinancial)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role)
        };
        if (warehouseId.HasValue) claims.Add(new Claim("WarehouseId", warehouseId.Value.ToString()));
        if (canSeeFinancial) claims.Add(new Claim(PermissionClaimTypes.Permission, WmsPermissions.ReportViewFinancial));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "WMS.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string Read(string path) => File.ReadAllText(path);

    private static void AssertActionPolicy(string actionName, string policy)
    {
        var methods = typeof(ReportsController)
            .GetMethods()
            .Where(method => method.Name == actionName)
            .ToList();
        Assert.NotEmpty(methods);
        Assert.Contains(methods, method => method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Any(attribute => attribute.Policy == policy));
    }
}
