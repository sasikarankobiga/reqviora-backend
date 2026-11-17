using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Projects.Interfaces;

public interface IProjectRepository
{
    Task<IReadOnlyCollection<Project>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Project project, CancellationToken cancellationToken = default);
}
