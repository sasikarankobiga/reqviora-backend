using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Authorization;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class PersistentAiJobService(
    IAiJobRepository repository,
    ReqvioraDbContext db,
    IOptions<AiOptions> aiOptions,
    IOptions<AiJobOptions> jobOptions,
    TimeProvider clock) : IAIJobQueue
{
    public async Task<AiJobEnqueueResult> EnqueueAsync(
        Guid ownerId,
        Guid projectId,
        string prompt,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("A job owner is required.", nameof(ownerId));
        if (projectId == Guid.Empty)
            throw new ArgumentException("A project is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > jobOptions.Value.MaxPromptCharacters)
            throw new ArgumentException("Prompt is empty or too large.", nameof(prompt));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128 ||
            idempotencyKey.Any(character => char.IsControl(character)))
            throw new ArgumentException("A valid Idempotency-Key is required.", nameof(idempotencyKey));

        await ProjectOwnerAccess.RequireWriteAsync(db, projectId, ownerId, cancellationToken);
        var now = clock.GetUtcNow();
        var job = new AiJob
        {
            ProjectId = projectId,
            OwnerId = ownerId,
            Prompt = prompt.Trim(),
            IdempotencyKey = idempotencyKey.Trim(),
            State = AiJobState.Pending,
            MaxAttempts = Math.Clamp(jobOptions.Value.MaxAttempts, 1, 10),
            TimeoutSeconds = Math.Clamp(aiOptions.Value.TimeoutSeconds, 10, 600),
            AvailableAtUnixTimeMilliseconds = now.ToUnixTimeMilliseconds(),
            CreatedAt = now
        };
        var (persisted, created) = await repository.GetOrCreateAsync(job, cancellationToken);
        return new AiJobEnqueueResult(ToStatus(persisted), created);
    }

    public async Task<AiJobStatus?> GetAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        await repository.GetOwnedAsync(id, ownerId, cancellationToken) is { } job
            ? ToStatus(job)
            : null;

    public async Task<AiJobStatus?> CancelAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        await repository.RequestCancellationAsync(id, ownerId, clock.GetUtcNow(), cancellationToken) is { } job
            ? ToStatus(job)
            : null;

    internal static AiJobStatus ToStatus(AiJob job) => new(
        job.Id,
        job.ProjectId,
        job.OwnerId,
        job.State.ToString().ToLowerInvariant(),
        job.Result,
        job.ErrorCode,
        job.ErrorMessage,
        job.AttemptCount,
        job.MaxAttempts,
        DateTimeOffset.FromUnixTimeMilliseconds(job.AvailableAtUnixTimeMilliseconds),
        job.CreatedAt,
        job.StartedAt,
        job.CompletedAt,
        job.CancellationRequestedAt);
}
