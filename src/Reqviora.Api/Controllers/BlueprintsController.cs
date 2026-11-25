using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/blueprints")]
public sealed class BlueprintsController(ReqvioraDbContext db, ProjectDraftService drafts) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType<BlueprintVersionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BlueprintVersionResponse>> GetCurrent(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var version = await ProjectApiAccess.FindCurrentVersionAsync(db, projectId, cancellationToken);
        return version?.BlueprintJson is null ? NotFound() : Ok(ProjectApiAccess.ToResponse(version));
    }

    [HttpGet("versions")]
    [ProducesResponseType<IReadOnlyList<BlueprintVersionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BlueprintVersionResponse>>> GetVersions(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var versions = await drafts.GetDraftHistoryAsync(projectId, UserId, cancellationToken);
        return Ok(versions.Where(version => version.BlueprintJson is not null)
            .Select(ProjectApiAccess.ToResponse).ToArray());
    }

    [HttpPut("current")]
    [ProducesResponseType<ProjectDraftResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDraftResponse>> Save(
        Guid projectId,
        SaveBlueprintRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await drafts.SaveDesignAsync(
            projectId,
            UserId,
            request.Draft!.Value.GetRawText(),
            request.Blueprint,
            request.ExpectedConcurrencyToken,
            cancellationToken);
        return Ok(ProjectApiAccess.ToResponse(draft));
    }

    public sealed record GenerateBlueprintRequest(string[] RequirementIds, Guid? ExpectedConcurrencyToken);

    [HttpPost("generate")]
    [ProducesResponseType<ProjectDraftResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDraftResponse>> Generate(
        Guid projectId, GenerateBlueprintRequest request,
        [FromServices] IRequirementReviewStore reviews,
        [FromServices] IBlueprintDraftGenerator generator,
        CancellationToken cancellationToken)
    {
        var userId = UserId;
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        if (request.RequirementIds is null || request.RequirementIds.Length is < 1 or > 50 ||
            request.RequirementIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 100) ||
            request.RequirementIds.Distinct(StringComparer.Ordinal).Count() != request.RequirementIds.Length)
            return BadRequest("Select one to fifty distinct requirement IDs.");

        var draft = await drafts.ResumeAsync(projectId, userId, cancellationToken);
        if (draft?.ConcurrencyToken != request.ExpectedConcurrencyToken)
            return Conflict("The draft changed. Reload and retry.");
        if (draft?.ApprovedRevisionId is not null ||
            (await ProjectApiAccess.FindCurrentVersionAsync(db, projectId, cancellationToken))?.BlueprintJson is not null)
            return Conflict("A Blueprint already exists. Initial generation cannot overwrite it.");

        var confirmed = new List<ReviewedRequirement>();
        foreach (var id in request.RequirementIds)
        {
            var snapshot = await reviews.FindAsync(projectId, id, cancellationToken);
            if (snapshot is null || snapshot.ProjectId != projectId)
                return NotFound("A selected requirement does not exist in this project.");
            if (snapshot.Requirement.Status != RequirementReviewStatus.Confirmed)
                return Conflict("Every selected requirement must be human-confirmed before generation.");
            confirmed.Add(snapshot.Requirement);
        }

        var blueprint = InitialBlueprintPlanner.Generate(generator, projectId, confirmed);
        // Review storage is independent of the draft DB. Detect review changes observed
        // during planning; approval-time verification is still required separately.
        foreach (var requirement in confirmed)
        {
            var current = await reviews.FindAsync(projectId, requirement.Id, cancellationToken);
            if (current is null || current.ProjectId != projectId ||
                current.Requirement.Revision != requirement.Revision ||
                current.Requirement.Status != RequirementReviewStatus.Confirmed)
                return Conflict("A selected requirement changed. Reload and retry.");
        }

        var saved = await drafts.SaveDesignAsync(projectId, userId, draft?.DraftJson ?? "{}",
            blueprint, request.ExpectedConcurrencyToken, cancellationToken);
        return Ok(ProjectApiAccess.ToResponse(saved));
    }

    [HttpPost("validate")]
    [ProducesResponseType<ValidationRunResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ValidationRunResponse>> Validate(
        Guid projectId,
        DraftTokenRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await drafts.ValidateAsync(
            projectId,
            UserId,
            request.ExpectedConcurrencyToken,
            cancellationToken);
        return Ok(new ValidationRunResponse(
            validation.Id,
            validation.DraftVersionId,
            validation.Passed,
            validation.ValidatedAt,
            validation.Findings.Select(finding => new ValidationFindingResponse(
                finding.Ordinal, finding.Code, finding.Path, finding.Message)).ToArray()));
    }

    [HttpGet("validations/{validationId:guid}")]
    [ProducesResponseType<ValidationRunResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ValidationRunResponse>> GetValidation(
        Guid projectId,
        Guid validationId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var validation = await db.ValidationRuns.AsNoTracking()
            .Include(run => run.Findings)
            .Join(
                db.DesignDraftVersions,
                run => run.DraftVersionId,
                version => version.Id,
                (run, version) => new { Run = run, version.ProjectId })
            .SingleOrDefaultAsync(item => item.Run.Id == validationId && item.ProjectId == projectId, cancellationToken);
        if (validation is null)
            return NotFound();

        return Ok(new ValidationRunResponse(
            validation.Run.Id,
            validation.Run.DraftVersionId,
            validation.Run.Passed,
            validation.Run.ValidatedAt,
            validation.Run.Findings.OrderBy(finding => finding.Ordinal)
                .Select(finding => new ValidationFindingResponse(
                    finding.Ordinal, finding.Code, finding.Path, finding.Message)).ToArray()));
    }

    [HttpGet("approved")]
    [ProducesResponseType<IReadOnlyList<ApprovedBlueprintResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApprovedBlueprintResponse>>> GetApproved(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var revisions = await db.ApprovedRevisions.AsNoTracking()
            .Where(revision => revision.ProjectId == projectId)
            .ToArrayAsync(cancellationToken);
        return Ok(revisions.OrderByDescending(revision => revision.ApprovedAt)
            .Select(ProjectApiAccess.ToResponse).ToArray());
    }

    [HttpGet("approved/{revisionId:guid}")]
    [ProducesResponseType<ApprovedBlueprintResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApprovedBlueprintResponse>> GetApproved(
        Guid projectId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        await RequireReadAsync(projectId, cancellationToken);
        var revision = await db.ApprovedRevisions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == revisionId && item.ProjectId == projectId,
            cancellationToken);
        return revision is null ? NotFound() : Ok(ProjectApiAccess.ToResponse(revision));
    }

    private Guid UserId => ProjectApiAccess.RequireUserId(User);

    private Task RequireReadAsync(Guid projectId, CancellationToken cancellationToken) =>
        ProjectApiAccess.RequireReadAsync(db, projectId, UserId, cancellationToken);
}
