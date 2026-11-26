using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Billing;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize]
[Route("api/projects/{projectId:guid}/billing/payments/{paymentId:guid}/refund-request")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProjectRefundController(
    IPaymentRefundService refunds,
    IWebHostEnvironment environment) : ControllerBase
{
    public sealed record RefundRequest(
        [Required, RegularExpression("^(duplicate_payment|service_issue|other)$")]
        string Reason);

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId, Guid paymentId,
        RefundRequest request, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
            return StatusCode(503);

        if (!TryUser(out var actorId))
            return Unauthorized();

        var result = await refunds.RequestAsync(
            projectId, paymentId, actorId, request.Reason, ct);

        return result.Code switch
        {
            "created" => StatusCode(201, result.Item),
            "already_requested" => Conflict(result),
            "not_found" => NotFound(),
            "not_eligible" => Conflict(new { code = result.Code }),
            "retry_required" => StatusCode(503),
            _ => BadRequest()
        };
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid projectId, Guid paymentId, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
            return StatusCode(503);

        if (!TryUser(out var actorId))
            return Unauthorized();

        var item = await refunds.FindAsync(
            projectId, paymentId, actorId, ct);

        return item is null ? NotFound() : Ok(item);
    }

    private bool TryUser(out Guid id) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub"), out id);
}
