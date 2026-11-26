namespace Reqviora.Infrastructure.Services;

public sealed class BillingOptions
{
    public const string SectionName = "Billing";
    public bool Enabled { get; init; }
    public bool Sandbox { get; init; } = true;
    public string MerchantId { get; init; } = string.Empty;
    public string MerchantSecret { get; init; } = string.Empty;
    public long DefaultProjectPriceUsdMinor { get; init; }
    public string ReturnUrl { get; init; } = string.Empty;
    public string CancelUrl { get; init; } = string.Empty;
    public string NotifyUrl { get; init; } = string.Empty;
    public int CheckoutExpiryMinutes { get; init; } = 30;
    public bool RefundsEnabled { get; init; }
    public string AppId { get; init; } = string.Empty;
    public string AppSecret { get; init; } = string.Empty;
}

public interface IPayHereRefundGateway
{
    Task<string> RefundAsync(string paymentId, string reason, long amountMinor, CancellationToken ct);
}
