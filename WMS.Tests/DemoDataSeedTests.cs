using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Sockets;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public class DemoDataSeedTests
{
    [Fact]
    public void DemoDataOptions_ShouldExposeThreeInternalWarehouseDomains()
    {
        using var db = CreateInMemoryDb(nameof(DemoDataOptions_ShouldExposeThreeInternalWarehouseDomains));
        var service = new DemoDataSeedService(db);

        var options = service.GetOptions();

        Assert.Collection(options,
            option => Assert.Equal("Demo kho thiết bị IT", option.Title),
            option => Assert.Equal("Demo kho vật tư y tế", option.Title),
            option => Assert.Equal("Demo kho thương mại điện tử", option.Title));
        Assert.All(options, option => Assert.DoesNotContain("thuê kho", option.Title + option.Subtitle, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyAsync_ShouldClearWarehouseDataButPreserveLoginAuthorizationData()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Services", "DemoDataSeedService.cs"));

        Assert.Contains("BeginTransactionAsync", source);
        Assert.Contains("CommitAsync", source);
        Assert.Contains("RollbackAsync", source);
        Assert.Contains("GetAuthScopedWarehouseIdsAsync", source);
        Assert.Contains("preservedWarehouseIds", source);
        Assert.Contains("preservedZoneIds", source);
        Assert.Contains("preservedPartnerIds", source);
        Assert.Contains("AppUsers", source);
        Assert.Contains("AppUserOwnerScopes", source);
        Assert.Contains("UserZoneAssignments", source);

        Assert.DoesNotContain("_db.AppUsers.ExecuteDelete", source);
        Assert.DoesNotContain("_db.AppRoles.ExecuteDelete", source);
        Assert.DoesNotContain("_db.Permissions.ExecuteDelete", source);
        Assert.DoesNotContain("_db.RolePermissions.ExecuteDelete", source);
        Assert.DoesNotContain("_db.AppUserOwnerScopes.ExecuteDelete", source);
        Assert.DoesNotContain("_db.UserZoneAssignments.ExecuteDelete", source);
        Assert.DoesNotContain("_db.LoginAuditLogs.ExecuteDelete", source);
        Assert.DoesNotContain("_db.MfaLoginChallenges.ExecuteDelete", source);
        Assert.DoesNotContain("_db.LoginHelpRequests.ExecuteDelete", source);
    }

    [Fact]
    public void DemoSeedArtifacts_ShouldExistAndAvoidThrowawayDataLabels()
    {
        var root = FindRepositoryRoot();
        var requiredFiles = new[]
        {
            Path.Combine(root, "scripts", "seed_demo_it_inventory.sql"),
            Path.Combine(root, "scripts", "seed_demo_medical_inventory.sql"),
            Path.Combine(root, "scripts", "seed_demo_ecommerce_inventory.sql"),
            Path.Combine(root, "DEMO_SCENARIOS.md"),
            Path.Combine(root, "Views", "System", "DemoData.cshtml")
        };

        foreach (var file in requiredFiles)
        {
            Assert.True(File.Exists(file), $"Missing demo artifact: {file}");
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("ForFun", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lorem", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[object Object]", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("undefined", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NaN", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DemoDataPage_ShouldUseEnterpriseAsyncConfirmInsteadOfNativeConfirm()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "System", "DemoData.cshtml"));

        Assert.Contains("window.enterpriseConfirm", view, StringComparison.Ordinal);
        Assert.Contains("form.dataset.demoConfirmed", view, StringComparison.Ordinal);
        Assert.Contains("HTMLFormElement.prototype.submit.call(form)", view, StringComparison.Ordinal);
        Assert.Contains("data-no-submit-loading=\"true\"", view, StringComparison.Ordinal);
        Assert.Contains("resetDemoButtonIdle(button)", view, StringComparison.Ordinal);
        Assert.Contains("setDemoButtonsLoading(button)", view, StringComparison.Ordinal);
        Assert.Contains("resetDemoButtonsIdle()", view, StringComparison.Ordinal);
        Assert.Contains("getDemoButtons()", view, StringComparison.Ordinal);
        Assert.Contains("delete form.dataset.demoConfirmed", view, StringComparison.Ordinal);
        Assert.Contains("Xác nhận nạp dữ liệu demo", view, StringComparison.Ordinal);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(view, "<script>", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count == 1, "DemoData page must render exactly one script block.");
        Assert.True(System.Text.RegularExpressions.Regex.Matches(view, "</script>", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count == 1, "DemoData page must close exactly one script block.");
        Assert.DoesNotContain("window.confirm(message)", view, StringComparison.Ordinal);
        Assert.DoesNotContain("form.requestSubmit()", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyDemoData_ShouldRefreshScopedWarehouseClaimAfterSuccessfulSeed()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "Controllers", "SystemController.cs"));

        Assert.Contains("RefreshScopedWarehouseClaimAsync(result.WarehouseId)", controller, StringComparison.Ordinal);
        Assert.Contains("User.FindFirst(\"WarehouseId\")", controller, StringComparison.Ordinal);
        Assert.Contains("CookieAuthenticationDefaults.AuthenticationScheme", controller, StringComparison.Ordinal);
        Assert.Contains("HttpContext.SignInAsync", controller, StringComparison.Ordinal);
        Assert.Contains("x.Type != \"WarehouseId\"", controller, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_ShouldRejectConcurrentSeedRequest()
    {
        using var db = CreateInMemoryDb(nameof(ApplyAsync_ShouldRejectConcurrentSeedRequest));
        var service = new DemoDataSeedService(db);
        var gateField = typeof(DemoDataSeedService).GetField("ApplyGate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(gateField);
        var gate = Assert.IsType<SemaphoreSlim>(gateField!.GetValue(null));
        Assert.True(await gate.WaitAsync(0));

        try
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApplyAsync(DemoDataDomain.ItInventory, "demo.admin"));

            Assert.Equal("DEMO_SEED_IN_PROGRESS", ex.Code);
            Assert.Contains("đang nạp dữ liệu demo", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task ApplyAsync_ShouldRunThreeDomainsReplaceWarehouseDataAndPreserveLoginData()
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
            WarehouseId = 9991,
            WarehouseCode = "DEMO-OLD-KHO",
            WarehouseName = "Kho demo cũ cần thay thế",
            Address = "Dữ liệu demo cũ",
            ManagerName = "Quản trị demo",
            Phone = "0900000000",
            IsActive = true
        });
        db.StockSnapshotRuns.Add(new StockSnapshotRun
        {
            StockSnapshotRunId = 9991,
            WarehouseId = 9991,
            SnapshotDate = new DateTime(2026, 7, 1),
            CreatedBy = "demo.admin",
            TotalItems = 0,
            TotalValue = 0,
            Status = "Completed"
        });
        db.ItemCategories.Add(new ItemCategory
        {
            CategoryId = 9991,
            CategoryCode = "DEMO-OLD-CAT",
            CategoryName = "Old demo category",
            IsActive = true
        });
        db.InspectionPlanTemplates.Add(new InspectionPlanTemplate
        {
            InspectionPlanTemplateId = 9991,
            PlanName = "AUDIT_TEST_OLD_DEMO_PLAN",
            ItemCategoryId = 9991,
            SampleSizeFormula = "Percentage",
            SampleSizeValue = 10,
            IsActive = true,
            CreatedBy = "demo.admin"
        });
        db.AppRoles.Add(new AppRole
        {
            RoleId = 1,
            RoleName = "Admin",
            Description = "Quản trị hệ thống"
        });
        db.AppUsers.Add(new AppUser
        {
            UserId = 1,
            UserName = "demo.admin",
            FullName = "Trần Minh Khôi",
            Email = "demo.admin@local.test",
            PasswordHash = "hash-for-test-only",
            RoleId = 1,
            IsActive = true,
            WarehouseId = 9991
        });
        await db.SaveChangesAsync();

        var service = new DemoDataSeedService(db);

        var it = await service.ApplyAsync(DemoDataDomain.ItInventory, "demo.admin");
        Assert.Equal("it", it.DomainKey);
        Assert.Equal(1, it.Warehouses);
        Assert.True(it.Items >= 8);
        Assert.True(it.Vouchers >= 2);
        Assert.True(it.QualityInspections >= 1);
        Assert.True(it.StockCountSheets >= 1);
        await AssertActorScopedToDemoWarehouseAsync(db, "DEMO-IT-KHO");
        await AssertDemoWarehouseIntegrityAsync(db, "DEMO-IT-KHO");
        await AssertInventoryLedgerBalancesValidAsync(db);
        Assert.Equal(1, await db.AppUsers.CountAsync());
        Assert.Equal(1, await db.AppRoles.CountAsync());
        Assert.True(await db.Warehouses.AnyAsync(x => x.WarehouseName == "Kho thiết bị IT"));
        Assert.False(await db.Warehouses.AnyAsync(x => x.WarehouseCode == "DEMO-OLD-KHO"));
        Assert.False(await db.StockSnapshotRuns.AnyAsync(x => x.StockSnapshotRunId == 9991));
        Assert.False(await db.InspectionPlanTemplates.AnyAsync(x => x.InspectionPlanTemplateId == 9991));
        Assert.True(await db.Items.AnyAsync(x => x.ItemCode == "DEMO-IT-LAP-DELL-5420"));
        Assert.True(await db.SerialNumbers.AnyAsync(x => x.SerialCode.StartsWith("DEMO-IT-DL5420")));
        Assert.True(await db.Vouchers.AnyAsync(x =>
            x.VoucherCode == "PN-IT-20260609-0004"
            && x.VoucherType == VoucherTypeEnum.NhapKho
            && !x.IsPosted
            && !x.IsCancelled
            && x.InboundStatus == InboundStatusEnum.Approved
            && x.AsnCode == "ASN-IT-20260609-0004"
            && x.ExpectedArrivalAt.HasValue
            && x.DockAppointmentStart.HasValue
            && x.DockAppointmentEnd.HasValue
            && x.CarrierName == "Vận tải Minh Long"
            && x.DriverName == "Lê Gia Hân"));
        await AssertItemBaseUomAsync(db, "DEMO-IT-LAP-DELL-5420", "Chiếc");
        await AssertItemBaseUomAsync(db, "DEMO-IT-PROJ-EPSON-X49", "Chiếc");
        await AssertItemBaseUomAsync(db, "DEMO-IT-MOUSE-M185", "Cái");
        await AssertItemBaseUomAsync(db, "DEMO-IT-KBD-DELL-KB216", "Cái");
        await AssertSerialTrackedItemsHaveSerialsMatchingStockAsync(db, "DEMO-IT-");
        Assert.True(await db.QualityInspections.AnyAsync(x => x.InspectorName == "Trần Minh Khôi" && x.InspectionPlanName == "QC thiết bị IT"));
        Assert.False(await db.Items.AnyAsync(x => x.ItemCode.StartsWith("DEMO-MED-") || x.ItemCode.StartsWith("DEMO-ECOM-")));

        var medical = await service.ApplyAsync(DemoDataDomain.MedicalInventory, "demo.admin");
        Assert.Equal("medical", medical.DomainKey);
        Assert.Equal(1, medical.Warehouses);
        await AssertActorScopedToDemoWarehouseAsync(db, "DEMO-MED-KHO");
        await AssertDemoWarehouseIntegrityAsync(db, "DEMO-MED-KHO");
        await AssertInventoryLedgerBalancesValidAsync(db);
        Assert.True(await db.Warehouses.AnyAsync(x => x.WarehouseName == "Kho vật tư y tế"));
        Assert.True(await db.Items.AnyAsync(x => x.ItemCode == "DEMO-MED-TEST-COVID" && x.TrackLot && x.TrackExpiry));
        Assert.True(await db.ItemLocations.AnyAsync(x => x.Item!.ItemCode.StartsWith("DEMO-MED-") && x.LotNumber != null && x.ExpiryDate != null));
        await AssertItemBaseUomAsync(db, "DEMO-MED-MASK-4L", "Hộp");
        await AssertItemBaseUomAsync(db, "DEMO-MED-TEST-COVID", "Bộ");
        await AssertItemBaseUomAsync(db, "DEMO-MED-SANITIZER-500", "Chai");
        await AssertItemBaseUomAsync(db, "DEMO-MED-BANDAGE-ROLL", "Gói");
        await AssertItemBaseUomAsync(db, "DEMO-MED-PARA-500", "Vỉ");
        Assert.False(await db.Items.AnyAsync(x => x.ItemCode.StartsWith("DEMO-MED-") && (!x.TrackLot || !x.TrackExpiry || x.TrackSerial)));
        Assert.True(await db.QualityInspections.AnyAsync(x => x.InspectorName == "Bác sĩ Nguyễn Thảo Vy" && x.InspectionPlanName == "QC vật tư y tế"));
        Assert.True(await db.StockAlerts.AnyAsync());
        Assert.False(await db.Items.AnyAsync(x => x.ItemCode.StartsWith("DEMO-IT-") || x.ItemCode.StartsWith("DEMO-ECOM-")));
        Assert.False(await db.Warehouses.AnyAsync(x => x.WarehouseCode.StartsWith("DEMO-IT-") || x.WarehouseCode.StartsWith("DEMO-ECOM-")));
        Assert.Equal(1, await db.AppUsers.CountAsync());
        Assert.Equal(1, await db.AppRoles.CountAsync());

        var ecommerce = await service.ApplyAsync(DemoDataDomain.EcommerceInventory, "demo.admin");
        Assert.Equal("ecommerce", ecommerce.DomainKey);
        Assert.Equal(1, ecommerce.Warehouses);
        await AssertActorScopedToDemoWarehouseAsync(db, "DEMO-ECOM-KHO");
        await AssertDemoWarehouseIntegrityAsync(db, "DEMO-ECOM-KHO");
        await AssertInventoryLedgerBalancesValidAsync(db);
        Assert.True(await db.Warehouses.AnyAsync(x => x.WarehouseName == "Kho thương mại điện tử"));
        Assert.True(await db.Items.AnyAsync(x => x.ItemCode == "DEMO-ECOM-HEAD-BT-A9"));
        Assert.True(await db.StockReservations.AnyAsync());
        Assert.True(await db.PickTasks.AnyAsync(x => x.TaskCode.StartsWith("DEMO-ECOM-PICK-")));
        Assert.True(await db.Waves.AnyAsync(x => x.WaveCode == "DEMO-ECOM-WAVE-202606-01"));
        Assert.True(await db.Vouchers.AnyAsync(x =>
            x.VoucherCode == "PN-ECOM-20260609-0003"
            && x.InboundStatus == InboundStatusEnum.Approved
            && x.AsnCode == "ASN-ECOM-20260609-0003"
            && x.ExpectedArrivalAt.HasValue));
        var pendingQcVoucherId = await db.Vouchers
            .Where(x => x.VoucherCode == "PN-ECOM-20260609-0004"
                && x.InboundStatus == InboundStatusEnum.Receiving
                && !x.IsPosted
                && x.AsnCode == "ASN-ECOM-20260609-0004")
            .Select(x => x.VoucherId)
            .SingleAsync();
        Assert.True(await db.VoucherDetails.AnyAsync(x => x.VoucherId == pendingQcVoucherId));
        Assert.False(await db.QualityInspections.AnyAsync(x => x.VoucherId == pendingQcVoucherId));
        await AssertItemBaseUomAsync(db, "DEMO-ECOM-HEAD-BT-A9", "Cái");
        await AssertItemBaseUomAsync(db, "DEMO-ECOM-CHG-65W-GAN", "Cái");
        await AssertItemBaseUomAsync(db, "DEMO-ECOM-CABLE-C2C-1M", "Cái");
        await AssertItemBaseUomAsync(db, "DEMO-ECOM-MOUSE-G102", "Cái");
        await AssertItemBaseUomAsync(db, "DEMO-ECOM-KBD-MECH-K2", "Cái");
        await AssertSerialTrackedItemsHaveSerialsMatchingStockAsync(db, "DEMO-ECOM-");
        Assert.True(await db.QualityInspections.AnyAsync(x => x.InspectorName == "Lê Gia Hân" && x.InspectionPlanName == "QC hàng điện tử"));
        Assert.False(await db.Items.AnyAsync(x => x.ItemCode.StartsWith("DEMO-IT-") || x.ItemCode.StartsWith("DEMO-MED-")));
        Assert.False(await db.Warehouses.AnyAsync(x => x.WarehouseCode.StartsWith("DEMO-IT-") || x.WarehouseCode.StartsWith("DEMO-MED-")));
        Assert.Equal(1, await db.AppUsers.CountAsync());
        Assert.Equal(1, await db.AppRoles.CountAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.ActionType == "APPLY_DEMO_DATA" && x.ChangedBy == "demo.admin"));
        Assert.True(await db.AuditLogs.AnyAsync(x => x.ActionType == "APPLY_DEMO_DATA" && x.NewValue != null && x.NewValue.Contains("Kho thương mại điện tử")));
    }

    [Fact]
    public async Task ApplyAsync_SqlServer_ShouldSeedEveryDemoDomainOnDisposableDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("WMS_DEMO_SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var connection = new SqlConnectionStringBuilder(connectionString);
        var dataSource = connection.DataSource.Trim();
        var isLocalServer = IsLocalSqlServer(dataSource);
        Assert.True(isLocalServer, "Demo SQL integration test refuses a non-local SQL Server.");
        Assert.StartsWith("AUDIT_TEST_", connection.InitialCatalog, StringComparison.Ordinal);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new AppDbContext(options) { SkipAudit = true };
        await db.Database.EnsureDeletedAsync();
        try
        {
            await db.Database.MigrateAsync();

        var roleId = await db.AppRoles
            .Where(x => x.RoleName == "Admin")
            .Select(x => x.RoleId)
            .SingleAsync();
        if (!await db.AppUsers.AnyAsync(x => x.UserName == "AUDIT_TEST_DEMO_ADMIN"))
        {
            db.AppUsers.Add(new AppUser
            {
                UserName = "AUDIT_TEST_DEMO_ADMIN",
                FullName = "Audit Test Demo Admin",
                Email = "audit-test-demo-admin@local.test",
                PasswordHash = "AUDIT_TEST_HASH_NOT_FOR_LOGIN",
                RoleId = roleId,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var oldWarehouse = new Warehouse
        {
            WarehouseCode = "AUDIT_TEST_OLD_WH",
            WarehouseName = "Audit Test Old Demo Warehouse",
            Address = "Local SQL Server only",
            ManagerName = "Audit Test",
            Phone = "0000000000",
            IsActive = true
        };
        var oldCategory = new ItemCategory
        {
            CategoryCode = "AUDIT_TEST_OLD_CAT",
            CategoryName = "Audit Test Old Category",
            IsActive = true
        };
        db.Warehouses.Add(oldWarehouse);
        db.ItemCategories.Add(oldCategory);
        await db.SaveChangesAsync();
        db.StockSnapshotRuns.Add(new StockSnapshotRun
        {
            WarehouseId = oldWarehouse.WarehouseId,
            SnapshotDate = new DateTime(2026, 7, 1),
            CreatedBy = "AUDIT_TEST_DEMO_ADMIN",
            Status = "Completed"
        });
        db.InspectionPlanTemplates.Add(new InspectionPlanTemplate
        {
            PlanName = "AUDIT_TEST_OLD_SQL_DEMO_PLAN",
            ItemCategoryId = oldCategory.CategoryId,
            SampleSizeFormula = "Percentage",
            SampleSizeValue = 10,
            IsActive = true,
            CreatedBy = "AUDIT_TEST_DEMO_ADMIN"
        });
        await db.SaveChangesAsync();

        var service = new DemoDataSeedService(db);
        var it = await service.ApplyAsync(DemoDataDomain.ItInventory, "AUDIT_TEST_DEMO_ADMIN");
        var medical = await service.ApplyAsync(DemoDataDomain.MedicalInventory, "AUDIT_TEST_DEMO_ADMIN");
        var ecommerce = await service.ApplyAsync(DemoDataDomain.EcommerceInventory, "AUDIT_TEST_DEMO_ADMIN");

        Assert.Equal("it", it.DomainKey);
        Assert.Equal("medical", medical.DomainKey);
        Assert.Equal("ecommerce", ecommerce.DomainKey);
        Assert.True(await db.AppUsers.AnyAsync(x => x.UserName == "AUDIT_TEST_DEMO_ADMIN"));
        Assert.True(await db.Warehouses.AnyAsync(x => x.WarehouseCode == "DEMO-ECOM-KHO"));
        Assert.True(await db.Items.AnyAsync(x => x.ItemCode == "DEMO-ECOM-HEAD-BT-A9"));
        Assert.True(await db.Vouchers.AnyAsync(x => x.VoucherCode.StartsWith("PN-ECOM-")));
        Assert.True(await db.Vouchers.AnyAsync(x =>
            x.VoucherCode == "PN-ECOM-20260609-0003"
            && x.InboundStatus == InboundStatusEnum.Approved
            && x.AsnCode == "ASN-ECOM-20260609-0003"
            && x.ExpectedArrivalAt.HasValue));
        var pendingQcVoucherId = await db.Vouchers
            .Where(x => x.VoucherCode == "PN-ECOM-20260609-0004"
                && x.InboundStatus == InboundStatusEnum.Receiving
                && !x.IsPosted
                && x.AsnCode == "ASN-ECOM-20260609-0004")
            .Select(x => x.VoucherId)
            .SingleAsync();
        Assert.True(await db.VoucherDetails.AnyAsync(x => x.VoucherId == pendingQcVoucherId));
        Assert.False(await db.QualityInspections.AnyAsync(x => x.VoucherId == pendingQcVoucherId));
        Assert.False(await db.Warehouses.AnyAsync(x => x.WarehouseCode == "AUDIT_TEST_OLD_WH"));
        Assert.False(await db.InspectionPlanTemplates.AnyAsync(x => x.PlanName == "AUDIT_TEST_OLD_SQL_DEMO_PLAN"));
        await AssertInventoryLedgerBalancesValidAsync(db);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.CloseConnectionAsync();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ApplyAsync_ShouldClearAiRecommendationGraphBeforeReplacingDemoStockCounts()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options) { SkipAudit = true };
        await CreateSqliteSchemaAsync(db);

        var uom = new UnitOfMeasure { UomCode = "A3D", UomName = "Đơn vị AI-3 demo", IsActive = true };
        var owner = new Partner { PartnerCode = "AUDIT_TEST_AI3_OWNER", PartnerName = "Chủ hàng AI-3 demo", IsActive = true };
        var warehouse = new Warehouse
        {
            WarehouseCode = "AUDIT_TEST_AI3_WH",
            WarehouseName = "Kho AI-3 cũ",
            IsActive = true
        };
        var zone = new Zone
        {
            Warehouse = warehouse,
            ZoneCode = "AUDIT_TEST_AI3_ZONE",
            ZoneName = "Khu AI-3 cũ",
            ZoneType = ZoneTypeEnum.Storage,
            IsActive = true
        };
        var location = new Location
        {
            Zone = zone,
            LocationCode = "AUDIT_TEST_AI3_BIN",
            IsActive = true
        };
        var item = new Item
        {
            ItemCode = "AUDIT_TEST_AI3_SKU",
            ItemName = "Vật tư AI-3 cũ",
            BaseUom = uom,
            OwnerPartner = owner,
            CurrentStock = 5,
            IsActive = true
        };
        db.ItemLocations.Add(new ItemLocation
        {
            Item = item,
            OwnerPartner = owner,
            Location = location,
            Quantity = 5,
            ReservedQty = 0,
            HoldStatus = InventoryHoldStatusEnum.Available
        });
        var sheet = new StockCountSheet
        {
            SheetCode = "AUDIT_TEST_AI3_SHEET",
            Warehouse = warehouse,
            CountDate = VietnamTime.Today,
            Status = StockCountStatusEnum.Draft,
            CreatedBy = "audit.manager"
        };
        sheet.Lines.Add(new StockCountLine
        {
            Item = item,
            OwnerPartner = owner,
            Location = location,
            SystemQty = 5
        });
        db.StockCountSheets.Add(sheet);

        var modelVersion = new InventoryRiskModelVersion
        {
            ModelKey = "inventory-discrepancy-risk",
            Version = "AUDIT_TEST_AI3_1",
            FeatureSchemaVersion = "AUDIT_TEST_AI3_SCHEMA",
            ConfigurationJson = "{}",
            ArtifactHash = new string('A', 64),
            CreatedBy = "audit.scorer"
        };
        var snapshot = new InventoryRiskFeatureSnapshot
        {
            ModelVersion = modelVersion,
            BatchId = Guid.NewGuid(),
            PredictionCutoff = VietnamTime.Now,
            Warehouse = warehouse,
            OwnerPartner = owner,
            Item = item,
            Location = location,
            ScopeKey = "AUDIT_TEST_AI3_SCOPE",
            FeatureJson = "{\"onHandBaseQty\":5}",
            FeatureHash = new string('B', 64),
            SourceWatermark = "AUDIT_TEST_AI3_WATERMARK",
            DataQualityStatus = InventoryRiskDataQualityStatusEnum.Ok,
            DataQualityCodes = ""
        };
        var prediction = new InventoryRiskPrediction
        {
            FeatureSnapshot = snapshot,
            ModelVersion = modelVersion,
            RiskScore = 75,
            Severity = InventoryRiskSeverityEnum.High,
            ReasonCodesJson = "[]",
            FreshUntil = VietnamTime.Now.AddHours(1),
            OutputHash = new string('C', 64)
        };
        var recommendation = new CycleCountRecommendation
        {
            Prediction = prediction,
            Warehouse = warehouse,
            OwnerPartner = owner,
            Item = item,
            Location = location,
            ScopeKey = "AUDIT_TEST_AI3_SCOPE",
            PriorityScore = 75,
            SnapshotSystemQty = 5,
            State = CycleCountRecommendationStateEnum.CountSheetCreated,
            SnapshotWatermark = "AUDIT_TEST_AI3_WATERMARK",
            PredictionCutoff = snapshot.PredictionCutoff,
            FreshUntil = prediction.FreshUntil,
            StockCountSheet = sheet,
            CreatedBy = "inventory-risk-engine"
        };
        recommendation.Decisions.Add(new CycleCountRecommendationDecision
        {
            DecisionType = CycleCountRecommendationDecisionTypeEnum.CountSheetCreated,
            FromState = CycleCountRecommendationStateEnum.Approved,
            ToState = CycleCountRecommendationStateEnum.CountSheetCreated,
            ScopeKey = recommendation.ScopeKey,
            ModelVersion = modelVersion.Version,
            ReasonCode = "AUDIT_TEST_AI3_CREATED",
            BeforeJson = "{}",
            AfterJson = "{}",
            Actor = "audit.manager"
        });
        db.CycleCountRecommendations.Add(recommendation);
        await db.SaveChangesAsync();

        var result = await new DemoDataSeedService(db)
            .ApplyAsync(DemoDataDomain.EcommerceInventory, "audit.demo.admin");

        Assert.Equal("ecommerce", result.DomainKey);
        Assert.Empty(await db.CycleCountRecommendationDecisions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.CycleCountRecommendations.AsNoTracking().ToListAsync());
        Assert.Empty(await db.InventoryRiskPredictions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.InventoryRiskFeatureSnapshots.AsNoTracking().ToListAsync());
        Assert.Empty(await db.InventoryRiskModelVersions.AsNoTracking().ToListAsync());
        Assert.DoesNotContain(await db.StockCountSheets.AsNoTracking().ToListAsync(), row => row.SheetCode == "AUDIT_TEST_AI3_SHEET");
        Assert.True(await db.Items.AnyAsync(row => row.ItemCode.StartsWith("DEMO-ECOM-")));
    }

    private static bool IsLocalSqlServer(string dataSource)
    {
        var trimmed = dataSource.Trim();
        if (trimmed == "." || trimmed.StartsWith(@".\", StringComparison.Ordinal))
            return true;

        var host = trimmed.Split('\\', 2)[0].Split(',', 2)[0].Trim();
        if (host.Equals("(local)", StringComparison.OrdinalIgnoreCase)
            || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var address))
            return IPAddress.IsLoopback(address);

        try
        {
            return Dns.GetHostAddresses(host).Any(IPAddress.IsLoopback);
        }
        catch (SocketException)
        {
            return false;
        }
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

    private static AppDbContext CreateInMemoryDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{name}-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options) { SkipAudit = true };
    }

    private static async Task AssertInventoryLedgerBalancesValidAsync(AppDbContext db)
    {
        var transactions = await db.InventoryTransactions
            .AsNoTracking()
            .Select(transaction => new
            {
                transaction.InventoryTransactionId,
                transaction.QuantityDelta,
                transaction.ReservedDelta,
                transaction.AvailableDelta,
                transaction.QuantityBefore,
                transaction.QuantityAfter,
                transaction.ReservedBefore,
                transaction.ReservedAfter,
                transaction.AvailableBefore,
                transaction.AvailableAfter
            })
            .ToListAsync();
        var invalidTransactionIds = transactions
            .Where(transaction =>
                Math.Abs((transaction.QuantityAfter - transaction.QuantityBefore) - transaction.QuantityDelta) > 0.0001m
                || Math.Abs((transaction.ReservedAfter - transaction.ReservedBefore) - transaction.ReservedDelta) > 0.0001m
                || Math.Abs((transaction.AvailableAfter - transaction.AvailableBefore) - transaction.AvailableDelta) > 0.0001m
                || Math.Abs(transaction.AvailableBefore - (transaction.QuantityBefore - transaction.ReservedBefore)) > 0.0001m
                || Math.Abs(transaction.AvailableAfter - (transaction.QuantityAfter - transaction.ReservedAfter)) > 0.0001m
                || transaction.QuantityAfter < 0
                || transaction.ReservedAfter < 0
                || transaction.ReservedAfter > transaction.QuantityAfter
                || transaction.AvailableAfter < 0)
            .Select(transaction => transaction.InventoryTransactionId)
            .ToList();

        Assert.Empty(invalidTransactionIds);
    }

    private static async Task AssertItemBaseUomAsync(AppDbContext db, string itemCode, string expectedUomName)
    {
        var actual = await db.Items
            .Where(item => item.ItemCode == itemCode)
            .Select(item => item.BaseUom!.UomName)
            .SingleAsync();

        Assert.Equal(expectedUomName, actual);
    }

    private static async Task AssertSerialTrackedItemsHaveSerialsMatchingStockAsync(AppDbContext db, string itemCodePrefix)
    {
        var items = await db.Items
            .Where(item => item.ItemCode.StartsWith(itemCodePrefix) && item.TrackSerial)
            .Select(item => new { item.ItemId, item.ItemCode })
            .ToListAsync();

        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            var stockRows = await db.ItemLocations
                .Where(location => location.ItemId == item.ItemId)
                .Select(location => location.Quantity)
                .ToListAsync();
            var stockQty = stockRows.Sum();
            var activeSerials = await db.SerialNumbers
                .CountAsync(serial => serial.ItemId == item.ItemId && serial.Status == SerialNumberStatusEnum.Active);

            Assert.True(stockQty > 0, $"{item.ItemCode} must have demo stock.");
            Assert.Equal(stockQty, (decimal)activeSerials);
        }
    }

    private static async Task AssertActorScopedToDemoWarehouseAsync(AppDbContext db, string expectedWarehouseCode)
    {
        var actorScope = await db.AppUsers
            .Where(user => user.UserName == "demo.admin")
            .Select(user => new
            {
                user.WarehouseId,
                WarehouseCode = user.Warehouse == null ? null : user.Warehouse.WarehouseCode
            })
            .SingleAsync();

        Assert.NotNull(actorScope.WarehouseId);
        Assert.Equal(expectedWarehouseCode, actorScope.WarehouseCode);
    }

    private static async Task AssertDemoWarehouseIntegrityAsync(AppDbContext db, string expectedWarehouseCode)
    {
        var warehouseId = await db.Warehouses
            .Where(warehouse => warehouse.WarehouseCode == expectedWarehouseCode)
            .Select(warehouse => warehouse.WarehouseId)
            .SingleAsync();

        var itemDefaultLocationMismatches = await db.Items
            .Where(item => item.ItemCode.StartsWith("DEMO-") && item.DefaultLocationId.HasValue)
            .Select(item => new
            {
                item.ItemCode,
                LocationWarehouseId = item.DefaultLocation!.Zone!.WarehouseId
            })
            .Where(item => item.LocationWarehouseId != warehouseId)
            .ToListAsync();
        Assert.Empty(itemDefaultLocationMismatches);

        var itemDefaultLocationZoneMismatches = await db.Items
            .Where(item => item.ItemCode.StartsWith("DEMO-") && item.DefaultLocationId.HasValue)
            .Select(item => new
            {
                item.ItemCode,
                ZoneType = item.DefaultLocation!.Zone!.ZoneType
            })
            .Where(item => item.ZoneType != ZoneTypeEnum.Storage)
            .ToListAsync();
        Assert.Empty(itemDefaultLocationZoneMismatches);

        var stockLocationMismatches = await db.ItemLocations
            .Where(stock => stock.Item!.ItemCode.StartsWith("DEMO-"))
            .Select(stock => new
            {
                stock.Item!.ItemCode,
                LocationWarehouseId = stock.Location!.Zone!.WarehouseId
            })
            .Where(stock => stock.LocationWarehouseId != warehouseId)
            .ToListAsync();
        Assert.Empty(stockLocationMismatches);

        var occupiedStockKeys = await db.ItemLocations
            .Where(stock => stock.Item!.ItemCode.StartsWith("DEMO-") && stock.Quantity > 0)
            .Select(stock => new { stock.LocationId, stock.ItemId, stock.OwnerPartnerId })
            .ToListAsync();
        var mixedSkuLocations = occupiedStockKeys
            .GroupBy(stock => stock.LocationId)
            .Where(group => group
                .Select(stock => (stock.ItemId, stock.OwnerPartnerId))
                .Distinct()
                .Count() > 1)
            .Select(group => group.Key)
            .ToList();
        Assert.Empty(mixedSkuLocations);

        var voucherLocationMismatches = await db.VoucherDetails
            .Where(detail => detail.Item!.ItemCode.StartsWith("DEMO-") && detail.LocationId.HasValue)
            .Select(detail => new
            {
                detail.Item!.ItemCode,
                VoucherWarehouseId = detail.Voucher!.WarehouseId,
                LocationWarehouseId = detail.Location!.Zone!.WarehouseId
            })
            .Where(detail => detail.VoucherWarehouseId != warehouseId || detail.LocationWarehouseId != warehouseId)
            .ToListAsync();
        Assert.Empty(voucherLocationMismatches);

        var serialLocationMismatches = await db.SerialNumbers
            .Where(serial => serial.Item!.ItemCode.StartsWith("DEMO-") && serial.LocationId.HasValue)
            .Select(serial => new
            {
                serial.Item!.ItemCode,
                serial.WarehouseId,
                LocationWarehouseId = serial.Location!.Zone!.WarehouseId
            })
            .Where(serial => serial.WarehouseId != warehouseId || serial.LocationWarehouseId != warehouseId)
            .ToListAsync();
        Assert.Empty(serialLocationMismatches);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "WMS.csproj")))
                return dir;

            var parent = Directory.GetParent(dir);
            if (parent == null)
                break;
            dir = parent.FullName;
        }

        throw new DirectoryNotFoundException("Cannot find WMS.csproj from test output directory.");
    }
}
