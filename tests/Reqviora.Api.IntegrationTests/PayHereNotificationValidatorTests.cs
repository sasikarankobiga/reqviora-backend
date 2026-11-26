using System.Security.Cryptography;
using System.Text;
using Reqviora.Api.Features.Billing;
using Reqviora.Domain.Entities;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class PayHereNotificationValidatorTests
{
    private readonly string secret =
        Guid.NewGuid().ToString("N");

    private readonly string orderId =
        Guid.NewGuid().ToString("N");

    private static string Hash(string value) =>
        Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private string Signature(
        string merchant = "sandbox-merchant",
        string amount = "9.99",
        string currency = "USD",
        string status = "2")
    {
        return Hash(
            merchant + orderId + amount +
            currency + status + Hash(secret));
    }

    private Reqviora.Application.Features.Billing
        .VerifiedPaymentNotification? Validate(
            string? signature = null,
            string merchant = "sandbox-merchant",
            string amount = "9.99",
            string currency = "USD",
            string status = "2",
            string paymentId = "TEST-001")
    {
        return PayHereNotificationValidator.Validate(
            "sandbox-merchant",
            secret,
            merchant,
            orderId,
            paymentId,
            amount,
            currency,
            status,
            signature ?? Signature(
                merchant, amount, currency, status));
    }

    [Theory]
    [InlineData("2", ProjectPaymentStatus.Succeeded)]
    [InlineData("0", ProjectPaymentStatus.Pending)]
    [InlineData("-1", ProjectPaymentStatus.Cancelled)]
    [InlineData("-2", ProjectPaymentStatus.Failed)]
    [InlineData("-3", ProjectPaymentStatus.ChargedBack)]
    public void Valid_signature_maps_gateway_status(
        string code,
        ProjectPaymentStatus expected)
    {
        var result = Validate(status: code);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Status);
        Assert.Equal(9.99m, result.Amount);
        Assert.Equal(Guid.Parse(orderId), result.OrderId);
    }

    [Fact]
    public void Invalid_signature_is_rejected()
    {
        Assert.Null(Validate(signature: "invalid"));
    }

    [Fact]
    public void Incorrect_merchant_is_rejected()
    {
        Assert.Null(Validate(merchant: "other-merchant"));
    }

    [Fact]
    public void Tampered_amount_is_rejected()
    {
        Assert.Null(Validate(
            signature: Signature(), amount: "0.01"));
    }

    [Fact]
    public void Tampered_currency_is_rejected()
    {
        Assert.Null(Validate(
            signature: Signature(), currency: "LKR"));
    }

    [Fact]
    public void Tampered_status_is_rejected()
    {
        Assert.Null(Validate(
            signature: Signature(status: "0"),
            status: "2"));
    }

    [Fact]
    public void Unknown_status_is_rejected()
    {
        Assert.Null(Validate(status: "99"));
    }

    [Fact]
    public void Unsupported_currency_is_rejected()
    {
        Assert.Null(Validate(currency: "XYZ"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-9.99")]
    [InlineData("9.999")]
    [InlineData("9,99")]
    public void Invalid_amount_is_rejected(string amount)
    {
        Assert.Null(Validate(amount: amount));
    }

    [Fact]
    public void Missing_payment_reference_is_rejected()
    {
        Assert.Null(Validate(paymentId: ""));
    }
}
