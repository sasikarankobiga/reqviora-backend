
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Reqviora.Api.Features.Billing;

public static class PayHereSignature
{
    private static string Md5Upper(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(MD5.HashData(bytes));
    }

    public static string CreateCheckoutHash(
        string merchantId,
        string orderId,
        decimal amount,
        string currency,
        string merchantSecret)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));

        var formattedAmount =
            amount.ToString("0.00", CultureInfo.InvariantCulture);

        var secretHash = Md5Upper(merchantSecret);

        return Md5Upper(
            merchantId +
            orderId +
            formattedAmount +
            currency +
            secretHash);
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
        if (string.IsNullOrWhiteSpace(signature) ||
            signature.Length != 32 ||
            !signature.All(Uri.IsHexDigit))
        {
            return false;
        }

        var expected = Md5Upper(
            merchantId +
            orderId +
            amountText +
            currency +
            statusCode +
            Md5Upper(merchantSecret));

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expected),
            Convert.FromHexString(signature));
    }
}
