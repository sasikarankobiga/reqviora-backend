namespace Reqviora.Application.Common.Interfaces;

public sealed record AiJobStatus(
    Guid Id,
    Guid ProjectId,
    Guid OwnerId,
    string State,
    string? Result,
    string? ErrorCode,
    string? Error,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset AvailableAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancellationRequestedAt);

public sealed record AiJobEnqueueResult(AiJobStatus Job, bool Created);

public interface IAIJobQueue
{
    Task<AiJobEnqueueResult> EnqueueAsync(
        Guid ownerId,
        Guid projectId,
        string prompt,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AiJobStatus?> GetAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<AiJobStatus?> CancelAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
