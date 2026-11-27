using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reqviora.Application.Features.AdminHealth;

namespace Reqviora.Infrastructure.Services;

public sealed class AdminSystemHealth(HealthCheckService healthChecks, TimeProvider clock) : IAdminSystemHealth
{
    public async Task<AdminSystemHealthSnapshot> CheckAsync(CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(cancellationToken);
        var components = report.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new AdminHealthComponent(
                entry.Key,
                Status(entry.Value.Status),
                Math.Max(0, (long)entry.Value.Duration.TotalMilliseconds)))
            .ToArray();
        return new AdminSystemHealthSnapshot(Status(report.Status), clock.GetUtcNow(), components);
    }

    private static string Status(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "unhealthy"
    };
}
