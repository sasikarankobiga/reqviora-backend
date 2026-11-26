using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Exports;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = "Administrator"), Route("api/admin/exports")]
public sealed class AdminExportDiagnosticsController(IExportDiagnosticsService diagnostics) : ControllerBase
{
    [HttpGet("jobs")]
    public async Task<ActionResult<ExportJobPage>> List(
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 50,
        [FromQuery, RegularExpression("^(Succeeded|Failed)$")] string? status = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default) =>
        Ok(await diagnostics.ListAsync(page, pageSize, status, projectId, from, to, cancellationToken));

    [HttpGet("jobs/{exportJobId:guid}")]
    public async Task<ActionResult<ExportJobMetadata>> Get(Guid exportJobId, CancellationToken cancellationToken)
    {
        var job = await diagnostics.FindAsync(exportJobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<ExportDiagnosticsSummary>> Summary(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default) =>
        Ok(await diagnostics.SummaryAsync(from, to, cancellationToken));
}
