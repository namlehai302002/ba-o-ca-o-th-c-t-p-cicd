using Microsoft.EntityFrameworkCore;
using System.Data;
using WMS.Common;
using WMS.Data;
using WMS.Models;

namespace WMS.Services;

public sealed class PutawayPlanRequest
{
    public int ItemId { get; init; }
    public int WarehouseId { get; init; }
    public int? OwnerPartnerId { get; init; }
    public decimal Quantity { get; init; } = 1m;
    public string? LotNumber { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public int RowIndex { get; init; }
}

public sealed class PutawayPlanSuggestion
{
    public int ItemId { get; init; }
    public int RowIndex { get; init; }
    public int? LocationId { get; init; }
    public string LocationCode { get; init; } = "";
    public string ZoneName { get; init; } = "";
    public string Strategy { get; init; } = "";
    public string Reason { get; init; } = "";
    public decimal Score { get; init; }
    public bool RequiresOverrideReason { get; init; }
}

public interface IDirectedPutawayService
{
    Task<List<PutawayPlanSuggestion>> SuggestAsync(IReadOnlyCollection<PutawayPlanRequest> requests, CancellationToken ct = default);
    Task<AuditLog> RecordOverrideAsync(PutawayPlanSuggestion suggestion, int chosenLocationId, string overrideReason, string actor, CancellationToken ct = default);
}

public sealed class DirectedPutawayService : IDirectedPutawayService
{
    private readonly AppDbContext _db;

    public DirectedPutawayService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<PutawayPlanSuggestion>> SuggestAsync(IReadOnlyCollection<PutawayPlanRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0)
            return new List<PutawayPlanSuggestion>();

        var warehouseIds = requests.Select(r => r.WarehouseId).Distinct().ToList();
        if (warehouseIds.Count != 1 || warehouseIds[0] <= 0)
            throw new BusinessRuleException("Mỗi lần gợi ý cất hàng chỉ được áp dụng cho đúng một kho.", "PUTAWAY_WAREHOUSE_REQUIRED", "Warehouse");

        var warehouseId = warehouseIds[0];
        var itemIds = requests.Select(r => r.ItemId).Distinct().ToList();
        var items = await _db.Items
            .AsNoTracking()
            .Include(i => i.DefaultLocation)!.ThenInclude(l => l!.Zone)
            .Where(i => itemIds.Contains(i.ItemId) && i.IsActive)
            .ToDictionaryAsync(i => i.ItemId, ct);

        var locations = await _db.Locations
            .AsNoTracking()
            .Include(l => l.Zone)
            .Where(l => l.IsActive
                && l.Zone != null
                && l.Zone.IsActive
                && l.Zone.WarehouseId == warehouseId
                && l.Zone.ZoneType != ZoneTypeEnum.Shipping
                && l.Zone.ZoneType != ZoneTypeEnum.Receiving)
            .OrderBy(l => l.Zone.ZoneCode)
            .ThenBy(l => l.LocationCode)
            .ToListAsync(ct);

        var locationIds = locations.Select(l => l.LocationId).ToList();
        var stockRows = await _db.ItemLocations
            .AsNoTracking()
            .Include(il => il.Item)
            .Include(il => il.Location)!.ThenInclude(l => l!.Zone)
            .Where(il => locationIds.Contains(il.LocationId) && il.Quantity > 0)
            .ToListAsync(ct);

        var loadByLocation = stockRows
            .GroupBy(il => il.LocationId)
            .ToDictionary(g => g.Key, g => g.Sum(EstimateLoad));

        var assigned = new HashSet<int>();
        var plannedLoad = new Dictionary<int, decimal>();
        var result = new List<PutawayPlanSuggestion>();

        foreach (var request in requests.OrderBy(r => r.RowIndex))
        {
            if (!items.TryGetValue(request.ItemId, out var item))
            {
                result.Add(new PutawayPlanSuggestion
                {
                    ItemId = request.ItemId,
                    RowIndex = request.RowIndex,
                    Strategy = "Item not found",
                    Reason = "Vật tư không tồn tại hoặc đã ngừng hoạt động."
                });
                continue;
            }

            var candidate = locations
                .Select(location => ScoreCandidate(request, item, location, stockRows, loadByLocation, plannedLoad, assigned))
                .Where(x => x != null)
                .Select(x => x!)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Location.LocationCode)
                .FirstOrDefault();

            if (candidate == null)
            {
                result.Add(new PutawayPlanSuggestion
                {
                    ItemId = request.ItemId,
                    RowIndex = request.RowIndex,
                    Strategy = "No eligible location",
                    Reason = "Không có vị trí hoạt động nào đáp ứng đồng thời quy tắc khu vực, chủ hàng, lô, sức chứa, nhiệt độ và hàng nguy hiểm."
                });
                continue;
            }

            assigned.Add(candidate.Location.LocationId);
            plannedLoad[candidate.Location.LocationId] = plannedLoad.GetValueOrDefault(candidate.Location.LocationId) + EstimateRequestLoad(item, request.Quantity);

            result.Add(new PutawayPlanSuggestion
            {
                ItemId = request.ItemId,
                RowIndex = request.RowIndex,
                LocationId = candidate.Location.LocationId,
                LocationCode = candidate.Location.LocationCode,
                ZoneName = candidate.Location.Zone?.ZoneName ?? "",
                Strategy = candidate.Strategy,
                Reason = candidate.Reason,
                Score = candidate.Score,
                RequiresOverrideReason = true
            });
        }

