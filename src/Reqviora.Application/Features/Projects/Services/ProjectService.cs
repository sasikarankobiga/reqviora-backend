using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Projects.Services;

public sealed class ProjectService(IProjectRepository projects) : IProjectService
{
    public async Task<IReadOnlyCollection<ProjectResponseDto>> GetAllAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        (await projects.GetAllAsync(ownerId, cancellationToken)).Select(ToResponse).ToArray();

    public async Task<ProjectResponseDto> CreateAsync(CreateProjectDto request, Guid ownerId, CancellationToken cancellationToken = default)
    {
        var project = new Project { Name = request.Name, Description = request.Description, OwnerId = ownerId };
        await projects.AddAsync(project, cancellationToken);
        return ToResponse(project);
    }

    private static ProjectResponseDto ToResponse(Project project) => new(project.Id, project.Name, project.Description, project.OwnerId);
}
