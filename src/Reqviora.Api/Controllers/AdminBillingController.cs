using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Configuration;
using Reqviora.Api.Features.Administration;
using Reqviora.Application.Features.Billing;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Security;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
[EnableRateLimiting("authentication")]
[Route("api/admin/billing")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AdminBillingController(
    IAdminBillingService billing,
    ReqvioraDbContext db,
    LoginSecurityService security,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    public sealed record MfaRequest(
        [Required, RegularExpression("^[0-9]{6}$")]
        string Code);

    public sealed record PaymentListRequest(
        [Required, RegularExpression("^[0-9]{6}$")]
        string Code,
        [Range(1, 1000000)] int Page = 1,
        [Range(1, 100)] int PageSize = 20,
        Guid? ProjectId = null,
        string? Status = null,
        string? Currency = null);

    [HttpPost("summary")]
    public async Task<IActionResult> Summary(
        MfaRequest request,
        CancellationToken cancellationToken)
    {
        var denied = await VerifyAccessAsync(
            request.Code, cancellationToken);

        if (denied is not null)
            return denied;

        var rows = await billing.SummaryAsync(
            cancellationToken);

        return Ok(new
        {
            sandbox = true,
            accountingReport = false,
            currencies = rows
        });
    }

    [HttpPost("transactions")]
    public async Task<IActionResult> List(
        PaymentListRequest request,
        CancellationToken cancellationToken)
    {
        var denied = await VerifyAccessAsync(
            request.Code, cancellationToken);

        if (denied is not null)
            return denied;

        try
        {
            return Ok(await billing.ListAsync(
                request.Page,
                request.PageSize,
                request.ProjectId,
                request.Status,
                request.Currency,
                cancellationToken));
        }
        catch (ArgumentException)
        {
            return BadRequest(new
            {
                code = "invalid_billing_filter"
            });
        }
    }

    [HttpPost("transactions/{paymentId:guid}")]
    public async Task<IActionResult> Details(
        Guid paymentId,
        MfaRequest request,
        CancellationToken cancellationToken)
    {
        var denied = await VerifyAccessAsync(
            request.Code, cancellationToken);

        if (denied is not null)
            return denied;

        var row = await billing.FindAsync(
            paymentId, cancellationToken);

        return row is null ? NotFound() : Ok(row);
    }

    public sealed record RefundListRequest(
        [Required, RegularExpression("^[0-9]{6}$")] string Code,
        [Range(1, 1000000)] int Page = 1,
        [Range(1, 100)] int PageSize = 20);

    public sealed record RefundReviewRequest(
        [Required, RegularExpression("^[0-9]{6}$")] string Code,
        [Required, RegularExpression("^(reviewed|rejected)$")]
        string Decision);

    [HttpPost("refunds")]
    public async Task<IActionResult> Refunds(
        RefundListRequest request,
        [FromServices] IPaymentRefundService refunds,
        CancellationToken ct)
    {
        var denied = await VerifyAccessAsync(request.Code, ct);
        if (denied is not null) return denied;

        return Ok(await refunds.ListAdminAsync(
            request.Page, request.PageSize, ct));
    }

    [HttpPost("refunds/{paymentId:guid}/review")]
    public async Task<IActionResult> ReviewRefund(
        Guid paymentId,
        RefundReviewRequest request,
        [FromServices] IPaymentRefundService refunds,
        CancellationToken ct)
    {
        var denied = await VerifyAccessAsync(request.Code, ct);
        if (denied is not null) return denied;

        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out var actorId))
            return Unauthorized();

        var result = await refunds.ReviewAsync(
            paymentId, actorId, request.Decision, ct);

        return result.Code switch
        {
            "review_completed" => Ok(result.Item),
            "not_found" => NotFound(),
            "already_reviewed" => Conflict(result),
            "not_eligible" => Conflict(result),
            "retry_required" => StatusCode(503),
            _ => BadRequest()
        };
    }

    private async Task<IActionResult?> VerifyAccessAsync(
        string code,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return StatusCode(503);

        var subject = User.FindFirstValue(
            ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(subject, out var actorId))
            return Unauthorized();

        var admin = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == actorId,
                cancellationToken);

        var allowedEmails = configuration
            .GetSection("Security:AdminEmails")
            .Get<string[]>() ?? [];

        if (admin is null ||
            admin.IsDisabled ||
            !AdminOverviewAccess.IsEligible(
                admin, allowedEmails))
        {
            return Forbid();
        }

        if (!security.ValidateTotp(
            admin, code ?? string.Empty))
        {
            return Unauthorized(new
            {
                code = "admin_mfa_required"
            });
        }

        return null;
    }
}
