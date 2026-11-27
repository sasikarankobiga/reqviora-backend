using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Mcp;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class McpGrantService(ReqvioraDbContext db, TimeProvider clock) : IMcpGrantService
{
    private const int MaxPageSize = 100;

    public async Task<McpGrantCreated> CreateAsync(
        Guid projectId,
        Guid ownerId,
        Guid approvedRevisionId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (lifetime < TimeSpan.FromMinutes(5) || lifetime > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Grant lifetime must be between five minutes and 30 days.");

        var revisionExists = await db.ApprovedRevisions.AsNoTracking().AnyAsync(
            revision => revision.Id == approvedRevisionId && revision.ProjectId == projectId,
            cancellationToken);
        var ownsProject = await db.Projects.AsNoTracking().AnyAsync(
            project => project.Id == projectId && project.OwnerId == ownerId,
            cancellationToken);
        if (!revisionExists || !ownsProject)
            throw new UnauthorizedAccessException("An approved revision owned by the caller is required.");

        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var now = clock.GetUtcNow();
        var grant = new McpAccessGrant
        {
            ProjectId = projectId,
            ApprovedRevisionId = approvedRevisionId,
            OwnerId = ownerId,
            SecretHash = Hash(credential),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime)
        };
        db.McpAccessGrants.Add(grant);
        AddAudit(ownerId, "POST", $"/security/mcp/grants/{grant.Id}/created", 201, now);
        await db.SaveChangesAsync(cancellationToken);
        return new McpGrantCreated(ToMetadata(grant, now), credential);
    }

    public async Task<McpGrantPage> ListOwnerAsync(
        Guid projectId,
        Guid ownerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Projects.AsNoTracking().AnyAsync(
                project => project.Id == projectId && project.OwnerId == ownerId,
                cancellationToken))
            throw new UnauthorizedAccessException("Project owner access is required.");
        var query = db.McpAccessGrants.AsNoTracking()
            .Where(grant => grant.ProjectId == projectId && grant.OwnerId == ownerId);
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
            return PageInMemory(await query.ToArrayAsync(cancellationToken), page, pageSize, clock.GetUtcNow());
        return await PageAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<McpGrantPage> ListAdminAsync(
        int page,
        int pageSize,
        string? search,
        string? state,
        Guid? projectId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var query = db.McpAccessGrants.AsNoTracking();
        if (projectId.HasValue) query = query.Where(grant => grant.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            if (!Guid.TryParse(search.Trim(), out var id))
                return new McpGrantPage(page, pageSize, 0, []);
            query = query.Where(grant => grant.Id == id || grant.ProjectId == id || grant.OwnerId == id);
        }
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
        {
            var rows = await query.ToArrayAsync(cancellationToken);
            var filtered = state?.ToLowerInvariant() switch
            {
                "active" => rows.Where(grant => grant.RevokedAt == null && grant.ExpiresAt > now),
                "expired" => rows.Where(grant => grant.RevokedAt == null && grant.ExpiresAt <= now),
                "revoked" => rows.Where(grant => grant.RevokedAt != null),
                null or "" or "all" => rows.AsEnumerable(),
                _ => throw new ArgumentException("Unsupported grant state.", nameof(state))
            };
            return PageInMemory(filtered, page, pageSize, now);
        }
        query = state?.ToLowerInvariant() switch
        {
            "active" => query.Where(grant => grant.RevokedAt == null && grant.ExpiresAt > now),
            "expired" => query.Where(grant => grant.RevokedAt == null && grant.ExpiresAt <= now),
            "revoked" => query.Where(grant => grant.RevokedAt != null),
            null or "" or "all" => query,
            _ => throw new ArgumentException("Unsupported grant state.", nameof(state))
        };
        return await PageAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<McpGrantMetadata?> FindAdminAsync(Guid grantId, CancellationToken cancellationToken = default)
    {
        var grant = await db.McpAccessGrants.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == grantId, cancellationToken);
        return grant is null ? null : ToMetadata(grant, clock.GetUtcNow());
    }

    public async Task<McpGrantMetadata?> RevokeAsync(
        Guid grantId,
        Guid actorId,
        string reason,
        bool administrator,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A revocation reason is required.", nameof(reason));
        var grant = await db.McpAccessGrants.SingleOrDefaultAsync(item => item.Id == grantId &&
            (administrator || (item.OwnerId == actorId && item.ProjectId == projectId)), cancellationToken);
        if (grant is null) return null;
        if (grant.RevokedAt is not null) return ToMetadata(grant, clock.GetUtcNow());

        var now = clock.GetUtcNow();
        grant.RevokedAt = now;
        grant.RevokedBy = actorId;
        grant.RevocationReason = reason.Trim();
        grant.ConcurrencyToken = Guid.NewGuid();
        AddAudit(actorId, "POST", $"/security/mcp/grants/{grant.Id}/revoked", 200, now);
        await db.SaveChangesAsync(cancellationToken);
        return ToMetadata(grant, now);
    }

    public async Task<bool> ValidateAsync(
        string credential,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credential)) return false;
        var hash = Hash(credential);
        var grant = await db.McpAccessGrants.AsNoTracking().SingleOrDefaultAsync(item =>
            item.SecretHash == hash && item.ProjectId == projectId && item.OwnerId == ownerId,
            cancellationToken);
        return grant is not null && grant.RevokedAt is null && grant.ExpiresAt > clock.GetUtcNow();
    }

    private async Task<McpGrantPage> PageAsync(
        IQueryable<McpAccessGrant> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(page), "Page and page size are outside the supported range.");
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(grant => grant.CreatedAt).ThenBy(grant => grant.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        var now = clock.GetUtcNow();
        return new McpGrantPage(page, pageSize, total, rows.Select(grant => ToMetadata(grant, now)).ToArray());
    }

    private static McpGrantPage PageInMemory(
        IEnumerable<McpAccessGrant> query,
        int page,
        int pageSize,
        DateTimeOffset now)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(page), "Page and page size are outside the supported range.");
        var ordered = query.OrderByDescending(grant => grant.CreatedAt).ThenBy(grant => grant.Id).ToArray();
        return new McpGrantPage(page, pageSize, ordered.Length,
            ordered.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(grant => ToMetadata(grant, now)).ToArray());
    }

    private void AddAudit(Guid actorId, string method, string path, int statusCode, DateTimeOffset now) =>
        db.AuditEvents.Add(new AuditEvent
        {
            UserId = actorId,
            Method = method,
            Path = path,
            StatusCode = statusCode,
            IpAddress = "security-service",
            OccurredAt = now
        });

    private static string Hash(string credential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

    private static McpGrantMetadata ToMetadata(McpAccessGrant grant, DateTimeOffset now) => new(
        grant.Id,
        grant.ProjectId,
        grant.ApprovedRevisionId,
        grant.OwnerId,
        grant.CreatedAt,
        grant.ExpiresAt,
        grant.RevokedAt,
        grant.RevokedBy,
        grant.RevocationReason,
        grant.RevokedAt is not null ? "revoked" : grant.ExpiresAt <= now ? "expired" : "active");
}
