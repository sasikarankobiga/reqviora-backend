
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ProjectDraftService
{
    private readonly ReqvioraDbContext _dbContext;

    public ProjectDraftService(ReqvioraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Save a new draft or update an existing draft.
    public async Task<ProjectDraft> SaveAsync(
        Guid projectId,
        string draftJson,
        Guid? expectedToken,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("Project ID is required.");

        if (string.IsNullOrWhiteSpace(draftJson))
            throw new ArgumentException("Draft JSON is required.");

        var draft = await _dbContext.ProjectDrafts
            .SingleOrDefaultAsync(
                d => d.ProjectId == projectId,
                cancellationToken);

        if (draft is null)
        {
            if (expectedToken.HasValue)
                throw new InvalidOperationException(
                    "The draft no longer exists.");

            draft = new ProjectDraft
            {
                ProjectId = projectId,
                DraftJson = draftJson,
                Version = 1,
                ConcurrencyToken = Guid.NewGuid(),
                UpdatedAtUtc = DateTime.UtcNow
            };

            _dbContext.ProjectDrafts.Add(draft);
        }
        else
        {
            if (!expectedToken.HasValue ||
                draft.ConcurrencyToken != expectedToken.Value)
            {
                throw new DbUpdateConcurrencyException(
                    "The draft was changed by another user.");
            }

            draft.DraftJson = draftJson;
            draft.Version++;
            draft.ConcurrencyToken = Guid.NewGuid();
            draft.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return draft;
    }

    // Resume the most recently saved project draft.
    public async Task<ProjectDraft?> ResumeAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProjectDrafts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                d => d.ProjectId == projectId,
                cancellationToken);
    }
}
