using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public sealed class VoiceTranscript : AuditableEntity
{
    public VoiceTranscript() { }

    public VoiceTranscript(Guid id) => Id = id;

    public Guid ProjectId { get; set; }
    public Guid AudioFileId { get; set; }
    public string AudioStoragePath { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int DurationSeconds { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string EditableText { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public string Status { get; set; } = "draft";
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

public sealed class VoiceTranscriptSegment : BaseEntity
{
    public Guid VoiceTranscriptId { get; set; }
    public int Sequence { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Text { get; set; } = string.Empty;
}
