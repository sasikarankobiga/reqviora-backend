using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Projects.DTOs;

public sealed record CreateProjectDto(
    [property: Required, StringLength(200, MinimumLength = 2)] string Name,
    [property: StringLength(4000)] string Description);
