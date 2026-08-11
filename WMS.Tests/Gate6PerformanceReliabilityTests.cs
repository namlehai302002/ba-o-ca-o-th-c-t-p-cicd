using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using WMS.Data;
using WMS.Models;
using WMS.Services;

namespace WMS.Tests;

public class Gate6PerformanceReliabilityTests
{
    [Fact]
    public async Task IntegrationOutbox_Success_ShouldSendOnceAndForwardTraceHeaders()
    {
        await using var db = CreateDb(nameof(IntegrationOutbox_Success_ShouldSendOnceAndForwardTraceHeaders));
        db.IntegrationOutbox.Add(NewOutbox("AUDIT_TEST_GATE6_success"));
        await db.SaveChangesAsync();

        string? correlationHeader = null;
        string? idempotencyHeader = null;
        var handler = new StubHttpHandler(request =>
        {
            correlationHeader = request.Headers.GetValues("X-Correlation-Id").Single();
            idempotencyHeader = request.Headers.GetValues("X-Idempotency-Key").Single();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = CreateService(db, handler);

        await service.ProcessOutboxBatchAsync();

        var row = await db.IntegrationOutbox.SingleAsync();
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(OutboxStatusEnum.Sent, row.Status);
        Assert.Equal("AUDIT_TEST_GATE6_correlation", correlationHeader);
        Assert.Equal("AUDIT_TEST_GATE6_success", idempotencyHeader);
        Assert.NotNull(row.ProcessedAt);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task IntegrationOutbox_PermanentHttpFailure_ShouldDeadLetterImmediately()
    {
        await using var db = CreateDb(nameof(IntegrationOutbox_PermanentHttpFailure_ShouldDeadLetterImmediately));
        db.IntegrationOutbox.Add(NewOutbox("AUDIT_TEST_GATE6_permanent"));
        await db.SaveChangesAsync();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        await CreateService(db, handler).ProcessOutboxBatchAsync();

        var row = await db.IntegrationOutbox.SingleAsync();
        Assert.Equal(OutboxStatusEnum.DeadLetter, row.Status);
        Assert.Equal(1, row.RetryCount);
        Assert.Contains("HTTP 400", row.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", row.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IntegrationOutbox_Transient429_ShouldHonorBoundedRetryAfter()
    {
        await using var db = CreateDb(nameof(IntegrationOutbox_Transient429_ShouldHonorBoundedRetryAfter));
        db.IntegrationOutbox.Add(NewOutbox("AUDIT_TEST_GATE6_rate_limit"));
        await db.SaveChangesAsync();
        var before = WMS.Common.VietnamTime.Now;
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });

        await CreateService(db, handler).ProcessOutboxBatchAsync();

        var row = await db.IntegrationOutbox.SingleAsync();
        Assert.Equal(OutboxStatusEnum.Pending, row.Status);
        Assert.Equal(1, row.RetryCount);
        Assert.NotNull(row.ProcessedAt);
        Assert.InRange(row.ProcessedAt!.Value, before.AddSeconds(80), before.AddMinutes(5));
    }

    [Fact]
    public async Task IntegrationOutbox_TransientFailureAtLimit_ShouldDeadLetter()
    {
        await using var db = CreateDb(nameof(IntegrationOutbox_TransientFailureAtLimit_ShouldDeadLetter));
        var row = NewOutbox("AUDIT_TEST_GATE6_retry_limit");
        row.RetryCount = 2;
        db.IntegrationOutbox.Add(row);
        await db.SaveChangesAsync();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await CreateService(db, handler).ProcessOutboxBatchAsync();

        Assert.Equal(OutboxStatusEnum.DeadLetter, row.Status);
        Assert.Equal(3, row.RetryCount);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task IntegrationOutbox_EnqueueDuplicateDeadLetter_ShouldRequeueWithoutDuplicateRow()
    {
        await using var db = CreateDb(nameof(IntegrationOutbox_EnqueueDuplicateDeadLetter_ShouldRequeueWithoutDuplicateRow));
        var row = NewOutbox("AUDIT_TEST_GATE6_requeue");
        row.Status = OutboxStatusEnum.DeadLetter;
        row.RetryCount = 3;
        row.LastError = "HTTP 503";
        row.ProcessedAt = WMS.Common.VietnamTime.Now;
        db.IntegrationOutbox.Add(row);
        await db.SaveChangesAsync();
        var service = CreateService(db, new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        await service.EnqueueAsync(
            OutboxEventTypeEnum.MheCommandDispatched,
            "https://integration.example.test/mhe",
            new { command = "AUDIT_TEST_GATE6" },
            row.IdempotencyKey,
            "MHE_TEST");

        var persisted = Assert.Single(await db.IntegrationOutbox.ToListAsync());
        Assert.Equal(OutboxStatusEnum.Pending, persisted.Status);
        Assert.Equal(0, persisted.RetryCount);
        Assert.Null(persisted.LastError);
        Assert.Null(persisted.ProcessedAt);
        Assert.Equal(OutboxEventTypeEnum.MheCommandDispatched.ToString(), persisted.EventType);
        Assert.Equal("MHE_TEST", persisted.TargetSystem);
    }

    [Fact]
    public async Task WebhookReplay_HttpSubscription_ShouldCreateDeliveryAndOutboxTogether()
    {
        await using var db = CreateDb(nameof(WebhookReplay_HttpSubscription_ShouldCreateDeliveryAndOutboxTogether));
        var subscription = new WebhookSubscription
        {
            SubscriptionCode = "AUDIT_TEST_GATE6_WEBHOOK",
            EventType = "InventoryChanged",
            TargetUrl = "https://integration.example.test/webhook",
            SigningSecret = "test-only-signing-secret",
            IsActive = true,
            CreatedBy = "AUDIT_TEST_GATE6"
        };
        var original = new WebhookDelivery
        {
            Subscription = subscription,
            EventType = subscription.EventType,
            IdempotencyKey = "AUDIT_TEST_GATE6_WEBHOOK_ORIGINAL",
            PayloadJson = "{\"event\":\"InventoryChanged\",\"reference\":\"AUDIT_TEST_GATE6\"}",
            Signature = "original-signature",
            Status = WebhookDeliveryStatusEnum.Failed
        };
        db.WebhookSubscriptions.Add(subscription);
        db.WebhookDeliveries.Add(original);
        await db.SaveChangesAsync();
        var service = new EnterpriseIntegrationService(db);

        var replay = await service.ReplayWebhookAsync(original.WebhookDeliveryId, "AUDIT_TEST_GATE6");

        Assert.Equal(WebhookDeliveryStatusEnum.Pending, replay.Status);
        Assert.Equal(2, await db.WebhookDeliveries.CountAsync());
        var outbox = Assert.Single(await db.IntegrationOutbox.ToListAsync());
        Assert.Equal(replay.IdempotencyKey, outbox.IdempotencyKey);
        Assert.Equal(OutboxStatusEnum.Pending, outbox.Status);
        Assert.Equal(subscription.TargetUrl, outbox.TargetEndpoint);
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal("AUDIT_TEST_GATE6", payload.RootElement.GetProperty("reference").GetString());
    }

    [Fact]
    public void Gate6_ReadOnlyRoutes_ShouldUseDatabasePagingAndCancellation()
    {
        var root = FindRepositoryRoot();
        var reports = File.ReadAllText(Path.Combine(root, "Controllers", "ReportsController.Inventory.cs"));
        var warehouses = File.ReadAllText(Path.Combine(root, "Controllers", "WarehousesController.cs"));
        var analytics = File.ReadAllText(Path.Combine(root, "Controllers", "ReportsController.Analytics.cs"));

        Assert.Contains("pageSize = Math.Clamp(pageSize, 10, 100);", reports, StringComparison.Ordinal);
        Assert.Contains(".Skip((page - 1) * pageSize)", reports, StringComparison.Ordinal);
        Assert.Contains("CountAsync(cancellationToken)", reports, StringComparison.Ordinal);
        Assert.Contains("stockByItemQuery", reports, StringComparison.Ordinal);
        Assert.Contains(".AsSplitQuery()", warehouses, StringComparison.Ordinal);
        Assert.Contains("ToListAsync(cancellationToken)", warehouses, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(days ?? 30, 1, 180)", analytics, StringComparison.Ordinal);
    }

    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options) { SkipAudit = true };
    }

    private static IntegrationService CreateService(AppDbContext db, StubHttpHandler handler)
        => new(db, new StubHttpClientFactory(handler), NullLogger<IntegrationService>.Instance);

    private static IntegrationOutbox NewOutbox(string idempotencyKey) => new()
    {
        EventType = OutboxEventTypeEnum.InventoryChanged.ToString(),
        TargetEndpoint = "https://integration.example.test/events",
        Payload = "{\"event\":\"AUDIT_TEST_GATE6\"}",
        HttpMethod = "POST",
        Status = OutboxStatusEnum.Pending,
        IdempotencyKey = idempotencyKey,
        CorrelationId = "AUDIT_TEST_GATE6_correlation",
        CreatedBy = "AUDIT_TEST_GATE6",
        CreatedAt = WMS.Common.VietnamTime.Now
    };

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WMS.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc WMS để kiểm tra Gate 6.");
    }

    private sealed class StubHttpClientFactory(StubHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(respond(request));
        }
    }
}
