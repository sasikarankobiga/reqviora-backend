using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/admin")]
public sealed class AdminController(ReqvioraDbContext db, TimeProvider clock) : ControllerBase
{
    public sealed record PageResponse<T>(int Page, int PageSize, int Total, IReadOnlyList<T> Items);
    public sealed record AdminDashboardResponse(int Users, int DisabledUsers, int Projects, int Workspaces,
        int PendingAiJobs, int AuditEventsLast24Hours, int FailedLoginsLast24Hours);
    public sealed record AdminUserResponse(Guid Id, string Email, string DisplayName, bool EmailVerified,
        bool MfaEnabled, bool IsDisabled, DateTimeOffset? DisabledAt, string? DisabledReason,
        string Role, int SessionVersion);
    // The general administration directory is intentionally metadata-only. Project content,
    // owner contact details, drafts, snapshots, and exports require a separately audited
    // support workflow and must never be returned by a directory endpoint.
    public sealed record AdminProjectResponse(Guid Id, string Name, Guid OwnerId,
        Guid? WorkspaceId, int MemberCount, int DraftVersionCount, int ApprovedRevisionCount);
    public sealed record AdminAuditResponse(Guid Id, Guid? UserId, string Method, string Path,
        int StatusCode, string IpAddress, DateTimeOffset OccurredAt);
    public sealed record AccountStatusRequest(bool Disabled,
        [StringLength(500)] string? Reason);
    public sealed record RoleRequest([Required, StringLength(100)] string Role);

    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardResponse>> Dashboard(CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddHours(-24);
        int auditCount;
        int failedLoginCount;
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
        {
            auditCount = (await db.AuditEvents.AsNoTracking().Select(item => item.OccurredAt).ToArrayAsync(ct))
                .Count(occurredAt => occurredAt >= since);
            failedLoginCount = (await db.LoginAudits.AsNoTracking().Where(item => !item.Succeeded)
                .Select(item => item.OccurredAt).ToArrayAsync(ct)).Count(occurredAt => occurredAt >= since);
        }
        else
        {
            auditCount = await db.AuditEvents.CountAsync(item => item.OccurredAt >= since, ct);
            failedLoginCount = await db.LoginAudits.CountAsync(item => item.OccurredAt >= since && !item.Succeeded, ct);
        }
        return Ok(new AdminDashboardResponse(
            await db.Users.CountAsync(ct),
            await db.Users.CountAsync(user => user.IsDisabled, ct),
            await db.Projects.CountAsync(ct),
            await db.Workspaces.CountAsync(ct),
            await db.AiJobs.CountAsync(job => job.State == AiJobState.Pending || job.State == AiJobState.Running, ct),
            auditCount,
            failedLoginCount));
    }

