using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Projects.DTOs;

public sealed record UpdateProjectDto(
    [Required, StringLength(200, MinimumLength = 2)] string Name,
    [StringLength(4000)] string Description);
