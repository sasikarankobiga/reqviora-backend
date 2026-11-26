
using Reqviora.Application.Features.Documents;

namespace Reqviora.Application.Features.VoiceProcessing;

public sealed record TranscriptSegment(
    long StartMs,
    long EndMs,
    string Text
);

public sealed record TranscriptDraft(
    Guid TranscriptId,
    Guid AudioFileId,
    string LanguageCode,
    string OriginalText,
    string EditableText,
    int Revision,
    IReadOnlyList<TranscriptSegment> Segments
);

public sealed record EditTranscriptRequest(
    Guid TranscriptId,
    int ExpectedRevision,
    string EditedText
);

public sealed record ReviewTranscriptRequest(
    Guid TranscriptId,
    int ExpectedRevision,
    bool AcknowledgedByUser
);

public interface IVoiceTranscriber
{
    Task<TranscriptDraft> TranscribeAsync(
        VoiceInputRequest request,
        Stream audioContent,
        CancellationToken cancellationToken = default
    );
}

public static class VoiceTranscriptionContractValidator
{
    public static void Validate(TranscriptDraft draft)
    {
        if (draft.TranscriptId == Guid.Empty ||
            draft.AudioFileId == Guid.Empty)
        {
            throw new ArgumentException(
                "Transcript ID and audio file ID are required.");
        }

        if (!InputStudioContractValidator.IsSupportedLanguage(
            draft.LanguageCode))
        {
            throw new ArgumentException(
                "Unsupported transcript language.");
        }

        if (draft.Revision != 1)
            throw new ArgumentException(
                "Transcript revision must start at 1.");

        if (string.IsNullOrWhiteSpace(draft.OriginalText) ||
            draft.OriginalText.Length > 100_000 ||
            string.IsNullOrWhiteSpace(draft.EditableText) ||
            draft.EditableText.Length > 100_000)
        {
            throw new ArgumentException(
                "Transcript text is required.");
        }

        if (draft.Segments is null || draft.Segments.Count is < 1 or > 5000)
            throw new ArgumentException(
                "Transcript segments are required.");

        long previousEnd = 0;
        foreach (var segment in draft.Segments)
        {
            if (segment is null ||
                segment.StartMs < 0 ||
                segment.StartMs < previousEnd ||
                segment.EndMs <= segment.StartMs ||
                string.IsNullOrWhiteSpace(segment.Text) ||
                segment.Text.Length > 20_000)
            {
                throw new ArgumentException(
                    "Invalid transcript segment.");
            }
            previousEnd = segment.EndMs;
        }
    }

    public static void Validate(EditTranscriptRequest request)
    {
        if (request.TranscriptId == Guid.Empty ||
            request.ExpectedRevision < 1 ||
            string.IsNullOrWhiteSpace(request.EditedText) ||
            request.EditedText.Length > 100_000)
        {
            throw new ArgumentException(
                "Invalid transcript edit request.");
        }
    }

    public static void Validate(ReviewTranscriptRequest request)
    {
        if (request.TranscriptId == Guid.Empty ||
            request.ExpectedRevision < 1 ||
            !request.AcknowledgedByUser)
        {
            throw new ArgumentException(
                "Explicit transcript review acknowledgement is required.");
        }
    }
}