        return result;
    }

    public async Task<AuditLog> RecordOverrideAsync(PutawayPlanSuggestion suggestion, int chosenLocationId, string overrideReason, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(overrideReason))
            throw new BusinessRuleException("Vui lòng nhập lý do chọn vị trí khác với gợi ý cất hàng.", "PUTAWAY_OVERRIDE_REASON_REQUIRED", "AuditLog");

        var audit = new AuditLog
        {
            TableName = "PutawaySuggestion",
            RecordId = $"{suggestion.ItemId}:{suggestion.RowIndex}",
            ActionType = "OVERRIDE",
            ColumnChanged = "LocationId",
            OldValue = suggestion.LocationId?.ToString(),
            NewValue = $"Chosen:{chosenLocationId};Reason:{overrideReason.Trim()}",
            ChangedBy = actor,
            ChangedAt = VietnamTime.Now,
            AppModule = "DirectedPutaway"
        };
        _db.AuditLogs.Add(audit);
        await _db.SaveChangesAsync(ct);
        return audit;
    }

    private sealed class PutawayCandidate
    {
        public required Location Location { get; init; }
        public required string Strategy { get; init; }
        public required string Reason { get; init; }
        public decimal Score { get; init; }
    }

    private static PutawayCandidate? ScoreCandidate(
        PutawayPlanRequest request,
        Item item,
        Location location,
        List<ItemLocation> stockRows,
        IReadOnlyDictionary<int, decimal> loadByLocation,
        IReadOnlyDictionary<int, decimal> plannedLoad,
        HashSet<int> assigned)
    {
        if (assigned.Contains(location.LocationId))
            return null;
        if (location.Zone == null || !IsAllowedZone(item, location.Zone, stockRows.Select(x => x.Location?.Zone).Where(x => x != null).Cast<Zone>()))
            return null;
        if (!OwnerAndMixingAllowed(request, item, location, stockRows))
            return null;
        if (!FitsCapacity(request, item, location, loadByLocation, plannedLoad))
            return null;

        var sameLocationRows = stockRows
            .Where(x => x.LocationId == location.LocationId && x.ItemId == item.ItemId && x.OwnerPartnerId == (request.OwnerPartnerId ?? item.OwnerPartnerId))
            .ToList();
        var exactLot = sameLocationRows.Any(x =>
            (!string.IsNullOrWhiteSpace(request.LotNumber) && string.Equals(x.LotNumber, request.LotNumber, StringComparison.OrdinalIgnoreCase))
            || (request.ExpiryDate.HasValue && x.ExpiryDate?.Date == request.ExpiryDate.Value.Date));
        var sameItem = sameLocationRows.Count > 0;
        var empty = !stockRows.Any(x => x.LocationId == location.LocationId && x.Quantity > 0);
        var sameZoneAsDefault = item.DefaultLocation?.ZoneId == location.ZoneId;
        var isDefault = item.DefaultLocationId == location.LocationId;

        var score = 0m;
        var strategy = "Available bin";
        var reason = "Vị trí đang hoạt động và đáp ứng các điều kiện cất hàng.";

        if (exactLot)
        {
            score += 1000m;
            strategy = "Consolidate same lot / expiry";
            reason = "Cùng vật tư, chủ hàng, lô hoặc hạn dùng để giữ truy xuất nhất quán.";
        }
        else if (sameItem)
        {
            score += 900m;
            strategy = "Consolidate same item";
            reason = "Vật tư và chủ hàng này đã có tại vị trí.";
        }
        else if (isDefault)
        {
            score += 850m;
            strategy = "Vị trí mặc định";
            reason = "Vị trí mặc định của vật tư còn khả dụng và đáp ứng quy tắc sức chứa.";
        }
        else if (empty && sameZoneAsDefault)
        {
            score += 700m;
            strategy = "Empty bin in default zone";
            reason = "Giữ hàng gần khu mặc định đã cấu hình.";
        }
        else if (empty)
        {
            score += 600m;
            strategy = "Empty eligible bin";
            reason = "Vị trí đang trống và đáp ứng các quy tắc vận hành.";
        }

        var abc = ResolveAbc(item);
        if (abc == 'A' && location.IsGoldenZone)
            score += 180m;
        else if (abc == 'B' && location.HeightLevel <= 3)
            score += 80m;

        if (IsHazmat(item) && ZoneContains(location.Zone, "HAZ", "CHEM"))
            score += 140m;
        if (RequiresTemperatureZone(item) && ZoneContains(location.Zone, "COLD", "CHILL", "FREEZE", "TEMP"))
            score += 140m;

        score += CapacityFillScore(request, item, location, loadByLocation, plannedLoad);
        return new PutawayCandidate { Location = location, Strategy = strategy, Reason = reason, Score = score };
    }

    private static bool OwnerAndMixingAllowed(PutawayPlanRequest request, Item item, Location location, List<ItemLocation> stockRows)
    {
        var owner = request.OwnerPartnerId ?? item.OwnerPartnerId;
        var existing = stockRows.Where(x => x.LocationId == location.LocationId && x.Quantity > 0).ToList();
        if (existing.Count == 0)
            return true;

        if (owner.HasValue && existing.Any(x => x.OwnerPartnerId.HasValue && x.OwnerPartnerId != owner))
            return false;

        if (!location.AllowMixedSku && existing.Any(x => x.ItemId != item.ItemId))
            return false;

        return true;
    }

    private static bool FitsCapacity(
        PutawayPlanRequest request,
        Item item,
        Location location,
        IReadOnlyDictionary<int, decimal> loadByLocation,
        IReadOnlyDictionary<int, decimal> plannedLoad)
    {
        var current = Math.Max(location.CurrentLoad, loadByLocation.GetValueOrDefault(location.LocationId));
        var projected = current + plannedLoad.GetValueOrDefault(location.LocationId) + EstimateRequestLoad(item, request.Quantity);
        var max = item.ItemType == ItemTypeEnum.HoaChat
            ? location.MaxCapacity
            : location.MaxWeightCapacityKg ?? location.MaxCapacity;
        return max <= 0m || projected <= max;
    }

    private static decimal CapacityFillScore(
        PutawayPlanRequest request,
        Item item,
        Location location,
        IReadOnlyDictionary<int, decimal> loadByLocation,
        IReadOnlyDictionary<int, decimal> plannedLoad)
    {
        var max = item.ItemType == ItemTypeEnum.HoaChat
            ? location.MaxCapacity
            : location.MaxWeightCapacityKg ?? location.MaxCapacity;
        if (max <= 0m)
            return 0m;

        var current = Math.Max(location.CurrentLoad, loadByLocation.GetValueOrDefault(location.LocationId));
        var projectedFill = (current + plannedLoad.GetValueOrDefault(location.LocationId) + EstimateRequestLoad(item, request.Quantity)) / max;
        return Math.Max(0m, 100m - Math.Abs(0.65m - projectedFill) * 100m);
    }

    private static bool IsAllowedZone(Item item, Zone zone, IEnumerable<Zone> allZones)
    {
        if (!string.IsNullOrWhiteSpace(item.AllowedZoneTypes))
        {
            var allowed = item.AllowedZoneTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => byte.TryParse(x, out _))
                .Select(byte.Parse)
                .ToHashSet();
            if (allowed.Count > 0 && !allowed.Contains((byte)zone.ZoneType))
                return false;
        }

        if (IsHazmat(item) && allZones.Any(z => ZoneContains(z, "HAZ", "CHEM")))
            return ZoneContains(zone, "HAZ", "CHEM");

        if (RequiresTemperatureZone(item) && allZones.Any(z => ZoneContains(z, "COLD", "CHILL", "FREEZE", "TEMP")))
            return ZoneContains(zone, "COLD", "CHILL", "FREEZE", "TEMP");

        return zone.ZoneType is ZoneTypeEnum.Storage or ZoneTypeEnum.Staging or ZoneTypeEnum.CrossDock;
    }

    private static bool ZoneContains(Zone? zone, params string[] tokens)
    {
        var text = $"{zone?.ZoneCode} {zone?.ZoneName}".ToUpperInvariant();
        return tokens.Any(text.Contains);
    }

    private static bool IsHazmat(Item item)
        => item.ItemType == ItemTypeEnum.HoaChat
            || ContainsAny(item.Specifications, "HAZMAT", "CHEM", "DANGEROUS")
            || ContainsAny(item.Description, "HAZMAT", "CHEM", "DANGEROUS");

    private static bool RequiresTemperatureZone(Item item)
        => ContainsAny(item.Specifications, "COLD", "CHILL", "FREEZE", "TEMP")
            || ContainsAny(item.Description, "COLD", "CHILL", "FREEZE", "TEMP");

    private static bool ContainsAny(string? value, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var upper = value.ToUpperInvariant();
        return tokens.Any(upper.Contains);
    }

    private static char ResolveAbc(Item item)
    {
        if (string.IsNullOrWhiteSpace(item.AbcClass))
            return 'C';
        var c = char.ToUpperInvariant(item.AbcClass[0]);
        return c is 'A' or 'B' or 'C' ? c : 'C';
    }

    private static decimal EstimateRequestLoad(Item item, decimal quantity)
        => item.ItemType == ItemTypeEnum.HoaChat ? quantity : quantity * (item.Weight ?? 1m);

    private static decimal EstimateLoad(ItemLocation row)
        => row.Item?.ItemType == ItemTypeEnum.HoaChat ? row.Quantity : row.Quantity * (row.Item?.Weight ?? 1m);
}

public static class InventoryStatusEngine
{
    public static bool IsAvailableForAllocation(InventoryHoldStatusEnum status)
        => status is InventoryHoldStatusEnum.Available or InventoryHoldStatusEnum.Consigned;

    public static bool IsUnavailable(InventoryHoldStatusEnum status)
        => !IsAvailableForAllocation(status);

