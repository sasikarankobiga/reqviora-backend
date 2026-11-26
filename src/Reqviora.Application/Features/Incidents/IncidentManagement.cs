namespace Reqviora.Application.Features.Incidents;

public sealed record IncidentDetails(Guid Id, string Reference, string Category, string Priority, string Status,
    Guid ReporterId, Guid? AssignedAdminId, string? CorrelationId, string? ResolutionNotes,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt,
    Guid ConcurrencyToken);

public sealed record IncidentHistoryEntry(Guid Id, string Action, string? PreviousValue, string? NewValue,
    string? Notes, Guid ChangedBy, DateTimeOffset ChangedAt);

public sealed record IncidentPage(IReadOnlyList<IncidentDetails> Items, int Page, int PageSize, int Total);

public sealed record IncidentSummary(int Open, int InProgress, int Resolved, int Closed, int Critical,
    int Unassigned, int Total);

public interface IIncidentManagement
{
    Task<IncidentPage> SearchAsync(string? search, string? category, string? priority, string? status,
        Guid? assignedAdminId, int page, int pageSize, CancellationToken ct);
    Task<IncidentDetails> GetAsync(Guid id, CancellationToken ct);
    Task<IncidentDetails> CreateAsync(string category, string priority, string? correlationId, Guid reporterId,
        CancellationToken ct);
    Task<IncidentDetails> ChangeStatusAsync(Guid id, string status, string? resolutionNotes,
        Guid expectedConcurrencyToken, Guid actorId, CancellationToken ct);
    Task<IncidentDetails> AssignAsync(Guid id, Guid? assignedAdminId, Guid expectedConcurrencyToken,
        Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<IncidentHistoryEntry>> HistoryAsync(Guid id, int page, int pageSize, CancellationToken ct);
    Task<IncidentSummary> SummaryAsync(CancellationToken ct);
}
