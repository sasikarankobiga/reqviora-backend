using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Billing;

public sealed record VerifiedPaymentNotification(
    Guid OrderId,
    string GatewayPaymentId,
    decimal Amount,
    string Currency,
    ProjectPaymentStatus Status);

public enum ProjectPaymentNotificationResult
{
    Applied,
    DuplicateOrStale,
    OrderNotFound,
    PaymentMismatch,
    RetryRequired
}

public interface IPaymentNotificationService
{
    Task<ProjectPaymentNotificationResult> ApplyAsync(
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken = default);
}
