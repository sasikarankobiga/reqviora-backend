
using Reqviora.Api.Features.Billing;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class PayHereSignatureTests
{
    [Fact]
    public void Checkout_hash_is_deterministic()
    {
        var first = PayHereSignature.CreateCheckoutHash(
            "123456", "ORDER-001", 2500.00m,
            "LKR", "test-secret");

        var second = PayHereSignature.CreateCheckoutHash(
            "123456", "ORDER-001", 2500.00m,
            "LKR", "test-secret");

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void Invalid_payment_signature_is_rejected()
    {
        var valid = PayHereSignature.VerifyNotification(
            "123456", "ORDER-001", "2500.00",
            "LKR", "2", "INVALID",
            "test-secret");

        Assert.False(valid);
    }

    [Fact]
    public void Invalid_amount_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PayHereSignature.CreateCheckoutHash(
                "123456", "ORDER-001", -50m,
                "LKR", "test-secret"));
    }
}
