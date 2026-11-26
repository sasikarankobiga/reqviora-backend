namespace Reqviora.Application.Features.Billing;

public sealed record BillingPricingResponse(long AmountMinor, string Currency, string DisplayAmount,
    bool GatewayEnabled, bool ProjectOverride);

public sealed record BillingCheckoutContact(string FirstName, string LastName, string Email, string Phone,
    string Address, string City, string Country);

public sealed record BillingCheckoutResponse(Guid PaymentId, string OrderId, string ActionUrl,
    IReadOnlyDictionary<string, string> Fields, DateTimeOffset ExpiresAt);

public sealed record PaymentDetails(Guid Id, string OrderId, Guid ProjectId, Guid RequestedBy, long AmountMinor,
    string Currency, string Status, string? ProviderPaymentId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt, bool Entitled);

public sealed record PaymentHistoryPage(IReadOnlyList<PaymentDetails> Items, int Page, int PageSize, int Total);

public sealed record PaymentSummary(int Pending, int Processing, int Succeeded, int Failed, int Cancelled,
    int Refunded, int Disputed, long SucceededAmountMinor, string Currency);

public sealed record PayHereNotification(string MerchantId, string OrderId, string PaymentId, string Amount,
    string Currency, string StatusCode, string Signature, string? Method, string? StatusMessage);

public sealed record PaymentNotificationResult(Guid PaymentId, string Status, bool Duplicate);

public interface IBillingService
{
    Task<BillingPricingResponse> GetPricingAsync(Guid? projectId, Guid userId, CancellationToken ct);
    Task<BillingCheckoutResponse> CreateCheckoutAsync(Guid projectId, Guid userId,
        BillingCheckoutContact contact, CancellationToken ct);
    Task<PaymentDetails> GetProjectStatusAsync(Guid projectId, Guid userId, CancellationToken ct);
    Task<PaymentHistoryPage> GetProjectHistoryAsync(Guid projectId, Guid userId, int page, int pageSize,
        CancellationToken ct);
    Task<PaymentDetails> GetPaymentAsync(Guid paymentId, Guid userId, bool administrator, CancellationToken ct);
    Task<PaymentNotificationResult> ProcessNotificationAsync(PayHereNotification notification,
        CancellationToken ct);
    Task<PaymentHistoryPage> SearchAdminAsync(string? status, Guid? projectId, int page, int pageSize,
        CancellationToken ct);
    Task<PaymentSummary> GetSummaryAsync(CancellationToken ct);
    Task<BillingPricingResponse> SetProjectPriceAsync(Guid projectId, long amountMinor, Guid administratorId,
        CancellationToken ct);
    Task<PaymentDetails> RequestRefundAsync(Guid paymentId, string reason, Guid administratorId,
        CancellationToken ct);
}
