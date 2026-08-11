using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using WMS.Data;
using WMS.Models;

namespace WMS.Tests;

public class AuditPersistenceTests
{
    [Fact]
    public async Task VoucherWorkflowUpdate_ShouldKeepChangedColumnSummaryWithinDatabaseLimit()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nameof(VoucherWorkflowUpdate_ShouldKeepChangedColumnSummaryWithinDatabaseLimit))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new AppDbContext(options) { SkipAudit = true };
        var voucher = new Voucher
        {
            VoucherCode = "AUDIT_TEST_INBOUND_001",
            VoucherType = VoucherTypeEnum.NhapKho,
            VoucherDate = new DateTime(2026, 7, 12),
            WarehouseId = 1,
            CreatedBy = "AUDIT_TEST_CREATOR"
        };
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();

        db.SkipAudit = false;
        var now = new DateTime(2026, 7, 12, 13, 0, 0);
        voucher.CompletedAt = now;
        voucher.CompletedBy = "AUDIT_TEST_COMPLETER";
        voucher.DockCompletedAt = now;
        voucher.DockStatus = DockOperationStatusEnum.Completed;
        voucher.InboundStatus = InboundStatusEnum.Completed;
        voucher.IsPosted = true;
        voucher.ReviewNote = "AUDIT_TEST_REVIEW";
        voucher.UnloadEndAt = now;
        voucher.UnloadStartAt = now.AddMinutes(-15);
        voucher.ReceivedAt = now.AddMinutes(-20);
        voucher.ReceivedBy = "AUDIT_TEST_RECEIVER";
        voucher.ApprovedAt = now.AddMinutes(-30);
        voucher.ApprovedBy = "AUDIT_TEST_APPROVER";

        await db.SaveChangesAsync();

        var audit = Assert.Single(await db.AuditLogs
            .Where(x => x.TableName == nameof(Voucher) && x.ActionType == "UPDATE")
            .ToListAsync());

        Assert.NotNull(audit.ColumnChanged);
        Assert.True(audit.ColumnChanged!.Length <= 128, $"Audit column summary was {audit.ColumnChanged.Length} characters.");
        Assert.Contains("+", audit.ColumnChanged, StringComparison.Ordinal);

        using var oldValues = JsonDocument.Parse(audit.OldValue!);
        using var newValues = JsonDocument.Parse(audit.NewValue!);
        Assert.True(oldValues.RootElement.TryGetProperty(nameof(Voucher.CompletedAt), out _));
        Assert.True(newValues.RootElement.TryGetProperty(nameof(Voucher.UnloadStartAt), out _));
        Assert.True(newValues.RootElement.TryGetProperty(nameof(Voucher.ApprovedBy), out _));
    }
}
