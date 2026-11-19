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
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkspaceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var userId = ProjectApiAccess.RequireUserId(User);
        var workspaces = await db.Workspaces.AsNoTracking()
            .Where(workspace => workspace.OwnerUserId == userId)
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
            .Where(item => item.Id == workspaceId && item.OwnerUserId == userId)
            .Select(item => new WorkspaceResponse(
                item.Id,
                item.Name,
                item.OwnerUserId,
                db.Projects.Count(project => project.WorkspaceId == item.Id)))
            .SingleOrDefaultAsync(cancellationToken);
        return workspace is null ? NotFound() : Ok(workspace);
    }
}
