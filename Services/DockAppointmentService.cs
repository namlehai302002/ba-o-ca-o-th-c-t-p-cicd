using Microsoft.EntityFrameworkCore;
using WMS.Common;
using WMS.Data;
using WMS.Models;

namespace WMS.Services;

public sealed class DockAppointmentRequest
{
    public int WarehouseId { get; init; }
    public int? OwnerPartnerId { get; init; }
    public long? VoucherId { get; init; }
    public long? ShipmentLoadId { get; init; }
    public DockAppointmentDirectionEnum Direction { get; init; } = DockAppointmentDirectionEnum.Inbound;
    public string? DockDoor { get; init; }
    public DateTime PlannedStartAt { get; init; }
    public DateTime PlannedEndAt { get; init; }
    public string? GoodsType { get; init; }
    public bool IsHazmat { get; init; }
    public bool IsRefrigerated { get; init; }
    public bool IsUrgent { get; init; }
    public int Priority { get; init; } = 5;
    public decimal PalletCount { get; init; }
    public decimal CartonCount { get; init; }
    public decimal WeightKg { get; init; }
    public decimal VolumeCbm { get; init; }
    public string? Notes { get; init; }
    public string Actor { get; init; } = "system";
}

public sealed record DockDoorSuggestion(string DockDoor, int Score, string Reason, string? Warning);

public interface IDockAppointmentService
{
    Task<DockDoorSuggestion> SuggestDoorAsync(DockAppointmentRequest request, int? scopedWarehouseId = null, CancellationToken ct = default);
    Task<DockAppointment> CreateAsync(DockAppointmentRequest request, int? scopedWarehouseId = null, CancellationToken ct = default);
    Task<DockAppointment> RescheduleAsync(long appointmentId, DateTime plannedStartAt, DateTime plannedEndAt, string? dockDoor, int? scopedWarehouseId, string actor, CancellationToken ct = default);
    Task<DockAppointment> CancelAsync(long appointmentId, int? scopedWarehouseId, string actor, string? reason = null, CancellationToken ct = default);
    Task<DockAppointment> CheckInAsync(long appointmentId, int? scopedWarehouseId, string actor, CancellationToken ct = default);
    Task<DockAppointment> CheckOutAsync(long appointmentId, int? scopedWarehouseId, string actor, CancellationToken ct = default);
}

public class DockAppointmentService : IDockAppointmentService
{
    private readonly AppDbContext _db;
    private readonly IUnitOfWork _unitOfWork;

    public DockAppointmentService(AppDbContext db, IUnitOfWork? unitOfWork = null)
    {
        _db = db;
        _unitOfWork = unitOfWork ?? new EfUnitOfWork(db);
    }

    private static DateTime Now => VietnamTime.Now;

