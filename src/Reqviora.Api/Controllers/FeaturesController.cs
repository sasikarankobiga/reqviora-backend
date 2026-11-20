using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/features")]
public sealed class FeaturesController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(Array.Empty<object>());
}
