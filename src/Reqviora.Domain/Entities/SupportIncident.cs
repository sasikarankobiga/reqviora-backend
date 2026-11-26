using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class SupportIncident : AggregateRoot
{
    public string Reference { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = "Low";
    public string Status { get; set; } = "Open";
    public Guid ReporterId { get; set; }
    public Guid? AssignedAdminId { get; set; }
    public string? CorrelationId { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long UpdatedAtUnixTimeMilliseconds { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

public sealed class SupportIncidentHistory : BaseEntity
{
    public Guid IncidentId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? Notes { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public long ChangedAtUnixTimeMilliseconds { get; set; }
}
