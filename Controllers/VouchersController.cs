using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

using Microsoft.EntityFrameworkCore;

using WMS.Data;

using WMS.Models;

using WMS.ViewModels;

using WMS.Authorization;

using WMS.Common;

using WMS.Services;

using System.Text.Json;

using System.Linq;

using ClosedXML.Excel;

using System.Globalization;

using System.Data;

using Microsoft.Extensions.Logging.Abstractions;

namespace WMS.Controllers;

[Microsoft.AspNetCore.Authorization.Authorize]
public partial class VouchersController : Microsoft.AspNetCore.Mvc.Controller
{
    private readonly AppDbContext _db;

    private readonly IConfiguration _config;

    private readonly ILogger<VouchersController> _logger;

    private readonly IHttpClientFactory _httpClientFactory;

    private readonly IIntegrationService _integrationService;

    private readonly IInventoryReservationService _inventoryReservationService;

    private readonly IUnitOfWork _unitOfWork;

    private readonly IOutboundExecutionService _outboundExecutionService;

    private readonly IInboundExecutionService _inboundExecutionService;

    private readonly IInventoryBalanceService _inventoryBalanceService;

    private readonly IVoucherCancellationService _cancellationService;

    private readonly IOrderStreamingService _orderStreamingService;

    private readonly ISerialInventoryService _serialInventoryService;

    private readonly IInventoryTransactionService _inventoryTransactionService;

    private readonly ICatchWeightService _catchWeightService;

    private readonly IShipmentLoadService _shipmentLoadService;

    private readonly ICarrierIntegrationService _carrierIntegrationService;

    private readonly IVoucherDocumentIntakeService _voucherDocumentIntakeService;

    private readonly IVoucherSharedRuleService _voucherSharedRuleService;

    private readonly IVoucherImportQueryService _voucherImportQueryService;

    private readonly IVoucherCreateWorkflowService _voucherCreateWorkflowService;

    private readonly IVoucherDetailQueryService _voucherDetailQueryService;


    private static DateTime VietnamNow => VietnamTime.Now;


