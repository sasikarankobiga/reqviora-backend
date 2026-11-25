using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.GuidedBuilder;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/builder")]
public sealed class GuidedBuilderController(ProjectDraftService drafts) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32
    };

    [HttpGet]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GuidedBuilderResponse>> Get(Guid projectId, CancellationToken ct)
    {
        var draft = await drafts.ResumeAsync(projectId, ProjectApiAccess.RequireUserId(User), ct);
        return draft is null ? NotFound() : Ok(ToResponse(draft, ReadBuilder(draft.DraftJson)));
    }

    [HttpPost("validate")]
    [ProducesResponseType<GuidedBuilderValidationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GuidedBuilderValidationResponse>> Validate(Guid projectId, CancellationToken ct)
    {
        var draft = await drafts.ResumeAsync(projectId, ProjectApiAccess.RequireUserId(User), ct);
        if (draft is null) return NotFound();
        var findings = GuidedBuilderValidator.Validate(ReadBuilder(draft.DraftJson), requireComplete: true);
        return Ok(new GuidedBuilderValidationResponse(findings.Count == 0, findings));
    }

    [HttpPut("steps/identity-audience")]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GuidedBuilderResponse>> SaveIdentity(Guid projectId,
        BuilderStepSaveRequest<IdentityAudienceStep> request, CancellationToken ct) =>
        SaveAsync(projectId, request.Data, request.ExpectedConcurrencyToken,
            (builder, value) => builder with { IdentityAudience = value }, ct);

    [HttpPut("steps/features-screens")]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GuidedBuilderResponse>> SaveFeatures(Guid projectId,
        BuilderStepSaveRequest<FeaturesScreensStep> request, CancellationToken ct) =>
        SaveAsync(projectId, request.Data, request.ExpectedConcurrencyToken,
            (builder, value) => builder with { FeaturesScreens = value }, ct);

    [HttpPut("steps/navigation")]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GuidedBuilderResponse>> SaveNavigation(Guid projectId,
        BuilderStepSaveRequest<NavigationStep> request, CancellationToken ct) =>
        SaveAsync(projectId, request.Data, request.ExpectedConcurrencyToken,
            (builder, value) => builder with { Navigation = value }, ct);

    [HttpPut("steps/theme-brand")]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GuidedBuilderResponse>> SaveTheme(Guid projectId,
        BuilderStepSaveRequest<ThemeBrandStep> request, CancellationToken ct) =>
        SaveAsync(projectId, request.Data, request.ExpectedConcurrencyToken,
            (builder, value) => builder with { ThemeBrand = value }, ct);

    [HttpPut("steps/components-layout")]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GuidedBuilderResponse>> SaveComponents(Guid projectId,
        BuilderStepSaveRequest<ComponentsLayoutStep> request, CancellationToken ct) =>
        SaveAsync(projectId, request.Data, request.ExpectedConcurrencyToken,
            (builder, value) => builder with { ComponentsLayout = value }, ct);

    [HttpPut("steps/interactions-states")]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GuidedBuilderResponse>> SaveInteractions(Guid projectId,
        BuilderStepSaveRequest<InteractionsStatesStep> request, CancellationToken ct) =>
        SaveAsync(projectId, request.Data, request.ExpectedConcurrencyToken,
            (builder, value) => builder with { InteractionsStates = value }, ct);

    [HttpPut("steps/ai-design-workspace")]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GuidedBuilderResponse>> SaveWorkspace(Guid projectId,
        BuilderStepSaveRequest<AiDesignWorkspaceStep> request, CancellationToken ct) =>
        SaveAsync(projectId, request.Data, request.ExpectedConcurrencyToken,
            (builder, value) => builder with { AiDesignWorkspace = value }, ct);

    [HttpPut("steps/review-preview")]
    [ProducesResponseType<GuidedBuilderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<GuidedBuilderResponse>> SaveReview(Guid projectId,
        BuilderStepSaveRequest<ReviewPreviewStep> request, CancellationToken ct) =>
        SaveAsync(projectId, request.Data, request.ExpectedConcurrencyToken,
            (builder, value) => builder with { ReviewPreview = value }, ct);

    private async Task<ActionResult<GuidedBuilderResponse>> SaveAsync<T>(Guid projectId, T value,
        Guid? expectedToken, Func<GuidedBuilderDocument, T, GuidedBuilderDocument> update, CancellationToken ct)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var userId = ProjectApiAccess.RequireUserId(User);
        var current = await drafts.ResumeAsync(projectId, userId, ct);
        var root = current is null ? new JsonObject() : ReadRoot(current.DraftJson);
        var builder = current is null ? new GuidedBuilderDocument(GuidedBuilderValidator.CurrentVersion) : ReadBuilder(root);
        var updated = update(builder, value);
        GuidedBuilderValidator.RequireValid(updated);
        root["guidedBuilder"] = JsonSerializer.SerializeToNode(updated, Json);
        var saved = await drafts.SaveAsync(projectId, userId, root.ToJsonString(Json), expectedToken, ct);
        return Ok(ToResponse(saved, updated));
    }

    private static JsonObject ReadRoot(string draftJson) => JsonNode.Parse(draftJson,
        new JsonNodeOptions { PropertyNameCaseInsensitive = false },
        new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 32 }) as JsonObject
        ?? throw new InvalidDataException("Draft root must be an object.");

    private static GuidedBuilderDocument ReadBuilder(string draftJson) => ReadBuilder(ReadRoot(draftJson));

    private static GuidedBuilderDocument ReadBuilder(JsonObject root) =>
        root["guidedBuilder"]?.Deserialize<GuidedBuilderDocument>(Json)
        ?? new GuidedBuilderDocument(GuidedBuilderValidator.CurrentVersion);

    private static GuidedBuilderResponse ToResponse(ProjectDraft draft, GuidedBuilderDocument builder) =>
        new(draft.Id, draft.ProjectId, draft.Version, draft.ConcurrencyToken, draft.UpdatedAtUtc, builder);
}
