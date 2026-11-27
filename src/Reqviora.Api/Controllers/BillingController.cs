using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Configuration;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.Billing;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api")]
public sealed class BillingController(IBillingService billing) : ControllerBase
{
    public sealed record CheckoutRequest([Required, StringLength(100)] string FirstName,
        [Required, StringLength(100)] string LastName, [Required, EmailAddress, StringLength(320)] string Email,
        [Required, StringLength(40)] string Phone, [Required, StringLength(300)] string Address,
        [Required, StringLength(100)] string City, [Required, StringLength(100)] string Country);

    [HttpGet("billing/pricing")]
    public Task<BillingPricingResponse> Pricing([FromQuery] Guid? projectId, CancellationToken ct) =>
        billing.GetPricingAsync(projectId, UserId(), ct);

    [HttpPost("projects/{projectId:guid}/billing/checkout")]
    public Task<BillingCheckoutResponse> Checkout(Guid projectId, CheckoutRequest request, CancellationToken ct) =>
        billing.CreateCheckoutAsync(projectId, UserId(), new(request.FirstName, request.LastName, request.Email,
            request.Phone, request.Address, request.City, request.Country), ct);

    [HttpGet("projects/{projectId:guid}/billing/status")]
    public Task<PaymentDetails> Status(Guid projectId, CancellationToken ct) =>
        billing.GetProjectStatusAsync(projectId, UserId(), ct);

    [HttpGet("projects/{projectId:guid}/billing/history")]
    public Task<PaymentHistoryPage> History(Guid projectId, [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50, CancellationToken ct = default) =>
        billing.GetProjectHistoryAsync(projectId, UserId(), page, pageSize, ct);

    [HttpGet("billing/payments/{paymentId:guid}")]
    public Task<PaymentDetails> Payment(Guid paymentId, CancellationToken ct) =>
        billing.GetPaymentAsync(paymentId, UserId(), User.IsInRole("Admin") || User.IsInRole("Administrator"), ct);

    private Guid UserId() => ProjectApiAccess.RequireUserId(User);
}
