using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public enum AiJobState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

public sealed class AiJob : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public Guid OwnerId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public AiJobState State { get; set; } = AiJobState.Pending;
    public string? Result { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 120;
    public long AvailableAtUnixTimeMilliseconds { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string? LeaseOwner { get; set; }
    public long? LeaseExpiresAtUnixTimeMilliseconds { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancellationRequestedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