    public static InventoryHoldStatusEnum FromQualityStatus(QualityStatusEnum status)
        => status switch
        {
            QualityStatusEnum.Good or QualityStatusEnum.Passed => InventoryHoldStatusEnum.Available,
            QualityStatusEnum.Pending or QualityStatusEnum.Inspecting => InventoryHoldStatusEnum.QcHold,
            QualityStatusEnum.Quarantine => InventoryHoldStatusEnum.Quarantine,
            QualityStatusEnum.Defect or QualityStatusEnum.Failed => InventoryHoldStatusEnum.Damaged,
            QualityStatusEnum.OnHold => InventoryHoldStatusEnum.Blocked,
            _ => InventoryHoldStatusEnum.Blocked
        };

    public static void EnsurePostingAllowed(InventoryTransactionTypeEnum transactionType, InventoryHoldStatusEnum status)
    {
        var requiresAvailable = transactionType is InventoryTransactionTypeEnum.Pick
            or InventoryTransactionTypeEnum.Pack
            or InventoryTransactionTypeEnum.Ship
            or InventoryTransactionTypeEnum.TransferOut
            or InventoryTransactionTypeEnum.KitConsume
            or InventoryTransactionTypeEnum.VasConsume;

        if (requiresAvailable && !IsAvailableForAllocation(status))
            throw new BusinessRuleException(
                $"Trạng thái tồn kho [{ResolveHoldStatusLabel(status)}] không cho phép thực hiện nghiệp vụ [{ResolveTransactionLabel(transactionType)}].",
                "INVENTORY_STATUS_BLOCKED",
                "ItemLocation");
    }

    public static (decimal AvailableQty, decimal UnavailableQty) SplitAvailability(IEnumerable<ItemLocation> rows)
    {
        var available = 0m;
        var unavailable = 0m;
        foreach (var row in rows)
        {
            var qty = Math.Max(0m, row.Quantity - row.ReservedQty);
            if (IsAvailableForAllocation(row.HoldStatus))
                available += qty;
            else
                unavailable += qty;
        }

        return (available, unavailable);
    }

    private static string ResolveHoldStatusLabel(InventoryHoldStatusEnum status) => status switch
    {
        InventoryHoldStatusEnum.Available => "Khả dụng",
        InventoryHoldStatusEnum.QcHold => "Chờ kiểm tra chất lượng",
        InventoryHoldStatusEnum.Quarantine => "Cách ly",
        InventoryHoldStatusEnum.Damaged => "Hư hỏng",
        InventoryHoldStatusEnum.Expired => "Hết hạn",
        InventoryHoldStatusEnum.Blocked => "Bị khóa",
        InventoryHoldStatusEnum.Consigned => "Hàng ký gửi",
        _ => "Không xác định"
    };

    private static string ResolveTransactionLabel(InventoryTransactionTypeEnum transactionType) => transactionType switch
    {
        InventoryTransactionTypeEnum.Pick => "Lấy hàng",
        InventoryTransactionTypeEnum.Pack => "Đóng gói",
        InventoryTransactionTypeEnum.Ship => "Xuất hàng",
        InventoryTransactionTypeEnum.TransferOut => "Điều chuyển ra",
        InventoryTransactionTypeEnum.KitConsume => "Tiêu hao ráp bộ",
        InventoryTransactionTypeEnum.VasConsume => "Tiêu hao gia công phụ trợ",
        _ => "Giao dịch tồn kho"
    };
}

public sealed class AllocationRequest
{
    public int ItemId { get; init; }
    public int WarehouseId { get; init; }
    public int? OwnerPartnerId { get; init; }
    public decimal RequiredQty { get; init; }
    public AllocationStrategyEnum Strategy { get; init; } = AllocationStrategyEnum.Fefo;
    public bool AllowPartial { get; init; }
    public string? LotNumber { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public IReadOnlyCollection<int>? ZoneIds { get; init; }
    public IReadOnlyCollection<int>? ExcludedLocationIds { get; init; }
}

public sealed record AllocationSlice(int ItemLocationId, int LocationId, string? LotNumber, DateTime? ExpiryDate, decimal Qty);

public sealed class AllocationPlan
{
    public decimal RequestedQty { get; init; }
    public decimal AllocatedQty { get; init; }
    public decimal ShortQty => Math.Max(0m, RequestedQty - AllocatedQty);
    public bool IsComplete => ShortQty <= 0m;
    public List<AllocationSlice> Slices { get; init; } = new();
}

public interface IAdvancedAllocationService
{
    Task<AllocationPlan> AllocateAsync(AllocationRequest request, CancellationToken ct = default);
}

public sealed class AdvancedAllocationService : IAdvancedAllocationService
{
    private readonly AppDbContext _db;

    public AdvancedAllocationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AllocationPlan> AllocateAsync(AllocationRequest request, CancellationToken ct = default)
    {
        if (request.RequiredQty <= 0m)
            return new AllocationPlan { RequestedQty = request.RequiredQty };

        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.ItemId == request.ItemId && i.IsActive, ct);
        if (item == null)
            throw new BusinessRuleException("Vật tư cần phân bổ không tồn tại hoặc đã ngừng sử dụng.", "ALLOCATION_ITEM_NOT_FOUND", "Item");

        var excluded = (request.ExcludedLocationIds ?? Array.Empty<int>()).ToHashSet();
        var zoneIds = (request.ZoneIds ?? Array.Empty<int>()).ToHashSet();
        var rows = await _db.ItemLocations
            .AsNoTracking()
            .Include(il => il.Location)!.ThenInclude(l => l!.Zone)
            .Where(il => il.ItemId == request.ItemId
                && il.OwnerPartnerId == request.OwnerPartnerId
                && !excluded.Contains(il.LocationId)
                && il.Quantity > il.ReservedQty
                && (request.LotNumber == null || il.LotNumber == request.LotNumber)
                && (!request.ExpiryDate.HasValue || il.ExpiryDate == request.ExpiryDate)
                && il.Location != null
                && il.Location.IsActive
                && il.Location.Zone != null
                && il.Location.Zone.WarehouseId == request.WarehouseId
                && (zoneIds.Count == 0 || zoneIds.Contains(il.Location.ZoneId)))
            .ToListAsync(ct);

        rows = rows
            .Where(il => InventoryStatusEngine.IsAvailableForAllocation(il.HoldStatus))
            .ToList();

        if (item.TrackSerial)
            rows = await CapRowsByAvailableSerialsAsync(rows, request.OwnerPartnerId, ct);

        IOrderedEnumerable<ItemLocation> ordered = request.Strategy switch
        {
            AllocationStrategyEnum.Fifo => rows.OrderBy(x => x.UpdatedAt).ThenBy(x => x.ItemLocationId),
            AllocationStrategyEnum.Lifo => rows.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.ItemLocationId),
            _ => rows.OrderBy(x => x.ExpiryDate.HasValue ? 0 : 1).ThenBy(x => x.ExpiryDate).ThenBy(x => x.UpdatedAt).ThenBy(x => x.ItemLocationId)
        };

        var remaining = request.RequiredQty;
        var slices = new List<AllocationSlice>();
        foreach (var row in ordered)
        {
            InventoryStatusEngine.EnsurePostingAllowed(InventoryTransactionTypeEnum.Pick, row.HoldStatus);
            var available = Math.Max(0m, row.Quantity - row.ReservedQty);
            if (available <= 0m)
                continue;

            var take = Math.Min(remaining, available);
            slices.Add(new AllocationSlice(row.ItemLocationId, row.LocationId, row.LotNumber, row.ExpiryDate, take));
            remaining -= take;
            if (remaining <= 0m)
                break;
        }

        var allocated = slices.Sum(s => s.Qty);
        if (allocated < request.RequiredQty && !request.AllowPartial)
            throw new BusinessRuleException("Tồn khả dụng không đủ để phân bổ toàn bộ số lượng yêu cầu.", "ALLOCATION_INSUFFICIENT_STOCK", "ItemLocation");

