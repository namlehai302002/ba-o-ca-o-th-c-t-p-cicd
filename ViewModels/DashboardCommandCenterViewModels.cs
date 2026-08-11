namespace WMS.ViewModels;

public sealed class DashboardCommandCenterRequest
{
    public string Role { get; init; } = "";
    public int? WarehouseId { get; init; }
    public IReadOnlyCollection<int> OwnerPartnerIds { get; init; } = Array.Empty<int>();
    public DateTime Now { get; init; }
    public string? WorkState { get; init; }
    public string? Severity { get; init; }
    public string? Assignee { get; init; }
    public int WorkItemLimit { get; init; } = 16;
}

public sealed class DashboardCommandCenterViewModel
{
    public DateTime AsOfAt { get; set; }
    public DateTime BusinessDate { get; set; }
    public string TimeZoneLabel { get; set; } = "UTC+07:00";
    public string ShiftLabel { get; set; } = "Theo ngày vận hành";
    public string WarehouseScopeLabel { get; set; } = "Tất cả kho được phân quyền";
    public string OwnerScopeLabel { get; set; } = "Không giới hạn theo chủ hàng";
    public int? SelectedWarehouseId { get; set; }
    public string? SelectedWorkState { get; set; }
    public string? SelectedSeverity { get; set; }
    public string? SelectedAssignee { get; set; }
    public bool IsPartial { get; set; }
    public List<string> DataWarnings { get; set; } = new();
    public List<DashboardScopeOption> WarehouseOptions { get; set; } = new();
    public List<DashboardProcessSummary> ProcessSummaries { get; set; } = new();
    public List<DashboardWorkItemViewModel> WorkItems { get; set; } = new();
    public int TotalWorkItems { get; set; }
    public int FilteredWorkItems { get; set; }
    public int NotStartedCount { get; set; }
    public int InProgressCount { get; set; }
    public int WaitingCount { get; set; }
    public int BlockedCount { get; set; }
    public int OverdueCount { get; set; }
    public int CompletedTodayCount { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public int HiddenByLimitCount { get; set; }
}

public sealed class DashboardScopeOption
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
}

public sealed class DashboardProcessSummary
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "fa-list-check";
    public int DueToday { get; set; }
    public int CompletedToday { get; set; }
    public int Open { get; set; }
    public int Overdue { get; set; }
    public string Unit { get; set; } = "việc";
    public string DrillDownUrl { get; set; } = "";
    public decimal CompletionRate => DueToday > 0
        ? Math.Min(100m, Math.Round(CompletedToday * 100m / DueToday, 1))
        : 0m;
}

public sealed class DashboardWorkItemViewModel
{
    public string Key { get; set; } = "";
    public string KindKey { get; set; } = "";
    public string KindLabel { get; set; } = "";
    public string ReferenceCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string WarehouseLabel { get; set; } = "";
    public string SeverityKey { get; set; } = "medium";
    public string SeverityLabel { get; set; } = "Trung bình";
    public int SeverityRank { get; set; } = 2;
    public string StateKey { get; set; } = "not-started";
    public string StateLabel { get; set; } = "Chưa bắt đầu";
    public decimal ProgressDone { get; set; }
    public decimal ProgressTotal { get; set; }
    public string ProgressUnit { get; set; } = "việc";
    public DateTime? Deadline { get; set; }
    public DateTime WaitingSince { get; set; }
    public string Assignee { get; set; } = "Chưa phân công";
    public string? BlockerReason { get; set; }
    public string ActionUrl { get; set; } = "";
    public string ActionLabel { get; set; } = "Xem chi tiết";
    public bool CanAct { get; set; }
    public decimal ProgressPercent => ProgressTotal > 0
        ? Math.Clamp(Math.Round(ProgressDone * 100m / ProgressTotal, 1), 0m, 100m)
        : 0m;
}
