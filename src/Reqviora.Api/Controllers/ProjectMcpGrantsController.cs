using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Mcp;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/mcp/grants")]
public sealed class ProjectMcpGrantsController(IMcpGrantService grants, IConfiguration configuration) : ControllerBase
{
    public sealed record CreateGrantRequest(
        Guid ApprovedRevisionId,
        [Range(5, 43_200)] int LifetimeMinutes);

    public sealed record ProjectRevokeGrantRequest([Required, StringLength(500, MinimumLength = 3)] string Reason);

    [HttpPost]
    [ServiceFilter(typeof(Reqviora.Api.Features.Billing.ProjectPaymentGateFilter))]
    [ProducesResponseType(typeof(McpGrantCreated), StatusCodes.Status201Created)]
    public async Task<ActionResult<McpGrantCreated>> Create(
        Guid projectId,
        CreateGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enabled()) return NotFound();
        var created = await grants.CreateAsync(projectId, RequireUserId(), request.ApprovedRevisionId,
            TimeSpan.FromMinutes(request.LifetimeMinutes), cancellationToken);
        return CreatedAtAction(nameof(List), new { projectId }, created);
    }

    [HttpGet]
    public async Task<ActionResult<McpGrantPage>> List(
        Guid projectId,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled()) return NotFound();
        return Ok(await grants.ListOwnerAsync(projectId, RequireUserId(), page, pageSize, cancellationToken));
    }

    [HttpPost("{grantId:guid}/revoke")]
    public async Task<ActionResult<McpGrantMetadata>> Revoke(
        Guid projectId,
        Guid grantId,
        ProjectRevokeGrantRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enabled()) return NotFound();
        var actorId = RequireUserId();
        var grant = await grants.RevokeAsync(grantId, actorId, request.Reason, false, projectId, cancellationToken);
        return grant is null ? NotFound() : Ok(grant);
    }

    private bool Enabled() => configuration.GetValue("Mcp:Enabled", false);

    private Guid RequireUserId() => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id
        : throw new UnauthorizedAccessException("Authenticated user identity is required.");
}
