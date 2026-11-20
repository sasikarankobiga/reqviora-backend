using System.Security.Claims;
using Reqviora.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces")]
public sealed class WorkspacesController(ReqvioraDbContext db) : ControllerBase
{
    public sealed record CreateWorkspace(string Name);
    public sealed record AddMember(Guid UserId, string Role = "Member");

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkspaceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        var workspaces = await db.Workspaces.AsNoTracking()
            .Where(workspace => workspace.OwnerUserId == userId || db.WorkspaceMembers.Any(member => member.WorkspaceId == workspace.Id && member.UserId == userId))
            .OrderBy(workspace => workspace.Name)
            .Select(workspace => new WorkspaceResponse(
                workspace.Id,
                workspace.Name,
                workspace.OwnerUserId,
                db.Projects.Count(project => project.WorkspaceId == workspace.Id)))
            .ToArrayAsync(cancellationToken);
        return Ok(workspaces);
    }

    [HttpGet("{workspaceId:guid}")]
    [ProducesResponseType<WorkspaceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkspaceResponse>> GetById(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        var workspace = await db.Workspaces.AsNoTracking()
            .Where(item => item.Id == workspaceId && (item.OwnerUserId == userId || db.WorkspaceMembers.Any(member => member.WorkspaceId == workspaceId && member.UserId == userId)))
            .Select(item => new WorkspaceResponse(
                item.Id,
                item.Name,
                item.OwnerUserId,
                db.Projects.Count(project => project.WorkspaceId == item.Id)))
            .SingleOrDefaultAsync(cancellationToken);
        return workspace is null ? NotFound() : Ok(workspace);
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
        return CreatedAtAction(nameof(GetById), new { workspaceId = workspace.Id }, workspace);
    }

    [HttpGet("{id:guid}/details")]
    public async Task<IActionResult> GetDetails(Guid id, CancellationToken ct)
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
