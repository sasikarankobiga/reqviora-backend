
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
public sealed class AdminAuditController : ControllerBase
{
    private readonly ReqvioraDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly LoginSecurityService _security;

    public AdminAuditController(
        ReqvioraDbContext db,
        IConfiguration configuration,
        LoginSecurityService security)
    {
        _db = db;
        _configuration = configuration;
        _security = security;
    }

    [HttpPost("audit/search")]
    public async Task<IActionResult> SearchAudit(
        [FromBody] AdminMfaRequest request,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? statusCode = null)
    {
        var denied = await CheckAccess(request, ct);
        if (denied is not null) return denied;

        if (page < 1 || page > 100000 ||
            pageSize < 1 || pageSize > 100)
        {
            return BadRequest("Invalid pagination.");
        }

        if (statusCode.HasValue &&
            (statusCode.Value < 100 || statusCode.Value > 599))
        {
            return BadRequest("Invalid HTTP status code.");
        }

        var query = _db.AuditEvents.AsNoTracking();

        if (statusCode.HasValue)
        {
            query = query.Where(
                audit => audit.StatusCode == statusCode.Value);
        }

        var total = await query.CountAsync(ct);

        var events = await query
            .OrderByDescending(audit => audit.OccurredAt)
            .ThenByDescending(audit => audit.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(audit => new
            {
                audit.Id,
                audit.UserId,
                audit.Method,
                audit.Path,
                audit.StatusCode,
                audit.OccurredAt
            })
            .ToListAsync(ct);

        // Do not expose IP addresses in the default response.
        return Ok(new
        {
            page,
            pageSize,
            total,
            items = events
        });
    }

    [HttpPost("security/login-summary")]
    public async Task<IActionResult> LoginSummary(
        [FromBody] AdminMfaRequest request,
        CancellationToken ct)
    {
        var denied = await CheckAccess(request, ct);
        if (denied is not null) return denied;

        var total = await _db.LoginAudits
            .CountAsync(ct);

        var failed = await _db.LoginAudits
            .CountAsync(x => !x.Succeeded, ct);

        var otpRequired = await _db.LoginAudits
            .CountAsync(x => x.OtpRequired, ct);

        return Ok(new
        {
            totalAttempts = total,
            failedAttempts = failed,
            successfulAttempts = total - failed,
            otpRequiredAttempts = otpRequired
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
