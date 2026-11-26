using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reqviora.Application.Features.Validation;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ValidationHealth(ReqvioraDbContext db, IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<ValidationHealth> logger) : IValidationHealth
{
    public async Task RecordAsync(ValidationWorkflow workflow, TimeSpan duration,
        IReadOnlyList<ValidationSignal> findings, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var schema = 0;
        var broken = 0;
        var unsupported = 0;
        var navigation = 0;
        var other = 0;
        foreach (var finding in findings)
        {
            var schemaIssue = finding.Code is "BLUEPRINT_SCHEMA" or "NULL_BLUEPRINT" or "SCHEMA_VERSION";
            var brokenIssue = finding.Code is "BROKEN_REFERENCE" or "broken_reference" or "builder_broken_reference";
            var unsupportedIssue = finding.Code is "UNSUPPORTED_COMPONENT" or "unsupported_component" or "builder_unsupported_component";
            var navigationIssue = finding.Code is not ("missing_step" or "builder_missing_step") &&
                (finding.Path.StartsWith("navigation", StringComparison.Ordinal) ||
                finding.Path.StartsWith("$.navigation", StringComparison.Ordinal) ||
                finding.Path.StartsWith("guidedBuilder.navigation", StringComparison.Ordinal) ||
                finding.Code is "INVALID_ACTION_ROUTE" or "NAVIGATION_ACTION_MISMATCH" or "NAVIGATION_SOURCE_MISMATCH");
            if (schemaIssue) schema++;
            if (brokenIssue) broken++;
            if (unsupportedIssue) unsupported++;
            if (navigationIssue) navigation++;
            if (!(schemaIssue || brokenIssue || unsupportedIssue || navigationIssue)) other++;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var sink = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            sink.ValidationTelemetry.Add(new ValidationTelemetry
            {
                CompletedAtMilliseconds = now.ToUnixTimeMilliseconds(),
                UtcDay = DateOnly.FromDateTime(now.UtcDateTime).DayNumber,
                Workflow = workflow.ToString(),
                Passed = findings.Count == 0,
                DurationMilliseconds = Math.Max(0, (long)duration.TotalMilliseconds),
                SchemaFailures = schema,
                BrokenReferences = broken,
                UnsupportedComponents = unsupported,
                NavigationFailures = navigation,
                OtherFailures = other
            });
            await sink.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Could not record {Workflow} validation telemetry ({FailureType}).",
                workflow, exception.GetType().Name);
        }
    }

    public async Task<ValidationStatistics> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var days = await DailyAsync(from, to, ct);
        var attempts = days.Sum(x => x.Statistics.Attempts);
        var succeeded = days.Sum(x => x.Statistics.Succeeded);
        return Result(attempts, succeeded,
            days.Sum(x => x.Statistics.AverageDurationMilliseconds * x.Statistics.Attempts),
            new(days.Sum(x => x.Statistics.Errors.Schema), days.Sum(x => x.Statistics.Errors.BrokenReferences),
                days.Sum(x => x.Statistics.Errors.UnsupportedComponents), days.Sum(x => x.Statistics.Errors.Navigation),
                days.Sum(x => x.Statistics.Errors.Other)));
    }

    public async Task<IReadOnlyList<ValidationDay>> DailyAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var last = to ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var first = from ?? DateOnly.FromDayNumber(Math.Max(0, last.DayNumber - 29));
        if (last < first || last.DayNumber - first.DayNumber >= 366)
            throw new ArgumentException("Provide a chronological date range of at most 366 days.");
        var rows = await db.ValidationTelemetry.AsNoTracking()
            .Where(x => x.UtcDay >= first.DayNumber && x.UtcDay <= last.DayNumber)
            .GroupBy(x => x.UtcDay)
            .Select(g => new
            {
                Day = g.Key, Attempts = g.Count(), Succeeded = g.Count(x => x.Passed),
                Duration = g.Sum(x => x.DurationMilliseconds), Schema = g.Sum(x => x.SchemaFailures),
                Broken = g.Sum(x => x.BrokenReferences), Unsupported = g.Sum(x => x.UnsupportedComponents),
                Navigation = g.Sum(x => x.NavigationFailures), Other = g.Sum(x => x.OtherFailures)
            }).OrderBy(x => x.Day).ToArrayAsync(ct);
        return rows.Select(x => new ValidationDay(DateOnly.FromDayNumber(x.Day),
            Result(x.Attempts, x.Succeeded, x.Duration, new(x.Schema, x.Broken, x.Unsupported, x.Navigation, x.Other))))
            .ToArray();
    }

    private static ValidationStatistics Result(int attempts, int succeeded, double duration, ValidationErrorCounts errors) =>
        new(attempts, succeeded, attempts - succeeded, attempts == 0 ? 0 : 100d * succeeded / attempts,
            attempts == 0 ? 0 : 100d * (attempts - succeeded) / attempts,
            attempts == 0 ? 0 : duration / attempts, errors);
}
