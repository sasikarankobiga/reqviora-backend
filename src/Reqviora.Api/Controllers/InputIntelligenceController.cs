using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.VoiceProcessing;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/input-intelligence")]
public sealed class InputIntelligenceController(ReqvioraDbContext db) : ControllerBase
{
    public sealed record ExtractRequirementsRequest(
        string LanguageCode,
        string InputText,
        IReadOnlyList<SourceEvidenceRef> Evidence);

    [HttpPost("requirements/extract")]
    public async Task<IActionResult> ExtractRequirements(
        Guid projectId,
        ExtractRequirementsRequest body,
        [FromServices] IStructuredAiProvider provider,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireOwnerAsync(db, projectId, userId, cancellationToken);
        var request = new AiProviderRequest(
            projectId, Guid.NewGuid(), AiOperation.RequirementExtraction, body.LanguageCode, body.InputText);
        AiContractValidator.Validate(request);
        var response = await provider.ExecuteAsync(request, cancellationToken);
        if (response.Status != AiRunStatus.Completed)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { response.ErrorCode });
        return Ok(RequirementExtractionValidator.Parse(request, response, body.Evidence));
    }

    [HttpPost("voice/transcribe")]
    [RequestSizeLimit(26_214_400)]
    public async Task<IActionResult> Transcribe(
        Guid projectId,
        [FromForm] IFormFile audio,
        [FromForm] string languageCode,
        [FromForm] int durationSeconds,
        [FromForm] bool consentGranted,
        [FromServices] IVoiceTranscriber transcriber,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireOwnerAsync(db, projectId, userId, cancellationToken);
        var request = new VoiceInputRequest(
            projectId, Guid.NewGuid(), languageCode, audio.ContentType, audio.Length, durationSeconds, consentGranted);
        InputStudioContractValidator.Validate(request);
        await using var stream = audio.OpenReadStream();
        return Ok(await transcriber.TranscribeAsync(request, stream, cancellationToken));
    }
}
