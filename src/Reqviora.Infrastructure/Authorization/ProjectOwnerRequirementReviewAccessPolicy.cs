using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Authorization;

internal sealed class ProjectOwnerRequirementReviewAccessPolicy(ReqvioraDbContext dbContext)
    : IRequirementReviewAccessPolicy
{
    public Task<bool> CanReviewAsync(
        Guid projectId,
        Guid authenticatedUserId,
        CancellationToken cancellationToken = default) =>
        dbContext.Projects.AnyAsync(
            project => project.Id == projectId && project.OwnerId == authenticatedUserId,
            cancellationToken);
}
