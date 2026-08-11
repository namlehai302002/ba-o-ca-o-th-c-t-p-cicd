using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

using Microsoft.EntityFrameworkCore;

using WMS.Data;

using WMS.ViewModels;

using ClosedXML.Excel;

using System.IO;

using WMS.Models;

using System.Data;

using WMS.Authorization;

using WMS.Common;

using WMS.Services;

using Microsoft.Extensions.Logging.Abstractions;

namespace WMS.Controllers;

public partial class ReportsController
{

    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> StockMovement(
        int? itemId,
        int? warehouseId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        dateFrom ??= VietnamNow.Date.AddDays(-30);
        dateTo ??= VietnamNow.Date;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        var query = _db.VoucherDetails
            .AsNoTracking()
            .Include(vd => vd.Voucher).ThenInclude(v => v!.Warehouse)
            .Include(vd => vd.Item).ThenInclude(i => i!.BaseUom)
            .Include(vd => vd.Location)
            .Include(vd => vd.DestLocation)
            .Include(vd => vd.TransactionUom)
            .Where(vd => vd.Voucher != null
                && !vd.Voucher.IsCancelled
                && vd.Voucher.IsPosted
                && vd.Voucher.VoucherDate >= dateFrom.Value
                && vd.Voucher.VoucherDate <= dateTo.Value);

        if (itemId.HasValue)
            query = query.Where(vd => vd.ItemId == itemId.Value);
        if (warehouseId.HasValue)
            query = query.Where(vd => vd.Voucher!.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            query = query.Where(vd => (vd.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(vd.OwnerPartnerId.Value))
                || (vd.Voucher!.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(vd.Voucher.OwnerPartnerId.Value)));

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var data = await query
            .OrderByDescending(vd => vd.Voucher!.VoucherDate)
            .ThenByDescending(vd => vd.VoucherId)
            .ThenBy(vd => vd.LineNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        ViewBag.Items = await _db.Items.AsNoTracking()
            .Where(i => i.IsActive)
            .OrderBy(i => i.ItemCode)
            .ToListAsync(cancellationToken);
        ViewBag.Warehouses = await _db.Warehouses.AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.WarehouseCode)
            .ToListAsync(cancellationToken);
        ViewBag.ItemId = itemId;
        ViewBag.WarehouseId = warehouseId;
        ViewBag.DateFrom = dateFrom;
        ViewBag.DateTo = dateTo;
        ViewBag.Data = data;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = totalPages;

        return View();
    }


    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> ExportStockMovement(int? itemId, int? warehouseId, DateTime? dateFrom, DateTime? dateTo)
    {
        dateFrom ??= VietnamNow.Date.AddDays(-30);
        dateTo ??= VietnamNow.Date;

        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        var query = _db.VoucherDetails.AsNoTracking()
            .Include(vd => vd.Voucher).ThenInclude(v => v!.Warehouse)
            .Include(vd => vd.Item).ThenInclude(i => i!.BaseUom)
            .Include(vd => vd.Location)
            .Include(vd => vd.DestLocation)
            .Include(vd => vd.TransactionUom)
            .Where(vd => vd.Voucher != null
                && !vd.Voucher.IsCancelled
                && vd.Voucher.IsPosted
                && vd.Voucher.VoucherDate >= dateFrom.Value
                && vd.Voucher.VoucherDate <= dateTo.Value);

        if (itemId.HasValue)
            query = query.Where(vd => vd.ItemId == itemId.Value);
        if (warehouseId.HasValue)
            query = query.Where(vd => vd.Voucher!.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            query = query.Where(vd => (vd.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(vd.OwnerPartnerId.Value))
                || (vd.Voucher!.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(vd.Voucher.OwnerPartnerId.Value)));

        var data = await query
            .OrderByDescending(vd => vd.Voucher!.VoucherDate)
            .ThenBy(vd => vd.LineNumber)
            .Take(2000)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("XuatNhapTon");

        var row = 1;
        ws.Cell(row, 1).Value = "Ngày";
        ws.Cell(row, 2).Value = "Mã phiếu";
        ws.Cell(row, 3).Value = "Loại phiếu";
        ws.Cell(row, 4).Value = "Kho";
        ws.Cell(row, 5).Value = "Mã VT";
        ws.Cell(row, 6).Value = "Tên VT";
        ws.Cell(row, 7).Value = "Lô";
        ws.Cell(row, 8).Value = "NSX";
        ws.Cell(row, 9).Value = "HSD";
        ws.Cell(row, 10).Value = "Vị trí nguồn";
        ws.Cell(row, 11).Value = "Vị trí đích";
        ws.Cell(row, 12).Value = "SL (+/-)";
        ws.Cell(row, 13).Value = "ĐVT";

        ws.Range("A1:M1").Style.Font.Bold = true;
        ws.Range("A1:M1").Style.Fill.BackgroundColor = XLColor.AirForceBlue;
        ws.Range("A1:M1").Style.Font.FontColor = XLColor.White;

        foreach (var d in data)
        {
            row++;
            var v = d.Voucher!;

            ws.Cell(row, 1).Value = v.VoucherDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = v.VoucherCode;
            ws.Cell(row, 3).Value = v.VoucherTypeName;
            ws.Cell(row, 4).Value = v.Warehouse?.WarehouseName ?? "";
            ws.Cell(row, 5).Value = d.Item?.ItemCode ?? "";
            ws.Cell(row, 6).Value = d.Item?.ItemName ?? "";

            var signedQty = v.VoucherType switch
            {
                VoucherTypeEnum.NhapKho or VoucherTypeEnum.KhachTra or VoucherTypeEnum.NhapThanhPham => d.BaseQty,
                VoucherTypeEnum.XuatKho or VoucherTypeEnum.TraNCC or VoucherTypeEnum.XuatSanXuat => -d.BaseQty,
                VoucherTypeEnum.DieuChinh => d.BaseQty, // already carries sign (+/-)
                VoucherTypeEnum.ChuyenKho => 0m,        // transfer does not change total stock
                _ => 0m
            };
            ws.Cell(row, 7).Value = d.LotNumber ?? "";
            ws.Cell(row, 8).Value = d.ManufacturingDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(row, 9).Value = d.ExpiryDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(row, 10).Value = d.Location?.LocationCode ?? "";
            ws.Cell(row, 11).Value = d.DestLocation?.LocationCode ?? "";
            ws.Cell(row, 12).Value = signedQty;
            ws.Cell(row, 13).Value = d.Item?.BaseUom?.UomCode ?? d.TransactionUom?.UomCode ?? "Không áp dụng";
        }

        ws.Column(12).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();

        var fileName = $"XuatNhapTon_{VietnamNow:yyyyMMdd_HHmm}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> InventoryInOutSummary(
        DateTime? dateFrom,
        DateTime? dateTo,
        int? warehouseId,
        int? itemId,
        int? categoryId,
        int? locationId,
        int? partnerId,
        string movementType = "All",
        string? lotNumber = null)
    {
        var dateRangeError = ValidateInventoryInOutDateRange(dateFrom, dateTo);
        var model = await BuildInventoryInOutSummaryModelAsync(
            dateFrom,
            dateTo,
            warehouseId,
            itemId,
            categoryId,
            locationId,
            partnerId,
            movementType,
            lotNumber,
            skipRows: dateRangeError != null);

        if (dateRangeError != null)
            ViewBag.DateRangeError = dateRangeError;

        return View(model);
    }

    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> ExportInventoryInOutSummary(
        DateTime? dateFrom,
        DateTime? dateTo,
        int? warehouseId,
        int? itemId,
        int? categoryId,
        int? locationId,
        int? partnerId,
        string movementType = "All",
        string? lotNumber = null)
    {
        var dateRangeError = ValidateInventoryInOutDateRange(dateFrom, dateTo);
        if (dateRangeError != null)
            return BadRequest(dateRangeError);

        var model = await BuildInventoryInOutSummaryModelAsync(
            dateFrom,
            dateTo,
            warehouseId,
            itemId,
            categoryId,
            locationId,
            partnerId,
            movementType,
            lotNumber);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("NhapXuatTheoKy");
        var headers = new[]
        {
            "Ngày chứng từ", "Ngày ghi sổ", "Mã phiếu", "Loại phiếu", "Đối tác",
            "Mã vật tư", "Tên vật tư", "Danh mục", "Lô", "NSX", "HSD", "Ngày nhập nguồn",
            "Kho", "Vị trí nguồn", "Vị trí đích", "SL nhập", "SL xuất", "ĐVT",
            "Người lập", "Người duyệt/xác nhận", "Ghi chú"
        };

        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.AirForceBlue;
        ws.Range(1, 1, 1, headers.Length).Style.Font.FontColor = XLColor.White;

        var row = 1;
        foreach (var line in model.Rows)
        {
            row++;
            ws.Cell(row, 1).Value = line.DocumentDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(row, 2).Value = line.TransactionAt.ToString("dd/MM/yyyy HH:mm");
            ws.Cell(row, 3).Value = line.VoucherCode;
            ws.Cell(row, 4).Value = line.VoucherTypeName;
            ws.Cell(row, 5).Value = line.PartnerName;
            ws.Cell(row, 6).Value = line.ItemCode;
            ws.Cell(row, 7).Value = line.ItemName;
            ws.Cell(row, 8).Value = line.CategoryName;
            ws.Cell(row, 9).Value = line.LotNumber ?? "";
            ws.Cell(row, 10).Value = line.ManufacturingDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(row, 11).Value = line.ExpiryDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(row, 12).Value = line.SourceReceiveDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(row, 13).Value = line.WarehouseName;
            ws.Cell(row, 14).Value = line.SourceLocationCode;
            ws.Cell(row, 15).Value = line.DestinationLocationCode;
            ws.Cell(row, 16).Value = line.InboundQty;
            ws.Cell(row, 17).Value = line.OutboundQty;
            ws.Cell(row, 18).Value = line.UomCode;
            ws.Cell(row, 19).Value = line.Actor;
            ws.Cell(row, 20).Value = line.Approver;
            ws.Cell(row, 21).Value = line.Notes;
        }

        var totalRow = row + 2;
        ws.Cell(totalRow, 15).Value = "Tổng cộng";
        ws.Cell(totalRow, 16).Value = model.TotalInboundQty;
        ws.Cell(totalRow, 17).Value = model.TotalOutboundQty;
        ws.Range(totalRow, 15, totalRow, 17).Style.Font.Bold = true;

        ws.Columns(16, 17).Style.NumberFormat.Format = "#,##0.####";
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();
        var fileName = $"ThongKeNhapXuatTheoKy_{VietnamNow:yyyyMMdd_HHmm}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private async Task<InventoryInOutSummaryPageViewModel> BuildInventoryInOutSummaryModelAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        int? warehouseId,
        int? itemId,
        int? categoryId,
        int? locationId,
        int? partnerId,
        string movementType,
        string? lotNumber,
        bool skipRows = false)
    {
        var from = dateFrom?.Date ?? VietnamNow.Date.AddDays(-30);
        var to = dateTo?.Date ?? VietnamNow.Date;
        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue)
            warehouseId = scopedWh.Value;

        var model = new InventoryInOutSummaryPageViewModel
        {
            DateFrom = from,
            DateTo = to,
            WarehouseId = warehouseId,
            ItemId = itemId,
            CategoryId = categoryId,
            LocationId = locationId,
            PartnerId = partnerId,
            MovementType = NormalizeInventoryMovementType(movementType),
            LotNumber = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim()
        };

        model.Warehouses = await _db.Warehouses.AsNoTracking()
            .Where(w => w.IsActive)
            .Where(w => !scopedWh.HasValue || w.WarehouseId == scopedWh.Value)
            .OrderBy(w => w.WarehouseCode)
            .ToListAsync();
        model.Items = await _db.Items.AsNoTracking()
            .Where(i => i.IsActive)
            .Where(i => scopedOwnerIds.Count == 0 || (i.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(i.OwnerPartnerId.Value)))
            .OrderBy(i => i.ItemCode)
            .ToListAsync();
        model.Categories = await _db.ItemCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CategoryName)
            .ToListAsync();
        model.Locations = await _db.Locations.AsNoTracking()
            .OrderBy(l => l.LocationCode)
            .ToListAsync();
        model.Partners = await _db.Partners.AsNoTracking()
            .Where(p => p.IsActive)
            .Where(p => scopedOwnerIds.Count == 0 || scopedOwnerIds.Contains(p.PartnerId))
            .OrderBy(p => p.PartnerCode)
            .ToListAsync();

        if (skipRows || from > to)
            return model;

        var toExclusive = to.AddDays(1);
        var query = _db.InventoryTransactions.AsNoTracking()
            .Include(t => t.Warehouse)
            .Include(t => t.OwnerPartner)
            .Include(t => t.Item).ThenInclude(i => i!.BaseUom)
            .Include(t => t.Item).ThenInclude(i => i!.Category)
            .Include(t => t.Location)
            .Include(t => t.Voucher).ThenInclude(v => v!.Partner)
            .Include(t => t.Voucher)
            .Include(t => t.VoucherDetail).ThenInclude(d => d!.DestLocation)
            .Include(t => t.VoucherDetail).ThenInclude(d => d!.TransactionUom)
            .Where(t => t.TransactionAt >= from && t.TransactionAt < toExclusive);
        if (scopedOwnerIds.Count > 0)
            query = query.Where(t => t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value));

