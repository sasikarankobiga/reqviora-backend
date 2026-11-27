namespace Reqviora.Application.Features.AdminHealth;

public sealed record AdminHealthComponent(string Name, string Status, long DurationMilliseconds);

public sealed record AdminSystemHealthSnapshot(
    string Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<AdminHealthComponent> Components);

public interface IAdminSystemHealth
{
    Task<AdminSystemHealthSnapshot> CheckAsync(CancellationToken cancellationToken);
}
