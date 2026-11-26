using System.Globalization;
using System.Text.RegularExpressions;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;

namespace Reqviora.Api.Features.Billing;

public static class PayHereNotificationValidator
{
    public static VerifiedPaymentNotification? Validate(
        string expectedMerchantId,
        string merchantSecret,
        string merchantId,
        string orderId,
        string paymentId,
        string amountText,
        string currency,
        string statusCode,
        string signature)
    {
        if (string.IsNullOrWhiteSpace(expectedMerchantId) ||
            string.IsNullOrWhiteSpace(merchantSecret) ||
            !string.Equals(
                expectedMerchantId,
                merchantId,
                StringComparison.Ordinal))
        {
            return null;
        }

        if (!Guid.TryParseExact(orderId, "N", out var id) ||
            id == Guid.Empty ||
            id.ToString("N") != orderId)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(paymentId) ||
            paymentId.Length > 128 ||
            paymentId.Trim() != paymentId)
        {
            return null;
        }

        if (currency is not ("USD" or "LKR"))
            return null;

        if (amountText.Length > 15 ||
            !Regex.IsMatch(
                amountText,
                @"\A[0-9]+(?:\.[0-9]{1,2})?\z") ||
            !decimal.TryParse(
                amountText,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount) ||
            amount <= 0 ||
            amount > 999999999.99m)
        {
            return null;
        }

        ProjectPaymentStatus? status = statusCode switch
        {
            "2" => ProjectPaymentStatus.Succeeded,
            "0" => ProjectPaymentStatus.Pending,
            "-1" => ProjectPaymentStatus.Cancelled,
            "-2" => ProjectPaymentStatus.Failed,
            "-3" => ProjectPaymentStatus.ChargedBack,
            _ => null
        };

        if (status is null)
            return null;

        if (!PayHereSignature.VerifyNotification(
            merchantId,
            orderId,
            amountText,
            currency,
            statusCode,
            signature,
            merchantSecret))
        {
            return null;
        }

        return new(
            id,
            paymentId,
            amount,
            currency,
            status.Value);
    }
}
