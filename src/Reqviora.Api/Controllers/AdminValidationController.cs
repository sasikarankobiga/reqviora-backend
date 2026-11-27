using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Configuration;
using Reqviora.Application.Features.Validation;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = AuthorizationPolicies.Administrator), Route("api/admin/validation")]
public sealed class AdminValidationController(IValidationHealth health) : ControllerBase
{
    [HttpGet("summary")]
    public Task<ValidationStatistics> Summary(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        health.SummaryAsync(from, to, ct);

    [HttpGet("statistics")]
    [HttpGet("trends")]
    public Task<IReadOnlyList<ValidationDay>> Statistics(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        health.DailyAsync(from, to, ct);

    [HttpGet("error-categories")]
    public async Task<ValidationErrorCounts> ErrorCategories(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        (await health.SummaryAsync(from, to, ct)).Errors;
}
