namespace Reqviora.Application.Features.Mcp;

public sealed record McpGrantMetadata(
    Guid Id,
    Guid ProjectId,
    Guid ApprovedRevisionId,
    Guid OwnerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    Guid? RevokedBy,
    string? RevocationReason,
    string State);

public sealed record McpGrantCreated(McpGrantMetadata Grant, string Credential);

public sealed record McpGrantPage(int Page, int PageSize, int Total, IReadOnlyList<McpGrantMetadata> Items);

public interface IMcpGrantService
{
    Task<McpGrantCreated> CreateAsync(
        Guid projectId,
        Guid ownerId,
        Guid approvedRevisionId,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<McpGrantPage> ListOwnerAsync(
        Guid projectId,
        Guid ownerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<McpGrantPage> ListAdminAsync(
        int page,
        int pageSize,
        string? search,
        string? state,
        Guid? projectId,
        CancellationToken cancellationToken = default);

    Task<McpGrantMetadata?> FindAdminAsync(Guid grantId, CancellationToken cancellationToken = default);

    Task<McpGrantMetadata?> RevokeAsync(
        Guid grantId,
        Guid actorId,
        string reason,
        bool administrator,
        Guid? projectId = null,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateAsync(
        string credential,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an active grant for a project owner without ever returning its credential or hash.
    /// The returned revision identifier is the immutable boundary for all MCP reads.
    /// </summary>
    Task<McpGrantMetadata?> ResolveActiveAsync(
        string credential,
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
