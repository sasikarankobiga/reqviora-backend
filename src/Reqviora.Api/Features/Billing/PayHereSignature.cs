
using Reqviora.Application.Features.Billing;

namespace Reqviora.Api.Features.Billing;

public static class PayHereSignature
{
    public static string CreateCheckoutHash(
        string merchantId,
        string orderId,
        decimal amount,
        string currency,
        string merchantSecret)
    {
        return PayHereSecurity.CreateCheckoutHash(merchantId, orderId, amount, currency, merchantSecret);
    }

    public static bool VerifyNotification(
        string merchantId,
        string orderId,
        string amountText,
        string currency,
        string statusCode,
        string signature,
        string merchantSecret)
    {
        return PayHereSecurity.VerifyNotification(merchantId, orderId, amountText, currency, statusCode,
            signature, merchantSecret);
    }
}
