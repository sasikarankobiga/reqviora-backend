
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.Administration;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Security;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("authentication")]
[Route("api/admin/projects")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminProjectsController : ControllerBase
{
    private readonly ReqvioraDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly LoginSecurityService _security;

    public AdminProjectsController(
        ReqvioraDbContext db,
        IConfiguration configuration,
        LoginSecurityService security)
    {
        _db = db;
        _configuration = configuration;
        _security = security;
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(
        [FromBody] AdminMfaRequest request,
        CancellationToken ct,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var access = await AdminRequestGuard.CheckAsync(
            User, _db, _configuration, _security,
            request.Code, ct);

        if (access == AdminAccessStatus.Unauthorized)
            return Unauthorized();

        if (access == AdminAccessStatus.Forbidden)
            return Forbid();

        if (page < 1 || page > 100000 ||
            pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                message = "Invalid pagination."
            });
        }

        if ((search?.Length ?? 0) > 120)
        {
            return BadRequest(new
            {
                message = "Search is too long."
            });
        }

        var query = _db.Projects.AsNoTracking();

        var term = search?.Trim().ToUpperInvariant();

        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(project =>
                project.Name.ToUpper().Contains(term) ||
                project.Industry.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(ct);

        var projects = await query
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Industry,
                project.PrimaryPlatform,
                project.OwnerId,
                project.WorkspaceId,
                project.CreatedAt
            })
            .ToListAsync(ct);

        // Do not return documents, requirements,
        // drafts or private project descriptions.
        return Ok(new
        {
            page,
            pageSize,
            total,
            items = projects
        });
    }
}
