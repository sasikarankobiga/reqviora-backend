using Reqviora.Application.Features.AIOrchestration;

namespace Reqviora.Application.Features.RequirementReview;

// The candidate store shares the review store's table. Inserts never update an existing review.
public interface IRequirementCandidateStore
{
    Task InsertAsync(Guid projectId, IReadOnlyList<ReviewedRequirement> candidates,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewedRequirement>> ListAsync(Guid projectId, int offset, int limit,
        CancellationToken cancellationToken = default);
}

public static class RequirementCandidateRegistration
{
    // AI IDs are local to an extraction. Allocate project-safe IDs before storing them.
    public static RequirementExtractionPayload AssignIds(RequirementExtractionPayload validated) =>
        validated with
        {
            Candidates = validated.Candidates.Select(candidate => candidate with
            {
                Id = $"REQ-{Guid.NewGuid():N}".ToUpperInvariant()
            }).ToArray()
        };
}
