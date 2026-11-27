namespace Reqviora.Application.Features.Billing;

public sealed record AdminPaymentItem(
    Guid PaymentId,
    Guid ProjectId,
    Guid PayerId,
    string OrderReference,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminPaymentPage(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<AdminPaymentItem> Items);

public sealed record AdminBillingSummary(
    string Currency,
    int TotalOrders,
    int SuccessfulOrders,
    int PendingOrders,
    int FailedOrders,
    int CancelledOrders,
    int ChargebackOrders,
    decimal SuccessfulOrderAmount,
    decimal ChargebackAmount);

public interface IAdminBillingService
{
    Task<AdminPaymentPage> ListAsync(
        int page,
        int pageSize,
        Guid? projectId = null,
        string? status = null,
        string? currency = null,
        CancellationToken cancellationToken = default);

    Task<AdminPaymentItem?> FindAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminBillingSummary>> SummaryAsync(
        CancellationToken cancellationToken = default);
}
