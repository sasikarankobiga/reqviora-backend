using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.AdminProfile;

namespace Reqviora.Api.Controllers;

/// <summary>
/// Self-service session controls. The service returns metadata only and scopes every
/// mutation to the authenticated subject, so refresh/access-token material cannot leak.
/// </summary>
[ApiController, Authorize, Route("api/account-security/sessions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SessionManagementController(IAdminProfileService profiles) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AdminSessionDetails>> List(CancellationToken ct) =>
        profiles.SessionsAsync(UserId(), CurrentSessionId(required: false), ct);

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Revoke(Guid sessionId, CancellationToken ct)
    {
        await profiles.RevokeSessionAsync(UserId(), sessionId, ct);
        return NoContent();
    }

    [HttpPost("revoke-others")]
    public async Task<ActionResult<object>> RevokeOthers(CancellationToken ct)
    {
        var current = CurrentSessionId(required: true)!.Value;
        var revoked = await profiles.RevokeOtherSessionsAsync(UserId(), current, ct);
        return Ok(new { revoked });
    }

    private Guid UserId() => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id
        : throw new UnauthorizedAccessException("Authenticated user identity is required.");

    private Guid? CurrentSessionId(bool required)
    {
        var value = User.FindFirstValue(TokenClaimTypes.SessionId);
        if (Guid.TryParse(value, out var id)) return id;
        if (required) throw new UnauthorizedAccessException("A session-bound access token is required.");
        return null;
    }
}
