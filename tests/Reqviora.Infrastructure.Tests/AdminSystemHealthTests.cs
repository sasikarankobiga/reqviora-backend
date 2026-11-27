using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reqviora.Application.Features.AdminHealth;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class AdminSystemHealthTests
{
    [Fact]
    public async Task Snapshot_reports_component_states_without_descriptions_exceptions_or_configuration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddCheck("api", () => HealthCheckResult.Healthy("private healthy detail"))
            .AddCheck("smtp", () => HealthCheckResult.Unhealthy("smtp.internal:587", new("secret")));
        await using var provider = services.BuildServiceProvider();
        IAdminSystemHealth service = new AdminSystemHealth(
            provider.GetRequiredService<HealthCheckService>(), TimeProvider.System);

        var snapshot = await service.CheckAsync(CancellationToken.None);

        Assert.Equal("unhealthy", snapshot.Status);
        Assert.Collection(snapshot.Components,
            item => Assert.Equal(new("api", "healthy"), (item.Name, item.Status)),
            item => Assert.Equal(new("smtp", "unhealthy"), (item.Name, item.Status)));
        var serialized = System.Text.Json.JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("private healthy detail", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("smtp.internal", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", serialized, StringComparison.Ordinal);
    }
}
