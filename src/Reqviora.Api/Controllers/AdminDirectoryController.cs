
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
[Route("api/admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDirectoryController : ControllerBase
{
    private readonly ReqvioraDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly LoginSecurityService _security;

    public AdminDirectoryController(
        ReqvioraDbContext db,
        IConfiguration configuration,
        LoginSecurityService security)
    {
        _db = db;
        _configuration = configuration;
        _security = security;
    }

    [HttpPost("users/search")]
    public async Task<IActionResult> SearchUsers(
        [FromBody] AdminMfaRequest request,
        CancellationToken ct,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var denied = await CheckAccess(request, ct);
        if (denied is not null) return denied;

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

        var query = _db.Users.AsNoTracking();

        var term = search?.Trim().ToUpperInvariant();

        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(user =>
                user.NormalizedEmail.Contains(term) ||
                user.DisplayName.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderBy(user => user.Email)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.Email,
                user.EmailVerified,
                user.MfaEnabled,
                user.RoleId
            })
            .ToListAsync(ct);

        // Never return password hashes, MFA secrets,
        // recovery codes or authentication tokens.
        return Ok(new
        {
            page,
            pageSize,
            total,
            items = users
        });
    }

    [HttpPost("roles/list")]
    public async Task<IActionResult> ListRoles(
        [FromBody] AdminMfaRequest request,
        CancellationToken ct)
    {
        var denied = await CheckAccess(request, ct);
        if (denied is not null) return denied;

        var roles = await _db.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new
            {
                role.Id,
                role.Name
            })
            .ToListAsync(ct);

        return Ok(roles);
    }

    private async Task<IActionResult?> CheckAccess(
        AdminMfaRequest request,
        CancellationToken ct)
    {
        var result = await AdminRequestGuard.CheckAsync(
            User, _db, _configuration, _security,
            request.Code, ct);

        return result switch
        {
            AdminAccessStatus.Unauthorized => Unauthorized(),
            AdminAccessStatus.Forbidden => Forbid(),
            _ => null
        };
    }
}
