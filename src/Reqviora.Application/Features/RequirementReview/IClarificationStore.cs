namespace Reqviora.Application.Features.RequirementReview;

public interface IClarificationStore
{
    Task InsertAsync(Guid projectId, ClarificationQuestion question, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClarificationQuestion>> ListAsync(Guid projectId, string requirementId,
        int offset, int limit, CancellationToken cancellationToken = default);
    Task<ClarificationQuestion?> FindAsync(Guid projectId, string requirementId, Guid questionId,
        CancellationToken cancellationToken = default);
    Task<bool> TryAnswerAsync(Guid projectId, ClarificationQuestion answered, int expectedRevision,
        Guid actorId, CancellationToken cancellationToken = default);
}
