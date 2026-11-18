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
            .ToArrayAsync(cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default) =>
        await dbContext.Projects.AddAsync(project, cancellationToken);
}