        if (warehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        if (itemId.HasValue)
            query = query.Where(t => t.ItemId == itemId.Value);
        if (categoryId.HasValue)
            query = query.Where(t => t.Item != null && t.Item.CategoryId == categoryId.Value);
        if (locationId.HasValue)
            query = query.Where(t => t.LocationId == locationId.Value || (t.VoucherDetail != null && t.VoucherDetail.DestLocationId == locationId.Value));
        if (partnerId.HasValue)
            query = query.Where(t => (t.Voucher != null && t.Voucher.PartnerId == partnerId.Value) || t.OwnerPartnerId == partnerId.Value);
        if (!string.IsNullOrWhiteSpace(model.LotNumber))
            query = query.Where(t => t.LotNumber != null && t.LotNumber.Contains(model.LotNumber));

        query = ApplyInventoryMovementTypeFilter(query, model.MovementType);

        var transactions = await query
            .OrderByDescending(t => t.TransactionAt)
            .ThenByDescending(t => t.InventoryTransactionId)
            .Take(5000)
            .ToListAsync();

        model.Rows = transactions.Select(MapInventoryInOutSummaryRow).ToList();
        return model;
    }

    private static IQueryable<InventoryTransaction> ApplyInventoryMovementTypeFilter(IQueryable<InventoryTransaction> query, string movementType)
        => movementType switch
        {
            "Inbound" => query.Where(t => t.QuantityDelta > 0),
            "Outbound" => query.Where(t => t.QuantityDelta < 0),
            "Transfer" => query.Where(t => t.TransactionType == InventoryTransactionTypeEnum.TransferIn
                || t.TransactionType == InventoryTransactionTypeEnum.TransferOut
                || t.TransactionType == InventoryTransactionTypeEnum.Move),
            "Adjustment" => query.Where(t => t.TransactionType == InventoryTransactionTypeEnum.Adjust
                || t.TransactionType == InventoryTransactionTypeEnum.Reconcile
                || t.TransactionType == InventoryTransactionTypeEnum.Cancel)
                .Where(t => t.QuantityDelta != 0),
            _ => query.Where(t => t.QuantityDelta != 0)
        };

    private static string? ValidateInventoryInOutDateRange(DateTime? dateFrom, DateTime? dateTo)
    {
        if (!dateFrom.HasValue || !dateTo.HasValue)
            return "Vui lòng chọn đầy đủ Từ ngày và Đến ngày trước khi thống kê.";

        if (dateFrom.Value.Date > dateTo.Value.Date)
            return "Khoảng ngày không hợp lệ. Vui lòng chọn Từ ngày nhỏ hơn hoặc bằng Đến ngày.";

        return null;
    }

    private static InventoryInOutSummaryRow MapInventoryInOutSummaryRow(InventoryTransaction transaction)
    {
        var voucher = transaction.Voucher;
        var detail = transaction.VoucherDetail;
        var destinationCode = detail?.DestLocation?.LocationCode ?? "";
        var sourceCode = transaction.Location?.LocationCode ?? "";
        var inboundQty = transaction.QuantityDelta > 0 ? transaction.QuantityDelta : 0m;
        var outboundQty = transaction.QuantityDelta < 0 ? Math.Abs(transaction.QuantityDelta) : 0m;

        return new InventoryInOutSummaryRow
        {
            InventoryTransactionId = transaction.InventoryTransactionId,
            TransactionAt = transaction.TransactionAt,
            DocumentDate = voucher?.VoucherDate,
            VoucherCode = voucher?.VoucherCode ?? transaction.ReferenceCode ?? transaction.ReferenceId ?? "",
            VoucherId = transaction.VoucherId,
            VoucherTypeName = voucher?.VoucherTypeName ?? TransactionTypeLabel(transaction.TransactionType),
            MovementType = InventoryMovementTypeLabel(transaction),
            PartnerName = voucher?.Partner != null
                ? $"{voucher.Partner.PartnerCode} - {voucher.Partner.PartnerName}"
                : transaction.OwnerPartner?.PartnerName ?? "",
            ItemId = transaction.ItemId,
            ItemCode = transaction.Item?.ItemCode ?? transaction.ItemId.ToString(),
            ItemName = transaction.Item?.ItemName ?? "",
            CategoryName = transaction.Item?.Category?.CategoryName ?? "Chưa phân loại",
            LotNumber = transaction.LotNumber ?? detail?.LotNumber,
            ManufacturingDate = detail?.ManufacturingDate,
            ExpiryDate = transaction.ExpiryDate ?? detail?.ExpiryDate,
            SourceReceiveDate = ResolveSourceReceiveDate(transaction),
            WarehouseName = transaction.Warehouse != null
                ? $"{transaction.Warehouse.WarehouseCode} - {transaction.Warehouse.WarehouseName}"
                : transaction.WarehouseId.ToString(),
            SourceLocationCode = sourceCode,
            DestinationLocationCode = string.IsNullOrWhiteSpace(destinationCode) && inboundQty > 0 ? sourceCode : destinationCode,
            InboundQty = inboundQty,
            OutboundQty = outboundQty,
            UomCode = detail?.TransactionUom?.UomCode ?? transaction.Item?.BaseUom?.UomCode ?? "",
            Actor = string.IsNullOrWhiteSpace(transaction.Actor) ? voucher?.CreatedBy ?? "" : transaction.Actor,
            Approver = voucher?.ApprovedBy ?? voucher?.ReviewedBy ?? voucher?.CompletedBy ?? "",
            Notes = detail?.Notes ?? voucher?.Description ?? transaction.ReferenceType ?? ""
        };
    }

    private static string NormalizeInventoryMovementType(string? movementType)
        => movementType?.Trim() switch
        {
            "Inbound" or "Nhap" or "Nhập" => "Inbound",
            "Outbound" or "Xuat" or "Xuất" => "Outbound",
            "Transfer" or "DieuChuyen" or "Điều chuyển" => "Transfer",
            "Adjustment" or "DieuChinh" or "Điều chỉnh" => "Adjustment",
            _ => "All"
        };

    private static string InventoryMovementTypeLabel(InventoryTransaction transaction)
    {
        if (transaction.TransactionType is InventoryTransactionTypeEnum.TransferIn or InventoryTransactionTypeEnum.TransferOut or InventoryTransactionTypeEnum.Move)
            return "Điều chuyển";
        if (transaction.TransactionType is InventoryTransactionTypeEnum.Adjust or InventoryTransactionTypeEnum.Reconcile or InventoryTransactionTypeEnum.Cancel)
            return "Điều chỉnh";
        if (transaction.QuantityDelta > 0)
            return "Nhập";
        if (transaction.QuantityDelta < 0)
            return "Xuất";
        return TransactionTypeLabel(transaction.TransactionType);
    }

    private static string TransactionTypeLabel(InventoryTransactionTypeEnum type)
        => type switch
        {
            InventoryTransactionTypeEnum.OpeningBalance => "Số dư đầu kỳ",
            InventoryTransactionTypeEnum.Receive => "Nhận hàng",
            InventoryTransactionTypeEnum.Putaway => "Cất hàng",
            InventoryTransactionTypeEnum.Move => "Di chuyển",
            InventoryTransactionTypeEnum.Pick => "Giữ hàng để lấy",
            InventoryTransactionTypeEnum.Pack => "Đóng gói",
            InventoryTransactionTypeEnum.Ship => "Giao hàng",
            InventoryTransactionTypeEnum.Adjust => "Điều chỉnh",
            InventoryTransactionTypeEnum.Cancel => "Hủy nghiệp vụ",
            InventoryTransactionTypeEnum.TransferIn => "Nhập chuyển kho",
            InventoryTransactionTypeEnum.TransferOut => "Xuất chuyển kho",
            InventoryTransactionTypeEnum.Hold => "Khóa giữ hàng",
            InventoryTransactionTypeEnum.ReleaseHold => "Mở khóa giữ hàng",
            InventoryTransactionTypeEnum.Reconcile => "Đối soát tồn kho",
            InventoryTransactionTypeEnum.KitConsume => "Tiêu hao để lắp bộ hàng",
            InventoryTransactionTypeEnum.KitProduce => "Tạo thành phẩm bộ",
            InventoryTransactionTypeEnum.VasConsume => "Tiêu hao dịch vụ gia tăng",
            _ => "Không xác định"
        };

    private static DateTime? ResolveSourceReceiveDate(InventoryTransaction transaction)
    {
        if (transaction.QuantityDelta > 0)
            return transaction.TransactionAt.Date;
        if (transaction.Voucher?.VoucherDate is DateTime voucherDate)
            return voucherDate;
        return null;
    }


    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> InventoryTransactions(
        int? itemId,
        int? warehouseId,
        int? locationId,
        InventoryTransactionTypeEnum? transactionType,
        string? referenceType,
        string? referenceCode,
        long? licensePlateId,
        long? serialNumberId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var from = dateFrom?.Date ?? VietnamNow.Date.AddDays(-30);
        var to = dateTo?.Date ?? VietnamNow.Date;
        var toExclusive = to.AddDays(1);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue)
            warehouseId = scopedWh.Value;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        var query = _db.InventoryTransactions
            .AsNoTracking()
            .Include(t => t.Warehouse)
            .Include(t => t.Item)
            .Include(t => t.Location)
            .Include(t => t.LicensePlate)
            .Include(t => t.SerialNumber)
            .Where(t => t.TransactionAt >= from && t.TransactionAt < toExclusive);

