using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Incidents;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class IncidentManagementService(ReqvioraDbContext db, TimeProvider clock) : IIncidentManagement
{
    private static readonly string[] Priorities = ["Low", "Medium", "High", "Critical"];
    private static readonly string[] Statuses = ["Open", "InProgress", "Resolved", "Closed"];

    public async Task<IncidentPage> SearchAsync(string? search, string? category, string? priority, string? status,
        Guid? assignedAdminId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.SupportIncidents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Reference.Contains(term) || x.Category.Contains(term) ||
                (x.CorrelationId != null && x.CorrelationId.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category.Trim());
        if (!string.IsNullOrWhiteSpace(priority))
        {
            var normalizedPriority = Normalize(priority, Priorities, "priority");
            query = query.Where(x => x.Priority == normalizedPriority);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = Normalize(status, Statuses, "status");
            query = query.Where(x => x.Status == normalizedStatus);
        }
        if (assignedAdminId.HasValue) query = query.Where(x => x.AssignedAdminId == assignedAdminId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.UpdatedAtUnixTimeMilliseconds).ThenBy(x => x.Reference)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(Map).ToArray(), page, pageSize, total);
    }

    public async Task<IncidentDetails> GetAsync(Guid id, CancellationToken ct)
    {
        var incident = await db.SupportIncidents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("The incident was not found.");
        return Map(incident);
    }

    public async Task<IncidentDetails> CreateAsync(string category, string priority, string? correlationId,
        Guid reporterId, CancellationToken ct)
    {
        category = RequiredText(category, 80, "category");
        priority = Normalize(priority, Priorities, "priority");
        correlationId = OptionalText(correlationId, 100, "correlationId");
        RejectSecrets(category, correlationId);
        var now = clock.GetUtcNow();
        var incident = new SupportIncident
        {
            Reference = $"INC-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..30].ToUpperInvariant(),
            Category = category, Priority = priority, ReporterId = reporterId, CorrelationId = correlationId,
            CreatedAt = now, UpdatedAt = now, UpdatedAtUnixTimeMilliseconds = now.ToUnixTimeMilliseconds()
        };
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.SupportIncidents.Add(incident);
        AddHistory(incident.Id, "Created", null, "Open", null, reporterId, now);
        AddAudit(incident.Id, reporterId, "POST", "created", now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(incident);
    }

    public async Task<IncidentDetails> ChangeStatusAsync(Guid id, string status, string? resolutionNotes,
        Guid expectedConcurrencyToken, Guid actorId, CancellationToken ct)
    {
        status = Normalize(status, Statuses, "status");
        resolutionNotes = OptionalText(resolutionNotes, 1000, "resolutionNotes");
        RejectSecrets(resolutionNotes);
        var incident = await LoadForUpdate(id, expectedConcurrencyToken, ct);
        if (incident.Status == status) return Map(incident);
        if (!CanTransition(incident.Status, status))
            throw new InvalidOperationException($"Incident status cannot transition from {incident.Status} to {status}.");
        if (status == "Resolved" && string.IsNullOrWhiteSpace(resolutionNotes))
            throw new ArgumentException("Resolution notes are required when resolving an incident.");

        var now = clock.GetUtcNow();
        var previous = incident.Status;
        incident.Status = status;
        incident.UpdatedAt = now;
        incident.UpdatedAtUnixTimeMilliseconds = now.ToUnixTimeMilliseconds();
        incident.ConcurrencyToken = Guid.NewGuid();
        if (status == "Resolved") { incident.ResolvedAt = now; incident.ClosedAt = null; incident.ResolutionNotes = resolutionNotes; }
        if (status == "Closed") incident.ClosedAt = now;
        if (status is "Open" or "InProgress") { incident.ResolvedAt = null; incident.ClosedAt = null; incident.ResolutionNotes = null; }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        AddHistory(id, "StatusChanged", previous, status, resolutionNotes, actorId, now);
        AddAudit(id, actorId, "PUT", $"status/{status}", now);
        await SaveConcurrencyAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(incident);
    }

    public async Task<IncidentDetails> AssignAsync(Guid id, Guid? assignedAdminId,
        Guid expectedConcurrencyToken, Guid actorId, CancellationToken ct)
    {
        if (assignedAdminId.HasValue && !await IsAdministratorAsync(assignedAdminId.Value, ct))
            throw new ArgumentException("The assignee must be an active administrator.", nameof(assignedAdminId));
        var incident = await LoadForUpdate(id, expectedConcurrencyToken, ct);
        if (incident.AssignedAdminId == assignedAdminId) return Map(incident);
        var now = clock.GetUtcNow();
        var previous = incident.AssignedAdminId;
        incident.AssignedAdminId = assignedAdminId;
        incident.UpdatedAt = now;
        incident.UpdatedAtUnixTimeMilliseconds = now.ToUnixTimeMilliseconds();
        incident.ConcurrencyToken = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        AddHistory(id, "AssignmentChanged", previous?.ToString(), assignedAdminId?.ToString(), null, actorId, now);
        AddAudit(id, actorId, "PUT", assignedAdminId.HasValue ? "assigned" : "unassigned", now);
        await SaveConcurrencyAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(incident);
    }

    public async Task<IReadOnlyList<IncidentHistoryEntry>> HistoryAsync(Guid id, int page, int pageSize,
        CancellationToken ct)
    {
        if (!await db.SupportIncidents.AsNoTracking().AnyAsync(x => x.Id == id, ct))
            throw new KeyNotFoundException("The incident was not found.");
        return await db.SupportIncidentHistory.AsNoTracking().Where(x => x.IncidentId == id)
            .OrderByDescending(x => x.ChangedAtUnixTimeMilliseconds).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new IncidentHistoryEntry(x.Id, x.Action, x.PreviousValue, x.NewValue, x.Notes,
                x.ChangedBy, x.ChangedAt)).ToArrayAsync(ct);
    }

    public async Task<IncidentSummary> SummaryAsync(CancellationToken ct)
    {
        var rows = await db.SupportIncidents.AsNoTracking().GroupBy(x => 1).Select(g => new
        {
            Open = g.Count(x => x.Status == "Open"), InProgress = g.Count(x => x.Status == "InProgress"),
            Resolved = g.Count(x => x.Status == "Resolved"), Closed = g.Count(x => x.Status == "Closed"),
            Critical = g.Count(x => x.Priority == "Critical"), Unassigned = g.Count(x => x.AssignedAdminId == null),
            Total = g.Count()
        }).SingleOrDefaultAsync(ct);
        return rows is null ? new(0, 0, 0, 0, 0, 0, 0) :
            new(rows.Open, rows.InProgress, rows.Resolved, rows.Closed, rows.Critical, rows.Unassigned, rows.Total);
    }

    private async Task<SupportIncident> LoadForUpdate(Guid id, Guid expected, CancellationToken ct)
    {
        var incident = await db.SupportIncidents.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("The incident was not found.");
        if (incident.ConcurrencyToken != expected)
            throw new DbUpdateConcurrencyException("The incident has changed. Refresh it and try again.");
        return incident;
    }

    private async Task SaveConcurrencyAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw; }
        catch (DbUpdateException exception) { throw new DbUpdateConcurrencyException("The incident has changed.", exception); }
    }

    private Task<bool> IsAdministratorAsync(Guid userId, CancellationToken ct) => db.Users.AsNoTracking()
        .Where(x => x.Id == userId && !x.IsDisabled && x.RoleId != null)
        .AnyAsync(x => db.Roles.Any(role => role.Id == x.RoleId && (role.Name == "Admin" || role.Name == "Administrator")), ct);

    private void AddHistory(Guid incidentId, string action, string? previous, string? next, string? notes,
        Guid actor, DateTimeOffset now) => db.SupportIncidentHistory.Add(new SupportIncidentHistory
        { IncidentId = incidentId, Action = action, PreviousValue = previous, NewValue = next, Notes = notes,
            ChangedBy = actor, ChangedAt = now, ChangedAtUnixTimeMilliseconds = now.ToUnixTimeMilliseconds() });

    private void AddAudit(Guid incidentId, Guid actor, string method, string action, DateTimeOffset now) =>
        db.AuditEvents.Add(new AuditEvent { UserId = actor, Method = method, StatusCode = 200, OccurredAt = now,
            IpAddress = "security-service", Path = $"/security/incidents/{incidentId}/{action}" });

    private static bool CanTransition(string current, string next) => (current, next) switch
    {
        ("Open", "InProgress" or "Resolved") => true,
        ("InProgress", "Open" or "Resolved") => true,
        ("Resolved", "InProgress" or "Closed") => true,
        _ => false
    };

    private static string Normalize(string value, string[] allowed, string name) =>
        allowed.FirstOrDefault(x => x.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unsupported {name}.", name);

    private static string RequiredText(string? value, int max, string name) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > max
            ? throw new ArgumentException($"{name} is required and must not exceed {max} characters.", name)
            : value.Trim();

    private static string? OptionalText(string? value, int max, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max
            ? throw new ArgumentException($"{name} must not exceed {max} characters.", name) : value.Trim();

    private static void RejectSecrets(params string?[] values)
    {
        string[] markers = ["password=", "secret=", "api_key", "apikey", "bearer ", "service_role", "private key"];
        if (values.Any(value => value is not null && markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Incident metadata must not contain credentials or secrets.");
    }

    private static IncidentDetails Map(SupportIncident x) => new(x.Id, x.Reference, x.Category, x.Priority,
        x.Status, x.ReporterId, x.AssignedAdminId, x.CorrelationId, x.ResolutionNotes, x.CreatedAt, x.UpdatedAt,
        x.ResolvedAt, x.ClosedAt, x.ConcurrencyToken);
}
