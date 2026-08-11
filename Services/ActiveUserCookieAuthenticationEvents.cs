using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using WMS.Data;

namespace WMS.Services;

/// <summary>
/// Revalidates the security-sensitive account state behind an authentication cookie.
/// This makes account lock, deactivation and password-reset revocation effective on
/// the next authenticated request instead of waiting for the cookie to expire.
/// </summary>
public sealed class ActiveUserCookieAuthenticationEvents : CookieAuthenticationEvents
{
    private readonly AppDbContext _db;

    public ActiveUserCookieAuthenticationEvents(AppDbContext db) => _db = db;

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var account = await _db.AppUsers
            .AsNoTracking()
            .Where(user => user.UserId == userId)
            .Select(user => new
            {
                user.IsActive,
                user.LockoutEnd,
                user.TrustedDeviceRevokedAtUtc
            })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        var nowUtc = DateTime.UtcNow;
        var issuedUtc = context.Properties.IssuedUtc?.UtcDateTime;
        var revokedAfterIssue = account?.TrustedDeviceRevokedAtUtc is DateTime revokedAtUtc
            && (!issuedUtc.HasValue || revokedAtUtc >= issuedUtc.Value);

        if (account == null
            || !account.IsActive
            || (account.LockoutEnd.HasValue && account.LockoutEnd.Value > nowUtc)
            || revokedAfterIssue)
        {
            context.RejectPrincipal();
        }
    }
}
