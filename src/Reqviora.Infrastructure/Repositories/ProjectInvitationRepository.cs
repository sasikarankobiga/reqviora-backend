using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Reqviora.Application.Features.Sharing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Repositories;

public sealed class ProjectInvitationRepository(ReqvioraDbContext db) : IProjectInvitationRepository
{
    public async Task<IProjectInvitationTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new InvitationTransaction(await db.Database.BeginTransactionAsync(ct));

    public Task<Project?> FindProjectAsync(Guid projectId, CancellationToken ct) =>
        db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, ct);

    public Task<User?> FindUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);

    public Task<User?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, ct);

    public Task<ProjectInvitation?> FindPendingAsync(Guid projectId, string normalizedEmail, CancellationToken ct) =>
        db.ProjectInvitations.SingleOrDefaultAsync(x => x.ProjectId == projectId &&
            x.NormalizedRecipientEmail == normalizedEmail && x.Status == ProjectInvitationStatuses.Pending, ct);

    public Task<ProjectInvitation?> FindByIdAsync(Guid projectId, Guid invitationId, CancellationToken ct) =>
        db.ProjectInvitations.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == invitationId, ct);

    public Task<ProjectInvitation?> FindByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        db.ProjectInvitations.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<ProjectInvitation>> ListAsync(Guid projectId, CancellationToken ct) =>
        await db.ProjectInvitations.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAtUnixTimeMilliseconds).ThenByDescending(x => x.Id).ToArrayAsync(ct);

    public Task<bool> IsMemberAsync(Guid projectId, Guid userId, CancellationToken ct) =>
        db.ProjectMembers.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);

    public void AddInvitation(ProjectInvitation invitation) => db.ProjectInvitations.Add(invitation);
    public void AddMember(ProjectMember member) => db.ProjectMembers.Add(member);
    public void AddAudit(AuditEvent auditEvent) => db.AuditEvents.Add(auditEvent);
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private sealed class InvitationTransaction(IDbContextTransaction transaction) : IProjectInvitationTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
