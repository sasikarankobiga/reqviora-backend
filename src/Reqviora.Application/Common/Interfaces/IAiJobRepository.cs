using Reqviora.Domain.Entities;

namespace Reqviora.Application.Common.Interfaces;

public enum AiJobFailureDisposition
{
    Retrying,
    Failed,
    Cancelled,
    LeaseLost
}

public interface IAiJobRepository
{
    Task<(AiJob Job, bool Created)> GetOrCreateAsync(
        AiJob job,
        CancellationToken cancellationToken = default);

    Task<AiJob?> GetOwnedAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<AiJob?> TryLeaseNextAsync(
        string leaseOwner,
        DateTimeOffset now,
        TimeSpan minimumLease,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        Guid id,
        string leaseOwner,
        string result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task<AiJobFailureDisposition> FailAsync(
        Guid id,
        string leaseOwner,
        string errorCode,
        string safeMessage,
        DateTimeOffset now,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);

    Task<AiJob?> RequestCancellationAsync(
        Guid id,
        Guid ownerId,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default);

    Task<bool> IsCancellationRequestedAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> CancelLeasedAsync(
        Guid id,
        string leaseOwner,
        DateTimeOffset cancelledAt,
        CancellationToken cancellationToken = default);
}
