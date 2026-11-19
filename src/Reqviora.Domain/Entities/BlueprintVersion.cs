
using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class BlueprintVersion : BaseEntity
{
    public Guid BlueprintId { get; set; }

    public int VersionNumber { get; set; }

    public string DefinitionJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? ChangeSummary { get; set; }
}
