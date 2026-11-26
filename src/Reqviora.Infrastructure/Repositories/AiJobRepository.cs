using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Repositories;

public sealed class AiJobRepository(ReqvioraDbContext db) : IAiJobRepository
{
    public async Task<(AiJob Job, bool Created)> GetOrCreateAsync(
        AiJob job,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.AiJobs.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.OwnerId == job.OwnerId &&
                candidate.ProjectId == job.ProjectId &&
                candidate.IdempotencyKey == job.IdempotencyKey,
            cancellationToken);
        if (existing is not null) return (existing, false);

        db.AiJobs.Add(job);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (job, true);
        }
        catch (DbUpdateException)
        {
            db.Entry(job).State = EntityState.Detached;
            existing = await db.AiJobs.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.OwnerId == job.OwnerId &&
                    candidate.ProjectId == job.ProjectId &&
                    candidate.IdempotencyKey == job.IdempotencyKey,
                cancellationToken);
            if (existing is null) throw;
            return (existing, false);
        }
    }

    public Task<AiJob?> GetOwnedAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        db.AiJobs.AsNoTracking().SingleOrDefaultAsync(
            job => job.Id == id && job.OwnerId == ownerId,
            cancellationToken);

    public async Task<AiJob?> TryLeaseNextAsync(
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan minimumLease,
        CancellationToken cancellationToken = default)
    {
        var nowUnix = now.ToUnixTimeMilliseconds();
        await FinalizeUnrecoverableAsync(now, nowUnix, cancellationToken);

        var candidates = await db.AiJobs.AsNoTracking()
            .Where(job => job.CancellationRequestedAt == null &&
                job.AvailableAtUnixTimeMilliseconds <= nowUnix &&
                job.AttemptCount < job.MaxAttempts &&
                (job.State == AiJobState.Pending ||
                 job.State == AiJobState.Running &&
                 job.LeaseExpiresAtUnixTimeMilliseconds <= nowUnix))
            .OrderBy(job => job.AttemptCount)
            .ThenBy(job => job.Id)
            .Select(job => new
            {
                job.Id,
                job.StartedAt,
                job.TimeoutSeconds,
                job.ConcurrencyToken
            })
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var leaseDuration = TimeSpan.FromSeconds(Math.Max(
                minimumLease.TotalSeconds,
                candidate.TimeoutSeconds + 30d));
            var leaseExpiresAt = now.Add(leaseDuration).ToUnixTimeMilliseconds();
            var concurrencyToken = Guid.NewGuid();
            var updated = await db.AiJobs
                .Where(job => job.Id == candidate.Id &&
                    job.ConcurrencyToken == candidate.ConcurrencyToken &&
                    job.CancellationRequestedAt == null &&
                    job.AvailableAtUnixTimeMilliseconds <= nowUnix &&
                    job.AttemptCount < job.MaxAttempts &&
                    (job.State == AiJobState.Pending ||
                     job.State == AiJobState.Running &&
                     job.LeaseExpiresAtUnixTimeMilliseconds <= nowUnix))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(job => job.State, AiJobState.Running)
                    .SetProperty(job => job.LeaseOwner, leaseOwner)
                    .SetProperty(job => job.LeaseExpiresAtUnixTimeMilliseconds, leaseExpiresAt)
                    .SetProperty(job => job.StartedAt, candidate.StartedAt ?? now)
                    .SetProperty(job => job.AttemptCount, job => job.AttemptCount + 1)
                    .SetProperty(job => job.ErrorCode, (string?)null)
                    .SetProperty(job => job.ErrorMessage, (string?)null)
                    .SetProperty(job => job.LastModifiedAt, now)
                    .SetProperty(job => job.ConcurrencyToken, concurrencyToken),
                    cancellationToken);
            if (updated == 1)
                return await db.AiJobs.AsNoTracking().SingleAsync(
                    job => job.Id == candidate.Id,
                    cancellationToken);
        }

        return null;
    }

    public async Task<bool> CompleteAsync(
        Guid id,
        string leaseOwner,
        string result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default) =>
        await db.AiJobs
            .Where(job => job.Id == id &&
                job.State == AiJobState.Running &&
                job.LeaseOwner == leaseOwner &&
                job.CancellationRequestedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, AiJobState.Completed)
                .SetProperty(job => job.Result, result)
                .SetProperty(job => job.ErrorCode, (string?)null)
                .SetProperty(job => job.ErrorMessage, (string?)null)
                .SetProperty(job => job.CompletedAt, completedAt)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAtUnixTimeMilliseconds, (long?)null)
                .SetProperty(job => job.LastModifiedAt, completedAt)
                .SetProperty(job => job.ConcurrencyToken, Guid.NewGuid()),
                cancellationToken) == 1;

    public async Task<AiJobFailureDisposition> FailAsync(
        Guid id,
        string leaseOwner,
        string errorCode,
        string safeMessage,
        DateTimeOffset now,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        var job = await db.AiJobs.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == id &&
                candidate.State == AiJobState.Running &&
                candidate.LeaseOwner == leaseOwner,
            cancellationToken);
        if (job is null) return AiJobFailureDisposition.LeaseLost;
        if (job.CancellationRequestedAt is not null)
            return await CancelLeasedAsync(id, leaseOwner, now, cancellationToken)
                ? AiJobFailureDisposition.Cancelled
                : AiJobFailureDisposition.LeaseLost;

        var exhausted = job.AttemptCount >= job.MaxAttempts;
        var updated = await db.AiJobs
            .Where(candidate => candidate.Id == id &&
                candidate.State == AiJobState.Running &&
                candidate.LeaseOwner == leaseOwner &&
                candidate.CancellationRequestedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.State,
                    exhausted ? AiJobState.Failed : AiJobState.Pending)
                .SetProperty(candidate => candidate.ErrorCode, errorCode)
                .SetProperty(candidate => candidate.ErrorMessage, safeMessage)
                .SetProperty(candidate => candidate.AvailableAtUnixTimeMilliseconds,
                    exhausted ? now.ToUnixTimeMilliseconds() : now.Add(retryDelay).ToUnixTimeMilliseconds())
                .SetProperty(candidate => candidate.CompletedAt,
                    exhausted ? now : (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.LeaseOwner, (string?)null)
                .SetProperty(candidate => candidate.LeaseExpiresAtUnixTimeMilliseconds, (long?)null)
                .SetProperty(candidate => candidate.LastModifiedAt, now)
                .SetProperty(candidate => candidate.ConcurrencyToken, Guid.NewGuid()),
                cancellationToken);
        if (updated != 1) return AiJobFailureDisposition.LeaseLost;
        return exhausted ? AiJobFailureDisposition.Failed : AiJobFailureDisposition.Retrying;
    }

    public async Task<AiJob?> RequestCancellationAsync(
        Guid id,
        Guid ownerId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        var cancelledPending = await db.AiJobs
            .Where(job => job.Id == id && job.OwnerId == ownerId && job.State == AiJobState.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, AiJobState.Cancelled)
                .SetProperty(job => job.CancellationRequestedAt, requestedAt)
                .SetProperty(job => job.CompletedAt, requestedAt)
                .SetProperty(job => job.LastModifiedAt, requestedAt)
                .SetProperty(job => job.ConcurrencyToken, Guid.NewGuid()),
                cancellationToken);

        if (cancelledPending == 0)
        {
            await db.AiJobs
                .Where(job => job.Id == id &&
                    job.OwnerId == ownerId &&
                    job.State == AiJobState.Running &&
                    job.CancellationRequestedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(job => job.CancellationRequestedAt, requestedAt)
                    .SetProperty(job => job.LastModifiedAt, requestedAt)
                    .SetProperty(job => job.ConcurrencyToken, Guid.NewGuid()),
                    cancellationToken);
        }

        return await GetOwnedAsync(id, ownerId, cancellationToken);
    }

    public Task<bool> IsCancellationRequestedAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        db.AiJobs.AsNoTracking().AnyAsync(
            job => job.Id == id && job.CancellationRequestedAt != null,
            cancellationToken);

    public async Task<bool> CancelLeasedAsync(
        Guid id,
        string leaseOwner,
        DateTimeOffset cancelledAt,
        CancellationToken cancellationToken = default) =>
        await db.AiJobs
            .Where(job => job.Id == id &&
                job.State == AiJobState.Running &&
                job.LeaseOwner == leaseOwner &&
                job.CancellationRequestedAt != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, AiJobState.Cancelled)
                .SetProperty(job => job.CompletedAt, cancelledAt)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAtUnixTimeMilliseconds, (long?)null)
                .SetProperty(job => job.LastModifiedAt, cancelledAt)
                .SetProperty(job => job.ConcurrencyToken, Guid.NewGuid()),
                cancellationToken) == 1;

    private async Task FinalizeUnrecoverableAsync(
        DateTimeOffset now,
        long nowUnix,
        CancellationToken cancellationToken)
    {
        await db.AiJobs
            .Where(job => job.State == AiJobState.Pending && job.CancellationRequestedAt != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, AiJobState.Cancelled)
                .SetProperty(job => job.CompletedAt, now)
                .SetProperty(job => job.LastModifiedAt, now)
                .SetProperty(job => job.ConcurrencyToken, Guid.NewGuid()),
                cancellationToken);

        await db.AiJobs
            .Where(job => job.State == AiJobState.Running &&
                job.LeaseExpiresAtUnixTimeMilliseconds <= nowUnix &&
                job.CancellationRequestedAt != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, AiJobState.Cancelled)
                .SetProperty(job => job.CompletedAt, now)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAtUnixTimeMilliseconds, (long?)null)
                .SetProperty(job => job.LastModifiedAt, now)
                .SetProperty(job => job.ConcurrencyToken, Guid.NewGuid()),
                cancellationToken);

        await db.AiJobs
            .Where(job => job.State == AiJobState.Running &&
                job.LeaseExpiresAtUnixTimeMilliseconds <= nowUnix &&
                job.AttemptCount >= job.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, AiJobState.Failed)
                .SetProperty(job => job.ErrorCode, "worker_lease_expired")
                .SetProperty(job => job.ErrorMessage, "AI processing could not be completed.")
                .SetProperty(job => job.CompletedAt, now)
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseExpiresAtUnixTimeMilliseconds, (long?)null)
                .SetProperty(job => job.LastModifiedAt, now)
                .SetProperty(job => job.ConcurrencyToken, Guid.NewGuid()),
                cancellationToken);
    }
}
