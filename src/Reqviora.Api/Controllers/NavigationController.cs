using Microsoft.AspNetCore.Mvc;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/navigation")]
public sealed class NavigationController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(Array.Empty<object>());
}
