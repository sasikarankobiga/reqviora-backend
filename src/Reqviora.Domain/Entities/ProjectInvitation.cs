using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public static class ProjectInvitationStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Revoked = "Revoked";
    public const string Expired = "Expired";
}

public sealed class ProjectInvitation : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string NormalizedRecipientEmail { get; set; } = string.Empty;
    public string Role { get; set; } = ProjectMemberRoles.Viewer;
    public string TokenHash { get; set; } = string.Empty;
    public string? PendingRecipientKey { get; set; }
    public string Status { get; set; } = ProjectInvitationStatuses.Pending;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedAtUnixTimeMilliseconds { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? AcceptedBy { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? RejectedBy { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? SupersedesInvitationId { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