    public async Task<DockDoorSuggestion> SuggestDoorAsync(DockAppointmentRequest request, int? scopedWarehouseId = null, CancellationToken ct = default)
    {
        ValidateWindow(request.WarehouseId, request.PlannedStartAt, request.PlannedEndAt, scopedWarehouseId);

        var start = request.PlannedStartAt;
        var end = request.PlannedEndAt;
        var minuteOfDay = start.Hour * 60 + start.Minute;
        var requiredDoorType = request.Direction == DockAppointmentDirectionEnum.Outbound
            ? DockDoorTypeEnum.Shipping
            : DockDoorTypeEnum.Receiving;

        var capacities = await _db.DockDoorCapacities.AsNoTracking()
            .Where(c => c.WarehouseId == request.WarehouseId
                && (!c.DayOfWeek.HasValue || c.DayOfWeek == start.DayOfWeek)
                && c.SlotStartMinutes <= minuteOfDay
                && c.SlotEndMinutes > minuteOfDay)
            .ToListAsync(ct);

        if (capacities.Count == 0)
        {
            var fallbackDoor = string.IsNullOrWhiteSpace(request.DockDoor) ? "DOCK-A" : NormalizeDoor(request.DockDoor);
            return new DockDoorSuggestion(fallbackDoor, 10, "Chưa cấu hình năng lực cửa bến, dùng cửa mặc định để lập lịch tạm.", "Chưa có cấu hình sức chứa cửa bến cho khung giờ này.");
        }

        var activeStatuses = ActiveStatuses;
        var existing = await _db.DockAppointments.AsNoTracking()
            .Where(a => a.WarehouseId == request.WarehouseId
                && activeStatuses.Contains(a.Status)
                && a.PlannedStartAt < end
                && a.PlannedEndAt > start)
            .GroupBy(a => a.DockDoor)
            .Select(g => new { DockDoor = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DockDoor, x => x.Count, StringComparer.OrdinalIgnoreCase, ct);

        DockDoorSuggestion? best = null;
        foreach (var c in capacities)
        {
            var score = 100;
            if (c.DoorType != DockDoorTypeEnum.Both && c.DoorType != requiredDoorType)
                score -= 40;
            if (request.IsRefrigerated && !c.IsRefrigerated)
                score -= 50;
            if (request.IsHazmat && !c.IsHazmat)
                score -= 50;
            if (request.IsUrgent)
                score += 10;

            var load = existing.TryGetValue(c.DockDoor, out var count) ? count : 0;
            score -= load * 15;
            score += Math.Max(0, 120 - c.AvgUnloadMinutes) / 10;

            var warning = load >= Math.Max(1, c.MaxAppointments)
                ? $"Cửa {c.DockDoor} đã vượt năng lực {load}/{c.MaxAppointments} lịch trong khung giờ."
                : null;
            var reason = $"{c.DockDoor}: tải {load}/{c.MaxAppointments}, loại {DockDoorTypeLabel(c.DoorType)}, điểm {score}.";
            var candidate = new DockDoorSuggestion(NormalizeDoor(c.DockDoor), score, reason, warning);
            if (best == null || candidate.Score > best.Score)
                best = candidate;
        }

        return best ?? throw new BusinessRuleException("Không có cửa bến phù hợp để lập lịch.", "DOCK_NO_CAPACITY", "DockAppointment");
    }

    public async Task<DockAppointment> CreateAsync(DockAppointmentRequest request, int? scopedWarehouseId = null, CancellationToken ct = default)
    {
        ValidateWindow(request.WarehouseId, request.PlannedStartAt, request.PlannedEndAt, scopedWarehouseId);
        var suggestion = await SuggestDoorAsync(request, scopedWarehouseId, ct);
        var dockDoor = string.IsNullOrWhiteSpace(request.DockDoor) ? suggestion.DockDoor : NormalizeDoor(request.DockDoor);
        await EnsureNoHardConflictAsync(0, request.WarehouseId, dockDoor, request.PlannedStartAt, request.PlannedEndAt, ct);

        var warning = await BuildOverloadWarningAsync(request.WarehouseId, dockDoor, request.PlannedStartAt, request.PlannedEndAt, ct)
            ?? suggestion.Warning;

        var appointment = new DockAppointment
        {
            AppointmentCode = await GenerateCodeAsync(request.WarehouseId, request.PlannedStartAt, ct),
            WarehouseId = request.WarehouseId,
            OwnerPartnerId = request.OwnerPartnerId,
            VoucherId = request.VoucherId,
            ShipmentLoadId = request.ShipmentLoadId,
            Direction = request.Direction,
            Status = DockAppointmentStatusEnum.Scheduled,
            DockDoor = dockDoor,
            PlannedStartAt = request.PlannedStartAt,
            PlannedEndAt = request.PlannedEndAt,
            GoodsType = Clean(request.GoodsType),
            IsHazmat = request.IsHazmat,
            IsRefrigerated = request.IsRefrigerated,
            IsUrgent = request.IsUrgent,
            Priority = request.Priority,
            PalletCount = request.PalletCount,
            CartonCount = request.CartonCount,
            WeightKg = request.WeightKg,
            VolumeCbm = request.VolumeCbm,
            SuggestedScore = suggestion.Score,
            HasConflictWarning = warning != null,
            OverloadWarning = warning,
            Notes = Clean(request.Notes),
            CreatedBy = CleanActor(request.Actor),
            CreatedAt = Now
        };

        _db.DockAppointments.Add(appointment);
        await SyncLinkedWorkAsync(appointment, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return appointment;
    }

    public async Task<DockAppointment> RescheduleAsync(long appointmentId, DateTime plannedStartAt, DateTime plannedEndAt, string? dockDoor, int? scopedWarehouseId, string actor, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, scopedWarehouseId, ct);
        if (appointment.Status is DockAppointmentStatusEnum.Completed or DockAppointmentStatusEnum.Cancelled)
            throw new BusinessRuleException("Không thể đổi lịch appointment đã đóng.", "DOCK_APPOINTMENT_CLOSED", "DockAppointment");

        ValidateWindow(appointment.WarehouseId, plannedStartAt, plannedEndAt, scopedWarehouseId);
        var door = string.IsNullOrWhiteSpace(dockDoor) ? appointment.DockDoor : NormalizeDoor(dockDoor);
        await EnsureNoHardConflictAsync(appointment.DockAppointmentId, appointment.WarehouseId, door, plannedStartAt, plannedEndAt, ct);

        appointment.DockDoor = door;
        appointment.PlannedStartAt = plannedStartAt;
        appointment.PlannedEndAt = plannedEndAt;
        appointment.OverloadWarning = await BuildOverloadWarningAsync(appointment.WarehouseId, door, plannedStartAt, plannedEndAt, ct);
        appointment.HasConflictWarning = appointment.OverloadWarning != null;
        appointment.UpdatedAt = Now;
        appointment.UpdatedBy = CleanActor(actor);
        await SyncLinkedWorkAsync(appointment, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return appointment;
    }

    public async Task<DockAppointment> CancelAsync(long appointmentId, int? scopedWarehouseId, string actor, string? reason = null, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, scopedWarehouseId, ct);
        appointment.Status = DockAppointmentStatusEnum.Cancelled;
        appointment.Notes = string.Join(" | ", new[] { appointment.Notes, Clean(reason) }.Where(x => !string.IsNullOrWhiteSpace(x)));
        appointment.UpdatedAt = Now;
        appointment.UpdatedBy = CleanActor(actor);
        await _unitOfWork.SaveChangesAsync(ct);
        return appointment;
    }

    public async Task<DockAppointment> CheckInAsync(long appointmentId, int? scopedWarehouseId, string actor, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, scopedWarehouseId, ct);
        appointment.Status = DockAppointmentStatusEnum.CheckedIn;
        appointment.CheckInAt ??= Now;
        appointment.UpdatedAt = Now;
        appointment.UpdatedBy = CleanActor(actor);
        await SyncLinkedWorkAsync(appointment, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return appointment;
    }

    public async Task<DockAppointment> CheckOutAsync(long appointmentId, int? scopedWarehouseId, string actor, CancellationToken ct = default)
    {
        var appointment = await LoadAsync(appointmentId, scopedWarehouseId, ct);
        appointment.Status = DockAppointmentStatusEnum.Completed;
        appointment.CheckOutAt ??= Now;
        appointment.DockEndAt ??= appointment.CheckOutAt;
        appointment.UpdatedAt = Now;
        appointment.UpdatedBy = CleanActor(actor);
        await SyncLinkedWorkAsync(appointment, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return appointment;
    }

    private async Task<DockAppointment> LoadAsync(long appointmentId, int? scopedWarehouseId, CancellationToken ct)
    {
        var appointment = await _db.DockAppointments.FirstOrDefaultAsync(a => a.DockAppointmentId == appointmentId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy lịch cửa bến.", "DOCK_APPOINTMENT_NOT_FOUND", "DockAppointment");
        EnsureWarehouseScope(appointment.WarehouseId, scopedWarehouseId);
        return appointment;
    }

    private async Task EnsureNoHardConflictAsync(long appointmentId, int warehouseId, string dockDoor, DateTime start, DateTime end, CancellationToken ct)
    {
        var statuses = ActiveStatuses;
        var exists = await _db.DockAppointments.AnyAsync(a =>
            a.DockAppointmentId != appointmentId
            && a.WarehouseId == warehouseId
            && a.DockDoor == dockDoor
            && statuses.Contains(a.Status)
            && a.PlannedStartAt < end
            && a.PlannedEndAt > start, ct);
        if (exists)
            throw new BusinessRuleException($"Cửa {dockDoor} đã có lịch trùng khung giờ.", "DOCK_APPOINTMENT_CONFLICT", "DockAppointment");
    }

    private async Task<string?> BuildOverloadWarningAsync(int warehouseId, string dockDoor, DateTime start, DateTime end, CancellationToken ct)
    {
        var minuteOfDay = start.Hour * 60 + start.Minute;
        var capacity = await _db.DockDoorCapacities.AsNoTracking()
            .Where(c => c.WarehouseId == warehouseId
                && c.DockDoor == dockDoor
                && (!c.DayOfWeek.HasValue || c.DayOfWeek == start.DayOfWeek)
                && c.SlotStartMinutes <= minuteOfDay
                && c.SlotEndMinutes > minuteOfDay)
            .OrderByDescending(c => c.MaxAppointments)
            .FirstOrDefaultAsync(ct);
        if (capacity == null)
            return "Chưa có cấu hình năng lực cho cửa/khung giờ này.";

        var statuses = ActiveStatuses;
        var count = await _db.DockAppointments.CountAsync(a =>
            a.WarehouseId == warehouseId
            && a.DockDoor == dockDoor
            && statuses.Contains(a.Status)
            && a.PlannedStartAt < end
            && a.PlannedEndAt > start, ct);
        return count + 1 > capacity.MaxAppointments
            ? $"Quá tải cửa {dockDoor}: {count + 1}/{capacity.MaxAppointments} lịch trong khung giờ."
            : null;
    }

    private async Task SyncLinkedWorkAsync(DockAppointment appointment, CancellationToken ct)
    {
        if (appointment.VoucherId.HasValue)
        {
            var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.VoucherId == appointment.VoucherId.Value, ct);
            if (voucher != null)
            {
                voucher.DockDoor = appointment.DockDoor;
                voucher.DockAppointmentStart = appointment.PlannedStartAt;
                voucher.DockAppointmentEnd = appointment.PlannedEndAt;
                if (appointment.Status == DockAppointmentStatusEnum.CheckedIn)
                    voucher.GateInAt ??= appointment.CheckInAt ?? Now;
                if (appointment.Status == DockAppointmentStatusEnum.Completed)
                {
                    voucher.DockCompletedAt ??= appointment.DockEndAt ?? Now;
                    voucher.DockStatus = DockOperationStatusEnum.Completed;
                }
            }
        }

        if (appointment.ShipmentLoadId.HasValue)
        {
            var load = await _db.ShipmentLoads.FirstOrDefaultAsync(l => l.ShipmentLoadId == appointment.ShipmentLoadId.Value, ct);
            if (load != null)
            {
                load.DockDoor = appointment.DockDoor;
                load.PlannedDepartureAt = appointment.PlannedEndAt;
                load.UpdatedAt = Now;
            }
        }
    }

    private async Task<string> GenerateCodeAsync(int warehouseId, DateTime date, CancellationToken ct)
    {
        var prefix = $"DA-{date:yyyyMMdd}-{warehouseId}-";
        var count = await _db.DockAppointments.CountAsync(x => x.AppointmentCode.StartsWith(prefix), ct);
        return $"{prefix}{count + 1:0000}";
    }

    private static void ValidateWindow(int warehouseId, DateTime start, DateTime end, int? scopedWarehouseId)
    {
        EnsureWarehouseScope(warehouseId, scopedWarehouseId);
        if (warehouseId <= 0)
            throw new BusinessRuleException("Kho là bắt buộc khi lập lịch cửa bến.", "DOCK_WAREHOUSE_REQUIRED", "DockAppointment");
        if (end <= start)
            throw new BusinessRuleException("Giờ kết thúc phải lớn hơn giờ bắt đầu.", "DOCK_WINDOW_INVALID", "DockAppointment");
    }

    private static void EnsureWarehouseScope(int warehouseId, int? scopedWarehouseId)
    {
        if (scopedWarehouseId.HasValue && warehouseId != scopedWarehouseId.Value)
            throw new UnauthorizedAccessException("Không được thao tác lịch cửa bến của kho khác.");
    }

    private static DockAppointmentStatusEnum[] ActiveStatuses =>
    [
        DockAppointmentStatusEnum.Scheduled,
        DockAppointmentStatusEnum.CheckedIn,
        DockAppointmentStatusEnum.AtDock
    ];

    private static string NormalizeDoor(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();

    private static string DockDoorTypeLabel(DockDoorTypeEnum doorType) => doorType switch
    {
        DockDoorTypeEnum.Receiving => "nhận hàng",
        DockDoorTypeEnum.Shipping => "xuất hàng",
        DockDoorTypeEnum.Both => "hai chiều",
        _ => "khác"
    };

    private static string CleanActor(string? actor)
        => string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim()[..Math.Min(actor.Trim().Length, 100)];

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
