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

[Microsoft.AspNetCore.Authorization.Authorize]
public partial class ReportsController : Microsoft.AspNetCore.Mvc.Controller
{
    private readonly AppDbContext _db;

    private readonly ILogger<ReportsController> _logger;

    private readonly IInventoryBalanceService _inventoryBalanceService;

    private readonly IUnitOfWork _unitOfWork;

    private readonly IInventoryTransactionService _inventoryTransactionService;
    private readonly IEnterpriseAnalyticsService _enterpriseAnalyticsService;
    private readonly ICycleCountPlanningService _cycleCountPlanningService;
    private readonly IInventoryRiskScoringService _inventoryRiskScoringService;
    private readonly IInventoryRiskRecommendationService _inventoryRiskRecommendationService;

    public ReportsController(
        AppDbContext db,
        ILogger<ReportsController> logger,
        IInventoryBalanceService inventoryBalanceService,
        IUnitOfWork unitOfWork,
        IInventoryTransactionService? inventoryTransactionService = null,
        IEnterpriseAnalyticsService? enterpriseAnalyticsService = null,
        ICycleCountPlanningService? cycleCountPlanningService = null,
        IInventoryRiskScoringService? inventoryRiskScoringService = null,
        IInventoryRiskRecommendationService? inventoryRiskRecommendationService = null)
    {
        _db = db;
        _logger = logger;
        _inventoryBalanceService = inventoryBalanceService;
        _unitOfWork = unitOfWork;
        _inventoryTransactionService = inventoryTransactionService ?? new InventoryTransactionService(db);
        _enterpriseAnalyticsService = enterpriseAnalyticsService ?? new EnterpriseAnalyticsService(db);
        _cycleCountPlanningService = cycleCountPlanningService ?? new CycleCountPlanningService(db, unitOfWork);
        _inventoryRiskScoringService = inventoryRiskScoringService ?? new InventoryRiskScoringService(db);
        _inventoryRiskRecommendationService = inventoryRiskRecommendationService
            ?? new InventoryRiskRecommendationService(db, unitOfWork, _cycleCountPlanningService, _inventoryRiskScoringService);
    }


    private int? GetScopedWarehouseId()
    {
        if (User.IsInRole("Admin")) return null;
        var claim = User.FindFirst("WarehouseId")?.Value;
        return int.TryParse(claim, out var id) ? id : (int?)null;
    }

    private IReadOnlyList<int> GetScopedOwnerPartnerIds()
    {
        if (User.IsInRole("Admin")) return Array.Empty<int>();

        return User.FindAll(TenantClaimTypes.OwnerPartnerId)
            .Select(c => int.TryParse(c.Value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }


    private bool CanSeeFinancial()
        => User.IsInRole(WmsRoles.Admin)
            || string.Equals(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, WmsRoles.Admin, StringComparison.OrdinalIgnoreCase)
            || User.Claims.Any(c =>
                c.Type == PermissionClaimTypes.Permission &&
                string.Equals(c.Value, WmsPermissions.ReportViewFinancial, StringComparison.Ordinal));


    private static DateTime VietnamNow => VietnamTime.Now;


    private static string GetHoldStatusDisplay(InventoryHoldStatusEnum? status)
        => status switch
        {
            null => "Theo dữ liệu chốt",
            InventoryHoldStatusEnum.Available => "Khả dụng",
            InventoryHoldStatusEnum.QcHold => "Giữ kiểm tra chất lượng",
            InventoryHoldStatusEnum.Quarantine => "Cách ly",
            InventoryHoldStatusEnum.Damaged => "Hư hỏng",
            InventoryHoldStatusEnum.Expired => "Hết hạn",
            InventoryHoldStatusEnum.Blocked => "Khóa nghiệp vụ",
            InventoryHoldStatusEnum.Consigned => "Ký gửi khả dụng",
            _ => "Không xác định"
        };

}
