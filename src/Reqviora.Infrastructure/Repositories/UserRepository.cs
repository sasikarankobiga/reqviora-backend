using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly List<User> _users = [];

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.SingleOrDefault(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }
}
