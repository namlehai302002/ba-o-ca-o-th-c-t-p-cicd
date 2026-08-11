using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WMS.Authorization;
using WMS.Common;
using WMS.Data;
using WMS.Models;
using WMS.ViewModels;

namespace WMS.Services;

public interface IEnterpriseAnalyticsService
{
    Task<SemanticBiDashboardViewModel> BuildSemanticDashboardAsync(int? warehouseId, int days, bool canSeeFinancial, CancellationToken ct = default);
    Task<SemanticBiDashboardViewModel> BuildSemanticDashboardAsync(int? warehouseId, int days, bool canSeeFinancial, IReadOnlyCollection<int> allowedOwnerPartnerIds, CancellationToken ct = default);
    Task<FinancialCostDashboardViewModel> BuildFinancialCostDashboardAsync(int? warehouseId, int days, bool canSeeFinancial, CancellationToken ct = default);
    Task<FinancialCostDashboardViewModel> BuildFinancialCostDashboardAsync(int? warehouseId, int days, bool canSeeFinancial, IReadOnlyCollection<int> allowedOwnerPartnerIds, CancellationToken ct = default);
    Task<PredictiveAlertsViewModel> BuildPredictiveAlertsAsync(int? warehouseId, CancellationToken ct = default);
    Task<PredictiveAlertsViewModel> BuildPredictiveAlertsAsync(int? warehouseId, IReadOnlyCollection<int> allowedOwnerPartnerIds, CancellationToken ct = default);
    Task<AuditAnalyticsViewModel> BuildAuditAnalyticsAsync(CancellationToken ct = default);
    Task<AiAssistantViewModel> LoadAssistantAsync(ClaimsPrincipal user, long? sessionId, CancellationToken ct = default);
    Task<AiAssistantMessage> AskAssistantAsync(ClaimsPrincipal user, string prompt, long? sessionId, CancellationToken ct = default);
}

public interface IRoleWorkspaceService
{
    RoleWorkspaceViewModel Build(ClaimsPrincipal user);
}

public interface IProductionSreService
{
    Task RecordRequestAsync(RequestTelemetryLog row, CancellationToken ct = default);
    Task<SreDashboardViewModel> BuildDashboardAsync(int periodMinutes, CancellationToken ct = default);
    Task<SreMetricSnapshot> CaptureSnapshotAsync(int periodMinutes, CancellationToken ct = default);
}

