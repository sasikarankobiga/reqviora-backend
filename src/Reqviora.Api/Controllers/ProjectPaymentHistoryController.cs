using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Billing;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/billing/payments")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class ProjectPaymentHistoryController(
    IProjectPaymentHistory history,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        Guid projectId,
        [FromQuery, Range(1, 1000000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
            return StatusCode(503);

        if (!TryUserId(out var actorId))
            return Unauthorized();

        var result = await history.ListAsync(
            projectId,
            actorId,
            page,
            pageSize,
            cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{paymentId:guid}")]
    public async Task<IActionResult> Details(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return StatusCode(503);

        if (!TryUserId(out var actorId))
            return Unauthorized();

        var result = await history.FindAsync(
            projectId,
            actorId,
            paymentId,
            cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{paymentId:guid}/receipt")]
    public async Task<IActionResult> Receipt(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return StatusCode(503);

        if (!TryUserId(out var actorId))
            return Unauthorized();

        var result = await history.FindAsync(
            projectId,
            actorId,
            paymentId,
            cancellationToken);

        if (result is null)
            return NotFound();

        if (!result.ReceiptAvailable)
        {
            return Conflict(new
            {
                code = "payment_receipt_unavailable"
            });
        }

        return Ok(new
        {
            documentType = "sandbox-payment-confirmation",
            sandbox = true,
            officialTaxInvoice = false,
            receiptReference = result.OrderReference,
            result.PaymentId,
            result.ProjectId,
            result.Amount,
            result.Currency,
            result.Status,
            result.CreatedAt,
            result.UpdatedAt,
            provider = "PayHere"
        });
    }

    private bool TryUserId(out Guid actorId)
    {
        return Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub"),
            out actorId);
    }
}
