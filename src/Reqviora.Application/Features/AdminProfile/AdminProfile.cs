namespace Reqviora.Application.Features.AdminProfile;

public sealed record AdminProfileDetails(Guid Id, string Email, string DisplayName, bool EmailVerified,
    bool MfaEnabled);

public sealed record AdminSessionDetails(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    bool IsCurrent);

public sealed record AdminLoginHistoryEntry(Guid Id, string IpAddress, string DeviceId, bool Succeeded,
    bool OtpRequired, DateTimeOffset OccurredAt);

public sealed record AdminSecurityEvent(Guid Id, string Method, string Path, int StatusCode,
    DateTimeOffset OccurredAt);

public sealed record AdminSecurityDetails(bool MfaEnabled, bool EmailVerified, int ActiveSessions,
    IReadOnlyList<AdminSecurityEvent> RecentEvents);

public interface IAdminProfileService
{
    Task<AdminProfileDetails> GetAsync(Guid userId, CancellationToken ct);
    Task<AdminProfileDetails> UpdateAsync(Guid userId, string displayName, CancellationToken ct);
    Task<AdminSecurityDetails> SecurityAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<AdminSessionDetails>> SessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken ct);
    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct);
    Task<int> RevokeOtherSessionsAsync(Guid userId, Guid currentSessionId, CancellationToken ct);
    Task<IReadOnlyList<AdminLoginHistoryEntry>> LoginHistoryAsync(Guid userId, int page, int pageSize,
        CancellationToken ct);
}
