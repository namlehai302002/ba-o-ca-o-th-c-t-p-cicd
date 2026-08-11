namespace WMS.Models;

public static class WmsRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Staff = "Staff";
    public const string Viewer = "Viewer";

    public const string InboundStaff = "InboundStaff";
    public const string OutboundStaff = "OutboundStaff";
    public const string InventoryStaff = "InventoryStaff";
    public const string TransportStaff = "TransportStaff";
    public const string ReportViewer = "ReportViewer";

    public const string WarehouseExecutionRoles = "Admin,Manager,Staff,InboundStaff,OutboundStaff,InventoryStaff,TransportStaff";
    public const string InboundRoles = "Admin,Manager,Staff,InboundStaff";
    public const string OutboundRoles = "Admin,Manager,Staff,OutboundStaff";
    public const string InventoryRoles = "Admin,Manager,Staff,InventoryStaff";
    public const string InventoryReadRoles = "Admin,Manager,Staff,InventoryStaff,Viewer,ReportViewer";
    public const string TransportRoles = "Admin,Manager,Staff,TransportStaff";
    public const string OutboundTransportRoles = "Admin,Manager,Staff,OutboundStaff,TransportStaff";
    public const string ReportRoles = "Admin,Manager,Viewer,ReportViewer";
    public const string ReportManagerRoles = "Admin,Manager,ReportViewer";
    public const string WarehouseReportRoles = "Admin,Manager,Staff,InboundStaff,OutboundStaff,InventoryStaff,TransportStaff,ReportViewer";
    public const string AdminManagerRoles = "Admin,Manager";

    public static readonly string[] OutboundAssigneeRoleNames = { Manager, Staff, OutboundStaff };

    public static readonly RoleDefinition[] Definitions =
    {
        new(Admin, "Quản trị viên", "Toàn quyền hệ thống, người dùng, phân quyền, bảo mật và cấu hình trọng yếu."),
        new(Manager, "Quản lý kho", "Điều phối vận hành, duyệt nghiệp vụ, xử lý ngoại lệ và xem báo cáo quản trị."),
        new(Staff, "Nhân viên kho tổng hợp", "Vai trò vận hành cũ, dùng cho nhân viên kiêm nhiệm nhiều nghiệp vụ kho."),
        new(InboundStaff, "Nhân viên nhập kho", "Tạo, tiếp nhận, quét nhận hàng, kiểm tra nhận hàng và theo dõi lịch sử nhập."),
        new(OutboundStaff, "Nhân viên xuất kho", "Lấy hàng, quét lấy hàng, đóng gói và bàn giao đơn xuất."),
        new(InventoryStaff, "Nhân viên tồn kho/kiểm kê", "Xem tồn, mã kiện, số sê-ri, kiểm kê, điều chỉnh và di chuyển tồn kho."),
        new(TransportStaff, "Nhân viên vận chuyển", "Điều phối giao hàng, chuyến xe, nhãn/chứng từ và đối soát giao hàng."),
        new(ReportViewer, "Nhân viên báo cáo", "Xem dashboard, báo cáo vận hành và phân tích được phân quyền, không làm đổi tồn kho."),
        new(Viewer, "Chỉ xem", "Chỉ xem dữ liệu cơ bản theo phạm vi, không thực hiện thao tác làm đổi tồn kho.")
    };

    public static bool IsAdmin(string? role) => Is(role, Admin);
    public static bool IsManager(string? role) => Is(role, Manager);
    public static bool IsAdminOrManager(string? role) => IsAdmin(role) || IsManager(role);
    public static bool IsLegacyStaff(string? role) => Is(role, Staff);
    public static bool IsInbound(string? role) => IsAdminOrManager(role) || IsLegacyStaff(role) || Is(role, InboundStaff);
    public static bool IsOutbound(string? role) => IsAdminOrManager(role) || IsLegacyStaff(role) || Is(role, OutboundStaff);
    public static bool IsInventory(string? role) => IsAdminOrManager(role) || IsLegacyStaff(role) || Is(role, InventoryStaff);
    public static bool IsTransport(string? role) => IsAdminOrManager(role) || IsLegacyStaff(role) || Is(role, TransportStaff);
    public static bool IsWarehouseOperator(string? role) => IsInbound(role) || IsOutbound(role) || IsInventory(role) || IsTransport(role);
    public static bool IsReportViewer(string? role) => IsAdminOrManager(role) || Is(role, ReportViewer) || Is(role, Viewer);
    public static bool IsReportingSpecialist(string? role) => Is(role, ReportViewer);
    public static bool IsViewerOnly(string? role) => Is(role, Viewer);

    public static string Label(string? role)
        => Definitions.FirstOrDefault(x => Is(role, x.Name))?.Label ?? (string.IsNullOrWhiteSpace(role) ? "Chưa gán" : role.Trim());

    public static string Description(string? role)
        => Definitions.FirstOrDefault(x => Is(role, x.Name))?.Description
           ?? "Vai trò tùy chỉnh, cần kiểm tra quyền chi tiết trước khi cấp cho người dùng.";

    public static string BadgeClass(string? role) => role?.Trim() switch
    {
        Admin => "badge-danger",
        Manager => "badge-warning",
        Staff => "badge-info",
        InboundStaff => "badge-success",
        OutboundStaff => "badge-info",
        InventoryStaff => "badge-soft-info",
        TransportStaff => "badge-warning",
        ReportViewer => "badge-secondary",
        Viewer => "badge-secondary",
        _ => "badge-secondary"
    };

    private static bool Is(string? role, string expected)
        => string.Equals(role?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}

public sealed record RoleDefinition(string Name, string Label, string Description);
