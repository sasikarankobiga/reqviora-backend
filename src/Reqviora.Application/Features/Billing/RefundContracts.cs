namespace Reqviora.Application.Features.Billing;

public sealed record RefundRequestItem(
    Guid PaymentId,
    string Reason,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ReviewedAt,
    bool Sandbox = true,
    bool RefundExecuted = false);

public sealed record RefundResult(
    string Code,
    RefundRequestItem? Item = null);

public sealed record RefundRequestPage(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<RefundRequestItem> Items);

public interface IPaymentRefundService
{
    Task<RefundResult> RequestAsync(
        Guid projectId, Guid paymentId, Guid actorId,
        string reason, CancellationToken ct = default);

    Task<RefundRequestItem?> FindAsync(
        Guid projectId, Guid paymentId, Guid actorId,
        CancellationToken ct = default);

    Task<RefundRequestPage> ListAdminAsync(
        int page, int pageSize,
        CancellationToken ct = default);

    Task<RefundResult> ReviewAsync(
        Guid paymentId, Guid actorId, string decision,
        CancellationToken ct = default);
}