        return new AllocationPlan
        {
            RequestedQty = request.RequiredQty,
            AllocatedQty = allocated,
            Slices = slices
        };
    }

    private async Task<List<ItemLocation>> CapRowsByAvailableSerialsAsync(List<ItemLocation> rows, int? ownerPartnerId, CancellationToken ct)
    {
        if (rows.Count == 0)
            return rows;

        var itemIds = rows.Select(x => x.ItemId).Distinct().ToList();
        var locationIds = rows.Select(x => x.LocationId).Distinct().ToList();
        var serials = await _db.SerialNumbers
            .AsNoTracking()
            .Where(s => itemIds.Contains(s.ItemId)
                && s.LocationId.HasValue
                && locationIds.Contains(s.LocationId.Value)
                && s.OwnerPartnerId == ownerPartnerId
                && s.HoldStatus == InventoryHoldStatusEnum.Available
                && (s.Status == SerialNumberStatusEnum.Active || s.Status == SerialNumberStatusEnum.Available))
            .GroupBy(s => new { s.ItemId, LocationId = s.LocationId!.Value, s.LotNumber, s.ExpiryDate })
            .Select(g => new { g.Key.ItemId, g.Key.LocationId, g.Key.LotNumber, g.Key.ExpiryDate, Count = g.Count() })
            .ToListAsync(ct);

        var counts = serials.ToDictionary(x => (x.ItemId, x.LocationId, x.LotNumber, x.ExpiryDate), x => x.Count);
        foreach (var row in rows)
        {
            var availableSerials = counts.GetValueOrDefault((row.ItemId, row.LocationId, row.LotNumber, row.ExpiryDate));
            row.Quantity = Math.Min(row.Quantity, availableSerials);
            row.ReservedQty = 0m;
        }

        return rows.Where(x => x.Quantity > 0m).ToList();
    }
}

public sealed record CycleCountRecommendationSheetRequest(
    long RecommendationId,
    int WarehouseId,
    int? OwnerPartnerId,
    int ItemId,
    int LocationId,
    string? LotNumber,
    DateTime? ExpiryDate,
    decimal ExpectedSystemQty,
    DateTime PredictionCutoff,
    string ModelVersion,
    bool IsBlindCount,
    string Actor);

public interface ICycleCountPlanningService
{
    Task<int> CreateOrRefreshSchedulesAsync(int programId, IReadOnlyCollection<int>? zoneIds, CancellationToken ct = default);
    Task<StockCountSheet> GenerateDueSheetAsync(int programId, string actor, int maxLines = 50, CancellationToken ct = default);
    Task<StockCountSheet> GenerateRecommendationSheetAsync(CycleCountRecommendationSheetRequest request, CancellationToken ct = default);
    Task<int> CompleteApprovedSheetAsync(long stockCountSheetId, CancellationToken ct = default);
}

public sealed class CycleCountPlanningService : ICycleCountPlanningService
{
    private readonly AppDbContext _db;
    private readonly IUnitOfWork _unitOfWork;

    public CycleCountPlanningService(AppDbContext db, IUnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
    }