    [HttpGet("users")]
    public async Task<ActionResult<PageResponse<AdminUserResponse>>> Users(
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50,
        [FromQuery, StringLength(200)] string? search = null,
        [FromQuery, RegularExpression("^(all|enabled|disabled)$")] string status = "all",
        CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToUpperInvariant();
            query = query.Where(user => user.NormalizedEmail.Contains(value) ||
                user.DisplayName.ToUpper().Contains(value));
        }
        if (status == "enabled") query = query.Where(user => !user.IsDisabled);
        if (status == "disabled") query = query.Where(user => user.IsDisabled);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(user => user.NormalizedEmail)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(user => new AdminUserResponse(user.Id, user.Email, user.DisplayName, user.EmailVerified,
                user.MfaEnabled, user.IsDisabled, user.DisabledAt, user.DisabledReason,
                user.RoleId == null ? "User" : db.Roles.Where(role => role.Id == user.RoleId)
                    .Select(role => role.Name).SingleOrDefault() ?? "User",
                user.SessionVersion))
            .ToArrayAsync(ct);
        return Ok(new PageResponse<AdminUserResponse>(page, pageSize, total, items));
    }

    [HttpPut("users/{userId:guid}/status")]
    public async Task<ActionResult<AdminUserResponse>> SetStatus(
        Guid userId, AccountStatusRequest request, CancellationToken ct)
    {
        var actorId = RequireUserId();
        if (request.Disabled && actorId == userId)
            return BadRequest(new ProblemDetails { Title = "Administrators cannot disable their own account." });
        if (request.Disabled && string.IsNullOrWhiteSpace(request.Reason))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Reason)] = ["A reason is required when disabling an account."]
            }));
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        user.IsDisabled = request.Disabled;
        user.DisabledAt = request.Disabled ? clock.GetUtcNow() : null;
        user.DisabledReason = request.Disabled ? request.Reason!.Trim() : null;
        user.SessionVersion = checked(user.SessionVersion + 1);
        await db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, clock.GetUtcNow()), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(await UserResponseAsync(userId, ct));
    }

    [HttpPut("users/{userId:guid}/role")]
    public async Task<ActionResult<AdminUserResponse>> SetRole(
        Guid userId, RoleRequest request, CancellationToken ct)
    {
        if (RequireUserId() == userId)
            return BadRequest(new ProblemDetails { Title = "Administrators cannot change their own role." });
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null) return NotFound();
        var roleName = request.Role.Trim();
        Guid? roleId = null;
        if (!roleName.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            roleId = await db.Roles.Where(role => role.Name.ToUpper() == roleName.ToUpper())
                .Select(role => (Guid?)role.Id).SingleOrDefaultAsync(ct);
            if (roleId is null)
                return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    [nameof(request.Role)] = ["The requested role does not exist."]
                }));
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        user.RoleId = roleId;
        user.SessionVersion = checked(user.SessionVersion + 1);
        await db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, clock.GetUtcNow()), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(await UserResponseAsync(userId, ct));
    }

    [HttpGet("projects")]
    public async Task<ActionResult<PageResponse<AdminProjectResponse>>> Projects(
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50,
        [FromQuery, StringLength(200)] string? search = null,
        [FromQuery] Guid? ownerId = null,
        CancellationToken ct = default)
    {
        var query = db.Projects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToUpper();
            query = query.Where(project => project.Name.ToUpper().Contains(value));
        }
        if (ownerId.HasValue) query = query.Where(project => project.OwnerId == ownerId);
        var total = await query.CountAsync(ct);
        var items = await LoadProjectsAsync(query, (page - 1) * pageSize, pageSize, ct);
        return Ok(new PageResponse<AdminProjectResponse>(page, pageSize, total, items));
    }

    [HttpGet("projects/{projectId:guid}")]
    public async Task<ActionResult<AdminProjectResponse>> Project(Guid projectId, CancellationToken ct)
    {
        var item = (await LoadProjectsAsync(
            db.Projects.AsNoTracking().Where(project => project.Id == projectId), 0, 1, ct)).SingleOrDefault();
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("audit-events")]
    public async Task<ActionResult<PageResponse<AdminAuditResponse>>> AuditEvents(
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50,
        [FromQuery] Guid? userId = null,
        [FromQuery, Range(100, 599)] int? statusCode = null,
        [FromQuery, StringLength(200)] string? path = null,
        CancellationToken ct = default)
    {
        var query = db.AuditEvents.AsNoTracking();
        if (userId.HasValue) query = query.Where(item => item.UserId == userId);
        if (statusCode.HasValue) query = query.Where(item => item.StatusCode == statusCode);
        if (!string.IsNullOrWhiteSpace(path)) query = query.Where(item => item.Path.Contains(path.Trim()));
        var total = await query.CountAsync(ct);
        AdminAuditResponse[] items;
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
        {
            items = (await query.ToArrayAsync(ct)).OrderByDescending(item => item.OccurredAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(item => new AdminAuditResponse(item.Id, item.UserId, item.Method, item.Path,
                    item.StatusCode, item.IpAddress, item.OccurredAt)).ToArray();
        }
        else
        {
            items = await query.OrderByDescending(item => item.OccurredAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(item => new AdminAuditResponse(item.Id, item.UserId, item.Method, item.Path,
                    item.StatusCode, item.IpAddress, item.OccurredAt)).ToArrayAsync(ct);
        }
        return Ok(new PageResponse<AdminAuditResponse>(page, pageSize, total, items));
    }

    private async Task<AdminProjectResponse[]> LoadProjectsAsync(
        IQueryable<Project> query, int skip, int take, CancellationToken ct)
    {
        var rows = await (from project in query
            orderby project.Name, project.Id
            select new
            {
                project.Id, project.Name, project.OwnerId, project.WorkspaceId
            }).Skip(skip).Take(take).ToArrayAsync(ct);
        var ids = rows.Select(row => row.Id).ToArray();
        var members = await db.ProjectMembers.AsNoTracking().Where(item => ids.Contains(item.ProjectId))
            .GroupBy(item => item.ProjectId).Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, ct);
        var versions = await db.DesignDraftVersions.AsNoTracking().Where(item => ids.Contains(item.ProjectId))
            .GroupBy(item => item.ProjectId).Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, ct);
        var revisions = await db.ApprovedRevisions.AsNoTracking().Where(item => ids.Contains(item.ProjectId))
            .GroupBy(item => item.ProjectId).Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, ct);
        return rows.Select(row => new AdminProjectResponse(row.Id, row.Name,
            row.OwnerId, row.WorkspaceId,
            members.GetValueOrDefault(row.Id), versions.GetValueOrDefault(row.Id),
            revisions.GetValueOrDefault(row.Id))).ToArray();
    }

    private async Task<AdminUserResponse> UserResponseAsync(Guid userId, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => new AdminUserResponse(user.Id, user.Email, user.DisplayName, user.EmailVerified,
                user.MfaEnabled, user.IsDisabled, user.DisabledAt, user.DisabledReason,
                user.RoleId == null ? "User" : db.Roles.Where(role => role.Id == user.RoleId)
                    .Select(role => role.Name).SingleOrDefault() ?? "User",
                user.SessionVersion)).SingleAsync(ct);

    private Guid RequireUserId() => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id
        : throw new UnauthorizedAccessException("Authenticated user identity is required.");
}
