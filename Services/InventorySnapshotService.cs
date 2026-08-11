using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using WMS.Common;
using WMS.Data;
using WMS.Models;

namespace WMS.Services;

public sealed record LpnMovementSnapshotRequest(
    long LicensePlateId,
    int WarehouseId,
    int SourceLocationId,
    int DestinationLocationId,
    string IdempotencyKey,
    string Actor,
    string? Reason = null);

public interface IInventorySnapshotService
{
    Task<IReadOnlyCollection<int>> RecordAndApplyLpnMovedAsync(LpnMovementSnapshotRequest request, CancellationToken ct = default);
    Task ProcessOutboxBatchAsync(int batchSize = 100, CancellationToken ct = default);
}

public interface IInventoryReconciliationService
{
    Task<InventoryReconciliationRun> RunAsync(int? warehouseId = null, decimal toleranceQty = 0.0001m, CancellationToken ct = default);
}

public class InventorySnapshotService : IInventorySnapshotService, IInventoryReconciliationService
{
    private const int MaxOutboxRetries = 5;

    private static readonly LpnStatusEnum[] SnapshotStatuses =
    {
        LpnStatusEnum.Stored,
        LpnStatusEnum.Allocated,
        LpnStatusEnum.Picked,
        LpnStatusEnum.Packed
    };

    private readonly AppDbContext _db;
    private readonly IInventoryBalanceService _balanceService;
    private readonly IInventoryTransactionService _inventoryTransactionService;
    private readonly ILogger<InventorySnapshotService> _logger;

    public InventorySnapshotService(
        AppDbContext db,
        IInventoryBalanceService? balanceService = null,
        IInventoryTransactionService? inventoryTransactionService = null,
        ILogger<InventorySnapshotService>? logger = null)
    {
        _db = db;
        _balanceService = balanceService ?? new InventoryBalanceService(db);
        _inventoryTransactionService = inventoryTransactionService ?? new InventoryTransactionService(db);
        _logger = logger ?? NullLogger<InventorySnapshotService>.Instance;
    }

    private static DateTime Now => VietnamTime.Now;

    public async Task<IReadOnlyCollection<int>> RecordAndApplyLpnMovedAsync(LpnMovementSnapshotRequest request, CancellationToken ct = default)
    {
        if (request.SourceLocationId == request.DestinationLocationId)
            return Array.Empty<int>();

        var existing = await _db.InventorySnapshotOutbox
            .FirstOrDefaultAsync(o => o.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing?.Status == InventorySnapshotOutboxStatusEnum.Processed)
            return Array.Empty<int>();

        var outbox = existing ?? new InventorySnapshotOutbox
        {
            EventType = InventorySnapshotEventTypeEnum.LpnMoved,
            LicensePlateId = request.LicensePlateId,
            WarehouseId = request.WarehouseId,
            SourceLocationId = request.SourceLocationId,
            DestinationLocationId = request.DestinationLocationId,
            IdempotencyKey = request.IdempotencyKey,
            CreatedBy = request.Actor,
            CreatedAt = Now
        };

        outbox.PayloadJson = JsonSerializer.Serialize(new LpnMovedPayload(
            request.LicensePlateId,
            request.WarehouseId,
            request.SourceLocationId,
            request.DestinationLocationId,
            request.Actor,
            request.Reason,
            Now));
        outbox.Status = InventorySnapshotOutboxStatusEnum.Processing;
        outbox.LastError = null;
        outbox.NextAttemptAt = null;

        if (existing == null)
            _db.InventorySnapshotOutbox.Add(outbox);

        try
        {
            var affectedItemIds = await ApplyLpnLocationDeltaAsync(
                request.LicensePlateId,
                request.WarehouseId,
                request.SourceLocationId,
                request.DestinationLocationId,
                ct);

            outbox.Status = InventorySnapshotOutboxStatusEnum.Processed;
            outbox.ProcessedAt = Now;
            return affectedItemIds;
        }
        catch (Exception ex)
        {
            MarkSnapshotFailure(outbox, ex, "Không thể ghi nhận snapshot tồn kho lúc này.");
            throw;
        }
    }

