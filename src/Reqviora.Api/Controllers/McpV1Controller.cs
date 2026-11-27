using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.Mcp;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

// V1 is authenticated, read-only, and permanently bound to the approved revision in the grant.
[ApiController, Authorize, Route("api/mcp/v1")]
public sealed class McpV1Controller(
    ReqvioraDbContext db,
    IConfiguration configuration,
    IMcpGrantService grants) : ControllerBase
{
    private static readonly object[] Tools =
    [
        new { name = "reqviora.project.overview", description = "Read metadata for the grant-bound approved project revision." },
        new { name = "reqviora.blueprint.current", description = "Read the immutable Blueprint snapshot bound to the grant." },
        new { name = "reqviora.validation.latest", description = "Read validation metadata for the grant-bound approved revision." }
    ];

    private static readonly object[] Resources =
    [
        new { uriTemplate = "reqviora://projects/{projectId}/overview", mimeType = "application/json" },
        new { uriTemplate = "reqviora://projects/{projectId}/blueprint/current", mimeType = "application/json" }
    ];

    private static readonly object[] Prompts =
    [
        new { name = "requirements-review", description = "Review suggested requirements without auto-approving them." },
        new { name = "blueprint-validation", description = "Explain current Blueprint validation findings." }
    ];

    public sealed record ToolCallRequest(string Name, Guid ProjectId);

    [HttpGet("catalog")]
    public IActionResult Catalog() => Enabled()
        ? Ok(new { protocolVersion = "v1", readOnly = true, tools = Tools, resources = Resources, prompts = Prompts })
        : NotFound();

    [HttpPost("tools/call")]
    [ServiceFilter(typeof(Reqviora.Api.Features.Billing.ProjectPaymentGateFilter))]
    public async Task<IActionResult> Call(
        ToolCallRequest request,
        [FromHeader(Name = "X-MCP-Grant")] string? credential,
        CancellationToken cancellationToken)
    {
        if (!Enabled()) return NotFound();
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, request.ProjectId, userId, cancellationToken);
        var grant = await grants.ResolveActiveAsync(credential ?? string.Empty, request.ProjectId, userId, cancellationToken);
        if (grant is null)
            return Problem(title: "A valid active MCP project grant is required.", statusCode: 403);
        var revision = await db.ApprovedRevisions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == grant.ApprovedRevisionId && item.ProjectId == request.ProjectId, cancellationToken);
        if (revision is null)
            return Problem(title: "The grant-bound approved revision is unavailable.", statusCode: 403);

        return request.Name switch
        {
            "reqviora.project.overview" => Ok(new
            {
                projectId = request.ProjectId,
                approvedRevisionId = revision.Id,
                revision.ApprovedAt
            }),
            "reqviora.blueprint.current" => string.IsNullOrWhiteSpace(revision.SnapshotJson)
                ? NotFound()
                : Content(revision.SnapshotJson, "application/json"),
            "reqviora.validation.latest" => Ok(await db.ValidationRuns.AsNoTracking()
                .Where(run => run.Id == revision.ValidationRunId)
                .OrderByDescending(run => run.ValidatedAt)
                .Select(run => new { run.Id, run.DraftVersionId, run.Passed, run.ValidatedAt })
                .FirstOrDefaultAsync(cancellationToken)),
            _ => BadRequest(new { error = "MCP_TOOL_NOT_ALLOWED" })
        };
    }

    [HttpGet("prompts/{name}")]
    public IActionResult Prompt(string name)
    {
        if (!Enabled()) return NotFound();
        return name switch
        {
            "requirements-review" => Ok(new { name, template = "Review each candidate against its cited evidence. Never auto-approve; return suggested edits for human confirmation." }),
            "blueprint-validation" => Ok(new { name, template = "Explain each validation finding, its path, and a safe correction. Do not modify an approved revision." }),
            _ => NotFound()
        };
    }

    private bool Enabled() => configuration.GetValue("Mcp:Enabled", false);
}
