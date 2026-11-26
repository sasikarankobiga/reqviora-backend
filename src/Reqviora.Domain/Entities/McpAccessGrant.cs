using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class McpAccessGrant : BaseEntity
{
    public Guid ProjectId { get; init; }
    public Guid ApprovedRevisionId { get; init; }
    public Guid OwnerId { get; init; }
    public string SecretHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
