using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.Projects.Interfaces;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return TryGetUserId(out var userId)
            ? Ok(await projects.GetAllAsync(userId, cancellationToken))
            : Unauthorized();
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateProjectDto request, CancellationToken cancellationToken)
    {
        return TryGetUserId(out var userId)
            ? Ok(await projects.CreateAsync(request, userId, cancellationToken))
            : Unauthorized();
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}