    public async Task<int> CreateOrRefreshSchedulesAsync(int programId, IReadOnlyCollection<int>? zoneIds, CancellationToken ct = default)
    {
        var program = await _db.CycleCountPrograms.FirstOrDefaultAsync(p => p.ProgramId == programId && p.IsActive, ct)
            ?? throw new BusinessRuleException("Không tìm thấy chương trình kiểm kê định kỳ đang hoạt động.", "CYCLE_PROGRAM_NOT_FOUND", "CycleCountProgram");

        var zoneSet = (zoneIds ?? Array.Empty<int>()).ToHashSet();
        var today = VietnamTime.Now.Date;
        var rows = await _db.ItemLocations
            .AsNoTracking()
            .Include(il => il.Item)
            .Include(il => il.Location)!.ThenInclude(l => l!.Zone)
            .Where(il => il.Quantity != 0
                && il.Location != null
                && il.Location.Zone != null
                && il.Location.Zone.WarehouseId == program.WarehouseId
                && (zoneSet.Count == 0 || zoneSet.Contains(il.Location.ZoneId)))
            .ToListAsync(ct);

        var existing = await _db.CycleCountSchedules
            .Where(s => s.ProgramId == programId)
            .ToDictionaryAsync(s => (s.ItemId, s.OwnerPartnerId, s.LocationId), ct);

        var scheduleRows = rows
            .GroupBy(r => new { r.ItemId, r.OwnerPartnerId, r.LocationId })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.OwnerPartnerId,
                g.Key.LocationId,
                Item = g.Select(r => r.Item).FirstOrDefault(),
                HighRisk = g.Any(r => r.HoldStatus != InventoryHoldStatusEnum.Available)
            })
            .ToList();

        var upserts = 0;
        foreach (var row in scheduleRows)
        {
            var abc = ResolveAbc(row.Item);
            var key = (row.ItemId, row.OwnerPartnerId, row.LocationId);
            if (!existing.TryGetValue(key, out var schedule))
            {
                schedule = new CycleCountSchedule
                {
                    ProgramId = programId,
                    ItemId = row.ItemId,
                    OwnerPartnerId = row.OwnerPartnerId,
                    LocationId = row.LocationId,
                    AbcClass = abc,
                    NextScheduledAt = today,
                    IsActive = true
                };
                _db.CycleCountSchedules.Add(schedule);
                existing[key] = schedule;
                upserts++;
                continue;
            }

            schedule.AbcClass = abc;
            schedule.IsActive = true;
            if (!schedule.NextScheduledAt.HasValue || row.HighRisk)
                schedule.NextScheduledAt = today;
            upserts++;
        }

        program.NextRunAt = today;
        await _unitOfWork.SaveChangesAsync();
        return upserts;
    }

    public async Task<StockCountSheet> GenerateDueSheetAsync(int programId, string actor, int maxLines = 50, CancellationToken ct = default)
    {
        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var program = await _db.CycleCountPrograms.FirstOrDefaultAsync(p => p.ProgramId == programId && p.IsActive, ct)
                ?? throw new BusinessRuleException("Không tìm thấy chương trình kiểm kê định kỳ đang hoạt động.", "CYCLE_PROGRAM_NOT_FOUND", "CycleCountProgram");

            var today = VietnamTime.Now.Date;
            var due = await _db.CycleCountSchedules
                .Include(s => s.Item)
                .Where(s => s.ProgramId == programId
                    && s.IsActive
                    && (!s.NextScheduledAt.HasValue || s.NextScheduledAt.Value.Date <= today)
                    && !_db.StockCountLines.Any(line =>
                        line.ItemId == s.ItemId
                        && line.OwnerPartnerId == s.OwnerPartnerId
                        && line.LocationId == s.LocationId
                        && line.StockCountSheet != null
                        && line.StockCountSheet.WarehouseId == program.WarehouseId
                        && (line.StockCountSheet.Status == StockCountStatusEnum.Draft
                            || line.StockCountSheet.Status == StockCountStatusEnum.Counting
                            || line.StockCountSheet.Status == StockCountStatusEnum.Counted)))
                .OrderBy(s => s.AbcClass)
                .ThenByDescending(s => (double)(s.CumulativeVariance ?? 0m))
                .ThenBy(s => s.LocationId)
                .Take(Math.Clamp(maxLines, 1, 500))
                .ToListAsync(ct);

            if (due.Count == 0)
                throw new BusinessRuleException("Hiện không có lịch kiểm kê nào đến hạn.", "CYCLE_NO_DUE_LINES", "CycleCountSchedule");

            var sheet = new StockCountSheet
            {
                SheetCode = $"CC-{today:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                WarehouseId = program.WarehouseId,
                CountDate = today,
                Status = StockCountStatusEnum.Draft,
                CreatedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
                CreatedAt = VietnamTime.Now,
                Notes = $"Kiểm kê định kỳ: {program.ProgramName}; kiểm kê ẩn={program.IsBlindCount}"
            };
            _db.StockCountSheets.Add(sheet);
            await _unitOfWork.SaveChangesAsync();

            foreach (var schedule in due)
            {
                var stockRows = await _db.ItemLocations
                    .AsNoTracking()
                    .Where(il => il.ItemId == schedule.ItemId
                        && il.OwnerPartnerId == schedule.OwnerPartnerId
                        && il.LocationId == schedule.LocationId)
                    .Select(il => new { il.LotNumber, il.ExpiryDate, il.Quantity })
                    .ToListAsync(ct);
                var stockBatches = stockRows
                    .GroupBy(il => new { il.LotNumber, il.ExpiryDate })
                    .Select(g => new
                    {
                        g.Key.LotNumber,
                        g.Key.ExpiryDate,
                        SystemQty = g.Sum(il => il.Quantity)
                    })
                    .ToList();

                if (stockBatches.Count == 0)
                {
                    stockBatches.Add(new
                    {
                        LotNumber = (string?)null,
                        ExpiryDate = (DateTime?)null,
                        SystemQty = 0m
                    });
                }

                foreach (var batch in stockBatches)
                {
                    _db.StockCountLines.Add(new StockCountLine
                    {
                        StockCountSheetId = sheet.StockCountSheetId,
                        ItemId = schedule.ItemId,
                        OwnerPartnerId = schedule.OwnerPartnerId,
                        LocationId = schedule.LocationId,
                        LotNumber = batch.LotNumber,
                        ExpiryDate = batch.ExpiryDate,
                        SystemQty = batch.SystemQty,
                        CountedQty = null,
                        Variance = null,
                        Status = 1
                    });
                }
                schedule.CountAttempt++;
            }

            program.LastRunAt = today;
            program.NextRunAt = today.AddDays(Math.Min(program.FrequencyA, Math.Min(program.FrequencyB, program.FrequencyC)));
            await _unitOfWork.SaveChangesAsync();
            if (startedTransaction)
                await _unitOfWork.CommitAsync(ct);
            return sheet;
        }
        catch
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
    }

    public async Task<StockCountSheet> GenerateRecommendationSheetAsync(
        CycleCountRecommendationSheetRequest request,
        CancellationToken ct = default)
    {
        var startedTransaction = !_unitOfWork.HasActiveTransaction;
        if (startedTransaction)
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var locationIsValid = await _db.Locations
                .AsNoTracking()
                .AnyAsync(location => location.LocationId == request.LocationId
                    && location.Zone != null
                    && location.Zone.WarehouseId == request.WarehouseId, ct);
            if (!locationIsValid)
                throw new BusinessRuleException(
                    "Vị trí kiểm kê không thuộc kho đã chọn.",
                    "AI_COUNT_LOCATION_SCOPE_MISMATCH",
                    nameof(Location));

            var itemIsActive = await _db.Items
                .AsNoTracking()
                .AnyAsync(item => item.ItemId == request.ItemId && item.IsActive, ct);
            if (!itemIsActive)
                throw new BusinessRuleException(
                    "Vật tư trong đề xuất không còn hoạt động.",
                    "AI_COUNT_ITEM_INACTIVE",
                    nameof(Item));

            var activeDuplicate = await _db.StockCountLines
                .AsNoTracking()
                .AnyAsync(line => line.ItemId == request.ItemId
                    && line.OwnerPartnerId == request.OwnerPartnerId
                    && line.LocationId == request.LocationId
                    && line.LotNumber == request.LotNumber
                    && line.ExpiryDate == request.ExpiryDate
                    && line.StockCountSheet != null
                    && line.StockCountSheet.WarehouseId == request.WarehouseId
                    && (line.StockCountSheet.Status == StockCountStatusEnum.Draft
                        || line.StockCountSheet.Status == StockCountStatusEnum.Counting
                        || line.StockCountSheet.Status == StockCountStatusEnum.Counted), ct);
            if (activeDuplicate)
                throw new BusinessRuleException(
                    "Đã có phiếu kiểm kê đang hoạt động cho vật tư tại vị trí này.",
                    "AI_COUNT_ACTIVE_DUPLICATE",
                    nameof(StockCountSheet));

            var currentQuantities = await _db.ItemLocations
                .AsNoTracking()
                .Where(row => row.ItemId == request.ItemId
                    && row.OwnerPartnerId == request.OwnerPartnerId
                    && row.LocationId == request.LocationId
                    && row.LotNumber == request.LotNumber
                    && row.ExpiryDate == request.ExpiryDate)
                .Select(row => row.Quantity)
                .ToListAsync(ct);
            var currentSystemQty = currentQuantities.Sum();
            if (Math.Abs(currentSystemQty - request.ExpectedSystemQty) > 0.0001m)
                throw new BusinessRuleException(
                    "Tồn kho đã thay đổi sau thời điểm chấm điểm. Vui lòng chấm lại trước khi tạo phiếu kiểm kê.",
                    "AI_COUNT_SNAPSHOT_STALE",
                    nameof(ItemLocation));

            var now = VietnamTime.Now;
            var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var sheet = new StockCountSheet
            {
                SheetCode = $"CC-AI-{now:yyyyMMdd}-{suffix}",
                WarehouseId = request.WarehouseId,
                CountDate = now.Date,
                Status = StockCountStatusEnum.Draft,
                CreatedBy = string.IsNullOrWhiteSpace(request.Actor) ? "system" : request.Actor.Trim(),
                CreatedAt = now,
                Notes = $"Đề xuất kiểm kê #{request.RecommendationId}; model={request.ModelVersion}; blind={request.IsBlindCount}; cutoff={request.PredictionCutoff:O}"
            };
            _db.StockCountSheets.Add(sheet);
            await _unitOfWork.SaveChangesAsync();

            _db.StockCountLines.Add(new StockCountLine
            {
                StockCountSheetId = sheet.StockCountSheetId,
                ItemId = request.ItemId,
                OwnerPartnerId = request.OwnerPartnerId,
                LocationId = request.LocationId,
                LotNumber = request.LotNumber,
                ExpiryDate = request.ExpiryDate,
                SystemQty = currentSystemQty,
                CountedQty = null,
                Variance = null,
                Status = 1
            });
            await _unitOfWork.SaveChangesAsync();

            if (startedTransaction)
                await _unitOfWork.CommitAsync(ct);
            return sheet;
        }
        catch
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (startedTransaction && _unitOfWork.HasActiveTransaction)
                await _unitOfWork.RollbackAsync(CancellationToken.None);
        }
    }

    public async Task<int> CompleteApprovedSheetAsync(long stockCountSheetId, CancellationToken ct = default)
    {
        var sheet = await _db.StockCountSheets
            .AsNoTracking()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.StockCountSheetId == stockCountSheetId
                && s.Status == StockCountStatusEnum.Approved
                && s.ApprovedAt.HasValue, ct)
            ?? throw new BusinessRuleException(
                "Không tìm thấy phiếu kiểm kê đã được duyệt.",
                "CYCLE_SHEET_NOT_APPROVED",
                "StockCountSheet");

        var itemIds = sheet.Lines.Select(line => line.ItemId).Distinct().ToList();
        var locationIds = sheet.Lines.Select(line => line.LocationId).Distinct().ToList();
        if (itemIds.Count == 0 || locationIds.Count == 0)
            return 0;

        var linesByScheduleKey = sheet.Lines
            .GroupBy(line => (line.ItemId, line.OwnerPartnerId, line.LocationId))
            .ToDictionary(
                group => group.Key,
                group => group.Sum(line => Math.Abs(line.Variance
                    ?? (line.CountedQty.HasValue ? line.CountedQty.Value - line.SystemQty : 0m))));

        var schedules = await _db.CycleCountSchedules
            .Include(schedule => schedule.Program)
            .Where(schedule => schedule.IsActive
                && schedule.Program != null
                && schedule.Program.IsActive
                && schedule.Program.WarehouseId == sheet.WarehouseId
                && itemIds.Contains(schedule.ItemId)
                && locationIds.Contains(schedule.LocationId))
            .ToListAsync(ct);

        var approvedAt = sheet.ApprovedAt!.Value;
        var changed = 0;
        foreach (var schedule in schedules)
        {
            if (!linesByScheduleKey.TryGetValue(
                    (schedule.ItemId, schedule.OwnerPartnerId, schedule.LocationId),
                    out var absoluteVariance))
                continue;

            // The exact approval timestamp is the idempotency marker for this count result.
            if (schedule.LastCountedAt.HasValue && schedule.LastCountedAt.Value >= approvedAt)
                continue;

            schedule.LastCountedAt = approvedAt;
            schedule.NextScheduledAt = approvedAt.Date.AddDays(FrequencyFor(schedule.Program!, schedule.AbcClass));
            schedule.CumulativeVariance = (schedule.CumulativeVariance ?? 0m) + absoluteVariance;
            changed++;
        }

        if (changed > 0)
            await _unitOfWork.SaveChangesAsync(ct);

        return changed;
    }

    private static char ResolveAbc(Item? item)
    {
        if (string.IsNullOrWhiteSpace(item?.AbcClass))
            return 'C';
        var c = char.ToUpperInvariant(item.AbcClass[0]);
        return c is 'A' or 'B' or 'C' ? c : 'C';
    }

    private static int FrequencyFor(CycleCountProgram program, char abc)
        => abc switch
        {
            'A' => Math.Max(1, program.FrequencyA),
            'B' => Math.Max(1, program.FrequencyB),
            _ => Math.Max(1, program.FrequencyC)
        };
}

