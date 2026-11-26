using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Configuration;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.PlatformSettings;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = AuthorizationPolicies.Administrator), Route("api/admin/settings")]
public sealed class AdminPlatformSettingsController(IPlatformSettings settings) : ControllerBase
{
    public sealed record ChangePlatformSettingRequest([Required] long? Value, [Required, Range(0, int.MaxValue)] int? ExpectedRevision,
        [Required, StringLength(500, MinimumLength = 3)] string Reason);
    public sealed record RollbackPlatformSettingRequest([Required, Range(1, int.MaxValue)] int? TargetRevision,
        [Required, Range(1, int.MaxValue)] int? ExpectedRevision, [Required, StringLength(500, MinimumLength = 3)] string Reason);

    [HttpGet]
    public async Task<PlatformSetting[]> List(CancellationToken ct) =>
        [await settings.GetAsync(IPlatformSettings.DocumentUploadMaxBytes, ct)];

    [HttpGet("{key}")]
    public Task<PlatformSetting> Get(string key, CancellationToken ct) => settings.GetAsync(key, ct);

    [HttpPut("{key}")]
    public Task<PlatformSetting> Change(string key, ChangePlatformSettingRequest request, CancellationToken ct) =>
        settings.ChangeAsync(key, request.Value!.Value, request.ExpectedRevision!.Value, request.Reason,
            ProjectApiAccess.RequireUserId(User), ct);

    [HttpGet("{key}/history")]
    public Task<IReadOnlyList<SettingHistoryEntry>> History(string key,
        [FromQuery, Range(1, 1_000_000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50,
        CancellationToken ct = default) => settings.HistoryAsync(key, page, pageSize, ct);

    [HttpPost("{key}/rollback")]
    public Task<PlatformSetting> Rollback(string key, RollbackPlatformSettingRequest request, CancellationToken ct) =>
        settings.RollbackAsync(key, request.TargetRevision!.Value, request.ExpectedRevision!.Value, request.Reason,
            ProjectApiAccess.RequireUserId(User), ct);
}
