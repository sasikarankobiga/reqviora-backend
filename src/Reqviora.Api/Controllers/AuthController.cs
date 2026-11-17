using Microsoft.AspNetCore.Mvc;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { ready = false, message = "Configure identity and JWT before enabling authentication." });
}
