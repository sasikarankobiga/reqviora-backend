namespace Reqviora.Application.Features.Projects.DTOs;

public sealed record ProjectResponseDto(Guid Id, string Name, string Description, Guid OwnerId);
