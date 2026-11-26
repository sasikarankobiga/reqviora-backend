using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Api.Configuration;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Security;

namespace Reqviora.Api.Controllers;

[ApiController, Route("api/security/admin-mfa"), EnableRateLimiting("authentication")]
public sealed class AdminMfaController(
    ReqvioraDbContext db,
    IDataProtectionProvider protection,
    IPasswordHasher<User> passwordHasher,
    IEmailService email,
    LoginSecurityService security,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<AdminMfaController> logger) : ControllerBase
{
    public sealed record BootstrapRequest(
        [Required, EmailAddress, StringLength(320)] string Email,
        [Required, StringLength(128, MinimumLength = 8)] string Password);

    public sealed record RecoveryRequest(
        [Required, EmailAddress, StringLength(320)] string AdminEmail);

    public sealed record CredentialRequest(
        [Required, StringLength(512, MinimumLength = 32)] string Token);

    public sealed record CredentialVerification(
        [Required, StringLength(512, MinimumLength = 32)] string Token,
        [Required, RegularExpression("^[0-9]{6}$")] string Code);

    [AllowAnonymous]
    [HttpPost("bootstrap/request")]
    public async Task<IActionResult> RequestBootstrap(BootstrapRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is not null && user.EmailVerified && !user.MfaEnabled &&
            await security.IsAdminAsync(user, cancellationToken) &&
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) !=
                PasswordVerificationResult.Failed)
        {
            await IssueCredentialAsync(user, AccountActionPurpose.AdminMfaEnrollment, cancellationToken);
        }

        // Identical response for unknown, non-admin and invalid-password requests.
        return Accepted();
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [HttpPost("recovery/request")]
    public async Task<IActionResult> RequestRecovery(RecoveryRequest request, CancellationToken cancellationToken)
    {
        var supervisor = await CurrentUserAsync(cancellationToken);
        if (supervisor is null) return Unauthorized();
        if (!supervisor.MfaEnabled || !await security.IsAdminAsync(supervisor, cancellationToken))
            return Forbid();

        var normalizedEmail = request.AdminEmail.Trim().ToUpperInvariant();
        var target = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        if (target is null || target.Id == supervisor.Id || !target.EmailVerified || !target.MfaEnabled ||
            !await security.IsAdminAsync(target, cancellationToken))
        {
            // Do not disclose administrator identities to the caller.
            return Accepted();
        }

        await IssueCredentialAsync(target, AccountActionPurpose.AdminMfaRecovery, cancellationToken);
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("provision")]
    public async Task<IActionResult> Provision(CredentialRequest request, CancellationToken cancellationToken)
    {
        var action = await FindCredentialAsync(request.Token, cancellationToken);
        if (action is null || action.TargetValue is not null ||
            !await security.IsAdminAsync(action.User, cancellationToken) ||
            action.Purpose == AccountActionPurpose.AdminMfaEnrollment && action.User.MfaEnabled ||
            action.Purpose == AccountActionPurpose.AdminMfaRecovery && !action.User.MfaEnabled)
        {
            return InvalidCredential();
        }

        var secret = RandomNumberGenerator.GetBytes(20);
        var secretText = Convert.ToBase64String(secret);
        var recoveryCodes = Enumerable.Range(0, 8)
            .Select(_ => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5)))
            .ToArray();
        action.User.MfaSecretProtected = protection.CreateProtector("Reqviora.MfaSecret.v1").Protect(secretText);
        action.User.RecoveryCodeHashes = string.Join(',', recoveryCodes.Select(Hash));
        action.User.MfaEnabled = false;
        action.TargetValue = "provisioned";
        if (action.Purpose == AccountActionPurpose.AdminMfaRecovery)
        {
            action.User.SessionVersion = checked(action.User.SessionVersion + 1);
            await RevokeSessionsAsync(action.UserId, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);

        var base32 = Base32(secret);
        var uri = $"otpauth://totp/REQVIORA:{Uri.EscapeDataString(action.User.Email)}" +
            $"?secret={base32}&issuer=REQVIORA";
        return Ok(new { secret = base32, uri, recoveryCodes });
    }

    [AllowAnonymous]
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(CredentialVerification request, CancellationToken cancellationToken)
    {
        var action = await FindCredentialAsync(request.Token, cancellationToken);
        if (action is null || action.TargetValue != "provisioned" ||
            !security.ValidateTotp(action.User, request.Code)) return InvalidCredential();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        action.ConsumedAt = now;
        action.User.MfaEnabled = true;
        action.User.SessionVersion = checked(action.User.SessionVersion + 1);
        await RevokeSessionsAsync(action.UserId, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidCredential();
        }
    }

    private async Task IssueCredentialAsync(
        User user, AccountActionPurpose purpose, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var pending = await db.AccountActionTokens
            .Where(action => action.UserId == user.Id && action.Purpose == purpose && action.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var action in pending) action.ConsumedAt = now;

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var credential = new AccountActionToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = Hash(rawToken),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(10)
        };
        db.AccountActionTokens.Add(credential);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var baseUrl = configuration["AccountLinks:BaseUrl"] ?? "https://localhost";
            var link = $"{baseUrl.TrimEnd('/')}/admin-mfa?token={Uri.EscapeDataString(rawToken)}";
            await email.SendAsync(user.Email, "REQVIORA administrator MFA credential", link, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or SmtpException)
        {
            credential.ConsumedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(exception, "Administrator MFA credential delivery failed.");
        }
    }

    private async Task<AccountActionToken?> FindCredentialAsync(string rawToken, CancellationToken cancellationToken)
    {
        var tokenHash = Hash(rawToken);
        var action = await db.AccountActionTokens.Include(candidate => candidate.User)
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash &&
                (candidate.Purpose == AccountActionPurpose.AdminMfaEnrollment ||
                 candidate.Purpose == AccountActionPurpose.AdminMfaRecovery), cancellationToken);
        return action is not null && action.IsActive(clock.GetUtcNow()) ? action : null;
    }

    private async Task<User?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var userId)
            ? await db.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
            : null;
    }

    private async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sessions = await db.RefreshTokens
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions.Where(session => session.ExpiresAt > now)) session.RevokedAt = now;
    }

    private IActionResult InvalidCredential() =>
        Problem(title: "The administrator MFA credential is invalid or expired.",
            statusCode: StatusCodes.Status400BadRequest);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }
}
