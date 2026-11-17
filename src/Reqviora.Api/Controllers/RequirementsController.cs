using Microsoft.AspNetCore.Mvc;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/requirements")]
public sealed class RequirementsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(Array.Empty<object>());
}
