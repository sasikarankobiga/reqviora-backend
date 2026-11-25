
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Security;

namespace Reqviora.Api.Features.Administration;

public sealed record AdminMfaRequest(string? Code);

public enum AdminAccessStatus
{
    Allowed,
    Unauthorized,
    Forbidden
}

public static class AdminRequestGuard
{
    public static async Task<AdminAccessStatus> CheckAsync(
        ClaimsPrincipal principal,
        ReqvioraDbContext db,
        IConfiguration configuration,
        LoginSecurityService security,
        string? code,
        CancellationToken ct)
    {
        var userIdText =
            principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");

        if (!Guid.TryParse(userIdText, out var userId))
        {
            return AdminAccessStatus.Unauthorized;
        }

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == userId,
                ct);

        if (user is null)
        {
            return AdminAccessStatus.Unauthorized;
        }

        var adminEmails = configuration
            .GetSection("Security:AdminEmails")
            .Get<string[]>() ?? [];

        if (!AdminOverviewAccess.IsEligible(
                user, adminEmails))
        {
            return AdminAccessStatus.Forbidden;
        }

        var otp = code?.Trim() ?? string.Empty;

        if (otp.Length != 6 ||
            !otp.All(c => c >= '0' && c <= '9'))
        {
            return AdminAccessStatus.Unauthorized;
        }

        try
        {
            return security.ValidateOtp(user, otp)
                ? AdminAccessStatus.Allowed
                : AdminAccessStatus.Unauthorized;
        }
        catch (CryptographicException)
        {
            return AdminAccessStatus.Unauthorized;
        }
        catch (FormatException)
        {
            return AdminAccessStatus.Unauthorized;
        }
    }
}
