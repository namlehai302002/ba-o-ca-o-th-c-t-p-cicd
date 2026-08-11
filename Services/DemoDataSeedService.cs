using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Services;

public enum DemoDataDomain
{
    ItInventory,
    MedicalInventory,
    EcommerceInventory
}

public sealed class DemoDataSeedResult
{
    public string DomainKey { get; init; } = "";
    public string DomainName { get; init; } = "";
    public int WarehouseId { get; init; }
    public int Warehouses { get; init; }
    public int Locations { get; init; }
    public int Items { get; init; }
    public int Vouchers { get; init; }
    public int StockRows { get; init; }
    public int QualityInspections { get; init; }
    public int StockCountSheets { get; init; }
    public int Reservations { get; init; }
}

public interface IDemoDataSeedService
{
    IReadOnlyList<DemoDataOptionViewModel> GetOptions();
    Task<DemoDataSeedResult> ApplyAsync(DemoDataDomain domain, string actor, CancellationToken ct = default);
}

public sealed class DemoDataSeedService : IDemoDataSeedService
{
    private static readonly SemaphoreSlim ApplyGate = new(1, 1);
    private readonly AppDbContext _db;

    public DemoDataSeedService(AppDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<DemoDataOptionViewModel> GetOptions() => new[]
    {
        new DemoDataOptionViewModel
        {
            Key = "it",
            Title = "Demo kho thiết bị IT",
            Subtitle = "Laptop, máy chiếu, thiết bị mạng, chuột, bàn phím, màn hình; nhấn mạnh serial, cấp phát, thu hồi và kiểm kê tài sản.",
            IconClass = "fas fa-laptop-code",
            AccentClass = "info",
            Highlights = new[] { "Serial thiết bị", "Cấp phát phòng ban", "QC laptop lỗi", "Kiểm kê thiếu chuột" }
        },
        new DemoDataOptionViewModel
        {
            Key = "medical",
            Title = "Demo kho vật tư y tế",
            Subtitle = "Khẩu trang, găng tay, bộ test, sát khuẩn, bông băng, kim tiêm, thuốc thông dụng; nhấn mạnh lô, hạn dùng và an toàn truy xuất.",
            IconClass = "fas fa-kit-medical",
            AccentClass = "success",
            Highlights = new[] { "Lot/HSD", "FEFO", "QC theo lô", "Cảnh báo gần hết hạn" }
        },
        new DemoDataOptionViewModel
        {
            Key = "ecommerce",
            Title = "Demo kho thương mại điện tử",
            Subtitle = "Tai nghe, sạc nhanh, cáp Type-C, ốp lưng, chuột gaming, bàn phím cơ; nhấn mạnh reservation, picking, packing và shipping.",
            IconClass = "fas fa-boxes-packing",
            AccentClass = "warning",
            Highlights = new[] { "3 đơn đồng thời", "Giữ chỗ tồn", "Wave/picking", "Bàn giao vận chuyển" }
        }
    };

    public static DemoDataDomain ParseDomainKey(string? key)
        => (key ?? "").Trim().ToLowerInvariant() switch
        {
            "it" or "it-inventory" => DemoDataDomain.ItInventory,
            "medical" or "medical-inventory" => DemoDataDomain.MedicalInventory,
            "ecommerce" or "ecom" or "ecommerce-inventory" => DemoDataDomain.EcommerceInventory,
            _ => throw new BusinessRuleException("Bộ dữ liệu demo không hợp lệ.", "DEMO_DOMAIN_INVALID", nameof(DemoDataDomain))
        };

    public async Task<DemoDataSeedResult> ApplyAsync(DemoDataDomain domain, string actor, CancellationToken ct = default)
    {
        actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();

        var acquired = await ApplyGate.WaitAsync(0, ct);
        if (!acquired)
        {
            throw new BusinessRuleException(
                "Hệ thống đang nạp dữ liệu demo khác, vui lòng đợi hoàn tất rồi thử lại.",
                "DEMO_SEED_IN_PROGRESS",
                nameof(DemoDataDomain));
        }

        var oldSkipAudit = _db.SkipAudit;
        _db.SkipAudit = true;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            var actorHadWarehouseScope = await ActorHasWarehouseScopeAsync(actor, ct);
            await ReleaseDemoScopedAuthorizationReferencesAsync(ct);

            var preservedWarehouseIds = await GetAuthScopedWarehouseIdsAsync(ct);
            var preservedZoneIds = await _db.UserZoneAssignments
                .AsNoTracking()
                .Select(x => x.ZoneId)
                .Distinct()
                .ToListAsync(ct);
            var preservedPartnerIds = await _db.AppUserOwnerScopes
                .AsNoTracking()
                .Select(x => x.OwnerPartnerId)
                .Distinct()
                .ToListAsync(ct);

            await ClearOperationalDataAsync(preservedWarehouseIds, preservedZoneIds, preservedPartnerIds, ct);

            var result = domain switch
            {
                DemoDataDomain.ItInventory => await SeedItInventoryAsync(actor, ct),
                DemoDataDomain.MedicalInventory => await SeedMedicalInventoryAsync(actor, ct),
                DemoDataDomain.EcommerceInventory => await SeedEcommerceInventoryAsync(actor, ct),
                _ => throw new BusinessRuleException("Bộ dữ liệu demo không hợp lệ.", "DEMO_DOMAIN_INVALID", nameof(DemoDataDomain))
            };

            await AlignActorWarehouseScopeAsync(actor, result.WarehouseId, actorHadWarehouseScope, ct);

            _db.AuditLogs.Add(new AuditLog
            {
                TableName = "DemoData",
                RecordId = result.DomainKey,
                ActionType = "APPLY_DEMO_DATA",
                NewValue = $"Đã nạp {result.DomainName}: {result.Items} vật tư, {result.StockRows} dòng tồn, {result.Vouchers} phiếu.",
                ChangedBy = actor,
                ChangedAt = VietnamTime.Now,
                AppModule = "DemoDataSeed"
            });
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            _db.SkipAudit = oldSkipAudit;
            ApplyGate.Release();
        }
    }

    private async Task<bool> ActorHasWarehouseScopeAsync(string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.Equals(actor, "system", StringComparison.OrdinalIgnoreCase))
            return false;