public sealed class EnterpriseAnalyticsService : IEnterpriseAnalyticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;

    public EnterpriseAnalyticsService(AppDbContext db)
    {
        _db = db;
    }

    private static DateTime Now => VietnamTime.Now;

    public Task<SemanticBiDashboardViewModel> BuildSemanticDashboardAsync(int? warehouseId, int days, bool canSeeFinancial, CancellationToken ct = default)
        => BuildSemanticDashboardAsync(warehouseId, days, canSeeFinancial, Array.Empty<int>(), ct);

    public async Task<SemanticBiDashboardViewModel> BuildSemanticDashboardAsync(
        int? warehouseId,
        int days,
        bool canSeeFinancial,
        IReadOnlyCollection<int> allowedOwnerPartnerIds,
        CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var ownerScope = NormalizeOwnerScope(allowedOwnerPartnerIds);
        var definitions = await EnsureMetricDefinitionsAsync(ct);
        var from = Now.Date.AddDays(-days);
        var scopeKey = ScopeKey(warehouseId, ownerScope);

        var existing = await _db.SemanticMetricSnapshots
            .Where(x => x.MetricDate == Now.Date && x.ScopeKey == scopeKey)
            .ToListAsync(ct);

        if (existing.Count == 0)
        {
            var snapshots = await CalculateMetricSnapshotsAsync(definitions, warehouseId, ownerScope, from, Now.Date, ct);
            _db.SemanticMetricSnapshots.AddRange(snapshots);
            await _db.SaveChangesAsync(ct);
        }

        var query = _db.SemanticMetricSnapshots
            .AsNoTracking()
            .Include(x => x.MetricDefinition)
            .Where(x => x.MetricDate >= from && x.ScopeKey == scopeKey);

        if (!canSeeFinancial)
            query = query.Where(x => !x.MetricDefinition.IsFinancial);

        return new SemanticBiDashboardViewModel
        {
            WarehouseId = warehouseId,
            Days = days,
            Warehouses = await ActiveWarehousesAsync(warehouseId, ct),
            Definitions = definitions.Where(x => canSeeFinancial || !x.IsFinancial).ToList(),
            Snapshots = await query.OrderByDescending(x => x.MetricDate).ThenBy(x => x.MetricDefinition.MetricCode).ToListAsync(ct),
            CanSeeFinancial = canSeeFinancial
        };
    }

    public Task<FinancialCostDashboardViewModel> BuildFinancialCostDashboardAsync(int? warehouseId, int days, bool canSeeFinancial, CancellationToken ct = default)
        => BuildFinancialCostDashboardAsync(warehouseId, days, canSeeFinancial, Array.Empty<int>(), ct);

    public async Task<FinancialCostDashboardViewModel> BuildFinancialCostDashboardAsync(
        int? warehouseId,
        int days,
        bool canSeeFinancial,
        IReadOnlyCollection<int> allowedOwnerPartnerIds,
        CancellationToken ct = default)
    {
        if (!canSeeFinancial)
            throw new UnauthorizedAccessException("Bạn không có quyền xem dữ liệu chi phí.");

        days = Math.Clamp(days, 1, 365);
        var ownerScope = NormalizeOwnerScope(allowedOwnerPartnerIds);
        var hasOwnerScope = ownerScope.Count > 0;
        var from = Now.Date.AddDays(-days);

        var invoiceRows = await _db.ThreePlInvoiceLines
            .AsNoTracking()
            .Include(x => x.Invoice).ThenInclude(x => x.Warehouse)
            .Include(x => x.Invoice).ThenInclude(x => x.OwnerPartner)
            .Where(x => x.Invoice.CreatedAt >= from
                && (!warehouseId.HasValue || x.Invoice.WarehouseId == warehouseId.Value)
                && (!hasOwnerScope || (x.Invoice.OwnerPartnerId.HasValue && ownerScope.Contains(x.Invoice.OwnerPartnerId.Value))))
            .Select(x => new FinancialCostRow
            {
                OwnerName = x.Invoice.OwnerPartner != null ? x.Invoice.OwnerPartner.PartnerName : "Toàn hệ thống",
                WarehouseName = x.Invoice.Warehouse.WarehouseName,
                ServiceType = EnterpriseUiLabels.ThreePlChargeType(x.ChargeType),
                SourceType = "Hóa đơn kho nhiều chủ hàng",
                SourceCode = x.Invoice.InvoiceCode,
                Quantity = x.Quantity,
                Amount = x.TotalAmount
            })
            .ToListAsync(ct);

        var unpricedLaborActivityCount = await _db.LaborActivities
            .AsNoTracking()
            .Where(x => x.StartedAt >= from
                && (!warehouseId.HasValue || x.WarehouseId == warehouseId.Value)
                && (!hasOwnerScope || (x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value))))
            .CountAsync(ct);

        // LaborActivity has duration and productivity but no approved monetary rate.
        // Do not invent a rate: only ledger-backed invoice amounts belong in the cost total.
        var rows = invoiceRows.OrderByDescending(x => x.Amount).ToList();
        return new FinancialCostDashboardViewModel
        {
            WarehouseId = warehouseId,
            Days = days,
            Warehouses = await ActiveWarehousesAsync(warehouseId, ct),
            Rows = rows,
            TotalCost = rows.Sum(x => x.Amount),
            UnpricedLaborActivityCount = unpricedLaborActivityCount
        };
    }

    public Task<PredictiveAlertsViewModel> BuildPredictiveAlertsAsync(int? warehouseId, CancellationToken ct = default)
        => BuildPredictiveAlertsAsync(warehouseId, Array.Empty<int>(), ct);

    public async Task<PredictiveAlertsViewModel> BuildPredictiveAlertsAsync(
        int? warehouseId,
        IReadOnlyCollection<int> allowedOwnerPartnerIds,
        CancellationToken ct = default)
    {
        var today = Now.Date;
        var rows = new List<EnterprisePredictiveAlert>();
        var ownerScope = allowedOwnerPartnerIds.Where(id => id > 0).Distinct().ToList();
        var hasOwnerScope = ownerScope.Count > 0;

        var stockoutInventoryRows = await _db.ItemLocations.AsNoTracking()
            .Include(x => x.Item)
            .Include(x => x.Location).ThenInclude(x => x!.Zone)
            .Where(x => x.Item != null && x.Item.IsActive && x.Item.MinThreshold > 0
                && x.Location != null && x.Location.Zone != null
                && (!warehouseId.HasValue || x.Location.Zone.WarehouseId == warehouseId.Value)
                && (!hasOwnerScope || (x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value))))
            .ToListAsync(ct);

        var stockoutRows = stockoutInventoryRows
            .Where(x => x.Item != null && x.Location?.Zone != null)
            .GroupBy(x => new
            {
                x.ItemId,
                x.OwnerPartnerId,
                WarehouseId = x.Location!.Zone.WarehouseId
            })
            .Select(g =>
            {
                var item = g.First().Item!;
                var availableQty = g
                    .Where(x => IsPredictiveAvailableStock(x.HoldStatus))
                    .Sum(x => Math.Max(0m, x.Quantity - x.ReservedQty));

                return new
                {
                    item.ItemId,
                    item.ItemCode,
                    item.MinThreshold,
                    g.Key.OwnerPartnerId,
                    g.Key.WarehouseId,
                    AvailableQty = availableQty
                };
            })
            .Where(x => x.AvailableQty <= x.MinThreshold)
            .OrderBy(x => x.AvailableQty)
            .ThenBy(x => x.ItemCode)
            .ToList();

        foreach (var item in stockoutRows)
        {
            rows.Add(NewPredictiveAlert(
                PredictiveAlertTypeEnum.StockoutRisk,
                EnterpriseSeverityEnum.Critical,
                item.WarehouseId,
                item.OwnerPartnerId,
                "ItemStock",
                $"{item.ItemId}:{item.WarehouseId}:{item.OwnerPartnerId?.ToString() ?? "0"}",
                $"Nguy cơ thiếu hàng {item.ItemCode}",
                $"Tồn khả dụng {item.AvailableQty:N2} thấp hơn ngưỡng {item.MinThreshold:N2}.",
                95,
                today.AddDays(1),
                new { item.ItemCode, item.WarehouseId, item.OwnerPartnerId, item.AvailableQty, item.MinThreshold }));
        }

        var expiryRows = await _db.ItemLocations.AsNoTracking()
            .Include(x => x.Item)
            .Include(x => x.Location).ThenInclude(x => x!.Zone)
            .Where(x => x.Quantity > 0 && x.ExpiryDate.HasValue && x.ExpiryDate.Value <= today.AddDays(30)
                && x.Location != null && x.Location.Zone != null
                && (!warehouseId.HasValue || x.Location.Zone.WarehouseId == warehouseId.Value)
                && (!hasOwnerScope || (x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value))))
            .OrderBy(x => x.ExpiryDate)
            .ThenBy(x => x.ItemId)
            .ThenBy(x => x.LocationId)
            .ToListAsync(ct);

        foreach (var row in expiryRows)
        {
            var daysLeft = (row.ExpiryDate!.Value.Date - today).Days;
            rows.Add(NewPredictiveAlert(PredictiveAlertTypeEnum.ExpiryRisk, daysLeft <= 7 ? EnterpriseSeverityEnum.Critical : EnterpriseSeverityEnum.Warning, row.Location!.Zone.WarehouseId, row.OwnerPartnerId, "ItemLocation", row.ItemLocationId.ToString(), $"Hàng sắp hết hạn {row.Item?.ItemCode}", $"Lô {row.LotNumber} còn {row.Quantity:N2}, hết hạn sau {daysLeft} ngày.", daysLeft <= 7 ? 90 : 70, row.ExpiryDate.Value.Date, new { row.Item?.ItemCode, row.LotNumber, row.Quantity, row.ExpiryDate, WarehouseId = row.Location.Zone.WarehouseId, row.OwnerPartnerId }));
        }

        var overdueVouchers = await _db.Vouchers.AsNoTracking()
            .Where(x => !x.IsCancelled && !x.IsPosted && x.RequestedDeliveryDate.HasValue && x.RequestedDeliveryDate.Value < today
                && (!warehouseId.HasValue || x.WarehouseId == warehouseId.Value)
                && (!hasOwnerScope || (x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value))))
            .OrderBy(x => x.RequestedDeliveryDate)
            .ThenBy(x => x.VoucherCode)
            .ToListAsync(ct);

        foreach (var voucher in overdueVouchers)
        {
            rows.Add(NewPredictiveAlert(PredictiveAlertTypeEnum.SlaDelay, EnterpriseSeverityEnum.Critical, voucher.WarehouseId, voucher.OwnerPartnerId, "Voucher", voucher.VoucherId.ToString(), $"Nguy cơ trễ SLA {voucher.VoucherCode}", $"Phiếu cần giao trước {voucher.RequestedDeliveryDate:dd/MM/yyyy} nhưng chưa hoàn tất.", 92, today, new { voucher.VoucherCode, voucher.RequestedDeliveryDate, voucher.FulfillmentStatus }));
        }

        // Sức chứa là thuộc tính dùng chung của vị trí, hiện schema không có phần sở hữu
        // theo chủ hàng. Không công bố tải tổng của các chủ hàng khác trong phiên bị scope.
        var capacityRows = hasOwnerScope
            ? new List<Location>()
            : await _db.Locations.AsNoTracking()
                .Include(x => x.Zone)
                .Where(x => x.IsActive && x.MaxCapacity > 0 && (!warehouseId.HasValue || (x.Zone != null && x.Zone.WarehouseId == warehouseId.Value)))
                .OrderBy(x => x.Zone != null ? x.Zone.WarehouseId : 0)
                .ThenBy(x => x.LocationCode)
                .ToListAsync(ct);
        var locationIds = capacityRows.Select(x => x.LocationId).ToList();
        var qtyByLocation = locationIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await _db.ItemLocations.AsNoTracking()
                .Where(x => locationIds.Contains(x.LocationId))
                .GroupBy(x => x.LocationId)
                .Select(g => new { LocationId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.LocationId, x => x.Qty, ct);

        foreach (var location in capacityRows)
        {
            var qty = qtyByLocation.GetValueOrDefault(location.LocationId);
            if (qty < location.MaxCapacity * 0.9m) continue;
            rows.Add(NewPredictiveAlert(PredictiveAlertTypeEnum.CapacityOverload, qty >= location.MaxCapacity ? EnterpriseSeverityEnum.Critical : EnterpriseSeverityEnum.Warning, location.Zone?.WarehouseId ?? warehouseId, null, "Location", location.LocationId.ToString(), $"Vị trí quá tải {location.LocationCode}", $"Tải vị trí {qty:N2}/{location.MaxCapacity:N2}.", Math.Min(99, qty / location.MaxCapacity * 100), today.AddDays(1), new { location.LocationCode, qty, location.MaxCapacity }));
        }

        await UpsertPredictiveAlertsAsync(rows, warehouseId, ownerScope, ct);

        var alerts = await _db.EnterprisePredictiveAlerts.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.OwnerPartner)
            .Where(x => x.Status == EnterpriseFindingStatusEnum.Open
                && (!warehouseId.HasValue || x.WarehouseId == warehouseId.Value || x.WarehouseId == null)
                && (!hasOwnerScope
                    || (x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value))))
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.RiskScore)
            .Take(200)
            .ToListAsync(ct);

        return new PredictiveAlertsViewModel
        {
            WarehouseId = warehouseId,
            Warehouses = await ActiveWarehousesAsync(warehouseId, ct),
            Alerts = alerts
        };
    }

    public async Task<AuditAnalyticsViewModel> BuildAuditAnalyticsAsync(CancellationToken ct = default)
    {
        var from = Now.AddDays(-14);
        var findings = new List<AuditAnalyticsFinding>();

        var exports = await _db.AuditLogs.AsNoTracking()
            .Where(x => x.ChangedAt >= from && x.ActionType == "EXPORT")
            .OrderByDescending(x => x.ChangedAt)
            .ThenByDescending(x => x.AuditLogId)
            .Take(200)
            .ToListAsync(ct);
        foreach (var row in exports)
            findings.Add(NewFinding(AuditFindingTypeEnum.SensitiveExport, EnterpriseSeverityEnum.Warning, row.ChangedBy, "AuditLog", row.AuditLogId.ToString(), "Xuất dữ liệu nhạy cảm", row.ChangedAt, new { row.TableName, row.RecordId, row.ChangedAt }));

        var denied = await _db.AuditLogs.AsNoTracking()
            .Where(x => x.ChangedAt >= from && x.ActionType == "DENIED")
            .OrderByDescending(x => x.ChangedAt)
            .ThenByDescending(x => x.AuditLogId)
            .Take(200)
            .ToListAsync(ct);
        foreach (var row in denied)
            findings.Add(NewFinding(AuditFindingTypeEnum.ScopeDenied, EnterpriseSeverityEnum.Critical, row.ChangedBy, "AuditLog", row.AuditLogId.ToString(), "Truy cập ngoài phạm vi bị chặn", row.ChangedAt, new { row.TableName, row.RecordId, row.ChangedAt }));

        var loginRows = await _db.LoginAuditLogs.AsNoTracking()
            .Where(x => x.CreatedAt >= from)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.LoginAuditLogId)
            .Take(500)
            .ToListAsync(ct);
        foreach (var row in loginRows.Where(x => x.CreatedAt.Hour < 6 || x.CreatedAt.Hour >= 22))
            findings.Add(NewFinding(AuditFindingTypeEnum.OutOfHoursAccess, EnterpriseSeverityEnum.Warning, row.UserName, "LoginAuditLog", row.LoginAuditLogId.ToString(), "Truy cập ngoài giờ vận hành", row.CreatedAt, new { row.UserName, row.Outcome, row.CreatedAt }));

        var mutationGroups = await _db.AuditLogs.AsNoTracking()
            .Where(x => x.ChangedAt >= from && x.ActionType != "EXPORT" && x.ActionType != "DENIED")
            .GroupBy(x => x.ChangedBy ?? "unknown")
            .Select(g => new { UserName = g.Key, Count = g.Count(), LastAt = g.Max(x => x.ChangedAt) })
            .ToListAsync(ct);
        var mutationCounts = mutationGroups.Select(row => (decimal)row.Count).OrderBy(value => value).ToList();
        var robustSample = mutationCounts.Count >= 4;
        var median = Percentile(mutationCounts, 0.50m);
        var q1 = Percentile(mutationCounts, 0.25m);
        var q3 = Percentile(mutationCounts, 0.75m);
        var iqr = q3 - q1;
        var mad = mutationCounts.Count == 0
            ? 0m
            : Percentile(mutationCounts.Select(value => Math.Abs(value - median)).OrderBy(value => value).ToList(), 0.50m);
        var iqrThreshold = q3 + (1.5m * iqr);
        var madThreshold = median + (3m * 1.4826m * mad);

        foreach (var row in mutationGroups)
        {
            var isAbnormal = robustSample
                ? row.Count >= 20 && row.Count > iqrThreshold && row.Count > madThreshold
                : row.Count >= 80;
            if (!isAbnormal)
                continue;

            findings.Add(NewFinding(
                AuditFindingTypeEnum.AbnormalMutation,
                EnterpriseSeverityEnum.Warning,
                row.UserName,
                "AuditLog",
                row.UserName,
                "Tần suất thao tác bất thường",
                row.LastAt,
                new
                {
                    row.Count,
                    row.LastAt,
                    Method = robustSample ? "IQR+MAD" : "MIN_SAMPLE_FALLBACK",
                    SampleSize = mutationCounts.Count,
                    Median = median,
                    Mad = mad,
                    Q1 = q1,
                    Q3 = q3,
                    IqrThreshold = iqrThreshold,
                    MadThreshold = madThreshold,
                    FallbackThreshold = robustSample ? (int?)null : 80
                }));
        }

        await UpsertAuditFindingsAsync(findings, ct);

        var persisted = await _db.AuditAnalyticsFindings.AsNoTracking()
            .Where(x => x.OccurredAt >= from && x.Status != EnterpriseFindingStatusEnum.Resolved)
            .OrderByDescending(x => x.OccurredAt)
            .Take(300)
            .ToListAsync(ct);

        return new AuditAnalyticsViewModel
        {
            Findings = persisted,
            SensitiveExportCount = persisted.Count(x => x.FindingType == AuditFindingTypeEnum.SensitiveExport),
            OutOfHoursCount = persisted.Count(x => x.FindingType == AuditFindingTypeEnum.OutOfHoursAccess),
            ScopeDeniedCount = persisted.Count(x => x.FindingType == AuditFindingTypeEnum.ScopeDenied),
            AbnormalMutationCount = persisted.Count(x => x.FindingType == AuditFindingTypeEnum.AbnormalMutation)
        };
    }

    public async Task<AiAssistantViewModel> LoadAssistantAsync(ClaimsPrincipal user, long? sessionId, CancellationToken ct = default)
    {
        var userName = user.Identity?.Name ?? "system";
        var session = sessionId.HasValue
            ? await _db.AiAssistantSessions.AsNoTracking()
                .Include(x => x.Messages).ThenInclude(x => x.Citations)
                .FirstOrDefaultAsync(x => x.AiAssistantSessionId == sessionId.Value && x.UserName == userName, ct)
            : null;

        return new AiAssistantViewModel
        {
            SessionId = session?.AiAssistantSessionId,
            RoleName = CurrentRole(user),
            ScopeSummary = ScopeSummary(user),
            Messages = session?.Messages.OrderBy(x => x.CreatedAt).ToList() ?? new List<AiAssistantMessage>()
        };
    }

    public async Task<AiAssistantMessage> AskAssistantAsync(ClaimsPrincipal user, string prompt, long? sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new BusinessRuleException("Vui lòng nhập câu hỏi.", "AI_ASSISTANT_PROMPT_REQUIRED", "AiAssistantMessage");

        var scopedWarehouseId = ScopedWarehouseId(user);
        var role = CurrentRole(user);
        var userName = user.Identity?.Name ?? "system";
        var isMutation = ContainsMutationIntent(prompt);

        var session = sessionId.HasValue
            ? await _db.AiAssistantSessions.Include(x => x.Messages).FirstOrDefaultAsync(x => x.AiAssistantSessionId == sessionId.Value && x.UserName == userName, ct)
            : null;

        if (session == null)
        {
            session = new AiAssistantSession
            {
                SessionCode = "AI-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                UserName = userName,
                RoleName = role,
                WarehouseId = scopedWarehouseId,
                Purpose = "Governed internal BI assistant",
                CreatedAt = Now,
                LastMessageAt = Now
            };
            _db.AiAssistantSessions.Add(session);
        }

        var scopedOwnerIds = user.IsInRole(WmsRoles.Admin)
            ? Array.Empty<int>()
            : user.FindAll(TenantClaimTypes.OwnerPartnerId)
                .Select(claim => int.TryParse(claim.Value, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToArray();
        var canSeeFinancial = CanSeeFinancial(user);
        var semantic = await BuildSemanticDashboardAsync(scopedWarehouseId, 30, canSeeFinancial, scopedOwnerIds, ct);
        var alerts = await BuildPredictiveAlertsAsync(scopedWarehouseId, scopedOwnerIds, ct);
        var intent = ResolveAssistantIntent(prompt);
        var topMetric = intent == AssistantReadIntent.Cost && !canSeeFinancial
            ? null
            : SelectCitationMetric(intent, semantic);
        var topAlert = SelectCitationAlert(intent, alerts);

        var response = isMutation
            ? "Tôi chỉ được đọc dữ liệu và trích dẫn nguồn. Yêu cầu thay đổi dữ liệu đã bị chặn; vui lòng dùng màn nghiệp vụ có phê duyệt nếu cần thao tác."
            : await BuildAssistantAnswerAsync(prompt, semantic, alerts, intent, scopedWarehouseId, scopedOwnerIds, canSeeFinancial, ct);

        var message = new AiAssistantMessage
        {
            Session = session,
            MessageRole = AiAssistantMessageRoleEnum.Assistant,
            Prompt = prompt.Trim(),
            Response = response,
            IsMutationBlocked = isMutation,
            ScopeSummary = ScopeSummary(user),
            CreatedAt = Now
        };
        _db.AiAssistantMessages.Add(message);

        if (topMetric != null)
        {
            message.Citations.Add(new AiAssistantCitation
            {
                SourceType = "SemanticMetricSnapshot",
                SourceId = topMetric.SemanticMetricSnapshotId.ToString(),
                SourceLabel = topMetric.MetricDefinition.MetricName,
                SourceUrl = "/Reports/SemanticBi",
                Excerpt = topMetric.SourceCitation
            });
        }
        if (topAlert != null)
        {
            message.Citations.Add(new AiAssistantCitation
            {
                SourceType = "EnterprisePredictiveAlert",
                SourceId = topAlert.EnterprisePredictiveAlertId.ToString(),
                SourceLabel = topAlert.Title,
                SourceUrl = "/Reports/PredictiveAlerts",
                Excerpt = topAlert.Message
            });
        }

        session.LastMessageAt = Now;
        await _db.SaveChangesAsync(ct);
        return message;
    }

    private async Task<List<SemanticMetricDefinition>> EnsureMetricDefinitionsAsync(CancellationToken ct)
    {
        var definitions = new[]
        {
            ("inventory.total_stock", "Tổng tồn kho", SemanticMetricCategoryEnum.Inventory, "qty", "Cộng số lượng tồn theo vị trí", "Tồn kho theo vị trí"),
            ("order.open_outbound", "Phiếu xuất đang mở", SemanticMetricCategoryEnum.Order, "order", "Đếm phiếu xuất chưa ghi sổ", "Phiếu kho"),
            ("labor.productivity", "Năng suất lao động trung bình", SemanticMetricCategoryEnum.Labor, "%", "Trung bình tỷ lệ năng suất lao động", "Hoạt động lao động"),
            ("billing.total_cost", "Tổng chi phí kho nhiều chủ hàng", SemanticMetricCategoryEnum.Billing, "VND", "Cộng giá trị dòng phí", "Dòng phí kho nhiều chủ hàng"),
            ("sla.overdue_order", "Phiếu trễ SLA", SemanticMetricCategoryEnum.Sla, "order", "Đếm phiếu quá hạn giao theo ngày yêu cầu", "Phiếu kho")
        };

        var existing = await _db.SemanticMetricDefinitions.ToListAsync(ct);
        foreach (var def in definitions)
        {
            if (existing.Any(x => x.MetricCode == def.Item1)) continue;
            _db.SemanticMetricDefinitions.Add(new SemanticMetricDefinition
            {
                MetricCode = def.Item1,
                MetricName = def.Item2,
                Category = def.Item3,
                Unit = def.Item4,
                Formula = def.Item5,
                SourceLabel = def.Item6,
                IsFinancial = def.Item1.Contains("billing", StringComparison.Ordinal)
            });
        }
        await _db.SaveChangesAsync(ct);
        return await _db.SemanticMetricDefinitions.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Category).ThenBy(x => x.MetricCode).ToListAsync(ct);
    }

    private async Task<List<SemanticMetricSnapshot>> CalculateMetricSnapshotsAsync(
        List<SemanticMetricDefinition> definitions,
        int? warehouseId,
        IReadOnlyCollection<int> allowedOwnerPartnerIds,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        var result = new List<SemanticMetricSnapshot>();
        var ownerScope = NormalizeOwnerScope(allowedOwnerPartnerIds);
        var hasOwnerScope = ownerScope.Count > 0;
        var singleOwnerId = ownerScope.Count == 1 ? ownerScope[0] : (int?)null;
        var endExclusive = to.Date.AddDays(1);
        foreach (var def in definitions)
        {
            var value = 0m;
            var count = 0;
            if (def.MetricCode == "inventory.total_stock")
            {
                var query = _db.ItemLocations.AsNoTracking().Include(x => x.Location).ThenInclude(x => x!.Zone).AsQueryable();
                if (warehouseId.HasValue) query = query.Where(x => x.Location != null && x.Location.Zone != null && x.Location.Zone.WarehouseId == warehouseId.Value);
                if (hasOwnerScope) query = query.Where(x => x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value));
                value = await query.SumAsync(x => x.Quantity, ct);
                count = await query.CountAsync(ct);
            }
            else if (def.MetricCode == "order.open_outbound")
            {
                var types = new[] { VoucherTypeEnum.XuatKho, VoucherTypeEnum.TraNCC, VoucherTypeEnum.ChuyenKho, VoucherTypeEnum.XuatSanXuat };
                var query = _db.Vouchers.AsNoTracking().Where(x => !x.IsCancelled && !x.IsPosted && types.Contains(x.VoucherType));
                if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId.Value);
                if (hasOwnerScope) query = query.Where(x => x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value));
                value = await query.CountAsync(ct);
                count = (int)value;
            }
            else if (def.MetricCode == "labor.productivity")
            {
                var query = _db.LaborActivities.AsNoTracking().Where(x => x.StartedAt >= from && x.StartedAt < endExclusive);
                if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId.Value);
                if (hasOwnerScope) query = query.Where(x => x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value));
                var rows = await query.Select(x => x.ProductivityPercent).ToListAsync(ct);
                value = rows.Count == 0 ? 0 : rows.Average();
                count = rows.Count;
            }
            else if (def.MetricCode == "billing.total_cost")
            {
                var query = _db.ThreePlInvoiceLines.AsNoTracking().Include(x => x.Invoice).Where(x => x.Invoice.CreatedAt >= from && x.Invoice.CreatedAt < endExclusive);
                if (warehouseId.HasValue) query = query.Where(x => x.Invoice.WarehouseId == warehouseId.Value);
                if (hasOwnerScope) query = query.Where(x => x.Invoice.OwnerPartnerId.HasValue && ownerScope.Contains(x.Invoice.OwnerPartnerId.Value));
                value = await query.SumAsync(x => x.TotalAmount, ct);
                count = await query.CountAsync(ct);
            }
            else if (def.MetricCode == "sla.overdue_order")
            {
                var query = _db.Vouchers.AsNoTracking().Where(x => !x.IsCancelled && !x.IsPosted && x.RequestedDeliveryDate.HasValue && x.RequestedDeliveryDate.Value < Now.Date);
                if (warehouseId.HasValue) query = query.Where(x => x.WarehouseId == warehouseId.Value);
                if (hasOwnerScope) query = query.Where(x => x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value));
                value = await query.CountAsync(ct);
                count = (int)value;
            }

            result.Add(new SemanticMetricSnapshot
            {
                SemanticMetricDefinitionId = def.SemanticMetricDefinitionId,
                WarehouseId = warehouseId,
                OwnerPartnerId = singleOwnerId,
                MetricDate = Now.Date,
                MetricValue = value,
                ScopeKey = ScopeKey(warehouseId, ownerScope),
                SourceCount = count,
                SourceCitation = $"{def.SourceLabel}: {def.Formula}"
            });
        }
        return result;
    }

    private async Task UpsertPredictiveAlertsAsync(
        IReadOnlyCollection<EnterprisePredictiveAlert> alerts,
        int? warehouseId,
        IReadOnlyCollection<int> allowedOwnerPartnerIds,
        CancellationToken ct)
    {
        var hasOwnerScope = allowedOwnerPartnerIds.Count > 0;
        var existingAlerts = await _db.EnterprisePredictiveAlerts
            .Where(x => x.Status != EnterpriseFindingStatusEnum.Resolved
                && (!warehouseId.HasValue || x.WarehouseId == warehouseId.Value)
                && (!hasOwnerScope
                    || (x.OwnerPartnerId.HasValue && allowedOwnerPartnerIds.Contains(x.OwnerPartnerId.Value))))
            .ToListAsync(ct);

        var remaining = existingAlerts.ToHashSet();
        foreach (var alert in alerts)
        {
            var matches = existingAlerts
                .Where(x => x.AlertType == alert.AlertType
                    && x.ReferenceType == alert.ReferenceType
                    && x.ReferenceId == alert.ReferenceId
                    && x.WarehouseId == alert.WarehouseId
                    && x.OwnerPartnerId == alert.OwnerPartnerId)
                .OrderBy(x => x.EnterprisePredictiveAlertId)
                .ToList();
            var current = matches.FirstOrDefault();
            if (current == null)
            {
                _db.EnterprisePredictiveAlerts.Add(alert);
                continue;
            }

            current.Severity = alert.Severity;
            current.Title = alert.Title;
            current.Message = alert.Message;
            current.RiskScore = alert.RiskScore;
            current.ForecastFor = alert.ForecastFor;
            current.CitationJson = alert.CitationJson;
            current.ResolvedAt = null;
            remaining.Remove(current);

            foreach (var duplicate in matches.Skip(1))
            {
                duplicate.Status = EnterpriseFindingStatusEnum.Resolved;
                duplicate.ResolvedAt = Now;
                remaining.Remove(duplicate);
            }
        }

        foreach (var stale in remaining)
        {
            stale.Status = EnterpriseFindingStatusEnum.Resolved;
            stale.ResolvedAt = Now;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static bool IsPredictiveAvailableStock(InventoryHoldStatusEnum status)
        => status is InventoryHoldStatusEnum.Available or InventoryHoldStatusEnum.Consigned;

    private async Task UpsertAuditFindingsAsync(List<AuditAnalyticsFinding> findings, CancellationToken ct)
    {
        var existingFindings = await _db.AuditAnalyticsFindings.ToListAsync(ct);
        var activeAbnormalKeys = findings
            .Where(finding => finding.FindingType == AuditFindingTypeEnum.AbnormalMutation)
            .Select(finding => (finding.ReferenceType, finding.ReferenceId))
            .ToHashSet();

        foreach (var finding in findings)
        {
            var existing = existingFindings.FirstOrDefault(x =>
                x.FindingType == finding.FindingType
                && x.ReferenceType == finding.ReferenceType
                && x.ReferenceId == finding.ReferenceId);
            if (existing == null)
            {
                _db.AuditAnalyticsFindings.Add(finding);
                continue;
            }

            existing.Severity = finding.Severity;
            existing.UserName = finding.UserName;
            existing.Title = finding.Title;
            existing.EvidenceJson = finding.EvidenceJson;
            existing.OccurredAt = finding.OccurredAt;
            if (existing.FindingType == AuditFindingTypeEnum.AbnormalMutation
                && existing.Status == EnterpriseFindingStatusEnum.Resolved)
            {
                existing.Status = EnterpriseFindingStatusEnum.Open;
                existing.ReviewedAt = null;
                existing.ReviewedBy = null;
            }
        }

        foreach (var stale in existingFindings.Where(finding =>
                     finding.FindingType == AuditFindingTypeEnum.AbnormalMutation
                     && finding.Status != EnterpriseFindingStatusEnum.Resolved
                     && !activeAbnormalKeys.Contains((finding.ReferenceType, finding.ReferenceId))))
        {
            stale.Status = EnterpriseFindingStatusEnum.Resolved;
        }
        await _db.SaveChangesAsync(ct);
    }

    private static EnterprisePredictiveAlert NewPredictiveAlert(PredictiveAlertTypeEnum type, EnterpriseSeverityEnum severity, int? warehouseId, int? ownerId, string referenceType, string referenceId, string title, string message, decimal score, DateTime forecastFor, object citation)
        => new()
        {
            AlertType = type,
            Severity = severity,
            WarehouseId = warehouseId,
            OwnerPartnerId = ownerId,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Title = title,
            Message = message,
            RiskScore = score,
            ForecastFor = forecastFor,
            CitationJson = JsonSerializer.Serialize(citation, JsonOptions),
            CreatedAt = Now
        };

    private static AuditAnalyticsFinding NewFinding(AuditFindingTypeEnum type, EnterpriseSeverityEnum severity, string? userName, string referenceType, string referenceId, string title, DateTime occurredAt, object evidence)
        => new()
        {
            FindingType = type,
            Severity = severity,
            UserName = userName,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Title = title,
            EvidenceJson = JsonSerializer.Serialize(evidence, JsonOptions),
            OccurredAt = occurredAt,
            CreatedAt = Now
        };

    private static decimal Percentile(IReadOnlyList<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0)
            return 0m;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = Math.Clamp(percentile, 0m, 1m) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        var fraction = position - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * fraction);
    }

    private static bool ContainsMutationIntent(string prompt)
    {
        var text = prompt.ToLowerInvariant();
        return new[] { "xóa", "xoá", "sửa", "duyệt", "post", "hủy", "huỷ", "tạo phiếu", "update", "delete", "approve", "cancel" }
            .Any(text.Contains);
    }

    private async Task<string> BuildAssistantAnswerAsync(
        string prompt,
        SemanticBiDashboardViewModel semantic,
        PredictiveAlertsViewModel alerts,
        AssistantReadIntent intent,
        int? warehouseId,
        IReadOnlyCollection<int> allowedOwnerPartnerIds,
        bool canSeeFinancial,
        CancellationToken ct)
    {
        return intent switch
        {
            AssistantReadIntent.Inventory => await BuildInventoryAnswerAsync(prompt, semantic, warehouseId, allowedOwnerPartnerIds, ct),
            AssistantReadIntent.Alert => BuildAlertAnswer(alerts, warehouseId),
            AssistantReadIntent.Cost => await BuildCostAnswerAsync(warehouseId, allowedOwnerPartnerIds, canSeeFinancial, ct),
            AssistantReadIntent.Sla => BuildSlaAnswer(semantic, alerts, warehouseId),
            AssistantReadIntent.Labor => BuildLaborAnswer(semantic, warehouseId),
            _ => BuildSummaryAnswer(prompt, semantic, alerts)
        };
    }

    private static string BuildAlertAnswer(PredictiveAlertsViewModel alerts, int? warehouseId)
    {
        var total = alerts.Alerts.Count;
        var critical = alerts.Alerts.Count(x => x.Severity == EnterpriseSeverityEnum.Critical);
        var warning = alerts.Alerts.Count(x => x.Severity == EnterpriseSeverityEnum.Warning);
        var info = alerts.Alerts.Count(x => x.Severity == EnterpriseSeverityEnum.Info);
        var topAlerts = alerts.Alerts
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.RiskScore)
            .Take(3)
            .Select(x => $"{AlertTypeLabel(x.AlertType)} - {x.Title} (điểm ưu tiên {x.RiskScore:N1}/100)")
            .ToList();
        var topText = topAlerts.Count == 0 ? "chưa có cảnh báo mở cần xử lý" : string.Join("; ", topAlerts);

        return $"Tổng cảnh báo hiện tại: {total} cảnh báo mở. " +
               $"Phân loại mức độ: khẩn cấp {critical}, cần theo dõi {warning}, thông tin {info}. " +
               $"Top cảnh báo: {topText}. Phạm vi: {ScopeText(warehouseId)}. " +
               "Nguồn đối chiếu: Cảnh báo theo quy tắc.";
    }

    private async Task<string> BuildCostAnswerAsync(
        int? warehouseId,
        IReadOnlyCollection<int> allowedOwnerPartnerIds,
        bool canSeeFinancial,
        CancellationToken ct)
    {
        if (!canSeeFinancial)
            return "Bạn chưa có quyền xem số liệu chi phí. Vui lòng yêu cầu quyền báo cáo tài chính hoặc mở màn Chi phí vận hành bằng tài khoản được phân quyền; trợ lý sẽ không hiển thị số tiền khi chưa đủ quyền.";

        var financial = await BuildFinancialCostDashboardAsync(warehouseId, 30, true, allowedOwnerPartnerIds, ct);
        var ownerText = FormatTopFinancialGroups(financial.Rows.GroupBy(x => x.OwnerName), "chủ hàng");
        var warehouseText = FormatTopFinancialGroups(financial.Rows.GroupBy(x => x.WarehouseName), "kho");
        var serviceText = FormatTopFinancialGroups(financial.Rows.GroupBy(x => x.ServiceType), "dịch vụ");

        return $"Tổng chi phí trong kỳ 30 ngày: {FormatVnd(financial.TotalCost)}. " +
               $"Theo chủ hàng/bên phát sinh: {ownerText}. Theo kho: {warehouseText}. Theo dịch vụ: {serviceText}. " +
               $"Phạm vi: {ScopeText(warehouseId)}. Nguồn đối chiếu: Dòng phí kho nhiều chủ hàng / tác vụ lao động ({financial.Rows.Count} dòng nguồn).";
    }

    private static string BuildSlaAnswer(SemanticBiDashboardViewModel semantic, PredictiveAlertsViewModel alerts, int? warehouseId)
    {
        var metric = semantic.Snapshots.FirstOrDefault(x => x.MetricDefinition.MetricCode == "sla.overdue_order");
        var count = metric?.MetricValue ?? alerts.Alerts.Count(x => x.AlertType == PredictiveAlertTypeEnum.SlaDelay);
        var topSlaAlerts = alerts.Alerts
            .Where(x => x.AlertType == PredictiveAlertTypeEnum.SlaDelay)
            .OrderByDescending(x => x.RiskScore)
            .Take(3)
            .Select(x => $"{x.Title} (điểm ưu tiên {x.RiskScore:N1}/100)")
            .ToList();
        var topText = topSlaAlerts.Count == 0 ? "chưa có phiếu trễ SLA mở" : string.Join("; ", topSlaAlerts);

        return $"Phiếu trễ SLA hiện tại: {FormatQuantity(count)} phiếu. " +
               $"Ưu tiên xử lý: {topText}. Phạm vi: {ScopeText(warehouseId)}. " +
               "Nguồn đối chiếu: Phiếu kho / cảnh báo theo quy tắc / báo cáo SLA.";
    }

    private static string BuildLaborAnswer(SemanticBiDashboardViewModel semantic, int? warehouseId)
    {
        var metric = semantic.Snapshots.FirstOrDefault(x => x.MetricDefinition.MetricCode == "labor.productivity");
        var value = metric?.MetricValue ?? 0m;
        var sourceCount = metric?.SourceCount ?? 0;

        return $"Năng suất lao động trung bình: {FormatQuantity(value)}%. " +
               $"Số dòng hoạt động dùng để tính: {sourceCount}. Phạm vi: {ScopeText(warehouseId)}. " +
               "Nguồn đối chiếu: Hoạt động lao động / báo cáo năng suất.";
    }

    private static string BuildSummaryAnswer(string prompt, SemanticBiDashboardViewModel semantic, PredictiveAlertsViewModel alerts)
    {
        var topMetrics = semantic.Snapshots
            .Take(5)
            .Select(x => $"{x.MetricDefinition.MetricName}: {FormatMetricValue(x.MetricValue, x.MetricDefinition.Unit)}");
        var topAlert = alerts.Alerts.FirstOrDefault();
        var alertText = topAlert == null
            ? "Chưa có cảnh báo dự báo mở trong phạm vi hiện tại."
            : $"Cảnh báo ưu tiên: {topAlert.Title} ({topAlert.RiskScore:N1}%).";
        return $"Tóm tắt theo quyền hiện tại: {string.Join("; ", topMetrics)}. {alertText} Câu hỏi của bạn: {prompt.Trim()}";
    }

    private async Task<string> BuildInventoryAnswerAsync(
        string prompt,
        SemanticBiDashboardViewModel semantic,
        int? warehouseId,
        IReadOnlyCollection<int> allowedOwnerPartnerIds,
        CancellationToken ct)
    {
        var ownerScope = NormalizeOwnerScope(allowedOwnerPartnerIds);
        var hasOwnerScope = ownerScope.Count > 0;
        var rows = await _db.ItemLocations
            .AsNoTracking()
            .Include(x => x.Item).ThenInclude(x => x!.BaseUom)
            .Include(x => x.Location).ThenInclude(x => x!.Zone).ThenInclude(x => x.Warehouse)
            .Where(x => x.Item != null && x.Location != null && x.Location.Zone != null
                && (!warehouseId.HasValue || x.Location.Zone.WarehouseId == warehouseId.Value)
                && (!hasOwnerScope || (x.OwnerPartnerId.HasValue && ownerScope.Contains(x.OwnerPartnerId.Value))))
            .ToListAsync(ct);

        var scopeText = ResolveInventoryScopeText(rows, warehouseId);
        var itemSummaries = rows
            .Where(x => x.Item != null)
            .GroupBy(x => x.ItemId)
            .Select(g =>
            {
                var item = g.First().Item!;
                return new
                {
                    Item = item,
                    Unit = string.IsNullOrWhiteSpace(item.BaseUom?.UomCode) ? "đơn vị" : item.BaseUom!.UomCode,
                    Total = g.Sum(x => x.Quantity),
                    Reserved = g.Sum(x => Math.Max(0m, x.ReservedQty)),
                    Available = g.Sum(x => Math.Max(0m, x.Quantity - x.ReservedQty)),
                    Locations = g.Where(x => x.Location != null && x.Quantity != 0)
                        .OrderByDescending(x => x.Quantity)
                        .Take(4)
                        .Select(x => $"{x.Location!.LocationCode}: {FormatQuantity(x.Quantity)}")
                        .ToList()
                };
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        var normalizedPrompt = NormalizePrompt(prompt);
        var matchedItem = itemSummaries.FirstOrDefault(x =>
            (!string.IsNullOrWhiteSpace(x.Item.ItemCode) && normalizedPrompt.Contains(NormalizePrompt(x.Item.ItemCode), StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(x.Item.ItemName) && NormalizePrompt(x.Item.ItemName).Length >= 4 && normalizedPrompt.Contains(NormalizePrompt(x.Item.ItemName), StringComparison.Ordinal)));

        if (matchedItem != null)
        {
            var locationText = matchedItem.Locations.Count == 0 ? "chưa có phân bổ vị trí có tồn" : string.Join("; ", matchedItem.Locations);
            return $"Vật tư [{matchedItem.Item.ItemCode}] {matchedItem.Item.ItemName} còn {FormatQuantity(matchedItem.Total)} {matchedItem.Unit}. " +
                   $"Khả dụng: {FormatQuantity(matchedItem.Available)} {matchedItem.Unit}; Đang giữ chỗ: {FormatQuantity(matchedItem.Reserved)} {matchedItem.Unit}. " +
                   $"Phân bổ nhanh: {locationText}. Phạm vi: {scopeText}. Nguồn đối chiếu: Tồn kho theo vị trí / báo cáo tồn kho.";
        }

        var inventoryMetric = semantic.Snapshots.FirstOrDefault(x => x.MetricDefinition.MetricCode == "inventory.total_stock");
        var totalStock = inventoryMetric?.MetricValue ?? rows.Sum(x => x.Quantity);
        var itemCount = itemSummaries.Count(x => x.Total != 0);
        var locationCount = rows.Where(x => x.Quantity != 0).Select(x => x.LocationId).Distinct().Count();
        var topItems = itemSummaries.Take(3).Select(x => $"[{x.Item.ItemCode}] {x.Item.ItemName}: {FormatQuantity(x.Total)} {x.Unit}").ToList();
        var topText = topItems.Count == 0 ? "chưa có vật tư phát sinh tồn" : string.Join("; ", topItems);
        var sourceRows = inventoryMetric?.SourceCount ?? rows.Count;

        return $"Tổng tồn kho hiện tại: {FormatQuantity(totalStock)} đơn vị tồn kho. " +
               $"Phạm vi: {scopeText}. Chi tiết nhanh: {itemCount} vật tư có tồn tại {locationCount} vị trí; top tồn cao: {topText}. " +
               $"Nguồn đối chiếu: Tồn kho theo vị trí / báo cáo tồn kho ({sourceRows} dòng tồn kho).";
    }

    private static string FormatTopFinancialGroups(IEnumerable<IGrouping<string, FinancialCostRow>> groups, string emptyLabel)
    {
        var rows = groups
            .Select(g => new
            {
                Label = string.IsNullOrWhiteSpace(g.Key) ? "Chưa phân loại" : g.Key,
                Amount = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.Amount)
            .Take(3)
            .ToList();

        return rows.Count == 0
            ? $"chưa có dòng chi phí theo {emptyLabel}"
            : string.Join("; ", rows.Select(x => $"{x.Label}: {FormatVnd(x.Amount)}"));
    }

    private static string ScopeText(int? warehouseId)
        => warehouseId.HasValue ? $"Kho {warehouseId.Value} theo quyền hiện tại" : "Toàn hệ thống theo quyền hiện tại";

    private static string AlertTypeLabel(PredictiveAlertTypeEnum type) => type switch
    {
        PredictiveAlertTypeEnum.CapacityOverload => "Quá tải vị trí",
        PredictiveAlertTypeEnum.StockoutRisk => "Nguy cơ thiếu hàng",
        PredictiveAlertTypeEnum.SlaDelay => "Trễ SLA",
        PredictiveAlertTypeEnum.ExpiryRisk => "Sắp hết hạn",
        _ => "Cảnh báo"
    };

    private static string FormatVnd(decimal value)
        => $"{value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} VND";

    private static SemanticMetricSnapshot? SelectCitationMetric(AssistantReadIntent intent, SemanticBiDashboardViewModel semantic)
    {
        var metricCode = intent switch
        {
            AssistantReadIntent.Inventory => "inventory.total_stock",
            AssistantReadIntent.Sla => "sla.overdue_order",
            AssistantReadIntent.Cost => "billing.total_cost",
            AssistantReadIntent.Labor => "labor.productivity",
            AssistantReadIntent.Summary => null,
            AssistantReadIntent.Alert => null,
            _ => null
        };
        return metricCode == null
            ? intent == AssistantReadIntent.Summary
                ? semantic.Snapshots.OrderByDescending(x => x.CalculatedAt).FirstOrDefault()
                : null
            : semantic.Snapshots.FirstOrDefault(x => x.MetricDefinition.MetricCode == metricCode)
                ?? semantic.Snapshots.OrderByDescending(x => x.CalculatedAt).FirstOrDefault();
    }

    private static EnterprisePredictiveAlert? SelectCitationAlert(AssistantReadIntent intent, PredictiveAlertsViewModel alerts)
    {
        if (intent == AssistantReadIntent.Inventory)
            return alerts.Alerts.FirstOrDefault(x => x.AlertType is PredictiveAlertTypeEnum.StockoutRisk or PredictiveAlertTypeEnum.ExpiryRisk);
        if (intent == AssistantReadIntent.Sla)
            return alerts.Alerts.FirstOrDefault(x => x.AlertType == PredictiveAlertTypeEnum.SlaDelay);
        if (intent == AssistantReadIntent.Alert)
            return alerts.Alerts.FirstOrDefault();
        return null;
    }

    private static AssistantReadIntent ResolveAssistantIntent(string prompt)
    {
        var text = NormalizePrompt(prompt);
        if (text.Contains("tom tat", StringComparison.Ordinal) || text.Contains("tong hop", StringComparison.Ordinal))
            return AssistantReadIntent.Summary;
        if (ContainsAny(text, "canh bao", "bao nhieu canh bao", "tong canh bao", "bat thuong", "rui ro", "alert"))
            return AssistantReadIntent.Alert;
        if (ContainsAny(text, "ton kho", "hang ton", "con bao nhieu", "so luong ton", "ton con", "stock", "inventory", "itemlocation"))
            return AssistantReadIntent.Inventory;
        if (ContainsAny(text, "sla", "tre han", "qua han", "don tre", "phieu tre"))
            return AssistantReadIntent.Sla;
        if (ContainsAny(text, "chi phí", "chủ hàng", "các bên", "3pl", "billing", "cost", "giá trị"))
            return AssistantReadIntent.Cost;
        if (ContainsAny(text, "nang suat", "lao dong", "labor", "hieu suat nhan vien"))
            return AssistantReadIntent.Labor;
        return AssistantReadIntent.Summary;
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(term => text.Contains(NormalizePrompt(term), StringComparison.Ordinal));

    private static string ResolveInventoryScopeText(IEnumerable<ItemLocation> rows, int? warehouseId)
    {
        if (warehouseId.HasValue)
        {
            var warehouseName = rows.Select(x => x.Location?.Zone?.Warehouse?.WarehouseName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            return string.IsNullOrWhiteSpace(warehouseName) ? $"Kho {warehouseId.Value} theo quyền hiện tại" : warehouseName!;
        }

        var warehouses = rows
            .Select(x => x.Location?.Zone?.Warehouse?.WarehouseName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        return warehouses.Count == 0 ? "Toàn hệ thống theo quyền hiện tại" : $"Toàn hệ thống theo quyền hiện tại ({string.Join(", ", warehouses)})";
    }

    private static string FormatMetricValue(decimal value, string? unit)
    {
        if (string.Equals(unit, "VND", StringComparison.OrdinalIgnoreCase))
            return $"{value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} VND";
        return $"{FormatQuantity(value)} {DisplayMetricUnit(unit)}";
    }

    private static string DisplayMetricUnit(string? unit)
        => string.IsNullOrWhiteSpace(unit) || string.Equals(unit, "qty", StringComparison.OrdinalIgnoreCase)
            ? "đơn vị"
            : unit;

    private static string FormatQuantity(decimal value)
    {
        var culture = CultureInfo.GetCultureInfo("vi-VN");
        var rounded = decimal.Round(value, 4);
        return rounded == decimal.Truncate(rounded)
            ? rounded.ToString("N0", culture)
            : rounded.ToString("N2", culture);
    }

    private static string NormalizePrompt(string value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(ch == 'đ' ? 'd' : ch == 'Đ' ? 'D' : ch);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private enum AssistantReadIntent
    {
        Summary,
        Inventory,
        Sla,
        Cost,
        Labor,
        Alert
    }

    private static string ScopeKey(int? warehouseId, int? ownerPartnerId)
        => $"WH:{warehouseId?.ToString() ?? "ALL"}|OWNER:{ownerPartnerId?.ToString() ?? "ALL"}";

    private static string ScopeKey(int? warehouseId, IReadOnlyCollection<int> allowedOwnerPartnerIds)
    {
        var ownerScope = NormalizeOwnerScope(allowedOwnerPartnerIds);
        var ownerPart = ownerScope.Count == 0 ? "ALL" : string.Join(',', ownerScope);
        return $"WH:{warehouseId?.ToString() ?? "ALL"}|OWNER:{ownerPart}";
    }

    private static List<int> NormalizeOwnerScope(IEnumerable<int>? allowedOwnerPartnerIds)
        => (allowedOwnerPartnerIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

    private static int? ScopedWarehouseId(ClaimsPrincipal user)
    {
        if (user.IsInRole("Admin")) return null;
        var value = user.FindFirst("WarehouseId")?.Value;
        return int.TryParse(value, out var id) ? id : null;
    }

    private static bool CanSeeFinancial(ClaimsPrincipal user)
        => user.IsInRole(WmsRoles.Admin)
            || string.Equals(user.FindFirst(ClaimTypes.Role)?.Value, WmsRoles.Admin, StringComparison.OrdinalIgnoreCase)
            || user.Claims.Any(c => c.Type == PermissionClaimTypes.Permission && c.Value == WmsPermissions.ReportViewFinancial);

    private static string CurrentRole(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.Role)?.Value ?? (user.IsInRole("Admin") ? "Admin" : "Viewer");

    private static string ScopeSummary(ClaimsPrincipal user)
    {
        var wh = ScopedWarehouseId(user);
        return wh.HasValue ? $"Kho {wh.Value}; vai trò {CurrentRole(user)}" : $"Toàn hệ thống; vai trò {CurrentRole(user)}";
    }

    private Task<List<Warehouse>> ActiveWarehousesAsync(CancellationToken ct)
        => ActiveWarehousesAsync(null, ct);

    private Task<List<Warehouse>> ActiveWarehousesAsync(int? warehouseId, CancellationToken ct)
        => _db.Warehouses.AsNoTracking()
            .Where(x => x.IsActive && (!warehouseId.HasValue || x.WarehouseId == warehouseId.Value))
            .OrderBy(x => x.WarehouseCode)
            .ToListAsync(ct);
}

public sealed class RoleWorkspaceService : IRoleWorkspaceService
{
    public RoleWorkspaceViewModel Build(ClaimsPrincipal user)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? WmsRoles.Viewer;
        if (WmsRoles.IsAdmin(role))
            return Workspace(WmsRoles.Admin, WmsRoles.Label(role), "Bàn làm việc quản trị", "Theo dõi toàn hệ thống, cấu hình, bảo mật, báo cáo dữ liệu và giám sát hệ thống.", new[]
            {
                Action("Người dùng", "/Users", "fa-users-cog", "primary"),
                Action("Báo cáo dữ liệu", "/Reports/SemanticBi", "fa-chart-line", "secondary"),
                Action("Giám sát hệ thống", "/System/SreDashboard", "fa-heart-pulse", "secondary"),
                Action("Quy tắc vận hành", "/Operations/WorkflowProfiles", "fa-sliders", "secondary")
            }, "admin", "manager", "staff", "viewer", "financial", "sre");

        if (WmsRoles.IsManager(role))
            return Workspace(WmsRoles.Manager, WmsRoles.Label(role), "Bàn làm việc quản lý kho", "Điều phối ca, xử lý ngoại lệ, duyệt quy trình và theo dõi chỉ số theo kho.", new[]
            {
                Action("Bảng điều phối", "/Operations/DockBoard", "fa-display", "primary"),
                Action("Năng suất", "/Operations/LaborProductivity", "fa-users-gear", "secondary"),
                Action("Cảnh báo dự báo", "/Reports/PredictiveAlerts", "fa-triangle-exclamation", "secondary"),
                Action("Quy tắc vận hành", "/Operations/WorkflowProfiles", "fa-sliders", "secondary")
            }, "manager", "staff", "viewer", "workflow");

        if (string.Equals(role, WmsRoles.InboundStaff, StringComparison.OrdinalIgnoreCase))
            return Workspace(WmsRoles.InboundStaff, WmsRoles.Label(role), "Bàn làm việc nhập kho", "Đi thẳng vào nhận hàng, quét nhận, kiểm tra chất lượng và lịch sử nhập.", new[]
            {
                Action("Quét nhận", "/Operations/RfReceiving", "fa-mobile-screen", "primary"),
                Action("Tiếp nhận hàng", "/Operations/Receiving", "fa-truck-loading", "secondary"),
                Action("Kiểm tra chất lượng", "/Operations/QualityInspection", "fa-microscope", "secondary"),
                Action("Lịch sử nhập", "/Reports/StockMovement?nav=inbound", "fa-clock-rotate-left", "secondary")
            }, "inbound", "mobile");

        if (string.Equals(role, WmsRoles.OutboundStaff, StringComparison.OrdinalIgnoreCase))
            return Workspace(WmsRoles.OutboundStaff, WmsRoles.Label(role), "Bàn làm việc xuất kho", "Tập trung lấy hàng, quét lấy hàng, đóng gói và bàn giao đơn xuất.", new[]
            {
                Action("Quét lấy", "/Operations/RfPicking", "fa-hand-pointer", "primary"),
                Action("Nhiệm vụ lấy hàng", "/Operations/PickTasks", "fa-list-check", "secondary"),
                Action("Đóng gói", "/Operations/Shipping", "fa-box-open", "secondary"),
                Action("Nhiệm vụ tiếp theo", "/Operations/NextTask", "fa-forward", "secondary")
            }, "outbound", "mobile");

        if (string.Equals(role, WmsRoles.InventoryStaff, StringComparison.OrdinalIgnoreCase))
            return Workspace(WmsRoles.InventoryStaff, WmsRoles.Label(role), "Bàn làm việc tồn kho/kiểm kê", "Theo dõi tồn, kiểm kê, di chuyển, mã kiện và số sê-ri theo phạm vi kho.", new[]
            {
                Action("Xem tồn", "/Reports/Inventory?nav=inventory", "fa-boxes-stacked", "primary"),
                Action("Kiểm kê", "/Reports/StockCount", "fa-clipboard-check", "secondary"),
                Action("Quét di chuyển", "/Operations/RfMovement", "fa-dolly", "secondary"),
                Action("Tra cứu mã kiện", "/Operations/PackageLookup", "fa-tags", "secondary")
            }, "inventory", "mobile");

        if (string.Equals(role, WmsRoles.TransportStaff, StringComparison.OrdinalIgnoreCase))
            return Workspace(WmsRoles.TransportStaff, WmsRoles.Label(role), "Bàn làm việc vận chuyển", "Điều phối giao hàng, chuyến xe, nhãn/chứng từ và đối soát giao hàng.", new[]
            {
                Action("Điều phối", "/Operations/ShippingDispatch", "fa-route", "primary"),
                Action("Chuyến xe", "/Operations/ShipmentLoads", "fa-truck-ramp-box", "secondary"),
                Action("Đối soát giao hàng", "/Operations/DeliveryReconciliation", "fa-clipboard-check", "secondary"),
                Action("Nhãn & chứng từ", "/Labels", "fa-tags", "secondary")
            }, "transport", "mobile");

        if (WmsRoles.IsLegacyStaff(role))
            return Workspace(WmsRoles.Staff, WmsRoles.Label(role), "Bàn làm việc nhân viên kho tổng hợp", "Đi thẳng vào các tác vụ quét, nhận, lấy, di chuyển và đóng gói.", new[]
            {
                Action("Nhiệm vụ tiếp theo", "/Operations/NextTask", "fa-forward", "primary"),
                Action("Quét nhận", "/Operations/RfReceiving", "fa-mobile-screen", "secondary"),
                Action("Quét lấy", "/Operations/RfPicking", "fa-hand-pointer", "secondary"),
                Action("Di chuyển", "/Operations/RfMovement", "fa-dolly", "secondary")
            }, "staff", "mobile", "viewer");

        if (WmsRoles.IsReportingSpecialist(role))
            return Workspace(WmsRoles.ReportViewer, WmsRoles.Label(role), "Bàn làm việc báo cáo", "Theo dõi chỉ số, báo cáo tồn kho, vận hành và cảnh báo theo phạm vi được phân quyền.", new[]
            {
                Action("Tổng quan kho", "/Reports/WarehouseOverview", "fa-table-columns", "primary"),
                Action("Chỉ số vận hành", "/Reports/OpsKpi", "fa-gauge-high", "secondary"),
                Action("Thống kê nhập/xuất", "/Reports/InventoryInOutSummary", "fa-chart-column", "secondary"),
                Action("Báo cáo tồn kho", "/Reports/Inventory?nav=report", "fa-boxes-stacked", "secondary")
            }, "reports", "viewer");

        return Workspace(WmsRoles.Viewer, WmsRoles.Label(role), "Bàn xem dữ liệu", "Chỉ xem báo cáo và hướng dẫn được phân quyền theo phạm vi.", new[]
        {
            Action("Tồn kho", "/Reports/Inventory?nav=report", "fa-boxes-stacked", "primary"),
            Action("Hướng dẫn", "/Help", "fa-book-open", "secondary")
        }, "viewer");
    }

    private static RoleWorkspaceViewModel Workspace(string role, string roleLabel, string title, string description, IEnumerable<RoleWorkspaceAction> actions, params string[] sections)
        => new()
        {
            RoleKey = role,
            RoleLabel = roleLabel,
            Title = title,
            Description = description,
            QuickActions = actions.ToList(),
            VisibleSections = sections.ToList()
        };

    private static RoleWorkspaceAction Action(string label, string url, string icon, string variant)
        => new() { Label = label, Url = url, Icon = icon, Variant = variant };
}

public sealed class ProductionSreService : IProductionSreService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration? _config;

    public ProductionSreService(AppDbContext db, IConfiguration? config = null)
    {
        _db = db;
        _config = config;
    }

    private static DateTime Now => VietnamTime.Now;

    public async Task RecordRequestAsync(RequestTelemetryLog row, CancellationToken ct = default)
    {
        if (IsTelemetryNoise(row.Path))
            return;

        if (row.CreatedAt == default)
            row.CreatedAt = Now;

        _db.RequestTelemetryLogs.Add(row);
        await PruneExpiredRequestTelemetryAsync(row.CreatedAt, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task PruneExpiredRequestTelemetryAsync(DateTime now, CancellationToken ct)
    {
        var retentionDays = Math.Clamp(_config?.GetValue("ProductionSre:TelemetryRetentionDays", 30) ?? 30, 1, 3650);
        var cutoff = now.AddDays(-retentionDays);
        var expiredRows = await _db.RequestTelemetryLogs
            .Where(x => x.CreatedAt < cutoff)
            .OrderBy(x => x.CreatedAt)
            .Take(1000)
            .ToListAsync(ct);

        if (expiredRows.Count > 0)
            _db.RequestTelemetryLogs.RemoveRange(expiredRows);
    }

    private static bool IsTelemetryNoise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = path.Split('?', 2)[0];
        if (normalized.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            return true;

        var extension = Path.GetExtension(normalized);
        return extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".map", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SreDashboardViewModel> BuildDashboardAsync(int periodMinutes, CancellationToken ct = default)
    {
        var snapshot = await CaptureSnapshotAsync(periodMinutes, ct);
        return new SreDashboardViewModel
        {
            PeriodMinutes = periodMinutes,
            Snapshot = snapshot,
            RecentSnapshots = await _db.SreMetricSnapshots.AsNoTracking().OrderByDescending(x => x.SnapshotAt).Take(30).ToListAsync(ct),
            RecentRequests = await _db.RequestTelemetryLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(80).ToListAsync(ct),
            OutboxRows = await _db.IntegrationOutbox.AsNoTracking().Where(x => x.Status == OutboxStatusEnum.Pending || x.Status == OutboxStatusEnum.Failed || x.Status == OutboxStatusEnum.DeadLetter).OrderByDescending(x => x.CreatedAt).Take(80).ToListAsync(ct),
            WebhookFailures = await _db.WebhookDeliveries.AsNoTracking().Include(x => x.Subscription).Where(x => x.Status == WebhookDeliveryStatusEnum.Failed || x.Status == WebhookDeliveryStatusEnum.DeadLetter).OrderByDescending(x => x.CreatedAt).Take(80).ToListAsync(ct)
        };
    }

    public async Task<SreMetricSnapshot> CaptureSnapshotAsync(int periodMinutes, CancellationToken ct = default)
    {
        periodMinutes = Math.Clamp(periodMinutes, 1, 1440);
        var from = Now.AddMinutes(-periodMinutes);
        var requests = await _db.RequestTelemetryLogs.AsNoTracking().Where(x => x.CreatedAt >= from).ToListAsync(ct);
        var durations = requests.Select(x => x.DurationMs).OrderBy(x => x).ToList();
        var errors = requests.Count(x => x.IsError || x.StatusCode >= 500);

        var queueDepth = await _db.IntegrationOutbox.CountAsync(x => x.Status == OutboxStatusEnum.Pending || x.Status == OutboxStatusEnum.Processing, ct);
        var deadLetters = await _db.IntegrationOutbox.CountAsync(x => x.Status == OutboxStatusEnum.DeadLetter, ct)
            + await _db.InventorySnapshotOutbox.CountAsync(
                x => x.Status == InventorySnapshotOutboxStatusEnum.Failed
                    || x.Status == InventorySnapshotOutboxStatusEnum.DeadLetter,
                ct);
        var scanRetry = await _db.PickTaskScanLogs.CountAsync(x => x.ScannedAt >= from && x.Notes != null && x.Notes.Contains("retry"), ct);
        var carrierFailures = await _db.CarrierShipmentEvents.CountAsync(x => x.EventAt >= from && x.EventType == CarrierShipmentEventTypeEnum.Failed, ct);
        var webhookFailures = await _db.WebhookDeliveries.CountAsync(x => x.CreatedAt >= from && (x.Status == WebhookDeliveryStatusEnum.Failed || x.Status == WebhookDeliveryStatusEnum.DeadLetter), ct);

        var snapshot = new SreMetricSnapshot
        {
            SnapshotAt = Now,
            PeriodMinutes = periodMinutes,
            AverageLatencyMs = durations.Count == 0 ? 0 : Math.Round((decimal)durations.Average(), 2),
            P95LatencyMs = Percentile(durations, 0.95m),
            RequestCount = requests.Count,
            ErrorCount = errors,
            ErrorRatePercent = requests.Count == 0 ? 0 : Math.Round(errors * 100m / requests.Count, 4),
            QueueDepth = queueDepth,
            DeadLetterCount = deadLetters,
            ScanRetryCount = scanRetry,
            CarrierFailureCount = carrierFailures,
            WebhookFailureCount = webhookFailures,
            Notes = $"Correlation telemetry window {periodMinutes} minutes"
        };
        _db.SreMetricSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
        return snapshot;
    }

    private static decimal Percentile(List<long> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0) return 0;
        var index = (int)Math.Ceiling(sortedValues.Count * percentile) - 1;
        index = Math.Clamp(index, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public const string HeaderName = "X-Correlation-ID";

    public static IApplicationBuilder UseWmsCorrelationTelemetry(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("WMS.Correlation");
            var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrWhiteSpace(header)
                ? header.ToString()
                : Activity.Current?.Id ?? context.TraceIdentifier;

            context.TraceIdentifier = correlationId;
            context.Response.Headers[HeaderName] = correlationId;
            Activity.Current?.SetTag("wms.correlation_id", correlationId);

            var sw = Stopwatch.StartNew();
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                try
                {
                    await next();
                }
                finally
                {
                    sw.Stop();
                    if (ShouldRecord(context, config, sw.ElapsedMilliseconds))
                    {
                        try
                        {
                            await using var scope = context.RequestServices.CreateAsyncScope();
                            var sre = scope.ServiceProvider.GetRequiredService<IProductionSreService>();
                            await sre.RecordRequestAsync(new RequestTelemetryLog
                            {
                                CorrelationId = correlationId,
                                Method = context.Request.Method,
                                Path = context.Request.Path.Value ?? "",
                                StatusCode = context.Response.StatusCode,
                                DurationMs = sw.ElapsedMilliseconds,
                                UserName = context.User.Identity?.Name,
                                WarehouseId = int.TryParse(context.User.FindFirst("WarehouseId")?.Value, out var wh) ? wh : null,
                                IsError = context.Response.StatusCode >= 500,
                                CreatedAt = VietnamTime.Now
                            }, CancellationToken.None);
                        }
                        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                        {
                            logger.LogDebug("Request telemetry skipped because the request was aborted for {CorrelationId}", correlationId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Unable to record request telemetry for {CorrelationId}", correlationId);
                        }
                    }
                }
            }
        });

    private static bool ShouldRecord(HttpContext context, IConfiguration config, long durationMs)
    {
        var path = context.Request.Path.Value ?? "";
        return ShouldRecordForTelemetry(context.Response.StatusCode, durationMs, path, config);
    }

    public static bool ShouldRecordForTelemetry(int statusCode, long durationMs, string? path, IConfiguration config)
    {
        if (!config.GetValue("ProductionSre:TelemetryPersistenceEnabled", true))
            return false;

        path ??= "";
        if (path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            return false;

        if (statusCode >= 400)
            return true;

        var slowThresholdMs = Math.Clamp(config.GetValue("ProductionSre:LatencyWarningMs", 1500), 1, 600000);
        if (durationMs >= slowThresholdMs)
            return true;

        var sampling = Math.Clamp(config.GetValue("ProductionSre:TelemetrySamplingPercent", 100), 0, 100);
        if (sampling >= 100) return true;
        if (sampling <= 0) return false;
        return Random.Shared.Next(0, 100) < sampling;
    }
}
