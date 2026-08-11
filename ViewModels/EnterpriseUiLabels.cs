using WMS.Models;

namespace WMS.ViewModels;

public static class EnterpriseUiLabels
{
    public static string MheSystemType(MheSystemTypeEnum type) => type switch
    {
        MheSystemTypeEnum.Wcs => "WCS - hệ điều khiển kho",
        MheSystemTypeEnum.Conveyor => "Băng chuyền",
        MheSystemTypeEnum.Sorter => "Máy chia chọn",
        MheSystemTypeEnum.Robot => "Robot kho",
        MheSystemTypeEnum.Amr => "AMR - xe tự hành",
        _ => "Khác"
    };

    public static string MheCommandStatus(MheCommandStatusEnum status) => status switch
    {
        MheCommandStatusEnum.Pending => "Chờ xử lý",
        MheCommandStatusEnum.Queued => "Đã đưa vào hàng đợi",
        MheCommandStatusEnum.Sent => "Đã gửi",
        MheCommandStatusEnum.Acknowledged => "Thiết bị đã xác nhận",
        MheCommandStatusEnum.InProgress => "Đang thực hiện",
        MheCommandStatusEnum.Completed => "Hoàn tất",
        MheCommandStatusEnum.Failed => "Lỗi",
        MheCommandStatusEnum.Cancelled => "Đã hủy",
        MheCommandStatusEnum.DeadLetter => "Chờ xử lý lỗi",
        _ => "Không xác định"
    };

    public static string MheCommandStatusClass(MheCommandStatusEnum status) => status switch
    {
        MheCommandStatusEnum.Completed or MheCommandStatusEnum.Acknowledged or MheCommandStatusEnum.Sent => "success",
        MheCommandStatusEnum.Failed or MheCommandStatusEnum.DeadLetter or MheCommandStatusEnum.Cancelled => "danger",
        MheCommandStatusEnum.Pending or MheCommandStatusEnum.Queued or MheCommandStatusEnum.InProgress => "warning",
        _ => "neutral"
    };

    public static string AutomationTelemetryType(AutomationTelemetryTypeEnum type) => type switch
    {
        AutomationTelemetryTypeEnum.Heartbeat => "Nhịp kết nối",
        AutomationTelemetryTypeEnum.Throughput => "Năng suất xử lý",
        AutomationTelemetryTypeEnum.Downtime => "Thời gian dừng",
        AutomationTelemetryTypeEnum.Error => "Lỗi thiết bị",
        _ => "Không xác định"
    };

    public static string AutomationScenario(WcsSimulatorScenarioEnum scenario) => scenario switch
    {
        WcsSimulatorScenarioEnum.AcceptAndComplete => "Nhận lệnh và hoàn tất",
        WcsSimulatorScenarioEnum.SorterReject => "Máy chia chọn từ chối kiện",
        WcsSimulatorScenarioEnum.RobotFail => "Robot xử lý thất bại",
        WcsSimulatorScenarioEnum.Timeout => "Quá thời gian phản hồi",
        _ => "Không xác định"
    };

    public static string AutomationScenarioText(string? scenario) => Normalize(scenario) switch
    {
        "acceptandcomplete" => "Nhận lệnh và hoàn tất",
        "sorterreject" => "Máy chia chọn từ chối kiện",
        "robotfail" => "Robot xử lý thất bại",
        "timeout" => "Quá thời gian phản hồi",
        _ => string.IsNullOrWhiteSpace(scenario) ? "Chưa rõ" : scenario.Trim()
    };

    public static string AutomationSourceType(string? sourceType) => Normalize(sourceType) switch
    {
        "wcssimulator" => "Mô phỏng WCS",
        _ => string.IsNullOrWhiteSpace(sourceType) ? "Chưa rõ" : sourceType.Trim()
    };

    public static string AutomationOverrideAction(AutomationOverrideActionEnum action) => action switch
    {
        AutomationOverrideActionEnum.Retry => "Gửi lại",
        AutomationOverrideActionEnum.Cancel => "Hủy lệnh",
        AutomationOverrideActionEnum.Complete => "Đánh dấu hoàn tất",
        AutomationOverrideActionEnum.DeadLetter => "Chuyển hàng lỗi",
        _ => "Không xác định"
    };

    public static string AdapterHealth(string? status) => Normalize(status) switch
    {
        "healthy" => "Ổn định",
        "down" => "Mất kết nối",
        "warning" => "Cảnh báo",
        "unknown" => "Chưa rõ",
        _ => string.IsNullOrWhiteSpace(status) ? "Chưa rõ" : status.Trim()
    };

