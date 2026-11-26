using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.Billing;
using Reqviora.Application.Features.Billing;

namespace Reqviora.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/billing/payhere")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class PayHereNotificationController(
    IPaymentNotificationService payments,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("notify")]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(16384)]
    [RequestFormLimits(
        ValueCountLimit = 40,
        ValueLengthLimit = 2048)]
    public async Task<IActionResult> Notify(
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() ||
            !configuration.GetValue(
                "Billing:PayHere:NotificationsEnabled", false))
        {
            return StatusCode(503);
        }

        var merchantId =
            configuration["Billing:PayHere:MerchantId"];

        var merchantSecret =
            configuration["Billing:PayHere:MerchantSecret"];

        if (string.IsNullOrWhiteSpace(merchantId) ||
            string.IsNullOrWhiteSpace(merchantSecret))
        {
            return StatusCode(503);
        }

        if (!Request.HasFormContentType)
            return StatusCode(415);

        var form = await Request.ReadFormAsync(cancellationToken);

        string[] required =
        [
            "merchant_id",
            "order_id",
            "payment_id",
            "payhere_amount",
            "payhere_currency",
            "status_code",
            "md5sig"
        ];

        foreach (var key in required)
        {
            if (!form.TryGetValue(key, out var values) ||
                values.Count != 1 ||
                string.IsNullOrWhiteSpace(values[0]))
            {
                return BadRequest();
            }
        }

        var notification = PayHereNotificationValidator.Validate(
            merchantId,
            merchantSecret,
            form["merchant_id"].ToString(),
            form["order_id"].ToString(),
            form["payment_id"].ToString(),
            form["payhere_amount"].ToString(),
            form["payhere_currency"].ToString(),
            form["status_code"].ToString(),
            form["md5sig"].ToString());

        if (notification is null)
            return BadRequest();

        var result = await payments.ApplyAsync(
            notification, cancellationToken);

        return result switch
        {
            PaymentNotificationResult.Applied =>
                Ok(),

            PaymentNotificationResult.DuplicateOrStale =>
                Ok(),

            PaymentNotificationResult.OrderNotFound =>
                NotFound(),

            PaymentNotificationResult.PaymentMismatch =>
                Conflict(),

            PaymentNotificationResult.RetryRequired =>
                StatusCode(503),

            _ => StatusCode(500)
        };
    }
}