        if (itemId.HasValue)
            query = query.Where(t => t.ItemId == itemId.Value);
        if (warehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            query = query.Where(t => t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value));
        if (locationId.HasValue)
            query = query.Where(t => t.LocationId == locationId.Value);
        if (transactionType.HasValue)
            query = query.Where(t => t.TransactionType == transactionType.Value);
        if (!string.IsNullOrWhiteSpace(referenceType))
            query = query.Where(t => t.ReferenceType == referenceType.Trim());
        if (!string.IsNullOrWhiteSpace(referenceCode))
        {
            var cleanReference = referenceCode.Trim();
            query = query.Where(t => t.ReferenceCode != null && t.ReferenceCode.Contains(cleanReference));
        }
        if (licensePlateId.HasValue)
            query = query.Where(t => t.LicensePlateId == licensePlateId.Value);
        if (serialNumberId.HasValue)
            query = query.Where(t => t.SerialNumberId == serialNumberId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var data = await query
            .OrderByDescending(t => t.TransactionAt)
            .ThenByDescending(t => t.InventoryTransactionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        ViewBag.Items = await _db.Items.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.ItemCode).ToListAsync(cancellationToken);
        ViewBag.Warehouses = await _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync(cancellationToken);
        ViewBag.Locations = await _db.Locations.AsNoTracking().OrderBy(l => l.LocationCode).ToListAsync(cancellationToken);
        ViewBag.TransactionTypes = Enum.GetValues<InventoryTransactionTypeEnum>();
        ViewBag.ItemId = itemId;
        ViewBag.WarehouseId = warehouseId;
        ViewBag.LocationId = locationId;
        ViewBag.TransactionType = transactionType;
        ViewBag.ReferenceType = referenceType;
        ViewBag.ReferenceCode = referenceCode;
        ViewBag.LicensePlateId = licensePlateId;
        ViewBag.SerialNumberId = serialNumberId;
        ViewBag.DateFrom = from;
        ViewBag.DateTo = to;
        ViewBag.Data = data;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = totalPages;

        return View();
    }


    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> ExportInventoryTransactions(
        int? itemId,
        int? warehouseId,
        int? locationId,
        InventoryTransactionTypeEnum? transactionType,
        string? referenceType,
        string? referenceCode,
        long? licensePlateId,
        long? serialNumberId,
        DateTime? dateFrom,
        DateTime? dateTo)
    {
        var from = dateFrom?.Date ?? VietnamNow.Date.AddDays(-30);
        var to = dateTo?.Date ?? VietnamNow.Date;
        var toExclusive = to.AddDays(1);
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue)
            warehouseId = scopedWh.Value;
        var scopedOwnerIds = GetScopedOwnerPartnerIds();

        var query = _db.InventoryTransactions
            .AsNoTracking()
            .Include(t => t.Warehouse)
            .Include(t => t.Item)
            .Include(t => t.Location)
            .Include(t => t.LicensePlate)
            .Include(t => t.SerialNumber)
            .Where(t => t.TransactionAt >= from && t.TransactionAt < toExclusive);

        if (itemId.HasValue)
            query = query.Where(t => t.ItemId == itemId.Value);
        if (warehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        if (scopedOwnerIds.Count > 0)
            query = query.Where(t => t.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(t.OwnerPartnerId.Value));
        if (locationId.HasValue)
            query = query.Where(t => t.LocationId == locationId.Value);
        if (transactionType.HasValue)
            query = query.Where(t => t.TransactionType == transactionType.Value);
        if (!string.IsNullOrWhiteSpace(referenceType))
            query = query.Where(t => t.ReferenceType == referenceType.Trim());
        if (!string.IsNullOrWhiteSpace(referenceCode))
        {
            var cleanReference = referenceCode.Trim();
            query = query.Where(t => t.ReferenceCode != null && t.ReferenceCode.Contains(cleanReference));
        }
        if (licensePlateId.HasValue)
            query = query.Where(t => t.LicensePlateId == licensePlateId.Value);
        if (serialNumberId.HasValue)
            query = query.Where(t => t.SerialNumberId == serialNumberId.Value);

        var data = await query
            .OrderByDescending(t => t.TransactionAt)
            .ThenByDescending(t => t.InventoryTransactionId)
            .Take(5000)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("InventoryLedger");
        var headers = new[]
        {
            "Thời điểm", "Loại giao dịch", "Nhóm giao dịch", "Khóa chống trùng", "Kho", "Vật tư", "Vị trí", "Lô", "Hạn dùng",
            "Trạng thái trước", "Trạng thái sau", "Thay đổi tồn", "Thay đổi giữ chỗ", "Thay đổi khả dụng",
            "Tồn trước", "Tồn sau", "Giữ chỗ trước", "Giữ chỗ sau", "Khả dụng trước", "Khả dụng sau",
            "Tham chiếu", "Mã kiện", "Số sê-ri", "Người thao tác"
        };

        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.AirForceBlue;
        ws.Range(1, 1, 1, headers.Length).Style.Font.FontColor = XLColor.White;

        var row = 1;
        foreach (var transaction in data)
        {
            row++;
            ws.Cell(row, 1).Value = transaction.TransactionAt;
            ws.Cell(row, 2).Value = TransactionTypeLabel(transaction.TransactionType);
            ws.Cell(row, 3).Value = transaction.TransactionGroupKey;
            ws.Cell(row, 4).Value = transaction.IdempotencyKey;
            ws.Cell(row, 5).Value = transaction.Warehouse?.WarehouseCode ?? transaction.WarehouseId.ToString();
            ws.Cell(row, 6).Value = transaction.Item?.ItemCode ?? transaction.ItemId.ToString();
            ws.Cell(row, 7).Value = transaction.Location?.LocationCode ?? transaction.LocationId.ToString();
            ws.Cell(row, 8).Value = transaction.LotNumber ?? "";
            ws.Cell(row, 9).Value = transaction.ExpiryDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 10).Value = transaction.HoldStatusBefore.HasValue ? GetHoldStatusDisplay(transaction.HoldStatusBefore) : "";
            ws.Cell(row, 11).Value = transaction.HoldStatusAfter.HasValue ? GetHoldStatusDisplay(transaction.HoldStatusAfter) : "";
            ws.Cell(row, 12).Value = transaction.QuantityDelta;
            ws.Cell(row, 13).Value = transaction.ReservedDelta;
            ws.Cell(row, 14).Value = transaction.AvailableDelta;
            ws.Cell(row, 15).Value = transaction.QuantityBefore;
            ws.Cell(row, 16).Value = transaction.QuantityAfter;
            ws.Cell(row, 17).Value = transaction.ReservedBefore;
            ws.Cell(row, 18).Value = transaction.ReservedAfter;
            ws.Cell(row, 19).Value = transaction.AvailableBefore;
            ws.Cell(row, 20).Value = transaction.AvailableAfter;
            ws.Cell(row, 21).Value = transaction.ReferenceCode ?? transaction.ReferenceId ?? transaction.ReferenceType ?? "";
            ws.Cell(row, 22).Value = transaction.LicensePlate?.LpnCode ?? transaction.LicensePlateId?.ToString() ?? "";
            ws.Cell(row, 23).Value = transaction.SerialNumber?.SerialCode ?? transaction.SerialNumberId?.ToString() ?? "";
            ws.Cell(row, 24).Value = transaction.Actor;
        }

        ws.Column(1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        ws.Columns(12, 20).Style.NumberFormat.Format = "#,##0.0000";
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();
        var fileName = $"InventoryLedger_{VietnamNow:yyyyMMdd_HHmm}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }


    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> Inventory(
        int? warehouseId,
        int? categoryId,
        string? search,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var canSeeFinancial = CanSeeFinancial();
        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var itemQuery = _db.Items.AsNoTracking()
            .Where(i => i.IsActive).AsQueryable();

        if (categoryId.HasValue)
        {
            var targetCategoryIds = await _db.ItemCategories
                .AsNoTracking()
                .Where(c => c.CategoryId == categoryId.Value || c.ParentCategoryId == categoryId.Value)
                .Select(c => c.CategoryId)
                .ToListAsync(cancellationToken);

            itemQuery = itemQuery.Where(i => i.CategoryId.HasValue && targetCategoryIds.Contains(i.CategoryId.Value));
        }

        if (search != null)
        {
            itemQuery = itemQuery.Where(i => i.ItemCode.Contains(search) || i.ItemName.Contains(search));
        }

        var stockQuery = _db.ItemLocations.AsNoTracking().Where(il => il.Quantity != 0);
        if (warehouseId.HasValue)
        {
            stockQuery = stockQuery.Where(il => il.Location != null
                && il.Location.Zone != null
                && il.Location.Zone.WarehouseId == warehouseId.Value);
        }
        if (scopedOwnerIds.Count > 0)
        {
            stockQuery = stockQuery.Where(il => il.OwnerPartnerId.HasValue
                && scopedOwnerIds.Contains(il.OwnerPartnerId.Value));
        }

        var stockByItemQuery = stockQuery
            .GroupBy(il => il.ItemId)
            .Select(group => new { ItemId = group.Key, Quantity = group.Sum(il => il.Quantity) })
            .Where(row => row.Quantity > 0);

        var reportQuery =
            from item in itemQuery
            join stock in stockByItemQuery on item.ItemId equals stock.ItemId
            select new
            {
                item.ItemId,
                item.ItemCode,
                item.UnitCost,
                item.MinThreshold,
                stock.Quantity
            };

        var totalCount = await reportQuery.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var totalStockValue = canSeeFinancial
            ? await reportQuery.SumAsync(row => row.Quantity * row.UnitCost, cancellationToken)
            : 0m;
        var lowStockCount = await reportQuery.CountAsync(row => row.Quantity <= row.MinThreshold, cancellationToken);

        var pageRows = await reportQuery
            .OrderBy(row => row.ItemCode)
            .ThenBy(row => row.ItemId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var pageItemIds = pageRows.Select(row => row.ItemId).ToList();
        var itemMap = await _db.Items.AsNoTracking()
            .Include(i => i.Category)
            .Include(i => i.BaseUom)
            .Where(i => pageItemIds.Contains(i.ItemId))
            .ToDictionaryAsync(i => i.ItemId, cancellationToken);
        var items = pageRows
            .Where(row => itemMap.ContainsKey(row.ItemId))
            .Select(row =>
            {
                var item = itemMap[row.ItemId];
                item.CurrentStock = row.Quantity;
                item.TotalStockValue = row.Quantity * item.UnitCost;
                return item;
            })
            .ToList();

        // Least privilege: hide financial/cost fields for non-financial users.
        if (!canSeeFinancial)
        {
            foreach (var item in items)
            {
                item.UnitCost = 0m;
                item.TotalStockValue = 0m;
            }
        }

        ViewBag.Warehouses = await _db.Warehouses.AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.WarehouseCode)
            .ToListAsync(cancellationToken);
        ViewBag.Categories = await _db.ItemCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CategoryName)
            .ToListAsync(cancellationToken);
        ViewBag.WarehouseId = warehouseId;
        ViewBag.CategoryId = categoryId;
        ViewBag.CanSeeFinancial = canSeeFinancial;
        ViewBag.Search = search;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalStockValue = totalStockValue;
        ViewBag.LowStockCount = lowStockCount;

        return View(items);
    }


