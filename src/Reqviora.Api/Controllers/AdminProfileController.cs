using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Configuration;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.AdminProfile;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = AuthorizationPolicies.Administrator), Route("api/admin/profile")]
public sealed class AdminProfileController(IAdminProfileService profiles) : ControllerBase
{
    public sealed record UpdateProfileRequest([Required, StringLength(200, MinimumLength = 1)] string DisplayName)
    {
        [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
    }

    [HttpGet]
    public Task<AdminProfileDetails> Get(CancellationToken ct) => profiles.GetAsync(UserId(), ct);

    [HttpPut]
    public Task<AdminProfileDetails> Update(UpdateProfileRequest request, CancellationToken ct)
    {
        if (request.AdditionalProperties is { Count: > 0 })
            throw new ArgumentException("Only the displayName field may be changed.");
        return profiles.UpdateAsync(UserId(), request.DisplayName, ct);
    }

    [HttpGet("security")]
    public Task<AdminSecurityDetails> Security(CancellationToken ct) => profiles.SecurityAsync(UserId(), ct);

    [HttpGet("sessions")]
    public Task<IReadOnlyList<AdminSessionDetails>> Sessions(CancellationToken ct) =>
        profiles.SessionsAsync(UserId(), CurrentSessionId(required: false), ct);

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        await profiles.RevokeSessionAsync(UserId(), sessionId, ct);
        return NoContent();
    }

    [HttpPost("sessions/revoke-others")]
    public async Task<ActionResult<object>> RevokeOthers(CancellationToken ct)
    {
        var revoked = await profiles.RevokeOtherSessionsAsync(UserId(), CurrentSessionId(required: true)!.Value, ct);
        return Ok(new { revoked });
    }

    [HttpGet("login-history")]
    public Task<IReadOnlyList<AdminLoginHistoryEntry>> LoginHistory(
        [FromQuery, Range(1, 1_000_000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50,
        CancellationToken ct = default) => profiles.LoginHistoryAsync(UserId(), page, pageSize, ct);

    private Guid UserId() => ProjectApiAccess.RequireUserId(User);

    private Guid? CurrentSessionId(bool required)
    {
        var value = User.FindFirstValue(TokenClaimTypes.SessionId);
        if (Guid.TryParse(value, out var sessionId)) return sessionId;
        if (required) throw new InvalidOperationException("This action requires a session-bound sign-in.");
        return null;
    }
}
