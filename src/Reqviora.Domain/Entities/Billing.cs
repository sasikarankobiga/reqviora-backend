using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public static class PaymentStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Refunded = "Refunded";
    public const string Disputed = "Disputed";
}

public sealed class ProjectPricing : BaseEntity
{
    public Guid ProjectId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveUntil { get; set; }
    public Guid CreatedBy { get; set; }
}

public sealed class PaymentOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public Guid RequestedBy { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = PaymentStatuses.Pending;
    public string? ProviderPaymentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

public sealed class PaymentTransaction : BaseEntity
{
    public Guid PaymentOrderId { get; set; }
    public string Provider { get; set; } = "PayHere";
    public string? ProviderPaymentId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string Status { get; set; } = PaymentStatuses.Pending;
    public string ProviderStatusCode { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "USD";
    public string? PaymentMethod { get; set; }
    public string? StatusMessage { get; set; }
    public string PayloadSha256 { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class ProjectEntitlement : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid PaymentOrderId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class PaymentRefund : BaseEntity
{
    public Guid PaymentOrderId { get; set; }
    public string? ProviderRefundId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "Requested";
    public string Reason { get; set; } = string.Empty;
    public Guid RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class PaymentAuditRecord : BaseEntity
{
    public Guid? PaymentOrderId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
