using System.Security.Claims;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Documents;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Api.Features.ProjectWorkflow;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/documents")]
public sealed class ProjectDocumentsController(ReqvioraDbContext db, IFileStorageService storage) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> AllowedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["text/plain"] = ".txt"
        };

    public sealed record DocumentUploadResponse(
        Guid Id,
        Guid ProjectId,
        string FileName,
        string MediaType,
        long SizeBytes,
        DateTimeOffset CreatedAt);

    public sealed record DocumentMetadata(
        Guid Id,
        Guid ProjectId,
        string FileName,
        string MediaType,
        long SizeBytes,
        string ParsingStatus,
        DateTimeOffset? ParsedAt,
        DateTimeOffset CreatedAt);

    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct)
    {
        await ProjectApiAccess.RequireReadAsync(db, projectId, ProjectApiAccess.RequireUserId(User), ct);
        return Ok(await db.ProjectDocuments.AsNoTracking().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new DocumentMetadata(
                item.Id, item.ProjectId, item.FileName, item.MediaType, item.SizeBytes,
                item.ParsingStatus, item.ParsedAt, item.CreatedAt))
            .ToListAsync(ct));
    }

    [HttpGet("{documentId:guid}")]
    public async Task<IActionResult> Get(Guid projectId, Guid documentId, CancellationToken ct)
    {
        await ProjectApiAccess.RequireReadAsync(db, projectId, ProjectApiAccess.RequireUserId(User), ct);
        var document = await db.ProjectDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Id == documentId, ct);
        return document is null ? NotFound() : Ok(Metadata(document));
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<IActionResult> Download(
        Guid projectId,
        Guid documentId,
        CancellationToken ct)
    {
        await ProjectApiAccess.RequireReadAsync(db, projectId, ProjectApiAccess.RequireUserId(User), ct);
        var document = await db.ProjectDocuments.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.Id == documentId, ct);
        if (document is null) return NotFound();
        var signed = await storage.CreateSignedReadUrlAsync(
            document.StoragePath, TimeSpan.FromMinutes(5), ct);
        if (signed is not null)
            return Ok(new { url = signed, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) });
        var stream = await storage.OpenReadAsync(document.StoragePath, ct);
        return File(stream, document.MediaType, document.FileName, enableRangeProcessing: true);
    }

    [HttpPost("{documentId:guid}/parse")]
    public async Task<IActionResult> Parse(Guid projectId, Guid documentId,
        [FromServices] IDocumentTextParser parser, CancellationToken ct)
    {
        await ProjectApiAccess.RequireWriteAsync(db, projectId, ProjectApiAccess.RequireUserId(User), ct);
        var document = await db.ProjectDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Id == documentId, ct);
        if (document is null) return NotFound();
        if (document.ParsingStatus is "completed" or "requires_ocr")
            return Ok(await PersistedResultAsync(document, ct));
        var now = DateTimeOffset.UtcNow;
        if (document.ParsingStatus == "parsing" &&
            document.ParseLeaseExpiresAtUnixTimeMilliseconds > now.ToUnixTimeMilliseconds())
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Document parsing is already in progress.");

        var claimToken = Guid.NewGuid();
        var claimed = await db.ProjectDocuments
            .Where(item => item.ProjectId == projectId && item.Id == documentId &&
                item.ConcurrencyToken == document.ConcurrencyToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ParsingStatus, "parsing")
                .SetProperty(item => item.ParseErrorCode, (string?)null)
                .SetProperty(item => item.ParseLeaseExpiresAtUnixTimeMilliseconds,
                    now.AddMinutes(5).ToUnixTimeMilliseconds())
                .SetProperty(item => item.LastModifiedAt, now)
                .SetProperty(item => item.ConcurrencyToken, claimToken), ct);
        if (claimed != 1)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Document parsing state changed.");

        var mediaType = string.IsNullOrWhiteSpace(document.MediaType)
            ? AllowedExtensions.FirstOrDefault(item =>
                item.Value.Equals(Path.GetExtension(document.FileName), StringComparison.OrdinalIgnoreCase)).Key
            : document.MediaType;
        if (mediaType is null)
        {
            await MarkFailedAsync(documentId, claimToken, "unsupported_media_type", ct);
            return BadRequest();
        }
        try
        {
            await using var stream = await storage.OpenReadAsync(document.StoragePath, ct);
            var input = new DocumentInputRequest(projectId, document.Id, document.FileName, mediaType, stream.Length);
            var result = await parser.ParseAsync(input, stream, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.ProjectDocumentSegments.Where(segment => segment.ProjectDocumentId == documentId)
                .ExecuteDeleteAsync(ct);
            db.ProjectDocumentSegments.AddRange(result.Segments.Select(segment => new ProjectDocumentSegment
            {
                ProjectDocumentId = documentId,
                Sequence = segment.Sequence,
                Text = segment.Text,
                SourceId = segment.Evidence.SourceId,
                Locator = segment.Evidence.Locator,
                PageNumber = segment.Evidence.PageNumber,
                StartOffset = segment.Evidence.StartOffset,
                EndOffset = segment.Evidence.EndOffset
            }));
            var status = result.Segments.Count == 0 ? "requires_ocr" : "completed";
            var updated = await db.ProjectDocuments
                .Where(item => item.Id == documentId && item.ConcurrencyToken == claimToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ParsingStatus, status)
                    .SetProperty(item => item.ParsingWarningsJson, JsonSerializer.Serialize(result.Warnings))
                    .SetProperty(item => item.ParsedAt, now)
                    .SetProperty(item => item.ParseLeaseExpiresAtUnixTimeMilliseconds, (long?)null)
                    .SetProperty(item => item.LastModifiedAt, now)
                    .SetProperty(item => item.ConcurrencyToken, Guid.NewGuid()), ct);
            if (updated != 1) throw new DbUpdateConcurrencyException("Document parsing state changed.");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(result);
        }
        catch (InvalidDataException)
        {
            await MarkFailedAsync(documentId, claimToken, "invalid_document", ct);
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The document cannot be parsed or exceeds parsing limits.");
        }
        catch (IOException)
        {
            await MarkFailedAsync(documentId, claimToken, "storage_unavailable", ct);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The stored document is unavailable.");
        }
    }

    [HttpPost, Consumes("multipart/form-data"), RequestSizeLimit(26_214_400)]
    [ProducesResponseType<DocumentUploadResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(Guid projectId, IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
                out var actor))
        {
            return Unauthorized();
        }

        await ProjectApiAccess.RequireWriteAsync(db, projectId, actor, ct);

        var safeName = Path.GetFileName(file.FileName);
        var document = new ProjectDocument
        {
            ProjectId = projectId,
            FileName = safeName,
            MediaType = file.ContentType,
            SizeBytes = file.Length,
            CreatedBy = actor.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        InputStudioContractValidator.Validate(new DocumentInputRequest(
            projectId,
            document.Id,
            file.FileName,
            file.ContentType,
            file.Length));
        if (!AllowedExtensions.TryGetValue(file.ContentType, out var requiredExtension) ||
            !Path.GetExtension(safeName).Equals(requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The document extension does not match its media type.");
        }

        await using var upload = file.OpenReadStream();
        await using var buffer = new MemoryStream((int)file.Length);
        await upload.CopyToAsync(buffer, ct);
        if (buffer.Length != file.Length)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "The uploaded document size changed.");
        document.Sha256 = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
        if (await db.ProjectDocuments.AsNoTracking().AnyAsync(
            item => item.ProjectId == projectId && item.Sha256 == document.Sha256, ct))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This document was already uploaded.");
        buffer.Position = 0;
        try
        {
            document.StoragePath = await storage.SaveAsync(buffer, safeName, ct);
        }
        catch (InvalidDataException)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The uploaded document was rejected.");
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Document security scanning is unavailable.");
        }

        db.ProjectDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        return Created(
            $"/api/projects/{projectId}/documents/{document.Id}",
            new DocumentUploadResponse(
                document.Id,
                document.ProjectId,
                document.FileName,
                file.ContentType,
                file.Length,
                document.CreatedAt));
    }

    private static DocumentMetadata Metadata(ProjectDocument document) => new(
        document.Id,
        document.ProjectId,
        document.FileName,
        document.MediaType,
        document.SizeBytes,
        document.ParsingStatus,
        document.ParsedAt,
        document.CreatedAt);

    private async Task<DocumentParseResult> PersistedResultAsync(
        ProjectDocument document,
        CancellationToken cancellationToken)
    {
        var segments = await db.ProjectDocumentSegments.AsNoTracking()
            .Where(segment => segment.ProjectDocumentId == document.Id)
            .OrderBy(segment => segment.Sequence)
            .Select(segment => new ParsedDocumentSegment(
                segment.Sequence,
                segment.Text,
                new SourceEvidenceRef(segment.SourceId, segment.Locator, segment.PageNumber,
                    segment.StartOffset, segment.EndOffset, segment.Text)))
            .ToListAsync(cancellationToken);
        IReadOnlyList<string> warnings;
        try
        {
            warnings = JsonSerializer.Deserialize<string[]>(document.ParsingWarningsJson) ?? [];
        }
        catch (JsonException)
        {
            warnings = ["Stored parsing warnings could not be restored."];
        }
        return new DocumentParseResult(document.Id, segments, warnings);
    }

    private Task<int> MarkFailedAsync(
        Guid documentId,
        Guid claimToken,
        string errorCode,
        CancellationToken cancellationToken) =>
        db.ProjectDocuments.Where(item => item.Id == documentId && item.ConcurrencyToken == claimToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ParsingStatus, "failed")
                .SetProperty(item => item.ParseErrorCode, errorCode)
                .SetProperty(item => item.ParseLeaseExpiresAtUnixTimeMilliseconds, (long?)null)
                .SetProperty(item => item.LastModifiedAt, DateTimeOffset.UtcNow)
                .SetProperty(item => item.ConcurrencyToken, Guid.NewGuid()), cancellationToken);
}
