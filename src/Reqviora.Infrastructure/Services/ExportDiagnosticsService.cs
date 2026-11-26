using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Exports;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ExportDiagnosticsService(ReqvioraDbContext db) : IExportDiagnosticsService
{
    public async Task<ExportJobPage> ListAsync(int page, int pageSize, string? status, Guid? projectId,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
        {
            var all = await db.ExportJobDiagnostics.AsNoTracking().ToArrayAsync(cancellationToken);
            var filtered = FilterInMemory(all, status, projectId, from, to)
                .OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id).ToArray();
            return new ExportJobPage(page, pageSize, filtered.Length,
                filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(ToMetadata).ToArray());
        }
        var query = Filter(status, projectId, from, to);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new ExportJobPage(page, pageSize, total, rows.Select(ToMetadata).ToArray());
    }

    public async Task<ExportJobMetadata?> FindAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var item = await db.ExportJobDiagnostics.AsNoTracking()
            .SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken);
        return item is null ? null : ToMetadata(item);
    }

    public async Task<ExportDiagnosticsSummary> SummaryAsync(DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
        {
            var all = await db.ExportJobDiagnostics.AsNoTracking().ToArrayAsync(cancellationToken);
            return CalculateSummary(FilterInMemory(all, null, null, from, to).ToArray());
        }
        var rows = await Filter(null, null, from, to).ToArrayAsync(cancellationToken);
        return CalculateSummary(rows);
    }

    private static ExportDiagnosticsSummary CalculateSummary(ExportJobDiagnostic[] rows)
    {
        var successful = rows.Count(item => item.Status == "Succeeded");
        var failed = rows.Count(item => item.Status == "Failed");
        var durations = rows.Where(item => item.DurationMilliseconds.HasValue)
            .Select(item => item.DurationMilliseconds!.Value).ToArray();
        return new ExportDiagnosticsSummary(rows.Length, successful, failed,
            rows.Length == 0 ? 0 : successful * 100d / rows.Length,
            durations.Length == 0 ? 0 : durations.Average(),
            rows.Count(item => item.IntegrityStatus == "Verified"),
            rows.Count(item => item.IntegrityStatus == "Failed"));
    }

    private static IEnumerable<ExportJobDiagnostic> FilterInMemory(IEnumerable<ExportJobDiagnostic> rows,
        string? status, Guid? projectId, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(item => item.Status == status);
        if (projectId.HasValue) rows = rows.Where(item => item.ProjectId == projectId);
        if (from.HasValue) rows = rows.Where(item => item.CreatedAt >= from.Value);
        if (to.HasValue) rows = rows.Where(item => item.CreatedAt <= to.Value);
        return rows;
    }

    private IQueryable<ExportJobDiagnostic> Filter(string? status, Guid? projectId,
        DateTimeOffset? from, DateTimeOffset? to)
    {
        var query = db.ExportJobDiagnostics.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status);
        if (projectId.HasValue) query = query.Where(item => item.ProjectId == projectId);
        if (from.HasValue) query = query.Where(item => item.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(item => item.CreatedAt <= to.Value);
        return query;
    }

    private static ExportJobMetadata ToMetadata(ExportJobDiagnostic item) => new(
        item.Id, item.ProjectId, item.ApprovedRevisionId, item.ExportRecordId, item.ExportType,
        item.Status, item.CreatedAt, item.StartedAt, item.CompletedAt, item.DurationMilliseconds,
        item.FailureCode, item.FailureCategory, item.IntegrityStatus);
}
