
using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class ScreenAction : BaseEntity
{
    public Guid ScreenId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ActionType { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsRequired { get; set; } = false;
}
