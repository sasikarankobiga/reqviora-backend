using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class ProjectDocument : AuditableEntity
{
    public Guid ProjectId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string ParsingStatus { get; set; } = "pending";
    public string? ParseErrorCode { get; set; }
    public string ParsingWarningsJson { get; set; } = "[]";
    public DateTimeOffset? ParsedAt { get; set; }
    public long? ParseLeaseExpiresAtUnixTimeMilliseconds { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

public sealed class ProjectDocumentSegment : BaseEntity
{
    public Guid ProjectDocumentId { get; set; }
    public int Sequence { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Locator { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
}
