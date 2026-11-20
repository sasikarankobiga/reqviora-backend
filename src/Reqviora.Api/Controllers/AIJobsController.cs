using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/ai/jobs")]
public sealed class AIJobsController(IAIJobQueue jobs) : ControllerBase
{
    public sealed record CreateAiJob(string Prompt);

    [HttpPost]
    public async Task<IActionResult> Create(CreateAiJob request, CancellationToken cancellationToken)
    {
        if (!UserId(out var ownerId)) return Unauthorized();
        var id = await jobs.EnqueueAsync(ownerId, request.Prompt, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id }, new { id, state = "queued" });
    }

    [HttpGet("{id:guid}")]
    public IActionResult Get(Guid id) =>
        UserId(out var ownerId) && jobs.Get(id, ownerId) is { } job ? Ok(job) : NotFound();

    private bool UserId(out Guid id) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out id);
}
