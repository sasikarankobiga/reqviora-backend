
using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class RequirementFeature : BaseEntity
{
    public Guid RequirementId { get; set; }

    public Guid FeatureId { get; set; }
}
