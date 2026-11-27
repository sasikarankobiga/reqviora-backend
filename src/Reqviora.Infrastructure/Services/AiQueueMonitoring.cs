using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class AiQueueMonitoring(ReqvioraDbContext db, TimeProvider clock, IOptions<AiOptions> ai,
    IOptions<AiJobOptions> worker, AiWorkerSignal signal) : IAiQueueMonitoring
{
    // The existing durable queue supports one operation. Input intelligence uses a separate synchronous flow.
    private const string JobType = "completion";
    private bool Sqlite => db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

    public async Task<AdminAiJobPage> ListAsync(int page, int pageSize, string? status, string? type,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = Select(status, type, from, to);
        var offset = (page - 1) * pageSize;
        var now = clock.GetUtcNow();
        if (Sqlite)
        {
            var rows = (await query.ToArrayAsync(ct)).Where(x => InRange(x, from, to))
                .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
            return new(page, pageSize, rows.Length, rows.Skip(offset).Take(pageSize).Select(x => ToDto(x, now)).ToArray());
        }
        var total = await query.CountAsync(ct);
        var results = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(offset).Take(pageSize).ToArrayAsync(ct);
        return new(page, pageSize, total, results.Select(x => ToDto(x, now)).ToArray());
    }

    public async Task<AdminAiJob?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await Select(null, null, null, null).SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : ToDto(row, clock.GetUtcNow());
    }

    public async Task<AiQueueStatistics> StatisticsAsync(string? type, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var counts = new int[5];
        long retries = 0, durations = 0;
        int retried = 0, expired = 0, stale = 0, durationSamples = 0, providerFailures = 0, timeouts = 0;
        double ageTotal = 0;
        double? oldestAge = null;
        await foreach (var row in Select(null, type, from, to).AsAsyncEnumerable().WithCancellation(ct))
        {
            if (Sqlite && !InRange(row, from, to)) continue;
            counts[(int)row.State]++;
            if (row.AttemptCount > 1)
            {
                retries += row.AttemptCount - 1;
                retried++;
            }
            if (row.State == AiJobState.Pending)
            {
                var age = Math.Max(0, (now - row.CreatedAt).TotalSeconds);
                ageTotal += age;
                oldestAge = Math.Max(oldestAge ?? 0, age);
            }
            if (LeaseExpired(row, now)) expired++;
            if (IsStale(row, now)) stale++;
            if (Duration(row) is { } elapsed)
            {
                durationSamples++;
                durations += elapsed;
            }
            if (row.ErrorCode == "provider_error") providerFailures++;
            if (row.ErrorCode == "timeout") timeouts++;
        }
        return new(counts[0], counts[1], counts[2], counts[3], counts[4], retries, retried, expired, stale,
            oldestAge, counts[0] == 0 ? null : ageTotal / counts[0],
            durationSamples == 0 ? null : (double)durations / durationSamples, providerFailures, timeouts);
    }

    public async Task<AiQueueHealth> HealthAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var epoch = now.ToUnixTimeMilliseconds();
        var stale = await db.AiJobs.CountAsync(job => job.State == AiJobState.Running &&
            (job.LeaseExpiresAtUnixTimeMilliseconds == null || job.LeaseExpiresAtUnixTimeMilliseconds <= epoch), ct);
        var lastPoll = signal.LastPoll;
        var silence = TimeSpan.FromSeconds(Math.Max(Math.Clamp(worker.Value.LeaseSeconds, 30, 3600),
            Math.Clamp(ai.Value.TimeoutSeconds, 10, 600) + 30));
        var localStatus = lastPoll is null ? "unknown" :
            now - lastPoll.At > silence || !lastPoll.Succeeded ? "degraded" : "healthy";
        var providerStatus = await ProviderStatusAsync(now, ct);
        var queueStatus = stale > 0 || localStatus == "degraded" ? "degraded" : localStatus;
        return new(providerStatus, localStatus, lastPoll?.At, queueStatus, stale);
    }

    private async Task<string> ProviderStatusAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!ai.Value.Enabled) return "disabled";
        if (string.IsNullOrWhiteSpace(ai.Value.ApiKey) || string.IsNullOrWhiteSpace(ai.Value.Model))
            return "unavailable";

        var cutoff = now.AddMinutes(-30);
        var evidence = db.AiJobs.AsNoTracking().Where(j => j.CompletedAt != null &&
            (j.State == AiJobState.Completed ||
                (j.State == AiJobState.Failed && (j.ErrorCode == "provider_error" || j.ErrorCode == "timeout"))));
        if (!Sqlite) evidence = evidence.Where(j => j.CompletedAt >= cutoff);
        var success = false;
        var failure = false;
        await foreach (var job in evidence.Select(j => new { j.State, j.CompletedAt })
            .AsAsyncEnumerable().WithCancellation(ct))
        {
            if (job.CompletedAt < cutoff) continue;
            if (job.State == AiJobState.Completed) success = true;
            else failure = true;
            if (success && failure) break;
        }
        if (failure) return success ? "degraded" : "unavailable";
        return success ? "healthy" : "unknown";
    }

    private IQueryable<JobRow> Select(string? status, string? type, DateTimeOffset? from, DateTimeOffset? to)
    {
        from = from?.ToUniversalTime();
        to = to?.ToUniversalTime();
        if (from > to) throw new ArgumentException("The requested date range is invalid.");
        var query = db.AiJobs.AsNoTracking();
        if (type is not null && !string.Equals(type, JobType, StringComparison.OrdinalIgnoreCase))
            return Project(query.Where(_ => false));
        if (status is not null)
        {
            var state = status.ToLowerInvariant() switch
            {
                "pending" => AiJobState.Pending, "processing" => AiJobState.Running,
                "completed" => AiJobState.Completed, "failed" => AiJobState.Failed,
                "cancelled" => AiJobState.Cancelled,
                _ => throw new ArgumentException("The requested AI job status is unsupported.")
            };
            query = query.Where(x => x.State == state);
        }
        if (!Sqlite)
        {
            if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
            if (to.HasValue) query = query.Where(x => x.CreatedAt <= to.Value);
        }
        return Project(query);
    }

    private static IQueryable<JobRow> Project(IQueryable<AiJob> query) => query.Select(x => new JobRow
    {
        Id = x.Id, ProjectId = x.ProjectId, State = x.State, AttemptCount = x.AttemptCount,
        MaxAttempts = x.MaxAttempts, CreatedAt = x.CreatedAt, StartedAt = x.StartedAt,
        CompletedAt = x.CompletedAt, Lease = x.LeaseExpiresAtUnixTimeMilliseconds, ErrorCode = x.ErrorCode
    });
    private static bool InRange(JobRow x, DateTimeOffset? from, DateTimeOffset? to) =>
        (!from.HasValue || x.CreatedAt >= from) && (!to.HasValue || x.CreatedAt <= to);
    private static bool LeaseExpired(JobRow row, DateTimeOffset now) =>
        row.State == AiJobState.Running && row.Lease <= now.ToUnixTimeMilliseconds();
    private static bool IsStale(JobRow row, DateTimeOffset now) =>
        row.State == AiJobState.Running && (row.Lease is null || LeaseExpired(row, now));
    private static long? Duration(JobRow row) => row.CompletedAt is { } end && row.StartedAt is { } start
        ? Math.Max(0, (long)(end - start).TotalMilliseconds) : null;
    private static AdminAiJob ToDto(JobRow row, DateTimeOffset now) => new(row.Id, row.ProjectId, JobType,
        row.State == AiJobState.Running ? "processing" : row.State.ToString().ToLowerInvariant(),
        row.AttemptCount, row.MaxAttempts, row.CreatedAt, row.StartedAt, row.CompletedAt, Duration(row),
        row.ErrorCode switch
        {
            "provider_error" => "provider",
            "timeout" => "timeout",
            "worker_lease_expired" => "expired_lease",
            null => null,
            _ => "other"
        }, LeaseExpired(row, now), IsStale(row, now));

    private sealed class JobRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public AiJobState State { get; init; }
        public int AttemptCount { get; init; }
        public int MaxAttempts { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public long? Lease { get; init; }
        public string? ErrorCode { get; init; }
    }
}
