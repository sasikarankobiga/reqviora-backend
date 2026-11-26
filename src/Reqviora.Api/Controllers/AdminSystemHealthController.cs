using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Configuration;
using Reqviora.Application.Features.AdminHealth;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = AuthorizationPolicies.Administrator), Route("api/admin/system-health")]
public sealed class AdminSystemHealthController(IAdminSystemHealth health) : ControllerBase
{
    [HttpGet]
    public Task<AdminSystemHealthSnapshot> Get(CancellationToken cancellationToken) =>
        health.CheckAsync(cancellationToken);
}
