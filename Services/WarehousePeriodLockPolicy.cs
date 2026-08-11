using Microsoft.EntityFrameworkCore;
using WMS.Data;
using WMS.Models;

namespace WMS.Services;

internal static class WarehousePeriodLockPolicy
{
    public static DateTime ResolveTransactionDate(Voucher voucher, DateTime operationTime)
        => (voucher.CompletedAt ?? operationTime).Date;

    public static async Task<DateTime?> FindBlockingLockDateAsync(
        AppDbContext db,
        Voucher voucher,
        DateTime operationTime,
        CancellationToken cancellationToken = default)
    {
        var transactionDate = ResolveTransactionDate(voucher, operationTime);
        return await db.WarehousePeriodLocks
            .AsNoTracking()
            .Where(periodLock => periodLock.WarehouseId == voucher.WarehouseId
                && periodLock.IsActive
                && periodLock.LockDate >= transactionDate)
            .OrderByDescending(periodLock => periodLock.LockDate)
            .Select(periodLock => (DateTime?)periodLock.LockDate)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
