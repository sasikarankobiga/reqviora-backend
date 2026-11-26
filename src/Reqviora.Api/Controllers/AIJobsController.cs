using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/ai/jobs")]
public sealed class AIJobsController(IAIJobQueue jobs, IOptions<AiOptions> options) : ControllerBase
{
    public sealed record CreateAiJob(
        Guid ProjectId,
        [Required, StringLength(100_000, MinimumLength = 1)] string Prompt);

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateAiJob request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        if (!UserId(out var ownerId)) return Unauthorized();
        if (request.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [request.ProjectId == Guid.Empty ? "projectId" : "Idempotency-Key"] =
                    [request.ProjectId == Guid.Empty
                        ? "A project is required."
                        : "The Idempotency-Key header is required."]
            }));
        var result = await jobs.EnqueueAsync(
            ownerId, request.ProjectId, request.Prompt, idempotencyKey, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id = result.Job.Id }, new
        {
            id = result.Job.Id,
            state = result.Job.State,
            created = result.Created
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        if (!UserId(out var ownerId)) return Unauthorized();
        return await jobs.GetAsync(id, ownerId, cancellationToken) is { } job ? Ok(job) : NotFound();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        if (!UserId(out var ownerId)) return Unauthorized();
        return await jobs.CancelAsync(id, ownerId, cancellationToken) is { } job
            ? AcceptedAtAction(nameof(Get), new { id }, job)
            : NotFound();
    }

    private bool UserId(out Guid id) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out id);
}
