using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly List<Project> _projects = [];

    public Task<IReadOnlyCollection<Project>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<Project>>(_projects.ToArray());

    public Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        _projects.Add(project);
        return Task.CompletedTask;
    }
}
