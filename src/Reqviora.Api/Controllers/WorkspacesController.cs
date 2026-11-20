using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize, Route("api/workspaces")]
public sealed class WorkspacesController(ReqvioraDbContext db) : ControllerBase
{
    public sealed record CreateWorkspace(string Name);
    public sealed record AddMember(Guid UserId, string Role = "Member");

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!UserId(out var userId)) return Unauthorized();
        var items = await db.Workspaces.AsNoTracking()
            .Where(x => x.OwnerUserId == userId || db.WorkspaceMembers.Any(m => m.WorkspaceId == x.Id && m.UserId == userId))
            .OrderBy(x => x.Name).ToArrayAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateWorkspace request, CancellationToken ct)
    {
        if (!UserId(out var userId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 150) return ValidationProblem();
        var workspace = new Workspace { Name = request.Name.Trim(), OwnerUserId = userId, CreatedBy = userId.ToString() };
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = userId, Role = "Owner" });
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = workspace.Id }, workspace);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!UserId(out var userId)) return Unauthorized();
        var workspace = await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            (x.OwnerUserId == userId || db.WorkspaceMembers.Any(m => m.WorkspaceId == id && m.UserId == userId)), ct);
        if (workspace is null) return NotFound();
        var members = await db.WorkspaceMembers.AsNoTracking().Where(x => x.WorkspaceId == id).ToArrayAsync(ct);
        var projects = await db.Projects.AsNoTracking().Where(x => x.WorkspaceId == id).ToArrayAsync(ct);
        return Ok(new { workspace, members, projects });
    }

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> Add(Guid id, AddMember request, CancellationToken ct)
    {
        if (!UserId(out var userId)) return Unauthorized();
        if (!await db.Workspaces.AnyAsync(x => x.Id == id && x.OwnerUserId == userId, ct)) return NotFound();
        if (!await db.Users.AnyAsync(x => x.Id == request.UserId, ct)) return ValidationProblem();
        if (await db.WorkspaceMembers.AnyAsync(x => x.WorkspaceId == id && x.UserId == request.UserId, ct)) return Conflict();
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = id, UserId = request.UserId, Role = request.Role.Trim() });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private bool UserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out id);
}
