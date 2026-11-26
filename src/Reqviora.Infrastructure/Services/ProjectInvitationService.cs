using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Sharing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ProjectInvitationService(
    IProjectInvitationRepository repository,
    IEmailService email,
    IOptions<ProjectInvitationOptions> options,
    IConfiguration configuration,
    TimeProvider clock) : IProjectInvitationService
{
    public async Task<ProjectInvitationDetails> CreateAsync(Guid projectId, Guid ownerId, string recipientEmail,
        string role, CancellationToken ct)
    {
        var (emailAddress, normalizedEmail) = NormalizeEmail(recipientEmail);
        role = NormalizeRole(role);
        var project = await RequireOwnerAsync(projectId, ownerId, ct);
        var owner = await repository.FindUserAsync(project.OwnerId, ct)
            ?? throw new KeyNotFoundException("The project owner was not found.");
        if (owner.NormalizedEmail == normalizedEmail)
            throw new InvalidOperationException("The project owner cannot be invited as a member.");

        var now = clock.GetUtcNow();
        ProjectInvitation invitation;
        string rawToken;
        await using (var transaction = await repository.BeginTransactionAsync(ct))
        {
            var pending = await repository.FindPendingAsync(projectId, normalizedEmail, ct);
            if (pending is not null && pending.ExpiresAt > now)
                throw new InvalidOperationException("A pending invitation already exists for this recipient.");
            if (pending is not null)
                Expire(pending);

            var recipient = await repository.FindUserByNormalizedEmailAsync(normalizedEmail, ct);
            if (recipient is not null && await repository.IsMemberAsync(projectId, recipient.Id, ct))
                throw new InvalidOperationException("The recipient is already a project member.");

            (rawToken, var tokenHash) = IssueToken();
            invitation = NewInvitation(projectId, ownerId, emailAddress, normalizedEmail, role, tokenHash, now, null);
            repository.AddInvitation(invitation);
            AddAudit(invitation.Id, ownerId, "POST", "created", 201, now);
            await SaveLifecycleAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await SendInvitationAsync(invitation, rawToken, ct);
        return Map(invitation, now);
    }

    public async Task<IReadOnlyList<ProjectInvitationDetails>> ListAsync(Guid projectId, Guid ownerId,
        CancellationToken ct)
    {
        await RequireOwnerAsync(projectId, ownerId, ct);
        var now = clock.GetUtcNow();
        return (await repository.ListAsync(projectId, ct)).Select(x => Map(x, now)).ToArray();
    }

    public async Task<ProjectInvitationDetails> RevokeAsync(Guid projectId, Guid invitationId, Guid ownerId,
        CancellationToken ct)
    {
        await RequireOwnerAsync(projectId, ownerId, ct);
        var now = clock.GetUtcNow();
        await using var transaction = await repository.BeginTransactionAsync(ct);
        var invitation = await repository.FindByIdAsync(projectId, invitationId, ct)
            ?? throw new KeyNotFoundException("The invitation was not found.");
        if (invitation.Status == ProjectInvitationStatuses.Revoked)
            return Map(invitation, now);
        if (EffectiveStatus(invitation, now) != ProjectInvitationStatuses.Pending)
            throw new InvalidOperationException("Only a pending invitation can be revoked.");
        invitation.Status = ProjectInvitationStatuses.Revoked;
        invitation.RevokedAt = now;
        invitation.RevokedBy = ownerId;
        invitation.PendingRecipientKey = null;
        invitation.ConcurrencyToken = Guid.NewGuid();
        AddAudit(invitation.Id, ownerId, "POST", "revoked", 200, now);
        await SaveLifecycleAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(invitation, now);
    }

    public async Task<ProjectInvitationDetails> ResendAsync(Guid projectId, Guid invitationId, Guid ownerId,
        CancellationToken ct)
    {
        await RequireOwnerAsync(projectId, ownerId, ct);
        var now = clock.GetUtcNow();
        ProjectInvitation replacement;
        string rawToken;
        await using (var transaction = await repository.BeginTransactionAsync(ct))
        {
            var invitation = await repository.FindByIdAsync(projectId, invitationId, ct)
                ?? throw new KeyNotFoundException("The invitation was not found.");
            var state = EffectiveStatus(invitation, now);
            if (state is not (ProjectInvitationStatuses.Pending or ProjectInvitationStatuses.Expired))
                throw new InvalidOperationException("Only pending or expired invitations can be resent.");
            var recipient = await repository.FindUserByNormalizedEmailAsync(
                invitation.NormalizedRecipientEmail, ct);
            if (recipient is not null && await repository.IsMemberAsync(projectId, recipient.Id, ct))
                throw new InvalidOperationException("The recipient is already a project member.");

            invitation.Status = ProjectInvitationStatuses.Revoked;
            invitation.RevokedAt = now;
            invitation.RevokedBy = ownerId;
            invitation.PendingRecipientKey = null;
            invitation.ConcurrencyToken = Guid.NewGuid();
            (rawToken, var tokenHash) = IssueToken();
            replacement = NewInvitation(projectId, ownerId, invitation.RecipientEmail,
                invitation.NormalizedRecipientEmail, invitation.Role, tokenHash, now, invitation.Id);
            repository.AddInvitation(replacement);
            AddAudit(invitation.Id, ownerId, "POST", "superseded", 200, now);
            AddAudit(replacement.Id, ownerId, "POST", "resent", 201, now);
            await SaveLifecycleAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await SendInvitationAsync(replacement, rawToken, ct);
        return Map(replacement, now);
    }

    public async Task<AcceptedProjectMembership> AcceptAsync(string rawToken, Guid recipientId, CancellationToken ct)
    {
        var tokenHash = HashToken(ValidateRawToken(rawToken));
        var now = clock.GetUtcNow();
        await using var transaction = await repository.BeginTransactionAsync(ct);
        var invitation = await repository.FindByTokenHashAsync(tokenHash, ct)
            ?? throw new ArgumentException("The invitation token is invalid.", nameof(rawToken));
        var recipient = await RequireRecipientAsync(invitation, recipientId, ct);
        EnsurePending(invitation, now);
        if (await repository.IsMemberAsync(invitation.ProjectId, recipientId, ct))
            throw new InvalidOperationException("The recipient is already a project member.");

        invitation.Status = ProjectInvitationStatuses.Accepted;
        invitation.AcceptedBy = recipientId;
        invitation.AcceptedAt = now;
        invitation.PendingRecipientKey = null;
        invitation.ConcurrencyToken = Guid.NewGuid();
        var member = new ProjectMember
        {
            ProjectId = invitation.ProjectId,
            UserId = recipientId,
            Role = invitation.Role,
            JoinedAtUtc = now.UtcDateTime
        };
        repository.AddMember(member);
        AddAudit(invitation.Id, recipientId, "POST", "accepted", 200, now);
        await SaveLifecycleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(invitation.ProjectId, recipientId, invitation.Role, now);
    }

    public async Task<ProjectInvitationDetails> RejectAsync(string rawToken, Guid recipientId, CancellationToken ct)
    {
        var tokenHash = HashToken(ValidateRawToken(rawToken));
        var now = clock.GetUtcNow();
        await using var transaction = await repository.BeginTransactionAsync(ct);
        var invitation = await repository.FindByTokenHashAsync(tokenHash, ct)
            ?? throw new ArgumentException("The invitation token is invalid.", nameof(rawToken));
        await RequireRecipientAsync(invitation, recipientId, ct);
        EnsurePending(invitation, now);
        invitation.Status = ProjectInvitationStatuses.Rejected;
        invitation.RejectedBy = recipientId;
        invitation.RejectedAt = now;
        invitation.PendingRecipientKey = null;
        invitation.ConcurrencyToken = Guid.NewGuid();
        AddAudit(invitation.Id, recipientId, "POST", "rejected", 200, now);
        await SaveLifecycleAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(invitation, now);
    }

    private async Task<Project> RequireOwnerAsync(Guid projectId, Guid ownerId, CancellationToken ct)
    {
        var project = await repository.FindProjectAsync(projectId, ct)
            ?? throw new KeyNotFoundException("The project was not found.");
        if (project.OwnerId != ownerId)
            throw new UnauthorizedAccessException("Project owner access is required.");
        return project;
    }

    private async Task<User> RequireRecipientAsync(ProjectInvitation invitation, Guid recipientId,
        CancellationToken ct)
    {
        var recipient = await repository.FindUserAsync(recipientId, ct)
            ?? throw new UnauthorizedAccessException("The authenticated recipient was not found.");
        if (!recipient.EmailVerified)
            throw new InvalidOperationException("The recipient email must be verified before responding.");
        if (!string.Equals(recipient.NormalizedEmail, invitation.NormalizedRecipientEmail,
                StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The invitation belongs to another recipient.");
        return recipient;
    }

    private void EnsurePending(ProjectInvitation invitation, DateTimeOffset now)
    {
        var state = EffectiveStatus(invitation, now);
        if (state != ProjectInvitationStatuses.Pending)
            throw new InvalidOperationException($"The invitation is {state.ToLowerInvariant()} and cannot be used.");
    }

    private ProjectInvitation NewInvitation(Guid projectId, Guid ownerId, string emailAddress,
        string normalizedEmail, string role, string tokenHash, DateTimeOffset now, Guid? supersedes) => new()
    {
        ProjectId = projectId,
        RecipientEmail = emailAddress,
        NormalizedRecipientEmail = normalizedEmail,
        Role = role,
        TokenHash = tokenHash,
        PendingRecipientKey = $"{projectId:N}:{normalizedEmail}",
        Status = ProjectInvitationStatuses.Pending,
        CreatedBy = ownerId,
        CreatedAt = now,
        CreatedAtUnixTimeMilliseconds = now.ToUnixTimeMilliseconds(),
        ExpiresAt = now.AddHours(Math.Clamp(options.Value.ExpirationHours, 1, 720)),
        SupersedesInvitationId = supersedes
    };

    private async Task SendInvitationAsync(ProjectInvitation invitation, string rawToken, CancellationToken ct)
    {
        var baseUrl = configuration["AccountLinks:BaseUrl"] ?? "https://localhost";
        var link = $"{baseUrl.TrimEnd('/')}/project-invitations?token={Uri.EscapeDataString(rawToken)}";
        var body = $"You have been invited to a REQVIORA project as {invitation.Role}. " +
                   $"This invitation expires at {invitation.ExpiresAt:O}. Open: {link}";
        await email.SendAsync(invitation.RecipientEmail, "REQVIORA project invitation", body, ct);
    }

    private async Task SaveLifecycleAsync(CancellationToken ct)
    {
        try
        {
            await repository.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("The invitation changed. Reload and retry.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("The invitation could not be changed because its state is no longer current.", exception);
        }
    }

    private void AddAudit(Guid invitationId, Guid actorId, string method, string action, int status,
        DateTimeOffset now) => repository.AddAudit(new AuditEvent
    {
        UserId = actorId,
        Method = method,
        Path = $"/security/project-invitations/{invitationId}/{action}",
        StatusCode = status,
        IpAddress = "security-service",
        OccurredAt = now
    });

    private static (string Address, string Normalized) NormalizeEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 320)
            throw new ArgumentException("A valid recipient email is required.", nameof(value));
        try
        {
            var address = new MailAddress(value.Trim()).Address;
            return (address, address.ToUpperInvariant());
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("A valid recipient email is required.", nameof(value), exception);
        }
    }

    private static string NormalizeRole(string role)
    {
        if (!ProjectMemberRoles.IsSupported(role))
            throw new ArgumentException("Role must be Viewer or Editor.", nameof(role));
        return ProjectMemberRoles.Normalize(role);
    }

    private static (string Raw, string Hash) IssueToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (raw, HashToken(raw));
    }

    private static string ValidateRawToken(string token) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 512
            ? throw new ArgumentException("The invitation token is invalid.", nameof(token))
            : token.Trim();

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void Expire(ProjectInvitation invitation)
    {
        invitation.Status = ProjectInvitationStatuses.Expired;
        invitation.PendingRecipientKey = null;
        invitation.ConcurrencyToken = Guid.NewGuid();
    }

    private static string EffectiveStatus(ProjectInvitation invitation, DateTimeOffset now) =>
        invitation.Status == ProjectInvitationStatuses.Pending && invitation.ExpiresAt <= now
            ? ProjectInvitationStatuses.Expired
            : invitation.Status;

    private static ProjectInvitationDetails Map(ProjectInvitation invitation, DateTimeOffset now) => new(
        invitation.Id, invitation.ProjectId, invitation.RecipientEmail, invitation.Role,
        EffectiveStatus(invitation, now), invitation.CreatedBy, invitation.CreatedAt, invitation.ExpiresAt,
        invitation.AcceptedBy, invitation.AcceptedAt, invitation.RejectedBy, invitation.RejectedAt,
        invitation.RevokedBy, invitation.RevokedAt, invitation.SupersedesInvitationId);
}
