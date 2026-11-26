using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Features.Authentication;

public sealed class EmailVerificationSender(
    ReqvioraDbContext db, IEmailService email, IConfiguration configuration,
    TimeProvider clock, ILogger<EmailVerificationSender> logger)
{
    public async Task<bool> SendAsync(User user, CancellationToken cancellationToken)
    {
        if (user.EmailVerified) return false;
        var now = clock.GetUtcNow();
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var action = new AccountActionToken
        {
            UserId = user.Id,
            Purpose = AccountActionPurpose.EmailVerification,
            TargetValue = user.NormalizedEmail,
            TokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30)
        };
        db.AccountActionTokens.Add(action);
        await db.SaveChangesAsync(cancellationToken);
        var baseUrl = configuration["AccountLinks:BaseUrl"] ?? "https://localhost";
        try
        {
            await email.SendAsync(user.Email, "Verify your REQVIORA email",
                $"{baseUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(raw)}", cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or SmtpException or FormatException)
        {
            action.ConsumedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Verification email delivery failed; a resend can be requested.");
            return false;
        }
    }

    public async Task ResendAsync(string address, CancellationToken cancellationToken)
    {
        var normalized = address.Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, cancellationToken);
        if (user is null || user.EmailVerified) return;
        var since = clock.GetUtcNow().AddMinutes(-1);
        // Compare in memory for SQLite test-provider DateTimeOffset compatibility.
        var recent = await db.AccountActionTokens.AsNoTracking()
            .Where(x => x.UserId == user.Id && x.Purpose == AccountActionPurpose.EmailVerification && x.ConsumedAt == null)
            .Select(x => x.CreatedAt).ToListAsync(cancellationToken);
        if (recent.Any(x => x >= since)) return;
        await SendAsync(user, cancellationToken);
    }
}
