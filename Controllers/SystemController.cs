using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Common;
using WMS.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using WMS.Authorization;
using WMS.Services;
using WMS.ViewModels;
using System.Globalization;
using System.Security.Claims;

namespace WMS.Controllers;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
public class SystemController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly IProductionSreService _productionSreService;
    private readonly ITier1DataQualityAuditService _tier1DataQualityAuditService;
    private readonly IDemoDataSeedService _demoDataSeedService;

    public SystemController(
        AppDbContext db,
        IWebHostEnvironment env,
        IConfiguration config,
        IProductionSreService? productionSreService = null,
        ITier1DataQualityAuditService? tier1DataQualityAuditService = null,
        IDemoDataSeedService? demoDataSeedService = null)
    {
        _db = db;
        _env = env;
        _config = config;
        _productionSreService = productionSreService ?? new ProductionSreService(db);
        _tier1DataQualityAuditService = tier1DataQualityAuditService ?? new Tier1DataQualityAuditService(db);
        _demoDataSeedService = demoDataSeedService ?? new DemoDataSeedService(db);
    }

    private bool IsDangerOpsAllowed()
    {
        return _env.IsDevelopment() || string.Equals(_config["System:AllowDangerOps"], "true", StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet]
    public async Task<IActionResult> SreDashboard(int periodMinutes = 15)
    {
        periodMinutes = Math.Clamp(periodMinutes, 1, 1440);
        var model = await _productionSreService.BuildDashboardAsync(periodMinutes);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ExportSreSnapshot(int periodMinutes = 15)
    {
        var model = await _productionSreService.BuildDashboardAsync(periodMinutes);
        var lines = new List<string>
        {
            "thoi_diem,so_phut,so_yeu_cau,so_loi,ty_le_loi,do_tre_tb_ms,do_tre_p95_ms,do_sau_hang_doi,hang_loi,quet_gui_lai,loi_van_tai,loi_diem_nhan",
            string.Join(',',
                model.Snapshot.SnapshotAt.ToString("O"),
                model.Snapshot.PeriodMinutes,
                model.Snapshot.RequestCount,
                model.Snapshot.ErrorCount,
                model.Snapshot.ErrorRatePercent,
                model.Snapshot.AverageLatencyMs,
                model.Snapshot.P95LatencyMs,
                model.Snapshot.QueueDepth,
                model.Snapshot.DeadLetterCount,
                model.Snapshot.ScanRetryCount,
                model.Snapshot.CarrierFailureCount,
                model.Snapshot.WebhookFailureCount)
        };
        return File(SpreadsheetExportSecurity.EncodeUtf8Csv(string.Join(Environment.NewLine, lines)), "text/csv; charset=utf-8", $"sre-snapshot-{VietnamTime.FileStamp("yyyyMMddHHmm")}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> DataQualityAudit(CancellationToken cancellationToken)
    {
        var result = await _tier1DataQualityAuditService.RunAsync(cancellationToken);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> DemoData()
    {
        var model = new DemoDataPageViewModel
        {
            Options = _demoDataSeedService.GetOptions(),
            WarehouseCount = await _db.Warehouses.CountAsync(),
            ItemCount = await _db.Items.CountAsync(),
            VoucherCount = await _db.Vouchers.CountAsync(),
            StockLocationCount = await _db.ItemLocations.CountAsync(),
            LastAppliedMessage = TempData["DemoDataResult"] as string
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyDemoData(string domain, string confirmApply, CancellationToken cancellationToken)
    {
        if (!string.Equals(confirmApply, "APPLY_DEMO_DATA", StringComparison.Ordinal))
        {
            TempData["Error"] = "Chưa xác nhận thao tác nạp demo. Dữ liệu hiện tại chưa bị thay đổi.";
            return RedirectToAction(nameof(DemoData));
        }

        try
        {
            var selectedDomain = DemoDataSeedService.ParseDomainKey(domain);
            var actor = User.Identity?.Name ?? "admin";
            var result = await _demoDataSeedService.ApplyAsync(selectedDomain, actor, cancellationToken);
            await RefreshScopedWarehouseClaimAsync(result.WarehouseId);

            TempData["Success"] = $"Đã nạp {result.DomainName}. Hệ thống giữ nguyên tài khoản, mật khẩu, vai trò và phân quyền đăng nhập.";
            TempData["DemoDataResult"] = $"{result.Warehouses} kho, {result.Locations} vị trí, {result.Items} vật tư, {result.StockRows} dòng tồn, {result.Vouchers} phiếu, {result.QualityInspections} QC, {result.StockCountSheets} kiểm kê.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Code switch
            {
                "DEMO_DOMAIN_INVALID" => "Bộ dữ liệu demo không hợp lệ. Vui lòng chọn lại một bối cảnh trong danh sách.",
                "DEMO_SEED_IN_PROGRESS" => "Hệ thống đang nạp dữ liệu demo khác, vui lòng đợi hoàn tất rồi thử lại.",
                _ => "Không thể nạp dữ liệu demo. Hệ thống đã hoàn tác giao dịch, dữ liệu đăng nhập không bị thay đổi."
            };
        }
        catch
        {
            TempData["Error"] = "Không thể nạp dữ liệu demo. Hệ thống đã hoàn tác giao dịch, dữ liệu đăng nhập không bị thay đổi.";
        }

        return RedirectToAction(nameof(DemoData));
    }

    private async Task RefreshScopedWarehouseClaimAsync(int warehouseId)
    {
        if (warehouseId <= 0 || User.Identity?.IsAuthenticated != true || User.FindFirst("WarehouseId") is null)
            return;

        var claims = User.Claims.Where(x => x.Type != "WarehouseId").ToList();
        claims.Add(new Claim("WarehouseId", warehouseId.ToString(CultureInfo.InvariantCulture)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }

    [HttpGet]
    public async Task<IActionResult> Units()
    {
        var uoms = await _db.UnitsOfMeasure.Where(u => u.IsActive).OrderBy(u => u.UomCode).ToListAsync();
        ViewBag.PackagingUnits = await _db.PackagingUnits
            .Include(p => p.BaseUom)
            .Where(p => p.IsActive).OrderBy(p => p.TenDongGoi).ToListAsync();
        ViewBag.AllUoms = uoms;
        return View("~/Views/Units/Index.cshtml", uoms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUnit(string uomCode, string uomName)
    {
        if (!string.IsNullOrWhiteSpace(uomCode) && !string.IsNullOrWhiteSpace(uomName))
        {
            if (!await _db.UnitsOfMeasure.AnyAsync(u => u.UomCode == uomCode))
            {
                _db.UnitsOfMeasure.Add(new UnitOfMeasure { UomCode = uomCode, UomName = uomName, IsActive = true });
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Đã thêm ĐVT '{uomName}'.";
            }
            else
            {
                TempData["Error"] = $"Mã ĐVT '{uomCode}' đã tồn tại.";
            }
        }
        return RedirectToAction("Units");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUnit(int id)
    {
        var uom = await _db.UnitsOfMeasure.FindAsync(id);
        if (uom != null)
        {
            uom.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã xóa ĐVT '{uom.UomName}'.";
        }
        return RedirectToAction("Units");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePackaging(string tenDongGoi, int baseUomId, decimal giaTri)
    {
        if (!string.IsNullOrWhiteSpace(tenDongGoi) && giaTri > 0)
        {
            if (await _db.PackagingUnits.AnyAsync(p => p.TenDongGoi == tenDongGoi && p.IsActive))
            {
                TempData["Error"] = $"Tên đóng gói '{tenDongGoi}' đã tồn tại.";
            }
            else
            {
                _db.PackagingUnits.Add(new PackagingUnit
                {
                    TenDongGoi = tenDongGoi,
                    BaseUomId = baseUomId,
                    GiaTri = giaTri,
                    IsActive = true
                });
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Đã thêm quy cách đóng gói '{tenDongGoi}'.";
            }
        }
        return RedirectToAction("Units");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePackaging(int id)
    {
        var pkg = await _db.PackagingUnits.FindAsync(id);
        if (pkg != null)
        {
            pkg.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã xóa đóng gói '{pkg.TenDongGoi}'.";
        }
        return RedirectToAction("Units");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = WmsPermissions.DangerOps)]
    public IActionResult MergeLocationsPerLevel()
    {
        if (!IsDangerOpsAllowed()) return Forbid();

        TempData["Info"] = "Thao tác gộp vị trí hàng loạt không thực thi thay đổi trong bản vận hành. Cần kế hoạch dữ liệu, kiểm kê trước/sau và phê duyệt DangerOps riêng.";
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = WmsPermissions.DangerOps)]
    public IActionResult ResetDatabase()
    {
        if (!IsDangerOpsAllowed()) return Forbid();

        TempData["Info"] = "Thao tác reset dữ liệu không thực thi thay đổi trong bản vận hành. Khôi phục dữ liệu chỉ thực hiện bằng runbook backup/restore đã được phê duyệt.";
        return RedirectToAction("Index", "Home");
    }
}
