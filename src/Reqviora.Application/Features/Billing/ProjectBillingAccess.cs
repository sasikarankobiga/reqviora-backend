namespace Reqviora.Application.Features.Billing;

public sealed record ProjectBillingSnapshot(
    Guid ProjectId,
    DateTimeOffset ProjectCreatedAt,
    bool HasSuccessfulPayment,
    bool HasPendingPayment);

public interface IProjectBillingAccess
{
    Task<ProjectBillingSnapshot?> ReadAsync(
        Guid projectId,
        Guid actorId,
        CancellationToken cancellationToken = default);
}
