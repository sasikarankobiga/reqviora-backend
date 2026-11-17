using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class User : AggregateRoot
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Guid? RoleId { get; set; }
}
