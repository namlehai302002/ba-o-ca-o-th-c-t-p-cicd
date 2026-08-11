using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using Xunit;

namespace WMS.Tests;

public sealed class YardTms3PlLaborEnterpriseCompletionTests
{
    [Fact]
    public async Task DockAppointment_ShouldSuggestDoorAndRejectOverlappingWindow()
    {
        await using var db = CreateDb();
        SeedWarehouseAndOwner(db);
        db.DockDoorCapacities.AddRange(
            new DockDoorCapacity
            {
                WarehouseId = 1,
                DockDoor = "DOCK-R1",
                DoorType = DockDoorTypeEnum.Receiving,
                IsRefrigerated = true,
                SlotStartMinutes = 0,
                SlotEndMinutes = 1440,
                MaxAppointments = 1,
                AvgUnloadMinutes = 45
            },
            new DockDoorCapacity
            {
                WarehouseId = 1,
                DockDoor = "DOCK-S1",
                DoorType = DockDoorTypeEnum.Shipping,
                SlotStartMinutes = 0,
                SlotEndMinutes = 1440,
                MaxAppointments = 2,
                AvgUnloadMinutes = 60
            });
        await db.SaveChangesAsync();

        var service = new DockAppointmentService(db);
        var start = VietnamTime.Now.Date.AddHours(9);
        var request = new DockAppointmentRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            Direction = DockAppointmentDirectionEnum.Inbound,
            PlannedStartAt = start,
            PlannedEndAt = start.AddHours(1),
            IsRefrigerated = true,
            GoodsType = "Cold",
            Actor = "yard-manager"
        };

        var suggestion = await service.SuggestDoorAsync(request);
        Assert.Equal("DOCK-R1", suggestion.DockDoor);

        var appointment = await service.CreateAsync(request);
        Assert.Equal(DockAppointmentStatusEnum.Scheduled, appointment.Status);
        Assert.Equal("DOCK-R1", appointment.DockDoor);

