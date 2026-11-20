using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/screens")]
public sealed class ScreensController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(Array.Empty<object>());
}