public sealed class ReturnRmaLineRequest
{
    public int ItemId { get; init; }
    public decimal Quantity { get; init; }
    public int BaseUomId { get; init; }
    public int LocationId { get; init; }
    public string? LotNumber { get; init; }
    public DateTime? ExpiryDate { get; init; }
}

public sealed class ReturnRmaRequest
{
    public long? OriginalOutboundVoucherId { get; init; }
    public int WarehouseId { get; init; }
    public int? CustomerPartnerId { get; init; }
    public int? OwnerPartnerId { get; init; }
    public string Reason { get; init; } = "";
    public string Actor { get; init; } = "system";
    public List<ReturnRmaLineRequest> Lines { get; init; } = new();
}

public sealed record ReturnRmaDispositionResult(long ReturnVoucherId, decimal RestockedQty, InventoryHoldStatusEnum ResultStatus);

public interface IReturnRmaService
{
    Task<Voucher> CreateReturnAsync(ReturnRmaRequest request, CancellationToken ct = default);
    Task<ReturnRmaDispositionResult> DispositionAsync(long returnVoucherId, QcDispositionEnum disposition, string actor, string reason, CancellationToken ct = default);
}

public sealed class ReturnRmaService : IReturnRmaService
{
    private readonly AppDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInventoryBalanceService _inventoryBalanceService;
    private readonly IInventoryTransactionService _inventoryTransactionService;