    public static string TelemetryStatus(string? status) => Normalize(status) switch
    {
        "ok" => "Bình thường",
        "down" => "Mất kết nối",
        "completed" => "Hoàn tất",
        "failed" => "Lỗi",
        "queued" => "Đang chờ",
        "pending" => "Chờ xử lý",
        _ => string.IsNullOrWhiteSpace(status) ? "Chưa rõ" : status.Trim()
    };

    public static string OptimizationStatus(string? status) => Normalize(status) switch
    {
        "recommend" => "Đề xuất áp dụng",
        "review" => "Cần rà soát",
        "readytowave" => "Sẵn sàng lập đợt",
        "inventoryshort" => "Thiếu tồn khả dụng",
        _ => string.IsNullOrWhiteSpace(status) ? "Chưa rõ" : status.Trim()
    };

    public static string ConnectorType(EnterpriseConnectorTypeEnum type) => type switch
    {
        EnterpriseConnectorTypeEnum.Erp => "ERP - quản trị doanh nghiệp",
        EnterpriseConnectorTypeEnum.Tms => "TMS - vận tải",
        EnterpriseConnectorTypeEnum.Oms => "OMS - quản lý đơn hàng",
        _ => "Khác"
    };

    public static string ConnectorHealth(EnterpriseConnectorHealthEnum health) => health switch
    {
        EnterpriseConnectorHealthEnum.Unknown => "Chưa rõ",
        EnterpriseConnectorHealthEnum.Healthy => "Ổn định",
        EnterpriseConnectorHealthEnum.Warning => "Cảnh báo",
        EnterpriseConnectorHealthEnum.Down => "Mất kết nối",
        _ => "Không xác định"
    };

    public static string DeliveryStatus(WebhookDeliveryStatusEnum status) => status switch
    {
        WebhookDeliveryStatusEnum.Pending => "Chờ gửi",
        WebhookDeliveryStatusEnum.Sent => "Đã gửi",
        WebhookDeliveryStatusEnum.Failed => "Lỗi",
        WebhookDeliveryStatusEnum.DeadLetter => "Hàng lỗi",
        _ => "Không xác định"
    };

    public static string OutboxStatus(OutboxStatusEnum status) => status switch
    {
        OutboxStatusEnum.Pending => "Chờ xử lý",
        OutboxStatusEnum.Processing => "Đang xử lý",
        OutboxStatusEnum.Sent => "Đã gửi",
        OutboxStatusEnum.Failed => "Lỗi",
        OutboxStatusEnum.DeadLetter => "Hàng lỗi",
        _ => "Không xác định"
    };

    public static string IntegrationEvent(string? eventType) => Normalize(eventType) switch
    {
        "shipmentposted" => "Phiếu giao hàng đã ghi sổ",
        "asnreceived" => "ASN đã nhận",
        "asnstatuschanged" => "Trạng thái ASN thay đổi",
        "vouchercompleted" => "Phiếu hoàn tất",
        "stockalert" => "Cảnh báo tồn kho",
        "exceptionraised" => "Phát sinh ngoại lệ",
        "recallissued" => "Phát hành thu hồi",
        "wavecompleted" => "Đợt lấy hàng hoàn tất",
        "mhecommanddispatched" => "Lệnh thiết bị đã phát",
        "carriershipmentrequested" => "Yêu cầu vận đơn",
        "carriershipmentcancelled" => "Hủy vận đơn",
        "carriershipmentstatusrequested" => "Yêu cầu cập nhật vận đơn",
        "inventorychanged" => "Tồn kho thay đổi",
        "shipmentconfirmed" => "Giao hàng đã xác nhận",
        "threeplinvoiceissued" => "Hóa đơn kho nhiều chủ hàng đã phát hành",
        "webhookdelivery" => "Điểm nhận tự động",
        "edimessageprocessed" => "Thông điệp EDI đã xử lý",
        _ => string.IsNullOrWhiteSpace(eventType) ? "Chưa rõ" : eventType.Trim()
    };

    public static string TargetSystem(string? targetSystem) => Normalize(targetSystem) switch
    {
        "erp" => "ERP",
        "tms" => "TMS",
        "oms" => "OMS",
        "wcs" => "WCS",
        _ => string.IsNullOrWhiteSpace(targetSystem) ? "Chưa rõ" : targetSystem.Trim()
    };

