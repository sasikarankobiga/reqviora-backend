namespace Reqviora.Application.Features.AIOrchestration;

public sealed record AdminAiJob(Guid Id, Guid ProjectId, string Type, string Status, int Attempts, int MaxAttempts,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, long? DurationMilliseconds,
    string? FailureCategory, bool ExpiredLease, bool Stale);

public sealed record AdminAiJobPage(int Page, int PageSize, int Total, IReadOnlyList<AdminAiJob> Items);

public sealed record AiQueueStatistics(int Pending, int Processing, int Completed, int Failed, int Cancelled,
    long RetryAttempts, int RetriedJobs, int ExpiredLeases, int StaleJobs, double? OldestPendingSeconds,
    double? AveragePendingAgeSeconds, double? AverageProcessingMilliseconds, int ProviderFailures, int Timeouts);

public sealed record AiQueueHealth(string ProviderStatus, string LocalWorkerStatus, DateTimeOffset? LastLocalPoll,
    string QueueStatus, int StaleJobs);

public interface IAiQueueMonitoring
{
    Task<AdminAiJobPage> ListAsync(int page, int pageSize, string? status, string? type,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
    Task<AdminAiJob?> FindAsync(Guid id, CancellationToken ct);
    Task<AiQueueStatistics> StatisticsAsync(string? type, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
    Task<AiQueueHealth> HealthAsync(CancellationToken ct);
}