    public async Task ProcessOutboxBatchAsync(int batchSize = 100, CancellationToken ct = default)
    {
        var now = Now;
        var eventIds = await _db.InventorySnapshotOutbox
            .AsNoTracking()
            .Where(o => (o.Status == InventorySnapshotOutboxStatusEnum.Pending || o.Status == InventorySnapshotOutboxStatusEnum.Failed)
                && (o.NextAttemptAt == null || o.NextAttemptAt <= now))
            .OrderBy(o => o.CreatedAt)
            .Take(Math.Clamp(batchSize, 1, 500))
            .Select(o => o.InventorySnapshotOutboxId)
            .ToListAsync(ct);

        foreach (var eventId in eventIds)
        {
            IDbContextTransaction? transaction = null;
            try
            {
                if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
                    transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                InventorySnapshotOutbox? evt;
                if (_db.Database.IsRelational())
                {
                    var claimed = await _db.InventorySnapshotOutbox
                        .Where(o => o.InventorySnapshotOutboxId == eventId
                            && (o.Status == InventorySnapshotOutboxStatusEnum.Pending || o.Status == InventorySnapshotOutboxStatusEnum.Failed)
                            && (o.NextAttemptAt == null || o.NextAttemptAt <= now))
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(o => o.Status, InventorySnapshotOutboxStatusEnum.Processing)
                            .SetProperty(o => o.LastError, (string?)null), ct);
                    if (claimed == 0)
                    {
                        if (transaction != null)
                            await transaction.CommitAsync(ct);
                        continue;
                    }

                    evt = await _db.InventorySnapshotOutbox.FirstOrDefaultAsync(o => o.InventorySnapshotOutboxId == eventId, ct);
                }
                else
                {
                    evt = await _db.InventorySnapshotOutbox.FirstOrDefaultAsync(o => o.InventorySnapshotOutboxId == eventId, ct);
                    if (evt == null
                        || (evt.Status != InventorySnapshotOutboxStatusEnum.Pending && evt.Status != InventorySnapshotOutboxStatusEnum.Failed)
                        || (evt.NextAttemptAt.HasValue && evt.NextAttemptAt > now))
                    {
                        continue;
                    }
                    evt.Status = InventorySnapshotOutboxStatusEnum.Processing;
                }

                if (evt == null)
                    continue;

                evt.LastError = null;
                using var ledgerScope = _inventoryTransactionService.BeginScope(new InventoryTransactionContext
                {
                    TransactionType = InventoryTransactionTypeEnum.Move,
                    TransactionGroupKey = $"snapshot-outbox:{evt.InventorySnapshotOutboxId}:{evt.IdempotencyKey}",
                    IdempotencyKeyPrefix = $"snapshot-outbox:{evt.InventorySnapshotOutboxId}:{evt.IdempotencyKey}",
                    WarehouseId = evt.WarehouseId,
                    LicensePlateId = evt.LicensePlateId,
                    ReferenceType = "InventorySnapshotOutbox",
                    ReferenceId = evt.InventorySnapshotOutboxId.ToString(),
                    ReferenceCode = evt.IdempotencyKey,
                    Actor = evt.CreatedBy
                });
                IReadOnlyCollection<int> affectedItemIds = evt.EventType switch
                {
                    InventorySnapshotEventTypeEnum.LpnMoved when evt.LicensePlateId.HasValue && evt.SourceLocationId.HasValue && evt.DestinationLocationId.HasValue
                        => await ApplyLpnLocationDeltaAsync(evt.LicensePlateId.Value, evt.WarehouseId, evt.SourceLocationId.Value, evt.DestinationLocationId.Value, ct),
                    _ => Array.Empty<int>()
                };

                evt.Status = InventorySnapshotOutboxStatusEnum.Processed;
                evt.ProcessedAt = Now;
                evt.NextAttemptAt = null;
                await _db.SaveChangesAsync(ct);

                if (affectedItemIds.Count > 0)
                {
                    await _balanceService.SyncCurrentStockAsync(affectedItemIds);
                    await _db.SaveChangesAsync(ct);
                }

                if (transaction != null)
                    await transaction.CommitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (transaction != null)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inventory snapshot outbox event {EventId} failed", eventId);
                if (transaction != null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    await transaction.DisposeAsync();
                    transaction = null;
                    _db.ChangeTracker.Clear();
                }

                var failedEvent = await _db.InventorySnapshotOutbox.FirstOrDefaultAsync(o => o.InventorySnapshotOutboxId == eventId, ct);
                if (failedEvent == null || failedEvent.Status == InventorySnapshotOutboxStatusEnum.Processed)
                    continue;

                MarkSnapshotFailure(failedEvent, ex, "Không thể xử lý snapshot tồn kho lúc này.");
                await _db.SaveChangesAsync(ct);
            }
            finally
            {
                if (transaction != null)
                    await transaction.DisposeAsync();
            }
        }
    }

    private static void MarkSnapshotFailure(InventorySnapshotOutbox outbox, Exception exception, string fallbackMessage)
    {
        outbox.RetryCount += 1;
        outbox.LastError = UserSafeError.From(exception, fallbackMessage);
        if (outbox.RetryCount >= MaxOutboxRetries)
        {
            outbox.Status = InventorySnapshotOutboxStatusEnum.DeadLetter;
            outbox.NextAttemptAt = null;
            return;
        }

        outbox.Status = InventorySnapshotOutboxStatusEnum.Failed;
        outbox.NextAttemptAt = Now.AddMinutes(Math.Min(30, Math.Max(1, outbox.RetryCount * 2)));
    }

    public async Task<InventoryReconciliationRun> RunAsync(int? warehouseId = null, decimal toleranceQty = 0.0001m, CancellationToken ct = default)
    {
        var run = new InventoryReconciliationRun
        {
            WarehouseId = warehouseId,
            StartedAt = Now,
            ToleranceQty = toleranceQty,
            Status = InventoryReconciliationRunStatusEnum.Running
        };
        _db.InventoryReconciliationRuns.Add(run);
        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational() && _db.Database.CurrentTransaction == null)
            transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var expectedRows = await BuildExpectedSnapshotAsync(warehouseId, ct);
            var actualRows = await BuildActualSnapshotAsync(warehouseId, ct);
            var actualByKey = actualRows.ToDictionary(x => x.Key, x => x.Row);
            var affectedItemIds = new HashSet<int>();
            using var ledgerScope = _inventoryTransactionService.BeginScope(new InventoryTransactionContext
            {
                TransactionType = InventoryTransactionTypeEnum.Reconcile,
                TransactionGroupKey = $"reconciliation:{run.StartedAt:yyyyMMddHHmmss}:{warehouseId?.ToString() ?? "all"}",
                IdempotencyKeyPrefix = $"reconciliation:{run.StartedAt:yyyyMMddHHmmss}:{warehouseId?.ToString() ?? "all"}",
                WarehouseId = warehouseId,
                ReferenceType = "InventoryReconciliationRun",
                ReferenceCode = $"RUN-{run.StartedAt:yyyyMMddHHmmss}",
                Actor = "reconciliation"
            });

            run.ExpectedRowCount = expectedRows.Count;
            run.SnapshotRowCount = actualRows.Count;

            foreach (var expected in expectedRows)
            {
                actualByKey.TryGetValue(expected.Key, out var actualRow);
                var snapshotQty = actualRow?.Quantity ?? 0m;
                var delta = expected.Quantity - snapshotQty;
                // P2-2: dùng tham số tolerance thay vì hard-coded 1e-7; tránh bỏ qua delta nhỏ với UoM gram/ml.
                if (delta == 0m)
                    continue;

                var canAutoHeal = Math.Abs(delta) <= toleranceQty;
                var issue = new InventoryReconciliationIssue
                {
                    WarehouseId = expected.Key.WarehouseId,
                    ItemId = expected.Key.ItemId,
                    LocationId = expected.Key.LocationId,
                    LotNumber = expected.Key.LotNumber,
                    ExpiryDate = expected.Key.ExpiryDate,
                    HoldStatus = expected.Key.HoldStatus,
                    ExpectedQty = expected.Quantity,
                    SnapshotQty = snapshotQty,
                    DeltaQty = delta,
                    Action = canAutoHeal ? InventoryReconciliationActionEnum.AutoHealed : InventoryReconciliationActionEnum.Alert,
                    Severity = canAutoHeal ? InventoryReconciliationSeverityEnum.Info : InventoryReconciliationSeverityEnum.Warning,
                    Message = canAutoHeal
                        ? "Đã tự đồng bộ ảnh chụp tồn từ số dư mã kiện trong ngưỡng dung sai."
                        : "Ảnh chụp tồn lệch số dư mã kiện vượt ngưỡng dung sai; cần kiểm tra thủ công.",
                    CreatedAt = Now
                };
                run.Issues.Add(issue);

                if (canAutoHeal)
                {
                    if (actualRow == null)
                    {
                        actualRow = new ItemLocation
                        {
                            ItemId = expected.Key.ItemId,
                            OwnerPartnerId = expected.Key.OwnerPartnerId, // P0-8
                            LocationId = expected.Key.LocationId,
                            LotNumber = expected.Key.LotNumber,
                            ExpiryDate = expected.Key.ExpiryDate,
                            HoldStatus = expected.Key.HoldStatus,
                            ReservedQty = 0,
                            UpdatedAt = Now
                        };
                        _db.ItemLocations.Add(actualRow);
                    }

                    actualRow.Quantity = expected.Quantity;
                    actualRow.UpdatedAt = Now;
                    affectedItemIds.Add(expected.Key.ItemId);
                    run.AutoHealedCount += 1;
                }
                else
                {
                    run.AlertCount += 1;
                }
            }

            run.IssueCount = run.Issues.Count;
            run.Status = InventoryReconciliationRunStatusEnum.Completed;
            run.CompletedAt = Now;
            await _db.SaveChangesAsync(ct);

            if (affectedItemIds.Count > 0)
            {
                await _balanceService.SyncCurrentStockAsync(affectedItemIds);
                await _db.SaveChangesAsync(ct);
            }

            if (transaction != null)
                await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (transaction != null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inventory reconciliation run failed for warehouse {WarehouseId}", warehouseId);
            var safeError = UserSafeError.From(ex, "Không thể đối soát snapshot tồn kho lúc này.");
            if (transaction != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                transaction = null;
                _db.ChangeTracker.Clear();
                run = new InventoryReconciliationRun
                {
                    WarehouseId = warehouseId,
                    StartedAt = run.StartedAt,
                    CompletedAt = Now,
                    ToleranceQty = toleranceQty,
                    ExpectedRowCount = run.ExpectedRowCount,
                    SnapshotRowCount = run.SnapshotRowCount,
                    Status = InventoryReconciliationRunStatusEnum.Failed,
                    ErrorMessage = safeError
                };
                _db.InventoryReconciliationRuns.Add(run);
            }
            else
            {
                run.Status = InventoryReconciliationRunStatusEnum.Failed;
                run.ErrorMessage = safeError;
                run.CompletedAt = Now;
            }
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }

        return run;
    }

    private async Task<IReadOnlyCollection<int>> ApplyLpnLocationDeltaAsync(long rootLicensePlateId, int warehouseId, int sourceLocationId, int destinationLocationId, CancellationToken ct)
    {
        using var ledgerScope = _db.CurrentInventoryTransactionContext == null
            ? _inventoryTransactionService.BeginScope(new InventoryTransactionContext
            {
                TransactionType = InventoryTransactionTypeEnum.Move,
                TransactionGroupKey = $"lpn:{rootLicensePlateId}:move:{sourceLocationId}:{destinationLocationId}",
                IdempotencyKeyPrefix = $"lpn:{rootLicensePlateId}:move:{sourceLocationId}:{destinationLocationId}",
                WarehouseId = warehouseId,
                LicensePlateId = rootLicensePlateId,
                ReferenceType = "LicensePlate",
                ReferenceId = rootLicensePlateId.ToString(),
                Actor = "system"
            })
            : null;
        var lpnIds = await ResolveLpnTreeIdsAsync(rootLicensePlateId, warehouseId, ct);
        if (lpnIds.Count == 0)
            throw new BusinessRuleException("Không tìm thấy LPN để cập nhật snapshot tồn kho.", "LPN_SNAPSHOT_NOT_FOUND", "LicensePlate");

        var detailRows = await _db.LicensePlateDetails
            .Where(d => lpnIds.Contains(d.LicensePlateId) && d.Quantity > 0)
            .Select(d => new
            {
                d.ItemId,
                d.OwnerPartnerId,
                d.LotNumber,
                d.ExpiryDate,
                d.HoldStatus,
                d.Quantity
            })
            .ToListAsync(ct);

        var groups = detailRows
            .GroupBy(d => new SnapshotKey(warehouseId, sourceLocationId, d.ItemId, d.OwnerPartnerId, d.LotNumber, d.ExpiryDate, d.HoldStatus))
            .Select(g => new
            {
                SourceKey = g.Key,
                DestinationKey = g.Key with { LocationId = destinationLocationId },
                Quantity = g.Sum(x => x.Quantity)
            })
            .ToList();

        var affectedItemIds = new HashSet<int>();
        foreach (var group in groups)
        {
            var source = await FindSnapshotRowAsync(group.SourceKey, ct);
            if (source == null)
                throw new BusinessRuleException("Thiếu dòng snapshot tồn kho tại vị trí nguồn của LPN. Vui lòng chạy đối soát tồn kho trước khi di chuyển LPN này.", "LPN_SOURCE_SNAPSHOT_MISSING", "ItemLocation");
            if (source.Quantity < group.Quantity)
                throw new BusinessRuleException("Snapshot tồn kho tại vị trí nguồn của LPN sẽ bị âm. Vui lòng chạy đối soát tồn kho trước khi di chuyển LPN này.", "LPN_SOURCE_SNAPSHOT_NEGATIVE", "ItemLocation");

            source.Quantity -= group.Quantity;
            source.UpdatedAt = Now;

            var destination = await FindSnapshotRowAsync(group.DestinationKey, ct);
            if (destination == null)
            {
                destination = new ItemLocation
                {
                    ItemId = group.DestinationKey.ItemId,
                    OwnerPartnerId = group.DestinationKey.OwnerPartnerId, // P0-8
                    LocationId = group.DestinationKey.LocationId,
                    Quantity = group.Quantity,
                    ReservedQty = 0,
                    LotNumber = group.DestinationKey.LotNumber,
                    ExpiryDate = group.DestinationKey.ExpiryDate,
                    HoldStatus = group.DestinationKey.HoldStatus,
                    UpdatedAt = Now
                };
                _db.ItemLocations.Add(destination);
            }
            else
            {
                destination.Quantity += group.Quantity;
                destination.UpdatedAt = Now;
            }

            affectedItemIds.Add(group.SourceKey.ItemId);
        }

        return affectedItemIds.ToList();
    }

    private async Task<ItemLocation?> FindSnapshotRowAsync(SnapshotKey key, CancellationToken ct)
        => await _db.ItemLocations.FirstOrDefaultAsync(il =>
            il.ItemId == key.ItemId
            && il.OwnerPartnerId == key.OwnerPartnerId
            && il.LocationId == key.LocationId
            && il.LotNumber == key.LotNumber
            && il.ExpiryDate == key.ExpiryDate
            && il.HoldStatus == key.HoldStatus, ct);

    private async Task<List<long>> ResolveLpnTreeIdsAsync(long rootLicensePlateId, int warehouseId, CancellationToken ct)
    {
        var lpns = await _db.LicensePlates
            .Where(l => l.WarehouseId == warehouseId && l.IsActive && l.Status != LpnStatusEnum.Voided && l.Status != LpnStatusEnum.Shipped)
            .Select(l => new { l.LicensePlateId, l.ParentLpnId })
            .ToListAsync(ct);

        if (!lpns.Any(l => l.LicensePlateId == rootLicensePlateId))
            return new List<long>();

        var childrenByParent = lpns
            .Where(l => l.ParentLpnId.HasValue)
            .GroupBy(l => l.ParentLpnId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.LicensePlateId).ToList());

        var result = new List<long>();
        var stack = new Stack<long>();
        var seen = new HashSet<long>();
        stack.Push(rootLicensePlateId);

        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id))
                continue;

            result.Add(id);
            if (childrenByParent.TryGetValue(id, out var children))
            {
                foreach (var child in children)
                    stack.Push(child);
            }
        }

        return result;
    }

    private async Task<List<ExpectedSnapshotRow>> BuildExpectedSnapshotAsync(int? warehouseId, CancellationToken ct)
    {
        var rows = await _db.LicensePlateDetails
            .AsNoTracking()
            .Where(d => d.Quantity != 0
                && d.LicensePlate != null
                && d.LicensePlate.IsActive
                && SnapshotStatuses.Contains(d.LicensePlate.Status)
                && d.LicensePlate.CurrentLocationId.HasValue
                && (!warehouseId.HasValue || d.LicensePlate.WarehouseId == warehouseId.Value))
            .Select(d => new
            {
                WarehouseId = d.LicensePlate!.WarehouseId,
                LocationId = d.LicensePlate.CurrentLocationId!.Value,
                d.ItemId,
                d.OwnerPartnerId,
                d.LotNumber,
                d.ExpiryDate,
                d.HoldStatus,
                d.Quantity
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new SnapshotKey(r.WarehouseId, r.LocationId, r.ItemId, r.OwnerPartnerId, r.LotNumber, r.ExpiryDate, r.HoldStatus))
            .Select(g => new ExpectedSnapshotRow(g.Key, g.Sum(x => x.Quantity)))
            .ToList();
    }

    private async Task<List<ActualSnapshotRow>> BuildActualSnapshotAsync(int? warehouseId, CancellationToken ct)
    {
        var rows = await _db.ItemLocations
            .Include(il => il.Location)!.ThenInclude(l => l!.Zone)
            .Where(il => il.Location != null
                && il.Location.Zone != null
                && (!warehouseId.HasValue || il.Location.Zone.WarehouseId == warehouseId.Value))
            .ToListAsync(ct);

        return rows
            .Where(il => il.Location?.Zone != null)
            .Select(il => new ActualSnapshotRow(
                new SnapshotKey(il.Location!.Zone!.WarehouseId, il.LocationId, il.ItemId, il.OwnerPartnerId, il.LotNumber, il.ExpiryDate, il.HoldStatus),
                il))
            .ToList();
    }

    private sealed record LpnMovedPayload(
        long LicensePlateId,
        int WarehouseId,
        int SourceLocationId,
        int DestinationLocationId,
        string Actor,
        string? Reason,
        DateTime EventAt);

    // P0-8: thêm OwnerPartnerId vào key để snapshot row tạo mới đúng theo chủ hàng
    // (ItemLocation có unique index bao gồm OwnerPartnerId — nếu để null sẽ tách sai owner trong 3PL).
    private sealed record SnapshotKey(
        int WarehouseId,
        int LocationId,
        int ItemId,
        int? OwnerPartnerId,
        string? LotNumber,
        DateTime? ExpiryDate,
        InventoryHoldStatusEnum HoldStatus);

    private sealed record ExpectedSnapshotRow(SnapshotKey Key, decimal Quantity);
    private sealed record ActualSnapshotRow(SnapshotKey Key, ItemLocation Row);
}
