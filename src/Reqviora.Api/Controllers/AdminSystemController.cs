
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reqviora.Api.Features.Administration;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Security;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("authentication")]
[Route("api/admin/system")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSystemController : ControllerBase
{
    private readonly ReqvioraDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly LoginSecurityService _security;
    private readonly HealthCheckService _health;

    public AdminSystemController(
        ReqvioraDbContext db,
        IConfiguration configuration,
        LoginSecurityService security,
        HealthCheckService health)
    {
        _db = db;
        _configuration = configuration;
        _security = security;
        _health = health;
    }

    [HttpPost("report")]
    public async Task<IActionResult> Report(
        [FromBody] AdminMfaRequest request,
        CancellationToken ct)
    {
        var denied = await CheckAccess(request, ct);
        if (denied is not null) return denied;

        var users = await _db.Users.CountAsync(ct);
        var projects = await _db.Projects.CountAsync(ct);
        var drafts = await _db.ProjectDrafts.CountAsync(ct);
        var roles = await _db.Roles.CountAsync(ct);
        var audits = await _db.AuditEvents.CountAsync(ct);

        var failedLogins = await _db.LoginAudits
            .CountAsync(x => !x.Succeeded, ct);

        return Ok(new
        {
            totalUsers = users,
            totalProjects = projects,
            totalDrafts = drafts,
            totalRoles = roles,
            totalAuditEvents = audits,
            totalFailedLogins = failedLogins,
            generatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    [HttpPost("health")]
    public async Task<IActionResult> Health(
        [FromBody] AdminMfaRequest request,
        CancellationToken ct)
    {
        var denied = await CheckAccess(request, ct);
        if (denied is not null) return denied;

        var report = await _health.CheckHealthAsync(
            cancellationToken: ct);

        var checks = report.Entries
            .OrderBy(entry => entry.Key)
            .Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString()
            })
            .ToArray();

        // Do not expose database connection strings,
        // exception details or internal secret values.
        return Ok(new
        {
            status = report.Status.ToString(),
            totalDurationMs =
                report.TotalDuration.TotalMilliseconds,
            checks
        });
    }

    private async Task<IActionResult?> CheckAccess(
        AdminMfaRequest request,
        CancellationToken ct)
    {
        var access = await AdminRequestGuard.CheckAsync(
            User, _db, _configuration, _security,
            request.Code, ct);

        return access switch
        {
            AdminAccessStatus.Unauthorized => Unauthorized(),
            AdminAccessStatus.Forbidden => Forbid(),
            _ => null
        };
    }
}
