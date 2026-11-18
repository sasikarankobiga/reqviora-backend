
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Reqviora.Application.Features.DocumentIntelligence;

namespace Reqviora.Application.Features.AIOrchestration;

public sealed record CandidateEvidence(
    string SourceId,
    string Locator,
    string Quote
);

public sealed record RequirementCandidate(
    string Id,
    string Title,
    string Description,
    string LanguageCode,
    decimal Confidence,
    IReadOnlyList<CandidateEvidence> Evidence
);

public sealed record RequirementExtractionPayload(
    string ContractVersion,
    IReadOnlyList<RequirementCandidate> Candidates
);

public static class RequirementExtractionValidator
{
    private static readonly Regex RequirementIdPattern = new(
        @"^REQ-[A-Z0-9]+(-[A-Z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public static RequirementExtractionPayload Parse(
        AiProviderRequest request,
        AiProviderResponse response,
        IReadOnlyCollection<SourceEvidenceRef> allowedEvidence)
    {
        AiContractValidator.Validate(request);
        AiContractValidator.Validate(response);

        if (request.Operation != AiOperation.RequirementExtraction)
            throw new ArgumentException(
                "Expected requirement extraction operation.");

        if (response.RequestId != request.RequestId)
            throw new ArgumentException(
                "AI request/response IDs do not match.");

        if (response.Status != AiRunStatus.Completed)
            throw new ArgumentException(
                "AI extraction was not completed.");

        if (allowedEvidence is null)
            throw new ArgumentException(
                "Allowed source evidence is required.");

        if (response.StructuredJson!.Length > 2_000_000)
            throw new ArgumentException("AI response is too large.");

        using var document = JsonDocument.Parse(
            response.StructuredJson,
            new JsonDocumentOptions { MaxDepth = 32 });

        RejectDuplicateJsonKeys(document.RootElement);

        RequirementExtractionPayload payload;

        try
        {
            payload = JsonSerializer.Deserialize
                <RequirementExtractionPayload>(
                    response.StructuredJson,
                    JsonOptions)
                ?? throw new ArgumentException(
                    "Empty extraction payload.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                "Invalid extraction output structure.", ex);
        }

        if (payload.ContractVersion != "1.0" ||
            payload.Candidates is null ||
            payload.Candidates.Count > 100)
        {
            throw new ArgumentException(
                "Unsupported contract version or candidate count.");
        }

        var candidateIds = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var candidate in payload.Candidates)
        {
            if (candidate is null ||
                string.IsNullOrWhiteSpace(candidate.Id) ||
                candidate.Id.Length > 100 ||
                !RequirementIdPattern.IsMatch(candidate.Id))
            {
                throw new ArgumentException(
                    "Invalid candidate requirement ID.");
            }

            if (!candidateIds.Add(candidate.Id))
                throw new ArgumentException(
                    "Duplicate requirement candidate ID.");

            if (string.IsNullOrWhiteSpace(candidate.Title) ||
                candidate.Title.Length > 200 ||
                candidate.Description is null ||
                candidate.Description.Length > 4000)
            {
                throw new ArgumentException(
                    "Invalid candidate title or description.");
            }

            if (candidate.LanguageCode is not (
                "en" or "en-LK" or "ta" or "ta-LK"))
            {
                throw new ArgumentException(
                    "Unsupported candidate language.");
            }

            if (candidate.Confidence < 0 ||
                candidate.Confidence > 1)
            {
                throw new ArgumentException(
                    "AI confidence must be between 0 and 1.");
            }

            if (candidate.Evidence is null ||
                candidate.Evidence.Count is < 1 or > 10)
            {
                throw new ArgumentException(
                    "Candidate needs 1 to 10 source evidence references.");
            }

            var seenEvidence = new HashSet<string>(
                StringComparer.Ordinal);

            foreach (var evidence in candidate.Evidence)
            {
                if (evidence is null ||
                    string.IsNullOrWhiteSpace(evidence.SourceId) ||
                    string.IsNullOrWhiteSpace(evidence.Locator) ||
                    string.IsNullOrWhiteSpace(evidence.Quote))
                {
                    throw new ArgumentException(
                        "Invalid source evidence.");
                }

                var evidenceKey =
                    evidence.SourceId + "\u001f" +
                    evidence.Locator + "\u001f" +
                    evidence.Quote;

                if (!seenEvidence.Add(evidenceKey))
                    throw new ArgumentException(
                        "Duplicate source evidence.");

                bool found = allowedEvidence.Any(source =>
                    source.SourceId == evidence.SourceId &&
                    source.Locator == evidence.Locator &&
                    source.Quote == evidence.Quote);

                if (!found)
                    throw new ArgumentException(
                        "AI cited an unknown source evidence reference.");
            }
        }

        return payload;
    }

    private static void RejectDuplicateJsonKeys(
        JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(
                StringComparer.Ordinal);

            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new ArgumentException(
                        "Duplicate JSON property in AI output.");

                RejectDuplicateJsonKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateJsonKeys(item);
        }
    }
}
