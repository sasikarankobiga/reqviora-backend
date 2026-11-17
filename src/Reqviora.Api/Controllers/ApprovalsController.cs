using Microsoft.AspNetCore.Mvc;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/approvals")]
public sealed class ApprovalsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(Array.Empty<object>());
}
