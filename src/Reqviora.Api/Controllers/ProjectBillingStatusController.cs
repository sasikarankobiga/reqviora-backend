using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.Billing;
using Reqviora.Application.Features.Billing;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/billing/status")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class ProjectBillingStatusController(
    IProjectBillingAccess billing,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var subject =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out var actorId))
            return Unauthorized();

        var snapshot = await billing.ReadAsync(
            projectId, actorId, cancellationToken);

        if (snapshot is null)
            return NotFound();

        var enabled = configuration.GetValue(
            "Billing:EnforcementEnabled", false);

        var decision = ProjectBillingPolicy.Evaluate(
            snapshot,
            enabled,
            environment.IsDevelopment(),
            configuration["Billing:RequiredFromUtc"]);

        return Ok(new
        {
            projectId,
            paymentStatus = snapshot.HasSuccessfulPayment
                ? "paid"
                : snapshot.HasPendingPayment
                    ? "pending"
                    : "unpaid",

            sandbox = true,

            enforcementEnabled = enabled,

            grandfathered =
                decision == BillingGateDecision.Grandfathered,

            paidOperationsAllowed =
                decision == BillingGateDecision.Allowed ||
                decision == BillingGateDecision.Grandfathered,

            paymentRequired =
                decision == BillingGateDecision.PaymentRequired,

            enforcementAvailable =
                decision != BillingGateDecision.Unavailable
        });
    }
}
