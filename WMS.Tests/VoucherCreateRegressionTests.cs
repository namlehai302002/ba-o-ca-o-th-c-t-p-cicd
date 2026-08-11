using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public sealed class VoucherCreateRegressionTests
{
    [Fact]
    public async Task VoucherCreateWorkflow_ShouldAlwaysIncludeBaseUomForActiveItems()
    {
        await using var db = CreateDb("voucher-uom-base");
        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 10, UomCode = "PCS", UomName = "Pieces", IsActive = true },
            new UnitOfMeasure { UomId = 20, UomCode = "M", UomName = "Meter", IsActive = true },
            new UnitOfMeasure { UomId = 30, UomCode = "BOX", UomName = "Box", IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 1, ItemCode = "A", ItemName = "Item A", BaseUomId = 10, IsActive = true },
            new Item { ItemId = 2, ItemCode = "B", ItemName = "Item B", BaseUomId = 20, IsActive = true });
        db.UnitConversions.Add(new UnitConversion
        {
            ItemId = 1,
            FromUomId = 30,
            ToUomId = 10,
            ConversionRate = 12m,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new VoucherCreateWorkflowService(db);
        var json = await service.BuildItemAllowedSourceUomsJsonAsync(db.Items.AsNoTracking().ToList());
        var map = JsonSerializer.Deserialize<Dictionary<int, List<int>>>(json);

        Assert.NotNull(map);
        Assert.Contains(10, map![1]);
        Assert.Contains(30, map[1]);
        Assert.Contains(20, map[2]);
        Assert.Single(map[2]);
    }

    [Fact]
    public async Task VoucherCreateWorkflow_ShouldExposeOnlyActivePositiveSourceUoms()
    {
        await using var db = CreateDb("voucher-uom-active-positive");
        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { UomId = 1, UomCode = "PCS", UomName = "Pieces", IsActive = true },
            new UnitOfMeasure { UomId = 2, UomCode = "BOX", UomName = "Box", IsActive = true },
            new UnitOfMeasure { UomId = 3, UomCode = "OLD", UomName = "Inactive", IsActive = false },
            new UnitOfMeasure { UomId = 4, UomCode = "PAL", UomName = "Pallet", IsActive = true });
        db.Items.AddRange(
            new Item { ItemId = 100, ItemCode = "SKU-OK", ItemName = "Selectable", BaseUomId = 1, IsActive = true },
            new Item { ItemId = 101, ItemCode = "SKU-BASE", ItemName = "Base only", BaseUomId = 4, IsActive = true },
            new Item { ItemId = 102, ItemCode = "SKU-BAD", ItemName = "Inactive base", BaseUomId = 3, IsActive = true });
        db.UnitConversions.AddRange(
            new UnitConversion { ConversionId = 1, ItemId = 100, FromUomId = 2, ToUomId = 1, ConversionRate = 12m, IsActive = true },
            new UnitConversion { ConversionId = 2, ItemId = 100, FromUomId = 3, ToUomId = 1, ConversionRate = 12m, IsActive = true },
            new UnitConversion { ConversionId = 3, ItemId = 100, FromUomId = 4, ToUomId = 1, ConversionRate = 0m, IsActive = true });
        await db.SaveChangesAsync();

        var service = new VoucherCreateWorkflowService(db);
        var json = await service.BuildItemAllowedSourceUomsJsonAsync(await db.Items.AsNoTracking().ToListAsync());
        var map = JsonSerializer.Deserialize<Dictionary<int, List<int>>>(json) ?? new();

        Assert.Equal(new[] { 1, 2 }, map[100]);
        Assert.Equal(new[] { 4 }, map[101]);
        Assert.Empty(map[102]);
    }

    [Fact]
    public async Task VoucherSharedRuleService_ShouldRejectZeroOrUnmappedConversionRates()
    {
        await using var db = CreateDb("voucher-uom-resolve");
        var service = new VoucherSharedRuleService(db);
        var conversions = new List<UnitConversion>
        {
            new() { FromUomId = 2, ToUomId = 1, ConversionRate = 12m, IsActive = true },
            new() { FromUomId = 3, ToUomId = 1, ConversionRate = 0m, IsActive = true }
        };

        Assert.Equal(1m, service.ResolveConversionRate(conversions, itemId: 10, fromUomId: 1, toUomId: 1));
        Assert.Equal(12m, service.ResolveConversionRate(conversions, itemId: 10, fromUomId: 2, toUomId: 1));
        Assert.Null(service.ResolveConversionRate(conversions, itemId: 10, fromUomId: 3, toUomId: 1));
        Assert.Null(service.ResolveConversionRate(conversions, itemId: 10, fromUomId: 4, toUomId: 1));
    }

    [Fact]
    public void QueueWidget_ShouldStartHiddenWhenEmptyToAvoidNavigationFlash()
    {
        var root = FindRepositoryRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Views", "Shared", "_Layout.cshtml"));
        var css = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "site.css"));
        var js = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "offline-scan-queue.js"));

        Assert.Contains("class=\"offline-queue-widget is-empty\"", layout, StringComparison.Ordinal);
        Assert.Contains("data-pending-count=\"0\"", layout, StringComparison.Ordinal);
        Assert.Matches(@"\.offline-queue-widget:not\(\.is-ready\)\s*\{\s*display:\s*none;", css);
        Assert.Matches(@"\.offline-queue-widget\.is-empty\s*\{\s*display:\s*none;", css);
        Assert.Contains("widget.classList.toggle('is-empty', count === 0)", js, StringComparison.Ordinal);
        Assert.Contains("widget.setAttribute('data-pending-count', String(count))", js, StringComparison.Ordinal);
        Assert.Contains("widget.classList.add('is-ready')", js, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateView_ShouldSyncSelect2AfterRebuildingSourceUoms()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("function normalizeSourceUomIds", view, StringComparison.Ordinal);
        Assert.Contains("function syncSourceUomSelect2", view, StringComparison.Ordinal);
        Assert.Contains("function syncItemSelect2", view, StringComparison.Ordinal);
        Assert.Contains("$(sourceUomSelect).val(normalizedValue).trigger('change.select2')", view, StringComparison.Ordinal);
        Assert.Contains("sourceUomSelect.dispatchEvent(new Event('change', { bubbles: true }))", view, StringComparison.Ordinal);
        Assert.Contains("function bindVoucherSelect2ChangeBridge", view, StringComparison.Ordinal);
        Assert.Contains(".off('change.wmsVoucherItemSelect2Bridge', '.item-select')", view, StringComparison.Ordinal);
        Assert.Contains(".off('change.wmsVoucherSourceUomSelect2Bridge', '.source-uom-select')", view, StringComparison.Ordinal);
        Assert.Contains("if (event.originalEvent) return;", view, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateSubmit_ShouldNotLeaveButtonLoadingWhenCustomValidationStopsPost()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("id=\"voucherForm\" novalidate data-no-submit-loading=\"true\"", view, StringComparison.Ordinal);
        Assert.Contains("const submitter = e.submitter instanceof HTMLButtonElement", view, StringComparison.Ordinal);
        Assert.Contains("if (requiredPartner && partnerVisible", view, StringComparison.Ordinal);
        Assert.Contains("e.preventDefault();", view, StringComparison.Ordinal);
        Assert.DoesNotContain("document.getElementById('submitBtn').disabled = true;", view, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultUomQueries_ShouldBeDeterministicAndUseActiveUoms()
    {
        var root = FindRepositoryRoot();
        var importController = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Import.cs"));
        var apiController = File.ReadAllText(Path.Combine(root, "Controllers", "ApiIntegrationController.cs"));

        foreach (var source in new[] { importController, apiController })
        {
            Assert.DoesNotContain("_db.UnitsOfMeasure.Select(u => u.UomId).FirstOrDefaultAsync()", source, StringComparison.Ordinal);
            Assert.Contains(".Where(u => u.IsActive)", source, StringComparison.Ordinal);
            Assert.Contains(".OrderBy(u => u.UomId)", source, StringComparison.Ordinal);
            Assert.Contains(".Select(u => u.UomId)", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RepresentativeStockAndLoadQueries_ShouldUseDeterministicOrdering()
    {
        var root = FindRepositoryRoot();
        var voucherIndex = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Index.cs"));
        var voucherHelpers = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Helpers.cs"));
        var voucherOutbound = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Outbound.cs"));
        var warehouses = File.ReadAllText(Path.Combine(root, "Controllers", "WarehousesController.cs"));

        Assert.Contains(".OrderBy(il => il.ExpiryDate ?? DateTime.MaxValue)", voucherHelpers, StringComparison.Ordinal);
        Assert.Contains(".ThenByDescending(il => il.Quantity - il.ReservedQty)", voucherHelpers, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(il => il.Location!.LocationCode)", voucherHelpers, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(l => l.LocationCode)", voucherHelpers, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(l => l.LocationId)", voucherHelpers, StringComparison.Ordinal);
        Assert.Contains("ResolveInboundPutawayLocationAsync(item, vm.WarehouseId, voucher.OwnerPartnerId)", voucherIndex, StringComparison.Ordinal);

        Assert.Contains(".OrderByDescending(x => x.AddedAt)", voucherOutbound, StringComparison.Ordinal);
        Assert.Contains(".ThenByDescending(x => x.ShipmentLoadId)", voucherOutbound, StringComparison.Ordinal);

        Assert.Contains(".OrderBy(il => il.Item != null ? il.Item.ItemCode : il.ItemId.ToString())", warehouses, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(il => il.LotNumber)", warehouses, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(il => il.ExpiryDate)", warehouses, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(il => il.ItemLocationId)", warehouses, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateUi_ShouldUseDedicatedScrollableSelect2Dropdowns()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));
        var css = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "site.css"));

        Assert.Contains("dropdownParent: $(document.body)", view, StringComparison.Ordinal);
        Assert.Contains("dropdownCssClass: 'wms-item-select-dropdown'", view, StringComparison.Ordinal);
        Assert.Contains("dropdownCssClass: 'wms-source-uom-dropdown'", view, StringComparison.Ordinal);
        Assert.Contains(".select2-dropdown.wms-item-select-dropdown .select2-results__options", css, StringComparison.Ordinal);
        Assert.Contains("max-height: min(52vh, 440px)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateUi_ShouldFollowItemExpiryPolicyOnInboundLines()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("data-track-lot=\"@(item.TrackLot ? \"true\" : \"false\")\"", view, StringComparison.Ordinal);
        Assert.Contains("data-track-expiry=\"@(item.TrackExpiry ? \"true\" : \"false\")\"", view, StringComparison.Ordinal);
        Assert.Contains("data-track-serial=\"@(item.TrackSerial ? \"true\" : \"false\")\"", view, StringComparison.Ordinal);
        Assert.Contains("class=\"text-muted fs-xs expiry-policy-note\"", view, StringComparison.Ordinal);
        Assert.Contains("function syncExpiryPolicyForRow(row)", view, StringComparison.Ordinal);
        Assert.Contains("selectedItem?.dataset?.trackExpiry === 'true'", view, StringComparison.Ordinal);
        Assert.Contains("expiryInput.required = true;", view, StringComparison.Ordinal);
        Assert.Contains("expiryInput.disabled = true;", view, StringComparison.Ordinal);
        Assert.Contains("expiryInput.value = '';", view, StringComparison.Ordinal);
        Assert.Contains("Bắt buộc HSD", view, StringComparison.Ordinal);
        Assert.Contains("Không áp dụng", view, StringComparison.Ordinal);
        Assert.Contains("opt.dataset.trackExpiry = (item.TrackExpiry ?? item.trackExpiry) ? 'true' : 'false';", view, StringComparison.Ordinal);
        Assert.Contains("syncExpiryPolicyForRow(targetRow);", view, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateUi_ShouldRequireExplicitSourceLotLocationSelectionForOutboundFefo()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));
        var css = File.ReadAllText(Path.Combine(root, "wwwroot", "css", "site.css"));

        Assert.Contains("class=\"source-lot-input\"", view, StringComparison.Ordinal);
        Assert.Contains("class=\"source-expiry-input\"", view, StringComparison.Ordinal);
        Assert.Contains("class=\"fefo-override-reason-input\"", view, StringComparison.Ordinal);
        Assert.Contains("class=\"btn btn-xs btn-outline-primary source-location-btn\"", view, StringComparison.Ordinal);
        Assert.Contains("const isSourceStockVoucherJs", view, StringComparison.Ordinal);
        Assert.Contains("const canOverrideFefoJs", view, StringComparison.Ordinal);
        Assert.Contains("function clearSourceSelectionForRow(row)", view, StringComparison.Ordinal);
        Assert.Contains("function renderSourceSelectionNote(row, locationCode, lotNumber, expiryDate, isFefoRecommended, overrideReason)", view, StringComparison.Ordinal);
        Assert.Contains("requiredQty=${encodeURIComponent(qty)}", view, StringComparison.Ordinal);
        Assert.Contains("data-wms-json-args", view, StringComparison.Ordinal);
        Assert.Contains("Chỉ quản lý kho hoặc quản trị viên được chọn khác lô/vị trí FEFO", view, StringComparison.Ordinal);
        Assert.Contains("if (!isSourceStockVoucherJs && defaultLoc", view, StringComparison.Ordinal);
        Assert.DoesNotContain("if (defaultLoc && defaultLoc !== \"\" && defaultLoc !== \"0\") {\r\n                    locInput.value = defaultLoc;", view, StringComparison.Ordinal);
        Assert.Contains(".source-location-btn", css, StringComparison.Ordinal);
        Assert.Contains(".source-fefo-note", css, StringComparison.Ordinal);
        Assert.Contains(".chip-muted", css, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreatePost_ShouldRejectInactiveBaseOrUnmappedTransactionUom()
    {
        var root = FindRepositoryRoot();
        var index = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Index.cs"));
        var helpers = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Helpers.cs"));
        var exceptions = File.ReadAllText(Path.Combine(root, "Models", "CustomExceptions.cs"));

        Assert.Contains("lineItemIds.Contains(i.ItemId) && i.IsActive", index, StringComparison.Ordinal);
        Assert.Contains("!activeUomIds.Contains(item.BaseUomId)", index, StringComparison.Ordinal);
        Assert.Contains("WmsExceptions.ItemBaseUomInvalid(item.ItemCode)", index, StringComparison.Ordinal);
        Assert.Contains("WmsExceptions.TransactionUomInvalid(item.ItemCode)", index, StringComparison.Ordinal);
        Assert.Contains("uc.ConversionRate > 0m", index, StringComparison.Ordinal);
        Assert.Contains("uc.ConversionRate > 0m", helpers, StringComparison.Ordinal);
        Assert.Contains("ITEM_BASE_UOM_INVALID", exceptions, StringComparison.Ordinal);
        Assert.Contains("ITEM_UOM_INVALID", exceptions, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateVisualSpec_ShouldCheckItemDropdownDepthAndUomEnablement()
    {
        var root = FindRepositoryRoot();
        var spec = File.ReadAllText(Path.Combine(root, "tests", "visual", "wms-visual-regression.spec.ts"));

        Assert.Contains("itemValues.length, 'voucher create should expose more than the first three active items'", spec, StringComparison.Ordinal);
        Assert.Contains("wms-item-select-dropdown", spec, StringComparison.Ordinal);
        Assert.Contains("baseUom", spec, StringComparison.Ordinal);
        Assert.Contains("source UOM should be enabled after selecting", spec, StringComparison.Ordinal);
        Assert.Contains("getVoucherDocumentFixture", spec, StringComparison.Ordinal);
        Assert.Contains("fixture.item.itemCode", spec, StringComparison.Ordinal);
        Assert.Contains("select.item-select + .select2-container", spec, StringComparison.Ordinal);
        Assert.Contains("voucher create validation does not leave submit button loading", spec, StringComparison.Ordinal);
        Assert.Contains("await partnerSelect.selectOption('')", spec, StringComparison.Ordinal);
        Assert.Contains("await expect(submitButton).toBeEnabled()", spec, StringComparison.Ordinal);
        Assert.Contains("missing master-data fixture", spec, StringComparison.Ordinal);
        Assert.Contains("type: 'blocked'", spec, StringComparison.Ordinal);
        Assert.Contains("option:not([value=\"\"]):not([disabled])", spec, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateAndPutawayUi_ShouldNotLeakEnglishFallbackLabels()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));
        var helper = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Helpers.cs"));

        foreach (var source in new[] { view, helper })
        {
            Assert.DoesNotContain("Internal / unowned", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unowned", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Chủ hàng kho dịch vụ", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Nội bộ / chưa gán chủ hàng", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Fixed Bin", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fixed bin", source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("InventoryOwnershipMode", view, StringComparison.Ordinal);
        Assert.Contains("Loại sở hữu hàng", view, StringComparison.Ordinal);
        Assert.Contains("Nội bộ", view, StringComparison.Ordinal);
        Assert.Contains("Khách hàng thuê kho", view, StringComparison.Ordinal);
        Assert.Contains("Chưa tìm được vị trí phù hợp trong kho đang chọn", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Kho nội bộ", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Hàng 3PL", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Chủ hàng 3PL", view, StringComparison.Ordinal);
        Assert.Contains("Nhà cung cấp / nguồn giao", view, StringComparison.Ordinal);
        Assert.Contains("Khách hàng / nơi nhận", view, StringComparison.Ordinal);
        Assert.Contains("vị trí mặc định còn sức chứa", view, StringComparison.Ordinal);
        Assert.Contains("\"Vị trí mặc định\"", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreatePost_ShouldDefaultToInternalAndRequireOwnerOnlyForThreePlMode()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "ViewModels", "ViewModels.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Index.cs"));

        Assert.Contains("public string? InventoryOwnershipMode", viewModel, StringComparison.Ordinal);
        Assert.Contains("NormalizeInventoryOwnershipMode", controller, StringComparison.Ordinal);
        Assert.Contains("vm.InventoryOwnershipMode == \"Internal\"", controller, StringComparison.Ordinal);
        Assert.Contains("vm.OwnerPartnerId = null", controller, StringComparison.Ordinal);
        Assert.Contains("TENANT_OWNER_REQUIRED", controller, StringComparison.Ordinal);
        Assert.Contains("ownerPartnerId.HasValue ? \"ThreePl\" : \"Internal\"", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowProfilesUi_ShouldDisplayVietnameseBusinessLabels()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Operations", "WorkflowProfiles.cshtml"));

        Assert.Contains("WorkflowModuleLabel", view, StringComparison.Ordinal);
        Assert.Contains("WorkflowProfileLabel", view, StringComparison.Ordinal);
        Assert.Contains("Nhập kho và kiểm phẩm", view, StringComparison.Ordinal);
        Assert.Contains("Di chuyển có chỉ dẫn", view, StringComparison.Ordinal);
        Assert.Contains("Lấy hàng, đóng gói và xuất kho", view, StringComparison.Ordinal);
        Assert.Contains("Bàn giao đơn vận chuyển", view, StringComparison.Ordinal);
        Assert.Contains("Kiểm kê chu kỳ và điều chỉnh", view, StringComparison.Ordinal);
        Assert.Contains("Quy tắc vận hành kho", view, StringComparison.Ordinal);
        Assert.Contains("Phạm vi áp dụng", view, StringComparison.Ordinal);
        Assert.Contains("Toàn kho", view, StringComparison.Ordinal);
        Assert.Contains("Theo khách hàng thuê kho", view, StringComparison.Ordinal);
        Assert.Contains("WorkflowScopeLabel", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Theo chủ hàng 3PL", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Chủ hàng 3PL", view, StringComparison.Ordinal);
        Assert.DoesNotContain("@profile.ModuleKey", view, StringComparison.Ordinal);
        Assert.DoesNotContain("@profile.ProfileName", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Chủ hàng</th>", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Cấu hình quy trình", view, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowProfilePost_ShouldUseExplicitScopeModeWithoutChangingSchema()
    {
        var root = FindRepositoryRoot();
        var viewModel = File.ReadAllText(Path.Combine(root, "ViewModels", "Enterprise1113ViewModels.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "Controllers", "OperationsController.WorkflowProfiles.cs"));

        Assert.Contains("WorkflowScopeMode", viewModel, StringComparison.Ordinal);
        Assert.Contains("string? workflowScopeMode", controller, StringComparison.Ordinal);
        Assert.Contains("NormalizeWorkflowScopeMode", controller, StringComparison.Ordinal);
        Assert.Contains("ownerPartnerId = null", controller, StringComparison.Ordinal);
        Assert.Contains("THREEPL", controller, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsAllowedWorkflowModule", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherDetailsDockSeed_ShouldRenderAsJsonWithoutHtmlEntityDoubleEncoding()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Details.cshtml"));

        Assert.Contains("var dockSeedJson = System.Text.Json.JsonSerializer.Serialize(new", view, StringComparison.Ordinal);
        Assert.Contains("const dockSeed = @Html.Raw(dockSeedJson);", view, StringComparison.Ordinal);
        Assert.Contains("carrierName = Model.CarrierName ?? string.Empty", view, StringComparison.Ordinal);
        Assert.Contains("driverName = Model.DriverName ?? string.Empty", view, StringComparison.Ordinal);
        Assert.DoesNotContain("carrierName: '@(Model.CarrierName", view, StringComparison.Ordinal);
        Assert.DoesNotContain("driverName: '@(Model.DriverName", view, StringComparison.Ordinal);
        Assert.DoesNotContain("dockDoor: '@(Model.DockDoor", view, StringComparison.Ordinal);
        Assert.DoesNotContain("vehicleNumber: '@(Model.VehicleNumber", view, StringComparison.Ordinal);
        Assert.DoesNotContain("driverPhone: '@(Model.DriverPhone", view, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateView_ShouldExposeWarehouseAndFilterPutawayLocationsByWarehouse()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("id=\"WarehouseId\"", view, StringComparison.Ordinal);
        Assert.Contains("data-wms-change-call=\"warehouseChanged\"", view, StringComparison.Ordinal);
        Assert.Contains("data-default-wh", view, StringComparison.Ordinal);
        Assert.Contains("data-warehouse-id", view, StringComparison.Ordinal);
        Assert.Contains("function syncLocationSelectForWarehouse", view, StringComparison.Ordinal);
        Assert.Contains("function warehouseChanged", view, StringComparison.Ordinal);
        Assert.Contains("getValidLocationValueForWarehouse(putawaySelect", view, StringComparison.Ordinal);
        Assert.Contains("limitImportLocations(putawaySelect", view, StringComparison.Ordinal);
        Assert.Contains("syncAllLocationSelectsForWarehouse();", view, StringComparison.Ordinal);
        Assert.Contains("Chưa tìm được vị trí phù hợp trong kho đang chọn", view, StringComparison.Ordinal);
        Assert.Contains("skippedSuggestions", view, StringComparison.Ordinal);
        Assert.Contains("không thuộc kho đang chọn hoặc không còn hợp lệ trong danh sách", view, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelector('input[name=\"WarehouseId\"]')?.value", view, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreateView_ShouldOnlyAutoSelectServerValidatedPutawayLocation()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Views", "Vouchers", "Create.cshtml"));

        Assert.Contains("safeSuggestionIds.includes(validDefaultLocId)", view, StringComparison.Ordinal);
        Assert.Contains("putawaySelect.value = '';", view, StringComparison.Ordinal);
        Assert.Contains("putawayRequestId", view, StringComparison.Ordinal);
        Assert.DoesNotContain("allowedIds.push(validDefaultLocId);", view, StringComparison.Ordinal);
        Assert.DoesNotContain("putawaySelect.value = validDefaultPutawayLocation;", view, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreatePost_ShouldUsePutawaySpecificWarehouseMismatchMessageForInbound()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Index.cs"));
        var exceptions = File.ReadAllText(Path.Combine(root, "Models", "CustomExceptions.cs"));

        Assert.Contains("WmsExceptions.PutawayLocationNotInWarehouse(item.ItemCode)", controller, StringComparison.Ordinal);
        Assert.Contains("PUTAWAY_LOCATION_WRONG_WAREHOUSE", exceptions, StringComparison.Ordinal);
        Assert.Contains("WmsExceptions.PutawayLocationRequired(item.ItemCode)", controller, StringComparison.Ordinal);
        Assert.Contains("Vị trí cất hàng không thuộc kho nhận đã chọn", exceptions, StringComparison.Ordinal);
        Assert.Contains("Thiếu vị trí cất hàng", exceptions, StringComparison.Ordinal);
        Assert.Contains("PUTAWAY_LOCATION_REQUIRED", exceptions, StringComparison.Ordinal);
        Assert.DoesNotContain("Vị trí nguồn không thuộc kho đã chọn", exceptions, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherCreatePost_ShouldResolveInboundPutawayLocationInsideSelectedWarehouse()
    {
        var root = FindRepositoryRoot();
        var index = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Index.cs"));
        var helpers = File.ReadAllText(Path.Combine(root, "Controllers", "VouchersController.Helpers.cs"));

        Assert.Contains("ResolveInboundPutawayLocationAsync(item, vm.WarehouseId, voucher.OwnerPartnerId)", index, StringComparison.Ordinal);
        Assert.DoesNotContain("line.LocationId = item.DefaultLocationId;", index, StringComparison.Ordinal);
        Assert.Contains("l.Zone.WarehouseId == warehouseId", helpers, StringComparison.Ordinal);
        Assert.Contains("l.Zone.ZoneType == ZoneTypeEnum.Storage", helpers, StringComparison.Ordinal);
        Assert.Contains("il.ItemId != item.ItemId", helpers, StringComparison.Ordinal);
        Assert.Contains("hasOtherItem = hasOccupants && occupants is not null && occupants.Any(x => x.ItemId != req.ItemId || x.OwnerPartnerId != requestOwnerId)", helpers, StringComparison.Ordinal);
    }

    [Fact]
    public void WarehouseSuggestedLocations_ShouldOnlyReturnStorageBinsForRegularInboundPutaway()
    {
        var root = FindRepositoryRoot();
        var warehouses = File.ReadAllText(Path.Combine(root, "Controllers", "WarehousesController.cs"));

        Assert.Contains("l.Zone.ZoneType == ZoneTypeEnum.Storage", warehouses, StringComparison.Ordinal);
        Assert.DoesNotContain("l.Zone == null || (l.Zone.ZoneType != ZoneTypeEnum.Shipping && l.Zone.ZoneType != ZoneTypeEnum.Staging)", warehouses, StringComparison.Ordinal);
        Assert.Contains("LocationCode.Contains(\"Tank\", StringComparison.OrdinalIgnoreCase)", warehouses, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreUserFacingSources_ShouldNotContainKnownRawEnglishOrUnaccentedVietnameseFallbacks()
    {
        var root = FindRepositoryRoot();
        var directories = new[] { "Controllers", "Services", "Views", "Models" };
        var pattern = new Regex(@"\b(Khong|khong|duoc|tim thay|hop le)\b|Internal / unowned|unowned|Chủ hàng kho dịch vụ|Nội bộ / chưa gán chủ hàng|Fixed Bin|fixed bin|Chu hang|Vi tri nguon va dich phai khac nhau|LPN va vi tri dich phai thuoc cung kho|Cay LPN dang lech vi tri vat ly|Nhiem vu LPN thieu|LPN movement bat buoc xac nhan nguyen kien|LPN da thay doi trang thai hoac vi tri truoc khi xac nhan|Vui long quet ma LPN truoc khi xac nhan di chuyen|Kho la bat buoc khi lap lich cua ben|Cua .* da co lich|Cua .* da vuot nang luc|Gio ket thuc phai lon hon gio bat dau|Qua tai cua|Carrier chua co van don|Webhook can event|Can ly do khieu nai dong phi|Adapter code/name la bat buoc|Bulk pick task thieu vi tri staging|Chua cau hinh nang luc cua ben|Chua co DockDoorCapacity|Vui long nhap ma LPN|Da lap lich cua ben|Da doi lich|Da huy lich|Da check-in|Da check-out|Vui long chon file bang chung|File bang chung toi da|Chi ho tro anh|Da luu bang chung|Can file bang chung|Chua co webhook subscription|Da co nhiem vu LPN|Chua co cau hinh nang luc|Da dong bo|Da luu chuan|Da bat dau|Da hoan tat|Da xu ly|Da tao nhiem vu|Da luu he thong|LPN not found for inventory snapshot movement|LPN source snapshot row is missing|LPN source snapshot would become negative|(?-i:Kho nội bộ)|(?<!Chủ )Hàng 3PL|Theo chủ hàng 3PL|Chọn chủ hàng 3PL",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var relativeDir in directories)
        {
            var dir = Path.Combine(root, relativeDir);
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                         .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)))
            {
                var source = File.ReadAllText(file);
                var match = pattern.Match(source);
                Assert.False(match.Success, $"Known raw fallback '{match.Value}' remains in {Path.GetRelativePath(root, file)}.");
            }
        }
    }

    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{name}-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options) { SkipAudit = true };
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

        throw new DirectoryNotFoundException("Could not locate WMS.sln.");
    }
}
