using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
