using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class ReviewSchemaHealthCheckTests
{
    [Fact]
    public async Task Missing_clarification_table_is_unhealthy_then_complete_schema_is_healthy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ReqvioraDbContext(new DbContextOptionsBuilder<ReqvioraDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("ATTACH DATABASE ':memory:' AS reqviora_review");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE reqviora_review.requirements (project_id, requirement_id, title, description, status,
                revision, evidence_json, confirmed_by, confirmed_at);
            CREATE TABLE reqviora_review.events (event_id, project_id, requirement_id, decision, actor_id,
                previous_revision, new_revision, occurred_at);
            """);
        var check = new ReviewSchemaHealthCheck(db);
        var context = new HealthCheckContext();
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(context)).Status);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE reqviora_review.clarifications (project_id, requirement_id, question_id, question,
                answer, revision, answered_by, answered_at);
            """);
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(context)).Status);
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }
}
