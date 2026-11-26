using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

/// <summary>Read-only readiness probe for the independently deployed review schema.</summary>
public sealed class ReviewSchemaHealthCheck(ReqvioraDbContext db) : IHealthCheck
{
    private static readonly string[] Probes =
    [
        "SELECT project_id, requirement_id, title, description, status, revision, evidence_json, confirmed_by, confirmed_at FROM reqviora_review.requirements WHERE 1 = 0",
        "SELECT event_id, project_id, requirement_id, decision, actor_id, previous_revision, new_revision, occurred_at FROM reqviora_review.events WHERE 1 = 0",
        "SELECT project_id, requirement_id, question_id, question, answer, revision, answered_by, answered_at FROM reqviora_review.clarifications WHERE 1 = 0"
    ];

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        try
        {
            if (!wasOpen)
                await db.Database.OpenConnectionAsync(cancellationToken);
            foreach (var probe in Probes)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = probe;
                command.CommandTimeout = 5;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            }
            return HealthCheckResult.Healthy();
        }
        catch (System.Data.Common.DbException)
        {
            return HealthCheckResult.Unhealthy("Review schema is unavailable or incomplete.");
        }
        finally
        {
            if (!wasOpen && connection.State == ConnectionState.Open)
                await db.Database.CloseConnectionAsync();
        }
    }
}