        var conflict = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(request));
        Assert.Equal("DOCK_APPOINTMENT_CONFLICT", conflict.Code);

        var checkedIn = await service.CheckInAsync(appointment.DockAppointmentId, null, "gate");
        Assert.Equal(DockAppointmentStatusEnum.CheckedIn, checkedIn.Status);
        Assert.NotNull(checkedIn.CheckInAt);

        var completed = await service.CheckOutAsync(appointment.DockAppointmentId, null, "gate");
        Assert.Equal(DockAppointmentStatusEnum.Completed, completed.Status);
        Assert.NotNull(completed.CheckOutAt);
    }

    [Fact]
    public async Task ThreePlBilling_ShouldRateInvoiceLockAndResolveDispute()
    {
        await using var db = CreateDb();
        SeedWarehouseAndOwner(db);
        var service = new ThreePlEnterpriseBillingService(db);

        var contract = await service.SaveContractAsync(new ThreePlContractRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            ContractCode = "3PLC-TEST",
            ContractName = "Enterprise 3PL contract",
            EffectiveFrom = VietnamTime.Now.Date.AddDays(-1),
            Currency = "VND",
            MinimumCharge = 1000m,
            TaxPercent = 10m,
            DiscountPercent = 5m,
            Actor = "billing"
        });

        await service.SaveContractRateAsync(new ThreePlContractRateRequest
        {
            ContractId = contract.ThreePlContractId,
            ChargeType = ThreePlChargeTypeEnum.Storage,
            RateCode = "STORAGE-TIER",
            ChargeUnit = "pallet-day",
            UnitRate = 100m,
            TierFromQty = 0m,
            TierToQty = 100m,
            IncludedQty = 2m,
            MinimumCharge = 500m,
            SurchargePercent = 10m,
            OffHoursSurcharge = 50m,
            UrgentSurcharge = 70m,
            SlaPenaltyPercent = 5m,
            EffectiveFrom = VietnamTime.Now.Date.AddDays(-1)
        });

        var rating = await service.RateAsync(new ThreePlRatingRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            ChargeType = ThreePlChargeTypeEnum.Storage,
            Quantity = 12m,
            ServiceDate = VietnamTime.Now.Date,
            IsOffHours = true,
            IsUrgent = true,
            SlaBreached = true
        });

        Assert.Equal(1000m, rating.SubtotalAmount);
        Assert.Equal(170m, rating.AdjustmentAmount);
        Assert.Equal(58.5m, rating.DiscountAmount);
        Assert.Equal(111.15m, rating.TaxAmount);
        Assert.Equal(1222.65m, rating.TotalAmount);

        var run = new ThreePlBillingRun
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            RunCode = "RUN-TEST",
            PeriodFrom = VietnamTime.Now.Date,
            PeriodTo = VietnamTime.Now.Date,
            Currency = "VND",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            CreatedBy = "billing"
        };
        run.Charges.Add(new ThreePlBillingCharge
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            ChargeType = ThreePlChargeTypeEnum.Storage,
            SourceType = "Inventory",
            SourceId = "INV-1",
            SourceCode = "INV-1",
            Quantity = 5m,
            UnitRate = 100m,
            Amount = 500m,
            Currency = "VND",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            MetadataJson = "{}",
            CreatedBy = "billing",
            CreatedAt = VietnamTime.Now.Date.AddHours(10)
        });
        db.ThreePlBillingRuns.Add(run);
        await db.SaveChangesAsync();

        var invoice = await service.GenerateInvoiceFromRunAsync(run.ThreePlBillingRunId, null, "billing");
        Assert.Single(invoice.Lines);
        Assert.Equal(ThreePlInvoiceStatusEnum.Draft, invoice.Status);

        var locked = await service.ConfirmInvoiceAsync(invoice.ThreePlInvoiceId, null, "manager");
        Assert.Equal(ThreePlInvoiceStatusEnum.Locked, locked.Status);
        Assert.NotNull(locked.LockedAt);

        var invoiceLine = locked.Lines.Single();
        var lineId = invoiceLine.ThreePlInvoiceLineId;
        var adjustmentBeforeDispute = invoiceLine.AdjustmentAmount;
        var dispute = await service.CreateDisputeAsync(lineId, 100m, "Rate mismatch", "owner");
        Assert.Equal(ThreePlDisputeStatusEnum.Open, dispute.Status);
        Assert.Equal(10, dispute.OwnerPartnerId);

        var resolved = await service.ResolveDisputeAsync(dispute.ThreePlDisputeId, approve: true, approvedAmount: 100m, response: "Approved credit", scopedWarehouseId: 1, actor: "manager");
        Assert.Equal(ThreePlDisputeStatusEnum.Approved, resolved.Status);
        Assert.Equal(100m, resolved.ApprovedAmount);
        var adjustedLine = await db.ThreePlInvoiceLines.AsNoTracking().SingleAsync(x => x.ThreePlInvoiceLineId == lineId);
        Assert.Equal(adjustmentBeforeDispute - 100m, adjustedLine.AdjustmentAmount);
    }

    [Fact]
    public async Task LaborManagement_ShouldCaptureExceptionAndManagerApproval()
    {
        await using var db = CreateDb();
        SeedWarehouseAndOwner(db);
        var service = new LaborManagementService(db);

        var standard = await service.SaveStandardAsync(new LaborStandardRequest
        {
            WarehouseId = 1,
            TaskType = "Pick",
            TaskTypeName = "Picking",
            UnitOfWork = "line",
            ExpectedMinutesPerUnit = 10m,
            MinPerformancePercent = 80m,
            ExcellentPerformancePercent = 120m,
            EffectiveFrom = VietnamTime.Now.Date.AddDays(-1)
        });
        Assert.True(standard.LaborStandardId > 0);

        var activity = await service.StartActivityAsync(new LaborActivityRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            UserName = "staff01",
            ShiftCode = "DAY",
            TaskType = "Pick",
            TaskSourceType = "PickTask",
            TaskSourceId = "PICK-1",
            WorkQuantity = 1m,
            UnitOfWork = "line",
            StartedAt = VietnamTime.Now.AddMinutes(-100),
            WaitingMinutes = 45,
            BacklogAtStart = 12,
            Actor = "staff01"
        });

        var completed = await service.CompleteActivityAsync(activity.LaborActivityId, 1m, null, 1, "staff01");
        Assert.Equal(LaborActivityStatusEnum.Exception, completed.Status);
        Assert.True(completed.IsException);
        Assert.True(completed.ProductivityPercent < 80m);

        var review = await db.LaborExceptionReviews.SingleAsync();
        var approved = await service.ApproveExceptionAsync(review.LaborExceptionReviewId, approve: true, productivityAfter: 95m, incentiveAmount: 25000m, notes: "Traffic at dock", scopedWarehouseId: 1, actor: "manager");

        Assert.Equal(LaborExceptionStatusEnum.Approved, approved.Status);
        Assert.Equal(95m, approved.ProductivityAfter);
        Assert.Equal(25000m, approved.IncentiveAmount);
        Assert.Equal(LaborActivityStatusEnum.Completed, approved.LaborActivity.Status);
    }

    [Fact]
    public async Task LaborManagement_ShouldClampExtremeProductivityForDatabasePrecision()
    {
        await using var db = CreateDb();
        SeedWarehouseAndOwner(db);
        var service = new LaborManagementService(db);

        await service.SaveStandardAsync(new LaborStandardRequest
        {
            WarehouseId = 1,
            TaskType = "Pick",
            TaskTypeName = "Picking",
            UnitOfWork = "line",
            ExpectedMinutesPerUnit = 20m,
            MinPerformancePercent = 80m,
            ExcellentPerformancePercent = 120m,
            EffectiveFrom = VietnamTime.Now.Date.AddDays(-1)
        });

        var startedAt = new DateTime(2026, 6, 9, 8, 0, 0);
        var activity = await service.StartActivityAsync(new LaborActivityRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            UserName = "staff01",
            ShiftCode = "DAY",
            TaskType = "Pick",
            TaskSourceType = "PickTask",
            TaskSourceId = "PICK-EXTREME-PRODUCTIVITY",
            WorkQuantity = 100m,
            UnitOfWork = "line",
            StartedAt = startedAt,
            Actor = "staff01"
        });

        activity.EndedAt = startedAt.AddMilliseconds(1);
        await db.SaveChangesAsync();

        var completed = await service.CompleteActivityAsync(activity.LaborActivityId, 100m, null, 1, "staff01");
        Assert.Equal(999.9999m, completed.ProductivityPercent);
    }

    [Fact]
    public async Task LaborManagement_ShouldReturnExistingActivityWhenParallelCaptureCreatesSameSource()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = CreateSqliteOptions(connection);
        await using var db = new AppDbContext(options);
        db.SkipAudit = true;
        await CreateLaborSqliteSchemaAsync(db);

        var service = new LaborManagementService(db, new InjectLaborActivityCollisionUnitOfWork(db, options, duplicateSource: true));
        var activity = await service.StartActivityAsync(new LaborActivityRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            UserName = "staff01",
            ShiftCode = "DAY",
            TaskType = "Pick",
            TaskSourceType = "PickTask",
            TaskSourceId = "PICK-CONCURRENT-1",
            WorkQuantity = 1m,
            UnitOfWork = "line",
            StartedAt = new DateTime(2026, 6, 3, 8, 0, 0),
            Actor = "staff01"
        });

        Assert.Equal("parallel-capture", activity.CreatedBy);
        Assert.Equal("PICK-CONCURRENT-1", activity.TaskSourceId);
        Assert.Equal(1, await db.LaborActivities.AsNoTracking().CountAsync(x => x.TaskSourceType == "PickTask" && x.TaskSourceId == "PICK-CONCURRENT-1"));
    }

    [Fact]
    public async Task LaborManagement_ShouldRetryActivityCodeWhenParallelManualActivityUsesSameCode()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = CreateSqliteOptions(connection);
        await using var db = new AppDbContext(options);
        db.SkipAudit = true;
        await CreateLaborSqliteSchemaAsync(db);

        var service = new LaborManagementService(db, new InjectLaborActivityCollisionUnitOfWork(db, options, duplicateSource: false));
        var activity = await service.StartActivityAsync(new LaborActivityRequest
        {
            WarehouseId = 1,
            OwnerPartnerId = 10,
            UserName = "staff02",
            ShiftCode = "DAY",
            TaskType = "CycleCount",
            TaskSourceType = "Manual",
            WorkQuantity = 1m,
            UnitOfWork = "task",
            StartedAt = new DateTime(2026, 6, 3, 8, 0, 0),
            Actor = "staff02"
        });

        Assert.Equal("LAB-20260603-1-0002", activity.ActivityCode);
        Assert.Equal(2, await db.LaborActivities.AsNoTracking().CountAsync(x => x.ActivityCode.StartsWith("LAB-20260603-1-")));
    }

    [Fact]
    public void Enterprise567StaticArtifacts_ShouldExposeUiExportsMigrationAndChecklist()
    {
        var root = FindRepositoryRoot();
        var controller = Read(Path.Combine(root, "Controllers", "OperationsController.Enterprise567.cs"));
        var dockBoard = Read(Path.Combine(root, "Views", "Operations", "DockBoard.cshtml"));
        var yard = Read(Path.Combine(root, "Views", "Operations", "YardManagement.cshtml"));
        var contracts = Read(Path.Combine(root, "Views", "Operations", "ThreePlContracts.cshtml"));
        var invoice = Read(Path.Combine(root, "Views", "Operations", "ThreePlInvoiceDetails.cshtml"));
        var portal = Read(Path.Combine(root, "Views", "Operations", "ThreePlClientPortal.cshtml"));
        var labor = Read(Path.Combine(root, "Views", "Operations", "LaborProductivity.cshtml"));
        var css = Read(Path.Combine(root, "wwwroot", "css", "site.css"));
        var tasks = Read(Path.Combine(root, "FINAL_WMS_ENTERPRISE_QA_REPORT.md"));

        Assert.Contains("CreateDockAppointment", controller, StringComparison.Ordinal);
        Assert.Contains("UploadYardVisitEvidence", controller, StringComparison.Ordinal);
        Assert.Contains("GenerateThreePlInvoice", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveThreePlDispute", controller, StringComparison.Ordinal);
        Assert.Contains("ExportLaborProductivity", controller, StringComparison.Ordinal);
        Assert.True(controller.Split("[HttpPost]").Length - 1 <= controller.Split("[ValidateAntiForgeryToken]").Length - 1,
            "Every POST in the enterprise 5-7 controller must carry anti-forgery.");

        Assert.Contains("ExportDockAppointments", dockBoard, StringComparison.Ordinal);
        Assert.Contains("yardops-evidence-form", yard, StringComparison.Ordinal);
        Assert.Contains("SaveThreePlContract", contracts, StringComparison.Ordinal);
        Assert.Contains("ExportThreePlInvoiceExcel", invoice, StringComparison.Ordinal);
        Assert.Contains("ExportThreePlInvoicePdf", invoice, StringComparison.Ordinal);
        Assert.Contains("OwnerPartnerId", portal, StringComparison.Ordinal);
        Assert.Contains("ExportLaborProductivity", labor, StringComparison.Ordinal);
        Assert.Contains(".yardops-inline-card", css, StringComparison.Ordinal);
        Assert.Contains("CompleteYard3PlLaborEnterprise", string.Join("\n", Directory.GetFiles(Path.Combine(root, "Migrations")).Select(Path.GetFileName)), StringComparison.Ordinal);

        foreach (var code in new[] { "YARD-01", "YARD-02", "YARD-03", "YARD-04", "CAR-01", "CAR-02", "3PL-01", "3PL-02", "3PL-03", "3PL-04", "3PL-05", "3PL-06", "3PL-07", "3PL-08", "LAB-01", "LAB-02", "LAB-03", "LAB-04", "LAB-05" })
        {
            Assert.Contains($"- [x] `{code}`", tasks, StringComparison.Ordinal);
        }
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static DbContextOptions<AppDbContext> CreateSqliteOptions(SqliteConnection connection)
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    private static async Task CreateLaborSqliteSchemaAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE LaborActivities (
                LaborActivityId INTEGER PRIMARY KEY AUTOINCREMENT,
                ActivityCode TEXT NOT NULL,
                WarehouseId INTEGER NOT NULL,
                ZoneId INTEGER NULL,
                UserId INTEGER NULL,
                UserName TEXT NOT NULL,
                ShiftCode TEXT NOT NULL,
                TaskType TEXT NOT NULL,
                TaskSourceType TEXT NOT NULL,
                TaskSourceId TEXT NULL,
                TaskSourceCode TEXT NULL,
                OwnerPartnerId INTEGER NULL,
                ItemClass TEXT NULL,
                WorkQuantity TEXT NOT NULL,
                UnitOfWork TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT NULL,
                Status INTEGER NOT NULL,
                ExpectedMinutes TEXT NOT NULL,
                ActualMinutes TEXT NOT NULL DEFAULT '0',
                ProductivityPercent TEXT NOT NULL DEFAULT '0',
                WaitingMinutes INTEGER NOT NULL,
                BacklogAtStart INTEGER NOT NULL,
                IsException INTEGER NOT NULL DEFAULT 0,
                ExceptionReason TEXT NULL,
                CreatedBy TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX UX_LaborActivities_Code ON LaborActivities (ActivityCode);
            CREATE UNIQUE INDEX UX_LaborActivities_Source ON LaborActivities (TaskSourceType, TaskSourceId) WHERE TaskSourceId IS NOT NULL;
            CREATE TABLE LaborStandards (
                LaborStandardId INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskType TEXT NOT NULL,
                TaskTypeName TEXT NOT NULL,
                UnitOfWork TEXT NOT NULL,
                ExpectedMinutesPerUnit TEXT NOT NULL,
                ExpectedUnitsPerHour TEXT NOT NULL,
                MinPerformancePercent TEXT NOT NULL,
                ExcellentPerformancePercent TEXT NOT NULL,
                WarehouseId INTEGER NULL,
                ZoneId INTEGER NULL,
                ItemClass TEXT NULL,
                EffectiveFrom TEXT NOT NULL,
                EffectiveTo TEXT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NULL
            );
            """);
    }

    private static void SeedWarehouseAndOwner(AppDbContext db)
    {
        db.Warehouses.Add(new Warehouse { WarehouseId = 1, WarehouseCode = "WH01", WarehouseName = "Main warehouse" });
        db.Partners.Add(new Partner
        {
            PartnerId = 10,
            PartnerCode = "OWN01",
            PartnerName = "Owner 01",
            PartnerType = PartnerTypeEnum.Customer,
            IsThreePlClient = true,
            IsActive = true
        });
        db.SaveChanges();
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate WMS.sln from test output directory.");
    }

    private static string Read(string path)
        => File.ReadAllText(path);

    private sealed class InjectLaborActivityCollisionUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly bool _duplicateSource;
        private bool _injected;

        public InjectLaborActivityCollisionUnitOfWork(AppDbContext db, DbContextOptions<AppDbContext> options, bool duplicateSource)
        {
            _db = db;
            _options = options;
            _duplicateSource = duplicateSource;
        }

        public bool HasActiveTransaction => false;

        public Task BeginTransactionAsync(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var pending = _db.ChangeTracker.Entries<LaborActivity>()
                .SingleOrDefault(x => x.State == EntityState.Added)
                ?.Entity;
            if (!_injected && pending != null)
            {
                _injected = true;
                await using var other = new AppDbContext(_options);
                other.SkipAudit = true;
                other.LaborActivities.Add(new LaborActivity
                {
                    ActivityCode = pending.ActivityCode,
                    WarehouseId = pending.WarehouseId,
                    ZoneId = pending.ZoneId,
                    UserId = pending.UserId,
                    UserName = pending.UserName,
                    ShiftCode = pending.ShiftCode,
                    TaskType = pending.TaskType,
                    TaskSourceType = _duplicateSource ? pending.TaskSourceType : "InjectedManual",
                    TaskSourceId = _duplicateSource ? pending.TaskSourceId : Guid.NewGuid().ToString("N"),
                    TaskSourceCode = pending.TaskSourceCode,
                    OwnerPartnerId = pending.OwnerPartnerId,
                    ItemClass = pending.ItemClass,
                    WorkQuantity = pending.WorkQuantity,
                    UnitOfWork = pending.UnitOfWork,
                    StartedAt = pending.StartedAt,
                    Status = LaborActivityStatusEnum.InProgress,
                    ExpectedMinutes = pending.ExpectedMinutes,
                    WaitingMinutes = pending.WaitingMinutes,
                    BacklogAtStart = pending.BacklogAtStart,
                    CreatedBy = "parallel-capture",
                    CreatedAt = VietnamTime.Now
                });
                await other.SaveChangesAsync(cancellationToken);
            }

            return await _db.SaveChangesAsync(cancellationToken);
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