    public VouchersController(AppDbContext db, IConfiguration config, IHttpClientFactory httpClientFactory,
        IIntegrationService integrationService,
        IInventoryReservationService inventoryReservationService,
        IUnitOfWork unitOfWork,
        IOutboundExecutionService outboundExecutionService,
        IInboundExecutionService inboundExecutionService,
        IInventoryBalanceService inventoryBalanceService,
        IVoucherCancellationService cancellationService,
        IOrderStreamingService orderStreamingService,
        ISerialInventoryService serialInventoryService,
        IInventoryTransactionService inventoryTransactionService,
        ICatchWeightService catchWeightService,
        IShipmentLoadService shipmentLoadService,
        ICarrierIntegrationService carrierIntegrationService,
        IVoucherDocumentIntakeService voucherDocumentIntakeService,
        IVoucherSharedRuleService voucherSharedRuleService,
        IVoucherImportQueryService voucherImportQueryService,
        IVoucherCreateWorkflowService voucherCreateWorkflowService,
        IVoucherDetailQueryService voucherDetailQueryService,
        ILogger<VouchersController>? logger = null)
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _integrationService = integrationService;
        _logger = logger ?? NullLogger<VouchersController>.Instance;
        _inventoryReservationService = inventoryReservationService;
        _unitOfWork = unitOfWork;
        _inventoryBalanceService = inventoryBalanceService;
        _outboundExecutionService = outboundExecutionService;
        _inboundExecutionService = inboundExecutionService;
        _cancellationService = cancellationService;
        _orderStreamingService = orderStreamingService;
        _serialInventoryService = serialInventoryService;
        _inventoryTransactionService = inventoryTransactionService;
        _catchWeightService = catchWeightService;
        _shipmentLoadService = shipmentLoadService;
        _carrierIntegrationService = carrierIntegrationService;
        _voucherDocumentIntakeService = voucherDocumentIntakeService;
        _voucherSharedRuleService = voucherSharedRuleService;
        _voucherImportQueryService = voucherImportQueryService;
        _voucherCreateWorkflowService = voucherCreateWorkflowService;
        _voucherDetailQueryService = voucherDetailQueryService;
    }


    /// <summary>
    /// P1.1 — SoD enforcement: kiểm tra xem actor có phải là người tạo (maker) không.
    /// Nếu là maker, không được thực hiện các action verifier.
    /// </summary>
    private void EnforceSod(string createdBy, string verifierPermission)
        => _voucherSharedRuleService.EnforceSod(User, createdBy, verifierPermission);


    private int? GetScopedWarehouseId()
        => _voucherSharedRuleService.GetScopedWarehouseId(User);


    private bool CanSeeFinancial()
        => _voucherSharedRuleService.CanSeeFinancial(User);


    private List<int> GetOwnerScopeClaimIds()
        => _voucherSharedRuleService.GetOwnerScopeClaimIds(User);


    private IQueryable<Voucher> ApplyVoucherOwnerScope(IQueryable<Voucher> query)
    {
        var allowedOwnerIds = GetOwnerScopeClaimIds();
        return allowedOwnerIds.Count == 0
            ? query
            : query.Where(v => v.OwnerPartnerId.HasValue && allowedOwnerIds.Contains(v.OwnerPartnerId.Value));
    }


    private async Task EnsureVoucherOwnerScopeAsync(int? ownerPartnerId)
    {
        var allowed = GetOwnerScopeClaimIds();
        if (allowed.Count == 0)
            return;
        if (!ownerPartnerId.HasValue || !allowed.Contains(ownerPartnerId.Value))
            throw new UnauthorizedAccessException("Bạn không có quyền thao tác chủ hàng kho nhiều chủ hàng này.");
        var ownerOk = await _db.Partners.AnyAsync(p => p.PartnerId == ownerPartnerId.Value && p.IsThreePlClient && p.IsActive);
        if (!ownerOk)
            throw new BusinessRuleException("Chủ hàng không hợp lệ hoặc ngoài phạm vi được phân quyền.", "TENANT_OWNER_INVALID", "Voucher");
    }


    private async Task<IActionResult?> EnsureVoucherOwnerScopeResultAsync(int? ownerPartnerId)
    {
        try
        {
            await EnsureVoucherOwnerScopeAsync(ownerPartnerId);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }


    private static DateTime ResolveLockTransactionDate(Voucher voucher, DateTime? operationTime = null)
    {
        return voucher.CompletedAt
            ?? operationTime
            ?? voucher.VoucherDate;
    }


    private static bool IsLocked(DateTime transactionDate, DateTime? lockDate)
    {
        return lockDate.HasValue && transactionDate.Date <= lockDate.Value.Date;
    }


    private bool IsInboundVoucherType(VoucherTypeEnum voucherType)
        => _voucherSharedRuleService.IsInboundVoucherType(voucherType);


    private string? NormalizeText(string? value, bool toUpper = false)
        => _voucherSharedRuleService.NormalizeText(value, toUpper);


    private int GetRequiredSerialCount(decimal qty)
        => _voucherSharedRuleService.GetRequiredSerialCount(qty);


    private List<string> ParseSerialCodes(string? raw)
        => _voucherSharedRuleService.ParseSerialCodes(raw);


    private bool RequiresManifest(VoucherTypeEnum voucherType)
        => _voucherSharedRuleService.RequiresManifest(voucherType);


    private bool RequiresTrackingOrManifest(VoucherTypeEnum voucherType)
        => _voucherSharedRuleService.RequiresTrackingOrManifest(voucherType);


    private bool RequiresPartner(VoucherTypeEnum voucherType, ExportModeEnum exportMode)
        => _voucherSharedRuleService.RequiresPartner(voucherType, exportMode);


    private decimal? ResolveConversionRate(
        IEnumerable<UnitConversion> conversions,
        int itemId,
        int fromUomId,
        int toUomId)
        => _voucherSharedRuleService.ResolveConversionRate(conversions, itemId, fromUomId, toUomId);


    private sealed record FefoAllocation(int LocationId, string? LotNumber, DateTime? ExpiryDate, decimal Qty, bool IsPartial = false);

    private sealed record PickedReservationQty(long? VoucherDetailId, int ItemId, int LocationId, string? LotNumber, DateTime? ExpiryDate, decimal PickedQty);


    [Authorize(Roles = "Admin,Manager")]
    [HttpGet]
    public async Task<IActionResult> CreateAdjustmentFromSnapshot(int warehouseId, DateTime snapshotDate, int? ownerPartnerId = null)
    {
        snapshotDate = snapshotDate.Date;
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue && warehouseId != scopedWh.Value)
            return Forbid();
        var scopedOwnerIds = GetOwnerScopeClaimIds();
        if (!ownerPartnerId.HasValue && scopedOwnerIds.Count == 1)
            ownerPartnerId = scopedOwnerIds[0];
        if (ownerPartnerId.HasValue && await EnsureVoucherOwnerScopeResultAsync(ownerPartnerId) is IActionResult forbidden)
            return forbidden;

        var snapshotQuery = _db.StockSnapshots.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.SnapshotDate == snapshotDate);
        if (ownerPartnerId.HasValue)
            snapshotQuery = snapshotQuery.Where(s => s.OwnerPartnerId == ownerPartnerId.Value);
        else if (scopedOwnerIds.Count > 0)
            snapshotQuery = snapshotQuery.Where(s => s.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(s.OwnerPartnerId.Value));

        var snapshotRows = await snapshotQuery.ToListAsync();

        if (snapshotRows.Count == 0)
        {
            TempData["Error"] = "Chưa có snapshot cho kho/ngày đã chọn. Vui lòng chốt tồn trước.";
            return RedirectToAction("StockSnapshot", "Reports", new { warehouseId, snapshotDate, ownerPartnerId });
        }

        var snapshotOwnerIds = snapshotRows.Select(s => s.OwnerPartnerId).Distinct().ToList();
        if (snapshotOwnerIds.Count > 1)
        {
            TempData["Error"] = "Snapshot đang có nhiều chủ hàng. Vui lòng lọc/chốt theo một chủ hàng trước khi tạo phiếu điều chỉnh.";
            return RedirectToAction("StockSnapshot", "Reports", new { warehouseId, snapshotDate, ownerPartnerId });
        }
        var adjustmentOwnerId = snapshotOwnerIds.Single();

        var currentStocks = await _db.ItemLocations.AsNoTracking()
            .Include(il => il.Location).ThenInclude(l => l!.Zone)
            .Where(il => il.Quantity != 0
                && il.Location != null
                && il.Location.Zone != null
                && il.Location.Zone.WarehouseId == warehouseId
                && il.OwnerPartnerId == adjustmentOwnerId)
            .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
            .Select(g => new { g.Key.ItemId, g.Key.OwnerPartnerId, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => (x.ItemId, x.OwnerPartnerId), x => x.Qty);

        var itemIds = snapshotRows.Select(s => s.ItemId).Distinct().ToList();
        var items = await _db.Items.AsNoTracking().Where(i => i.IsActive && itemIds.Contains(i.ItemId)).ToListAsync();
        var itemById = items.ToDictionary(i => i.ItemId, i => i);

        // Location suggestion for decrease: pick a location in the warehouse that has stock for the item
        var stockLocs = await _db.ItemLocations.AsNoTracking()
            .Include(il => il.Location).ThenInclude(l => l!.Zone)
            .Where(il => il.Quantity > 0
                && il.Location != null
                && il.Location.Zone != null
                && il.Location.Zone.WarehouseId == warehouseId
                && il.OwnerPartnerId == adjustmentOwnerId
                && itemIds.Contains(il.ItemId))
            .OrderByDescending(il => il.Quantity)
            .ToListAsync();

        var bestLocByItem = stockLocs
            .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
            .ToDictionary(g => (g.Key.ItemId, g.Key.OwnerPartnerId), g => g.First().LocationId);

        var lines = new List<VoucherDetailLine>();
        foreach (var s in snapshotRows)
        {
            if (!itemById.TryGetValue(s.ItemId, out var item)) continue;
            var stockKey = (s.ItemId, s.OwnerPartnerId);
            var currentQty = currentStocks.TryGetValue(stockKey, out var q) ? q : 0m;
            var diff = s.ClosingStock - currentQty; // needed adjustment to match snapshot
            if (diff == 0) continue;

            var sign = diff > 0 ? (sbyte)1 : (sbyte)-1;
            var abs = Math.Abs(diff);

            lines.Add(new VoucherDetailLine
            {
                ItemId = s.ItemId,
                TransactionQty = abs,
                TransactionUomId = item.BaseUomId,
                AdjustSign = sign,
                LocationId = sign < 0
                    ? (bestLocByItem.TryGetValue(stockKey, out var locId) ? locId : item.DefaultLocationId)
                    : (item.DefaultLocationId ?? (bestLocByItem.TryGetValue(stockKey, out var locId2) ? locId2 : null)),
                UnitPrice = item.UnitCost,
                LineAmount = item.UnitCost * abs,
                Notes = $"Điều chỉnh theo snapshot {snapshotDate:dd/MM/yyyy}"
            });
        }

        var vm = new VoucherCreateViewModel
        {
            VoucherType = VoucherTypeEnum.DieuChinh,
            WarehouseId = warehouseId,
            OwnerPartnerId = adjustmentOwnerId,
            InventoryOwnershipMode = adjustmentOwnerId.HasValue ? "ThreePl" : "Internal",
            ReferenceNo = $"SNAP-{snapshotDate:yyyyMMdd}",
            Description = $"Điều chỉnh tồn theo snapshot ngày {snapshotDate:dd/MM/yyyy}",
            Lines = lines
        };

        // Populate dropdown data as in Create()
        vm.Warehouses = await _db.Warehouses.Where(w => w.IsActive).ToListAsync();
        vm.Partners = await _db.Partners.Where(p => p.IsActive).ToListAsync();
        vm.Items = await _db.Items.Include(i => i.BaseUom).Where(i => i.IsActive).OrderBy(i => i.ItemCode).ToListAsync();
        vm.Uoms = await _db.UnitsOfMeasure.Where(u => u.IsActive).ToListAsync();
        vm.Locations = await _db.Locations.Where(l => l.IsActive).ToListAsync();
        vm.PackagingUnits = await _db.PackagingUnits.Include(p => p.BaseUom).Where(p => p.IsActive).OrderBy(p => p.TenDongGoi).ToListAsync();

        TempData["Info"] = $"Đã tạo nháp phiếu điều chỉnh theo snapshot {snapshotDate:dd/MM/yyyy}. Vui lòng kiểm tra và lưu.";
        ViewBag.CanSeeFinancial = CanSeeFinancial();
        await PopulateVoucherCreateMetadataAsync(vm);
        return View("Create", vm);
    }


    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff,InboundStaff,OutboundStaff,InventoryStaff,TransportStaff")]
    public IActionResult DownloadImportTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(VoucherImportWorksheetName);

        for (var column = 1; column <= VoucherImportHeaders.Length; column++)
            ws.Cell(1, column).Value = VoucherImportHeaders[column - 1];

        ws.Range(1, 1, 1, 10).Style.Font.Bold = true;
        ws.Range(1, 1, 1, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#111827");
        ws.Range(1, 1, 1, 10).Style.Font.FontColor = XLColor.White;

        ws.Cell(2, 1).Value = "VT-001";
        ws.Cell(2, 2).Value = "Bu-lông neo M20x500";
        ws.Cell(2, 3).Value = 10;
        ws.Cell(2, 4).Value = 0;
        ws.Cell(2, 5).Value = "Pcs";
        ws.Cell(2, 6).Value = "A1-01";
        ws.Cell(2, 7).Value = "";
        ws.Cell(2, 8).Value = "";
        ws.Cell(2, 9).Value = 0;
        ws.Cell(2, 10).Value = "";

        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, 2, VoucherImportHeaders.Length).SetAutoFilter();
        ws.Column(7).Style.DateFormat.Format = "yyyy-MM-dd";
        ws.Columns().AdjustToContents();

        var metadata = wb.Worksheets.Add(VoucherImportMetadataWorksheetName);
        metadata.Cell(1, 1).Value = "TemplateType";
        metadata.Cell(1, 2).Value = "TemplateVersion";
        metadata.Cell(1, 3).Value = "GeneratedAtUtc";
        metadata.Cell(2, 1).Value = "VoucherLines";
        metadata.Cell(2, 2).Value = VoucherImportTemplateVersion;
        metadata.Cell(2, 3).Value = VietnamTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        metadata.Visibility = XLWorksheetVisibility.VeryHidden;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "WMS_ImportLines_Template.xlsx");
    }


    public class PutawayRequest
    {
        public int ItemId { get; set; }
        public int RowIndex { get; set; }
        public int? WarehouseId { get; set; }
        public int? OwnerPartnerId { get; set; }
        public decimal? Quantity { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

}
