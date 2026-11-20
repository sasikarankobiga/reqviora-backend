using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/documents")]
public sealed class ProjectDocumentsController(ReqvioraDbContext db, IFileStorageService storage) : ControllerBase
{
    [HttpPost, RequestSizeLimit(26_214_400)]
    public async Task<IActionResult> Upload(Guid projectId, IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor)) return Unauthorized();
        if (!await db.Projects.AnyAsync(x => x.Id == projectId && x.OwnerId == actor, ct)) return NotFound();
        await using var stream = file.OpenReadStream();
        var path = await storage.SaveAsync(stream, file.FileName, ct);
        var document = new ProjectDocument { ProjectId = projectId, FileName = Path.GetFileName(file.FileName), StoragePath = path,
            CreatedBy = actor.ToString(), CreatedAt = DateTimeOffset.UtcNow };
        db.ProjectDocuments.Add(document); await db.SaveChangesAsync(ct);
        return Created($"/api/projects/{projectId}/documents/{document.Id}", document);
    }
}
