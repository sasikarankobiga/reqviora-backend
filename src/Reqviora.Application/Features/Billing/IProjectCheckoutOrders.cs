using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Billing;

public enum CheckoutOrderResultCode
{
    Created,
    NotFound,
    NotEligible,
    AlreadyPaid,
    PendingPayment
}

public sealed record CheckoutOrderResult(
    CheckoutOrderResultCode Code,
    ProjectPaymentOrder? Order = null,
    string? CustomerEmail = null);

public interface IProjectCheckoutOrders
{
    Task<CheckoutOrderResult> CreateAsync(
        Guid projectId,
        Guid actorId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);
}
