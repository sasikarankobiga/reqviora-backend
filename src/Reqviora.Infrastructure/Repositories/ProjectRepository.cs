using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Repositories;

public sealed class ProjectRepository(ReqvioraDbContext dbContext) : IProjectRepository
{
    public async Task<IReadOnlyCollection<Project>> GetAllAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.OwnerId == ownerId)
            .OrderByDescending(project => project.LastModifiedAt ?? project.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public Task<Project?> GetByIdAsync(
        Guid projectId,
        Guid ownerId,
        bool trackChanges = false,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Projects.Where(project =>
            project.Id == projectId && project.OwnerId == ownerId);
        if (!trackChanges)
            query = query.AsNoTracking();

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default) =>
        await dbContext.Projects.AddAsync(project, cancellationToken);
}
