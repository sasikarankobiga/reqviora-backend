using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class ValidationTelemetry : BaseEntity
{
    public long CompletedAtMilliseconds { get; init; }
    public int UtcDay { get; init; }
    public string Workflow { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public long DurationMilliseconds { get; init; }
    public int SchemaFailures { get; init; }
    public int BrokenReferences { get; init; }
    public int UnsupportedComponents { get; init; }
    public int NavigationFailures { get; init; }
    public int OtherFailures { get; init; }
}