    public static string DockAppointmentDirection(DockAppointmentDirectionEnum direction) => direction switch
    {
        DockAppointmentDirectionEnum.Inbound => "Nhập kho",
        DockAppointmentDirectionEnum.Outbound => "Xuất kho",
        DockAppointmentDirectionEnum.Transfer => "Luân chuyển",
        _ => "Không xác định"
    };

    public static string DockAppointmentStatus(DockAppointmentStatusEnum status) => status switch
    {
        DockAppointmentStatusEnum.Scheduled => "Đã lên lịch",
        DockAppointmentStatusEnum.CheckedIn => "Đã vào cổng",
        DockAppointmentStatusEnum.AtDock => "Đang tại cửa bến",
        DockAppointmentStatusEnum.Completed => "Hoàn tất",
        DockAppointmentStatusEnum.Cancelled => "Đã hủy",
        DockAppointmentStatusEnum.NoShow => "Không đến",
        _ => "Không xác định"
    };

    public static string YardEvidenceType(YardEvidenceTypeEnum type) => type switch
    {
        YardEvidenceTypeEnum.GateInPhoto => "Ảnh vào cổng",
        YardEvidenceTypeEnum.GateOutPhoto => "Ảnh ra cổng",
        YardEvidenceTypeEnum.SealPhoto => "Ảnh niêm phong",
        YardEvidenceTypeEnum.DriverDocument => "Giấy tờ tài xế",
        YardEvidenceTypeEnum.ContainerCondition => "Tình trạng công-ten-nơ",
        _ => "Khác"
    };

    public static string SemanticMetricCategory(SemanticMetricCategoryEnum category) => category switch
    {
        SemanticMetricCategoryEnum.Inventory => "Tồn kho",
        SemanticMetricCategoryEnum.Order => "Đơn hàng",
        SemanticMetricCategoryEnum.Labor => "Lao động",
        SemanticMetricCategoryEnum.Billing => "Chi phí",
        SemanticMetricCategoryEnum.Sla => "SLA vận hành",
        _ => "Khác"
    };

    public static string PredictiveAlertType(PredictiveAlertTypeEnum type) => type switch
    {
        PredictiveAlertTypeEnum.CapacityOverload => "Quá tải vị trí",
        PredictiveAlertTypeEnum.StockoutRisk => "Nguy cơ thiếu hàng",
        PredictiveAlertTypeEnum.SlaDelay => "Trễ SLA",
        PredictiveAlertTypeEnum.ExpiryRisk => "Sắp hết hạn",
        _ => "Cảnh báo"
    };

    public static string EnterpriseSeverity(EnterpriseSeverityEnum severity) => severity switch
    {
        EnterpriseSeverityEnum.Info => "Thông tin",
        EnterpriseSeverityEnum.Warning => "Cần theo dõi",
        EnterpriseSeverityEnum.Critical => "Khẩn cấp",
        _ => "Chưa phân loại"
    };

    public static string EnterpriseSeverityClass(EnterpriseSeverityEnum severity) => severity switch
    {
        EnterpriseSeverityEnum.Critical => "danger",
        EnterpriseSeverityEnum.Warning => "warning",
        EnterpriseSeverityEnum.Info => "info",
        _ => "neutral"
    };

    public static string AuditFindingType(AuditFindingTypeEnum type) => type switch
    {
        AuditFindingTypeEnum.SensitiveExport => "Xuất dữ liệu nhạy cảm",
        AuditFindingTypeEnum.OutOfHoursAccess => "Truy cập ngoài giờ",
        AuditFindingTypeEnum.ScopeDenied => "Bị chặn phạm vi",
        AuditFindingTypeEnum.AbnormalMutation => "Thao tác bất thường",
        _ => "Phát hiện nhật ký"
    };

    public static string AuditFindingTypeClass(AuditFindingTypeEnum type) => type switch
    {
        AuditFindingTypeEnum.SensitiveExport => "warning",
        AuditFindingTypeEnum.OutOfHoursAccess => "warning",
        AuditFindingTypeEnum.ScopeDenied => "danger",
        AuditFindingTypeEnum.AbnormalMutation => "danger",
        _ => "neutral"
    };

