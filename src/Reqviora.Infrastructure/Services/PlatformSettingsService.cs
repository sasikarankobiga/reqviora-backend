using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.PlatformSettings;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class PlatformSettingsService(ReqvioraDbContext db, IOptions<StorageOptions> storage,
    TimeProvider clock) : IPlatformSettings
{
    private const long ContractMaximum = 25_000_000;
    private long Maximum => Math.Clamp(storage.Value.MaxFileBytes, 1, ContractMaximum);

    public async Task<PlatformSetting> GetAsync(string key, CancellationToken ct)
    {
        var latest = await Revisions(key).FirstOrDefaultAsync(ct);
        return new(key, Math.Min(latest?.Value ?? Maximum, Maximum), latest?.Revision ?? 0, 1, Maximum);
    }

    public async Task<IReadOnlyList<SettingHistoryEntry>> HistoryAsync(string key, int page, int pageSize,
        CancellationToken ct) => await Revisions(key).Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new SettingHistoryEntry(x.Revision, x.PreviousValue, x.Value, x.ChangedBy, x.ChangedAt,
            x.Reason, x.RolledBackTo)).ToArrayAsync(ct);

    public Task<PlatformSetting> ChangeAsync(string key, long value, int expectedRevision, string reason,
        Guid actor, CancellationToken ct) => WriteAsync(key, value, expectedRevision, reason, actor, null, ct);

    public async Task<PlatformSetting> RollbackAsync(string key, int targetRevision, int expectedRevision,
        string reason, Guid actor, CancellationToken ct)
    {
        var prior = await Revisions(key).SingleOrDefaultAsync(x => x.Revision == targetRevision, ct)
            ?? throw new KeyNotFoundException("The requested historical revision was not found.");
        if (targetRevision >= expectedRevision)
            throw new ArgumentException("Select a revision older than the current revision.");
        return await WriteAsync(key, prior.Value, expectedRevision, reason, actor, targetRevision, ct);
    }

    private async Task<PlatformSetting> WriteAsync(string key, long value, int expectedRevision, string reason,
        Guid actor, int? rollback, CancellationToken ct)
    {
        var latest = await Revisions(key).FirstOrDefaultAsync(ct);
        if ((latest?.Revision ?? 0) != expectedRevision)
            throw new DbUpdateConcurrencyException("The settings revision is stale.");
        if (value < 1 || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500)
            throw new ArgumentException("A reason of 3 to 500 characters is required.", nameof(reason));

        var now = clock.GetUtcNow();
        var row = new PlatformSettingRevision
        {
            Key = key, Revision = checked(expectedRevision + 1), Value = value,
            PreviousValue = latest?.Value ?? Maximum, ChangedBy = actor, ChangedAt = now,
            Reason = reason.Trim(), RolledBackTo = rollback
        };
        var audit = new AuditEvent
        {
            UserId = actor, Method = rollback is null ? "PUT" : "POST", StatusCode = 200,
            OccurredAt = now, IpAddress = "security-service",
            Path = $"/security/platform-settings/{key}/{row.Revision}/{(rollback is null ? "changed" : "rolled-back")}"
        };
        db.AddRange(row, audit);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception)
        {
            db.Entry(row).State = EntityState.Detached;
            db.Entry(audit).State = EntityState.Detached;
            if (DatabaseErrors.IsUniqueViolation(exception) ||
                await Revisions(key).AnyAsync(x => x.Revision == row.Revision, ct))
                throw new DbUpdateConcurrencyException("The settings revision is stale.", exception);
            throw;
        }
        return new(key, value, row.Revision, 1, Maximum);
    }

    private IOrderedQueryable<PlatformSettingRevision> Revisions(string key)
    {
        if (!string.Equals(key, IPlatformSettings.DocumentUploadMaxBytes, StringComparison.Ordinal))
            throw new KeyNotFoundException("The requested setting is not available.");
        return db.PlatformSettingRevisions.AsNoTracking().Where(x => x.Key == key)
            .OrderByDescending(x => x.Revision);
    }
}
