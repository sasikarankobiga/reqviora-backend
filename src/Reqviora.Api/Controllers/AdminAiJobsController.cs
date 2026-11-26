using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Configuration;
using Reqviora.Application.Features.AIOrchestration;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = AuthorizationPolicies.Administrator), Route("api/admin/ai-jobs")]
public sealed class AdminAiJobsController(IAiQueueMonitoring monitoring) : ControllerBase
{
    [HttpGet]
    public Task<AdminAiJobPage> List([FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50, string? status = null, string? type = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        monitoring.ListAsync(page, pageSize, status, type, from, to, ct);

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<AdminAiJob>> Get(Guid jobId, CancellationToken ct)
    {
        var job = await monitoring.FindAsync(jobId, ct);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("summary")]
    [HttpGet("statistics")]
    public Task<AiQueueStatistics> Statistics(string? type, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        monitoring.StatisticsAsync(type, from, to, ct);

    [HttpGet("health")]
    public Task<AiQueueHealth> Health(CancellationToken ct) => monitoring.HealthAsync(ct);
}
