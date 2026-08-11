using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WMS.Authorization;
using WMS.Controllers;
using WMS.Data;
using WMS.Models;
using WMS.Services;
using WMS.ViewModels;

namespace WMS.Tests;

public class AuthorizationMatrixTests
{
    [Fact]
    public async Task AdminRole_ShouldSatisfyEveryPermissionPolicyWithoutIndividualClaims()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, WmsRoles.Admin) },
            authenticationType: "unit-test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var handler = new PermissionHandler();

        foreach (var permission in WmsPermissions.All)
        {
            var requirement = new PermissionRequirement(permission);
            var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);
            await handler.HandleAsync(context);
            Assert.True(context.HasSucceeded, $"Admin did not satisfy policy [{permission}].");
        }
    }

    [Fact]
    public async Task RbacSeed_ShouldGrantAdminEveryDefinedPermissionAndCreateAllRoles()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"rbac-seed-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new AppDbContext(options);
        var logger = new CapturingLogger<RbacSeedService>();
        var service = new RbacSeedService(db, logger);

        await service.EnsureSeededAsync();
        Assert.True(logger.Exception == null, logger.Exception?.ToString());

        var adminRoleId = await db.AppRoles
            .Where(role => role.RoleName == WmsRoles.Admin)
            .Select(role => role.RoleId)
            .SingleAsync();
        var adminPermissions = await db.RolePermissions
            .Where(rolePermission => rolePermission.RoleId == adminRoleId)
            .Join(db.Permissions, rolePermission => rolePermission.PermissionId, permission => permission.PermissionId, (_, permission) => permission.Code)
            .ToListAsync();

        Assert.Equal(WmsPermissions.All.OrderBy(code => code), adminPermissions.OrderBy(code => code));
        Assert.Equal(
            WmsRoles.Definitions.Select(role => role.Name).OrderBy(name => name),
            await db.AppRoles.Select(role => role.RoleName).OrderBy(name => name).ToListAsync());
    }

    [Fact]
    public async Task RbacSeed_ShouldKeepReportAndTransportRolesLeastPrivilege()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"rbac-least-privilege-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new AppDbContext(options);
        var logger = new CapturingLogger<RbacSeedService>();
        await new RbacSeedService(db, logger).EnsureSeededAsync();
        Assert.Null(logger.Exception);

        async Task<string[]> GetPermissionsAsync(string roleName)
        {
            var roleId = await db.AppRoles
                .Where(role => role.RoleName == roleName)
                .Select(role => role.RoleId)
                .SingleAsync();
            return await db.RolePermissions
                .Where(rolePermission => rolePermission.RoleId == roleId)
                .Join(db.Permissions, rolePermission => rolePermission.PermissionId, permission => permission.PermissionId, (_, permission) => permission.Code)
                .OrderBy(code => code)
                .ToArrayAsync();
        }

        Assert.Equal(
            new[] { WmsPermissions.ReportView },
            await GetPermissionsAsync(WmsRoles.ReportViewer));
        Assert.Equal(
            new[] { WmsPermissions.ReportView, WmsPermissions.VoucherConfirmShipping }.OrderBy(code => code),
            await GetPermissionsAsync(WmsRoles.TransportStaff));
    }

    [Fact]
    public void ReportViewer_ShouldBeDeniedByEveryVoucherMutationContract()
    {
        var controllerType = typeof(VouchersController);
        var reportPermissions = new HashSet<string>(StringComparer.Ordinal)
        {
            WmsPermissions.ReportView
        };
        var mutationActions = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsActionMethod)
            .Where(method => method.GetCustomAttributes<HttpPostAttribute>(true).Any()
                || method.GetCustomAttributes<HttpPutAttribute>(true).Any()
                || method.GetCustomAttributes<HttpPatchAttribute>(true).Any()
                || method.GetCustomAttributes<HttpDeleteAttribute>(true).Any())
            .ToList();

        Assert.NotEmpty(mutationActions);
        foreach (var action in mutationActions)
        {
            var roles = GetEffectiveRoles(controllerType, action);
            var policies = action.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(controllerType.GetCustomAttributes<AuthorizeAttribute>(true))
                .Select(attribute => attribute.Policy)
                .Where(policy => !string.IsNullOrWhiteSpace(policy))
                .Cast<string>()
                .ToArray();
            var deniedByRole = roles.Length > 0
                && !roles.Contains(WmsRoles.ReportViewer, StringComparer.OrdinalIgnoreCase);
            var deniedByPolicy = policies.Any(policy => !reportPermissions.Contains(policy));

            Assert.True(
                deniedByRole || deniedByPolicy,
                $"{controllerType.Name}.{action.Name} does not deny the report-only role.");
        }
    }

    [Fact]
    public void SystemAdministrationControllers_ShouldBeAdminOnly()
    {
        foreach (var controllerType in new[] { typeof(SystemController), typeof(UsersController) })
        {
            var actions = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(IsActionMethod)
                .ToList();
            Assert.NotEmpty(actions);

            foreach (var action in actions)
            {
                Assert.Equal(
                    new[] { WmsRoles.Admin },
                    GetEffectiveRoles(controllerType, action));
            }
        }
    }

    [Fact]
    public void VoucherMutationContracts_ShouldNotExposeServerOwnedStateFields()
    {
        var serverOwnedStateFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(Voucher.InboundStatus),
            nameof(Voucher.FulfillmentStatus),
            nameof(Voucher.IsPosted),
            nameof(Voucher.IsCancelled)
        };
        var protectedCreateFields = new HashSet<string>(serverOwnedStateFields, StringComparer.OrdinalIgnoreCase)
        {
            nameof(Voucher.CreatedBy),
            nameof(Voucher.CancelledBy),
            nameof(Voucher.CompletedBy)
        };

        foreach (var requestType in new[] { typeof(VoucherCreateViewModel), typeof(ApiCreateVoucherRequest) })
        {
            Assert.DoesNotContain(requestType.GetProperties(), property => protectedCreateFields.Contains(property.Name));
        }

        static IEnumerable<Type> ExpandBoundTypes(Type type)
        {
            var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
            yield return effectiveType;
            if (effectiveType.IsArray)
            {
                yield return effectiveType.GetElementType()!;
            }
            else if (effectiveType.IsGenericType)
            {
                foreach (var argument in effectiveType.GetGenericArguments())
                    yield return argument;
            }
        }

        var controllerAssembly = typeof(VouchersController).Assembly;
        var mutationActions = controllerAssembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.GetCustomAttributes<HttpPostAttribute>(true).Any()
                || method.GetCustomAttributes<HttpPutAttribute>(true).Any()
                || method.GetCustomAttributes<HttpPatchAttribute>(true).Any())
            .ToList();

        foreach (var action in mutationActions)
        {
            foreach (var parameter in action.GetParameters())
            {
                Assert.False(
                    serverOwnedStateFields.Contains(parameter.Name ?? string.Empty),
                    $"{action.DeclaringType?.Name}.{action.Name} binds server-owned parameter [{parameter.Name}].");

                foreach (var boundType in ExpandBoundTypes(parameter.ParameterType)
                    .Where(type => type.Assembly == controllerAssembly && !type.IsEnum))
                {
                    var exposedProperties = boundType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(property => property.CanWrite && serverOwnedStateFields.Contains(property.Name))
                        .Select(property => property.Name)
                        .ToArray();
                    Assert.True(
                        exposedProperties.Length == 0,
                        $"{action.DeclaringType?.Name}.{action.Name} binds [{boundType.Name}] with server-owned fields: {string.Join(", ", exposedProperties)}.");
                }
            }
        }
    }

    [Theory]
    [InlineData(typeof(UsersController), nameof(UsersController.Index), new[] { "Admin" })]
    [InlineData(typeof(UsersController), nameof(UsersController.Create), new[] { "Admin" })]
    [InlineData(typeof(UsersController), nameof(UsersController.ResetPassword), new[] { "Admin" })]
    [InlineData(typeof(UsersController), nameof(UsersController.LoginHelpRequests), new[] { "Admin" })]
    [InlineData(typeof(UsersController), nameof(UsersController.MarkLoginHelpInReview), new[] { "Admin" })]
    [InlineData(typeof(UsersController), nameof(UsersController.ResolveLoginHelpRequest), new[] { "Admin" })]
    [InlineData(typeof(UsersController), nameof(UsersController.RejectLoginHelpRequest), new[] { "Admin" })]
    [InlineData(typeof(UsersController), nameof(UsersController.Delete), new[] { "Admin" })]
    [InlineData(typeof(CategoriesController), nameof(CategoriesController.Index), new[] { "Admin", "Manager" })]
    [InlineData(typeof(CategoriesController), nameof(CategoriesController.Create), new[] { "Admin", "Manager" })]
    [InlineData(typeof(CategoriesController), nameof(CategoriesController.Edit), new[] { "Admin", "Manager" })]
    [InlineData(typeof(CategoriesController), nameof(CategoriesController.Delete), new[] { "Admin" })]
    [InlineData(typeof(UnitsController), nameof(UnitsController.Index), new[] { "Admin", "Manager" })]
    [InlineData(typeof(UnitsController), nameof(UnitsController.Create), new[] { "Admin", "Manager" })]
    [InlineData(typeof(UnitsController), nameof(UnitsController.Delete), new[] { "Admin", "Manager" })]
    [InlineData(typeof(PartnersController), nameof(PartnersController.Index), new[] { "Admin", "Manager" })]
    [InlineData(typeof(PartnersController), nameof(PartnersController.Create), new[] { "Admin", "Manager" })]
    [InlineData(typeof(PartnersController), nameof(PartnersController.Edit), new[] { "Admin", "Manager" })]
    [InlineData(typeof(PartnersController), nameof(PartnersController.Delete), new[] { "Admin" })]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.Edit), new[] { "Admin", "Manager" })]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.Create), new[] { "Admin", "Manager" })]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.Delete), new[] { "Admin", "Manager" })]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.CreateZone), new[] { "Admin", "Manager" })]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.CreateZoneWithLocations), new[] { "Admin", "Manager" })]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.CreateLocation), new[] { "Admin", "Manager" })]

    [InlineData(typeof(ItemsController), nameof(ItemsController.Create), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ItemsController), nameof(ItemsController.Edit), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ItemsController), nameof(ItemsController.Delete), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.Waves), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.PickTasks), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.Shipping), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ShippingDispatch), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DeliveryReconciliation), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ExportDeliveryReconciliationCsv), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ExportDeliveryReconciliationExcel), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CarrierConnectors), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SaveCarrierConnector), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateCarrierShipment), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RetryCarrierShipment), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelCarrierShipment), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SyncCarrierShipment), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DockBoard), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DockBoardData), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.UpdateDockMilestone), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.YardManagement), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateYardSpot), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.GateInYardVisit), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AssignYardSpot), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.MoveYardSpot), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.GateOutYardVisit), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.MovementTasks), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RfMovement), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AssignMovementTask), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.StartMovementTask), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelMovementTask), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ConfirmMovementTask), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SortationConfigs), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SaveSortationConfig), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DisableSortationConfig), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.OrderStreamingConfigs), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SaveOrderStreamingConfig), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DisableOrderStreamingConfig), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.KittingWorkOrders), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateKittingWorkOrder), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.KittingWorkOrderDetails), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReserveKittingWorkOrder), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CompleteKittingWorkOrder), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.PrintKittingWorkOrderLabels), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelKittingWorkOrder), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.VasWorkOrders), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateVasWorkOrder), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.VasWorkOrderDetails), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReserveVasWorkOrder), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.StartVasWorkOrder), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CompleteVasOperation), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SubmitVasQc), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RecordVasQc), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CompleteVasWorkOrder), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelVasWorkOrder), new[] { "Admin", "Manager" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintJobs), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintVoucher), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintPackage), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintShippingPackage), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintShipmentLoadPackageLabels), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintShipmentLoadManifest), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintShipmentLoadHandover), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintDirectHandover), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.Print), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.ShippingDocument), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.Templates), new[] { "Admin", "Manager" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.CreateTemplate), new[] { "Admin", "Manager" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.EditTemplate), new[] { "Admin", "Manager" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.ToggleTemplate), new[] { "Admin", "Manager" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.ItemRules), new[] { "Admin", "Manager" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.SaveItemRule), new[] { "Admin", "Manager" })]
    [InlineData(typeof(LabelsController), nameof(LabelsController.DeleteItemRule), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SlottingSimulation), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateSlottingSimulation), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ApproveSlottingSimulation), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ExceptionCenter), new[] { "Admin", "Manager", "Staff", "ReportViewer" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SerialLookup), new[] { "Admin", "Manager", "Staff", "Viewer", "ReportViewer" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SerialReceiving), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RegisterSerials), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AcknowledgeException), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AssignException), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ResolveException), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReassignTask), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCount), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountEntry), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountStart), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountSubmit), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountSaveDraft), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountRequestRecount), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountApproveDraft), new[] { "Admin", "Manager" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.Cancel), new[] { "Admin", "Manager" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.Create), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ReleaseDirect), new[] { "Admin", "Manager" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmForPicking), new[] { "Admin", "Manager" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmPickTask), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.PostReservedOutbound), new[] { "Admin", "Manager" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmPacking), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmShipping), new[] { "Admin", "Manager" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.Approve), new[] { "Admin", "Manager" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmActualReceivingQty), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.UpdateInboundDefect), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ReplenishDefect), new[] { "Admin", "Manager" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.AnalyzeReceipt), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.DownloadReceiptDocument), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.DownloadImportTemplate), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.DownloadSampleImport100), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ImportLinesExcel), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DownloadYardVisitEvidence), new[] { "Admin", "Manager", "Staff" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountUnlockApproved), new[] { "Admin" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.PeriodLocks), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.SetPeriodLock), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.ClearPeriodLock), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockValuation), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.ExportStockValuation), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockSnapshot), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.GenerateStockSnapshot), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.ExportStockSnapshot), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.AuditTrail), new[] { "Admin" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.Alerts), new[] { "Admin" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.RefreshExpiryAlerts), new[] { "Admin" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.ResolveAlert), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.IntegrationDashboard), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.IntegrationOpenApiContract), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ImportEdiMessage), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ExportEdiMessage), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReplayEdiMessage), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SaveWebhookSubscription), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReplayWebhookDelivery), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.EnsureEnterpriseConnectorPack), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CheckEnterpriseConnectorHealth), new[] { "Admin" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.OptimizationDashboard), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AutomationDashboard), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RunWavelessRelease), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.GeneratePickPathPlan), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateToteClusterPlan), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RecordMheTelemetry), new[] { "Admin", "Manager" })]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RunWcsSimulator), new[] { "Admin", "Manager" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.WarehouseOverview), new[] { "Admin", "Manager", "ReportViewer" })]
    [InlineData(typeof(ReportsController), nameof(ReportsController.OpsKpi), new[] { "Admin", "Manager", "ReportViewer" })]
    public void CriticalActions_ShouldMatchExpectedRoleMatrix(Type controllerType, string actionName, string[] expectedRoles)
    {
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, actionName, StringComparison.Ordinal))
            .Where(IsActionMethod)
            .ToList();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            var actualRoles = GetEffectiveRoles(controllerType, method);
            var normalizedExpectedRoles = ExpandExpectedOperationalRoles(expectedRoles, actualRoles);
            Assert.Equal(normalizedExpectedRoles.OrderBy(x => x), actualRoles.OrderBy(x => x));
        }
    }

    private static string[] ExpandExpectedOperationalRoles(string[] expectedRoles, string[] actualRoles)
    {
        var expected = expectedRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = actualRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasLegacyOperationalRole = expected.Contains(WmsRoles.Staff) || actual.Contains(WmsRoles.Staff);

        if (hasLegacyOperationalRole)
        {
            foreach (var role in new[] { WmsRoles.Staff, WmsRoles.InboundStaff, WmsRoles.OutboundStaff, WmsRoles.InventoryStaff, WmsRoles.TransportStaff })
            {
                if (actual.Contains(role))
                    expected.Add(role);
            }
        }

        if (!expected.Contains(WmsRoles.Viewer))
            Assert.DoesNotContain(WmsRoles.Viewer, actual);
        if (!expected.Contains(WmsRoles.ReportViewer))
            Assert.DoesNotContain(WmsRoles.ReportViewer, actual);
        return expected.ToArray();
    }

    [Theory]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockValuation), WmsPermissions.ReportViewFinancial)]
    [InlineData(typeof(ReportsController), nameof(ReportsController.ExportStockValuation), WmsPermissions.ReportViewFinancial)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.Create), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.SubmitForApproval), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmActualReceivingQty), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmReceiving), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ApproveInbound), WmsPermissions.VoucherApproveInbound)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.RejectInbound), WmsPermissions.VoucherApproveInbound)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmPickTask), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmPacking), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.CaptureCatchWeight), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.UpdateInboundDefect), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.SuggestPutaway), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.SuggestPutawayLocation), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.GetConversionRate), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.AnalyzeReceipt), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.AnalyzeReceipts), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ImportLinesExcel), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.AssignDock), WmsPermissions.VoucherApproveInbound)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.CreateBackorder), WmsPermissions.VoucherApproveOutbound)]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.GetItemLocations), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.GetSuggestedLocations), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.CheckLocationConflict), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.UpdateDockMilestone), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RegisterSerials), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ExecuteCrossDock), WmsPermissions.VoucherReleasePicking)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CompleteCrossDockTask), WmsPermissions.VoucherReleasePicking)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RunSlottingOptimization), WmsPermissions.WarehouseConfigManage)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RunWaveOptimization), WmsPermissions.VoucherReleasePicking)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RunWavelessRelease), WmsPermissions.VoucherReleasePicking)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.GeneratePickPathPlan), WmsPermissions.VoucherReleasePicking)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateToteClusterPlan), WmsPermissions.VoucherReleasePicking)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RecordMheTelemetry), WmsPermissions.MheManage)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RunWcsSimulator), WmsPermissions.MheManage)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SaveMheAdapterProfile), WmsPermissions.MheManage)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.OverrideMheCommand), WmsPermissions.MheManage)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateCycleCountProgram), WmsPermissions.StockCountApprove)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateLpnMovementTask), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AssignMovementTask), WmsPermissions.PickTaskReassign)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.StartMovementTask), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelMovementTask), WmsPermissions.VoucherCancel)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ConfirmMovementTask), WmsPermissions.VoucherCreate)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateShipmentLoad), WmsPermissions.VoucherConfirmShipping)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AddVoucherToShipmentLoad), WmsPermissions.VoucherConfirmShipping)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RemoveVoucherFromShipmentLoad), WmsPermissions.VoucherConfirmShipping)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ScanShipmentLoadPackage), WmsPermissions.VoucherConfirmShipping)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.MarkShipmentLoadStatus), WmsPermissions.VoucherConfirmShipping)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DepartShipmentLoad), WmsPermissions.VoucherConfirmShipping)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelShipmentLoad), WmsPermissions.VoucherConfirmShipping)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SubmitInspection), WmsPermissions.QcSubmitInspection)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReleaseQuarantine), WmsPermissions.QcResolveHold)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateRecallCase), WmsPermissions.QcResolveHold)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ResolveRecallCase), WmsPermissions.QcResolveHold)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RunCycleCountProgram), WmsPermissions.StockCountApprove)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReassignTask), WmsPermissions.PickTaskReassign)]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountEntry), WmsPermissions.ReportView)]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountStart), WmsPermissions.ReportView)]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountSubmit), WmsPermissions.ReportView)]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountRequestRecount), WmsPermissions.StockCountApprove)]
    public void SensitiveActions_ShouldRequireExpectedPolicy(Type controllerType, string actionName, string expectedPolicy)
    {
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, actionName, StringComparison.Ordinal))
            .Where(IsActionMethod)
            .ToList();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            var policies = method.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(controllerType.GetCustomAttributes<AuthorizeAttribute>(true))
                .Select(a => a.Policy)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();
            Assert.Contains(expectedPolicy, policies);
        }
    }

    [Theory]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmPickTask), WmsRoles.OutboundRoles)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmPacking), WmsRoles.OutboundRoles)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.SuggestPutaway), WmsRoles.InboundRoles)]
    [InlineData(typeof(VouchersController), nameof(VouchersController.SuggestPutawayLocation), WmsRoles.InboundRoles)]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.GetSuggestedLocations), WmsRoles.InboundRoles)]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.GetItemLocations), WmsRoles.OutboundRoles)]
    [InlineData(typeof(OperationsController), nameof(OperationsController.UpdateDockMilestone), WmsRoles.InboundRoles)]
    public void SpecializedWorkflowMutations_ShouldUseExactRoleGroup(Type controllerType, string actionName, string expectedRoles)
    {
        var method = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => string.Equals(m.Name, actionName, StringComparison.Ordinal) && IsActionMethod(m));
        var actualRoles = method.GetCustomAttributes<AuthorizeAttribute>(true)
            .SelectMany(attribute => (attribute.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var expected = expectedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected, actualRoles, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(typeof(HomeController), nameof(HomeController.Index))]
    [InlineData(typeof(ItemsController), nameof(ItemsController.Index))]
    [InlineData(typeof(ItemsController), nameof(ItemsController.Details))]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.Index))]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.Details))]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.InventoryMap))]
    [InlineData(typeof(VouchersController), nameof(VouchersController.Index))]
    [InlineData(typeof(VouchersController), nameof(VouchersController.Details))]
    [InlineData(typeof(ReportsController), nameof(ReportsController.Inventory))]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockMovement))]
    public void ReadOnlyActions_ShouldRequireAuthenticationAtMinimum(Type controllerType, string actionName)
    {
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, actionName, StringComparison.Ordinal))
            .Where(IsActionMethod)
            .ToList();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            var isAnonymous = method.GetCustomAttribute<AllowAnonymousAttribute>() != null
                || controllerType.GetCustomAttribute<AllowAnonymousAttribute>() != null;
            Assert.False(isAnonymous);
        }
    }

    [Theory]
    [InlineData(nameof(AccountController.TrustedDevices))]
    [InlineData(nameof(AccountController.RevokeCurrentTrustedDevice))]
    [InlineData(nameof(AccountController.RevokeAllTrustedDevices))]
    public void PersonalTrustedDeviceActions_ShouldAllowEveryAuthenticatedRole(string actionName)
    {
        var method = typeof(AccountController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => string.Equals(m.Name, actionName, StringComparison.Ordinal) && IsActionMethod(m));
        var authorize = method.GetCustomAttributes<AuthorizeAttribute>(true).ToArray();

        Assert.NotEmpty(authorize);
        Assert.All(authorize, attribute => Assert.True(string.IsNullOrWhiteSpace(attribute.Roles)));
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void DangerousSystemActions_ShouldRequireAdminAndAntiForgery()
    {
        var controllerType = typeof(SystemController);
        var actionNames = new[]
        {
            nameof(SystemController.MergeLocationsPerLevel),
            nameof(SystemController.ResetDatabase)
        };

        foreach (var actionName in actionNames)
        {
            var method = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == actionName && IsActionMethod(m));

            var roles = GetEffectiveRoles(controllerType, method);
            Assert.Equal(new[] { "Admin" }, roles);
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
    }

    [Fact]
    public void SystemController_ShouldNotExposeSeedDataAction()
    {
        var controllerType = typeof(SystemController);
        var seedDataActions = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, "SeedData", StringComparison.Ordinal))
            .Where(IsActionMethod)
            .ToList();

        Assert.Empty(seedDataActions);
    }

    [Theory]
    [InlineData(typeof(VouchersController), nameof(VouchersController.Cancel))]
    [InlineData(typeof(VouchersController), nameof(VouchersController.Approve))]
    [InlineData(typeof(VouchersController), nameof(VouchersController.PostReservedOutbound))]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmPacking))]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ConfirmShipping))]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountApproveDraft))]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountStart))]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountSubmit))]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountRequestRecount))]
    [InlineData(typeof(ReportsController), nameof(ReportsController.StockCountUnlockApproved))]
    [InlineData(typeof(UsersController), nameof(UsersController.Delete))]
    [InlineData(typeof(UsersController), nameof(UsersController.MarkLoginHelpInReview))]
    [InlineData(typeof(UsersController), nameof(UsersController.ResolveLoginHelpRequest))]
    [InlineData(typeof(UsersController), nameof(UsersController.RejectLoginHelpRequest))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateYardSpot))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.GateInYardVisit))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AssignYardSpot))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.MoveYardSpot))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.GateOutYardVisit))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.AssignMovementTask))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.StartMovementTask))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelMovementTask))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ConfirmMovementTask))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SaveSortationConfig))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DisableSortationConfig))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SaveOrderStreamingConfig))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.DisableOrderStreamingConfig))]
    [InlineData(typeof(VouchersController), nameof(VouchersController.ReleaseDirect))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReserveKittingWorkOrder))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CompleteKittingWorkOrder))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelKittingWorkOrder))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.ReserveVasWorkOrder))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.StartVasWorkOrder))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CompleteVasOperation))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SubmitVasQc))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RecordVasQc))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CompleteVasWorkOrder))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelVasWorkOrder))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SaveCarrierConnector))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateCarrierShipment))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.RetryCarrierShipment))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CancelCarrierShipment))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.SyncCarrierShipment))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.ToggleTemplate))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.SaveItemRule))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.DeleteItemRule))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintVoucher))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintPackage))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintShippingPackage))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintShipmentLoadPackageLabels))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintShipmentLoadManifest))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintShipmentLoadHandover))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.PrintDirectHandover))]
    public void SensitivePostActions_ShouldUseAntiForgery(Type controllerType, string actionName)
    {
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == actionName && IsActionMethod(m))
            .ToList();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
    }

    [Theory]
    [InlineData(typeof(LabelsController), nameof(LabelsController.CreateTemplate))]
    [InlineData(typeof(LabelsController), nameof(LabelsController.EditTemplate))]
    [InlineData(typeof(AccountController), nameof(AccountController.AccessHelp))]
    [InlineData(typeof(OperationsController), nameof(OperationsController.CreateVasWorkOrder))]
    public void SensitivePostOverloads_ShouldUseAntiForgery(Type controllerType, string actionName)
    {
        var postMethods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == actionName && IsActionMethod(m))
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
            .ToList();

        Assert.NotEmpty(postMethods);
        foreach (var method in postMethods)
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void PeriodLock_ShouldUseOperationDate_WhenOldVoucherIsPostedAfterLockedPeriod()
    {
        var voucher = new Voucher
        {
            VoucherDate = new DateTime(2026, 4, 1)
        };
        var operationDate = new DateTime(2026, 4, 30, 9, 0, 0);
        var lockDate = new DateTime(2026, 4, 15);

        var transactionDate = ResolveLockTransactionDate(voucher, operationDate);

        Assert.Equal(operationDate, transactionDate);
        Assert.False(IsPeriodLocked(transactionDate, lockDate));
    }

    [Fact]
    public void PeriodLock_ShouldUseCompletedAt_WhenNewVoucherCompletedInsideLockedPeriod()
    {
        var completedAt = new DateTime(2026, 4, 10, 16, 30, 0);
        var voucher = new Voucher
        {
            VoucherDate = new DateTime(2026, 4, 30),
            CompletedAt = completedAt
        };
        var operationDate = new DateTime(2026, 4, 30, 9, 0, 0);
        var lockDate = new DateTime(2026, 4, 15);

        var transactionDate = ResolveLockTransactionDate(voucher, operationDate);

        Assert.Equal(completedAt, transactionDate);
        Assert.True(IsPeriodLocked(transactionDate, lockDate));
    }

    [Fact]
    public async Task YardGateIn_ShouldPreventSecondActiveVisitForSameTrailer()
    {
        await using var db = CreateYardTestDb();
        var service = CreateYardService(db);

        await service.GateInAsync(new YardGateInRequest { WarehouseId = 1, TrailerNumber = "TR-100" }, null, "tester");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GateInAsync(new YardGateInRequest { WarehouseId = 1, TrailerNumber = "TR-100" }, null, "tester"));

        Assert.Equal("YARD_ACTIVE_VISIT_EXISTS", ex.Code);
    }

    [Fact]
    public async Task YardAssignSpot_ShouldPreventTwoActiveVisitsInOneSpot()
    {
        await using var db = CreateYardTestDb();
        var service = CreateYardService(db);
        var spot = await service.CreateSpotAsync(1, "Y-01", null, YardSpotTypeEnum.Standard, YardSpotStatusEnum.Available, null, "tester");
        var first = await service.GateInAsync(new YardGateInRequest { WarehouseId = 1, TrailerNumber = "TR-101", YardSpotId = spot.YardSpotId }, null, "tester");
        var second = await service.GateInAsync(new YardGateInRequest { WarehouseId = 1, TrailerNumber = "TR-102" }, null, "tester");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AssignSpotAsync(second.YardVisitId, spot.YardSpotId, null, "tester"));

        Assert.Equal("YARD_SPOT_OCCUPIED", ex.Code);
        Assert.Equal(YardSpotStatusEnum.Occupied, db.YardSpots.Single(s => s.YardSpotId == spot.YardSpotId).Status);
        Assert.Null(db.YardVisits.Single(v => v.YardVisitId == second.YardVisitId).CurrentSpotId);
        Assert.Equal(spot.YardSpotId, db.YardVisits.Single(v => v.YardVisitId == first.YardVisitId).CurrentSpotId);
    }

    [Fact]
    public async Task YardMoveSpot_ShouldUpdateCurrentSpotAndKeepVisitOpen()
    {
        await using var db = CreateYardTestDb();
        var service = CreateYardService(db);
        var spotA = await service.CreateSpotAsync(1, "Y-02", null, YardSpotTypeEnum.Standard, YardSpotStatusEnum.Available, null, "tester");
        var spotB = await service.CreateSpotAsync(1, "Y-03", null, YardSpotTypeEnum.Standard, YardSpotStatusEnum.Available, null, "tester");
        var visit = await service.GateInAsync(new YardGateInRequest { WarehouseId = 1, TrailerNumber = "TR-103", YardSpotId = spotA.YardSpotId }, null, "tester");

        var moved = await service.MoveSpotAsync(visit.YardVisitId, spotB.YardSpotId, null, "tester");

        Assert.Equal(spotB.YardSpotId, moved.CurrentSpotId);
        Assert.Null(moved.GateOutAt);
        Assert.Equal(YardVisitStatusEnum.Parked, moved.Status);
        Assert.Equal(YardSpotStatusEnum.Available, db.YardSpots.Single(s => s.YardSpotId == spotA.YardSpotId).Status);
        Assert.Equal(YardSpotStatusEnum.Occupied, db.YardSpots.Single(s => s.YardSpotId == spotB.YardSpotId).Status);
    }

    [Fact]
    public async Task YardGateOut_ShouldFinalizeVisitAndReleaseSpot()
    {
        await using var db = CreateYardTestDb();
        var service = CreateYardService(db);
        var spot = await service.CreateSpotAsync(1, "Y-04", null, YardSpotTypeEnum.Standard, YardSpotStatusEnum.Available, null, "tester");
        var visit = await service.GateInAsync(new YardGateInRequest { WarehouseId = 1, TrailerNumber = "TR-104", YardSpotId = spot.YardSpotId }, null, "tester");

        var closed = await service.GateOutAsync(visit.YardVisitId, null, "tester");

        Assert.Equal(YardVisitStatusEnum.GatedOut, closed.Status);
        Assert.NotNull(closed.GateOutAt);
        Assert.True(closed.GetDwellMinutes(closed.GateOutAt.Value) >= 0);
        Assert.Equal(YardSpotStatusEnum.Available, db.YardSpots.Single(s => s.YardSpotId == spot.YardSpotId).Status);
    }

    private static bool IsActionMethod(MethodInfo method)
    {
        if (method.IsSpecialName) return false;
        if (method.GetCustomAttribute<NonActionAttribute>() != null) return false;
        return typeof(IActionResult).IsAssignableFrom(method.ReturnType)
            || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                && typeof(IActionResult).IsAssignableFrom(method.ReturnType.GenericTypeArguments[0]));
    }

    private static string[] GetEffectiveRoles(Type controllerType, MethodInfo method)
    {
        var allowAnonymous = method.GetCustomAttribute<AllowAnonymousAttribute>() != null
            || controllerType.GetCustomAttribute<AllowAnonymousAttribute>() != null;
        if (allowAnonymous) return Array.Empty<string>();

        var methodAuth = method.GetCustomAttributes<AuthorizeAttribute>(true).ToList();
        if (methodAuth.Any(a => !string.IsNullOrWhiteSpace(a.Roles)))
            return ParseRoles(methodAuth);

        var classAuth = controllerType.GetCustomAttributes<AuthorizeAttribute>(true).ToList();
        return ParseRoles(classAuth);
    }

    private static string[] ParseRoles(IEnumerable<AuthorizeAttribute> attributes)
    {
        return attributes
            .SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x)
            .ToArray();
    }

    private static DateTime ResolveLockTransactionDate(Voucher voucher, DateTime operationDate)
    {
        var method = typeof(VouchersController).GetMethod("ResolveLockTransactionDate", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return (DateTime)method.Invoke(null, new object?[] { voucher, operationDate })!;
    }

    private static bool IsPeriodLocked(DateTime transactionDate, DateTime lockDate)
    {
        var method = typeof(VouchersController).GetMethod("IsLocked", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return (bool)method.Invoke(null, new object?[] { transactionDate, lockDate })!;
    }

    private static AppDbContext CreateYardTestDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"yard-tests-{Guid.NewGuid():N}")
            .Options;
        var db = new AppDbContext(options)
        {
            SkipAudit = true
        };
        db.Warehouses.Add(new Warehouse { WarehouseId = 1, WarehouseCode = "WH1", WarehouseName = "Warehouse 1", IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static YardManagementService CreateYardService(AppDbContext db)
        => new(db, new EfUnitOfWork(db));

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public Exception? Exception { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exception ??= exception;
        }
    }
}
