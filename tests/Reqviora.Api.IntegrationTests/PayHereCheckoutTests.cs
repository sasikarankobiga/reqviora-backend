using Microsoft.Extensions.Configuration;
using Reqviora.Api.Features.Billing;
using Reqviora.Domain.Entities;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class PayHereCheckoutTests
{
    [Fact]
    public void Checkout_is_disabled_by_default()
    {
        var configuration =
            new ConfigurationBuilder().Build();

        Assert.Null(
            PayHereCheckoutOptions.Read(configuration));
    }

    [Fact]
    public void Checkout_requires_valid_gateway_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Billing:CheckoutEnabled"] = "true"
                })
            .Build();

        Assert.Null(
            PayHereCheckoutOptions.Read(configuration));
    }

    [Fact]
    public void Checkout_contains_server_price_and_no_secret()
    {
        var secret = Guid.NewGuid().ToString("N");

        var options = new PayHereCheckoutOptions(
            "sandbox-merchant",
            secret,
            "https://example.invalid/return",
            "https://example.invalid/cancel",
            "https://example.invalid/api/billing/payhere/notify",
            9.99m,
            "USD");

        var order = ProjectPaymentOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            9.99m,
            "USD",
            DateTimeOffset.UtcNow);

        var customer = new CheckoutCustomer(
            "Test",
            "Customer",
            "0700000000",
            "Test address",
            "Jaffna",
            "Sri Lanka");

        var checkout = options.Build(
            order,
            customer,
            "owner@example.invalid");

        Assert.Equal(
            "https://sandbox.payhere.lk/pay/checkout",
            checkout.CheckoutUrl);

        Assert.Equal(
            "9.99", checkout.Fields["amount"]);

        Assert.Equal(
            "USD", checkout.Fields["currency"]);

        Assert.Equal(
            order.OrderReference,
            checkout.Fields["order_id"]);

        Assert.Equal(
            32, checkout.Fields["hash"].Length);

        Assert.DoesNotContain(
            secret, checkout.Fields.Values);

        Assert.False(
            checkout.Fields.ContainsKey("merchant_secret"));

        Assert.Equal(
            ProjectPaymentStatus.Pending, order.Status);
    }

    [Fact]
    public void Checkout_rejects_mismatched_price()
    {
        var options = new PayHereCheckoutOptions(
            "sandbox-merchant",
            Guid.NewGuid().ToString("N"),
            "https://example.invalid/return",
            "https://example.invalid/cancel",
            "https://example.invalid/api/billing/payhere/notify",
            9.99m,
            "USD");

        var order = ProjectPaymentOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0.01m,
            "USD",
            DateTimeOffset.UtcNow);

        var customer = new CheckoutCustomer(
            "Test", "Customer", "0700000000",
            "Test address", "Jaffna", "Sri Lanka");

        Assert.Throws<InvalidOperationException>(() =>
            options.Build(
                order,
                customer,
                "owner@example.invalid"));
    }
}
