using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Sharing;

public sealed record ProjectInvitationDetails(Guid Id, Guid ProjectId, string RecipientEmail, string Role,
    string Status, Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    Guid? AcceptedBy, DateTimeOffset? AcceptedAt, Guid? RejectedBy, DateTimeOffset? RejectedAt,
    Guid? RevokedBy, DateTimeOffset? RevokedAt, Guid? SupersedesInvitationId);

public sealed record AcceptedProjectMembership(Guid ProjectId, Guid UserId, string Role, DateTimeOffset JoinedAt);

public interface IProjectInvitationService
{
    Task<ProjectInvitationDetails> CreateAsync(Guid projectId, Guid ownerId, string recipientEmail, string role,
        CancellationToken ct);
    Task<IReadOnlyList<ProjectInvitationDetails>> ListAsync(Guid projectId, Guid ownerId,
        CancellationToken ct);
    Task<ProjectInvitationDetails> RevokeAsync(Guid projectId, Guid invitationId, Guid ownerId,
        CancellationToken ct);
    Task<ProjectInvitationDetails> ResendAsync(Guid projectId, Guid invitationId, Guid ownerId,
        CancellationToken ct);
    Task<AcceptedProjectMembership> AcceptAsync(string rawToken, Guid recipientId, CancellationToken ct);
    Task<ProjectInvitationDetails> RejectAsync(string rawToken, Guid recipientId, CancellationToken ct);
}

public interface IProjectInvitationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public interface IProjectInvitationRepository
{
    Task<IProjectInvitationTransaction> BeginTransactionAsync(CancellationToken ct);
    Task<Project?> FindProjectAsync(Guid projectId, CancellationToken ct);
    Task<User?> FindUserAsync(Guid userId, CancellationToken ct);
    Task<User?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<ProjectInvitation?> FindPendingAsync(Guid projectId, string normalizedEmail, CancellationToken ct);
    Task<ProjectInvitation?> FindByIdAsync(Guid projectId, Guid invitationId, CancellationToken ct);
    Task<ProjectInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken ct);
    Task<IReadOnlyList<ProjectInvitation>> ListAsync(Guid projectId, CancellationToken ct);
    Task<bool> IsMemberAsync(Guid projectId, Guid userId, CancellationToken ct);
    void AddInvitation(ProjectInvitation invitation);
    void AddMember(ProjectMember member);
    void AddAudit(AuditEvent auditEvent);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
