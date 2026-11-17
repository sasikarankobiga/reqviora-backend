using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class Project : AggregateRoot
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
}
