using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public sealed class DashboardCommandCenterTests
{
    public static IEnumerable<object[]> RoleProcessCases()
    {
        var all = new[] { "inbound", "outbound", "movement", "count-quality", "transfer", "return" };
        yield return new object[] { WmsRoles.Admin, all };
        yield return new object[] { WmsRoles.Manager, all };
        yield return new object[] { WmsRoles.Staff, all };
        yield return new object[] { WmsRoles.InboundStaff, new[] { "inbound", "return" } };
        yield return new object[] { WmsRoles.OutboundStaff, new[] { "outbound", "return" } };
        yield return new object[] { WmsRoles.InventoryStaff, new[] { "movement", "count-quality", "transfer" } };
        yield return new object[] { WmsRoles.TransportStaff, new[] { "outbound" } };
        yield return new object[] { WmsRoles.ReportViewer, all };
        yield return new object[] { WmsRoles.Viewer, Array.Empty<string>() };
    }

    [Fact]
    public async Task BuildAsync_ShouldApplyWarehouseAndOwnerScopeAndSortBySeverity()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 16, 10, 0, 0);
        SeedScope(db);
        db.Vouchers.AddRange(
            NewInboundVoucher(101, "PN-OWNER-10-LATE", 1, 10, now.AddHours(-12), InboundStatusEnum.PendingApproval, 100),
            NewInboundVoucher(102, "PN-OWNER-20", 1, 20, now.AddHours(-12), InboundStatusEnum.PendingApproval, 100),
            NewInboundVoucher(103, "PN-WAREHOUSE-2", 2, 10, now.AddHours(-12), InboundStatusEnum.PendingApproval, 100),
            NewOutboundVoucher(104, "PX-OWNER-10-TODAY", 1, 10, now.Date, FulfillmentStatusEnum.Picking));
        db.OperationExceptionCases.Add(new OperationExceptionCase
        {
            OperationExceptionCaseId = 1,
            ExceptionKey = "GLOBAL|1",
            CategoryKey = "inventory",
            CategoryLabel = "Ngoại lệ không có owner",
            WarehouseId = 1,
            Status = OperationExceptionStatusEnum.Open,
            FirstDetectedAt = now.AddHours(-20),
            LastDetectedAt = now
        });
        await db.SaveChangesAsync();

        var service = new DashboardCommandCenterService(db);
        var model = await service.BuildAsync(new DashboardCommandCenterRequest
        {
            Role = WmsRoles.Manager,
            WarehouseId = 1,
            OwnerPartnerIds = new[] { 10 },
            Now = now,
            WorkItemLimit = 20
        });

        Assert.Equal("WH-01 - Kho 01", model.WarehouseScopeLabel);
        Assert.Equal("Chủ hàng A", model.OwnerScopeLabel);
        Assert.Contains(model.WorkItems, item => item.ReferenceCode == "PN-OWNER-10-LATE");
        Assert.Contains(model.WorkItems, item => item.ReferenceCode == "PX-OWNER-10-TODAY");
        Assert.DoesNotContain(model.WorkItems, item => item.ReferenceCode == "PN-OWNER-20");
        Assert.DoesNotContain(model.WorkItems, item => item.ReferenceCode == "PN-WAREHOUSE-2");
        Assert.DoesNotContain(model.WorkItems, item => item.KindKey == "exception");
        Assert.Equal("PN-OWNER-10-LATE", model.WorkItems[0].ReferenceCode);
        Assert.Equal("critical", model.WorkItems[0].SeverityKey);
        Assert.Equal("overdue", model.WorkItems[0].StateKey);
    }

    [Fact]
    public async Task BuildAsync_ShouldUseExclusiveEndOfBusinessDayAndAvoidDoubleCounting()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 16, 9, 0, 0);
        SeedScope(db);
        db.Vouchers.AddRange(
            NewInboundVoucher(201, "PN-AT-START", 1, 10, now.Date, InboundStatusEnum.Approved, 50),
            NewInboundVoucher(202, "PN-BEFORE-END", 1, 10, now.Date.AddDays(1).AddTicks(-1), InboundStatusEnum.Approved, 50),
            NewInboundVoucher(203, "PN-AT-END", 1, 10, now.Date.AddDays(1), InboundStatusEnum.Approved, 50),
            NewInboundVoucher(204, "PN-COMPLETED", 1, 10, now.Date.AddHours(8), InboundStatusEnum.Completed, 50, completedAt: now.Date.AddHours(8)));
        await db.SaveChangesAsync();

        var service = new DashboardCommandCenterService(db);
        var model = await service.BuildAsync(new DashboardCommandCenterRequest
        {
            Role = WmsRoles.Manager,
            WarehouseId = 1,
            OwnerPartnerIds = new[] { 10 },
            Now = now,
            WorkItemLimit = 20
        });

        var inbound = Assert.Single(model.ProcessSummaries, summary => summary.Key == "inbound");
        Assert.Equal(3, inbound.DueToday);
        Assert.Equal(1, inbound.CompletedToday);
        Assert.Equal(3, inbound.Open);
        Assert.Equal(1, model.CompletedTodayCount);
        Assert.Equal(2, model.WorkItems.Count(item => item.KindKey == "inbound"));
    }

    [Fact]
    public async Task BuildAsync_ShouldApplyReproducibleQueueFiltersWithoutChangingRawCounters()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 16, 10, 0, 0);
        SeedScope(db);
        db.Vouchers.AddRange(
            NewInboundVoucher(301, "PN-CRITICAL", 1, 10, now.AddHours(-12), InboundStatusEnum.PendingApproval, 100),
            NewInboundVoucher(302, "PN-MEDIUM", 1, 10, now.AddHours(6), InboundStatusEnum.Receiving, 50));
        await db.SaveChangesAsync();

        var service = new DashboardCommandCenterService(db);
        var model = await service.BuildAsync(new DashboardCommandCenterRequest
        {
            Role = WmsRoles.Manager,
            WarehouseId = 1,
            OwnerPartnerIds = new[] { 10 },
            Now = now,
            Severity = " CRITICAL ",
            WorkState = " overdue ",
            WorkItemLimit = 20
        });

        Assert.Equal(2, model.TotalWorkItems);
        Assert.Equal(1, model.CriticalCount);
        Assert.Equal(1, model.FilteredWorkItems);
        var item = Assert.Single(model.WorkItems);
        Assert.Equal("PN-CRITICAL", item.ReferenceCode);
        Assert.Equal("critical", model.SelectedSeverity);
        Assert.Equal("overdue", model.SelectedWorkState);
    }

    [Fact]
    public async Task BuildAsync_ReportViewerShouldReceiveReadOnlyActions()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 16, 10, 0, 0);
        SeedScope(db);
        db.Vouchers.Add(NewInboundVoucher(
            401,
            "PN-REPORT",
            1,
            10,
            now.AddHours(2),
            InboundStatusEnum.PendingApproval,
            50));
        await db.SaveChangesAsync();

        var service = new DashboardCommandCenterService(db);
        var model = await service.BuildAsync(new DashboardCommandCenterRequest
        {
            Role = WmsRoles.ReportViewer,
            WarehouseId = 1,
            OwnerPartnerIds = new[] { 10 },
            Now = now
        });

        var item = Assert.Single(model.WorkItems, work => work.ReferenceCode == "PN-REPORT");
        Assert.False(item.CanAct);
        Assert.Equal("Xem chi tiết", item.ActionLabel);
    }

    [Theory]
    [MemberData(nameof(RoleProcessCases))]
    public async Task BuildAsync_ShouldExposeOnlyProcessesAssignedToRole(
        string role,
        string[] expectedProcessKeys)
    {
        await using var db = CreateDb();
        SeedScope(db);
        await db.SaveChangesAsync();

        var service = new DashboardCommandCenterService(db);
        var model = await service.BuildAsync(new DashboardCommandCenterRequest
        {
            Role = role,
            WarehouseId = 1,
            Now = new DateTime(2026, 7, 16, 10, 0, 0)
        });

        Assert.Equal(expectedProcessKeys, model.ProcessSummaries.Select(summary => summary.Key));
    }

    [Fact]
    public async Task BuildAsync_ShouldReconcileAllProcessSummariesWithHandCalculatedFixture()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 16, 10, 0, 0);
        SeedScope(db);

        db.Vouchers.AddRange(
            NewInboundVoucher(501, "PN-DUE", 1, 10, now.Date.AddHours(12), InboundStatusEnum.Approved, 50),
            NewInboundVoucher(502, "PN-DONE", 1, 10, now.Date.AddDays(1), InboundStatusEnum.Completed, 50, now.Date.AddHours(8)),
            NewOutboundVoucher(503, "PX-DUE", 1, 10, now.Date, FulfillmentStatusEnum.Picking),
            NewPostedOutboundVoucher(504, "PX-DONE", 1, 10, now.Date.AddDays(1), now.Date.AddHours(9)),
            NewTransferVoucher(505, "CK-DUE", 1, 10, now.Date.AddHours(15), posted: false),
            NewTransferVoucher(506, "CK-DONE", 1, 10, now.Date.AddDays(1), posted: true, completedAt: now.Date.AddHours(7)),
            NewReturnVoucher(507, "KT-DUE", 1, 10, VoucherTypeEnum.KhachTra, now.Date.AddHours(14), posted: false),
            NewReturnVoucher(508, "TNCC-DONE", 1, 10, VoucherTypeEnum.TraNCC, now.Date.AddDays(1), posted: true, completedAt: now.Date.AddHours(6)));

        db.MovementTasks.AddRange(
            NewMovementTask(601, "MV-DUE", 1, 10, MovementTaskStatusEnum.Pending, now.Date.AddHours(13)),
            NewMovementTask(602, "MV-DONE", 1, 10, MovementTaskStatusEnum.Completed, now.Date.AddDays(1), now.Date.AddHours(8)),
            NewMovementTask(603, "MV-LATE", 1, 10, MovementTaskStatusEnum.Assigned, now.Date.AddDays(-1)));

        db.StockCountSheets.AddRange(
            NewCountSheet(701, "CC-DUE", 1, now.Date, StockCountStatusEnum.Draft),
            NewCountSheet(702, "CC-DONE", 1, now.Date.AddDays(-1), StockCountStatusEnum.Approved, now.Date.AddHours(8)),
            NewCountSheet(703, "CC-LATE", 1, now.Date.AddDays(-1), StockCountStatusEnum.Counting));

        db.QualityInspections.AddRange(
            NewQualityInspection(801, 501, 1, QualityStatusEnum.Pending, now.Date.AddHours(9)),
            NewQualityInspection(802, 501, 1, QualityStatusEnum.Passed, now.Date.AddDays(-1), now.Date.AddHours(8)),
            NewQualityInspection(803, 501, 1, QualityStatusEnum.Inspecting, now.AddHours(-15)));
        await db.SaveChangesAsync();

        var service = new DashboardCommandCenterService(db);
        var model = await service.BuildAsync(new DashboardCommandCenterRequest
        {
            Role = WmsRoles.Manager,
            WarehouseId = 1,
            Now = now,
            WorkItemLimit = 100
        });

        AssertProcess(model, "inbound", dueToday: 1, completedToday: 1, open: 1, overdue: 0);
        AssertProcess(model, "outbound", dueToday: 1, completedToday: 1, open: 1, overdue: 0);
        AssertProcess(model, "movement", dueToday: 1, completedToday: 1, open: 2, overdue: 1);
        AssertProcess(model, "count-quality", dueToday: 2, completedToday: 2, open: 4, overdue: 2);
        AssertProcess(model, "transfer", dueToday: 1, completedToday: 1, open: 1, overdue: 0);
        AssertProcess(model, "return", dueToday: 1, completedToday: 1, open: 1, overdue: 0);
        Assert.Equal(7, model.CompletedTodayCount);
    }

    private static void AssertProcess(
        DashboardCommandCenterViewModel model,
        string key,
        int dueToday,
        int completedToday,
        int open,
        int overdue)
    {
        var summary = Assert.Single(model.ProcessSummaries, item => item.Key == key);
        Assert.Equal(dueToday, summary.DueToday);
        Assert.Equal(completedToday, summary.CompletedToday);
        Assert.Equal(open, summary.Open);
        Assert.Equal(overdue, summary.Overdue);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("dashboard-command-center-" + Guid.NewGuid())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedScope(AppDbContext db)
    {
        db.Warehouses.AddRange(
            new Warehouse { WarehouseId = 1, WarehouseCode = "WH-01", WarehouseName = "Kho 01", IsActive = true },
            new Warehouse { WarehouseId = 2, WarehouseCode = "WH-02", WarehouseName = "Kho 02", IsActive = true });
        db.Partners.AddRange(
            new Partner { PartnerId = 10, PartnerCode = "OWNER-A", PartnerName = "Chủ hàng A", IsActive = true },
            new Partner { PartnerId = 20, PartnerCode = "OWNER-B", PartnerName = "Chủ hàng B", IsActive = true });
    }

    private static Voucher NewInboundVoucher(
        long id,
        string code,
        int warehouseId,
        int ownerId,
        DateTime expectedAt,
        InboundStatusEnum status,
        int priority,
        DateTime? completedAt = null)
        => new()
        {
            VoucherId = id,
            VoucherCode = code,
            VoucherType = VoucherTypeEnum.NhapKho,
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerId,
            VoucherDate = expectedAt.Date,
            ExpectedArrivalAt = expectedAt,
            DockAppointmentEnd = expectedAt,
            InboundStatus = status,
            Priority = priority,
            TotalLines = 2,
            IsPosted = status == InboundStatusEnum.Completed,
            CompletedAt = completedAt,
            CreatedAt = expectedAt.AddHours(-2),
            SubmittedAt = expectedAt.AddHours(-1),
            CreatedBy = "audit.test"
        };

    private static Voucher NewOutboundVoucher(
        long id,
        string code,
        int warehouseId,
        int ownerId,
        DateTime dueDate,
        FulfillmentStatusEnum status)
        => new()
        {
            VoucherId = id,
            VoucherCode = code,
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerId,
            VoucherDate = dueDate.Date,
            RequestedDeliveryDate = dueDate.Date,
            FulfillmentStatus = status,
            TotalLines = 1,
            CreatedAt = dueDate.Date.AddHours(7),
            CreatedBy = "audit.test"
        };

    private static Voucher NewPostedOutboundVoucher(
        long id,
        string code,
        int warehouseId,
        int ownerId,
        DateTime dueDate,
        DateTime completedAt)
        => new()
        {
            VoucherId = id,
            VoucherCode = code,
            VoucherType = VoucherTypeEnum.XuatKho,
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerId,
            VoucherDate = dueDate.Date,
            RequestedDeliveryDate = dueDate,
            FulfillmentStatus = FulfillmentStatusEnum.Shipped,
            IsPosted = true,
            ShippedAt = completedAt,
            CompletedAt = completedAt,
            CreatedAt = dueDate.Date.AddHours(7),
            CreatedBy = "audit.test"
        };

    private static Voucher NewTransferVoucher(
        long id,
        string code,
        int warehouseId,
        int ownerId,
        DateTime dueDate,
        bool posted,
        DateTime? completedAt = null)
        => new()
        {
            VoucherId = id,
            VoucherCode = code,
            VoucherType = VoucherTypeEnum.ChuyenKho,
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerId,
            VoucherDate = dueDate.Date,
            RequestedDeliveryDate = dueDate,
            IsPosted = posted,
            CompletedAt = completedAt,
            CreatedAt = dueDate.Date.AddHours(7),
            CreatedBy = "audit.test"
        };

    private static Voucher NewReturnVoucher(
        long id,
        string code,
        int warehouseId,
        int ownerId,
        VoucherTypeEnum type,
        DateTime dueDate,
        bool posted,
        DateTime? completedAt = null)
        => new()
        {
            VoucherId = id,
            VoucherCode = code,
            VoucherType = type,
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerId,
            VoucherDate = dueDate.Date,
            ExpectedArrivalAt = type == VoucherTypeEnum.KhachTra ? dueDate : null,
            RequestedDeliveryDate = type == VoucherTypeEnum.TraNCC ? dueDate : null,
            IsPosted = posted,
            CompletedAt = completedAt,
            CreatedAt = dueDate.Date.AddHours(7),
            CreatedBy = "audit.test"
        };

    private static MovementTask NewMovementTask(
        long id,
        string code,
        int warehouseId,
        int ownerId,
        MovementTaskStatusEnum status,
        DateTime dueAt,
        DateTime? completedAt = null)
        => new()
        {
            MovementTaskId = id,
            TaskCode = code,
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerId,
            ItemId = 1,
            SourceLocationId = 1,
            DestinationLocationId = 2,
            PlannedQty = 1,
            Status = status,
            DueAt = dueAt,
            CompletedAt = completedAt,
            CreatedAt = dueAt.AddHours(-2),
            CreatedBy = "audit.test"
        };

    private static StockCountSheet NewCountSheet(
        long id,
        string code,
        int warehouseId,
        DateTime countDate,
        StockCountStatusEnum status,
        DateTime? approvedAt = null)
        => new()
        {
            StockCountSheetId = id,
            SheetCode = code,
            WarehouseId = warehouseId,
            CountDate = countDate,
            Status = status,
            ApprovedAt = approvedAt,
            CreatedAt = countDate,
            CreatedBy = "audit.test"
        };

    private static QualityInspection NewQualityInspection(
        long id,
        long voucherId,
        int warehouseId,
        QualityStatusEnum status,
        DateTime createdAt,
        DateTime? inspectedAt = null)
        => new()
        {
            QualityInspectionId = id,
            VoucherId = voucherId,
            ItemId = 1,
            WarehouseId = warehouseId,
            TotalQty = 10,
            SampleQty = 1,
            OverallResult = status,
            CreatedAt = createdAt,
            InspectedAt = inspectedAt
        };
}
