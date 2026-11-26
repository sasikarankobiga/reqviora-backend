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

[ApiController, AllowAnonymous, Route("api/billing/payhere")]
public sealed class PayHereNotificationsController(IBillingService billing) : ControllerBase
{
    public sealed class NotificationForm
    {
        [FromForm(Name = "merchant_id"), Required] public string MerchantId { get; init; } = string.Empty;
        [FromForm(Name = "order_id"), Required] public string OrderId { get; init; } = string.Empty;
        [FromForm(Name = "payment_id"), Required] public string PaymentId { get; init; } = string.Empty;
        [FromForm(Name = "payhere_amount"), Required] public string Amount { get; init; } = string.Empty;
        [FromForm(Name = "payhere_currency"), Required] public string Currency { get; init; } = string.Empty;
        [FromForm(Name = "status_code"), Required] public string StatusCode { get; init; } = string.Empty;
        [FromForm(Name = "md5sig"), Required] public string Signature { get; init; } = string.Empty;
        [FromForm(Name = "method")] public string? Method { get; init; }
        [FromForm(Name = "status_message")] public string? StatusMessage { get; init; }
    }

    [HttpPost("notify")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Notify([FromForm] NotificationForm form, CancellationToken ct)
    {
        await billing.ProcessNotificationAsync(new(form.MerchantId, form.OrderId, form.PaymentId, form.Amount,
            form.Currency, form.StatusCode, form.Signature, form.Method, form.StatusMessage), ct);
        return Ok();
    }
}

[ApiController, Authorize(Policy = AuthorizationPolicies.Administrator), Route("api/admin/billing")]
public sealed class AdminBillingController(IBillingService billing) : ControllerBase
{
    public sealed record RefundRequest([Required, StringLength(500, MinimumLength = 3)] string Reason);
    public sealed record ProjectPriceRequest([Range(1, 100_000_000)] long AmountMinor);

    [HttpGet("payments")]
    public Task<PaymentHistoryPage> Search([FromQuery] string? status, [FromQuery] Guid? projectId,
        [FromQuery, Range(1, 1_000_000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50,
        CancellationToken ct = default) => billing.SearchAdminAsync(status, projectId, page, pageSize, ct);

    [HttpGet("payments/{paymentId:guid}")]
    public Task<PaymentDetails> Payment(Guid paymentId, CancellationToken ct) =>
        billing.GetPaymentAsync(paymentId, UserId(), true, ct);

    [HttpGet("summary")]
    public Task<PaymentSummary> Summary(CancellationToken ct) => billing.GetSummaryAsync(ct);

    [HttpPut("projects/{projectId:guid}/pricing")]
    public Task<BillingPricingResponse> SetProjectPrice(Guid projectId, ProjectPriceRequest request,
        CancellationToken ct) => billing.SetProjectPriceAsync(projectId, request.AmountMinor, UserId(), ct);

    [HttpPost("payments/{paymentId:guid}/refund")]
    public Task<PaymentDetails> Refund(Guid paymentId, RefundRequest request, CancellationToken ct) =>
        billing.RequestRefundAsync(paymentId, request.Reason, UserId(), ct);

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
        out var id) ? id : throw new UnauthorizedAccessException("A verified administrator identity is required.");
}
