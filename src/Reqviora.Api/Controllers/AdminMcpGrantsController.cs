using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Mcp;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/admin/mcp/grants")]
public sealed class AdminMcpGrantsController(IMcpGrantService grants) : ControllerBase
{
    public sealed record AdminEmergencyRevokeGrantRequest(
        [Required, StringLength(500, MinimumLength = 3)] string Reason);

    [HttpGet]
    public async Task<ActionResult<McpGrantPage>> List(
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50,
        [FromQuery, StringLength(100)] string? search = null,
        [FromQuery, RegularExpression("^(all|active|expired|revoked)$")] string state = "all",
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default) =>
        Ok(await grants.ListAdminAsync(page, pageSize, search, state, projectId, cancellationToken));

    [HttpGet("{grantId:guid}")]
    public async Task<ActionResult<McpGrantMetadata>> Get(Guid grantId, CancellationToken cancellationToken)
    {
        var grant = await grants.FindAdminAsync(grantId, cancellationToken);
        return grant is null ? NotFound() : Ok(grant);
    }

    [HttpPost("{grantId:guid}/revoke")]
    public async Task<ActionResult<McpGrantMetadata>> Revoke(
        Guid grantId,
        AdminEmergencyRevokeGrantRequest request,
        CancellationToken cancellationToken)
    {
        var grant = await grants.RevokeAsync(grantId, RequireUserId(), request.Reason, true,
            cancellationToken: cancellationToken);
        return grant is null ? NotFound() : Ok(grant);
    }

    private Guid RequireUserId() => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id
        : throw new UnauthorizedAccessException("Authenticated administrator identity is required.");
}