        return await _db.AppUsers
            .AnyAsync(x => (x.UserName == actor || x.Email == actor) && x.WarehouseId.HasValue, ct);
    }

    private async Task ReleaseDemoScopedAuthorizationReferencesAsync(CancellationToken ct)
    {
        var demoWarehouseIds = await _db.Warehouses
            .Where(x => x.WarehouseCode.StartsWith("DEMO-"))
            .Select(x => x.WarehouseId)
            .ToListAsync(ct);

        var demoPartnerIds = await _db.Partners
            .Where(x => x.PartnerCode.StartsWith("DEMO-"))
            .Select(x => x.PartnerId)
            .ToListAsync(ct);

        if (demoWarehouseIds.Count > 0)
        {
            var demoScopedUsers = await _db.AppUsers
                .Where(x => x.WarehouseId.HasValue && demoWarehouseIds.Contains(x.WarehouseId.Value))
                .ToListAsync(ct);

            foreach (var user in demoScopedUsers)
                user.WarehouseId = null;

            var demoZoneIds = await _db.Zones
                .Where(x => demoWarehouseIds.Contains(x.WarehouseId))
                .Select(x => x.ZoneId)
                .ToListAsync(ct);

            if (demoZoneIds.Count > 0)
                await _db.UserZoneAssignments.Where(x => demoZoneIds.Contains(x.ZoneId)).ExecuteDeleteAsync(ct);
        }

        if (demoPartnerIds.Count > 0)
            await _db.AppUserOwnerScopes.Where(x => demoPartnerIds.Contains(x.OwnerPartnerId)).ExecuteDeleteAsync(ct);

        await _db.SaveChangesAsync(ct);
    }

    private async Task AlignActorWarehouseScopeAsync(string actor, int warehouseId, bool actorHadWarehouseScope, CancellationToken ct)
    {
        if (!actorHadWarehouseScope || warehouseId <= 0 || string.IsNullOrWhiteSpace(actor) || string.Equals(actor, "system", StringComparison.OrdinalIgnoreCase))
            return;

        var actorUsers = await _db.AppUsers
            .Where(x => x.UserName == actor || x.Email == actor)
            .ToListAsync(ct);

        foreach (var user in actorUsers)
            user.WarehouseId = warehouseId;
    }

    private async Task<List<int>> GetAuthScopedWarehouseIdsAsync(CancellationToken ct)
    {
        var userWarehouseIds = await _db.AppUsers
            .AsNoTracking()
            .Where(x => x.WarehouseId.HasValue)
            .Select(x => x.WarehouseId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var zoneWarehouseIds = await _db.UserZoneAssignments
            .AsNoTracking()
            .Where(x => x.Zone != null)
            .Select(x => x.Zone!.WarehouseId)
            .Distinct()
            .ToListAsync(ct);

        return userWarehouseIds.Concat(zoneWarehouseIds).Distinct().ToList();
    }

    private async Task ClearOperationalDataAsync(
        IReadOnlyCollection<int> preservedWarehouseIds,
        IReadOnlyCollection<int> preservedZoneIds,
        IReadOnlyCollection<int> preservedPartnerIds,
        CancellationToken ct)
    {
        await _db.Vouchers.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.AiOcrLogId, (long?)null)
            .SetProperty(x => x.ParentVoucherId, (long?)null)
            .SetProperty(x => x.WaveId, (long?)null), ct);
        await _db.AiOcrLogs.ExecuteUpdateAsync(s => s.SetProperty(x => x.VoucherId, (long?)null), ct);
        await _db.OutboundPackages.ExecuteUpdateAsync(s => s.SetProperty(x => x.ShipmentLoadId, (long?)null), ct);
        await _db.SerialNumbers.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.ConsumedVoucherId, (long?)null)
            .SetProperty(x => x.ConsumedPickTaskId, (long?)null)
            .SetProperty(x => x.LicensePlateId, (long?)null), ct);
        await _db.LicensePlates.ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentLpnId, (long?)null), ct);

        await _db.LabelPrintJobLines.ExecuteDeleteAsync(ct);
        await _db.LabelPrintJobs.ExecuteDeleteAsync(ct);
        await _db.PartnerItemLabelRules.ExecuteDeleteAsync(ct);
        await _db.PartnerLabelTemplates.ExecuteDeleteAsync(ct);

        await _db.ThreePlDisputes.ExecuteDeleteAsync(ct);
        await _db.ThreePlInvoiceLines.ExecuteDeleteAsync(ct);
        await _db.ThreePlInvoices.ExecuteDeleteAsync(ct);
        await _db.ThreePlBillingCharges.ExecuteDeleteAsync(ct);
        await _db.ThreePlBillingRuns.ExecuteDeleteAsync(ct);
        await _db.ThreePlContractRates.ExecuteDeleteAsync(ct);
        await _db.ThreePlContracts.ExecuteDeleteAsync(ct);
        await _db.ThreePlBillingRates.ExecuteDeleteAsync(ct);

        await _db.WebhookDeliveries.ExecuteDeleteAsync(ct);
        await _db.WebhookSubscriptions.ExecuteDeleteAsync(ct);
        await _db.EnterpriseConnectorDeliveries.ExecuteDeleteAsync(ct);
        await _db.EnterpriseConnectors.ExecuteDeleteAsync(ct);
        await _db.EdiMessages.ExecuteDeleteAsync(ct);
        await _db.IntegrationOutbox.ExecuteDeleteAsync(ct);
        await _db.IntegrationIdempotencyKeys.ExecuteDeleteAsync(ct);

        await _db.MheTelemetryEvents.ExecuteDeleteAsync(ct);
        await _db.MheMissionEvents.ExecuteDeleteAsync(ct);
        await _db.MheCommands.ExecuteDeleteAsync(ct);
        await _db.WcsSimulatorRuns.ExecuteDeleteAsync(ct);
        await _db.AutomationOverrides.ExecuteDeleteAsync(ct);
        await _db.MheAdapterProfiles.ExecuteDeleteAsync(ct);
        await _db.MheSystems.ExecuteDeleteAsync(ct);

        await _db.YardVisitEvidence.ExecuteDeleteAsync(ct);
        await _db.YardBillingCharges.ExecuteDeleteAsync(ct);
        await _db.YardVisits.ExecuteDeleteAsync(ct);
        await _db.Trailers.ExecuteDeleteAsync(ct);
        await _db.YardSpots.ExecuteDeleteAsync(ct);
        await _db.YardBillingRates.ExecuteDeleteAsync(ct);

        await _db.ShipmentLoadPackages.ExecuteDeleteAsync(ct);
        await _db.ShipmentLoadVouchers.ExecuteDeleteAsync(ct);
        await _db.ShippingHandoverLogs.ExecuteDeleteAsync(ct);
        await _db.CarrierShipmentEvents.ExecuteDeleteAsync(ct);
        await _db.CarrierShipments.ExecuteDeleteAsync(ct);
        await _db.CarrierConnectors.ExecuteDeleteAsync(ct);
        await _db.DockAppointments.ExecuteDeleteAsync(ct);
        await _db.ShipmentLoads.ExecuteDeleteAsync(ct);

        await _db.CatchWeightEntries.ExecuteDeleteAsync(ct);
        await _db.PickTaskSerialAssignments.ExecuteDeleteAsync(ct);
        await _db.PickTaskScanLogs.ExecuteDeleteAsync(ct);
        await _db.PickTaskAllocations.ExecuteDeleteAsync(ct);
        await _db.SerialReservations.ExecuteDeleteAsync(ct);
        await _db.SerialInventoryOperations.ExecuteDeleteAsync(ct);
        await _db.StockReservations.ExecuteDeleteAsync(ct);
        await _db.PickTasks.ExecuteDeleteAsync(ct);
        await _db.WaveLines.ExecuteDeleteAsync(ct);
        await _db.Waves.ExecuteDeleteAsync(ct);
        await _db.LicensePlateDetails.ExecuteDeleteAsync(ct);
        await _db.LicensePlates.ExecuteDeleteAsync(ct);
        await _db.SerialNumbers.ExecuteDeleteAsync(ct);
        await _db.OutboundPackages.ExecuteDeleteAsync(ct);

        await _db.QualityInspections.ExecuteDeleteAsync(ct);
        await _db.CycleCountSchedules.ExecuteDeleteAsync(ct);
        await _db.CycleCountPrograms.ExecuteDeleteAsync(ct);
        await _db.RecallLines.ExecuteDeleteAsync(ct);
        await _db.RecallCases.ExecuteDeleteAsync(ct);

        if (await TableExistsAsync("CycleCountRecommendationDecisions", ct))
            await _db.CycleCountRecommendationDecisions.ExecuteDeleteAsync(ct);
        if (await TableExistsAsync("CycleCountRecommendations", ct))
            await _db.CycleCountRecommendations.ExecuteDeleteAsync(ct);
        if (await TableExistsAsync("InventoryRiskPredictions", ct))
            await _db.InventoryRiskPredictions.ExecuteDeleteAsync(ct);
        if (await TableExistsAsync("InventoryRiskFeatureSnapshots", ct))
            await _db.InventoryRiskFeatureSnapshots.ExecuteDeleteAsync(ct);
        if (await TableExistsAsync("InventoryRiskModelVersions", ct))
            await _db.InventoryRiskModelVersions.ExecuteDeleteAsync(ct);

        await _db.StockCountLines.ExecuteDeleteAsync(ct);
        await _db.StockCountSheets.ExecuteDeleteAsync(ct);
        await _db.StockSnapshots.ExecuteDeleteAsync(ct);
        await _db.StockSnapshotRuns.ExecuteDeleteAsync(ct);
        await _db.StockAlerts.ExecuteDeleteAsync(ct);
        await _db.WarehousePeriodLocks.ExecuteDeleteAsync(ct);
        await _db.InventoryTransactions.ExecuteDeleteAsync(ct);
        await _db.InventoryReconciliationIssues.ExecuteDeleteAsync(ct);
        await _db.InventoryReconciliationRuns.ExecuteDeleteAsync(ct);
        await _db.InventorySnapshotOutbox.ExecuteDeleteAsync(ct);

        await _db.CrossDockTasks.ExecuteDeleteAsync(ct);
        await _db.MovementTasks.ExecuteDeleteAsync(ct);
        await _db.ReplenishmentAutomationLines.ExecuteDeleteAsync(ct);
        await _db.ReplenishmentAutomationRuns.ExecuteDeleteAsync(ct);
        await _db.KittingWorkOrderLines.ExecuteDeleteAsync(ct);
        await _db.KittingWorkOrders.ExecuteDeleteAsync(ct);
        await _db.VasMaterialLines.ExecuteDeleteAsync(ct);
        await _db.VasOperations.ExecuteDeleteAsync(ct);
        await _db.VasWorkOrders.ExecuteDeleteAsync(ct);

        await _db.LaborExceptionReviews.ExecuteDeleteAsync(ct);
        await _db.LaborActivities.ExecuteDeleteAsync(ct);
        await _db.LaborActivityStandards.ExecuteDeleteAsync(ct);
        await _db.LaborStandards.ExecuteDeleteAsync(ct);
        await _db.SlaMetrics.ExecuteDeleteAsync(ct);
        await _db.ItemVelocityClassifications.ExecuteDeleteAsync(ct);
        await _db.CapacityScenarios.ExecuteDeleteAsync(ct);
        await _db.SlottingSimulationLines.ExecuteDeleteAsync(ct);
        await _db.SlottingSimulationScenarios.ExecuteDeleteAsync(ct);
        await _db.OptimizationRecommendationLines.ExecuteDeleteAsync(ct);
        await _db.OptimizationRuns.ExecuteDeleteAsync(ct);
        await _db.WavelessReleaseQueue.ExecuteDeleteAsync(ct);
        await _db.PickPathPlanStops.ExecuteDeleteAsync(ct);
        await _db.PickPathPlans.ExecuteDeleteAsync(ct);
        await _db.ToteClusterAssignments.ExecuteDeleteAsync(ct);
        await _db.ToteClusterPlans.ExecuteDeleteAsync(ct);
        await _db.PickTotes.ExecuteDeleteAsync(ct);
        await _db.PickCarts.ExecuteDeleteAsync(ct);

        await _db.SemanticMetricSnapshots.ExecuteDeleteAsync(ct);
        await _db.SemanticMetricDefinitions.ExecuteDeleteAsync(ct);
        await _db.EnterprisePredictiveAlerts.ExecuteDeleteAsync(ct);
        await _db.AuditAnalyticsFindings.ExecuteDeleteAsync(ct);
        await _db.AiAssistantCitations.ExecuteDeleteAsync(ct);
        await _db.AiAssistantMessages.ExecuteDeleteAsync(ct);
        await _db.AiAssistantSessions.ExecuteDeleteAsync(ct);
        await _db.RequestTelemetryLogs.ExecuteDeleteAsync(ct);
        await _db.SreMetricSnapshots.ExecuteDeleteAsync(ct);
        await _db.ScheduledReports.ExecuteDeleteAsync(ct);
        await _db.WarehouseWorkflowProfiles.ExecuteDeleteAsync(ct);
        await _db.OperationExceptionCases.ExecuteDeleteAsync(ct);

        await _db.AiOcrAdjustments.ExecuteDeleteAsync(ct);
        await _db.AiOcrLogs.ExecuteDeleteAsync(ct);
        await _db.VoucherDetails.ExecuteDeleteAsync(ct);
        await _db.Vouchers.ExecuteDeleteAsync(ct);
        await _db.ItemLocations.ExecuteDeleteAsync(ct);
        await _db.UnitConversions.ExecuteDeleteAsync(ct);
        await _db.BillOfMaterials.ExecuteDeleteAsync(ct);
        await _db.Items.ExecuteDeleteAsync(ct);
        await _db.PackagingUnits.ExecuteDeleteAsync(ct);
        await _db.InspectionPlanTemplates.ExecuteDeleteAsync(ct);
        await _db.ItemCategories.ExecuteDeleteAsync(ct);

        if (preservedZoneIds.Count == 0)
            await _db.Locations.ExecuteDeleteAsync(ct);
        else
            await _db.Locations.Where(x => !preservedZoneIds.Contains(x.ZoneId)).ExecuteDeleteAsync(ct);

        await _db.WarehouseSortationConfigs.ExecuteDeleteAsync(ct);
        await _db.WarehouseOrderStreamingConfigs.ExecuteDeleteAsync(ct);
        await _db.DockDoorCapacities.ExecuteDeleteAsync(ct);

        if (preservedZoneIds.Count == 0)
            await _db.Zones.ExecuteDeleteAsync(ct);
        else
            await _db.Zones.Where(x => !preservedZoneIds.Contains(x.ZoneId)).ExecuteDeleteAsync(ct);

        if (preservedWarehouseIds.Count == 0)
            await _db.Warehouses.ExecuteDeleteAsync(ct);
        else
            await _db.Warehouses.Where(x => !preservedWarehouseIds.Contains(x.WarehouseId)).ExecuteDeleteAsync(ct);

        if (preservedPartnerIds.Count == 0)
            await _db.Partners.ExecuteDeleteAsync(ct);
        else
            await _db.Partners.Where(x => !preservedPartnerIds.Contains(x.PartnerId)).ExecuteDeleteAsync(ct);

        await _db.UnitsOfMeasure.ExecuteDeleteAsync(ct);
        await _db.CurrencyRates.ExecuteDeleteAsync(ct);
        await _db.AuditLogs.ExecuteDeleteAsync(ct);
    }

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
            return true;

        var provider = _db.Database.ProviderName ?? "";
        if (!provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
            && !provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return false;

        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            var parameter = command.CreateParameter();
            parameter.ParameterName = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? "@qualifiedName"
                : "$tableName";
            parameter.Value = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? $"[{tableName}]"
                : tableName;
            command.Parameters.Add(parameter);
            command.CommandText = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? "SELECT CASE WHEN OBJECT_ID(@qualifiedName, N'U') IS NULL THEN 0 ELSE 1 END"
                : "SELECT CASE WHEN EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $tableName) THEN 1 ELSE 0 END";

            var result = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task<DemoDataSeedResult> SeedItInventoryAsync(string actor, CancellationToken ct)
    {
        var now = VietnamTime.Now;
        var today = VietnamTime.Today;
        var uom = await AddUomsAsync(new[]
        {
            ("CAI", "Cái", "Số lượng"),
            ("BO", "Bộ", "Số lượng"),
            ("CHIEC", "Chiếc", "Số lượng"),
            ("THUNG", "Thùng", "Đóng gói")
        }, ct);

        var categories = AddCategories(new[]
        {
            ("DEMO-IT-LAPTOP", "Thiết bị máy tính"),
            ("DEMO-IT-NETWORK", "Thiết bị mạng"),
            ("DEMO-IT-PERIPHERAL", "Phụ kiện IT"),
            ("DEMO-IT-PRESENTATION", "Thiết bị trình chiếu")
        });

        var warehouse = new Warehouse
        {
            WarehouseCode = "DEMO-IT-KHO",
            WarehouseName = "Kho thiết bị IT",
            Address = "Tầng 1 - Tòa nhà điều hành",
            ManagerName = "Trần Minh Khôi",
            Phone = "02873008888",
            IsActive = true,
            CreatedAt = now
        };
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(ct);

        var receiving = AddZone(warehouse, "DEMO-IT-RCV", "Khu tiếp nhận thiết bị", ZoneTypeEnum.Receiving);
        var storage = AddZone(warehouse, "DEMO-IT-STO", "Phòng thiết bị", ZoneTypeEnum.Storage);
        var network = AddZone(warehouse, "DEMO-IT-NET", "Tủ thiết bị mạng", ZoneTypeEnum.Storage);
        var qc = AddZone(warehouse, "DEMO-IT-QC", "Khu QC thiết bị", ZoneTypeEnum.Staging);
        var shipping = AddZone(warehouse, "DEMO-IT-ISS", "Khu cấp phát phòng ban", ZoneTypeEnum.Shipping);
        await _db.SaveChangesAsync(ct);

        var loc = AddLocations(new[]
        {
            (receiving, "DEMO-IT-DOCK-01", "RCV", "01", "01", "01"),
            (storage, "DEMO-IT-A01-01", "A", "01", "01", "01"),
            (storage, "DEMO-IT-A01-02", "A", "01", "01", "02"),
            (storage, "DEMO-IT-A01-03", "A", "01", "01", "03"),
            (storage, "DEMO-IT-B01-01", "B", "01", "01", "01"),
            (storage, "DEMO-IT-B01-02", "B", "01", "01", "02"),
            (storage, "DEMO-IT-B01-03", "B", "01", "01", "03"),
            (network, "DEMO-IT-NET-CAB-01", "NET", "CAB", "01", "01"),
            (network, "DEMO-IT-NET-CAB-02", "NET", "CAB", "01", "02"),
            (qc, "DEMO-IT-QC-01", "QC", "01", "01", "01"),
            (shipping, "DEMO-IT-LAB-STAGE", "LAB", "01", "01", "01")
        });
        await _db.SaveChangesAsync(ct);

        var partners = AddPartners(new[]
        {
            ("DEMO-IT-SUP-DELL", "Công ty TNHH Dell Technologies Việt Nam", PartnerTypeEnum.Supplier, "Nguyễn Phương Linh"),
            ("DEMO-IT-SUP-EPS", "Epson Việt Nam - Thiết bị trình chiếu", PartnerTypeEnum.Supplier, "Hoàng Gia Bảo"),
            ("DEMO-IT-SUP-NET", "Nhà phân phối thiết bị mạng An Phát", PartnerTypeEnum.Supplier, "Đặng Quốc Huy"),
            ("DEMO-IT-DEPT-HC", "Phòng Hành chính", PartnerTypeEnum.Customer, "Lê Thanh Mai"),
            ("DEMO-IT-DEPT-LAB", "Phòng Lab Công nghệ", PartnerTypeEnum.Customer, "Phạm Quốc Đạt")
        });
        await _db.SaveChangesAsync(ct);

        var items = new List<Item>
        {
            Item("DEMO-IT-LAP-DELL-5420", "Laptop Dell Latitude 5420 i5/16GB/512GB", "IT-LAP-DELL-5420", categories["DEMO-IT-LAPTOP"], uom["CHIEC"], 10, 3, 18500000, true, false, false, loc["DEMO-IT-A01-01"], "Máy tính cấp phát cho nhân viên và phòng lab."),
            Item("DEMO-IT-LAP-HP-440G9", "Laptop HP ProBook 440 G9 i5/8GB/256GB", "IT-LAP-HP-440G9", categories["DEMO-IT-LAPTOP"], uom["CHIEC"], 6, 2, 16200000, true, false, false, loc["DEMO-IT-A01-02"], "Máy tính dự phòng cho khối văn phòng."),
            Item("DEMO-IT-PROJ-EPSON-X49", "Máy chiếu Epson EB-X49", "IT-PROJ-EPSON-X49", categories["DEMO-IT-PRESENTATION"], uom["CHIEC"], 4, 1, 12900000, true, false, false, loc["DEMO-IT-B01-01"], "Thiết bị trình chiếu cho phòng họp và lớp học."),
            Item("DEMO-IT-RT-TPLINK-AX55", "Router TP-Link Archer AX55", "IT-RT-TPLINK-AX55", categories["DEMO-IT-NETWORK"], uom["CHIEC"], 12, 3, 2450000, true, false, false, loc["DEMO-IT-NET-CAB-01"], "Router Wi-Fi 6 cho khu văn phòng."),
            Item("DEMO-IT-SW-CISCO-24P", "Switch Cisco CBS250 24-Port", "IT-SW-CISCO-24P", categories["DEMO-IT-NETWORK"], uom["CHIEC"], 5, 2, 7800000, true, false, false, loc["DEMO-IT-NET-CAB-02"], "Switch quản lý cho phòng lab."),
            Item("DEMO-IT-MOUSE-M185", "Chuột Logitech M185 Wireless", "IT-MOUSE-M185", categories["DEMO-IT-PERIPHERAL"], uom["CAI"], 49, 15, 210000, false, false, false, loc["DEMO-IT-B01-02"], "Chuột không dây cấp phát theo yêu cầu."),
            Item("DEMO-IT-KBD-DELL-KB216", "Bàn phím Dell KB216", "IT-KBD-DELL-KB216", categories["DEMO-IT-PERIPHERAL"], uom["CAI"], 30, 10, 190000, false, false, false, loc["DEMO-IT-B01-03"], "Bàn phím có dây tiêu chuẩn."),
            Item("DEMO-IT-MON-SAMSUNG-24", "Màn hình Samsung 24 inch IPS", "IT-MON-SAMSUNG-24", categories["DEMO-IT-PERIPHERAL"], uom["CHIEC"], 16, 4, 2950000, true, false, false, loc["DEMO-IT-A01-03"], "Màn hình làm việc cho nhân sự mới.")
        };
        _db.Items.AddRange(items);
        await _db.SaveChangesAsync(ct);

        AddStock(items[0], loc["DEMO-IT-A01-01"], 9);
        AddStock(items[0], loc["DEMO-IT-QC-01"], 1, hold: InventoryHoldStatusEnum.QcHold);
        AddStock(items[1], loc["DEMO-IT-A01-02"], 6);
        AddStock(items[2], loc["DEMO-IT-B01-01"], 4);
        AddStock(items[3], loc["DEMO-IT-NET-CAB-01"], 12);
        AddStock(items[4], loc["DEMO-IT-NET-CAB-02"], 5);
        AddStock(items[5], loc["DEMO-IT-B01-02"], 49);
        AddStock(items[6], loc["DEMO-IT-B01-03"], 30);
        AddStock(items[7], loc["DEMO-IT-A01-03"], 16);
        await _db.SaveChangesAsync(ct);

        var inbound = AddVoucher("PN-IT-20260609-0001", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-IT-SUP-DELL"], "HD-IT-2026-042", "Nhập thiết bị phục vụ phòng lab và nhân sự mới", actor, isPosted: true);
        AddVoucherLine(inbound, items[0], loc["DEMO-IT-A01-01"], 10, uom["CHIEC"], 18500000, line: 1);
        AddVoucherLine(inbound, items[5], loc["DEMO-IT-B01-02"], 50, uom["CAI"], 210000, line: 2);
        AddVoucherLine(inbound, items[6], loc["DEMO-IT-B01-03"], 30, uom["CAI"], 190000, line: 3);

        var projectionInbound = AddVoucher("PN-IT-20260609-0002", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-IT-SUP-EPS"], "HD-IT-2026-043", "Nhập máy chiếu cho phòng họp và lớp học", actor, isPosted: true);
        AddVoucherLine(projectionInbound, items[2], loc["DEMO-IT-B01-01"], 4, uom["CHIEC"], 12900000, line: 1);

        var infrastructureInbound = AddVoucher("PN-IT-20260609-0003", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-IT-SUP-NET"], "HD-IT-2026-044", "Nhập thiết bị hạ tầng IT và màn hình làm việc", actor, isPosted: true);
        AddVoucherLine(infrastructureInbound, items[1], loc["DEMO-IT-A01-02"], 6, uom["CHIEC"], 16200000, line: 1);
        AddVoucherLine(infrastructureInbound, items[3], loc["DEMO-IT-NET-CAB-01"], 12, uom["CHIEC"], 2450000, line: 2);
        AddVoucherLine(infrastructureInbound, items[4], loc["DEMO-IT-NET-CAB-02"], 5, uom["CHIEC"], 7800000, line: 3);
        AddVoucherLine(infrastructureInbound, items[7], loc["DEMO-IT-A01-03"], 16, uom["CHIEC"], 2950000, line: 4);

        var scheduledInbound = AddVoucher("PN-IT-20260609-0004", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-IT-SUP-DELL"], "HD-IT-2026-101", "Lô laptop và phụ kiện đang chờ nhận tại cửa kho cho đợt cấp phát nhân sự tháng 06", actor, isPosted: false);
        scheduledInbound.InboundStatus = InboundStatusEnum.Approved;
        scheduledInbound.ReceivedBy = null;
        scheduledInbound.ReceivedAt = null;
        scheduledInbound.ReviewedBy = null;
        scheduledInbound.ReviewedAt = null;
        scheduledInbound.ReviewResult = ReviewResultEnum.Pending;
        scheduledInbound.ReviewNote = null;
        scheduledInbound.CompletedBy = null;
        scheduledInbound.CompletedAt = null;
        scheduledInbound.AsnCode = "ASN-IT-20260609-0004";
        scheduledInbound.ExpectedArrivalAt = today.AddHours(10);
        scheduledInbound.DockAppointmentStart = today.AddHours(10);
        scheduledInbound.DockAppointmentEnd = today.AddHours(11);
        scheduledInbound.DockDoor = "DOCK-01";
        scheduledInbound.CarrierName = "Vận tải Minh Long";
        scheduledInbound.VehicleNumber = "51D-862.10";
        scheduledInbound.DriverName = "Lê Gia Hân";
        scheduledInbound.DriverPhone = "0363 636 363";
        AddVoucherLine(scheduledInbound, items[0], loc["DEMO-IT-A01-01"], 8, uom["CHIEC"], 18500000, line: 1);
        AddVoucherLine(scheduledInbound, items[5], loc["DEMO-IT-B01-02"], 40, uom["CAI"], 210000, line: 2);

        var outbound = AddVoucher("PX-IT-20260609-0001", VoucherTypeEnum.XuatKho, warehouse, partners["DEMO-IT-DEPT-LAB"], "YC-LAB-2026-018", "Cấp phát thiết bị cho phòng lab công nghệ", actor, isPosted: true);
        AddVoucherLine(outbound, items[0], loc["DEMO-IT-A01-01"], 2, uom["CHIEC"], 18500000, line: 1);
        AddVoucherLine(outbound, items[5], loc["DEMO-IT-B01-02"], 8, uom["CAI"], 210000, line: 2);
        AddVoucherLine(outbound, items[6], loc["DEMO-IT-B01-03"], 8, uom["CAI"], 190000, line: 3);
        await _db.SaveChangesAsync(ct);

        AddQualityInspection(inbound, items[0], warehouse, 10, 1, 9, 1, "Laptop bị lỗi màn hình sau kiểm tra ngoại quan", "QC thiết bị IT", inspectorName: "Trần Minh Khôi");
        AddStockCount(warehouse, actor, "Kiểm kê nhanh kho IT: thiếu 1 chuột Logitech so với sổ.", items[5], loc["DEMO-IT-B01-02"], 50, 49);
        AddSerials(items[0], warehouse, loc["DEMO-IT-A01-01"], inbound, "DEMO-IT-DL5420", 9);
        AddSerials(items[0], warehouse, loc["DEMO-IT-QC-01"], inbound, "DEMO-IT-DL5420", 1, startIndex: 10, holdStatus: InventoryHoldStatusEnum.QcHold, notes: "Serial laptop đang tạm giữ QC do lỗi màn hình.");
        AddSerials(items[1], warehouse, loc["DEMO-IT-A01-02"], infrastructureInbound, "DEMO-IT-HP440G9", 6);
        AddSerials(items[2], warehouse, loc["DEMO-IT-B01-01"], projectionInbound, "DEMO-IT-EPX49", 4);
        AddSerials(items[3], warehouse, loc["DEMO-IT-NET-CAB-01"], infrastructureInbound, "DEMO-IT-AX55", 12);
        AddSerials(items[4], warehouse, loc["DEMO-IT-NET-CAB-02"], infrastructureInbound, "DEMO-IT-CBS250", 5);
        AddSerials(items[7], warehouse, loc["DEMO-IT-A01-03"], infrastructureInbound, "DEMO-IT-SM24", 16);
        AddInventoryTransactions(warehouse, items, actor, "DEMO-IT");
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync("it", "Kho thiết bị IT", warehouse.WarehouseId, ct);
    }

    private async Task<DemoDataSeedResult> SeedMedicalInventoryAsync(string actor, CancellationToken ct)
    {
        var now = VietnamTime.Now;
        var today = VietnamTime.Today;
        var uom = await AddUomsAsync(new[]
        {
            ("HOP", "Hộp", "Đóng gói"),
            ("THUNG", "Thùng", "Đóng gói"),
            ("CHAI", "Chai", "Số lượng"),
            ("BO", "Bộ", "Số lượng"),
            ("VI", "Vỉ", "Đóng gói"),
            ("GOI", "Gói", "Đóng gói")
        }, ct);

        var categories = AddCategories(new[]
        {
            ("DEMO-MED-PPE", "Vật tư bảo hộ"),
            ("DEMO-MED-CONSUMABLE", "Vật tư tiêu hao"),
            ("DEMO-MED-TEST", "Sinh phẩm xét nghiệm"),
            ("DEMO-MED-DRUG", "Thuốc thông dụng")
        });

        var warehouse = new Warehouse
        {
            WarehouseCode = "DEMO-MED-KHO",
            WarehouseName = "Kho vật tư y tế",
            Address = "Khu dược - tầng trệt phòng khám",
            ManagerName = "Bác sĩ Nguyễn Thảo Vy",
            Phone = "02873009999",
            IsActive = true,
            CreatedAt = now
        };
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(ct);

        var receiving = AddZone(warehouse, "DEMO-MED-RCV", "Khu tiếp nhận vật tư y tế", ZoneTypeEnum.Receiving);
        var drug = AddZone(warehouse, "DEMO-MED-DRUG", "Kho dược", ZoneTypeEnum.Storage);
        var consumable = AddZone(warehouse, "DEMO-MED-CONS", "Kệ vật tư tiêu hao", ZoneTypeEnum.Storage);
        var expiry = AddZone(warehouse, "DEMO-MED-EXP", "Khu hàng gần hết hạn", ZoneTypeEnum.Storage);
        var qc = AddZone(warehouse, "DEMO-MED-QC", "Khu QC y tế", ZoneTypeEnum.Staging);
        await _db.SaveChangesAsync(ct);

        var loc = AddLocations(new[]
        {
            (receiving, "DEMO-MED-DOCK-01", "RCV", "01", "01", "01"),
            (drug, "DEMO-MED-A01-01", "A", "01", "01", "01"),
            (drug, "DEMO-MED-A01-02", "A", "01", "01", "02"),
            (drug, "DEMO-MED-A01-03", "A", "01", "01", "03"),
            (consumable, "DEMO-MED-B01-01", "B", "01", "01", "01"),
            (consumable, "DEMO-MED-B01-02", "B", "01", "01", "02"),
            (consumable, "DEMO-MED-B01-03", "B", "01", "01", "03"),
            (expiry, "DEMO-MED-EXP-01", "EXP", "01", "01", "01"),
            (qc, "DEMO-MED-QC-01", "QC", "01", "01", "01")
        });
        await _db.SaveChangesAsync(ct);

        var partners = AddPartners(new[]
        {
            ("DEMO-MED-SUP-ANPHAR", "Công ty Dược An Phát", PartnerTypeEnum.Supplier, "Dược sĩ Lê Minh Anh"),
            ("DEMO-MED-SUP-MEDTECH", "MedTech Việt Nam", PartnerTypeEnum.Supplier, "Trần Bảo Ngọc"),
            ("DEMO-MED-DEPT-ER", "Khoa Cấp cứu", PartnerTypeEnum.Customer, "Điều dưỡng trưởng Phạm Hải Yến"),
            ("DEMO-MED-DEPT-CLINIC", "Phòng khám Tổng quát", PartnerTypeEnum.Customer, "Bác sĩ Đỗ Quốc Hưng")
        });
        await _db.SaveChangesAsync(ct);

        var items = new List<Item>
        {
            Item("DEMO-MED-MASK-4L", "Khẩu trang y tế 4 lớp", "MED-MASK-4L", categories["DEMO-MED-PPE"], uom["HOP"], 180, 40, 42000, false, true, true, loc["DEMO-MED-B01-01"], "Hộp 50 cái, quản lý lô và hạn dùng."),
            Item("DEMO-MED-GLOVE-NIT-M", "Găng tay nitrile size M", "MED-GLOVE-NIT-M", categories["DEMO-MED-PPE"], uom["HOP"], 95, 30, 98000, false, true, true, loc["DEMO-MED-B01-02"], "Găng tay không bột dùng trong khám bệnh."),
            Item("DEMO-MED-TEST-COVID", "Bộ test nhanh kháng nguyên", "MED-TEST-COVID", categories["DEMO-MED-TEST"], uom["BO"], 240, 60, 38000, false, true, true, loc["DEMO-MED-EXP-01"], "Ưu tiên xuất FEFO vì hạn dùng ngắn."),
            Item("DEMO-MED-SANITIZER-500", "Nước sát khuẩn tay 500ml", "MED-SANITIZER-500", categories["DEMO-MED-CONSUMABLE"], uom["CHAI"], 72, 20, 62000, false, true, true, loc["DEMO-MED-A01-01"], "Chai dung dịch sát khuẩn nhanh."),
            Item("DEMO-MED-BANDAGE-ROLL", "Bông băng cuộn vô trùng", "MED-BANDAGE-ROLL", categories["DEMO-MED-CONSUMABLE"], uom["GOI"], 140, 35, 18000, false, true, true, loc["DEMO-MED-A01-02"], "Vật tư tiêu hao cho thay băng."),
            Item("DEMO-MED-SYRINGE-5ML", "Kim tiêm 5ml vô trùng", "MED-SYRINGE-5ML", categories["DEMO-MED-CONSUMABLE"], uom["HOP"], 60, 25, 76000, false, true, true, loc["DEMO-MED-B01-03"], "Hộp 100 chiếc, quản lý lô."),
            Item("DEMO-MED-PARA-500", "Paracetamol 500mg", "MED-PARA-500", categories["DEMO-MED-DRUG"], uom["VI"], 320, 80, 9500, false, true, true, loc["DEMO-MED-A01-03"], "Thuốc thông dụng, theo dõi hạn dùng.")
        };
        _db.Items.AddRange(items);
        await _db.SaveChangesAsync(ct);

        AddStock(items[0], loc["DEMO-MED-B01-01"], 180, "MASK-260601", today.AddMonths(20), today.AddYears(3));
        AddStock(items[1], loc["DEMO-MED-B01-02"], 95, "GLOVE-260520", today.AddMonths(18), today.AddYears(3));
        AddStock(items[2], loc["DEMO-MED-EXP-01"], 240, "TEST-260430", today.AddMonths(-1), today.AddMonths(4));
        AddStock(items[3], loc["DEMO-MED-A01-01"], 72, "SAN-260501", today.AddMonths(-1), today.AddYears(2));
        AddStock(items[4], loc["DEMO-MED-A01-02"], 140, "BAND-260515", today.AddMonths(-1), today.AddYears(4));
        AddStock(items[5], loc["DEMO-MED-B01-03"], 60, "SYR-260515", today.AddMonths(-1), today.AddYears(5));
        AddStock(items[6], loc["DEMO-MED-A01-03"], 320, "PARA-260401", today.AddMonths(-2), today.AddYears(2));
        await _db.SaveChangesAsync(ct);

        var inbound = AddVoucher("PN-MED-20260609-0001", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-MED-SUP-ANPHAR"], "HD-MED-2026-019", "Nhập vật tư theo lô và hạn dùng cho tháng 06", actor, isPosted: true);
        AddVoucherLine(inbound, items[0], loc["DEMO-MED-B01-01"], 120, uom["HOP"], 42000, "MASK-260601", today.AddMonths(20), today.AddYears(3), 1);
        AddVoucherLine(inbound, items[2], loc["DEMO-MED-EXP-01"], 240, uom["BO"], 38000, "TEST-260430", today.AddMonths(-1), today.AddMonths(4), 2);
        AddVoucherLine(inbound, items[6], loc["DEMO-MED-A01-03"], 320, uom["VI"], 9500, "PARA-260401", today.AddMonths(-2), today.AddYears(2), 3);

        var outbound = AddVoucher("PX-MED-20260609-0001", VoucherTypeEnum.XuatKho, warehouse, partners["DEMO-MED-DEPT-ER"], "YC-ER-2026-011", "Xuất vật tư cho khoa Cấp cứu theo nguyên tắc FEFO", actor, isPosted: true);
        AddVoucherLine(outbound, items[2], loc["DEMO-MED-EXP-01"], 40, uom["BO"], 38000, "TEST-260430", today.AddMonths(-1), today.AddMonths(4), 1);
        AddVoucherLine(outbound, items[0], loc["DEMO-MED-B01-01"], 30, uom["HOP"], 42000, "MASK-260601", today.AddMonths(20), today.AddYears(3), 2);
        await _db.SaveChangesAsync(ct);

        AddQualityInspection(inbound, items[2], warehouse, 240, 24, 23, 1, "Một bộ test có bao bì móp, đưa vào theo dõi QC.", "QC vật tư y tế", inspectorName: "Bác sĩ Nguyễn Thảo Vy");
        AddStockCount(warehouse, actor, "Kiểm kê khu gần hết hạn: xác nhận ưu tiên xuất bộ test nhanh.", items[2], loc["DEMO-MED-EXP-01"], 240, 240);
        _db.StockAlerts.AddRange(
            new StockAlert { ItemId = items[2].ItemId, AlertType = AlertTypeEnum.Expiry, CurrentStock = 240, Threshold = 180, IsRead = false, IsResolved = false, CreatedAt = now },
            new StockAlert { ItemId = items[5].ItemId, AlertType = AlertTypeEnum.LowStock, CurrentStock = 60, Threshold = 70, IsRead = false, IsResolved = false, CreatedAt = now });
        AddInventoryTransactions(warehouse, items, actor, "DEMO-MED");
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync("medical", "Kho vật tư y tế", warehouse.WarehouseId, ct);
    }

    private async Task<DemoDataSeedResult> SeedEcommerceInventoryAsync(string actor, CancellationToken ct)
    {
        var now = VietnamTime.Now;
        var uom = await AddUomsAsync(new[]
        {
            ("CAI", "Cái", "Số lượng"),
            ("BO", "Bộ", "Số lượng"),
            ("THUNG", "Thùng", "Đóng gói"),
            ("KIEN", "Kiện", "Đóng gói")
        }, ct);

        var categories = AddCategories(new[]
        {
            ("DEMO-ECOM-AUDIO", "Phụ kiện âm thanh"),
            ("DEMO-ECOM-CHARGER", "Sạc và cáp"),
            ("DEMO-ECOM-CASE", "Ốp lưng và bảo vệ"),
            ("DEMO-ECOM-GAMING", "Phụ kiện gaming"),
            ("DEMO-ECOM-STAND", "Phụ kiện làm việc")
        });

        var warehouse = new Warehouse
        {
            WarehouseCode = "DEMO-ECOM-KHO",
            WarehouseName = "Kho thương mại điện tử",
            Address = "Khu logistics nội bộ - cổng số 2",
            ManagerName = "Lê Gia Hân",
            Phone = "02873007777",
            IsActive = true,
            CreatedAt = now
        };
        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(ct);

        var receiving = AddZone(warehouse, "DEMO-ECOM-RCV", "Khu nhập hàng TMĐT", ZoneTypeEnum.Receiving);
        var pickA = AddZone(warehouse, "DEMO-ECOM-PICK-A", "Kệ picking A", ZoneTypeEnum.Storage);
        var pickB = AddZone(warehouse, "DEMO-ECOM-PICK-B", "Kệ picking B", ZoneTypeEnum.Storage);
        var packing = AddZone(warehouse, "DEMO-ECOM-PACK", "Khu đóng gói", ZoneTypeEnum.Staging);
        var shipping = AddZone(warehouse, "DEMO-ECOM-SHIP", "Khu chờ vận chuyển", ZoneTypeEnum.Shipping);
        await _db.SaveChangesAsync(ct);

        var loc = AddLocations(new[]
        {
            (receiving, "DEMO-ECOM-DOCK-01", "RCV", "01", "01", "01"),
            (pickA, "DEMO-ECOM-A01-01", "A", "01", "01", "01"),
            (pickA, "DEMO-ECOM-A01-02", "A", "01", "01", "02"),
            (pickA, "DEMO-ECOM-A01-03", "A", "01", "01", "03"),
            (pickA, "DEMO-ECOM-A01-04", "A", "01", "01", "04"),
            (pickB, "DEMO-ECOM-B01-01", "B", "01", "01", "01"),
            (pickB, "DEMO-ECOM-B01-02", "B", "01", "01", "02"),
            (pickB, "DEMO-ECOM-B01-03", "B", "01", "01", "03"),
            (packing, "DEMO-ECOM-PACK-01", "PACK", "01", "01", "01"),
            (shipping, "DEMO-ECOM-SHIP-01", "SHIP", "01", "01", "01")
        });
        await _db.SaveChangesAsync(ct);

        var partners = AddPartners(new[]
        {
            ("DEMO-ECOM-SUP-DIGI", "Nhà phân phối DigiHub Việt Nam", PartnerTypeEnum.Supplier, "Vũ Hoàng Nam"),
            ("DEMO-ECOM-SUP-GEAR", "GearZone Distribution", PartnerTypeEnum.Supplier, "Mai Phương Anh"),
            ("DEMO-ECOM-CUS-HCM01", "Khách lẻ kênh Online - Quận 1", PartnerTypeEnum.Customer, "Nguyễn Khánh An"),
            ("DEMO-ECOM-CUS-DN02", "Khách sỉ phụ kiện Đà Nẵng", PartnerTypeEnum.Customer, "Trương Hải Long"),
            ("DEMO-ECOM-CARRIER", "Đơn vị vận chuyển Hỏa Tốc 24h", PartnerTypeEnum.Customer, "Lê Gia Hân")
        });
        await _db.SaveChangesAsync(ct);

        var items = new List<Item>
        {
            Item("DEMO-ECOM-HEAD-BT-A9", "Tai nghe Bluetooth AirBeat A9", "ECOM-HEAD-BT-A9", categories["DEMO-ECOM-AUDIO"], uom["CAI"], 120, 25, 390000, false, false, false, loc["DEMO-ECOM-A01-01"], "SKU bán chạy, có mã vạch picking."),
            Item("DEMO-ECOM-CHG-65W-GAN", "Sạc nhanh GaN 65W", "ECOM-CHG-65W-GAN", categories["DEMO-ECOM-CHARGER"], uom["CAI"], 85, 20, 320000, false, false, false, loc["DEMO-ECOM-A01-02"], "Sạc nhanh cho laptop và điện thoại."),
            Item("DEMO-ECOM-CABLE-C2C-1M", "Cáp Type-C to Type-C 1m", "ECOM-CABLE-C2C-1M", categories["DEMO-ECOM-CHARGER"], uom["CAI"], 240, 60, 65000, false, false, false, loc["DEMO-ECOM-B01-01"], "Cáp sạc đóng gói lẻ."),
            Item("DEMO-ECOM-CASE-IP15", "Ốp lưng iPhone 15 trong suốt", "ECOM-CASE-IP15", categories["DEMO-ECOM-CASE"], uom["CAI"], 180, 50, 78000, false, false, false, loc["DEMO-ECOM-B01-02"], "Biến thể phổ biến cho kênh online."),
            Item("DEMO-ECOM-MOUSE-G102", "Chuột gaming Logitech G102", "ECOM-MOUSE-G102", categories["DEMO-ECOM-GAMING"], uom["CAI"], 64, 15, 385000, true, false, false, loc["DEMO-ECOM-A01-03"], "Hàng điện tử có serial theo đợt nhập."),
            Item("DEMO-ECOM-KBD-MECH-K2", "Bàn phím cơ Keychron K2", "ECOM-KBD-MECH-K2", categories["DEMO-ECOM-GAMING"], uom["CAI"], 38, 10, 1690000, true, false, false, loc["DEMO-ECOM-A01-04"], "Bàn phím cơ giá trị cao."),
            Item("DEMO-ECOM-STAND-LAP-ALU", "Giá đỡ laptop nhôm gấp gọn", "ECOM-STAND-LAP-ALU", categories["DEMO-ECOM-STAND"], uom["CAI"], 95, 20, 185000, false, false, false, loc["DEMO-ECOM-B01-03"], "Phụ kiện làm việc tại nhà.")
        };
        _db.Items.AddRange(items);
        await _db.SaveChangesAsync(ct);

        AddStock(items[0], loc["DEMO-ECOM-A01-01"], 120, reserved: 2);
        AddStock(items[1], loc["DEMO-ECOM-A01-02"], 85);
        AddStock(items[2], loc["DEMO-ECOM-B01-01"], 240);
        AddStock(items[3], loc["DEMO-ECOM-B01-02"], 180, reserved: 8);
        AddStock(items[4], loc["DEMO-ECOM-A01-03"], 64);
        AddStock(items[5], loc["DEMO-ECOM-A01-04"], 38);
        AddStock(items[6], loc["DEMO-ECOM-B01-03"], 95);
        await _db.SaveChangesAsync(ct);

        var inbound = AddVoucher("PN-ECOM-20260609-0001", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-ECOM-SUP-DIGI"], "HD-ECOM-2026-088", "Nhập hàng bổ sung trước chiến dịch flash sale", actor, isPosted: true);
        AddVoucherLine(inbound, items[0], loc["DEMO-ECOM-A01-01"], 80, uom["CAI"], 390000, line: 1);
        AddVoucherLine(inbound, items[1], loc["DEMO-ECOM-A01-02"], 60, uom["CAI"], 320000, line: 2);
        AddVoucherLine(inbound, items[2], loc["DEMO-ECOM-B01-01"], 160, uom["CAI"], 65000, line: 3);

        var gamingInbound = AddVoucher("PN-ECOM-20260609-0002", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-ECOM-SUP-GEAR"], "HD-ECOM-2026-089", "Nhập phụ kiện gaming đã kiểm serial cho chiến dịch cuối tuần", actor, isPosted: true);
        AddVoucherLine(gamingInbound, items[4], loc["DEMO-ECOM-A01-03"], 64, uom["CAI"], 385000, line: 1);
        AddVoucherLine(gamingInbound, items[5], loc["DEMO-ECOM-A01-04"], 38, uom["CAI"], 1690000, line: 2);

        var scheduledInbound = AddVoucher("PN-ECOM-20260609-0003", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-ECOM-SUP-GEAR"], "HD-ECOM-2026-090", "Lô phụ kiện gaming bổ sung đang chờ nhận tại cửa kho", actor, isPosted: false);
        scheduledInbound.InboundStatus = InboundStatusEnum.Approved;
        scheduledInbound.ReceivedBy = null;
        scheduledInbound.ReceivedAt = null;
        scheduledInbound.ReviewedBy = null;
        scheduledInbound.ReviewedAt = null;
        scheduledInbound.ReviewResult = ReviewResultEnum.Pending;
        scheduledInbound.ReviewNote = null;
        scheduledInbound.CompletedBy = null;
        scheduledInbound.CompletedAt = null;
        scheduledInbound.AsnCode = "ASN-ECOM-20260609-0003";
        scheduledInbound.CarrierName = "Đơn vị vận chuyển Hỏa Tốc 24h";
        scheduledInbound.DriverName = "Lê Gia Hân";
        scheduledInbound.VehicleNumber = "51D-333.33";
        scheduledInbound.DriverPhone = "0363636363";
        AddVoucherLine(scheduledInbound, items[4], loc["DEMO-ECOM-A01-03"], 12, uom["CAI"], 385000, line: 1);

        var pendingQcInbound = AddVoucher("PN-ECOM-20260609-0004", VoucherTypeEnum.NhapKho, warehouse, partners["DEMO-ECOM-SUP-DIGI"], "HD-ECOM-2026-091", "Lô sạc nhanh đang nhận hàng và chờ kiểm tra chất lượng", actor, isPosted: false);
        pendingQcInbound.InboundStatus = InboundStatusEnum.Receiving;
        pendingQcInbound.AsnCode = "ASN-ECOM-20260609-0004";
        pendingQcInbound.ExpectedArrivalAt = now.AddMinutes(-30);
        pendingQcInbound.DockAppointmentStart = now.AddMinutes(-30);
        pendingQcInbound.DockAppointmentEnd = now.AddMinutes(30);
        pendingQcInbound.DockDoor = "DOCK-02";
        pendingQcInbound.ReceivedBy = actor;
        pendingQcInbound.ReceivedAt = now.AddMinutes(-10);
        pendingQcInbound.ReviewedBy = null;
        pendingQcInbound.ReviewedAt = null;
        pendingQcInbound.ReviewResult = ReviewResultEnum.Pending;
        pendingQcInbound.ReviewNote = null;
        pendingQcInbound.CompletedBy = null;
        pendingQcInbound.CompletedAt = null;
        AddVoucherLine(pendingQcInbound, items[1], loc["DEMO-ECOM-A01-02"], 24, uom["CAI"], 320000, line: 1);

        var out1 = AddVoucher("PX-ECOM-20260609-0001", VoucherTypeEnum.XuatKho, warehouse, partners["DEMO-ECOM-CUS-HCM01"], "SO-ECOM-HCM-2026-001", "Đơn online gồm tai nghe, sạc nhanh và cáp Type-C", actor, isPosted: false, fulfillment: FulfillmentStatusEnum.WaitingForPick);
        AddVoucherLine(out1, items[0], loc["DEMO-ECOM-A01-01"], 2, uom["CAI"], 390000, line: 1);
        AddVoucherLine(out1, items[1], loc["DEMO-ECOM-A01-02"], 1, uom["CAI"], 320000, line: 2);
        AddVoucherLine(out1, items[2], loc["DEMO-ECOM-B01-01"], 3, uom["CAI"], 65000, line: 3);

        var out2 = AddVoucher("PX-ECOM-20260609-0002", VoucherTypeEnum.XuatKho, warehouse, partners["DEMO-ECOM-CUS-DN02"], "SO-ECOM-DN-2026-002", "Đơn sỉ phụ kiện: ốp lưng và giá đỡ laptop", actor, isPosted: false, fulfillment: FulfillmentStatusEnum.Picking);
        AddVoucherLine(out2, items[3], loc["DEMO-ECOM-B01-02"], 8, uom["CAI"], 78000, line: 1);
        AddVoucherLine(out2, items[6], loc["DEMO-ECOM-B01-03"], 3, uom["CAI"], 185000, line: 2);

        var out3 = AddVoucher("PX-ECOM-20260609-0003", VoucherTypeEnum.XuatKho, warehouse, partners["DEMO-ECOM-CUS-HCM01"], "SO-ECOM-HCM-2026-003", "Đơn gaming: chuột G102 và bàn phím cơ K2", actor, isPosted: false, fulfillment: FulfillmentStatusEnum.Packed);
        AddVoucherLine(out3, items[4], loc["DEMO-ECOM-A01-03"], 2, uom["CAI"], 385000, line: 1);
        AddVoucherLine(out3, items[5], loc["DEMO-ECOM-A01-04"], 1, uom["CAI"], 1690000, line: 2);
        await _db.SaveChangesAsync(ct);

        var wave = new Wave
        {
            WaveCode = "DEMO-ECOM-WAVE-202606-01",
            WaveProfile = "FlashSale-Priority",
            CarrierCode = "HT24H",
            CarrierName = "Hỏa Tốc 24h",
            RouteCode = "HCM-DN-MIX",
            CutoffTime = now.AddHours(3),
            Priority = WavePriorityEnum.High,
            WarehouseId = warehouse.WarehouseId,
            Status = WaveStatusEnum.Released,
            Notes = "Gom 3 đơn đồng thời để thể hiện reservation, picking và packing.",
            CreatedBy = actor,
            CreatedAt = now,
            ReleasedAt = now.AddMinutes(10)
        };
        _db.Waves.Add(wave);
        await _db.SaveChangesAsync(ct);

        AddWaveLine(wave, out1, items[0], 2);
        AddWaveLine(wave, out2, items[3], 8);
        AddWaveLine(wave, out3, items[4], 2);
        AddReservationAndPick(wave, out1, items[0], loc["DEMO-ECOM-A01-01"], 2, actor, "DEMO-ECOM-PICK-001");
        AddReservationAndPick(wave, out2, items[3], loc["DEMO-ECOM-B01-02"], 8, actor, "DEMO-ECOM-PICK-002");
        AddReservationAndPick(wave, out3, items[4], loc["DEMO-ECOM-A01-03"], 2, actor, "DEMO-ECOM-PICK-003", pickedQty: 2, status: PickTaskStatusEnum.Completed);
        AddQualityInspection(inbound, items[1], warehouse, 60, 6, 6, 0, "Kiểm ngoại quan lô sạc nhanh đạt yêu cầu.", "QC hàng điện tử", inspectorName: "Lê Gia Hân");
        AddStockCount(warehouse, actor, "Kiểm kê nhanh khu picking B: không chênh lệch ở cáp Type-C.", items[2], loc["DEMO-ECOM-B01-01"], 240, 240);
        AddSerials(items[4], warehouse, loc["DEMO-ECOM-A01-03"], gamingInbound, "DEMO-ECOM-G102", 64);
        AddSerials(items[5], warehouse, loc["DEMO-ECOM-A01-04"], gamingInbound, "DEMO-ECOM-K2", 38);
        AddInventoryTransactions(warehouse, items, actor, "DEMO-ECOM");
        await _db.SaveChangesAsync(ct);

        return await BuildResultAsync("ecommerce", "Kho thương mại điện tử", warehouse.WarehouseId, ct);
    }

    private async Task<Dictionary<string, UnitOfMeasure>> AddUomsAsync(IEnumerable<(string Code, string Name, string Group)> rows, CancellationToken ct)
    {
        var result = new Dictionary<string, UnitOfMeasure>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var uom = new UnitOfMeasure { UomCode = row.Code, UomName = row.Name, UomGroup = row.Group, IsActive = true };
            _db.UnitsOfMeasure.Add(uom);
            result[row.Code] = uom;
        }
        await _db.SaveChangesAsync(ct);
        return result;
    }

    private Dictionary<string, ItemCategory> AddCategories(IEnumerable<(string Code, string Name)> rows)
    {
        var result = new Dictionary<string, ItemCategory>(StringComparer.OrdinalIgnoreCase);
        var order = 10;
        foreach (var row in rows)
        {
            var category = new ItemCategory { CategoryCode = row.Code, CategoryName = row.Name, SortOrder = order, IsActive = true, CreatedAt = VietnamTime.Now };
            _db.ItemCategories.Add(category);
            result[row.Code] = category;
            order += 10;
        }
        return result;
    }

    private static Zone AddZone(Warehouse warehouse, string code, string name, ZoneTypeEnum type)
    {
        var zone = new Zone { Warehouse = warehouse, ZoneCode = code, ZoneName = name, ZoneType = type, IsActive = true };
        warehouse.Zones.Add(zone);
        return zone;
    }

    private Dictionary<string, Location> AddLocations(IEnumerable<(Zone Zone, string Code, string Aisle, string Rack, string Shelf, string Bin)> rows)
    {
        var result = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);
        var seq = 1;
        foreach (var row in rows)
        {
            var location = new Location
            {
                Zone = row.Zone,
                LocationCode = row.Code,
                AisleCode = row.Aisle,
                RackCode = row.Rack,
                ShelfCode = row.Shelf,
                BinCode = row.Bin,
                AisleSequence = seq++,
                CurrentLoad = 0,
                MaxCapacity = 999999,
                HeightLevel = 1,
                AllowMixedSku = true,
                AllowMechanicalHandling = true,
                IsActive = true,
                Barcode = row.Code
            };
            _db.Locations.Add(location);
            result[row.Code] = location;
        }
        return result;
    }

    private Dictionary<string, Partner> AddPartners(IEnumerable<(string Code, string Name, PartnerTypeEnum Type, string Contact)> rows)
    {
        var result = new Dictionary<string, Partner>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var partner = new Partner
            {
                PartnerCode = row.Code,
                PartnerName = row.Name,
                PartnerType = row.Type,
                ContactPerson = row.Contact,
                Phone = "0287300" + Math.Abs(row.Code.GetHashCode()).ToString("0000")[..4],
                Email = $"{row.Code.ToLowerInvariant().Replace("demo-", "").Replace("-", ".")}@example.local",
                Address = "Địa chỉ demo nội bộ",
                VendorRating = row.Type == PartnerTypeEnum.Supplier ? VendorRatingEnum.A : VendorRatingEnum.B,
                QcSamplePercent = row.Type == PartnerTypeEnum.Supplier ? 20 : 0,
                IsActive = true,
                CreatedAt = VietnamTime.Now
            };
            _db.Partners.Add(partner);
            result[row.Code] = partner;
        }
        return result;
    }

    private static Item Item(
        string code,
        string name,
        string sku,
        ItemCategory category,
        UnitOfMeasure uom,
        decimal stock,
        decimal min,
        decimal cost,
        bool serial,
        bool lot,
        bool expiry,
        Location defaultLocation,
        string description)
        => new()
        {
            ItemCode = code,
            ItemName = name,
            SkuCode = sku,
            Barcode = code,
            Category = category,
            BaseUom = uom,
            BaseUomId = uom.UomId,
            ItemType = serial ? ItemTypeEnum.PhuTung : ItemTypeEnum.NguyenVatLieu,
            CurrentStock = stock,
            MinThreshold = min,
            ReorderPoint = min,
            MaxThreshold = stock * 3,
            UnitCost = cost,
            LastCost = cost,
            TotalStockValue = stock * cost,
            Weight = serial ? 1.8m : 0.2m,
            Length = 30,
            Width = 20,
            Height = 10,
            TrackSerial = serial,
            TrackLot = lot,
            TrackExpiry = expiry,
            DefaultLocation = defaultLocation,
            Description = description,
            Specifications = $"SKU demo: {sku}; kiểm soát theo nghiệp vụ kho nội bộ.",
            IsActive = true,
            CreatedAt = VietnamTime.Now,
            CreatedBy = "demo-seed"
        };

    private void AddStock(
        Item item,
        Location location,
        decimal qty,
        string? lot = null,
        DateTime? mfg = null,
        DateTime? expiry = null,
        InventoryHoldStatusEnum hold = InventoryHoldStatusEnum.Available,
        decimal reserved = 0)
    {
        _db.ItemLocations.Add(new ItemLocation
        {
            Item = item,
            ItemId = item.ItemId,
            Location = location,
            LocationId = location.LocationId,
            Quantity = qty,
            ReservedQty = reserved,
            LotNumber = lot,
            ExpiryDate = expiry,
            HoldStatus = hold,
            UpdatedAt = VietnamTime.Now
        });
    }

    private Voucher AddVoucher(
        string code,
        VoucherTypeEnum type,
        Warehouse warehouse,
        Partner partner,
        string referenceNo,
        string description,
        string actor,
        bool isPosted,
        FulfillmentStatusEnum fulfillment = FulfillmentStatusEnum.Completed)
    {
        var isInbound = type is VoucherTypeEnum.NhapKho or VoucherTypeEnum.KhachTra or VoucherTypeEnum.NhapThanhPham;
        var voucher = new Voucher
        {
            VoucherCode = code,
            VoucherType = type,
            VoucherDate = VietnamTime.Today,
            Warehouse = warehouse,
            WarehouseId = warehouse.WarehouseId,
            Partner = partner,
            PartnerId = partner.PartnerId,
            ReferenceNo = referenceNo,
            Description = description,
            CurrencyCode = "VND",
            SourceType = SourceTypeEnum.Manual,
            IsPosted = isPosted,
            CreatedBy = actor,
            CreatedAt = VietnamTime.Now,
            FulfillmentStatus = fulfillment,
            InboundStatus = isInbound ? InboundStatusEnum.Completed : InboundStatusEnum.Draft,
            SubmittedBy = actor,
            SubmittedAt = VietnamTime.Now.AddMinutes(-45),
            ApprovedBy = actor,
            ApprovedAt = VietnamTime.Now.AddMinutes(-30),
            ReceivedBy = isInbound ? actor : null,
            ReceivedAt = isInbound ? VietnamTime.Now.AddMinutes(-15) : null,
            ReviewedBy = isInbound ? actor : null,
            ReviewedAt = isInbound ? VietnamTime.Now.AddMinutes(-10) : null,
            ReviewResult = isInbound ? ReviewResultEnum.Pass : ReviewResultEnum.Pending,
            ReviewNote = isInbound ? "Kiểm đủ số lượng, đúng mã hàng và đúng tình trạng bao bì." : null,
            CompletedBy = isPosted ? actor : null,
            CompletedAt = isPosted ? VietnamTime.Now : null,
            ExpectedArrivalAt = VietnamTime.Now.AddHours(2),
            DockAppointmentStart = VietnamTime.Now.AddHours(2),
            DockAppointmentEnd = VietnamTime.Now.AddHours(3),
            DockDoor = "DOCK-01",
            CarrierName = "Đơn vị vận chuyển nội bộ",
            VehicleNumber = "51D-286.08",
            DriverName = "Lê Gia Hân",
            DriverPhone = "0363636363"
        };
        _db.Vouchers.Add(voucher);
        return voucher;
    }

    private void AddVoucherLine(
        Voucher voucher,
        Item item,
        Location location,
        decimal qty,
        UnitOfMeasure uom,
        decimal unitPrice,
        string? lot = null,
        DateTime? mfg = null,
        DateTime? expiry = null,
        int line = 1)
    {
        voucher.Details.Add(new VoucherDetail
        {
            Voucher = voucher,
            Item = item,
            ItemId = item.ItemId,
            Location = location,
            LocationId = location.LocationId,
            TransactionQty = qty,
            TransactionUom = uom,
            TransactionUomId = uom.UomId,
            ConversionRate = 1,
            BaseQty = qty,
            UnitPrice = unitPrice,
            LineAmount = qty * unitPrice,
            QualityStatus = QualityStatusEnum.Good,
            LotNumber = lot,
            ManufacturingDate = mfg,
            ExpiryDate = expiry,
            LineNumber = line,
            Notes = voucher.IsInboundFlow && voucher.IsPosted
                ? $"[ACTUAL:{qty:N4};CHECKED_BY:{voucher.ReviewedBy};CHECKED_AT:{VietnamTime.Now:yyyy-MM-dd HH:mm}] Kiểm đủ số lượng, đúng mã hàng."
                : "Dòng nghiệp vụ kho nội bộ"
        });
        voucher.TotalLines = voucher.Details.Count;
        voucher.TotalAmount = voucher.Details.Sum(x => x.LineAmount);
    }

    private void AddQualityInspection(
        Voucher voucher,
        Item item,
        Warehouse warehouse,
        decimal totalQty,
        decimal sampleQty,
        decimal passedQty,
        decimal failedQty,
        string defectDescription,
        string planName,
        string inspectorName)
    {
        _db.QualityInspections.Add(new QualityInspection
        {
            Voucher = voucher,
            VoucherId = voucher.VoucherId,
            VoucherDetailId = voucher.Details.FirstOrDefault(x => x.ItemId == item.ItemId)?.VoucherDetailId,
            Item = item,
            ItemId = item.ItemId,
            Warehouse = warehouse,
            WarehouseId = warehouse.WarehouseId,
            TotalQty = totalQty,
            SampleQty = sampleQty,
            PassedQty = passedQty,
            FailedQty = failedQty,
            SamplePercent = totalQty > 0 ? Math.Round(sampleQty / totalQty * 100, 2) : 0,
            Disposition = failedQty > 0 ? QcDispositionEnum.Hold : QcDispositionEnum.Accept,
            OverallResult = failedQty > 0 ? QualityStatusEnum.OnHold : QualityStatusEnum.Passed,
            InspectorName = inspectorName,
            InspectedAt = VietnamTime.Now,
            DefectDescription = defectDescription,
            Notes = "QC demo có số lượng mẫu và kết quả rõ ràng.",
            LotNumber = voucher.Details.FirstOrDefault(x => x.ItemId == item.ItemId)?.LotNumber,
            InspectionPlanName = planName,
            CreatedAt = VietnamTime.Now
        });
    }

    private void AddStockCount(Warehouse warehouse, string actor, string notes, Item item, Location location, decimal systemQty, decimal countedQty)
    {
        var sheet = new StockCountSheet
        {
            SheetCode = $"DEMO-CC-{warehouse.WarehouseCode}-001",
            Warehouse = warehouse,
            WarehouseId = warehouse.WarehouseId,
            CountDate = VietnamTime.Today,
            Notes = notes,
            Status = StockCountStatusEnum.Approved,
            CreatedBy = actor,
            CreatedAt = VietnamTime.Now,
            CompletedAt = VietnamTime.Now,
            ApprovedBy = actor,
            ApprovedAt = VietnamTime.Now,
            ApprovalReason = "Dữ liệu demo kiểm kê đã được xác nhận."
        };
        sheet.Lines.Add(new StockCountLine
        {
            Item = item,
            ItemId = item.ItemId,
            Location = location,
            LocationId = location.LocationId,
            SystemQty = systemQty,
            CountedQty = countedQty,
            Variance = countedQty - systemQty,
            Status = 2,
            CountedBy = actor,
            CountedAt = VietnamTime.Now
        });
        _db.StockCountSheets.Add(sheet);
    }

    private void AddSerials(
        Item item,
        Warehouse warehouse,
        Location location,
        Voucher voucher,
        string prefix,
        int count,
        int startIndex = 1,
        InventoryHoldStatusEnum holdStatus = InventoryHoldStatusEnum.Available,
        string notes = "Serial demo phục vụ truy xuất tài sản.")
    {
        for (var i = 0; i < count; i++)
        {
            var serialNo = startIndex + i;
            _db.SerialNumbers.Add(new SerialNumber
            {
                SerialCode = $"{prefix}-{serialNo:0000}",
                Warehouse = warehouse,
                WarehouseId = warehouse.WarehouseId,
                Item = item,
                ItemId = item.ItemId,
                Location = location,
                LocationId = location.LocationId,
                Voucher = voucher,
                VoucherId = voucher.VoucherId,
                VoucherDetailId = voucher.Details.FirstOrDefault(x => x.ItemId == item.ItemId)?.VoucherDetailId,
                Status = SerialNumberStatusEnum.Active,
                HoldStatus = holdStatus,
                Notes = notes,
                CreatedAt = VietnamTime.Now
            });
        }
    }

    private void AddWaveLine(Wave wave, Voucher voucher, Item item, decimal qty)
    {
        _db.WaveLines.Add(new WaveLine
        {
            Wave = wave,
            WaveId = wave.WaveId,
            Voucher = voucher,
            VoucherId = voucher.VoucherId,
            Item = item,
            ItemId = item.ItemId,
            RequiredQty = qty,
            PickedQty = 0,
            Status = 1
        });
        voucher.Wave = wave;
        voucher.WaveId = wave.WaveId;
    }

    private void AddReservationAndPick(
        Wave wave,
        Voucher voucher,
        Item item,
        Location source,
        decimal qty,
        string actor,
        string taskCode,
        decimal pickedQty = 0,
        PickTaskStatusEnum status = PickTaskStatusEnum.Assigned)
    {
        var detail = voucher.Details.First(x => x.ItemId == item.ItemId);
        var reservation = new StockReservation
        {
            Voucher = voucher,
            VoucherId = voucher.VoucherId,
            VoucherDetail = detail,
            VoucherDetailId = detail.VoucherDetailId,
            Item = item,
            ItemId = item.ItemId,
            Location = source,
            LocationId = source.LocationId,
            ReservedQty = qty,
            ConsumedQty = status == PickTaskStatusEnum.Completed ? qty : 0,
            ReleasedQty = 0,
            Status = status == PickTaskStatusEnum.Completed ? ReservationStatusEnum.Consumed : ReservationStatusEnum.Active,
            Notes = "Giữ chỗ tồn demo cho đơn thương mại điện tử.",
            CreatedBy = actor,
            CreatedAt = VietnamTime.Now
        };
        var pick = new PickTask
        {
            TaskCode = taskCode,
            Wave = wave,
            WaveId = wave.WaveId,
            Voucher = voucher,
            VoucherId = voucher.VoucherId,
            VoucherDetail = detail,
            VoucherDetailId = detail.VoucherDetailId,
            Item = item,
            ItemId = item.ItemId,
            SourceLocation = source,
            SourceLocationId = source.LocationId,
            TargetQty = qty,
            PickedQty = pickedQty,
            Status = status,
            PickTaskMode = PickTaskModeEnum.Single,
            AssignedTo = "nhanvien.kho",
            AssignedAt = VietnamTime.Now,
            StartedAt = status == PickTaskStatusEnum.Completed ? VietnamTime.Now.AddMinutes(-12) : null,
            CompletedAt = status == PickTaskStatusEnum.Completed ? VietnamTime.Now : null,
            DueAt = VietnamTime.Now.AddHours(2)
        };
        pick.Allocations.Add(new PickTaskAllocation
        {
            StockReservation = reservation,
            Voucher = voucher,
            VoucherId = voucher.VoucherId,
            VoucherDetail = detail,
            VoucherDetailId = detail.VoucherDetailId,
            AllocatedQty = qty,
            PickedQty = pickedQty
        });
        _db.StockReservations.Add(reservation);
        _db.PickTasks.Add(pick);
    }

    private void AddInventoryTransactions(Warehouse warehouse, IEnumerable<Item> items, string actor, string prefix)
    {
        var itemSet = items.ToHashSet();
        var itemLocationRows = _db.ItemLocations.Local
            .Where(x => x.Item != null && itemSet.Contains(x.Item))
            .ToList();
        var stockRowsByKey = itemLocationRows.ToDictionary(DemoStockKey);
        var postedVoucherLines = _db.VoucherDetails.Local
            .Where(d => d.Voucher != null
                && d.Voucher.IsPosted
                && !d.Voucher.IsCancelled
                && d.Item != null
                && itemSet.Contains(d.Item)
                && d.LocationId.HasValue
                && d.Location != null
                && (d.Voucher.IsInboundFlow || IsDemoOutboundFlow(d.Voucher.VoucherType)))
            .OrderBy(d => d.Voucher!.CreatedAt)
            .ThenBy(d => d.Voucher!.VoucherCode)
            .ThenBy(d => d.LineNumber)
            .ThenBy(d => d.VoucherDetailId)
            .ToList();
        var inboundByKey = postedVoucherLines
            .Where(d => d.Voucher!.IsInboundFlow)
            .GroupBy(DemoStockKey)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.BaseQty));
        var outboundByKey = postedVoucherLines
            .Where(d => IsDemoOutboundFlow(d.Voucher!.VoucherType))
            .GroupBy(DemoStockKey)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.BaseQty));
        var activeReservations = _db.StockReservations.Local
            .Where(r => r.Status == ReservationStatusEnum.Active
                && r.Item != null
                && itemSet.Contains(r.Item)
                && r.ReservedQty - r.ConsumedQty - r.ReleasedQty > 0)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.VoucherId)
            .ThenBy(r => r.VoucherDetailId)
            .ToList();
        var runningQtyByKey = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var runningReservedByKey = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var row in itemLocationRows)
        {
            var key = DemoStockKey(row);
            var inboundQty = inboundByKey.GetValueOrDefault(key);
            var outboundQty = outboundByKey.GetValueOrDefault(key);
            var openingQty = row.Quantity - inboundQty + outboundQty;
            if (openingQty < 0)
            {
                throw new InvalidOperationException($"Demo stock ledger for {row.Item!.ItemCode} would start negative.");
            }

            runningQtyByKey[key] = openingQty;
            if (openingQty <= 0)
            {
                continue;
            }

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                TransactionType = InventoryTransactionTypeEnum.OpeningBalance,
                TransactionGroupKey = $"{prefix}-OPENING",
                IdempotencyKey = $"{prefix}-OPENING-{row.Item!.ItemCode}-{row.Location!.LocationCode}-{row.LotNumber ?? "NOLOT"}",
                Warehouse = warehouse,
                WarehouseId = warehouse.WarehouseId,
                Item = row.Item,
                ItemId = row.ItemId,
                Location = row.Location!,
                LocationId = row.LocationId,
                LotNumber = row.LotNumber,
                ExpiryDate = row.ExpiryDate,
                HoldStatusAfter = row.HoldStatus,
                QuantityDelta = openingQty,
                ReservedDelta = 0,
                AvailableDelta = openingQty,
                QuantityBefore = 0,
                QuantityAfter = openingQty,
                ReservedBefore = 0,
                ReservedAfter = 0,
                AvailableBefore = 0,
                AvailableAfter = openingQty,
                ReferenceType = "DemoSeed",
                ReferenceCode = prefix,
                Actor = actor,
                TransactionAt = VietnamTime.Now,
                MetadataJson = "{\"source\":\"demo-data\"}"
            });
        }

        foreach (var detail in postedVoucherLines)
        {
            var key = DemoStockKey(detail);
            if (!stockRowsByKey.TryGetValue(key, out var stockRow))
            {
                throw new InvalidOperationException($"Demo voucher ledger cannot find stock row for {detail.Item!.ItemCode}.");
            }

            var isInbound = detail.Voucher!.IsInboundFlow;
            var beforeQty = runningQtyByKey.GetValueOrDefault(key);
            var deltaQty = isInbound ? detail.BaseQty : -detail.BaseQty;
            var afterQty = beforeQty + deltaQty;
            if (afterQty < 0)
            {
                throw new InvalidOperationException($"Demo voucher ledger for {detail.Voucher.VoucherCode} would make {detail.Item!.ItemCode} negative.");
            }

            runningQtyByKey[key] = afterQty;
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                TransactionType = isInbound ? InventoryTransactionTypeEnum.Receive : InventoryTransactionTypeEnum.Ship,
                TransactionGroupKey = $"voucher:{detail.VoucherId}:demo-seed-{(isInbound ? "receive" : "ship")}",
                IdempotencyKey = $"{prefix}-VOUCHER-LEDGER-{detail.VoucherDetailId}",
                Warehouse = warehouse,
                WarehouseId = warehouse.WarehouseId,
                OwnerPartnerId = detail.Voucher.OwnerPartnerId,
                Item = detail.Item!,
                ItemId = detail.ItemId,
                Location = detail.Location!,
                LocationId = detail.LocationId!.Value,
                LotNumber = detail.LotNumber,
                ExpiryDate = detail.ExpiryDate,
                HoldStatusAfter = stockRow.HoldStatus,
                QuantityDelta = deltaQty,
                ReservedDelta = 0,
                AvailableDelta = deltaQty,
                QuantityBefore = beforeQty,
                QuantityAfter = afterQty,
                ReservedBefore = 0,
                ReservedAfter = 0,
                AvailableBefore = beforeQty,
                AvailableAfter = afterQty,
                Voucher = detail.Voucher,
                VoucherId = detail.VoucherId,
                VoucherDetail = detail,
                VoucherDetailId = detail.VoucherDetailId,
                ReferenceType = "Voucher",
                ReferenceId = detail.VoucherId.ToString(),
                ReferenceCode = detail.Voucher.VoucherCode,
                Actor = detail.Voucher.CompletedBy ?? detail.Voucher.ApprovedBy ?? detail.Voucher.CreatedBy ?? actor,
                TransactionAt = detail.Voucher.CompletedAt ?? detail.Voucher.ApprovedAt ?? detail.Voucher.CreatedAt,
                MetadataJson = "{\"source\":\"demo-data\",\"ledger\":\"posted-voucher\"}"
            });
        }

        foreach (var reservation in activeReservations)
        {
            var key = DemoStockKey(reservation);
            if (!stockRowsByKey.TryGetValue(key, out var stockRow))
            {
                throw new InvalidOperationException($"Demo reservation ledger cannot find stock row for {reservation.Item!.ItemCode}.");
            }

            var openQty = reservation.ReservedQty - reservation.ConsumedQty - reservation.ReleasedQty;
            var quantity = runningQtyByKey.GetValueOrDefault(key);
            var reservedBefore = runningReservedByKey.GetValueOrDefault(key);
            var reservedAfter = reservedBefore + openQty;
            if (reservedAfter > quantity)
            {
                throw new InvalidOperationException($"Demo reservation ledger for {reservation.Item!.ItemCode} exceeds available stock.");
            }

            runningReservedByKey[key] = reservedAfter;
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                TransactionType = InventoryTransactionTypeEnum.Pick,
                TransactionGroupKey = $"voucher:{reservation.VoucherId}:demo-seed-reserve",
                IdempotencyKey = $"{prefix}-RESERVATION-{reservation.VoucherDetailId}-{reservation.LocationId}-{reservation.LotNumber ?? "NOLOT"}",
                Warehouse = warehouse,
                WarehouseId = warehouse.WarehouseId,
                OwnerPartnerId = reservation.OwnerPartnerId,
                Item = reservation.Item!,
                ItemId = reservation.ItemId,
                Location = reservation.Location!,
                LocationId = reservation.LocationId,
                LotNumber = reservation.LotNumber,
                ExpiryDate = reservation.ExpiryDate,
                HoldStatusAfter = stockRow.HoldStatus,
                QuantityDelta = 0,
                ReservedDelta = openQty,
                AvailableDelta = -openQty,
                QuantityBefore = quantity,
                QuantityAfter = quantity,
                ReservedBefore = reservedBefore,
                ReservedAfter = reservedAfter,
                AvailableBefore = quantity - reservedBefore,
                AvailableAfter = quantity - reservedAfter,
                Voucher = reservation.Voucher,
                VoucherId = reservation.VoucherId,
                VoucherDetail = reservation.VoucherDetail,
                VoucherDetailId = reservation.VoucherDetailId,
                StockReservation = reservation,
                ReferenceType = "StockReservation",
                ReferenceId = reservation.VoucherDetailId?.ToString() ?? reservation.VoucherId.ToString(),
                ReferenceCode = reservation.Voucher?.VoucherCode,
                Actor = reservation.CreatedBy,
                TransactionAt = reservation.CreatedAt,
                MetadataJson = "{\"source\":\"demo-data\",\"ledger\":\"active-reservation\"}"
            });
        }

        foreach (var row in itemLocationRows)
        {
            var key = DemoStockKey(row);
            var ledgerReserved = runningReservedByKey.GetValueOrDefault(key);
            if (ledgerReserved != row.ReservedQty)
            {
                throw new InvalidOperationException(
                    $"Demo reservation ledger for {row.Item!.ItemCode} does not reconcile with the stock snapshot.");
            }
        }
    }

    private static bool IsDemoOutboundFlow(VoucherTypeEnum type)
        => type is VoucherTypeEnum.XuatKho or VoucherTypeEnum.TraNCC or VoucherTypeEnum.ChuyenKho or VoucherTypeEnum.XuatSanXuat;

    private static string DemoStockKey(ItemLocation row)
        => DemoStockKey(row.ItemId, row.LocationId, row.OwnerPartnerId, row.LotNumber, row.ExpiryDate);

    private static string DemoStockKey(VoucherDetail row)
        => DemoStockKey(row.ItemId, row.LocationId!.Value, row.OwnerPartnerId ?? row.Voucher?.OwnerPartnerId, row.LotNumber, row.ExpiryDate);

    private static string DemoStockKey(StockReservation row)
        => DemoStockKey(row.ItemId, row.LocationId, row.OwnerPartnerId, row.LotNumber, row.ExpiryDate);

    private static string DemoStockKey(int itemId, int locationId, int? ownerPartnerId, string? lotNumber, DateTime? expiryDate)
        => $"{itemId}|{locationId}|{ownerPartnerId?.ToString() ?? "NULL"}|{lotNumber ?? ""}|{expiryDate?.Date:yyyyMMdd}";

    private async Task<DemoDataSeedResult> BuildResultAsync(string key, string name, int warehouseId, CancellationToken ct)
    {
        return new DemoDataSeedResult
        {
            DomainKey = key,
            DomainName = name,
            WarehouseId = warehouseId,
            Warehouses = await _db.Warehouses.CountAsync(x => x.WarehouseCode.StartsWith("DEMO-"), ct),
            Locations = await _db.Locations.CountAsync(x => x.LocationCode.StartsWith("DEMO-"), ct),
            Items = await _db.Items.CountAsync(x => x.ItemCode.StartsWith("DEMO-"), ct),
            Vouchers = await _db.Vouchers.CountAsync(ct),
            StockRows = await _db.ItemLocations.CountAsync(ct),
            QualityInspections = await _db.QualityInspections.CountAsync(ct),
            StockCountSheets = await _db.StockCountSheets.CountAsync(ct),
            Reservations = await _db.StockReservations.CountAsync(ct)
        };
    }
}
