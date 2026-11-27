namespace Reqviora.Application.Features.Billing;

public sealed record PaymentHistoryItem(
    Guid PaymentId,
    Guid ProjectId,
    string OrderReference,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool ReceiptAvailable);

public sealed record ProjectPaymentHistoryPage(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<PaymentHistoryItem> Items);

public interface IProjectPaymentHistory
{
    Task<ProjectPaymentHistoryPage?> ListAsync(
        Guid projectId,
        Guid actorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PaymentHistoryItem?> FindAsync(
        Guid projectId,
        Guid actorId,
        Guid paymentId,
        CancellationToken cancellationToken = default);
}
