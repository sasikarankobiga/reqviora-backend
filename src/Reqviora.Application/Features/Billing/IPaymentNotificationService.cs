using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Billing;

public sealed record VerifiedPaymentNotification(
    Guid OrderId,
    string GatewayPaymentId,
    decimal Amount,
    string Currency,
    ProjectPaymentStatus Status);

public enum PaymentNotificationResult
{
    Applied,
    DuplicateOrStale,
    OrderNotFound,
    PaymentMismatch,
    RetryRequired
}

public interface IPaymentNotificationService
{
    Task<PaymentNotificationResult> ApplyAsync(
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken = default);
}
