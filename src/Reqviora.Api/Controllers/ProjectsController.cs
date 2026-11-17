using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.Projects.Interfaces;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) => Ok(await projects.GetAllAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateProjectDto request, CancellationToken cancellationToken) =>
        Ok(await projects.CreateAsync(request, Guid.Empty, cancellationToken));
}
