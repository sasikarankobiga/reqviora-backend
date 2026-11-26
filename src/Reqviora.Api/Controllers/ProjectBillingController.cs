using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.Billing;
using Reqviora.Application.Features.Billing;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class ProjectBillingController(
    IProjectCheckoutOrders orders,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("api/billing/pricing")]
    public IActionResult Pricing()
    {
        var options = environment.IsDevelopment()
            ? PayHereCheckoutOptions.Read(configuration)
            : null;

        return Ok(new
        {
            amount = configuration.GetValue(
                "Billing:ProjectPriceUsd", 9.99m),
            currency = "USD",
            model = "pay-per-project",
            sandbox = true,
            checkoutAvailable = options is not null
        });
    }

    [HttpPost("api/projects/{projectId:guid}/billing/checkout")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Checkout(
        Guid projectId,
        CheckoutCustomer customer,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return StatusCode(503);

        var options =
            PayHereCheckoutOptions.Read(configuration);

        if (options is null)
            return StatusCode(503);

        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out var userId))
            return Unauthorized();

        var result = await orders.CreateAsync(
            projectId,
            userId,
            options.Amount,
            options.Currency,
            cancellationToken);

        return result.Code switch
        {
            CheckoutOrderResultCode.NotFound =>
                NotFound(),

            CheckoutOrderResultCode.NotEligible =>
                Forbid(),

            CheckoutOrderResultCode.AlreadyPaid =>
                Conflict(new { code = "project_already_paid" }),

            CheckoutOrderResultCode.PendingPayment =>
                Conflict(new { code = "payment_pending" }),

            CheckoutOrderResultCode.Created =>
                Ok(options.Build(
                    result.Order!,
                    customer,
                    result.CustomerEmail!)),

            _ => StatusCode(500)
        };
    }
}
