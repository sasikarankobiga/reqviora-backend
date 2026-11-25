
using System.Security.Claims;
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
public sealed class AdminOverviewController : ControllerBase
{
    private readonly ReqvioraDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly LoginSecurityService _security;

    public AdminOverviewController(
        ReqvioraDbContext db,
        IConfiguration configuration,
        LoginSecurityService security)
    {
        _db = db;
        _configuration = configuration;
        _security = security;
    }

    public sealed record OverviewRequest(string Code);

    public sealed record OverviewResponse(
        int Users,
        int Projects,
        int Drafts,
        int AuditEvents);

    [HttpPost("overview")]
    [ResponseCache(
        NoStore = true,
        Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetOverview(
        [FromBody] OverviewRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve authenticated user
        var userIdText =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdText, out var userId))
        {
            return Unauthorized();
        }

        // 2. Load user from database
        var user = await _db.Users
            .SingleOrDefaultAsync(
                x => x.Id == userId,
                cancellationToken);

        if (user is null)
        {
            return Unauthorized();
        }

        // 3. Load admin allowlist from configuration
        var adminEmails = _configuration
            .GetSection("Security:AdminEmails")
            .Get<string[]>() ?? Array.Empty<string>();

        // 4. Verify admin access
        if (!AdminOverviewAccess.IsEligible(
                user, adminEmails))
        {
            return Forbid();
        }

        // 5. Require fresh MFA code
        var code = request.Code?.Trim() ?? string.Empty;

        if (code.Length != 6 ||
            !code.All(c => c >= '0' && c <= '9') ||
            !_security.ValidateOtp(user, code))
        {
            return Unauthorized(new
            {
                message = "Admin MFA verification failed."
            });
        }

        // 6. Read-only system statistics
        var result = new OverviewResponse(
            Users: await _db.Users.CountAsync(
                cancellationToken),
            Projects: await _db.Projects.CountAsync(
                cancellationToken),
            Drafts: await _db.ProjectDrafts.CountAsync(
                cancellationToken),
            AuditEvents: await _db.AuditEvents.CountAsync(
                cancellationToken)
        );

        return Ok(result);
    }
}
