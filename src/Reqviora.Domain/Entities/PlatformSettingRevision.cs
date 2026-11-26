namespace Reqviora.Domain.Entities;

public sealed class PlatformSettingRevision
{
    public string Key { get; init; } = string.Empty;
    public int Revision { get; init; }
    public long PreviousValue { get; init; }
    public long Value { get; init; }
    public Guid ChangedBy { get; init; }
    public DateTimeOffset ChangedAt { get; init; }
    public string Reason { get; init; } = string.Empty;
    public int? RolledBackTo { get; init; }
}
