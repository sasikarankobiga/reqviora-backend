using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;
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

    public sealed record RegenerateBlueprintRequest(
        Guid ExpectedConcurrencyToken, int ExpectedRevision,
        string[]? EditableFeatureIds, string[]? EditableScreenIds, string[]? EditableComponentIds,
        JsonElement? Proposal);

    [HttpPost("regenerate")]
    [ProducesResponseType<ProjectDraftResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectDraftResponse>> Regenerate(
        Guid projectId, RegenerateBlueprintRequest request,
        [FromServices] IRequirementReviewStore reviews,
        [FromServices] IBlueprintDraftGenerator generator,
        [FromServices] SafeRegenerationService regeneration,
        CancellationToken cancellationToken)
    {
        var userId = UserId;
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        var draft = await drafts.ResumeAsync(projectId, userId, cancellationToken);
        if (draft is null) return NotFound("No draft exists.");
        if (draft.ConcurrencyToken != request.ExpectedConcurrencyToken || draft.ApprovedRevisionId is not null)
            return Conflict("The draft changed or is approved. Reload or open a new draft.");
        var version = await ProjectApiAccess.FindCurrentVersionAsync(db, projectId, cancellationToken);
        if (version?.BlueprintJson is null) return NotFound("No Blueprint exists.");
        if (version.Version != draft.Version) return Conflict("The draft changed. Reload and retry.");
        BlueprintDocument current;
        try { current = BlueprintJsonContract.Parse(version.BlueprintJson); }
        catch (ArgumentException) { return Conflict("The stored Blueprint is invalid."); }
        if (current.ProjectId != projectId || current.Revision != request.ExpectedRevision ||
            current.Revision == int.MaxValue || !BlueprintContractValidator.Validate(current).IsValid)
            return Conflict("The Blueprint revision or graph is invalid or stale.");

        // Explicitly opt in to editing existing items. All others are protected.
        var editableFeatures = request.EditableFeatureIds ?? [];
        var editableScreens = request.EditableScreenIds ?? [];
        var editableComponents = request.EditableComponentIds ?? [];
        if (!ValidSelection(editableFeatures, current.Features.Select(x => x.Id)) ||
            !ValidSelection(editableScreens, current.Screens.Select(x => x.Id)) ||
            !ValidSelection(editableComponents, current.Components.Select(x => x.Id)))
            return BadRequest("Editable IDs must be distinct existing items.");
        var protectedComponents = current.Components.Where(x => !editableComponents.Contains(x.Id) ||
            Flag(x, "locked") || Flag(x, "userEdited")).Select(x => x.Id).ToArray();
        var lockedScreens = current.Components.Where(x => Flag(x, "locked") || Flag(x, "userEdited"))
            .Select(x => x.ScreenId).ToHashSet(StringComparer.Ordinal);
        var policy = new SafeRegenerationRequest(current.Revision,
            current.Features.Select(x => x.Id).Except(editableFeatures).ToArray(),
            current.Screens.Where(x => !editableScreens.Contains(x.Id) || lockedScreens.Contains(x.Id))
                .Select(x => x.Id).ToArray(), protectedComponents);

        // Do not rely on client confirmation metadata. This endpoint regenerates a design,
        // not the requirements themselves; changed reviews require a separate reconciliation.
        var confirmed = new List<ReviewedRequirement>();
        foreach (var requirement in current.Requirements.Where(x => x.Status == "confirmed"))
        {
            var record = await reviews.FindAsync(projectId, requirement.Id, cancellationToken);
            if (record is null || record.ProjectId != projectId || !MatchesReview(requirement, record.Requirement))
                return Conflict("A confirmed requirement no longer matches its review record.");
            confirmed.Add(record.Requirement);
        }
        if (confirmed.Count == 0) return Conflict("No confirmed requirements exist.");

        BlueprintDocument proposed;
        try
        {
            if (request.Proposal is { } proposal)
                proposed = BlueprintJsonContract.Parse(proposal.GetRawText());
            else
            {
                // Deterministic fallback, using the existing server-side planner.
                var generated = InitialBlueprintPlanner.Generate(generator, projectId, confirmed);
                proposed = new BlueprintDocument
                {
                    SchemaVersion = current.SchemaVersion, BlueprintId = current.BlueprintId,
                    ProjectId = projectId, Revision = current.Revision + 1, Lifecycle = "draft",
                    TargetPlatforms = current.TargetPlatforms, Requirements = current.Requirements,
                    Features = generated.Features, Screens = generated.Screens, Components = generated.Components,
                    Actions = generated.Actions, Navigation = generated.Navigation
                };
            }
            // Validate before persistence, including the storage ID length limit.
            var json = JsonSerializer.Serialize(proposed, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            proposed = BlueprintJsonContract.Parse(json);
        }
        catch (ArgumentException) { return BadRequest("Invalid regeneration proposal."); }

        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(current.Requirements),
            JsonSerializer.SerializeToElement(proposed.Requirements)))
            return Conflict("Regeneration cannot modify requirement records.");
        if (proposed.Requirements.Select(x => x.Id).Concat(proposed.Features.Select(x => x.Id))
            .Concat(proposed.Screens.Select(x => x.Id)).Concat(proposed.Components.Select(x => x.Id))
            .Concat(proposed.Actions.Select(x => x.Id)).Concat(proposed.Navigation.Select(x => x.Id))
            .Any(id => id.Length > 128)) return BadRequest("A Blueprint ID exceeds the storage limit.");
        var result = regeneration.TryApply(current, proposed, policy);
        if (!result.Applied) return Conflict(new { reason = result.Reason });
        // Protect incoming parent links too; otherwise an editable container could
        // silently reparent a protected component without changing its own JSON.
        foreach (var componentId in protectedComponents)
        {
            var previousParents = current.Components.Where(x => x.ChildComponentIds.Contains(componentId))
                .Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal);
            var proposedParents = proposed.Components.Where(x => x.ChildComponentIds.Contains(componentId))
                .Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal);
            if (!previousParents.SequenceEqual(proposedParents))
                return Conflict(new { reason = "PROTECTED_COMPONENT_PARENT_CHANGED" });
        }
        foreach (var requirement in confirmed)
        {
            var latest = await reviews.FindAsync(projectId, requirement.Id, cancellationToken);
            if (latest is null || latest.ProjectId != projectId || latest.Requirement.Revision != requirement.Revision ||
                !MatchesReview(current.Requirements.Single(x => x.Id == requirement.Id), latest.Requirement))
                return Conflict("A review changed during regeneration. Reload and retry.");
        }
        var saved = await drafts.SaveDesignAsync(projectId, userId, draft.DraftJson,
            result.Blueprint, request.ExpectedConcurrencyToken, cancellationToken);
        return Ok(ProjectApiAccess.ToResponse(saved));
    }

    private static bool ValidSelection(string[] selected, IEnumerable<string> available)
    {
        var ids = available.ToHashSet(StringComparer.Ordinal);
        return selected.Length <= ids.Count && selected.Distinct(StringComparer.Ordinal).Count() == selected.Length &&
            selected.All(id => id is not null && ids.Contains(id));
    }

    private static bool Flag(BlueprintComponent component, string name) =>
        component.Props.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool MatchesReview(BlueprintRequirement requirement, ReviewedRequirement review) =>
        review.Status == RequirementReviewStatus.Confirmed && review.Id == requirement.Id &&
        requirement.Text == review.Title + "\n" + review.Description &&
        review.ConfirmedBy is not null && Guid.TryParse(requirement.ConfirmedBy, out var actor) &&
        actor == review.ConfirmedBy && review.ConfirmedAt is not null && requirement.ConfirmedAt == review.ConfirmedAt &&
        review.Evidence.Count == requirement.Evidence.Count &&
        review.Evidence.Zip(requirement.Evidence).All(pair => pair.First.SourceId == pair.Second.SourceId &&
            pair.First.Locator == pair.Second.Locator && pair.First.Quote == pair.Second.Quote);

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
