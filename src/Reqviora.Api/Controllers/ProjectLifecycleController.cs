using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}")]
public sealed class ProjectLifecycleController(ProjectDraftService drafts, ProjectExportService exports) : ControllerBase
{
    public sealed record SaveDraftRequest(JsonElement Draft, BlueprintDocument? Blueprint, Guid? ExpectedToken);
    public sealed record TokenRequest(Guid ExpectedToken);
    public sealed record ApprovalRequest(Guid ExpectedToken, Guid ValidationRunId);

    [HttpGet("draft")]
    public async Task<IActionResult> Resume(Guid projectId, CancellationToken ct) =>
        UserId(out var actor) ? Ok(await drafts.ResumeAsync(projectId, actor, ct)) : Unauthorized();

    [HttpPut("draft")]
    public async Task<IActionResult> Save(Guid projectId, SaveDraftRequest request, CancellationToken ct)
    {
        if (!UserId(out var actor)) return Unauthorized();
        var json = request.Draft.GetRawText();
        var result = request.Blueprint is null
            ? await drafts.SaveAsync(projectId, actor, json, request.ExpectedToken, ct)
            : await drafts.SaveDesignAsync(projectId, actor, json, request.Blueprint, request.ExpectedToken, ct);
        return Ok(result);
    }

    [HttpGet("draft/history")]
    public async Task<IActionResult> DraftHistory(Guid projectId, CancellationToken ct) =>
        UserId(out var actor) ? Ok(await drafts.GetDraftHistoryAsync(projectId, actor, ct)) : Unauthorized();

    [HttpPost("draft/validate")]
    public async Task<IActionResult> Validate(Guid projectId, TokenRequest request, CancellationToken ct) =>
        UserId(out var actor) ? Ok(await drafts.ValidateAsync(projectId, actor, request.ExpectedToken, ct)) : Unauthorized();

    [HttpPost("approvals")]
    public async Task<IActionResult> Approve(Guid projectId, ApprovalRequest request, CancellationToken ct) =>
        UserId(out var actor)
            ? Ok(await drafts.ApproveAsync(projectId, actor, request.ExpectedToken, request.ValidationRunId, ct))
            : Unauthorized();

    [HttpPost("draft/new-after-approval")]
    public async Task<IActionResult> Start(Guid projectId, TokenRequest request, CancellationToken ct) =>
        UserId(out var actor) ? Ok(await drafts.StartAfterApprovalAsync(projectId, actor, request.ExpectedToken, ct)) : Unauthorized();

    [HttpGet("revisions")]
    public async Task<IActionResult> Revisions(Guid projectId, CancellationToken ct) =>
        UserId(out var actor) ? Ok(await drafts.GetRevisionHistoryAsync(projectId, actor, ct)) : Unauthorized();

    [HttpPost("revisions/{revisionId:guid}/exports")]
    public async Task<IActionResult> Export(Guid projectId, Guid revisionId, CancellationToken ct)
    {
        if (!UserId(out var actor)) return Unauthorized();
        var record = await exports.GetOrCreateAsync(projectId, revisionId, actor, ct);
        return File(record.Archive, "application/zip", $"reqviora-{projectId}-{revisionId}.zip");
    }

    private bool UserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out id);
}
