using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WMS.Data;
using WMS.Models;
using WMS.Common;

namespace WMS.Services;

/// <summary>
/// P1.2 — Integration Reliability Service
/// Cung cấp outbox pattern và idempotency check cho tất cả integration operations.
/// </summary>
public interface IIntegrationService
{
    /// <summary>Enqueue một event vào outbox (bất đồng bộ, retry tự động)</summary>
    Task EnqueueAsync(OutboxEventTypeEnum eventType, string targetEndpoint, object payload,
        string? idempotencyKey = null, string? targetSystem = null);

    /// <summary>Check idempotency key — trả về cached response nếu đã xử lý</summary>
    Task<(bool IsDuplicate, string? CachedResponse, int StatusCode)> CheckIdempotencyAsync(
        string keyValue, string operationType);

    /// <summary>Mark idempotency key sau khi xử lý thành công</summary>
    Task SetIdempotencyAsync(string keyValue, string operationType, string response, int statusCode);

    /// <summary>Background job: đọc pending outbox, gửi HTTP, retry nếu fail → dead-letter</summary>
    Task ProcessOutboxBatchAsync(CancellationToken ct = default);
}

public class IntegrationService : IIntegrationService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IntegrationService> _logger;
    private const int MaxRetries = 3;
    private static readonly TimeSpan ProcessingLeaseTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);

    public IntegrationService(AppDbContext db, IHttpClientFactory httpClientFactory, ILogger<IntegrationService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task EnqueueAsync(OutboxEventTypeEnum eventType, string targetEndpoint, object payload,
        string? idempotencyKey = null, string? targetSystem = null)
    {
        var actor = "system";
        var correlationId = Guid.NewGuid().ToString("N");
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.IntegrationOutbox
                .FirstOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey);
            if (existing != null)
            {
                if (existing.Status is OutboxStatusEnum.Failed or OutboxStatusEnum.DeadLetter)
                {
                    Requeue(existing);
                    await _db.SaveChangesAsync();
                    _logger.LogInformation(
                        "[Outbox] Requeued {EventType} with existing idempotency key (correlationId={CorrelationId})",
                        eventType,
                        existing.CorrelationId);
                }
                else
                {
                    _logger.LogInformation(
                        "[Outbox] Duplicate enqueue ignored for {EventType}; current status is {Status}",
                        eventType,
                        existing.Status);
                }

                return;
            }
        }

        var outbox = new IntegrationOutbox
        {
            EventType = eventType.ToString(),
            TargetEndpoint = targetEndpoint,
            Payload = payloadJson,
            HttpMethod = "POST",
            Status = OutboxStatusEnum.Pending,
            RetryCount = 0,
            IdempotencyKey = idempotencyKey,
            TargetSystem = targetSystem,
            CorrelationId = correlationId,
            CreatedBy = actor,
            CreatedAt = VietnamTime.Now
        };
        _db.IntegrationOutbox.Add(outbox);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _db.Entry(outbox).State = EntityState.Detached;
            var existing = await _db.IntegrationOutbox
                .FirstOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey);
            if (existing == null)
                throw;

            if (existing.Status is OutboxStatusEnum.Failed or OutboxStatusEnum.DeadLetter)
            {
                Requeue(existing);
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation(
                "[Outbox] Concurrent duplicate enqueue resolved for {EventType}; current status is {Status}",
                eventType,
                existing.Status);
            return;
        }

        _logger.LogInformation("[Outbox] Enqueued {EventType} to {Endpoint} (correlationId={CorrelationId})",
            eventType, targetEndpoint, correlationId);

        void Requeue(IntegrationOutbox existing)
        {
            existing.EventType = eventType.ToString();
            existing.TargetEndpoint = targetEndpoint;
            existing.Payload = payloadJson;
            existing.HttpMethod = "POST";
            existing.Status = OutboxStatusEnum.Pending;
            existing.RetryCount = 0;
            existing.LastError = null;
            existing.ProcessedAt = null;
            existing.TargetSystem = targetSystem;
            existing.CorrelationId = correlationId;
        }
    }

    public async Task<(bool IsDuplicate, string? CachedResponse, int StatusCode)> CheckIdempotencyAsync(
        string keyValue, string operationType)
    {
        var key = await _db.IntegrationIdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyValue == keyValue && k.OperationType == operationType
                && k.ExpiresAt > VietnamTime.Now);

        if (key != null)
        {
            _logger.LogInformation("[Idempotency] Duplicate key detected: {Key} ({Operation})",
                keyValue, operationType);
            return (true, key.CachedResponse, key.ResponseStatusCode);
        }

        return (false, null, 0);
    }

    public async Task SetIdempotencyAsync(string keyValue, string operationType, string response, int statusCode)
    {
        // Xóa key cũ nếu có
        var existing = await _db.IntegrationIdempotencyKeys
            .FirstOrDefaultAsync(k => k.KeyValue == keyValue && k.OperationType == operationType);
        if (existing != null)
        {
            existing.CachedResponse = response;
            existing.ResponseStatusCode = statusCode;
            existing.CreatedAt = VietnamTime.Now;
            existing.ExpiresAt = VietnamTime.Now.AddHours(24);
        }
        else
        {
            _db.IntegrationIdempotencyKeys.Add(new IntegrationIdempotencyKey
            {
                KeyValue = keyValue,
                OperationType = operationType,
                CachedResponse = response,
                ResponseStatusCode = statusCode,
                CreatedAt = VietnamTime.Now,
                ExpiresAt = VietnamTime.Now.AddHours(24)
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task ProcessOutboxBatchAsync(CancellationToken ct = default)
    {
        var now = VietnamTime.Now;
        var staleCutoff = now.Subtract(ProcessingLeaseTimeout);
        var stale = await _db.IntegrationOutbox
            .Where(o => o.Status == OutboxStatusEnum.Processing
                && (!o.ProcessedAt.HasValue || o.ProcessedAt < staleCutoff))
            .OrderBy(o => o.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        foreach (var s in stale)
        {
            s.Status = OutboxStatusEnum.Pending;
            s.ProcessedAt = now;
            s.LastError = "Reset từ trạng thái đang xử lý bị gián đoạn; hệ thống sẽ gửi lại an toàn.";
        }
        if (stale.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            foreach (var staleItem in stale)
                _db.Entry(staleItem).State = EntityState.Detached;
        }

        var eventIds = await _db.IntegrationOutbox
            .AsNoTracking()
            .Where(o => o.Status == OutboxStatusEnum.Pending || o.Status == OutboxStatusEnum.Failed)
            .Where(o => !o.ProcessedAt.HasValue || o.ProcessedAt <= now)
            .OrderBy(o => o.CreatedAt)
            .Take(50)
            .Select(o => o.OutboxId)
            .ToListAsync(ct);

        foreach (var eventId in eventIds)
        {
            ct.ThrowIfCancellationRequested();

            IntegrationOutbox? item;
            if (_db.Database.IsRelational())
            {
                var claimed = await _db.IntegrationOutbox
                    .Where(o => o.OutboxId == eventId
                        && (o.Status == OutboxStatusEnum.Pending || o.Status == OutboxStatusEnum.Failed)
                        && (!o.ProcessedAt.HasValue || o.ProcessedAt <= now))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(o => o.Status, OutboxStatusEnum.Processing)
                        .SetProperty(o => o.ProcessedAt, now)
                        .SetProperty(o => o.LastError, (string?)null), ct);
                if (claimed == 0)
                    continue;

                item = await _db.IntegrationOutbox.FirstOrDefaultAsync(o => o.OutboxId == eventId, ct);
            }
            else
            {
                item = await _db.IntegrationOutbox.FirstOrDefaultAsync(o => o.OutboxId == eventId, ct);
                if (item == null
                    || (item.Status != OutboxStatusEnum.Pending && item.Status != OutboxStatusEnum.Failed)
                    || (item.ProcessedAt.HasValue && item.ProcessedAt > now))
                {
                    continue;
                }

                item.Status = OutboxStatusEnum.Processing;
                item.ProcessedAt = now;
                item.LastError = null;
                await _db.SaveChangesAsync(ct);
            }

            if (item == null)
                continue;

            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(item.HttpMethod), item.TargetEndpoint);
                request.Content = new StringContent(item.Payload, System.Text.Encoding.UTF8, "application/json");
                request.Headers.Add("X-Correlation-Id", item.CorrelationId ?? "");
                if (!string.IsNullOrEmpty(item.IdempotencyKey))
                    request.Headers.Add("X-Idempotency-Key", item.IdempotencyKey);

                using var client = _httpClientFactory.CreateClient("Integration");
                using var response = await client.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    item.Status = OutboxStatusEnum.Sent;
                    item.ProcessedAt = VietnamTime.Now;
                    item.LastError = null;
                    _logger.LogInformation("[Outbox] Sent {EventType} → {Endpoint} [{CorrelationId}]",
                        item.EventType, item.TargetEndpoint, item.CorrelationId);
                }
                else
                {
                    var statusCode = (int)response.StatusCode;
                    var isTransient = IsTransientStatusCode(statusCode);
                    item.RetryCount++;
                    item.LastError = $"HTTP {statusCode} ({response.ReasonPhrase ?? "không có mô tả"}).";
                    item.Status = !isTransient || item.RetryCount >= MaxRetries
                        ? OutboxStatusEnum.DeadLetter
                        : OutboxStatusEnum.Pending;
                    item.ProcessedAt = item.Status == OutboxStatusEnum.Pending
                        ? VietnamTime.Now.Add(ComputeRetryDelay(item.RetryCount, response))
                        : VietnamTime.Now;
                    _logger.LogWarning("[Outbox] Failed {EventType} → {Endpoint} [{RetryCount}]: {Error}",
                        item.EventType, item.TargetEndpoint, item.RetryCount, item.LastError);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                item.Status = OutboxStatusEnum.Pending;
                item.ProcessedAt = VietnamTime.Now.AddSeconds(5);
                item.LastError = "Tiến trình gửi đã dừng an toàn; sự kiện đang chờ gửi lại.";
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                item.RetryCount++;
                item.LastError = UserSafeError.From(ex, "Không thể gửi sự kiện tích hợp lúc này. Hệ thống sẽ tự thử lại.");
                item.Status = item.RetryCount >= MaxRetries
                    ? OutboxStatusEnum.DeadLetter
                    : OutboxStatusEnum.Pending;
                item.ProcessedAt = item.Status == OutboxStatusEnum.Pending
                    ? VietnamTime.Now.Add(ComputeRetryDelay(item.RetryCount))
                    : VietnamTime.Now;
                _logger.LogError(ex, "[Outbox] Exception sending {EventType}", item.EventType);
            }

            await _db.SaveChangesAsync(ct);
        }
    }

    private static bool IsTransientStatusCode(int statusCode)
        => statusCode is 408 or 425 or 429 || statusCode >= 500;

    private static TimeSpan ComputeRetryDelay(int retryCount, HttpResponseMessage? response = null)
    {
        TimeSpan? providerDelay = response?.Headers.RetryAfter?.Delta;
        if (!providerDelay.HasValue && response?.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
            providerDelay = retryAt - VietnamTime.UtcNowOffset;

        var exponentialSeconds = 5 * Math.Pow(2, Math.Max(0, retryCount - 1));
        var delay = providerDelay.GetValueOrDefault(TimeSpan.FromSeconds(exponentialSeconds));
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        return delay > MaximumRetryDelay ? MaximumRetryDelay : delay;
    }
}