    public ReturnRmaService(
        AppDbContext db,
        IUnitOfWork unitOfWork,
        IInventoryBalanceService inventoryBalanceService,
        IInventoryTransactionService? inventoryTransactionService = null)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _inventoryBalanceService = inventoryBalanceService;
        _inventoryTransactionService = inventoryTransactionService ?? new InventoryTransactionService(db);
    }

    public async Task<Voucher> CreateReturnAsync(ReturnRmaRequest request, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("Phiếu hàng trả phải có ít nhất một dòng vật tư.", "RMA_LINES_REQUIRED", "Voucher");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Vui lòng nhập lý do trả hàng.", "RMA_REASON_REQUIRED", "Voucher");
        var normalizedQuantities = request.Lines
            .Select(line => VoucherQuantityPrecision.RoundTransaction(line.Quantity))
            .ToArray();
        if (normalizedQuantities.Any(quantity => quantity <= 0))
            throw new BusinessRuleException("Số lượng trên mỗi dòng hàng trả phải lớn hơn 0.", "RMA_QTY_INVALID", "VoucherDetail");
        if (request.Lines.Any(line => line.ItemId <= 0 || line.BaseUomId <= 0 || line.LocationId <= 0))
            throw new BusinessRuleException("Mỗi dòng hàng trả phải có vật tư, đơn vị tính và vị trí tiếp nhận hợp lệ.", "RMA_LINE_REFERENCE_INVALID", "VoucherDetail");

        await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var warehouseExists = await _db.Warehouses
                .AsNoTracking()
                .AnyAsync(w => w.WarehouseId == request.WarehouseId && w.IsActive, ct);
            if (!warehouseExists)
                throw new BusinessRuleException("Kho tiếp nhận hàng trả không hợp lệ hoặc đã ngừng hoạt động.", "RMA_WAREHOUSE_INVALID", "Warehouse");

            if (request.OriginalOutboundVoucherId.HasValue)
            {
                var originalIsValid = await _db.Vouchers
                    .AsNoTracking()
                    .AnyAsync(v => v.VoucherId == request.OriginalOutboundVoucherId.Value
                        && v.WarehouseId == request.WarehouseId
                        && v.IsPosted
                        && !v.IsCancelled
                        && (v.VoucherType == VoucherTypeEnum.XuatKho
                            || v.VoucherType == VoucherTypeEnum.TraNCC
                            || v.VoucherType == VoucherTypeEnum.XuatSanXuat), ct);
                if (!originalIsValid)
                    throw new BusinessRuleException("Phiếu xuất gốc không hợp lệ hoặc không thuộc phạm vi của phiếu hàng trả này.", "RMA_ORIGINAL_VOUCHER_INVALID", "Voucher");
            }

            var itemIds = request.Lines.Select(line => line.ItemId).Distinct().ToList();
            var validItems = await _db.Items
                .AsNoTracking()
                .Where(item => itemIds.Contains(item.ItemId) && item.IsActive)
                .Select(item => new { item.ItemId, item.BaseUomId })
                .ToDictionaryAsync(item => item.ItemId, ct);
            var invalidItemLine = request.Lines.FirstOrDefault(line =>
                !validItems.TryGetValue(line.ItemId, out var item) || item.BaseUomId != line.BaseUomId);
            if (invalidItemLine != null)
                throw new BusinessRuleException("Vật tư hoặc đơn vị tính cơ sở trên phiếu hàng trả không hợp lệ.", "RMA_ITEM_UOM_INVALID", "VoucherDetail");

            var locationIds = request.Lines.Select(line => line.LocationId).Distinct().ToList();
            var validLocationCount = await _db.Locations
                .AsNoTracking()
                .CountAsync(location => locationIds.Contains(location.LocationId)
                    && location.IsActive
                    && location.Zone != null
                    && location.Zone.IsActive
                    && location.Zone.WarehouseId == request.WarehouseId, ct);
            if (validLocationCount != locationIds.Count)
                throw new BusinessRuleException("Vị trí tiếp nhận hàng trả không hợp lệ hoặc không thuộc kho đã chọn.", "RMA_LOCATION_INVALID", "Location");

            var actor = string.IsNullOrWhiteSpace(request.Actor) ? "system" : request.Actor.Trim();
            var voucher = new Voucher
            {
                VoucherCode = await GenerateReturnCodeAsync(ct),
                VoucherType = VoucherTypeEnum.KhachTra,
                WarehouseId = request.WarehouseId,
                PartnerId = request.CustomerPartnerId,
                OwnerPartnerId = request.OwnerPartnerId,
                ParentVoucherId = request.OriginalOutboundVoucherId,
                VoucherDate = VietnamTime.Now.Date,
                SourceType = SourceTypeEnum.Manual,
                Description = request.Reason.Trim(),
                CreatedBy = actor,
                CreatedAt = VietnamTime.Now,
                InboundStatus = InboundStatusEnum.Receiving,
                ReviewResult = ReviewResultEnum.Pending,
                TotalLines = request.Lines.Count
            };
            _db.Vouchers.Add(voucher);

            var lineNo = 0;
            foreach (var (line, index) in request.Lines.Select((value, index) => (value, index)))
            {
                lineNo++;
                var normalizedQuantity = normalizedQuantities[index];
                voucher.Details.Add(new VoucherDetail
                {
                    ItemId = line.ItemId,
                    OwnerPartnerId = request.OwnerPartnerId,
                    LocationId = line.LocationId,
                    TransactionQty = normalizedQuantity,
                    TransactionUomId = line.BaseUomId,
                    ConversionRate = 1m,
                    BaseQty = normalizedQuantity,
                    QualityStatus = QualityStatusEnum.Pending,
                    LotNumber = line.LotNumber,
                    ExpiryDate = line.ExpiryDate,
                    LineNumber = lineNo,
                    Notes = "Hàng trả đang chờ kiểm tra chất lượng"
                });
            }

            await _unitOfWork.SaveChangesAsync(ct);
            _db.AuditLogs.Add(new AuditLog
            {
                TableName = "Voucher",
                RecordId = voucher.VoucherId.ToString(),
                ActionType = "RMA_CREATE",
                NewValue = $"Parent:{request.OriginalOutboundVoucherId};Lines:{lineNo}",
                ChangedBy = actor,
                ChangedAt = VietnamTime.Now,
                AppModule = "ReturnsRMA"
            });
            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitAsync(ct);
            return voucher;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ReturnRmaDispositionResult> DispositionAsync(long returnVoucherId, QcDispositionEnum disposition, string actor, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException("Vui lòng nhập lý do xử lý hàng trả.", "RMA_DISPOSITION_REASON_REQUIRED", "Voucher");

        await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var voucher = await _db.Vouchers
                .Include(v => v.Details)
                .FirstOrDefaultAsync(v => v.VoucherId == returnVoucherId && v.VoucherType == VoucherTypeEnum.KhachTra, ct)
                ?? throw new BusinessRuleException("Không tìm thấy phiếu hàng trả.", "RMA_NOT_FOUND", "Voucher");

            if (voucher.IsCancelled)
                throw new BusinessRuleException("Phiếu hàng trả đã bị hủy.", "RMA_CANCELLED", "Voucher");
            if (voucher.IsPosted || voucher.InboundStatus == InboundStatusEnum.Completed)
                throw new BusinessRuleException("Phiếu hàng trả đã được xử lý trước đó.", "RMA_ALREADY_DISPOSITIONED", "Voucher");
            if (voucher.InboundStatus != InboundStatusEnum.Receiving)
                throw new BusinessRuleException("Phiếu hàng trả phải ở bước tiếp nhận trước khi xử lý kết quả kiểm tra chất lượng.", "RMA_STATE_INVALID", "Voucher");

            var transactionDate = VietnamTime.Now.Date;
            var activeLockDate = await _db.WarehousePeriodLocks
                .AsNoTracking()
                .Where(l => l.WarehouseId == voucher.WarehouseId && l.IsActive)
                .OrderByDescending(l => l.LockDate)
                .Select(l => (DateTime?)l.LockDate)
                .FirstOrDefaultAsync(ct);
            if (activeLockDate.HasValue && transactionDate <= activeLockDate.Value.Date)
            {
                throw new BusinessRuleException(
                    $"Kho đã khóa kỳ đến {activeLockDate:dd/MM/yyyy}. Không thể xử lý hàng trả trong ngày {transactionDate:dd/MM/yyyy}.",
                    "RMA_PERIOD_LOCKED",
                    "WarehousePeriodLock");
            }

            var targetStatus = disposition switch
            {
                QcDispositionEnum.Accept or QcDispositionEnum.AcceptWithConditions => InventoryHoldStatusEnum.Available,
                QcDispositionEnum.Hold or QcDispositionEnum.Rework => InventoryHoldStatusEnum.QcHold,
                QcDispositionEnum.Scrap or QcDispositionEnum.Reject or QcDispositionEnum.ReturnToSupplier => InventoryHoldStatusEnum.Blocked,
                _ => InventoryHoldStatusEnum.Quarantine
            };

            using var ledgerScope = _inventoryTransactionService.BeginScope(new InventoryTransactionContext
            {
                TransactionType = InventoryTransactionTypeEnum.Receive,
                TransactionGroupKey = $"rma:{voucher.VoucherId}:disposition",
                IdempotencyKeyPrefix = $"rma:{voucher.VoucherId}:disposition",
                WarehouseId = voucher.WarehouseId,
                OwnerPartnerId = voucher.OwnerPartnerId,
                VoucherId = voucher.VoucherId,
                ReferenceType = "ReturnRma",
                ReferenceId = voucher.VoucherId.ToString(),
                ReferenceCode = voucher.VoucherCode,
                Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim()
            });

            var restockedQty = 0m;
            var affectedItems = new HashSet<int>();
            foreach (var detail in voucher.Details)
            {
                if (detail.BaseQty <= 0)
                    throw new BusinessRuleException("Số lượng trên dòng hàng trả phải lớn hơn 0.", "RMA_QTY_INVALID", "VoucherDetail");
                if (!detail.LocationId.HasValue || detail.LocationId.Value <= 0)
                    throw new BusinessRuleException("Dòng hàng trả chưa có vị trí tiếp nhận.", "RMA_LOCATION_REQUIRED", "VoucherDetail");
                if (detail.OwnerPartnerId.HasValue && detail.OwnerPartnerId != voucher.OwnerPartnerId)
                    throw new BusinessRuleException("Chủ hàng trên dòng chi tiết không khớp với chủ hàng của phiếu trả.", "RMA_OWNER_MISMATCH", "VoucherDetail");

                detail.OwnerPartnerId ??= voucher.OwnerPartnerId;
                var locationBelongsToWarehouse = await _db.Locations
                    .AsNoTracking()
                    .AnyAsync(l => l.LocationId == detail.LocationId.Value
                        && l.IsActive
                        && l.Zone != null
                        && l.Zone.IsActive
                        && l.Zone.WarehouseId == voucher.WarehouseId, ct);
                if (!locationBelongsToWarehouse)
                    throw new BusinessRuleException("Vị trí tiếp nhận hàng trả không hợp lệ hoặc không thuộc kho của phiếu.", "RMA_LOCATION_INVALID", "Location");

                if (targetStatus != InventoryHoldStatusEnum.Blocked)
                {
                    await LocationStoragePolicy.EnsureStorageLocationCanAcceptAsync(
                        _db,
                        detail.LocationId.Value,
                        detail.ItemId,
                        voucher.OwnerPartnerId,
                        ct);
                }

                detail.QualityStatus = targetStatus == InventoryHoldStatusEnum.Available
                    ? QualityStatusEnum.Passed
                    : targetStatus == InventoryHoldStatusEnum.Blocked
                        ? QualityStatusEnum.Failed
                        : QualityStatusEnum.OnHold;

                if (targetStatus == InventoryHoldStatusEnum.Blocked)
                    continue;

                var row = await _db.ItemLocations.FirstOrDefaultAsync(il =>
                    il.ItemId == detail.ItemId
                    && il.OwnerPartnerId == detail.OwnerPartnerId
                    && il.LocationId == detail.LocationId.Value
                    && il.LotNumber == detail.LotNumber
                    && il.ExpiryDate == detail.ExpiryDate
                    && il.HoldStatus == targetStatus, ct);
                if (row == null)
                {
                    row = new ItemLocation
                    {
                        ItemId = detail.ItemId,
                        OwnerPartnerId = detail.OwnerPartnerId,
                        LocationId = detail.LocationId.Value,
                        LotNumber = detail.LotNumber,
                        ExpiryDate = detail.ExpiryDate,
                        HoldStatus = targetStatus,
                        UpdatedAt = VietnamTime.Now
                    };
                    _db.ItemLocations.Add(row);
                }

                row.Quantity += detail.BaseQty;
                row.UpdatedAt = VietnamTime.Now;
                restockedQty += detail.BaseQty;
                affectedItems.Add(detail.ItemId);
            }

            voucher.IsPosted = true;
            voucher.InboundStatus = InboundStatusEnum.Completed;
            voucher.CompletedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
            voucher.CompletedAt = VietnamTime.Now;
            voucher.Description = $"{voucher.Description}; hướng xử lý hàng trả={disposition}; lý do={reason.Trim()}";

            _db.AuditLogs.Add(new AuditLog
            {
                TableName = "Voucher",
                RecordId = voucher.VoucherId.ToString(),
                ActionType = "RMA_QC",
                OldValue = "PendingQC",
                NewValue = $"{disposition};Restocked:{restockedQty:N4};Status:{targetStatus}",
                ChangedBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim(),
                ChangedAt = VietnamTime.Now,
                AppModule = "ReturnsRMA"
            });

            await _unitOfWork.SaveChangesAsync(ct);
            if (affectedItems.Count > 0)
            {
                await _inventoryBalanceService.SyncCurrentStockAsync(affectedItems);
                await _unitOfWork.SaveChangesAsync(ct);
            }

            await _unitOfWork.CommitAsync(ct);
            return new ReturnRmaDispositionResult(returnVoucherId, restockedQty, targetStatus);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<string> GenerateReturnCodeAsync(CancellationToken ct)
    {
        var prefix = $"RMA-{VietnamTime.Now:yyyyMMdd}-";
        var count = await _db.Vouchers.CountAsync(v => v.VoucherCode.StartsWith(prefix), ct);
        return $"{prefix}{count + 1:D5}";
    }
}

public sealed class CartonOption
{
    public string Code { get; init; } = "";
    public string PackageType { get; init; } = "";
    public decimal MaxWeightKg { get; init; }
    public decimal LengthCm { get; init; }
    public decimal WidthCm { get; init; }
    public decimal HeightCm { get; init; }
    public int? OwnerPartnerId { get; init; }
}

public sealed class CartonizationRecommendation
{
    public string PackageType { get; init; } = "";
    public int PackageCount { get; init; }
    public decimal EstimatedWeightKg { get; init; }
    public decimal EstimatedVolumeCbm { get; init; }
    public string Reason { get; init; } = "";
}

public interface ICartonizationService
{
    Task<CartonizationRecommendation> RecommendAsync(long voucherId, IReadOnlyCollection<CartonOption>? options = null, CancellationToken ct = default);
    string BuildOverrideNote(CartonizationRecommendation recommendation, string chosenPackageType, string overrideReason);
}

public sealed class CartonizationService : ICartonizationService
{
    private readonly AppDbContext _db;

    public CartonizationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CartonizationRecommendation> RecommendAsync(long voucherId, IReadOnlyCollection<CartonOption>? options = null, CancellationToken ct = default)
    {
        var voucher = await _db.Vouchers
            .AsNoTracking()
            .Include(v => v.Details)!.ThenInclude(d => d.Item)
            .FirstOrDefaultAsync(v => v.VoucherId == voucherId, ct)
            ?? throw new BusinessRuleException("Không tìm thấy phiếu cần đề xuất đóng gói.", "CARTON_VOUCHER_NOT_FOUND", "Voucher");

        var lines = voucher.Details.Where(d => d.BaseQty != 0m).ToList();
        if (lines.Count == 0)
            throw new BusinessRuleException("Phiếu không có dòng hàng đủ điều kiện đóng gói.", "CARTON_LINES_REQUIRED", "VoucherDetail");

        var totalWeight = lines.Sum(d => Math.Abs(d.BaseQty) * (d.Item?.Weight ?? 1m));
        var totalVolumeCm3 = lines.Sum(d =>
        {
            var item = d.Item;
            var unitVolume = (item?.Length ?? 10m) * (item?.Width ?? 10m) * (item?.Height ?? 10m);
            return Math.Abs(d.BaseQty) * unitVolume;
        });

        var availableOptions = (options == null || options.Count == 0 ? DefaultOptions() : options)
            .Where(o => !o.OwnerPartnerId.HasValue || o.OwnerPartnerId == voucher.OwnerPartnerId)
            .OrderBy(o => o.LengthCm * o.WidthCm * o.HeightCm)
            .ThenBy(o => o.MaxWeightKg)
            .ToList();
        if (availableOptions.Count == 0)
            availableOptions = DefaultOptions();

        var best = availableOptions
            .Select(option =>
            {
                var volumeCapacity = Math.Max(1m, option.LengthCm * option.WidthCm * option.HeightCm);
                var byWeight = option.MaxWeightKg <= 0m ? 1 : (int)Math.Ceiling(totalWeight / option.MaxWeightKg);
                var byVolume = (int)Math.Ceiling(totalVolumeCm3 / volumeCapacity);
                return new { Option = option, Count = Math.Max(1, Math.Max(byWeight, byVolume)), VolumeCapacity = volumeCapacity };
            })
            .OrderBy(x => x.Count)
            .ThenBy(x => x.VolumeCapacity)
            .First();

        return new CartonizationRecommendation
        {
            PackageType = best.Option.PackageType,
            PackageCount = best.Count,
            EstimatedWeightKg = totalWeight,
            EstimatedVolumeCbm = Math.Round(totalVolumeCm3 / 1_000_000m, 6),
            Reason = $"Phù hợp theo khối lượng {totalWeight:N2} kg và thể tích {totalVolumeCm3 / 1_000_000m:N4} m³ với quy cách {best.Option.Code}."
        };
    }

    public string BuildOverrideNote(CartonizationRecommendation recommendation, string chosenPackageType, string overrideReason)
    {
        if (string.IsNullOrWhiteSpace(overrideReason))
            throw new BusinessRuleException("Vui lòng nhập lý do chọn quy cách đóng gói khác với gợi ý.", "CARTON_OVERRIDE_REASON_REQUIRED", "OutboundPackage");

        return $"Hệ thống gợi ý {recommendation.PackageType} x{recommendation.PackageCount}; đã chọn {chosenPackageType}; lý do={overrideReason.Trim()}";
    }

    private static List<CartonOption> DefaultOptions()
        => new()
        {
            new CartonOption { Code = "S", PackageType = "Thùng nhỏ", MaxWeightKg = 5m, LengthCm = 30m, WidthCm = 20m, HeightCm = 15m },
            new CartonOption { Code = "M", PackageType = "Thùng vừa", MaxWeightKg = 15m, LengthCm = 50m, WidthCm = 35m, HeightCm = 30m },
            new CartonOption { Code = "L", PackageType = "Thùng lớn", MaxWeightKg = 30m, LengthCm = 70m, WidthCm = 50m, HeightCm = 45m },
            new CartonOption { Code = "P", PackageType = "Pallet", MaxWeightKg = 800m, LengthCm = 120m, WidthCm = 100m, HeightCm = 120m }
        };
}
