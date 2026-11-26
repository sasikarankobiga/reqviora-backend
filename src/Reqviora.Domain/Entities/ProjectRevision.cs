using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class ValidationRun : BaseEntity
{
    public Guid DraftVersionId { get; init; }
    public bool Passed { get; init; }
    public DateTimeOffset ValidatedAt { get; init; }
    public List<ValidationFinding> Findings { get; init; } = [];
}

public sealed record ValidationFinding(Guid ValidationRunId, int Ordinal, string Code, string Path, string Message);

public sealed class ApprovedRevision : BaseEntity
{
    public Guid ProjectId { get; init; }
    public Guid DraftVersionId { get; init; }
    public Guid ValidationRunId { get; init; }
    public Guid ApprovedBy { get; init; }
    public DateTimeOffset ApprovedAt { get; init; }
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class ExportRecord : BaseEntity
{
    public Guid ApprovedRevisionId { get; init; }
    public string GeneratorVersion { get; init; } = string.Empty;
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string ManifestJson { get; init; } = "{}";
    public string ArchiveSha256 { get; init; } = string.Empty;
    public byte[] Archive { get; init; } = [];
    public string? StoragePath { get; set; }
}

public sealed class ExportJobDiagnostic : BaseEntity
{
    public Guid ProjectId { get; init; }
    public Guid ApprovedRevisionId { get; init; }
    public Guid? ExportRecordId { get; set; }
    public string ExportType { get; init; } = "SecondBrain";
    public string Status { get; set; } = "Processing";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? DurationMilliseconds { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureCategory { get; set; }
    public string IntegrityStatus { get; set; } = "Unknown";
}
