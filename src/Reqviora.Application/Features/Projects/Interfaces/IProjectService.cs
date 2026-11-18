using Reqviora.Application.Features.Projects.DTOs;

namespace Reqviora.Application.Features.Projects.Interfaces;

public interface IProjectService
{
    Task<IReadOnlyCollection<ProjectResponseDto>> GetAllAsync(Guid ownerId, CancellationToken cancellationToken = default);
    Task<ProjectResponseDto> CreateAsync(CreateProjectDto request, Guid ownerId, CancellationToken cancellationToken = default);
}
