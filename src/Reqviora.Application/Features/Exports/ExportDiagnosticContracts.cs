namespace Reqviora.Application.Features.Exports;

public sealed record ExportJobMetadata(
    Guid Id, Guid ProjectId, Guid ApprovedRevisionId, Guid? ExportRecordId,
    string ExportType, string Status, DateTimeOffset CreatedAt, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, long? DurationMilliseconds, string? FailureCode,
    string? FailureCategory, string IntegrityStatus);

public sealed record ExportJobPage(int Page, int PageSize, int Total, IReadOnlyList<ExportJobMetadata> Items);

public sealed record ExportDiagnosticsSummary(
    int Total, int Successful, int Failed, double SuccessRate,
    double AverageDurationMilliseconds, int IntegrityVerified, int IntegrityFailed);

public interface IExportDiagnosticsService
{
    Task<ExportJobPage> ListAsync(int page, int pageSize, string? status, Guid? projectId,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default);
    Task<ExportJobMetadata?> FindAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<ExportDiagnosticsSummary> SummaryAsync(DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default);
}