    [Authorize(Policy = WmsPermissions.ReportViewFinancial)]
    public async Task<IActionResult> ExportInventory(int? warehouseId, int? categoryId)
    {
        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;

        var query = _db.Items.AsNoTracking().Include(i => i.Category).Include(i => i.BaseUom)
            .Where(i => i.IsActive).AsQueryable();

        if (categoryId.HasValue)
        {
            var targetCategoryIds = await _db.ItemCategories
                .Where(c => c.CategoryId == categoryId.Value || c.ParentCategoryId == categoryId.Value)
                .Select(c => c.CategoryId)
                .ToListAsync();

            query = query.Where(i => i.CategoryId.HasValue && targetCategoryIds.Contains(i.CategoryId.Value));
        }

        var items = await query.OrderBy(i => i.ItemCode).ToListAsync();
        var stockMap = await _inventoryBalanceService.GetStockByItemAsync(
            warehouseId,
            items.Select(i => i.ItemId),
            ownerPartnerIds: scopedOwnerIds.Count > 0 ? scopedOwnerIds : null);
        items = items
            .Where(i => stockMap.TryGetValue(i.ItemId, out var scopedQty) && scopedQty > 0)
            .ToList();
        _inventoryBalanceService.ApplyStockBalances(items, stockMap);

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("BaoCaoTonKho");
            var currentRow = 1;

            // Header Row
            worksheet.Cell(currentRow, 1).Value = "Mã VT";
            worksheet.Cell(currentRow, 2).Value = "Tên Vật Tư";
            worksheet.Cell(currentRow, 3).Value = "Loại";
            worksheet.Cell(currentRow, 4).Value = "Danh Mục";
            worksheet.Cell(currentRow, 5).Value = "ĐVT";
            worksheet.Cell(currentRow, 6).Value = "Tồn Kho";
            worksheet.Cell(currentRow, 7).Value = "Giá Vốn BQ";
            worksheet.Cell(currentRow, 8).Value = "Tổng Tiền (VNĐ)";

            // Header Styling
            worksheet.Range("A1:H1").Style.Font.Bold = true;
            worksheet.Range("A1:H1").Style.Fill.BackgroundColor = XLColor.AirForceBlue;
            worksheet.Range("A1:H1").Style.Font.FontColor = XLColor.White;

            foreach (var item in items)
            {
                currentRow++;
                worksheet.Cell(currentRow, 1).Value = item.ItemCode;
                worksheet.Cell(currentRow, 2).Value = item.ItemName;
                worksheet.Cell(currentRow, 3).Value = item.ItemTypeName;
                worksheet.Cell(currentRow, 4).Value = item.Category?.CategoryName ?? "Chưa có";
                worksheet.Cell(currentRow, 5).Value = item.BaseUom?.UomCode;
                worksheet.Cell(currentRow, 6).Value = item.CurrentStock;
                worksheet.Cell(currentRow, 7).Value = item.UnitCost;
                worksheet.Cell(currentRow, 8).Value = item.TotalStockValue;
            }

            // Formatting columns
            worksheet.Column(6).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Column(7).Style.NumberFormat.Format = "#,##0";
            worksheet.Column(8).Style.NumberFormat.Format = "#,##0";
            worksheet.Columns().AdjustToContents(); // Auto-fit

            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                var content = stream.ToArray();
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BaoCaoTonKho_{VietnamNow:yyyyMMdd_HHmm}.xlsx");
            }
        }
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportViewFinancial)]
    public async Task<IActionResult> StockValuation(
        int? warehouseId,
        int? categoryId,
        string? itemSearch,
        string? lotNumber,
        DateTime? expiryDate,
        string mode = "current",
        DateTime? snapshotDate = null)
    {
        var model = await BuildStockValuationModelAsync(warehouseId, categoryId, itemSearch, lotNumber, expiryDate, mode, snapshotDate);
        return View(model);
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportViewFinancial)]
    public async Task<IActionResult> ExportStockValuation(
        int? warehouseId,
        int? categoryId,
        string? itemSearch,
        string? lotNumber,
        DateTime? expiryDate,
        string mode = "current",
        DateTime? snapshotDate = null)
    {
        var model = await BuildStockValuationModelAsync(warehouseId, categoryId, itemSearch, lotNumber, expiryDate, mode, snapshotDate);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("DinhGiaTonKho");

        ws.Cell(1, 1).Value = "Báo cáo";
        ws.Cell(1, 2).Value = "Định giá tồn kho";
        ws.Cell(2, 1).Value = "Chế độ xem";
        ws.Cell(2, 2).Value = model.IsSnapshotMode ? "Ngày đã chốt" : "Tồn hiện tại";
        ws.Cell(3, 1).Value = "Ngày chốt";
        ws.Cell(3, 2).Value = model.SnapshotDate?.ToString("dd/MM/yyyy") ?? "";
        ws.Cell(4, 1).Value = "Tổng số mã hàng";
        ws.Cell(4, 2).Value = model.TotalItemCount;
        ws.Cell(5, 1).Value = "Tổng số lượng tồn";
        ws.Cell(5, 2).Value = model.TotalQuantity;
        ws.Cell(6, 1).Value = "Tổng giá trị tồn";
        ws.Cell(6, 2).Value = model.TotalValue;

        if (!string.IsNullOrWhiteSpace(model.Notice))
        {
            ws.Cell(7, 1).Value = "Thông báo";
            ws.Cell(7, 2).Value = model.Notice;
        }

        var headerRow = 9;
        var headers = new[]
        {
            "Kho", "Danh mục", "Mã hàng", "Tên hàng", "Đơn vị tính", "Lô", "Hạn dùng",
            "Trạng thái tồn", "Số lượng tồn", "Đã giữ chỗ", "Khả dụng", "Đơn giá vốn", "Giá trị tồn"
        };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(headerRow, i + 1).Value = headers[i];

        ws.Range(headerRow, 1, headerRow, headers.Length).Style.Font.Bold = true;
        ws.Range(headerRow, 1, headerRow, headers.Length).Style.Fill.BackgroundColor = XLColor.AirForceBlue;
        ws.Range(headerRow, 1, headerRow, headers.Length).Style.Font.FontColor = XLColor.White;

        var row = headerRow;
        foreach (var line in model.Rows)
        {
            row++;
            ws.Cell(row, 1).Value = $"{line.WarehouseCode} - {line.WarehouseName}";
            ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(line.CategoryName) ? "Chưa phân loại" : line.CategoryName;
            ws.Cell(row, 3).Value = line.ItemCode;
            ws.Cell(row, 4).Value = line.ItemName;
            ws.Cell(row, 5).Value = line.UomCode;
            ws.Cell(row, 6).Value = line.LotNumber ?? "";
            ws.Cell(row, 7).Value = line.ExpiryDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(row, 8).Value = GetHoldStatusDisplay(line.HoldStatus);
            ws.Cell(row, 9).Value = line.Quantity;
            ws.Cell(row, 10).Value = line.ReservedQty;
            ws.Cell(row, 11).Value = line.AvailableQty;
            ws.Cell(row, 12).Value = line.UnitCost;
            ws.Cell(row, 13).Value = line.StockValue;
        }

        var totalRow = row + 1;
        ws.Cell(totalRow, 8).Value = "Tổng cộng";
        ws.Cell(totalRow, 9).Value = model.TotalQuantity;
        ws.Cell(totalRow, 10).Value = model.TotalReservedQty;
        ws.Cell(totalRow, 11).Value = model.TotalAvailableQty;
        ws.Cell(totalRow, 13).Value = model.TotalValue;
        ws.Range(totalRow, 8, totalRow, 13).Style.Font.Bold = true;

        ws.Columns(9, 13).Style.NumberFormat.Format = "#,##0.####";
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DinhGiaTonKho_{VietnamNow:yyyyMMdd_HHmm}.xlsx");
    }


    private async Task<StockValuationPageViewModel> BuildStockValuationModelAsync(
        int? warehouseId,
        int? categoryId,
        string? itemSearch,
        string? lotNumber,
        DateTime? expiryDate,
        string? mode,
        DateTime? snapshotDate)
    {
        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;

        var normalizedMode = string.Equals(mode, "snapshot", StringComparison.OrdinalIgnoreCase) ? "snapshot" : "current";
        itemSearch = string.IsNullOrWhiteSpace(itemSearch) ? null : itemSearch.Trim();
        lotNumber = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber.Trim();
        expiryDate = expiryDate?.Date;
        snapshotDate = snapshotDate?.Date;

        var model = new StockValuationPageViewModel
        {
            WarehouseId = warehouseId,
            CategoryId = categoryId,
            ItemSearch = itemSearch,
            LotNumber = lotNumber,
            ExpiryDate = expiryDate,
            SnapshotDate = snapshotDate,
            Mode = normalizedMode,
            Warehouses = await _db.Warehouses.AsNoTracking()
                .Where(w => w.IsActive && (!scopedWh.HasValue || w.WarehouseId == scopedWh.Value))
                .OrderBy(w => w.WarehouseCode)
                .ToListAsync(),
            Categories = await _db.ItemCategories.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.CategoryName)
                .ToListAsync()
        };

        var targetCategoryIds = new List<int>();
        if (categoryId.HasValue)
        {
            targetCategoryIds = await _db.ItemCategories.AsNoTracking()
                .Where(c => c.CategoryId == categoryId.Value || c.ParentCategoryId == categoryId.Value)
                .Select(c => c.CategoryId)
                .ToListAsync();
        }

        if (model.IsSnapshotMode)
        {
            model.SnapshotDate ??= VietnamNow.Date;
            if (!warehouseId.HasValue)
            {
                model.Notice = "Vui lòng chọn kho để xem dữ liệu ngày đã chốt.";
                model.MissingSnapshot = true;
                return model;
            }

            var snapshotExists = await _db.StockSnapshots.AsNoTracking()
                .Where(s => scopedOwnerIds.Count == 0 || (s.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(s.OwnerPartnerId.Value)))
                .AnyAsync(s => s.WarehouseId == warehouseId.Value && s.SnapshotDate == model.SnapshotDate.Value);
            if (!snapshotExists)
            {
                model.Notice = "Chưa có dữ liệu chốt tồn cho kho và ngày đã chọn.";
                model.MissingSnapshot = true;
                return model;
            }

            var latestSnapshotRunId = await _db.StockSnapshotRuns.AsNoTracking()
                .Where(r => r.WarehouseId == warehouseId.Value && r.SnapshotDate == model.SnapshotDate.Value)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.StockSnapshotRunId)
                .Select(r => (long?)r.StockSnapshotRunId)
                .FirstOrDefaultAsync();

            var snapshotRows = await _db.StockSnapshots.AsNoTracking()
                .Include(s => s.Warehouse)
                .Include(s => s.OwnerPartner)
                .Include(s => s.Item).ThenInclude(i => i!.Category)
                .Include(s => s.Item).ThenInclude(i => i!.BaseUom)
                .Where(s => s.WarehouseId == warehouseId.Value && s.SnapshotDate == model.SnapshotDate.Value)
                .Where(s => latestSnapshotRunId.HasValue
                    ? s.StockSnapshotRunId == latestSnapshotRunId.Value
                    : s.StockSnapshotRunId == null)
                .Where(s => scopedOwnerIds.Count == 0 || (s.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(s.OwnerPartnerId.Value)))
                .ToListAsync();

            if (targetCategoryIds.Count > 0)
                snapshotRows = snapshotRows.Where(s => s.Item?.CategoryId != null && targetCategoryIds.Contains(s.Item.CategoryId.Value)).ToList();
            if (!string.IsNullOrWhiteSpace(itemSearch))
            {
                var keyword = itemSearch.ToLowerInvariant();
                snapshotRows = snapshotRows
                    .Where(s => (s.Item?.ItemCode ?? "").ToLowerInvariant().Contains(keyword)
                        || (s.Item?.ItemName ?? "").ToLowerInvariant().Contains(keyword))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(lotNumber) || expiryDate.HasValue)
                model.Notice = "Dữ liệu ngày đã chốt được lưu theo mã hàng, không tách theo lô hoặc hạn dùng.";

            model.Rows = snapshotRows
                .Where(s => s.Item != null)
                .Select(s => new StockValuationRow
                {
                    ItemId = s.ItemId,
                    WarehouseCode = s.Warehouse?.WarehouseCode ?? "",
                    WarehouseName = s.Warehouse?.WarehouseName ?? "",
                    CategoryName = s.Item?.Category?.CategoryName ?? "",
                    ItemCode = s.Item?.ItemCode ?? "",
                    ItemName = s.Item?.ItemName ?? "",
                    UomCode = s.Item?.BaseUom?.UomCode ?? "",
                    LotNumber = null,
                    ExpiryDate = null,
                    HoldStatus = null,
                    Quantity = s.ClosingStock,
                    ReservedQty = 0,
                    AvailableQty = s.ClosingStock,
                    UnitCost = s.UnitCost,
                    StockValue = s.TotalValue
                })
                .OrderBy(r => r.WarehouseCode)
                .ThenBy(r => r.CategoryName)
                .ThenBy(r => r.ItemCode)
                .ToList();

            return model;
        }

        var itemLocationRows = await _db.ItemLocations.AsNoTracking()
            .Include(il => il.Item).ThenInclude(i => i!.Category)
            .Include(il => il.Item).ThenInclude(i => i!.BaseUom)
            .Include(il => il.Location).ThenInclude(l => l!.Zone).ThenInclude(z => z!.Warehouse)
            .Where(il => il.Quantity != 0
                && il.Item != null
                && il.Item.IsActive
                && il.Location != null
                && il.Location.Zone != null
                && il.Location.Zone.Warehouse != null)
            .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
            .ToListAsync();

        if (warehouseId.HasValue)
            itemLocationRows = itemLocationRows.Where(il => il.Location!.Zone!.WarehouseId == warehouseId.Value).ToList();
        if (targetCategoryIds.Count > 0)
            itemLocationRows = itemLocationRows.Where(il => il.Item?.CategoryId != null && targetCategoryIds.Contains(il.Item.CategoryId.Value)).ToList();
        if (!string.IsNullOrWhiteSpace(itemSearch))
        {
            var keyword = itemSearch.ToLowerInvariant();
            itemLocationRows = itemLocationRows
                .Where(il => (il.Item?.ItemCode ?? "").ToLowerInvariant().Contains(keyword)
                    || (il.Item?.ItemName ?? "").ToLowerInvariant().Contains(keyword))
                .ToList();
        }
        if (!string.IsNullOrWhiteSpace(lotNumber))
            itemLocationRows = itemLocationRows.Where(il => (il.LotNumber ?? "").Contains(lotNumber, StringComparison.OrdinalIgnoreCase)).ToList();
        if (expiryDate.HasValue)
            itemLocationRows = itemLocationRows.Where(il => il.ExpiryDate?.Date == expiryDate.Value).ToList();

        model.Rows = itemLocationRows
            .GroupBy(il => new
            {
                il.Location!.Zone!.WarehouseId,
                il.Location.Zone.Warehouse!.WarehouseCode,
                il.Location.Zone.Warehouse.WarehouseName,
                CategoryName = il.Item!.Category != null ? il.Item.Category.CategoryName : "",
                il.ItemId,
                il.Item.ItemCode,
                il.Item.ItemName,
                UomCode = il.Item.BaseUom != null ? il.Item.BaseUom.UomCode : "",
                il.LotNumber,
                ExpiryDate = il.ExpiryDate?.Date,
                il.HoldStatus,
                il.Item.UnitCost
            })
            .Select(g =>
            {
                var quantity = g.Sum(x => x.Quantity);
                var reservedQty = g.Sum(x => x.ReservedQty);
                return new StockValuationRow
                {
                    ItemId = g.Key.ItemId,
                    WarehouseCode = g.Key.WarehouseCode,
                    WarehouseName = g.Key.WarehouseName,
                    CategoryName = g.Key.CategoryName,
                    ItemCode = g.Key.ItemCode,
                    ItemName = g.Key.ItemName,
                    UomCode = g.Key.UomCode,
                    LotNumber = g.Key.LotNumber,
                    ExpiryDate = g.Key.ExpiryDate,
                    HoldStatus = g.Key.HoldStatus,
                    Quantity = quantity,
                    ReservedQty = reservedQty,
                    AvailableQty = quantity - reservedQty,
                    UnitCost = g.Key.UnitCost,
                    StockValue = quantity * g.Key.UnitCost
                };
            })
            .OrderBy(r => r.WarehouseCode)
            .ThenBy(r => r.CategoryName)
            .ThenBy(r => r.ItemCode)
            .ThenBy(r => r.ExpiryDate)
            .ThenBy(r => r.LotNumber)
            .ThenBy(r => r.HoldStatus)
            .ToList();

        return model;
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> StockSnapshot(int? warehouseId, DateTime? snapshotDate, long? stockSnapshotRunId = null)
    {
        snapshotDate ??= VietnamNow.Date;

        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var stockSnapshotRunSchemaAvailable = await StockSnapshotRunSchemaAvailableAsync();
        if (!stockSnapshotRunSchemaAvailable)
            stockSnapshotRunId = null;

        if (stockSnapshotRunSchemaAvailable && warehouseId.HasValue && !stockSnapshotRunId.HasValue)
        {
            stockSnapshotRunId = await _db.StockSnapshotRuns.AsNoTracking()
                .Where(r => r.WarehouseId == warehouseId.Value && r.SnapshotDate == snapshotDate.Value.Date)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => (long?)r.StockSnapshotRunId)
                .FirstOrDefaultAsync();
        }

        ViewBag.Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.WarehouseCode).ToListAsync();
        if (scopedWh.HasValue)
            ViewBag.Warehouses = ((List<Warehouse>)ViewBag.Warehouses).Where(w => w.WarehouseId == scopedWh.Value).ToList();
        ViewBag.WarehouseId = warehouseId;
        ViewBag.SnapshotDate = snapshotDate;
        ViewBag.SelectedStockSnapshotRunId = stockSnapshotRunId;
        ViewBag.StockSnapshotRunSchemaAvailable = stockSnapshotRunSchemaAvailable;
        ViewBag.SnapshotHistory = await BuildStockSnapshotHistoryAsync(warehouseId, snapshotDate.Value.Date, scopedOwnerIds, stockSnapshotRunId, stockSnapshotRunSchemaAvailable);
        var isSnapshotDateToday = snapshotDate.Value.Date == VietnamNow.Date;
        ViewBag.IsSnapshotDateToday = isSnapshotDateToday;
        var snapshotGenerateDisabledReason = !stockSnapshotRunSchemaAvailable
            ? "Cần cập nhật schema lịch sử chốt tồn theo phiên trước khi chốt tồn mới."
            : !isSnapshotDateToday
                ? "Chỉ được chốt tồn cho ngày hiện tại."
                : scopedOwnerIds.Count > 0
                    ? "Tài khoản đang bị giới hạn chủ hàng chỉ được xem snapshot, không được tạo phiên chốt tồn chính thức toàn kho."
                    : "";
        ViewBag.CanGenerateSnapshot = warehouseId.HasValue && isSnapshotDateToday && stockSnapshotRunSchemaAvailable && scopedOwnerIds.Count == 0;
        ViewBag.SnapshotGenerateDisabledReason = snapshotGenerateDisabledReason;

        if (!warehouseId.HasValue)
        {
            return View(new List<StockSnapshotCompareRow>());
        }

        var snapshotQuery = CompatibleStockSnapshots(stockSnapshotRunSchemaAvailable).AsNoTracking()
            .Include(s => s.Item).ThenInclude(i => i!.BaseUom)
            .Include(s => s.OwnerPartner)
            .Where(s => s.SnapshotDate == snapshotDate.Value && s.WarehouseId == warehouseId.Value)
            .Where(s => scopedOwnerIds.Count == 0 || (s.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(s.OwnerPartnerId.Value)));
        if (stockSnapshotRunSchemaAvailable)
        {
            snapshotQuery = stockSnapshotRunId.HasValue
                ? snapshotQuery.Where(s => s.StockSnapshotRunId == stockSnapshotRunId.Value)
                : snapshotQuery.Where(s => s.StockSnapshotRunId == null);
        }
        var snapshotRows = await snapshotQuery
            .OrderBy(s => s.Item!.ItemCode)
            .ToListAsync();

        // Current stock per item in warehouse
        var currentStocks = await _db.ItemLocations.AsNoTracking()
            .Include(il => il.Location).ThenInclude(l => l!.Zone)
            .Where(il => il.Quantity != 0
                && il.Location != null
                && il.Location.Zone != null
                && il.Location.Zone.WarehouseId == warehouseId.Value)
            .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
            .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
            .Select(g => new { g.Key.ItemId, g.Key.OwnerPartnerId, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => (x.ItemId, x.OwnerPartnerId), x => x.Qty);

        List<StockSnapshotCompareRow> data;

        if (snapshotRows.Count == 0 && !isSnapshotDateToday)
        {
            data = new List<StockSnapshotCompareRow>();
            ViewBag.IsPreview = false;
            ViewBag.SnapshotPreviewBlocked = true;
        }
        else if (snapshotRows.Count == 0)
        {
            // No snapshot yet -> show PREVIEW of what will be snapshotted (current stock)
            var itemStocks = await _db.ItemLocations.AsNoTracking()
                .Include(il => il.Location).ThenInclude(l => l!.Zone)
                .Where(il => il.Quantity != 0
                    && il.Location != null
                    && il.Location.Zone != null
                    && il.Location.Zone.WarehouseId == warehouseId.Value)
                .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
                .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
                .Select(g => new { g.Key.ItemId, g.Key.OwnerPartnerId, Qty = g.Sum(x => x.Quantity) })
                .ToListAsync();

            var itemIds = itemStocks.Select(x => x.ItemId).ToList();
            var items = await _db.Items.AsNoTracking()
                .Include(i => i.BaseUom)
                .Where(i => i.IsActive && itemIds.Contains(i.ItemId))
                .OrderBy(i => i.ItemCode)
                .ToListAsync();
            var ownerIds = itemStocks.Where(x => x.OwnerPartnerId.HasValue).Select(x => x.OwnerPartnerId!.Value).Distinct().ToList();
            var owners = ownerIds.Count == 0
                ? new Dictionary<int, Partner>()
                : await _db.Partners.AsNoTracking().Where(p => ownerIds.Contains(p.PartnerId)).ToDictionaryAsync(p => p.PartnerId);

            var itemMap = items.ToDictionary(i => i.ItemId);
            data = itemStocks.Select(stock =>
            {
                if (!itemMap.TryGetValue(stock.ItemId, out var it)) return null;
                var currentQty = stock.Qty;
                var snapshotValue = currentQty * it.UnitCost;
                return new StockSnapshotCompareRow
                {
                    ItemId = it.ItemId,
                    OwnerPartnerId = stock.OwnerPartnerId,
                    OwnerName = stock.OwnerPartnerId.HasValue && owners.TryGetValue(stock.OwnerPartnerId.Value, out var owner)
                        ? $"{owner.PartnerCode} - {owner.PartnerName}"
                        : "",
                    ItemCode = it.ItemCode,
                    ItemName = it.ItemName,
                    UomCode = it.BaseUom?.UomCode ?? "",
                    SnapshotQty = currentQty, // preview: will be saved as snapshot qty
                    CurrentQty = currentQty,
                    DiffQty = 0,
                    UnitCost = it.UnitCost,
                    SnapshotValue = snapshotValue,
                    CurrentValue = snapshotValue,
                    DiffValue = 0
                };
            }).Where(x => x != null).Cast<StockSnapshotCompareRow>().OrderBy(x => x.ItemCode).ThenBy(x => x.OwnerName).ToList();

            ViewBag.IsPreview = true;
        }
        else
        {
            data = snapshotRows.Select(s =>
            {
                var currentQty = currentStocks.TryGetValue((s.ItemId, s.OwnerPartnerId), out var q) ? q : 0m;
                var diffQty = s.ClosingStock - currentQty; // needed adjustment to match snapshot
                var snapshotValue = s.ClosingStock * s.UnitCost;
                var currentValue = currentQty * s.UnitCost;
                return new StockSnapshotCompareRow
                {
                    ItemId = s.ItemId,
                    OwnerPartnerId = s.OwnerPartnerId,
                    OwnerName = s.OwnerPartner != null ? $"{s.OwnerPartner.PartnerCode} - {s.OwnerPartner.PartnerName}" : "",
                    ItemCode = s.Item?.ItemCode ?? "",
                    ItemName = s.Item?.ItemName ?? "",
                    UomCode = s.Item?.BaseUom?.UomCode ?? "",
                    SnapshotQty = s.ClosingStock,
                    CurrentQty = currentQty,
                    DiffQty = diffQty,
                    UnitCost = s.UnitCost,
                    SnapshotValue = snapshotValue,
                    CurrentValue = currentValue,
                    DiffValue = snapshotValue - currentValue
                };
            }).ToList();

            ViewBag.IsPreview = false;
        }

        ViewBag.HasSnapshot = snapshotRows.Count > 0;
        ViewBag.DiffCount = data.Count(x => x.DiffQty != 0);
        return View(data);
    }

    private async Task<List<StockSnapshotHistoryRow>> BuildStockSnapshotHistoryAsync(
        int? warehouseId,
        DateTime selectedDate,
        IReadOnlyList<int> scopedOwnerIds,
        long? selectedRunId,
        bool stockSnapshotRunSchemaAvailable)
    {
        var stockSnapshotSource = CompatibleStockSnapshots(stockSnapshotRunSchemaAvailable).AsNoTracking()
            .Include(s => s.Warehouse)
            .Where(s => !warehouseId.HasValue || s.WarehouseId == warehouseId.Value)
            .Where(s => scopedOwnerIds.Count == 0 || (s.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(s.OwnerPartnerId.Value)));
        if (stockSnapshotRunSchemaAvailable)
            stockSnapshotSource = stockSnapshotSource.Include(s => s.Run);

        var rows = await stockSnapshotSource
            .OrderByDescending(s => s.SnapshotDate)
            .ThenByDescending(s => s.CreatedAt)
            .Take(5000)
            .ToListAsync();

        if (rows.Count == 0)
            return new List<StockSnapshotHistoryRow>();

        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var currentRows = await _db.ItemLocations.AsNoTracking()
            .Include(il => il.Location).ThenInclude(l => l!.Zone)
            .Where(il => il.Location != null
                && il.Location.Zone != null
                && warehouseIds.Contains(il.Location.Zone.WarehouseId))
            .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
            .GroupBy(il => new { il.Location!.Zone!.WarehouseId, il.ItemId, il.OwnerPartnerId })
            .Select(g => new
            {
                g.Key.WarehouseId,
                g.Key.ItemId,
                g.Key.OwnerPartnerId,
                Qty = g.Sum(x => x.Quantity)
            })
            .ToListAsync();

        var currentLookup = currentRows.ToDictionary(
            x => (x.WarehouseId, x.ItemId, x.OwnerPartnerId),
            x => x.Qty);

        return rows
            .GroupBy(r => new
            {
                StockSnapshotRunId = stockSnapshotRunSchemaAvailable ? r.StockSnapshotRunId : null,
                r.WarehouseId,
                r.SnapshotDate
            })
            .Select(g =>
            {
                var snapshotKeys = g.Select(s => (s.WarehouseId, s.ItemId, s.OwnerPartnerId)).ToHashSet();
                var diffLines = g.Count(s =>
                {
                    var currentQty = currentLookup.TryGetValue((s.WarehouseId, s.ItemId, s.OwnerPartnerId), out var qty) ? qty : 0m;
                    return currentQty != s.ClosingStock;
                });
                diffLines += currentLookup.Keys.Count(k => k.WarehouseId == g.Key.WarehouseId && !snapshotKeys.Contains(k));
                var first = g.First();
                var warehouse = first.Warehouse;
                var run = first.Run;
                return new StockSnapshotHistoryRow
                {
                    StockSnapshotRunId = g.Key.StockSnapshotRunId,
                    WarehouseId = g.Key.WarehouseId,
                    WarehouseCode = warehouse?.WarehouseCode ?? g.Key.WarehouseId.ToString(),
                    WarehouseName = warehouse?.WarehouseName ?? "",
                    SnapshotDate = g.Key.SnapshotDate,
                    CreatedAt = run?.CreatedAt ?? g.Max(x => x.CreatedAt),
                    TotalItems = g.Count(),
                    TotalValue = run?.TotalValue ?? g.Sum(x => x.TotalValue),
                    DiffLines = diffLines,
                    IsSelected = warehouseId == g.Key.WarehouseId
                        && selectedDate.Date == g.Key.SnapshotDate.Date
                        && ((!selectedRunId.HasValue && !g.Key.StockSnapshotRunId.HasValue)
                            || selectedRunId == g.Key.StockSnapshotRunId)
                };
            })
            .OrderByDescending(r => r.SnapshotDate)
            .ThenByDescending(r => r.CreatedAt)
            .ThenBy(r => r.WarehouseCode)
            .Take(20)
            .ToList();
    }

    private IQueryable<StockSnapshot> CompatibleStockSnapshots(bool stockSnapshotRunSchemaAvailable)
    {
        if (stockSnapshotRunSchemaAvailable || !_db.Database.IsRelational())
            return _db.StockSnapshots;

        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return _db.StockSnapshots.FromSqlRaw("""
                SELECT
                    [SnapshotId],
                    CAST(NULL AS bigint) AS [StockSnapshotRunId],
                    [SnapshotDate],
                    [ItemId],
                    [OwnerPartnerId],
                    [WarehouseId],
                    [ClosingStock],
                    [UnitCost],
                    [TotalValue],
                    [CreatedAt]
                FROM [StockSnapshots]
                """);
        }
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return _db.StockSnapshots.FromSqlRaw("""
                SELECT
                    "SnapshotId",
                    CAST(NULL AS INTEGER) AS "StockSnapshotRunId",
                    "SnapshotDate",
                    "ItemId",
                    "OwnerPartnerId",
                    "WarehouseId",
                    "ClosingStock",
                    "UnitCost",
                    "TotalValue",
                    "CreatedAt"
                FROM "StockSnapshots"
                """);
        }

        return _db.StockSnapshots;
    }

    private async Task<bool> StockSnapshotRunSchemaAvailableAsync()
    {
        if (!_db.Database.IsRelational())
            return true;

        var provider = _db.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
            && !provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var connection = _db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? """
                      SELECT CASE
                          WHEN EXISTS (
                              SELECT 1
                              FROM sys.tables t
                              WHERE t.name = N'StockSnapshotRuns'
                          )
                           AND EXISTS (
                              SELECT 1
                              FROM sys.columns c
                              INNER JOIN sys.tables t ON t.object_id = c.object_id
                              WHERE t.name = N'StockSnapshots'
                                AND c.name = N'StockSnapshotRunId'
                          )
                          THEN 1 ELSE 0 END
                      """
                    : """
                      SELECT CASE
                          WHEN EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'StockSnapshotRuns')
                           AND EXISTS (SELECT 1 FROM pragma_table_info('StockSnapshots') WHERE name = 'StockSnapshotRunId')
                          THEN 1 ELSE 0 END
                      """;
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) == 1;
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stock snapshot run schema probe failed; falling back to legacy stock snapshot mode.");
            return false;
        }
    }


    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateStockSnapshot(int warehouseId, DateTime snapshotDate)
    {
        snapshotDate = snapshotDate.Date;
        if (snapshotDate != VietnamNow.Date)
        {
            TempData["Error"] = "Chỉ được chốt tồn cho ngày hiện tại. Muốn xem ngày cũ, hãy chọn một phiên chốt tồn đã được lưu trước đó.";
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate });
        }

        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue && warehouseId != scopedWh.Value)
            return Forbid();
        if (scopedOwnerIds.Count > 0)
        {
            TempData["Error"] = "Tài khoản đang bị giới hạn chủ hàng chỉ được xem snapshot, không được tạo phiên chốt tồn chính thức toàn kho.";
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate });
        }

        var wh = await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId == warehouseId && w.IsActive);
        if (wh == null)
        {
            TempData["Error"] = "Kho không hợp lệ.";
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate });
        }

        if (!await StockSnapshotRunSchemaAvailableAsync())
        {
            TempData["Error"] = "Cơ sở dữ liệu chưa cập nhật schema lịch sử chốt tồn theo phiên. Vui lòng chạy migration AddStockSnapshotRuns trước khi chốt tồn mới.";
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate });
        }

        long? createdRunId = null;
        await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Aggregate stock per item in warehouse
            var itemStocks = await _db.ItemLocations.AsNoTracking()
                .Include(il => il.Location).ThenInclude(l => l!.Zone)
                .Where(il => il.Quantity != 0
                    && il.Location != null
                    && il.Location.Zone != null
                    && il.Location.Zone.WarehouseId == warehouseId)
                .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
                .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
                .Select(g => new { g.Key.ItemId, g.Key.OwnerPartnerId, Qty = g.Sum(x => x.Quantity) })
                .ToListAsync();

            var itemIds = itemStocks.Select(x => x.ItemId).ToList();
            var items = await _db.Items.AsNoTracking()
                .Where(i => i.IsActive && itemIds.Contains(i.ItemId))
                .ToDictionaryAsync(i => i.ItemId, i => i);

            var snapshots = new List<StockSnapshot>(itemStocks.Count);
            foreach (var s in itemStocks)
            {
                if (!items.TryGetValue(s.ItemId, out var item)) continue;
                var qty = s.Qty;
                var unitCost = item.UnitCost;
                snapshots.Add(new StockSnapshot
                {
                    SnapshotDate = snapshotDate,
                    ItemId = item.ItemId,
                    OwnerPartnerId = s.OwnerPartnerId,
                    WarehouseId = warehouseId,
                    ClosingStock = qty,
                    UnitCost = unitCost,
                    TotalValue = qty * unitCost,
                    CreatedAt = VietnamNow
                });
            }

            var run = new StockSnapshotRun
            {
                WarehouseId = warehouseId,
                SnapshotDate = snapshotDate,
                CreatedAt = VietnamNow,
                CreatedBy = User.Identity?.Name ?? "system",
                TotalItems = snapshots.Count,
                TotalValue = snapshots.Sum(s => s.TotalValue),
                Status = "Completed"
            };
            _db.StockSnapshotRuns.Add(run);
            await _unitOfWork.SaveChangesAsync();
            createdRunId = run.StockSnapshotRunId;

            foreach (var snapshot in snapshots)
                snapshot.StockSnapshotRunId = run.StockSnapshotRunId;

            if (snapshots.Count > 0)
            {
                await _db.StockSnapshots.AddRangeAsync(snapshots);
                await _unitOfWork.SaveChangesAsync();
            }

            await _unitOfWork.CommitAsync();
            TempData["Success"] = $"Đã chốt tồn kho '{wh.WarehouseName}' ngày {snapshotDate:dd/MM/yyyy} ({snapshots.Count} mã, phiên #{run.StockSnapshotRunId}).";
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Generate stock snapshot failed. WarehouseId={WarehouseId}, SnapshotDate={SnapshotDate}, Actor={Actor}", warehouseId, snapshotDate, User.Identity?.Name);
            TempData["Error"] = UserSafeError.WithPrefix(ex, "Lỗi chốt tồn", "Không thể chốt tồn lúc này. Vui lòng thử lại.");
        }

        return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate, stockSnapshotRunId = createdRunId });
    }


    /// <summary>
    /// Tạo phiếu điều chỉnh 1-click từ snapshot: tính chênh lệch, tạo phiếu, cập nhật tồn kho — tất cả trong 1 bước.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportView)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAdjustFromSnapshot(int warehouseId, DateTime snapshotDate, long? stockSnapshotRunId, string? notes)
    {
        snapshotDate = snapshotDate.Date;
        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue && warehouseId != scopedWh.Value)
            return Forbid();
        if (scopedOwnerIds.Count > 0)
        {
            TempData["Error"] = "Tài khoản đang bị giới hạn chủ hàng chỉ được xem snapshot, không được tạo phiên chốt tồn chính thức toàn kho.";
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate });
        }

        var stockSnapshotRunSchemaAvailable = await StockSnapshotRunSchemaAvailableAsync();
        if (!stockSnapshotRunSchemaAvailable)
            stockSnapshotRunId = null;

        if (stockSnapshotRunSchemaAvailable)
        {
            stockSnapshotRunId ??= await _db.StockSnapshotRuns.AsNoTracking()
                .Where(r => r.WarehouseId == warehouseId && r.SnapshotDate == snapshotDate)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => (long?)r.StockSnapshotRunId)
                .FirstOrDefaultAsync();
        }

        // ── 1. Load snapshot rows ──
        var snapshotQuery = CompatibleStockSnapshots(stockSnapshotRunSchemaAvailable).AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.SnapshotDate == snapshotDate)
            .Where(s => scopedOwnerIds.Count == 0 || (s.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(s.OwnerPartnerId.Value)));
        if (stockSnapshotRunSchemaAvailable)
        {
            snapshotQuery = stockSnapshotRunId.HasValue
                ? snapshotQuery.Where(s => s.StockSnapshotRunId == stockSnapshotRunId.Value)
                : snapshotQuery.Where(s => s.StockSnapshotRunId == null);
        }
        var snapshotRows = await snapshotQuery.ToListAsync();
        if (snapshotRows.Count == 0)
        {
            TempData["Error"] = "Chưa có snapshot cho kho/ngày đã chọn. Vui lòng chốt tồn trước.";
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate, stockSnapshotRunId });
        }

        var voucherDate = VietnamNow.Date;
        var lockDate = await _db.WarehousePeriodLocks.AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId && l.IsActive)
            .OrderByDescending(l => l.LockDate)
            .Select(l => (DateTime?)l.LockDate)
            .FirstOrDefaultAsync();
        if (lockDate.HasValue && voucherDate <= lockDate.Value.Date)
        {
            TempData["Error"] = $"Kho đã khóa kỳ đến {lockDate.Value:dd/MM/yyyy}. Không thể tạo phiếu điều chỉnh.";
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate, stockSnapshotRunId });
        }

        var actor = User.Identity?.Name ?? "system";
        await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Recheck inside the transaction so a concurrently activated period lock
            // cannot be bypassed between the fast precheck and the inventory write.
            var transactionLockDate = await _db.WarehousePeriodLocks.AsNoTracking()
                .Where(l => l.WarehouseId == warehouseId && l.IsActive)
                .OrderByDescending(l => l.LockDate)
                .Select(l => (DateTime?)l.LockDate)
                .FirstOrDefaultAsync();
            if (transactionLockDate.HasValue && voucherDate <= transactionLockDate.Value.Date)
                throw WmsExceptions.WarehouseLocked(voucherDate.ToString("dd/MM/yyyy"), transactionLockDate.Value);

            // ── 2. Tính chênh lệch snapshot vs tồn hiện tại trong cùng transaction ──
            var currentStocks = await _db.ItemLocations.AsNoTracking()
                .Include(il => il.Location).ThenInclude(l => l!.Zone)
                .Where(il => il.Quantity != 0
                    && il.Location != null
                    && il.Location.Zone != null
                    && il.Location.Zone.WarehouseId == warehouseId)
                .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
                .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
                .Select(g => new { g.Key.ItemId, g.Key.OwnerPartnerId, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => (x.ItemId, x.OwnerPartnerId), x => x.Qty);

            var itemIds = snapshotRows.Select(s => s.ItemId).Distinct().ToList();
            var items = await _db.Items.Where(i => i.IsActive && itemIds.Contains(i.ItemId))
                .ToDictionaryAsync(i => i.ItemId, i => i);

            // Tìm vị trí tồn kho theo lô/hạn để giữ đúng granularity batch khi điều chỉnh giảm
            var stockLocs = await _db.ItemLocations.AsNoTracking()
                .Include(il => il.Location).ThenInclude(l => l!.Zone)
                .Where(il => il.Quantity > 0
                    && il.Location != null
                    && il.Location.Zone != null
                    && il.Location.Zone.WarehouseId == warehouseId
                    && itemIds.Contains(il.ItemId))
                .Where(il => scopedOwnerIds.Count == 0 || (il.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(il.OwnerPartnerId.Value)))
                .OrderBy(il => il.ExpiryDate == null)
                .ThenBy(il => il.ExpiryDate)
                .ThenByDescending(il => il.Quantity)
                .ToListAsync();

            var bestLocByItem = stockLocs
                .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
                .ToDictionary(g => g.Key, g => g.First().LocationId);
            var stockLayersByItem = stockLocs
                .GroupBy(il => new { il.ItemId, il.OwnerPartnerId })
                .ToDictionary(g => g.Key, g => g.ToList());

            // Build diff list
            var diffLines = new List<(int ItemId, int? OwnerPartnerId, decimal DiffQty, int? LocationId, string? LotNumber, DateTime? ExpiryDate)>();
            foreach (var s in snapshotRows)
            {
                if (!items.ContainsKey(s.ItemId)) continue;
                var ownerKey = new { s.ItemId, s.OwnerPartnerId };
                var currentQty = currentStocks.TryGetValue((s.ItemId, s.OwnerPartnerId), out var q) ? q : 0m;
                var diff = s.ClosingStock - currentQty;
                if (diff == 0) continue;

                var item = items[s.ItemId];
                if (diff < 0)
                {
                    if (!stockLayersByItem.TryGetValue(ownerKey, out var layers) || layers.Count == 0)
                        throw WmsExceptions.StockAdjustmentNoLotFound(item.ItemCode);

                    var remainingToReduce = Math.Abs(diff);
                    foreach (var layer in layers)
                    {
                        if (remainingToReduce <= 0) break;
                        if (layer.Quantity <= 0) continue;

                        var take = Math.Min(remainingToReduce, layer.Quantity);
                        if (take <= 0) continue;

                        diffLines.Add((s.ItemId, s.OwnerPartnerId, -take, layer.LocationId, layer.LotNumber, layer.ExpiryDate));
                        remainingToReduce -= take;
                    }

                    if (remainingToReduce > 0)
                        throw WmsExceptions.StockAdjustmentInsufficientLotStock(item.ItemCode);
                }
                else
                {
                    int? locId = item.DefaultLocationId
                        ?? (bestLocByItem.TryGetValue(ownerKey, out var loc2) ? loc2 : null);
                    if (!locId.HasValue)
                        throw WmsExceptions.StockAdjustmentNoDefaultLocation(item.ItemCode);
                    diffLines.Add((s.ItemId, s.OwnerPartnerId, diff, locId, null, null));
                }
            }

            if (diffLines.Count == 0)
            {
                await _unitOfWork.RollbackAsync();
                TempData["Info"] = "Không có chênh lệch giữa snapshot và tồn hiện tại. Không cần điều chỉnh.";
                return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate, stockSnapshotRunId });
            }
            var adjustmentOwnerIds = diffLines.Select(x => x.OwnerPartnerId).Distinct().ToList();
            if (adjustmentOwnerIds.Count > 1)
            {
                await _unitOfWork.RollbackAsync();
                TempData["Error"] = "Snapshot đang có chênh lệch của nhiều chủ hàng. Vui lòng lọc/chốt theo một chủ hàng trước khi tạo phiếu điều chỉnh.";
                return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate, stockSnapshotRunId });
            }
            var adjustmentOwnerId = adjustmentOwnerIds.Single();

            // ── 3. Tạo phiếu + cập nhật tồn kho ──
            using var ledgerScope = _inventoryTransactionService.BeginScope(new InventoryTransactionContext
            {
                TransactionType = InventoryTransactionTypeEnum.Adjust,
                TransactionGroupKey = $"snapshot:{warehouseId}:{snapshotDate:yyyyMMdd}:{stockSnapshotRunId?.ToString() ?? "legacy"}:quick-adjust",
                IdempotencyKeyPrefix = $"snapshot:{warehouseId}:{snapshotDate:yyyyMMdd}:{stockSnapshotRunId?.ToString() ?? "legacy"}:quick-adjust",
                WarehouseId = warehouseId,
                OwnerPartnerId = adjustmentOwnerId,
                ReferenceType = "StockSnapshot",
                ReferenceId = stockSnapshotRunId.HasValue
                    ? $"{warehouseId}:{snapshotDate:yyyyMMdd}:{stockSnapshotRunId.Value}"
                    : $"{warehouseId}:{snapshotDate:yyyyMMdd}",
                ReferenceCode = stockSnapshotRunId.HasValue
                    ? $"SNAP-{warehouseId}-{snapshotDate:yyyyMMdd}-{stockSnapshotRunId.Value}"
                    : $"SNAP-{warehouseId}-{snapshotDate:yyyyMMdd}",
                Actor = actor
            });
            // Generate voucher code
            var prefix = "PDC";
            var dateStr = VietnamNow.ToString("yyyyMMdd");
            Voucher? voucher = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var seq = await _db.Vouchers.CountAsync(v => v.VoucherCode.StartsWith(prefix + "-" + dateStr)) + 1;
                var random = Random.Shared.Next(0, 100).ToString("D2");
                var voucherCode = $"{prefix}-{dateStr}-{seq:D5}{random}";
                voucher = new Voucher
                {
                    VoucherCode = voucherCode,
                    VoucherType = VoucherTypeEnum.DieuChinh,
                    VoucherDate = voucherDate,
                    WarehouseId = warehouseId,
                    OwnerPartnerId = adjustmentOwnerId,
                    Description = string.IsNullOrWhiteSpace(notes)
                        ? $"Điều chỉnh tồn theo snapshot {snapshotDate:dd/MM/yyyy}"
                        : $"Điều chỉnh tồn theo snapshot {snapshotDate:dd/MM/yyyy} — {notes.Trim()}",
                    SourceType = SourceTypeEnum.Manual,
                    CreatedBy = actor,
                    CreatedAt = VietnamNow,
                    IsPosted = true
                };
                _db.Vouchers.Add(voucher);
                try
                {
                    await _unitOfWork.SaveChangesAsync();
                    break;
                }
                catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
                    || ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                    || ex.InnerException?.Message.Contains("2627", StringComparison.OrdinalIgnoreCase) == true
                    || ex.InnerException?.Message.Contains("2601", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _db.Entry(voucher).State = EntityState.Detached;
                    voucher = null;
                }
            }
            if (voucher == null)
                throw WmsExceptions.ReportAdjustmentCodeFailed();

            // Create detail lines + update stock
            int lineNo = 0;
            decimal totalAmount = 0;
            foreach (var (itemId, ownerPartnerId, diffQty, locationId, lotNumber, expiryDate) in diffLines)
            {
                var item = items[itemId];
                lineNo++;
                var abs = Math.Abs(diffQty);
                var lineAmount = item.UnitCost * abs;
                var snapQty = snapshotRows.First(r => r.ItemId == itemId && r.OwnerPartnerId == ownerPartnerId).ClosingStock;

                _db.VoucherDetails.Add(new VoucherDetail
                {
                    VoucherId = voucher.VoucherId,
                    ItemId = itemId,
                    OwnerPartnerId = ownerPartnerId,
                    LocationId = locationId,
                    LotNumber = lotNumber,
                    ExpiryDate = expiryDate,
                    TransactionQty = abs,
                    TransactionUomId = item.BaseUomId,
                    ConversionRate = 1m,
                    BaseQty = diffQty, // carries sign: + increase, - decrease
                    UnitPrice = abs > 0 ? lineAmount / abs : 0m,
                    LineAmount = lineAmount,
                    QualityStatus = QualityStatusEnum.Good,
                    Notes = $"Snapshot {snapshotDate:dd/MM/yyyy}: chốt {snapQty:N2}, hiện tại {snapQty - diffQty:N2}, điều chỉnh {(diffQty > 0 ? "+" : "")}{diffQty:N2}",
                    LineNumber = lineNo,
                    DefectQty = 0,
                    DefectBaseQty = 0
                });
                totalAmount += lineAmount;

                // Update ItemLocation stock
                if (locationId.HasValue)
                {
                    var itemLoc = await _db.ItemLocations
                        .FirstOrDefaultAsync(il => il.ItemId == itemId
                            && il.OwnerPartnerId == ownerPartnerId
                            && il.LocationId == locationId.Value
                            && il.LotNumber == lotNumber
                            && il.ExpiryDate == expiryDate);
                    if (itemLoc == null)
                    {
                        itemLoc = new ItemLocation
                        {
                            ItemId = itemId,
                            OwnerPartnerId = ownerPartnerId,
                            LocationId = locationId.Value,
                            LotNumber = lotNumber,
                            ExpiryDate = expiryDate,
                            Quantity = 0,
                            UpdatedAt = VietnamNow
                        };
                        _db.ItemLocations.Add(itemLoc);
                    }
                    itemLoc.Quantity += diffQty;
                    if (itemLoc.Quantity < 0)
                        throw WmsExceptions.AdjustmentMakesNegativeLocation(item.ItemCode);
                    itemLoc.UpdatedAt = VietnamNow;
                }

                // Update Item total stock
                item.CurrentStock += diffQty;
                if (item.CurrentStock < 0)
                    throw WmsExceptions.AdjustmentMakesNegativeItem(item.ItemCode);
                item.TotalStockValue = item.CurrentStock * item.UnitCost;
                item.UpdatedAt = VietnamNow;
            }

            voucher.TotalLines = lineNo;
            voucher.TotalAmount = totalAmount;

            await _unitOfWork.SaveChangesAsync();

            // P0-03: Sync CurrentStock from ItemLocation source of truth
            var quickAdjustAffectedItemIds = diffLines.Select(l => l.ItemId).Distinct();
            await _inventoryBalanceService.SyncCurrentStockAsync(quickAdjustAffectedItemIds);

            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitAsync();

            TempData["Success"] = $"Đã tạo phiếu điều chỉnh theo snapshot {snapshotDate:dd/MM/yyyy} ({lineNo} dòng chênh lệch).";
            return RedirectToAction("Details", "Vouchers", new { id = voucher.VoucherId });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await _unitOfWork.RollbackAsync();
            _logger.LogWarning(ex, "Concurrency conflict when quick-adjust from snapshot. WarehouseId={WarehouseId}, SnapshotDate={SnapshotDate}, Actor={Actor}", warehouseId, snapshotDate, User.Identity?.Name);
            TempData["Error"] = "Dữ liệu đã thay đổi bởi phiên khác trong lúc tạo phiếu điều chỉnh. Vui lòng tải lại và thử lại.";
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate, stockSnapshotRunId });
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync();
            _logger.LogError(ex, "Quick adjust from snapshot failed. WarehouseId={WarehouseId}, SnapshotDate={SnapshotDate}, Actor={Actor}", warehouseId, snapshotDate, User.Identity?.Name);
            TempData["Error"] = UserSafeError.WithPrefix(ex, "Lỗi tạo phiếu điều chỉnh", "Không thể tạo phiếu điều chỉnh lúc này. Vui lòng thử lại.");
            return RedirectToAction(nameof(StockSnapshot), new { warehouseId, snapshotDate, stockSnapshotRunId });
        }
    }


    [Authorize(Roles = "Admin,Manager")]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> ExportStockSnapshot(int warehouseId, DateTime snapshotDate, long? stockSnapshotRunId = null)
    {
        snapshotDate = snapshotDate.Date;
        var canSeeFinancial = CanSeeFinancial();
        var scopedWh = GetScopedWarehouseId();
        var scopedOwnerIds = GetScopedOwnerPartnerIds();
        if (scopedWh.HasValue && warehouseId != scopedWh.Value)
            return Forbid();

        var wh = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.WarehouseId == warehouseId);
        if (wh == null) return NotFound();

        var stockSnapshotRunSchemaAvailable = await StockSnapshotRunSchemaAvailableAsync();
        if (stockSnapshotRunSchemaAvailable)
        {
            stockSnapshotRunId ??= await _db.StockSnapshotRuns.AsNoTracking()
                .Where(r => r.WarehouseId == warehouseId && r.SnapshotDate == snapshotDate)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => (long?)r.StockSnapshotRunId)
                .FirstOrDefaultAsync();
        }
        else
        {
            stockSnapshotRunId = null;
        }

        var dataQuery = CompatibleStockSnapshots(stockSnapshotRunSchemaAvailable).AsNoTracking()
            .Include(s => s.Item).ThenInclude(i => i!.BaseUom)
            .Include(s => s.OwnerPartner)
            .Where(s => s.WarehouseId == warehouseId && s.SnapshotDate == snapshotDate)
            .Where(s => scopedOwnerIds.Count == 0 || (s.OwnerPartnerId.HasValue && scopedOwnerIds.Contains(s.OwnerPartnerId.Value)));
        if (stockSnapshotRunSchemaAvailable)
        {
            dataQuery = stockSnapshotRunId.HasValue
                ? dataQuery.Where(s => s.StockSnapshotRunId == stockSnapshotRunId.Value)
                : dataQuery.Where(s => s.StockSnapshotRunId == null);
        }
        var data = await dataQuery
            .OrderBy(s => s.Item!.ItemCode)
            .ToListAsync();
        var showOwner = data.Any(s => s.OwnerPartnerId.HasValue);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("ChotTon");

        ws.Cell(1, 1).Value = "Kho";
        ws.Cell(1, 2).Value = wh.WarehouseName;
        ws.Cell(2, 1).Value = "Ngày chốt";
        ws.Cell(2, 2).Value = snapshotDate.ToString("dd/MM/yyyy");

        var row = 4;
        var headers = new List<string> { "Mã VT" };
        if (showOwner)
            headers.Add("Chủ hàng");
        headers.AddRange(new[] { "Tên VT", "ĐVT", "Tồn chốt" });
        if (canSeeFinancial)
            headers.AddRange(new[] { "Giá vốn", "Thành tiền" });

        for (var column = 0; column < headers.Count; column++)
            ws.Cell(row, column + 1).Value = headers[column];

        var lastColumn = headers.Count;
        ws.Range(row, 1, row, lastColumn).Style.Font.Bold = true;
        ws.Range(row, 1, row, lastColumn).Style.Fill.BackgroundColor = XLColor.AirForceBlue;
        ws.Range(row, 1, row, lastColumn).Style.Font.FontColor = XLColor.White;

        foreach (var s in data)
        {
            row++;
            var column = 1;
            ws.Cell(row, column++).Value = s.Item?.ItemCode ?? "";
            if (showOwner)
                ws.Cell(row, column++).Value = s.OwnerPartner != null ? $"{s.OwnerPartner.PartnerCode} - {s.OwnerPartner.PartnerName}" : "";
            ws.Cell(row, column++).Value = s.Item?.ItemName ?? "";
            ws.Cell(row, column++).Value = s.Item?.BaseUom?.UomCode ?? "";
            ws.Cell(row, column++).Value = s.ClosingStock;
            if (canSeeFinancial)
            {
                ws.Cell(row, column++).Value = s.UnitCost;
                ws.Cell(row, column).Value = s.TotalValue;
            }
        }

        if (data.Count == 0)
        {
            row++;
            ws.Range(row, 1, row, lastColumn).Merge().Value = "Không có dữ liệu chốt tồn phù hợp với phạm vi và bộ lọc đã chọn.";
        }

        var stockColumn = showOwner ? 5 : 4;
        ws.Column(stockColumn).Style.NumberFormat.Format = "#,##0.####";
        if (canSeeFinancial)
        {
            ws.Column(stockColumn + 1).Style.NumberFormat.Format = "#,##0.####";
            ws.Column(stockColumn + 2).Style.NumberFormat.Format = "#,##0.####";
        }
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();

        var fileName = $"ChotTon_{wh.WarehouseCode}_{snapshotDate:yyyyMMdd}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

}
