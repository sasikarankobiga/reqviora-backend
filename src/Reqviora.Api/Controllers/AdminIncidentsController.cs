using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Configuration;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Features.Incidents;

namespace Reqviora.Api.Controllers;

[ApiController, Authorize(Policy = AuthorizationPolicies.Administrator), Route("api/admin/incidents")]
public sealed class AdminIncidentsController(IIncidentManagement incidents) : ControllerBase
{
    public sealed record CreateIncidentRequest([Required, StringLength(80)] string Category,
        [Required] string Priority, [StringLength(100)] string? CorrelationId);
    public sealed record ChangeStatusRequest([Required] string Status, [StringLength(1000)] string? ResolutionNotes,
        [Required] Guid? ExpectedConcurrencyToken);
    public sealed record AssignIncidentRequest(Guid? AssignedAdminId, [Required] Guid? ExpectedConcurrencyToken);

    [HttpGet]
    public Task<IncidentPage> Search([FromQuery] string? search, [FromQuery] string? category,
        [FromQuery] string? priority, [FromQuery] string? status, [FromQuery] Guid? assignedAdminId,
        [FromQuery, Range(1, 1_000_000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50,
        CancellationToken ct = default) => incidents.SearchAsync(search, category, priority, status,
            assignedAdminId, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<IncidentDetails> Get(Guid id, CancellationToken ct) => incidents.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<IncidentDetails>> Create(CreateIncidentRequest request, CancellationToken ct)
    {
        var result = await incidents.CreateAsync(request.Category, request.Priority, request.CorrelationId,
            ProjectApiAccess.RequireUserId(User), ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}/status")]
    public Task<IncidentDetails> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        incidents.ChangeStatusAsync(id, request.Status, request.ResolutionNotes,
            request.ExpectedConcurrencyToken!.Value, ProjectApiAccess.RequireUserId(User), ct);

    [HttpPut("{id:guid}/assignment")]
    public Task<IncidentDetails> Assign(Guid id, AssignIncidentRequest request, CancellationToken ct) =>
        incidents.AssignAsync(id, request.AssignedAdminId, request.ExpectedConcurrencyToken!.Value,
            ProjectApiAccess.RequireUserId(User), ct);

    [HttpGet("{id:guid}/history")]
    public Task<IReadOnlyList<IncidentHistoryEntry>> History(Guid id,
        [FromQuery, Range(1, 1_000_000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 50,
        CancellationToken ct = default) => incidents.HistoryAsync(id, page, pageSize, ct);

    [HttpGet("summary")]
    public Task<IncidentSummary> Summary(CancellationToken ct) => incidents.SummaryAsync(ct);
}
