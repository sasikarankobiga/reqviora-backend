namespace Reqviora.Application.Features.PlatformSettings;

public sealed record PlatformSetting(string Key, long Value, int Revision, long Minimum, long Maximum);

public sealed record SettingHistoryEntry(int Revision, long PreviousValue, long Value, Guid ChangedBy,
    DateTimeOffset ChangedAt, string Reason, int? RolledBackTo);

public interface IPlatformSettings
{
    const string DocumentUploadMaxBytes = "document-upload-max-bytes";
    Task<PlatformSetting> GetAsync(string key, CancellationToken ct);
    Task<IReadOnlyList<SettingHistoryEntry>> HistoryAsync(string key, int page, int pageSize, CancellationToken ct);
    Task<PlatformSetting> ChangeAsync(string key, long value, int expectedRevision, string reason, Guid actor, CancellationToken ct);
    Task<PlatformSetting> RollbackAsync(string key, int targetRevision, int expectedRevision, string reason, Guid actor, CancellationToken ct);
}
