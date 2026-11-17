using Microsoft.AspNetCore.Mvc;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/blueprints")]
public sealed class BlueprintsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(Array.Empty<object>());
}
