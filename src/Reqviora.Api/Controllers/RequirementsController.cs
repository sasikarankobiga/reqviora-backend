using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/requirements")]
public sealed class RequirementsController(ReqvioraDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RequirementResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RequirementResponse>>> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var blueprint = await GetCurrentBlueprintAsync(projectId, cancellationToken);
        return Ok(blueprint?.Requirements.Select(ToResponse).ToArray() ?? []);
    }

    [HttpGet("{requirementId}")]
    [ProducesResponseType<RequirementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequirementResponse>> GetById(
        Guid projectId,
        string requirementId,
        CancellationToken cancellationToken)
    {
        var blueprint = await GetCurrentBlueprintAsync(projectId, cancellationToken);
        var requirement = blueprint?.Requirements.SingleOrDefault(item => item.Id == requirementId);
        return requirement is null ? NotFound() : Ok(ToResponse(requirement));
    }

    // The existing /requirements endpoint represents the current Blueprint, not the review queue.
    [HttpGet("candidates")]
    public async Task<ActionResult<IReadOnlyList<ReviewedRequirement>>> GetCandidates(
        Guid projectId, [FromServices] IRequirementCandidateStore candidates,
        CancellationToken cancellationToken, [FromQuery] int offset = 0, [FromQuery] int limit = 50)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, projectId, userId, cancellationToken);
        if (offset < 0 || limit is < 1 or > 100) return BadRequest();
        return Ok(await candidates.ListAsync(projectId, offset, limit, cancellationToken));
    }

    public sealed record ManualRequirementRequest(string Title, string Description);

    [HttpPost("candidates/manual")]
    public async Task<ActionResult<ReviewedRequirement>> CreateManual(Guid projectId, ManualRequirementRequest body,
        [FromServices] IRequirementCandidateStore candidates, CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        if (string.IsNullOrWhiteSpace(body.Title) || body.Title.Length > 200 ||
            body.Description is null || body.Description.Length > 4000) return BadRequest();
        var id = $"REQ-{Guid.NewGuid():N}".ToUpperInvariant();
        // Manual evidence is the submitted text, not a fabricated uploaded document reference.
        var quote = string.IsNullOrWhiteSpace(body.Description) ? body.Title.Trim() : body.Description.Trim();
        var evidence = new Application.Features.AIOrchestration.CandidateEvidence(
            $"SRC-{id[4..]}", "Manual entry", quote);
        var requirement = new ReviewedRequirement(id, body.Title.Trim(), body.Description.Trim(),
            RequirementReviewStatus.Suggested, 1, new[] { evidence }, null, null);
        await candidates.InsertAsync(projectId, new[] { requirement }, cancellationToken);
        return CreatedAtAction(nameof(GetCandidate), new { projectId, requirementId = id }, requirement);
    }

    [HttpGet("candidates/{requirementId}")]
    public async Task<ActionResult<ReviewedRequirement>> GetCandidate(Guid projectId, string requirementId,
        [FromServices] IRequirementReviewStore store, CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, projectId, userId, cancellationToken);
        var snapshot = await store.FindAsync(projectId, requirementId, cancellationToken);
        return snapshot is null ? NotFound() : Ok(snapshot.Requirement);
    }

    private async Task<Application.Features.UIGeneration.Blueprint.Models.BlueprintDocument?> GetCurrentBlueprintAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireReadAsync(db, projectId, userId, cancellationToken);
        var version = await ProjectApiAccess.FindCurrentVersionAsync(db, projectId, cancellationToken);
        return ProjectApiAccess.ReadBlueprint(version?.BlueprintJson);
    }

    private static RequirementResponse ToResponse(
        Application.Features.UIGeneration.Blueprint.Models.BlueprintRequirement requirement) => new(
        requirement.Id,
        requirement.Text,
        requirement.Origin,
        requirement.Status,
        requirement.ConfirmedBy,
        requirement.ConfirmedAt,
        requirement.Evidence.Select(evidence => new RequirementEvidenceResponse(
            evidence.SourceId, evidence.Locator, evidence.Quote)).ToArray());
}
