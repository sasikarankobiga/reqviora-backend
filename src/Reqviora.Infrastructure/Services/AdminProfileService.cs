using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.AdminProfile;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class AdminProfileService(ReqvioraDbContext db, TimeProvider clock) : IAdminProfileService
{
    public async Task<AdminProfileDetails> GetAsync(Guid userId, CancellationToken ct) =>
        Map(await UserAsync(userId, ct));

    public async Task<AdminProfileDetails> UpdateAsync(Guid userId, string displayName, CancellationToken ct)
    {
        displayName = displayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > 200)
            throw new ArgumentException("Display name must contain 1 to 200 characters.", nameof(displayName));
        var user = await UserAsync(userId, ct);
        if (user.DisplayName == displayName) return Map(user);
        user.DisplayName = displayName;
        var now = clock.GetUtcNow();
        db.AuditEvents.Add(new AuditEvent { UserId = userId, Method = "PUT", StatusCode = 200,
            IpAddress = "security-service", Path = "/security/admin-profile/updated", OccurredAt = now });
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<AdminSecurityDetails> SecurityAsync(Guid userId, CancellationToken ct)
    {
        var user = await UserAsync(userId, ct);
        var now = clock.GetUtcNow();
        var activeTokens = await db.RefreshTokens.AsNoTracking().Where(x => x.UserId == userId &&
            x.RevokedAt == null).Select(x => x.ExpiresAt).ToArrayAsync(ct);
        var active = activeTokens.Count(expiresAt => expiresAt > now);
        var query = db.AuditEvents.AsNoTracking().Where(x => x.UserId == userId &&
            (x.Path.StartsWith("/security/") || x.Path.StartsWith("/api/account-security/")));
        AuditEvent[] events;
        if (IsSqlite())
            events = (await query.ToArrayAsync(ct)).OrderByDescending(x => x.OccurredAt).Take(50).ToArray();
        else
            events = await query.OrderByDescending(x => x.OccurredAt).Take(50).ToArrayAsync(ct);
        return new(user.MfaEnabled, user.EmailVerified, active, events.Select(x =>
            new AdminSecurityEvent(x.Id, x.Method, x.Path, x.StatusCode, x.OccurredAt)).ToArray());
    }

    public async Task<IReadOnlyList<AdminSessionDetails>> SessionsAsync(Guid userId, Guid? currentSessionId,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.RefreshTokens.AsNoTracking().Where(x => x.UserId == userId && x.RevokedAt == null);
        RefreshToken[] sessions;
        if (IsSqlite())
            sessions = (await query.ToArrayAsync(ct)).Where(x => x.ExpiresAt > now)
                .OrderByDescending(x => x.CreatedAt).ToArray();
        else
            sessions = await query.Where(x => x.ExpiresAt > now).OrderByDescending(x => x.CreatedAt).ToArrayAsync(ct);
        return sessions.Select(x => new AdminSessionDetails(x.Id, x.CreatedAt, x.ExpiresAt,
            x.Id == currentSessionId)).ToArray();
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.RefreshTokens.SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct)
            ?? throw new KeyNotFoundException("The session was not found.");
        if (session.RevokedAt is not null) return;
        var now = clock.GetUtcNow();
        session.RevokedAt = now;
        AddAudit(userId, sessionId, "revoked", now);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> RevokeOtherSessionsAsync(Guid userId, Guid currentSessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var current = await db.RefreshTokens.AsNoTracking().Where(x => x.Id == currentSessionId && x.UserId == userId)
            .Select(x => new { x.RevokedAt, x.ExpiresAt }).SingleOrDefaultAsync(ct);
        if (current is null || current.RevokedAt is not null || current.ExpiresAt <= now)
            throw new InvalidOperationException("The current session is not active.");
        var candidates = await db.RefreshTokens.Where(x => x.UserId == userId && x.Id != currentSessionId &&
            x.RevokedAt == null).ToArrayAsync(ct);
        var sessions = candidates.Where(x => x.ExpiresAt > now).ToArray();
        foreach (var session in sessions) session.RevokedAt = now;
        AddAudit(userId, currentSessionId, $"revoked-others/{sessions.Length}", now);
        await db.SaveChangesAsync(ct);
        return sessions.Length;
    }

    public async Task<IReadOnlyList<AdminLoginHistoryEntry>> LoginHistoryAsync(Guid userId, int page, int pageSize,
        CancellationToken ct)
    {
        var query = db.LoginAudits.AsNoTracking().Where(x => x.UserId == userId);
        LoginAudit[] rows;
        if (IsSqlite())
            rows = (await query.ToArrayAsync(ct)).OrderByDescending(x => x.OccurredAt)
                .Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        else
            rows = await query.OrderByDescending(x => x.OccurredAt).Skip((page - 1) * pageSize)
                .Take(pageSize).ToArrayAsync(ct);
        return rows.Select(x => new AdminLoginHistoryEntry(x.Id, x.IpAddress, x.DeviceId, x.Succeeded,
            x.OtpRequired, x.OccurredAt)).ToArray();
    }

    private async Task<User> UserAsync(Guid userId, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct)
        ?? throw new KeyNotFoundException("The administrator profile was not found.");

    private bool IsSqlite() => db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true;

    private void AddAudit(Guid userId, Guid sessionId, string action, DateTimeOffset now) =>
        db.AuditEvents.Add(new AuditEvent { UserId = userId, Method = "DELETE", StatusCode = 204,
            IpAddress = "security-service", Path = $"/security/admin-profile/sessions/{sessionId}/{action}",
            OccurredAt = now });

    private static AdminProfileDetails Map(User user) => new(user.Id, user.Email, user.DisplayName,
        user.EmailVerified, user.MfaEnabled);
}
