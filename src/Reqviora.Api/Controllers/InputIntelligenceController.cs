using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Reqviora.Application.Features.RequirementReview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.VoiceProcessing;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Reqviora.Domain.Entities;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/input-intelligence")]
public sealed class InputIntelligenceController(
    ReqvioraDbContext db,
    IOptions<AiOptions> options,
    ILogger<InputIntelligenceController> logger) : ControllerBase
{
    public sealed record ExtractRequirementsRequest(
        string LanguageCode,
        string InputText,
        IReadOnlyList<SourceEvidenceRef> Evidence,
        bool ConsentGranted);

    public sealed class TranscribeRequest
    {
        [Required]
        public IFormFile Audio { get; init; } = null!;

        [Required, StringLength(10)]
        public string LanguageCode { get; init; } = string.Empty;

        [Range(1, 1800)]
        public int DurationSeconds { get; init; }

        public bool ConsentGranted { get; init; }
    }

    public sealed record EditTranscriptBody(int ExpectedRevision, string EditedText);

    public sealed record ReviewTranscriptBody(int ExpectedRevision, bool AcknowledgedByUser);

    public sealed record VoiceTranscriptResource(
        Guid TranscriptId,
        Guid AudioFileId,
        string LanguageCode,
        string OriginalText,
        string EditableText,
        int Revision,
        IReadOnlyList<TranscriptSegment> Segments,
        string Status,
        DateTimeOffset? ReviewedAt);

    [HttpPost("requirements/extract")]
    public async Task<IActionResult> ExtractRequirements(
        Guid projectId,
        ExtractRequirementsRequest body,
        [FromServices] IStructuredAiProvider provider,
        [FromServices] IRequirementCandidateStore candidates,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        var request = new AiProviderRequest(
            projectId, Guid.NewGuid(), AiOperation.RequirementExtraction, body.LanguageCode, body.InputText);
        AiContractValidator.Validate(request);
        if (!body.ConsentGranted)
            throw new ArgumentException("AI text extraction consent is required.", nameof(body));
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        if (body.Evidence is null || body.Evidence.Count is < 1 or > 1000 ||
            body.Evidence.Any(item => item is null || string.IsNullOrWhiteSpace(item.SourceId) ||
                string.IsNullOrWhiteSpace(item.Locator) || string.IsNullOrWhiteSpace(item.Quote) ||
                !body.InputText.Contains(item.Quote, StringComparison.Ordinal)))
            throw new ArgumentException("Evidence quotes must occur in the submitted text.");
        // Supply the evidence references the provider is required to cite.
        request = request with { InputText = JsonSerializer.Serialize(new
        {
            text = body.InputText,
            evidence = body.Evidence
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        AiContractValidator.Validate(request);
        var response = await provider.ExecuteAsync(request, cancellationToken);
        if (response.Status != AiRunStatus.Completed)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { response.ErrorCode });
        RequirementExtractionPayload payload;
        try
        {
            payload = RequirementCandidateRegistration.AssignIds(
                RequirementExtractionValidator.Parse(request, response, body.Evidence));
        }
        catch (ArgumentException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid AI extraction output.");
        }
        var suggested = payload.Candidates.Select(RequirementReviewService.CreateCandidate).ToArray();
        await candidates.InsertAsync(projectId, suggested, cancellationToken);
        return Ok(payload);
    }

    [HttpPost("voice/transcribe")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(26_214_400)]
    public async Task<IActionResult> Transcribe(
        Guid projectId,
        [FromForm] TranscribeRequest body,
        [FromServices] IVoiceTranscriber transcriber,
        [FromServices] IFileStorageService storage,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        var request = new VoiceInputRequest(
            projectId,
            Guid.NewGuid(),
            body.LanguageCode,
            body.Audio.ContentType,
            body.Audio.Length,
            body.DurationSeconds,
            body.ConsentGranted);
        InputStudioContractValidator.Validate(request);
        if (!options.Value.Enabled)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AI provider approval is pending.");
        string storagePath;
        try
        {
            await using var upload = body.Audio.OpenReadStream();
            storagePath = await storage.SaveAsync(upload, AudioFileName(body.Audio.ContentType), cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The audio upload was rejected.");
        }
        catch (IOException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Private audio storage is unavailable.");
        }

        try
        {
            await using var stream = await storage.OpenReadAsync(storagePath, cancellationToken);
            var draft = await transcriber.TranscribeAsync(request, stream, cancellationToken);
            VoiceTranscriptionContractValidator.Validate(draft);
            if (draft.AudioFileId != request.AudioFileId ||
                !string.Equals(draft.LanguageCode, request.LanguageCode, StringComparison.Ordinal))
                throw new InvalidDataException("Transcription provider returned mismatched source metadata.");
            var entity = new VoiceTranscript(draft.TranscriptId)
            {
                ProjectId = projectId,
                AudioFileId = draft.AudioFileId,
                AudioStoragePath = storagePath,
                MediaType = body.Audio.ContentType,
                SizeBytes = body.Audio.Length,
                DurationSeconds = body.DurationSeconds,
                LanguageCode = draft.LanguageCode,
                OriginalText = draft.OriginalText,
                EditableText = draft.EditableText,
                Revision = draft.Revision,
                CreatedBy = userId.ToString()
            };
            db.VoiceTranscripts.Add(entity);
            db.VoiceTranscriptSegments.AddRange(draft.Segments.Select((segment, sequence) =>
                new VoiceTranscriptSegment
                {
                    VoiceTranscriptId = entity.Id,
                    Sequence = sequence,
                    StartMs = segment.StartMs,
                    EndMs = segment.EndMs,
                    Text = segment.Text
                }));
            await db.SaveChangesAsync(cancellationToken);
            return Ok(draft);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or IOException or ArgumentException)
        {
            await TryDeleteAsync(storage, storagePath);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Voice transcription is unavailable.");
        }
        catch
        {
            await TryDeleteAsync(storage, storagePath);
            throw;
        }
    }

    [HttpGet("voice/transcripts/{transcriptId:guid}")]
    public async Task<IActionResult> GetTranscript(
        Guid projectId,
        Guid transcriptId,
        CancellationToken cancellationToken)
    {
        await ProjectApiAccess.RequireReadAsync(
            db, projectId, ProjectApiAccess.RequireUserId(User), cancellationToken);
        var transcript = await db.VoiceTranscripts.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.Id == transcriptId, cancellationToken);
        return transcript is null
            ? NotFound()
            : Ok(await ToResourceAsync(transcript, cancellationToken));
    }

    [HttpPut("voice/transcripts/{transcriptId:guid}")]
    public async Task<IActionResult> EditTranscript(
        Guid projectId,
        Guid transcriptId,
        EditTranscriptBody body,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        VoiceTranscriptionContractValidator.Validate(
            new EditTranscriptRequest(transcriptId, body.ExpectedRevision, body.EditedText));
        var now = DateTimeOffset.UtcNow;
        var changed = await db.VoiceTranscripts
            .Where(item => item.ProjectId == projectId && item.Id == transcriptId &&
                item.Status == "draft" && item.Revision == body.ExpectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.EditableText, body.EditedText.Trim())
                .SetProperty(item => item.Revision, item => item.Revision + 1)
                .SetProperty(item => item.LastModifiedAt, now)
                .SetProperty(item => item.LastModifiedBy, userId.ToString())
                .SetProperty(item => item.ConcurrencyToken, Guid.NewGuid()), cancellationToken);
        if (changed != 1)
            return await TranscriptConflictOrNotFoundAsync(projectId, transcriptId, cancellationToken);
        var transcript = await db.VoiceTranscripts.AsNoTracking().SingleAsync(
            item => item.ProjectId == projectId && item.Id == transcriptId, cancellationToken);
        return Ok(await ToResourceAsync(transcript, cancellationToken));
    }

    [HttpPost("voice/transcripts/{transcriptId:guid}/review")]
    public async Task<IActionResult> ReviewTranscript(
        Guid projectId,
        Guid transcriptId,
        ReviewTranscriptBody body,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        await ProjectApiAccess.RequireWriteAsync(db, projectId, userId, cancellationToken);
        VoiceTranscriptionContractValidator.Validate(
            new ReviewTranscriptRequest(transcriptId, body.ExpectedRevision, body.AcknowledgedByUser));
        var now = DateTimeOffset.UtcNow;
        var changed = await db.VoiceTranscripts
            .Where(item => item.ProjectId == projectId && item.Id == transcriptId &&
                item.Status == "draft" && item.Revision == body.ExpectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "reviewed")
                .SetProperty(item => item.Revision, item => item.Revision + 1)
                .SetProperty(item => item.ReviewedAt, now)
                .SetProperty(item => item.ReviewedBy, userId)
                .SetProperty(item => item.LastModifiedAt, now)
                .SetProperty(item => item.LastModifiedBy, userId.ToString())
                .SetProperty(item => item.ConcurrencyToken, Guid.NewGuid()), cancellationToken);
        if (changed != 1)
            return await TranscriptConflictOrNotFoundAsync(projectId, transcriptId, cancellationToken);
        var transcript = await db.VoiceTranscripts.AsNoTracking().SingleAsync(
            item => item.ProjectId == projectId && item.Id == transcriptId, cancellationToken);
        return Ok(await ToResourceAsync(transcript, cancellationToken));
    }

    private async Task<VoiceTranscriptResource> ToResourceAsync(
        VoiceTranscript transcript,
        CancellationToken cancellationToken)
    {
        var segments = await db.VoiceTranscriptSegments.AsNoTracking()
            .Where(segment => segment.VoiceTranscriptId == transcript.Id)
            .OrderBy(segment => segment.Sequence)
            .Select(segment => new TranscriptSegment(segment.StartMs, segment.EndMs, segment.Text))
            .ToListAsync(cancellationToken);
        return new(transcript.Id, transcript.AudioFileId, transcript.LanguageCode,
            transcript.OriginalText, transcript.EditableText, transcript.Revision,
            segments, transcript.Status, transcript.ReviewedAt);
    }

    private async Task<IActionResult> TranscriptConflictOrNotFoundAsync(
        Guid projectId,
        Guid transcriptId,
        CancellationToken cancellationToken) =>
        await db.VoiceTranscripts.AsNoTracking().AnyAsync(
            item => item.ProjectId == projectId && item.Id == transcriptId, cancellationToken)
            ? Conflict()
            : NotFound();

    private async Task TryDeleteAsync(IFileStorageService storage, string storagePath)
    {
        try
        {
            await storage.DeleteAsync(storagePath, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to remove uncommitted voice audio from private storage.");
        }
    }

    private static string AudioFileName(string mediaType) => mediaType switch
    {
        "audio/wav" => "voice.wav",
        "audio/mpeg" => "voice.mp3",
        "audio/mp4" => "voice.m4a",
        "audio/webm" => "voice.webm",
        _ => "voice.bin"
    };
}
