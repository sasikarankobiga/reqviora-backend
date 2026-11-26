using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Reqviora.Domain.Entities;

namespace Reqviora.Api.Features.Billing;

public sealed record CheckoutCustomer(
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName,
    [Required, Phone, StringLength(30)] string Phone,
    [Required, StringLength(250)] string Address,
    [Required, StringLength(100)] string City,
    [Required, StringLength(100)] string Country);

public sealed record PayHereCheckoutResponse(
    string OrderReference,
    string CheckoutUrl,
    IReadOnlyDictionary<string, string> Fields);

public sealed record PayHereCheckoutOptions(
    string MerchantId,
    string MerchantSecret,
    string ReturnUrl,
    string CancelUrl,
    string NotifyUrl,
    decimal Amount,
    string Currency)
{
    public static PayHereCheckoutOptions? Read(
        IConfiguration configuration)
    {
        if (!configuration.GetValue(
            "Billing:CheckoutEnabled", false))
        {
            return null;
        }

        var section =
            configuration.GetSection("Billing:PayHere");

        var merchantId = section["MerchantId"];
        var secret = section["MerchantSecret"];
        var returnUrl = section["ReturnUrl"];
        var cancelUrl = section["CancelUrl"];
        var notifyUrl = section["NotifyUrl"];

        var amount = configuration.GetValue(
            "Billing:ProjectPriceUsd", 9.99m);

        if (string.IsNullOrWhiteSpace(merchantId) ||
            string.IsNullOrWhiteSpace(secret) ||
            !IsHttps(returnUrl) ||
            !IsHttps(cancelUrl) ||
            !IsHttps(notifyUrl) ||
            amount <= 0 ||
            amount > 999999999.99m ||
            decimal.Round(amount, 2) != amount)
        {
            return null;
        }

        var notify = new Uri(notifyUrl!);

        if (notify.IsLoopback ||
            notify.AbsolutePath != "/api/billing/payhere/notify" ||
            notify.Query.Length != 0 ||
            notify.Fragment.Length != 0)
        {
            return null;
        }

        return new(
            merchantId,
            secret,
            returnUrl!,
            cancelUrl!,
            notifyUrl!,
            amount,
            "USD");
    }

    private static bool IsHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo);

    public PayHereCheckoutResponse Build(
        ProjectPaymentOrder order,
        CheckoutCustomer customer,
        string verifiedEmail)
    {
        if (order.Status != ProjectPaymentStatus.Pending ||
            order.Amount != Amount ||
            order.Currency != Currency)
        {
            throw new InvalidOperationException(
                "Invalid checkout order.");
        }

        var fields = new Dictionary<string, string>
        {
            ["merchant_id"] = MerchantId,
            ["return_url"] = ReturnUrl,
            ["cancel_url"] = CancelUrl,
            ["notify_url"] = NotifyUrl,
            ["order_id"] = order.OrderReference,
            ["items"] = "REQVIORA Project Unlock",
            ["currency"] = order.Currency,
            ["amount"] = order.Amount.ToString(
                "0.00", CultureInfo.InvariantCulture),
            ["first_name"] = customer.FirstName.Trim(),
            ["last_name"] = customer.LastName.Trim(),
            ["email"] = verifiedEmail,
            ["phone"] = customer.Phone.Trim(),
            ["address"] = customer.Address.Trim(),
            ["city"] = customer.City.Trim(),
            ["country"] = customer.Country.Trim(),
            ["hash"] = PayHereSignature.CreateCheckoutHash(
                MerchantId,
                order.OrderReference,
                order.Amount,
                order.Currency,
                MerchantSecret)
        };

        return new(
            order.OrderReference,
            "https://sandbox.payhere.lk/pay/checkout",
            fields);
    }
}
