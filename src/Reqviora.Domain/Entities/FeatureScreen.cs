
using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class FeatureScreen : BaseEntity
{
    public Guid FeatureId { get; set; }

    public Guid ScreenId { get; set; }
}
