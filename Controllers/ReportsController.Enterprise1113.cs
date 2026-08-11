using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WMS.Models;

namespace WMS.Controllers;

public partial class ReportsController
{
    [Authorize(Roles = WmsRoles.ReportRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> SemanticBi(int? warehouseId, int days = 30)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var model = await _enterpriseAnalyticsService.BuildSemanticDashboardAsync(
            warehouseId,
            days,
            CanSeeFinancial(),
            GetScopedOwnerPartnerIds());
        return View(model);
    }

    [Authorize(Roles = WmsRoles.ReportManagerRoles)]
    [Authorize(Policy = WmsPermissions.ReportViewFinancial)]
    public async Task<IActionResult> FinancialCostDashboard(int? warehouseId, int days = 30)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var model = await _enterpriseAnalyticsService.BuildFinancialCostDashboardAsync(
            warehouseId,
            days,
            CanSeeFinancial(),
            GetScopedOwnerPartnerIds());
        return View(model);
    }

    [Authorize(Roles = WmsRoles.ReportRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> PredictiveAlerts(int? warehouseId)
    {
        var scopedWh = GetScopedWarehouseId();
        if (scopedWh.HasValue) warehouseId = scopedWh.Value;
        var model = await _enterpriseAnalyticsService.BuildPredictiveAlertsAsync(warehouseId, GetScopedOwnerPartnerIds());
        return View(model);
    }

    [Authorize(Roles = "Admin")]
    [Authorize(Policy = WmsPermissions.AuditTrailView)]
    public async Task<IActionResult> AuditAnalytics()
    {
        var model = await _enterpriseAnalyticsService.BuildAuditAnalyticsAsync();
        return View(model);
    }

    [Authorize(Roles = WmsRoles.ReportRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> AiAssistant(long? sessionId = null)
    {
        var model = await _enterpriseAnalyticsService.LoadAssistantAsync(User, sessionId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = WmsRoles.ReportRoles)]
    [Authorize(Policy = WmsPermissions.ReportView)]
    public async Task<IActionResult> AskAiAssistant(string prompt, long? sessionId = null)
    {
        var message = await _enterpriseAnalyticsService.AskAssistantAsync(User, prompt, sessionId);
        return RedirectToAction(nameof(AiAssistant), new { sessionId = message.Session.AiAssistantSessionId });
    }
}
