
using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class NavigationEdge : BaseEntity
{
    public Guid ProjectId { get; set; }

    public Guid FromScreenId { get; set; }

    public Guid ToScreenId { get; set; }

    public string Trigger { get; set; } = string.Empty;

    public string? Condition { get; set; }
}