    public static string ThreePlChargeType(ThreePlChargeTypeEnum type) => type switch
    {
        ThreePlChargeTypeEnum.Storage => "Phí lưu kho",
        ThreePlChargeTypeEnum.InboundHandling => "Phí xử lý nhập kho",
        ThreePlChargeTypeEnum.OutboundHandling => "Phí xử lý xuất kho",
        ThreePlChargeTypeEnum.Vas => "Dịch vụ gia tăng",
        ThreePlChargeTypeEnum.Yard => "Phí bến bãi",
        ThreePlChargeTypeEnum.PackageHandling => "Phí xử lý kiện",
        ThreePlChargeTypeEnum.ManualAdjustment => "Điều chỉnh thủ công",
        _ => "Chi phí khác"
    };

    public static string MetricUnit(string? unit) => Normalize(unit) switch
    {
        "qty" => "đơn vị",
        "order" => "phiếu",
        "vnd" => "VND",
        "%" => "%",
        _ => string.IsNullOrWhiteSpace(unit) ? "đơn vị" : unit.Trim()
    };

    public static string MetricBusinessCode(string? metricCode) => Normalize(metricCode) switch
    {
        "inventory.totalstock" => "Tồn kho",
        "order.openoutbound" => "Phiếu xuất mở",
        "labor.productivity" => "Năng suất",
        "billing.totalcost" => "Chi phí",
        "sla.overdueorder" => "Trễ SLA",
        _ => string.IsNullOrWhiteSpace(metricCode) ? "Chưa rõ" : metricCode.Trim()
    };

    public static string DataSourceLabel(string? sourceLabel) => Normalize(sourceLabel) switch
    {
        "itemlocation" or "itemlocations" => "Tồn kho theo vị trí",
        "vouchers" or "voucher" => "Phiếu kho",
        "laboractivities" or "labortask" => "Hoạt động lao động",
        "threeplinvoicelines" or "3plinvoice" => "Dòng phí kho nhiều chủ hàng",
        "enterprisepredictivealert" or "enterprisepredictivealerts" => "Cảnh báo dự báo",
        "semanticmetricsnapshot" or "semanticmetricsnapshots" => "Bản ghi chỉ số",
        "loginauditlog" or "loginauditlogs" => "Nhật ký đăng nhập",
        "auditlog" or "auditlogs" => "Nhật ký hệ thống",
        "location" or "locations" => "Vị trí kho",
        _ => string.IsNullOrWhiteSpace(sourceLabel) ? "Chưa rõ" : sourceLabel.Trim()
    };

    public static string SemanticMetricFormula(string? formula) => Normalize(formula) switch
    {
        "sumitemlocationquantity" => "Cộng số lượng tồn theo vị trí",
        "countunpostedoutboundvouchers" => "Đếm phiếu xuất chưa ghi sổ",
        "avgproductivitypercent" => "Trung bình tỷ lệ năng suất",
        "suminvoicelinetotal" => "Cộng giá trị dòng phí",
        "countoverduerequesteddelivery" => "Đếm phiếu quá hạn giao",
        _ => string.IsNullOrWhiteSpace(formula) ? "Chưa rõ" : formula.Trim()
    };

    public static string SemanticScope(string? scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
            return "Toàn hệ thống";

        var warehouse = "Tất cả kho";
        var owner = "Tất cả chủ hàng";
        foreach (var part in scopeKey.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("WH:", StringComparison.OrdinalIgnoreCase))
            {
                var value = part[3..];
                warehouse = string.Equals(value, "ALL", StringComparison.OrdinalIgnoreCase) ? "Tất cả kho" : $"Kho {value}";
            }
            else if (part.StartsWith("OWNER:", StringComparison.OrdinalIgnoreCase))
            {
                var value = part[6..];
                owner = string.Equals(value, "ALL", StringComparison.OrdinalIgnoreCase) ? "Tất cả chủ hàng" : $"Chủ hàng {value}";
            }
        }

        return $"{warehouse}; {owner}";
    }

    public static string SourceCitation(string? sourceCitation)
    {
        if (string.IsNullOrWhiteSpace(sourceCitation))
            return "Chưa có nguồn";

        var parts = sourceCitation.Split(':', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? $"{DataSourceLabel(parts[0])}: {SemanticMetricFormula(parts[1])}"
            : DataSourceLabel(sourceCitation);
    }

    public static string ReferenceLabel(string? referenceType, string? referenceId)
    {
        var label = DataSourceLabel(referenceType);
        return string.IsNullOrWhiteSpace(referenceId) ? label : $"{label}: {referenceId.Trim()}";
    }

    public static string FinancialSourceType(string? sourceType) => DataSourceLabel(sourceType);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
}
